using System;
using System.Collections.Generic;
using System.IO;

// Loader-neutral log front. The entry point that actually runs installs its sink;
// loader assemblies are only touched inside the adapter classes below, so the CLR
// never tries to resolve MelonLoader.dll under BepInEx or BepInEx.*.dll under
// MelonLoader (types/methods referencing them are never loaded/JITted).
public static class ModLogger
{
    private static Action<string> _msg;
    private static Action<string> _warn;

    public static void SetSinks(Action<string> msg, Action<string> warn)
    {
        _msg = msg;
        _warn = warn;
    }

    public static void Msg(string message)
    {
        _msg?.Invoke(message);
#if FEATURE_MCP
        RingAdd("INFO", message);
#endif
    }

    public static void Warning(string message)
    {
        _warn?.Invoke(message);
#if FEATURE_MCP
        RingAdd("WARN", message);
#endif
    }

#if FEATURE_MCP
    // ── Session log ring, for the MCP bridge's `log.tail` ────────────────────────────────────────
    // Reading the loader's own log FILE would mean racing the StreamWriter that is still appending
    // to it, from a different thread, with no shared lock. A ring the log front owns is race-free
    // and gives the agent the mod's lines only — not BepInEx's whole console.
    //
    // Allocated on demand: without the `%LocalLow%/Bugtopia/mcp` marker, EnableRing() is never
    // called, the array is never created, and every log call costs one bool test.
    // ⚠️ 256 LINES IS TWENTY SECONDS. That is what it held in practice: the farm alone writes three
    // or four lines a second, and every request to "look at the log" arrived after the interesting
    // part had already been overwritten — a livelock, a pair of teleports and a whole bubble walk
    // were each lost that way. A filtered tail can only find what is still in the ring.
    //
    // 4096 lines is about five minutes of a chatty run and costs well under a megabyte of strings,
    // allocated only when the MCP marker file turns the ring on at all.
    private const int RingCapacity = 4096;
    private static readonly object RingLock = new object();
    private static string[] _ring;
    private static int _ringNext;
    private static int _ringCount;

    internal static bool RingEnabled { get; private set; }

    internal static void EnableRing()
    {
        lock (RingLock)
        {
            if (_ring == null)
            {
                _ring = new string[RingCapacity];
            }

            RingEnabled = true;
        }
    }

    private static void RingAdd(string level, string message)
    {
        if (!RingEnabled)
        {
            return;
        }

        try
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + " [" + level + "] " + message;
            lock (RingLock)
            {
                if (_ring == null)
                {
                    return;
                }

                _ring[_ringNext] = line;
                _ringNext = (_ringNext + 1) % _ring.Length;
                if (_ringCount < _ring.Length)
                {
                    _ringCount++;
                }
            }
        }
        catch
        {
            // A logging path must never throw into its caller.
        }
    }

    // Oldest-to-newest, optionally filtered, then trimmed to the last `max` entries.
    internal static string[] RingSnapshot(int max, string filter)
    {
        List<string> matched = new List<string>(Math.Min(max, RingCapacity));
        lock (RingLock)
        {
            if (_ring == null || _ringCount == 0)
            {
                return Array.Empty<string>();
            }

            int start = (_ringNext - _ringCount + _ring.Length) % _ring.Length;
            for (int i = 0; i < _ringCount; i++)
            {
                string line = _ring[(start + i) % _ring.Length];
                if (line == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter)
                    && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matched.Add(line);
            }
        }

        if (matched.Count <= max)
        {
            return matched.ToArray();
        }

        return matched.GetRange(matched.Count - max, max).ToArray();
    }
#endif
}

// MelonLoader-backed sink. Only compiled under LOADER_MELON (MelonLoader / Universal), so a
// BepInEx-only build never references MelonLoader.dll.
#if LOADER_MELON
internal static class MelonLogAdapter
{
    public static void Install() => ModLogger.SetSinks(Msg, Warning);

    private static void Msg(string message) => MelonLoader.MelonLogger.Msg(message);

    private static void Warning(string message) => MelonLoader.MelonLogger.Warning(message);
}
#endif

// BepInEx-backed sink; also mirrors to Logs/bugtopia.log under the mod's LocalLow root.
// Only compiled under LOADER_BEPINEX.
#if LOADER_BEPINEX
internal static class BepInExLogAdapter
{
    private static BepInEx.Logging.ManualLogSource _log;
    private static StreamWriter _fileLog;

    public static void Install(BepInEx.Logging.ManualLogSource log)
    {
        _log = log;
        try
        {
            // Was BaseDirectory\UserData — the GAME folder. That was always a write into a
            // Steam-managed directory, and it is doubly wrong now that the loader lives outside the
            // game folder: the log ended up nowhere near BepInEx's own. HelperPaths puts it beside
            // Config.xml and the crash breadcrumbs, and creates the folder itself.
            string path = Path.Combine(HeartopiaMod.HelperPaths.GetDirectory("Logs"), "bugtopia.log");
            string rotation = RotatePreviousLog(path);
            _fileLog = new StreamWriter(path, append: true) { AutoFlush = true };
            _fileLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === session start ===");
            if (rotation != null)
            {
                _fileLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] [Log] {rotation}");
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning("Could not open Logs/bugtopia.log: " + ex.Message);
        }

        ModLogger.SetSinks(Msg, Warning);
    }

    // One log file per game launch. The previous session's bugtopia.log is MOVED into
    // Logs\archive\ under the time it was last written to (i.e. when that session ended), and
    // nothing is ever deleted — pruning is the user's call, not the mod's.
    //
    // Why per-launch and not per-size: the single appended file had reached 170 MB across 24 days,
    // which made "what did this session do" an offset hunt through session-start markers. A file
    // per launch answers that by its name.
    //
    // The current session always keeps the plain `bugtopia.log` name, so every doc, tool and
    // crash-report path that points at it stays correct.
    //
    // A rotation failure is NOT fatal: the file stays where it is and the session appends to it,
    // exactly like before. Losing a log to a locked file would be far worse than a long one.
    private static string RotatePreviousLog(string path)
    {
        try
        {
            FileInfo previous = new FileInfo(path);
            if (!previous.Exists || previous.Length == 0L)
            {
                return null;
            }

            string archiveDir = HeartopiaMod.HelperPaths.GetDirectory("Logs", "archive");
            string stamp = previous.LastWriteTime.ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(archiveDir, "bugtopia-" + stamp + ".log");
            // Two launches inside one second, or a restored backup, must not silently overwrite.
            for (int i = 2; i < 1000 && File.Exists(target); i++)
            {
                target = Path.Combine(archiveDir, "bugtopia-" + stamp + "-" + i + ".log");
            }

            if (File.Exists(target))
            {
                return "previous log NOT rotated (archive name collision) — appending instead";
            }

            long kb = previous.Length / 1024L;
            File.Move(path, target);
            return "previous session log rotated -> Logs\\archive\\" + Path.GetFileName(target)
                + " (" + kb + " KB); nothing is ever deleted from that folder";
        }
        catch (Exception ex)
        {
            return "previous log NOT rotated (" + ex.Message + ") — appending instead";
        }
    }

    private static void WriteFile(string level, string message)
    {
        try
        {
            _fileLog?.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
        }
        catch
        {
        }
    }

    private static void Msg(string message)
    {
        _log?.LogInfo(message);
        WriteFile("INFO", message);
    }

    private static void Warning(string message)
    {
        _log?.LogWarning(message);
        WriteFile("WARN", message);
    }
}
#endif

// Circuit breaker for per-frame feature ticks. A tick that throws repeatedly goes into a
// cooldown instead of hammering (and log-spamming) every frame; after several cooldown cycles
// in a row it is disabled for the session. One successful tick resets the whole state.
// Usage (no allocations on the hot path):
//   if (breaker.ShouldRun(now)) {
//       try { Tick(); breaker.Success(); }
//       catch (Exception ex) { breaker.Failure("Name", ex, now); }
//   }
public struct FeatureBreakerState
{
    private const int FailuresPerCooldown = 5;
    private const float CooldownSeconds = 30f;
    private const int CooldownCyclesUntilDisable = 5;

    private int consecutiveFailures;
    private int cooldownCycles;
    private float retryAt;
    private bool disabled;

    public bool ShouldRun(float now) => !disabled && now >= retryAt;

    public void Success()
    {
        consecutiveFailures = 0;
        cooldownCycles = 0;
    }

    public void Failure(string name, System.Exception ex, float now)
    {
        consecutiveFailures++;
        if (consecutiveFailures == 1)
        {
            ModLogger.Msg("[" + name + "] tick exception: " + ex.Message);
        }
        if (consecutiveFailures < FailuresPerCooldown)
        {
            return;
        }

        consecutiveFailures = 0;
        cooldownCycles++;
        if (cooldownCycles >= CooldownCyclesUntilDisable)
        {
            disabled = true;
            ModLogger.Warning("[" + name + "] disabled for this session after repeated tick exceptions: " + ex.Message);
        }
        else
        {
            retryAt = now + CooldownSeconds;
            ModLogger.Warning("[" + name + "] cooling down " + (int)CooldownSeconds + "s after repeated tick exceptions (cycle " + cooldownCycles + "/" + CooldownCyclesUntilDisable + ").");
        }
    }
}

// Last-resort exception guard for the loader entry points (Update/LateUpdate/OnGUI).
// An exception escaping those callbacks travels through the IL2CPP/interop trampoline,
// where it can abort the process or silently kill the rest of the frame's features,
// so callers catch at the boundary and report here. Reports are throttled per
// site+exception so a fault that fires every frame cannot flood the log.
public static class ModEntryGuard
{
    private const int ThrottleMs = 5000;
    private static readonly Dictionary<int, int> _lastReportTick = new Dictionary<int, int>();

    public static void Report(string site, Exception ex)
    {
        try
        {
            int key = (site?.GetHashCode() ?? 0) ^ ((ex.GetType().GetHashCode() * 397) ^ (ex.Message?.GetHashCode() ?? 0));
            int now = Environment.TickCount;
            lock (_lastReportTick)
            {
                if (_lastReportTick.TryGetValue(key, out int last) && unchecked(now - last) < ThrottleMs)
                {
                    return;
                }
                _lastReportTick[key] = now;
            }
            ModLogger.Warning("[Guard] Unhandled exception in " + site + ": " + ex);
        }
        catch
        {
        }
    }
}
