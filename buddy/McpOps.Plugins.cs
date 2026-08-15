#if FEATURE_MCP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // MCP plugin ops (phase 4) — the hot-reload loop.
    //
    // Every one of these is UNSAFE tier: a sandbox plugin runs arbitrary code inside the game
    // process and can take it down. They stay refused until someone ticks "allow unsafe ops" in
    // Settings → Logging for the session — the marker file authorises the CHANNEL, a human
    // authorises the privilege, and neither is persisted.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private void RegisterMcpPluginOps()
        {
            McpOps.Register(
                "plugin.list",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Loaded sandbox plugins, their tick counts and last errors, plus any that failed to "
                + "collect after unload (`leaked` — functionally unloaded, memory returns on restart).",
                "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                this.HandleMcpPluginList);

            McpOps.Register(
                "plugin.load",
                McpOpFlags.Write | McpOpFlags.Unsafe,
                McpOpCost.Heavy,
                "Load a base64 assembly into a collectible load context and call its Load(). The "
                + "assembly must contain exactly one public parameterless-constructible type "
                + "implementing HeartopiaMod.Plugins.IBugtopiaPlugin, and is REJECTED before loading "
                + "if it references Harmony/MonoMod, uses ClassInjector, DelegateSupport, Thread, "
                + "Timer, or declares a [DllImport] — each of those makes the context uncollectible. "
                + "All violations are reported at once.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\",\"description\":\"identifier used by unload/call\"},"
                + "\"dll\":{\"type\":\"string\",\"description\":\"base64 assembly bytes\"},"
                + "\"pdb\":{\"type\":\"string\",\"description\":\"base64 pdb, optional — gives line numbers in stack traces\"},"
                + "\"replace\":{\"type\":\"boolean\",\"description\":\"unload an existing plugin with this id first\"},"
                + "\"force\":{\"type\":\"boolean\",\"description\":\"load even if these bytes are quarantined for having crashed a previous session\"}},"
                + "\"required\":[\"id\",\"dll\"],\"additionalProperties\":false}",
                this.HandleMcpPluginLoad);

            McpOps.Register(
                "session.report",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Whether the PREVIOUS session died mid-operation and what it was running at the time, "
                + "the quarantine list, and the newest crash dumps on disk. Read this first after an "
                + "unexpected reconnect — an uncatchable native crash leaves no log line, so this "
                + "record is the only account of it.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"unquarantine\":{\"type\":\"string\",\"description\":\"sha256 to release from quarantine\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpSessionReport);

            McpOps.Register(
                "plugin.unload",
                McpOpFlags.Write | McpOpFlags.Unsafe,
                McpOpCost.Cheap,
                "Unload a plugin: stop ticking it, call Unload(), revoke its coroutines, drop every "
                + "host reference and unload its context. Collection is then verified over the next "
                + "few frames — check plugin.list for the outcome.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\"}},\"required\":[\"id\"],\"additionalProperties\":false}",
                this.HandleMcpPluginUnload);

            McpOps.Register(
                "plugin.call",
                McpOpFlags.Write | McpOpFlags.Unsafe,
                McpOpCost.Heavy,
                "Invoke a loaded plugin's Call(method, argsJson) on the main thread and return "
                + "whatever it produced.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\"},"
                + "\"method\":{\"type\":\"string\"},"
                + "\"args\":{\"type\":\"string\",\"description\":\"raw JSON string handed to the plugin verbatim\"}},"
                + "\"required\":[\"id\",\"method\"],\"additionalProperties\":false}",
                this.HandleMcpPluginCall);
        }

        private void ProcessMcpPluginsOnUpdate()
        {
            PluginHost.Tick();
        }

        // The toast surface is private to HeartopiaComplete, and PluginHost's HostApi is a nested
        // type of a different class — so the bridge goes through this partial, which does have
        // access. Cheaper than widening AddMenuNotification for one caller.
        internal void McpPluginToast(string message)
        {
            this.AddMenuNotification(message, Color.white);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpSessionReport(Dictionary<string, object> args)
        {
            string release = McpArgs.GetString(args, "unquarantine");
            bool released = false;
            if (!string.IsNullOrEmpty(release))
            {
                released = McpForensics.RemoveQuarantine(release);
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            if (!string.IsNullOrEmpty(release))
            {
                w.Bool("released", released);
            }

            McpForensics.WriteReportJson(w);
            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpPluginList(Dictionary<string, object> args)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            PluginHost.WriteListJson(w);
            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpPluginLoad(Dictionary<string, object> args)
        {
            string id = McpArgs.GetString(args, "id");
            string dllB64 = McpArgs.GetString(args, "dll");
            string pdbB64 = McpArgs.GetString(args, "pdb");
            bool replace = McpArgs.GetBool(args, "replace", true);

            if (string.IsNullOrEmpty(id))
            {
                throw new McpOpException("bad_args", "'id' is required");
            }

            if (string.IsNullOrEmpty(dllB64))
            {
                throw new McpOpException("bad_args", "'dll' (base64) is required");
            }

            byte[] dll;
            byte[] pdb = null;
            try
            {
                dll = Convert.FromBase64String(dllB64);
                if (!string.IsNullOrEmpty(pdbB64))
                {
                    pdb = Convert.FromBase64String(pdbB64);
                }
            }
            catch (FormatException ex)
            {
                throw new McpOpException("bad_args", "base64 decode failed: " + ex.Message);
            }

            string sha = PluginHost.Sha256Hex(dll);
            bool force = McpArgs.GetBool(args, "force", false);

            // Refuse what was running when the last session died, unless told otherwise. Without
            // this, an agent that auto-reloads its work walks straight back into the same crash on
            // every launch.
            if (!force && McpForensics.IsQuarantined(sha))
            {
                throw new McpOpException("plugin_error",
                    "sha " + sha.Substring(0, 12) + " is quarantined — it was running when a previous "
                    + "session died. Read session.report for the record, then retry with force:true "
                    + "if you believe it was not the cause.");
            }

            // Re-arm the crash record now that the identity is known: if this load is what kills the
            // process, the next session learns exactly WHICH bytes did it, not just "plugin.load".
            McpForensics.Arm("plugin.load", id, sha, Time.frameCount, this.IsWorldReady);

            if (replace)
            {
                // Reload is the common case, so it is the default: a fresh build with the same id
                // should just take over rather than erroring about a duplicate.
                PluginHost.TryUnload(id, out _);
            }

            bool ok = PluginHost.TryLoad(id, dll, pdb, out string error, out List<string> violations);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Bool("loaded", ok);
            w.Str("id", id);
            w.Num("bytes", dll.Length);
            w.Str("sha256", sha);
            if (!ok)
            {
                w.Str("error", error);
                if (violations != null && violations.Count > 0)
                {
                    w.BeginArray("violations");
                    for (int i = 0; i < violations.Count; i++)
                    {
                        w.ArrayStr(violations[i]);
                    }

                    w.EndArray();
                }
            }

            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpPluginUnload(Dictionary<string, object> args)
        {
            string id = McpArgs.GetString(args, "id");
            if (string.IsNullOrEmpty(id))
            {
                throw new McpOpException("bad_args", "'id' is required");
            }

            bool ok = PluginHost.TryUnload(id, out string error);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Bool("unloaded", ok);
            w.Str("id", id);
            if (!ok)
            {
                w.Str("error", error);
            }
            else
            {
                w.Str("note", "collection is verified over the next few frames — read plugin.list for the outcome");
            }

            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpPluginCall(Dictionary<string, object> args)
        {
            string id = McpArgs.GetString(args, "id");
            string method = McpArgs.GetString(args, "method");
            string argsJson = McpArgs.GetString(args, "args", "{}");

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(method))
            {
                throw new McpOpException("bad_args", "'id' and 'method' are required");
            }

            // The pump armed this op WITHOUT a sha (it only knows the args). Supply it now: the
            // plugin's actual work runs here, not in plugin.load, so a crash during a call is the
            // likely case — and without the sha the next session would name the op but quarantine
            // nothing.
            if (PluginHost.TryGetSha(id, out string callSha))
            {
                McpForensics.Arm("plugin.call", id + "." + method, callSha, Time.frameCount, this.IsWorldReady);
            }

            string result = PluginHost.CallPlugin(id, method, argsJson, out string error);
            if (error != null)
            {
                throw new McpOpException("plugin_error", error);
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Str("id", id);
            w.Str("method", method);
            // The plugin's answer is spliced raw when it parses as JSON, and quoted otherwise — a
            // plugin returning a bare string should not corrupt the envelope.
            if (LooksLikeJson(result))
            {
                w.Raw("result", result);
            }
            else
            {
                w.Str("result", result);
            }

            w.EndObject();
            return w.ToString();
        }

        private static bool LooksLikeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string trimmed = value.TrimStart();
            if (trimmed.Length == 0)
            {
                return false;
            }

            char c = trimmed[0];
            if (c != '{' && c != '[')
            {
                return false;
            }

            try
            {
                McpJsonParser.Parse(value);
                return true;
            }
            catch (McpJsonException)
            {
                return false;
            }
        }
    }
}
#endif
