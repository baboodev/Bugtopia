using System;
using System.Collections.Generic;

// Two-tier feature logging.
//
// The problem this solves: every subsystem used to route ALL of its output through a
// `MasterLog*` verbose flag, and 40 of the 60 flags ship OFF. A feature could therefore run for
// eighty minutes and leave the log completely empty — there was no way, after the fact, to tell
// whether it had been switched on at all. That is not a hypothetical: an entire bird-farm run
// (4184 photos, 81 minutes) was invisible except for one accidental event-subscription line.
//
//   Tier 1 — Life / Once / Toggle / Fail: UNCONDITIONAL. Enable, disable, first action of the
//            session, end-of-run totals, and every failure. A handful of lines per run, and they
//            are the ones that answer "did this feature work, and when".
//   Tier 2 — Detail: gated behind the feature's MasterLog* flag. Per-tick traces, field dumps,
//            resolver candidates. Unchanged from before.
//
// Tier 1 must stay CHEAP. Subsystems that tick every frame call Once(), which writes at most one
// line per (tag, key) per session — that is the difference between a useful record and the
// 792,000 AuraFarm lines a per-tick logger produced in 24 days.
//
// See docs/BUILD_AND_RUN.md (Logging) and the user rule in [[errors-and-detail-to-log]]: failure
// text is ALWAYS logged, never gated, never toast-only.
public static class FeatureLog
{
    // Guarded by itself. Log calls arrive mostly from the Unity main thread, but the event-drain
    // and coroutine bridges are not a guarantee, and a HashSet torn by a concurrent add would
    // throw INTO A LOGGING CALL — the one place that must never fault its caller.
    private static readonly HashSet<string> OnceKeys = new HashSet<string>(StringComparer.Ordinal);

    // ── Tier 1 ──────────────────────────────────────────────────────────────────────────────────

    // Lifecycle / result line. Always written.
    public static void Life(string tag, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        ModLogger.Msg("[" + tag + "] " + message);
    }

    // Always written, but only the FIRST time this (tag, key) pair is seen this session. For the
    // "feature actually did something" line in a per-frame tick, where Life() would flood.
    // Returns true when the line was written, so a caller can build the message lazily:
    //     if (FeatureLog.ShouldSayOnce(tag, "first-catch")) FeatureLog.Life(tag, Expensive());
    public static void Once(string tag, string key, string message)
    {
        if (ShouldSayOnce(tag, key))
        {
            Life(tag, message);
        }
    }

    public static bool ShouldSayOnce(string tag, string key)
    {
        try
        {
            lock (OnceKeys)
            {
                return OnceKeys.Add(tag + "|" + key);
            }
        }
        catch
        {
            return false;
        }
    }

    // Feature switched on or off. `reason` explains an OFF that the user did not ask for
    // (world change, server refusal, breaker trip); pass null for a plain manual toggle.
    public static void Toggle(string tag, bool enabled, string reason = null)
    {
        Life(tag, enabled
            ? ("ENABLED" + (string.IsNullOrEmpty(reason) ? string.Empty : " (" + reason + ")"))
            : ("DISABLED" + (string.IsNullOrEmpty(reason) ? string.Empty : " (" + reason + ")")));
    }

    // Failure. Always written, deduped by text so a fault inside a per-frame tick reports once
    // instead of every frame. Deliberately separate from Life(): the dedupe key is the message
    // itself, so a *changing* error still gets through.
    public static void Fail(string tag, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (ShouldSayOnce(tag, "fail:" + message))
        {
            ModLogger.Warning("[" + tag + "] " + message);
        }
    }

    // ── Tier 2 ──────────────────────────────────────────────────────────────────────────────────

    // Verbose trace. Written only while the feature's MasterLog* flag is on.
    public static void Detail(string tag, bool flag, string message)
    {
        if (!flag || string.IsNullOrEmpty(message))
        {
            return;
        }

        ModLogger.Msg("[" + tag + "] " + message);
    }
}
