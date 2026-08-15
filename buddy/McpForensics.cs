#if FEATURE_MCP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeartopiaMod
{
    // ============================================================================================
    // Crash forensics for the MCP bridge.
    //
    // The bridge deliberately hands an agent things that can kill the process: raw invokes, hot
    // loaded plugins, arbitrary compiled snippets. A wrong generic instantiation or a stale pointer
    // is an UNCATCHABLE native access violation — no exception, no log line, no stack. The process
    // is simply gone, and the next session has no idea why.
    //
    // So the last dangerous operation is written to disk BEFORE it runs and cleared after it
    // returns. A non-empty file at startup is therefore proof that the previous session died inside
    // that exact operation. That single fact converts "the game crashed at some point" into "the
    // game died running plugin X, sha Y, at frame Z".
    //
    // ── WHY ONLY WRITE/UNSAFE OPS ────────────────────────────────────────────────────────────────
    // Arming costs a file write. Doing it for every op would tax `status` and `log.tail`, which an
    // agent polls and which cannot crash anything. The cheap ops already leave a Breadcrumbs.Phase
    // marker (one 64-byte write, no file open), and that is enough for them.
    // ============================================================================================
    internal static class McpForensics
    {
        private const string LastOpFile = "lastop.json";
        private const string QuarantineFile = "quarantine.json";
        private const string ResidentFile = "resident.json";

        // What the PREVIOUS session was doing when it died, or null if it exited cleanly.
        internal static string PreviousCrashJson;
        internal static bool PreviousSessionCrashed;

        // Which plugins were LOADED AND TICKING when it died. A separate record because the
        // operation log only covers synchronous "died inside this call" — and the likeliest way for
        // a sandbox plugin to kill the game is its per-frame Tick, which runs outside the op pump
        // entirely. Arming per Tick is impossible (a file write every frame per plugin), so this is
        // written once on load and once on unload instead: weaker attribution than "died in this
        // call", but it is the difference between a suspect and no record at all.
        internal static string PreviousResidentJson;
        internal static bool PreviousSessionHadResident;
        // True only when the previous session had a working quit signal AND did not use it.
        internal static bool PreviousSessionKilled;


        private static readonly HashSet<string> Quarantined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> QuarantineReasons = new List<string>();

        private static string armedSha;
        private static bool armed;

        private static string Dir() => HelperPaths.GetDirectory("McpBridge");

        private static string PathOf(string file) => Path.Combine(Dir(), file);

        // ── Startup ──────────────────────────────────────────────────────────────────────────────

        // Reads the previous session's verdict, quarantines whatever was running, and clears the
        // file. Called once from InitializeMcpBridge, before the listener starts.
        internal static void ReadPreviousSession()
        {
            LoadQuarantine();
            // BOTH records, unconditionally. These used to be one method, and every `return` in the
            // lastop branch skipped the resident read at the end — so the one case the resident
            // record exists FOR (a crash in a plugin's Tick, which leaves lastop empty) was the one
            // case that never reached it. Two calls, no shared control flow.
            ReadPreviousLastOp();
            ReadPreviousResident();
        }

        private static void ReadPreviousLastOp()
        {
            string path = PathOf(LastOpFile);
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                PreviousCrashJson = text;
                PreviousSessionCrashed = true;

                string sha = ExtractString(text, "sha");
                string op = ExtractString(text, "op");
                string detail = ExtractString(text, "detail");

                ModLogger.Warning("[Mcp] the PREVIOUS session died while running '" + op + "'"
                    + (string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")")
                    + " — see McpBridge/lastop.json.");

                if (!string.IsNullOrEmpty(sha))
                {
                    // Auto-quarantine, or an auto-reloading agent walks straight back into the same
                    // crash on the next launch, and the next, and the next.
                    AddQuarantine(sha, "was running when the previous session died"
                        + (string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")"));
                    ModLogger.Warning("[Mcp] quarantined " + sha.Substring(0, Math.Min(12, sha.Length))
                        + " — plugin.load will refuse it unless force:true.");
                }

                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] could not read the previous session record: " + ex.Message);
            }
        }

        // Which plugins were loaded when the previous session ended — INFORMATIONAL ONLY.
        //
        // ⚠️ This was designed to auto-quarantine them, on the assumption that a non-empty file meant
        // the process was killed rather than closed. MEASURED 2026-08-15: that assumption is false on
        // this build. `AppDomain.ProcessExit` does not run when the game quits normally — Unity tears
        // the process down without letting CoreCLR shut down — so the record survives an ordinary
        // quit exactly as it would survive a crash. (The mod already had a latent version of this:
        // PlayerLoopProbe hangs its teardown on the same event, harmlessly.)
        //
        // Quarantining on that signal would have blocked innocent plugins after every normal exit,
        // which is worse than no attribution at all. So the record is reported and nothing more.
        //
        // Restoring the auto-quarantine needs a REAL quit signal — `Application.quitting` subscribed
        // through HookFreeDelegate (its ThunkVoid shape already matches `Action`), which is the only
        // in-process event that distinguishes "quit" from "killed". Until that exists and is
        // verified, `lastop.json` remains the only crash attribution, and it covers synchronous ops
        // only — a plugin that dies in its per-frame Tick is still unattributed.
        private static void ReadPreviousResident()
        {
            string path = PathOf(ResidentFile);
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.Trim() == "[]")
                {
                    return;
                }

                PreviousResidentJson = text;
                PreviousSessionHadResident = true;
                File.WriteAllText(path, string.Empty);

                // The record carries whether THAT session had a working quit signal. Only then does
                // its survival prove anything: a session that could have cleared this on exit and
                // did not was killed. Without the flag the same file is simply "what was loaded",
                // because an ordinary quit leaves it too.
                bool previousHadQuitSignal = text.IndexOf("\"quitSignal\":true", StringComparison.Ordinal) >= 0;
                if (!previousHadQuitSignal)
                {
                    return;
                }

                List<string> shas = ExtractAllStrings(text, "sha");
                PreviousSessionKilled = true;
                ModLogger.Warning("[Mcp] the previous session was KILLED (it had a working quit signal "
                    + "and never used it) with " + shas.Count + " sandbox plugin(s) resident.");

                foreach (string sha in shas)
                {
                    AddQuarantine(sha, "was resident and ticking when the previous session was killed "
                        + "— suspicion, not proof: release it with session.report unquarantine");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] could not read the resident-plugin record: " + ex.Message);
            }
        }

        // ── Resident set ─────────────────────────────────────────────────────────────────────────

        internal static void WriteResident(string json)
        {
            try
            {
                File.WriteAllText(PathOf(ResidentFile), json ?? "[]", new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        internal static void ClearResident()
        {
            try
            {
                File.WriteAllText(PathOf(ResidentFile), string.Empty);
            }
            catch
            {
            }
        }

        // ── Arm / disarm ─────────────────────────────────────────────────────────────────────────

        internal static void Arm(string op, string detail, string sha, int frame, bool worldReady)
        {
            armed = true;
            armedSha = sha;
            try
            {
                McpJsonWriter w = new McpJsonWriter();
                w.BeginObject();
                w.Str("op", op);
                w.Str("detail", detail);
                w.Str("sha", sha);
                w.Num("frame", frame);
                w.Bool("worldReady", worldReady);
                w.Str("utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                w.Str("version", ModBuildVersion.Display);
                w.EndObject();

                File.WriteAllText(PathOf(LastOpFile), w.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Forensics must never break the operation it is describing.
            }
        }

        internal static void Disarm()
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            armedSha = null;
            try
            {
                File.WriteAllText(PathOf(LastOpFile), string.Empty);
            }
            catch
            {
            }
        }

        // ── Quarantine ───────────────────────────────────────────────────────────────────────────

        internal static bool IsQuarantined(string sha)
        {
            return !string.IsNullOrEmpty(sha) && Quarantined.Contains(sha);
        }

        internal static void AddQuarantine(string sha, string reason)
        {
            if (string.IsNullOrEmpty(sha) || !Quarantined.Add(sha))
            {
                return;
            }

            QuarantineReasons.Add(sha + "\t" + (reason ?? string.Empty));
            SaveQuarantine();
        }

        internal static bool RemoveQuarantine(string sha)
        {
            if (string.IsNullOrEmpty(sha) || !Quarantined.Remove(sha))
            {
                return false;
            }

            for (int i = QuarantineReasons.Count - 1; i >= 0; i--)
            {
                if (QuarantineReasons[i].StartsWith(sha, StringComparison.OrdinalIgnoreCase))
                {
                    QuarantineReasons.RemoveAt(i);
                }
            }

            SaveQuarantine();
            return true;
        }

        private static void LoadQuarantine()
        {
            try
            {
                string path = PathOf(QuarantineFile);
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    QuarantineReasons.Add(line);
                    int tab = line.IndexOf('\t');
                    Quarantined.Add(tab > 0 ? line.Substring(0, tab) : line.Trim());
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] quarantine list unreadable: " + ex.Message);
            }
        }

        private static void SaveQuarantine()
        {
            try
            {
                File.WriteAllLines(PathOf(QuarantineFile), QuarantineReasons, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        // ── Reporting ────────────────────────────────────────────────────────────────────────────

        internal static void WriteReportJson(McpJsonWriter w)
        {
            w.Bool("previousSessionCrashed", PreviousSessionCrashed);
            if (PreviousSessionCrashed && !string.IsNullOrEmpty(PreviousCrashJson))
            {
                w.Raw("previousOp", PreviousCrashJson);
            }

            // Context, NOT evidence — and the difference is spelled out because the two fields look
            // alike: `previousOp` means "died inside this call"; this only means "was loaded when the
            // session ended", which on this build is equally true after an ordinary quit.
            w.Bool("previousSessionHadResidentPlugins", PreviousSessionHadResident);
            w.Bool("previousSessionKilled", PreviousSessionKilled);
            w.Bool("quitSignalArmed", McpQuitSignal.Armed);
            w.Str("quitSignal", McpQuitSignal.Status);
            if (PreviousSessionHadResident && !string.IsNullOrEmpty(PreviousResidentJson))
            {
                w.Raw("previousResident", PreviousResidentJson);
                w.Str("previousResidentNote", PreviousSessionKilled
                    ? "that session had a working quit signal and never used it, so it was KILLED "
                      + "rather than closed; the plugins above were resident at the time and are "
                      + "quarantined on suspicion, not proof."
                    : "informational only — that session had no working quit signal, so an ordinary "
                      + "quit leaves exactly this record and nothing was quarantined from it.");
            }

            w.BeginArray("quarantined");
            for (int i = 0; i < QuarantineReasons.Count; i++)
            {
                string line = QuarantineReasons[i];
                int tab = line.IndexOf('\t');
                w.BeginArrayObject();
                w.Str("sha", tab > 0 ? line.Substring(0, tab) : line);
                w.Str("reason", tab > 0 ? line.Substring(tab + 1) : string.Empty);
                w.EndObject();
            }

            w.EndArray();

            // The newest dump on disk is almost certainly the one that matches the record above, and
            // handing over its path is what lets the crash-dump-stack workflow start immediately
            // instead of with a filesystem hunt.
            w.BeginArray("recentDumps");
            try
            {
                foreach (string dump in FindRecentDumps(4))
                {
                    w.ArrayStr(dump);
                }
            }
            catch
            {
            }

            w.EndArray();
        }

        private static List<string> FindRecentDumps(int max)
        {
            List<KeyValuePair<DateTime, string>> found = new List<KeyValuePair<DateTime, string>>();
            List<string> roots = new List<string>();

            try
            {
                roots.Add(Path.Combine(HelperPaths.Root, "CrashDumps"));
            }
            catch
            {
            }

            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    roots.Add(Path.Combine(local, "CrashDumps"));
                }
            }
            catch
            {
            }

            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (string file in Directory.EnumerateFiles(root, "*.dmp"))
                    {
                        found.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(file), file));
                    }
                }
                catch
                {
                }
            }

            found.Sort((a, b) => b.Key.CompareTo(a.Key));
            List<string> result = new List<string>(Math.Min(max, found.Count));
            for (int i = 0; i < found.Count && i < max; i++)
            {
                result.Add(found[i].Value);
            }

            return result;
        }

        private static List<string> ExtractAllStrings(string json, string key)
        {
            List<string> found = new List<string>();
            try
            {
                string needle = "\"" + key + "\":\"";
                int at = 0;
                while (true)
                {
                    at = json.IndexOf(needle, at, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        break;
                    }

                    at += needle.Length;
                    int end = json.IndexOf('"', at);
                    if (end < 0)
                    {
                        break;
                    }

                    found.Add(json.Substring(at, end - at));
                    at = end;
                }
            }
            catch
            {
            }

            return found;
        }

        // Deliberately not a JSON parse: this runs during startup on a file that a crashed process
        // may have left half-written, and a torn value must degrade to null rather than throw.
        private static string ExtractString(string json, string key)
        {
            try
            {
                string needle = "\"" + key + "\":\"";
                int at = json.IndexOf(needle, StringComparison.Ordinal);
                if (at < 0)
                {
                    return null;
                }

                at += needle.Length;
                int end = json.IndexOf('"', at);
                return end < 0 ? null : json.Substring(at, end - at);
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
