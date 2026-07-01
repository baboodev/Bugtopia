using System;
using System.Collections.Generic;
using System.IO;

#if MELONLOADER
using MelonLoader;
#elif BEPINEX
using BepInEx.Logging;
#endif

public static class ModLogger
{
#if BEPINEX
    private static ManualLogSource _log;
    private static StreamWriter _fileLog;

    public static void Init(ManualLogSource log)
    {
        _log = log;
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "bugtopia.log");
            _fileLog = new StreamWriter(path, append: true) { AutoFlush = true };
            _fileLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === session start ===");
        }
        catch (Exception ex)
        {
            _log?.LogWarning("Could not open UserData/bugtopia.log: " + ex.Message);
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
#endif

    public static void Msg(string message)
    {
#if MELONLOADER
        MelonLogger.Msg(message);
#elif BEPINEX
        _log?.LogInfo(message);
        WriteFile("INFO", message);
#endif
    }

    public static void Warning(string message)
    {
#if MELONLOADER
        MelonLogger.Warning(message);
#elif BEPINEX
        _log?.LogWarning(message);
        WriteFile("WARN", message);
#endif
    }
}

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
