using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace HeartopiaMod
{
    // Crash breadcrumb trail. Some process deaths leave no dump and no crashlog: a stack overflow
    // skips every user-mode handler, a hard TerminateProcess / IL2CPP-mono abort raises no SEH that
    // WER or Unity's UnityCrashHandler can catch, and all logs just truncate
    // at their last flush (see docs/CRASH_DUMP_ANALYSIS.md). This writes the last N operations to a
    // tiny ring file and flushes after each entry, so even an instant kill leaves a trail pointing at
    // the feature/operation that was running.
    //
    // Two entry points:
    //   Drop(area, detail) - coarse, one flush per call. Use at operation boundaries (scan start,
    //                        teleport, sell pass) that happen at human/feature cadence.
    //   Tick(area)         - hot paths (per-frame, enumerate loops). Always counts; only rewrites the
    //                        file at most every ThrottleMs, so it is cheap enough for tight loops yet
    //                        the running count still shows up in the trail.
    //   Phase(area)        - SUB-FRAME resolution, many per frame. Writes to its own file as a
    //                        fixed-layout ring (see the Phase section below): one 64-byte seek+write
    //                        per call instead of Drop's whole-ring rewrite, so a single OnUpdate can
    //                        carry ~20 markers without turning the trail into the bottleneck.
    internal static class Breadcrumbs
    {
        // Bump on every diagnostic deploy so the running build is verifiable from the log header.
        private const string BuildTag = "2026-08-11T18 onupdate-phase-crumbs";
        private const int RingSize = 160;
        private const long ThrottleMs = 250;

        private static readonly object _gate = new object();
        private static readonly string[] _ring = new string[RingSize];
        private static readonly Dictionary<string, long> _tickCounts = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> _tickNextWriteAt = new Dictionary<string, long>();
        private static int _next;
        private static long _seq;
        private static FileStream _stream;
        private static bool _disabled;

        // Hang watchdog. Drop/Tick are written by the main thread, so a hang (the main thread itself
        // stuck in a loop / blocking native call) leaves NO trail -- the thread that would log is the
        // one frozen. A background thread instead watches the heartbeat (refreshed on every main-thread
        // Drop/Tick); when it stalls past HangThresholdMs it writes a WATCHDOG.hang crumb naming the
        // last area reached, from its OWN thread, so it fires even while the main thread is frozen.
        private const long HangThresholdMs = 5000;
        private static long _lastHeartbeatTick;
        private static volatile string _lastArea = "(none)";
        private static Thread _watchdog;

        // ---- Phase ring ------------------------------------------------------------------------
        // Drop() rewrites and flushes the ENTIRE ring on every call (~5 KB). That is fine at the five
        // markers/frame OnUpdate used to carry, but the 2026-08-11 no-dump crash died somewhere in a
        // ~150-line stretch between two of them, and resolving that needs ~20 markers per frame --
        // 4x the calls at 5 KB each. So phases get their own file with a FIXED record layout: every
        // slot is exactly PhaseRecordBytes wide, so a marker is one seek + one 64-byte write + flush,
        // with no StringBuilder, no encode and no allocation on the path.
        //
        // The file is a ring, so the slots are NOT in chronological order -- each record starts with
        // a zero-padded sequence number, so sorting the lines lexicographically replays the trail.
        private const bool PhaseEnabled = true;
        private const int PhaseSlots = 96;
        private const int PhaseRecordBytes = 64; // 10 seq + 1 + 12 time + 1 + 39 area + 1 newline
        private const int PhaseSeqDigits = 10;
        private const int PhaseAreaOffset = 24;
        private const int PhaseAreaWidth = PhaseRecordBytes - PhaseAreaOffset - 1;
        private static readonly object _phaseGate = new object();
        private static readonly byte[] _phaseRecord = new byte[PhaseRecordBytes];
        private static FileStream _phaseStream;
        private static int _phaseHeaderBytes;
        private static int _phaseNext;

        public static void Init()
        {
            try
            {
                string path = Path.Combine(HelperPaths.GetDirectory("Logs"), "breadcrumbs.log");
                // Share ReadWrite so the file can be tailed while the game runs.
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                try
                {
                    // Secondary trail: never let it take the main one down with it.
                    InitPhaseStream();
                }
                catch (Exception phaseEx)
                {
                    _phaseStream = null;
                    ModLogger.Warning("[Breadcrumb] phase ring unavailable: " + phaseEx.Message);
                }

                Drop("Init", "breadcrumb trail started");
                _lastHeartbeatTick = Environment.TickCount64;
                _watchdog = new Thread(WatchdogLoop) { IsBackground = true, Name = "BreadcrumbWatchdog" };
                _watchdog.Start();
                ModLogger.Msg("[Breadcrumb] trail -> " + path);
            }
            catch (Exception ex)
            {
                _disabled = true;
                ModLogger.Warning("[Breadcrumb] init failed: " + ex.Message);
            }
        }

        // Coarse marker: rings + flushes every call.
        public static void Drop(string area, string detail = null)
        {
            if (_disabled || _stream == null)
            {
                return;
            }

            // Heartbeat: a Drop from the main thread proves it is alive. Set before taking the lock so
            // the watchdog never mistakes lock contention for a hang. (The watchdog calls DropLocked
            // directly, bypassing this, so its own writes do NOT refresh the heartbeat.)
            _lastHeartbeatTick = Environment.TickCount64;
            lock (_gate)
            {
                DropLocked(area, detail);
            }
        }

        // Hot-path marker: counts every call, but only rewrites the file at most every ThrottleMs.
        public static void Tick(string area)
        {
            if (_disabled || _stream == null)
            {
                return;
            }

            long now = Environment.TickCount64;
            // Heartbeat on every Tick (even throttled ones that do not write): a tight but *progressing*
            // hot loop keeps ticking, so the watchdog won't false-positive on a slow-but-alive scan.
            _lastHeartbeatTick = now;
            lock (_gate)
            {
                _tickCounts.TryGetValue(area, out long count);
                count++;
                _tickCounts[area] = count;

                _tickNextWriteAt.TryGetValue(area, out long nextAt);
                if (now < nextAt)
                {
                    return;
                }
                _tickNextWriteAt[area] = now + ThrottleMs;
                DropLocked(area, "x" + count);
            }
        }

        // Sub-frame marker. Cheap enough to sprinkle through a single OnUpdate: one fixed-size record
        // written in place, no allocation. Refreshes the heartbeat and _lastArea too, so a hang that
        // freezes between two Drops now gets named by the watchdog at phase resolution.
        // FPS Watchdog attribution hook (FpsWatchdogFeature.cs). The phase markers already fence off
        // every stretch of OnUpdate, so the watchdog borrows them as section boundaries instead of
        // adding a second set of call sites. Null whenever the watchdog is off — the cost of the
        // hook is then one null test per marker. Called BEFORE the early-out on purpose: the marker
        // set is meaningful even when breadcrumb file IO is unavailable.
        internal static Action<string> PhaseObserver;

        public static void Phase(string area)
        {
            Action<string> observer = PhaseObserver;
            if (observer != null)
            {
                try { observer(area); } catch { }
            }

            if (!PhaseEnabled || _disabled || _phaseStream == null)
            {
                return;
            }

            _lastHeartbeatTick = Environment.TickCount64;
            _lastArea = area ?? "?";

            lock (_phaseGate)
            {
                try
                {
                    long n = Interlocked.Increment(ref _seq);
                    DateTime now = DateTime.Now;

                    byte[] rec = _phaseRecord;
                    WriteDigits(rec, 0, n, PhaseSeqDigits);
                    rec[10] = (byte)' ';
                    WriteDigits(rec, 11, now.Hour, 2);
                    rec[13] = (byte)':';
                    WriteDigits(rec, 14, now.Minute, 2);
                    rec[16] = (byte)':';
                    WriteDigits(rec, 17, now.Second, 2);
                    rec[19] = (byte)'.';
                    WriteDigits(rec, 20, now.Millisecond, 3);
                    rec[23] = (byte)' ';
                    WriteAsciiPadded(rec, PhaseAreaOffset, PhaseAreaWidth, area);
                    rec[PhaseRecordBytes - 1] = (byte)'\n';

                    int slot = _phaseNext;
                    _phaseNext = (_phaseNext + 1) % PhaseSlots;

                    _phaseStream.Seek(_phaseHeaderBytes + (slot * PhaseRecordBytes), SeekOrigin.Begin);
                    _phaseStream.Write(rec, 0, PhaseRecordBytes);
                    _phaseStream.Flush();
                }
                catch
                {
                    // Never let breadcrumb I/O throw into game code.
                }
            }
        }

        private static void InitPhaseStream()
        {
            if (!PhaseEnabled)
            {
                return;
            }

            string path = Path.Combine(HelperPaths.GetDirectory("Logs"), "breadcrumbs-phase.log");
            _phaseStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

            // Fixed-length header: the record offsets below depend on it, so it is written once and
            // never rewritten. Sort the records by their leading sequence number to read the trail.
            byte[] header = Encoding.ASCII.GetBytes(
                "# phase ring (sort lines by the leading seq to replay) build=" + BuildTag + "\n");
            _phaseHeaderBytes = header.Length;
            _phaseStream.Write(header, 0, header.Length);

            // Pre-size the ring so unwritten slots read as blanks instead of a ragged tail.
            byte[] blank = new byte[PhaseRecordBytes];
            for (int i = 0; i < PhaseRecordBytes - 1; i++)
            {
                blank[i] = (byte)' ';
            }
            blank[PhaseRecordBytes - 1] = (byte)'\n';
            for (int i = 0; i < PhaseSlots; i++)
            {
                _phaseStream.Write(blank, 0, PhaseRecordBytes);
            }
            _phaseStream.Flush();
        }

        private static void WriteDigits(byte[] buffer, int offset, long value, int width)
        {
            if (value < 0L)
            {
                value = 0L;
            }

            for (int i = width - 1; i >= 0; i--)
            {
                buffer[offset + i] = (byte)('0' + (int)(value % 10L));
                value /= 10L;
            }
        }

        private static void WriteAsciiPadded(byte[] buffer, int offset, int width, string text)
        {
            int copied = 0;
            if (text != null)
            {
                int count = text.Length < width ? text.Length : width;
                for (; copied < count; copied++)
                {
                    char c = text[copied];
                    buffer[offset + copied] = (c >= ' ' && c < (char)127) ? (byte)c : (byte)'?';
                }
            }

            for (; copied < width; copied++)
            {
                buffer[offset + copied] = (byte)' ';
            }
        }

        private static void DropLocked(string area, string detail)
        {
            _lastArea = area ?? "?";
            try
            {
                long n = Interlocked.Increment(ref _seq);
                string line = string.Concat(
                    n.ToString(), "\t",
                    DateTime.Now.ToString("HH:mm:ss.fff"), "\tT",
                    Thread.CurrentThread.ManagedThreadId.ToString(), "\t",
                    area ?? "?",
                    detail != null ? (" | " + detail) : string.Empty);

                _ring[_next] = line;
                _next = (_next + 1) % RingSize;

                // Rewrite the whole (tiny) ring oldest-first, then flush to the OS so the bytes
                // survive an instant process kill (Flush() hands them to the OS file cache).
                // First line is a fixed build header so the running DLL is always identifiable.
                StringBuilder sb = new StringBuilder(RingSize * 96);
                sb.Append("# build=").Append(BuildTag).Append('\n');
                for (int i = 0; i < RingSize; i++)
                {
                    string s = _ring[(_next + i) % RingSize];
                    if (s != null)
                    {
                        sb.Append(s).Append('\n');
                    }
                }

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                _stream.Seek(0, SeekOrigin.Begin);
                _stream.SetLength(bytes.Length);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch
            {
                // Never let breadcrumb I/O throw into game code.
            }
        }

        // Runs on a dedicated background thread. Detects a stalled main-thread heartbeat and records a
        // WATCHDOG.hang crumb (with the last area reached) so silent hangs leave a trail too. Reports
        // once per stall; rearms when the main thread recovers (heartbeat refreshes).
        private static void WatchdogLoop()
        {
            bool reported = false;
            while (!_disabled && _stream != null)
            {
                try
                {
                    Thread.Sleep(1000);

                    long hb = _lastHeartbeatTick;
                    if (hb == 0L)
                    {
                        continue; // main loop not started yet
                    }

                    long stuck = Environment.TickCount64 - hb;
                    if (stuck < HangThresholdMs)
                    {
                        reported = false; // alive (or recovered) -> rearm
                        continue;
                    }

                    if (reported)
                    {
                        continue; // already logged this stall
                    }
                    reported = true;

                    // The main thread is frozen and is NOT holding _gate (Drop/Tick release it
                    // immediately), so TryEnter should succeed quickly. DropLocked deliberately does not
                    // touch the heartbeat, so this write keeps the stall "stuck" for recovery detection.
                    if (Monitor.TryEnter(_gate, 1000))
                    {
                        try
                        {
                            DropLocked("WATCHDOG.hang", "stuck=" + stuck + "ms lastArea=" + _lastArea + " (main thread frozen)");
                        }
                        finally
                        {
                            Monitor.Exit(_gate);
                        }
                    }
                }
                catch
                {
                    // Never let the watchdog thread die on a transient error.
                }
            }
        }
    }
}
