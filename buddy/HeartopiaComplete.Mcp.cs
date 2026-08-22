#if FEATURE_MCP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // MCP bridge — the mod-side half: startup wiring, the per-frame pump call, and the phase-1 op
    // handlers. The transport lives in McpBridgeFeature.cs; this file is where ops are allowed to
    // touch the mod's own state, because it is a partial of HeartopiaComplete and runs exclusively
    // on the Unity main thread.
    //
    // Adding an op: register it in RegisterMcpOps() with its flags/cost/schema and write the
    // handler here (or in a McpOps.<Domain>.cs partial once this file grows). Nothing else needs
    // touching — the bridge publishes the registry through `rpc.describe`, so the external MCP
    // server picks a new op up on its next connect without a rebuild.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private FeatureBreakerState mcpBreaker;

        // Called from OnInitializeMelon, right after the beta gate. Order matters: the marker is
        // read, the registry is filled, and only then does the listener start — socket threads must
        // never observe a half-built op registry.
        //
        // These three are `partial void` implementations of hooks declared in HeartopiaComplete.cs.
        // With -p:Mcp=false this file is not compiled, the implementations do not exist, and the
        // C# compiler removes the CALL SITES too — the lifecycle glue needs no #if of its own.
        partial void InitializeMcpBridge()
        {
            try
            {
                McpBridge.CheckMarker();
                if (!McpBridge.MarkerPresent)
                {
                    // Silent by design. No marker means the feature does not exist this session,
                    // and a log line every launch would advertise a surface that is not there.
                    return;
                }

                // Ring FIRST, then the first log line — otherwise the bridge's own startup message
                // is the one thing `log.tail` cannot show. Everything logged before this point
                // (loader banner, localization, beta gate) stays out of the ring by design; the
                // loader's own log file is the record for that.
                ModLogger.EnableRing();
                ModLogger.Msg("[Mcp] marker found — starting the agent bridge. "
                              + "Writes and unsafe ops stay OFF until switched on for the session.");

                // Before anything can arm a new record: read what the PREVIOUS session was doing
                // when it died, quarantine whatever that was, and clear the file.
                McpForensics.ReadPreviousSession();
                // Distinguishes "quit" from "killed". ProcessExit was the obvious choice and is
                // measurably useless here (it never runs on this game's shutdown), so the real
                // signal is Unity's Application.quitting — see McpQuitSignal.cs. Installed AFTER the
                // previous session is read, because that read is what decides using the OLD
                // session's flag, not this one's.
                McpQuitSignal.Install();
                this.RegisterMcpOps();
                McpBridge.Start();
            }
            catch (Exception ex)
            {
                // Never let the bridge take the mod's startup with it.
                ModLogger.Warning("[Mcp] init failed (bridge disabled): " + ex);
            }
        }

        partial void ShutdownMcpBridge()
        {
            try
            {
                McpBridge.Stop();
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] shutdown failed: " + ex.Message);
            }
        }

        // One call per frame from OnUpdate. With no marker this is a single bool test; with the
        // bridge idle it is one more test on an empty queue.
        partial void ProcessMcpOnUpdate()
        {
            if (!McpBridge.Listening)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!this.mcpBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                McpBridge.Drain(Time.frameCount);
                // Sandbox plugin ticks + the unload-collection watchdog. Inside the same breaker as
                // the pump: a plugin host that starts throwing every frame must cool down like any
                // other feature rather than flooding the log.
                this.ProcessMcpPluginsOnUpdate();
                this.mcpBreaker.Success();
            }
            catch (Exception ex)
            {
                this.mcpBreaker.Failure("Mcp", ex, now);
            }
        }

        partial void ProcessMcpOnLateUpdate()
        {
            if (!McpBridge.Listening)
            {
                return;
            }

            try
            {
                McpScreenshot.OnLateUpdateFallback();
            }
            catch (Exception ex)
            {
                ModEntryGuard.Report("Mcp.LateUpdate", ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Registry
        // ────────────────────────────────────────────────────────────────────────────────────────

        private void RegisterMcpOps()
        {
            // Schemas are FULL JSON Schema objects, not shorthand: the bridge hands them to the MCP
            // client as `inputSchema` verbatim, so anything less would have to be translated —
            // and a translation layer is exactly the thing that goes stale when an op changes.
            McpOps.Register(
                "status",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Mod build, loader, pump, world-gate state, live feature summaries. The one op that "
                + "also answers before a world exists.",
                "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                this.HandleMcpStatus);

            McpOps.Register(
                "log.tail",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Last N lines the mod logged this session (ring buffer, newest last). The ring starts "
                + "when the bridge does, so earlier startup lines live only in the loader's log file.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"n\":{\"type\":\"integer\",\"description\":\"how many lines (1-500, default 100)\"},"
                + "\"filter\":{\"type\":\"string\",\"description\":\"case-insensitive substring\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpLogTail);

            McpOps.Register(
                "env",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Where things live on disk: the mod assembly, the loader's interop/core folders and "
                + "the runtime directory. This is what lets an external compiler build code against "
                + "the EXACT assemblies this session loaded, instead of guessing at paths.",
                "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                this.HandleMcpEnv);

            // Phase 2a world reads (McpOps.World.cs).
            this.RegisterMcpWorldOps();

            // Live type reflection (McpOps.Mono.cs) — what makes the AGENTS.md §7 alias ritual
            // unnecessary for new work.
            this.RegisterMcpMonoOps();

            // Phase 2b inventory / quests / events (McpOps.Data.cs). The event log stays off until
            // here, so a build without an agent connected never pays for it.
            this.RegisterMcpDataOps();

            // Phase 4 sandbox plugin host (McpOps.Plugins.cs / PluginHostFeature.cs).
            this.RegisterMcpPluginOps();

            McpEventLogEnabled = true;

            // The bag-change hook that invalidates backpack.list's cache. On the GATE, never from
            // OnUpdate — AGENTS.md §1: resolving/inflating game code before a world exists fails at
            // best and AVs at worst.
            this.RegisterWorldReadyCallback("McpDataEventHooks", this.EnsureMcpDataEventHooks);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Handlers (main thread)
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpStatus(Dictionary<string, object> args)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();

            w.BeginObject("mod");
            w.Str("version", ModBuildVersion.Display);
            w.Str("numeric", ModBuildVersion.Numeric);
            w.Str("loader", ModLoaderInfo.IsMelonLoader ? "MelonLoader" : "BepInEx");
            // Which pump is driving the mod decides what is even possible this session (the
            // injected-MonoBehaviour fallback means ClassInjector already ran) — worth one field.
            w.Str("pump", PlayerLoopProbe.DriveMod ? "playerloop" : "monobehaviour");
            w.Str("pumpStatus", PlayerLoopProbe.Status);
            w.Bool("beta", BetaEnabled);
            w.EndObject();

            w.BeginObject("world");
            w.Bool("ready", this.IsWorldReady);
            w.Bool("loadingScreen", this.IsWorldLoadingScreenVisible);
            w.Bool("dataQueryable", this.IsGameDataQueryable);
            w.Num("epoch", AuraMonoWorldEpoch);
            w.EndObject();

            w.BeginObject("runtime");
            w.Num("frame", Time.frameCount);
            w.Num("unscaledTime", Time.unscaledTime);
            float dt = Time.unscaledDeltaTime;
            w.Num("fps", dt > 0.0001f ? 1f / dt : 0f);
            // FPS Watchdog (FpsWatchdogFeature.cs). The raw `fps` above is one frame and says
            // nothing about stability; these are the smoothed reading, the baseline it is judged
            // against, and how much of the last frame the mod itself cost.
            w.Str("fpsWatchdog", this.BuildFpsWatchdogSummaryText());
            w.Num("modFrameMs", this.fpsWatchdogModMs);
            w.EndObject();

            w.BeginObject("mcp");
            w.Num("port", McpBridge.Port);
            w.Num("ops", McpOps.Count);
            w.Num("opsServed", McpBridge.OpsServed);
            w.Bool("allowWrites", McpBridge.AllowWrites);
            w.Bool("allowUnsafe", McpBridge.AllowUnsafe);
            w.Num("uptimeSec", (DateTime.UtcNow - McpBridge.StartedUtc).TotalSeconds);
            w.EndObject();

            // Feature summaries are best-effort: every entry reads mod state, but a few delegate to
            // static farm controllers, and `status` must stay answerable even if one of those is
            // mid-teardown. A failure here degrades to an empty list, never to a failed op.
            w.BeginArray("features");
            try
            {
                List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        LiveFeatureStatusEntry entry = entries[i];
                        if (entry == null)
                        {
                            continue;
                        }

                        w.BeginArrayObject();
                        w.Str("label", entry.Label);
                        w.Str("summary", entry.Summary);
                        if (entry.Details != null && entry.Details.Count > 0)
                        {
                            w.BeginArray("details");
                            for (int d = 0; d < entry.Details.Count; d++)
                            {
                                LiveFeatureStatusDetail detail = entry.Details[d];
                                if (detail == null)
                                {
                                    continue;
                                }

                                w.BeginArrayObject();
                                w.Str("label", detail.Label);
                                w.Str("value", detail.Value);
                                w.EndObject();
                            }

                            w.EndArray();
                        }

                        w.EndObject();
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] status feature collection failed: " + ex.Message);
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpEnv(Dictionary<string, object> args)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();

            string modPath = null;
            string pluginsDir = null;
            string loaderDir = null;
            try
            {
                // Everything derives from where THIS assembly actually loaded from, so it stays
                // correct for a BepInEx installed outside the game folder (which is this setup).
                modPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(modPath))
                {
                    pluginsDir = System.IO.Path.GetDirectoryName(modPath);
                    loaderDir = System.IO.Path.GetDirectoryName(pluginsDir);
                }
            }
            catch (Exception ex)
            {
                w.Str("resolveError", ex.Message);
            }

            w.Str("modAssembly", modPath);
            w.Str("pluginsDir", pluginsDir);
            w.Str("loaderDir", loaderDir);
            w.Str("interopDir", loaderDir == null ? null : System.IO.Path.Combine(loaderDir, "interop"));
            w.Str("coreDir", loaderDir == null ? null : System.IO.Path.Combine(loaderDir, "core"));

            try
            {
                w.Str("runtimeDir", System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
            }
            catch
            {
            }

            try
            {
                w.Str("baseDir", AppContext.BaseDirectory);
            }
            catch
            {
            }

            w.Str("userDataDir", HelperPaths.Root);
            w.Str("loader", ModLoaderInfo.IsMelonLoader ? "MelonLoader" : "BepInEx");
            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpLogTail(Dictionary<string, object> args)
        {
            int n = McpArgs.GetInt(args, "n", 100);
            if (n < 1)
            {
                n = 1;
            }
            else if (n > 500)
            {
                n = 500;
            }

            string filter = McpArgs.GetString(args, "filter");
            string[] lines = ModLogger.RingSnapshot(n, filter);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("count", lines.Length);
            w.Bool("ringEnabled", ModLogger.RingEnabled);
            w.BeginArray("lines");
            for (int i = 0; i < lines.Length; i++)
            {
                w.ArrayStr(lines[i]);
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }
    }
}
#endif
