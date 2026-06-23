using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace HeartopiaMod
{
    // Crash breadcrumb trail. Some process deaths leave no dump and no crashlog: a stack overflow
    // skips every user-mode handler, a hard TerminateProcess / IL2CPP-mono abort raises no SEH that
    // WER, Unity's UnityCrashHandler, or our CrashDumpHandler can catch, and all logs just truncate
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
    internal static class Breadcrumbs
    {
        // Bump on every diagnostic deploy so the running build is verifiable from the log header.
        private const string BuildTag = "2026-06-23T17 faceshop-colordata-pin-FIX";
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

        public static void Init()
        {
            try
            {
                string path = Path.Combine(HelperPaths.GetDirectory("CrashDumps"), "breadcrumbs.log");
                // Share ReadWrite so the file can be tailed while the game runs.
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                Drop("Init", "breadcrumb trail started");
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

        private static void DropLocked(string area, string detail)
        {
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
    }
}
