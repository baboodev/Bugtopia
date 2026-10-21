#if FEATURE_MCP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // AGENT TAB — the sidebar tab that only exists while the MCP bridge is listening.
    //
    // Two things live here, and they are the same two things for the same reason: they are only
    // meaningful while an agent is attached, so they do not belong in Settings where they would be
    // permanent clutter for everyone else.
    //
    //   "Bridge" — the write / unsafe privilege toggles (moved out of Settings → Logging) plus a
    //   live status line. The marker file authorises the CHANNEL; these authorise what it may DO.
    //   Plugin pages render on the Plugins tab (HeartopiaComplete.PluginMenu.cs).
    //
    // ── WHY THIS TAB BUILDS ITS OWN SUB-TAB BAR ──────────────────────────────────────────────────
    // Bridge is the only page. It is not declared in BuildUguiShell's static sub-tab array: the bar
    // lives in its own host so a theme rebuild, which destroys the window, can throw the buttons
    // away with it and build them again. CreateUguiTabBar parents buttons straight into whatever
    // Transform it is handed, so the host is what separates one generation from the next.
    //
    // Rebuild order matters: the OLD bar generation is deactivated BEFORE it is destroyed, because
    // Object.Destroy is deferred to the end of the frame — leave it active and two generations of
    // buttons overlap, both clickable, for one frame.
    //
    // ── WHY A PLUGIN PAGE IS A MODEL, NOT GAMEOBJECTS ────────────────────────────────────────────
    // A plugin can add a page before the menu has ever been opened (the shell is built lazily on the
    // first hotkey press), and RebuildUguiShellForTheme destroys the entire window and builds it
    // again. If a page were just the GameObjects, the first case would need an "open the menu first"
    // rule and the second would silently delete every plugin's UI on a colour change. Storing what
    // was asked for and rendering it whenever a shell exists costs one indirection and removes both.
    //
    // ── WHY EVERY CALLBACK GOES THROUGH A TRAMPOLINE ─────────────────────────────────────────────
    // A plugin's Action captures the plugin instance. Hand it to a Button and the host (via Unity)
    // holds a reference INTO the load context — the exact direction PluginContract.cs's header calls
    // out as the thing that makes unload impossible. So the Button gets a delegate over a HOST
    // object whose field holds the plugin's delegate, and unload nulls that field. After that the
    // chain is broken no matter how long Unity takes to finish destroying the button, and no matter
    // what il2cpp still has cached. Same shape as McpEventBroker, for the same reason.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Retained model
        // ----------------------------------------------------------------------------------------

        // Plugin pages live in HeartopiaComplete.PluginMenu.cs (the Plugins sidebar tab).

        // ----------------------------------------------------------------------------------------
        // Tab state
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellMcpHandle
        {
            public GameObject BarHost;    // one child per bar generation
            public GameObject PagesHost;  // parent of every page root, Bridge included
            public GameObject BridgePage;
            public UguiTabBarHandle Bar;
            public int BarGeneration;
            public float BarWidth;
            public float PageW;
            public float PageH;

            // Bridge page live bits.
            public GameObject StatusValue;
            public GameObject NoteLabel;
            public string NoteText;
            public float NoteTop;
            public float NoteWidth;
            public bool NoteMeasured; // false until TMP has really laid the paragraph out
            public Toggle WritesToggle;
            public Toggle UnsafeToggle;
            public bool Syncing;          // true while pushing state INTO the toggles
            public int ErrorCount;        // per-frame refresh disabled at 3 (LIVE rail idiom)
        }

        private UguiShellMcpHandle uguiShellMcp;

        // ----------------------------------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------------------------------

        // `barWidth` is the FULL main-column width, not the narrowed body width: the sub-tab row
        // spans past the LIVE rail on every other tab (BuildUguiShell's contentColW comment) and
        // this one must not be the odd one out.
        private GameObject BuildUguiShellMcpContent(Transform parent, float x, float y, float w, float h,
            float barWidth)
        {
            this.uguiShellMcp = null;

            UguiShellMcpHandle handle = new UguiShellMcpHandle();
            handle.BarWidth = barWidth;
            handle.PageW = w;
            handle.PageH = h - 44f;

            GameObject barHost = this.CreateUguiGo("McpTabBarHost", parent);
            PlaceUguiTopLeft(barHost, x, y, barWidth, 36f);
            handle.BarHost = barHost;

            GameObject pagesHost = this.CreateUguiGo("McpPages", parent);
            PlaceUguiTopLeft(pagesHost, x, y + 44f, w, handle.PageH);
            handle.PagesHost = pagesHost;

            handle.BridgePage = this.BuildUguiShellMcpBridgePage(handle, pagesHost.transform);

            this.uguiShellMcp = handle;
            this.RebuildUguiShellMcpTabBar(0);
            return handle.BridgePage;
        }

        // "Bridge" — status plus the two privilege toggles. This page is always sub-tab 0.
        private GameObject BuildUguiShellMcpBridgePage(UguiShellMcpHandle handle, Transform parent)
        {
            float w = handle.PageW;
            float h = handle.PageH;

            GameObject page = this.CreateUguiGo("BridgePage", parent);
            PlaceUguiTopLeft(page, 0f, 0f, w, h);
            this.AddUguiImage(page, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            float innerW = w - pad * 2f;

            GameObject title = this.CreateUguiHeaderLabel(page.transform, "Title", this.L("Agent Bridge"), 18f);
            PlaceUguiTopLeft(title, pad, 12f, innerW, 26f);

            handle.StatusValue = this.CreateUguiMutedLabel(page.transform, "Status", string.Empty, 12f);
            this.TrySetUguiLabelWrapped(handle.StatusValue);
            PlaceUguiTopLeft(handle.StatusValue, pad, 40f, innerW, 34f);

            float rowY = 80f;

            // The explanation that used to sit in Settings → Logging, kept verbatim in spirit: what
            // each privilege actually unlocks, and that neither survives a restart.
            //
            // The height is ESTIMATED here and corrected once the page is really on screen. TMP
            // cannot measure a label whose sub-tab has never been shown — it answers ~2px, which is
            // exactly the trap MeasureUguiPicturesWrappedHeight now rejects — so asking it at build
            // time would only ever yield the fallback anyway. Estimating gives a fallback that at
            // least scales with the translated string instead of being one hardcoded number.
            string note = this.L("Writes let the agent change game state. Unsafe additionally allows raw invokes and hot-loaded plugins, which can crash the game — and it implies writes, so the two move together. Session only: never saved, always off again after a restart.");
            handle.NoteText = note;
            handle.NoteTop = rowY;
            handle.NoteWidth = innerW;
            handle.NoteLabel = this.CreateUguiMutedLabel(page.transform, "Note", note, 12f);
            this.TrySetUguiLabelWrapped(handle.NoteLabel);
            float noteH = EstimateMcpNoteHeight(note, innerW);
            PlaceUguiTopLeft(handle.NoteLabel, pad, rowY, innerW, noteH);

            handle.WritesToggle = this.CreateUguiCheckbox(page.transform, "McpAllowWrites",
                this.L("Allow write ops"), McpBridge.AllowWrites,
                v => this.OnMcpAllowWritesChanged(v));

            handle.UnsafeToggle = this.CreateUguiCheckbox(page.transform, "McpAllowUnsafe",
                this.L("Allow unsafe ops (plugins, raw invokes)"), McpBridge.AllowUnsafe,
                v => this.OnMcpAllowUnsafeChanged(v));

            this.LayoutUguiShellMcpBridgeRows(handle, noteH);

            page.SetActive(false); // SelectUguiTab lights the initial page
            return page;
        }

        // The note's height is the only unknown on this page, so everything under it is placed from
        // one number — which is what makes the later re-measure a two-line correction rather than a
        // rebuild.
        private void LayoutUguiShellMcpBridgeRows(UguiShellMcpHandle handle, float noteHeight)
        {
            const float pad = 16f;
            float rowY = handle.NoteTop + noteHeight + 8f;
            if (handle.WritesToggle != null)
            {
                PlaceUguiTopLeft(handle.WritesToggle.gameObject, pad, rowY, handle.NoteWidth, 24f);
            }
            rowY += 28f;
            if (handle.UnsafeToggle != null)
            {
                PlaceUguiTopLeft(handle.UnsafeToggle.gameObject, pad, rowY, handle.NoteWidth, 24f);
            }
        }

        // Runs from the per-frame tick, which only reaches here while the Bridge page is the visible
        // one — i.e. exactly when TMP has laid the paragraph out and can answer honestly. One
        // correction per shell: `ok` is what says the measurement really happened.
        private void ReviseUguiShellMcpNoteHeight(UguiShellMcpHandle handle)
        {
            if (handle == null || handle.NoteMeasured || handle.NoteLabel == null)
            {
                return;
            }

            float measured = this.MeasureUguiPicturesWrappedHeight(handle.NoteLabel, handle.NoteText,
                handle.NoteWidth, -1f, out bool ok);
            if (!ok)
            {
                return; // still not laid out — try again next frame, keep the estimate meanwhile
            }

            handle.NoteMeasured = true;
            PlaceUguiTopLeft(handle.NoteLabel, 16f, handle.NoteTop, handle.NoteWidth, measured);
            this.LayoutUguiShellMcpBridgeRows(handle, measured);
        }

        // The two toggles are a LADDER, not independent switches: the op gate lets a Write op through
        // when either flag is set (McpBridgeFeature.cs), so a UI that showed writes off while unsafe
        // was on would be lying about what the bridge accepts. Turning unsafe on raises writes;
        // turning writes off drops unsafe with it.
        private void OnMcpAllowWritesChanged(bool value)
        {
            UguiShellMcpHandle handle = this.uguiShellMcp;
            if (handle != null && handle.Syncing)
            {
                return; // we are writing the toggle, not the user
            }
            McpBridge.SetPrivileges(value, value && McpBridge.AllowUnsafe);
            this.SyncUguiShellMcpToggles(handle);
        }

        private void OnMcpAllowUnsafeChanged(bool value)
        {
            UguiShellMcpHandle handle = this.uguiShellMcp;
            if (handle != null && handle.Syncing)
            {
                return;
            }
            McpBridge.SetPrivileges(value || McpBridge.AllowWrites, value);
            this.SyncUguiShellMcpToggles(handle);
        }

        // Pushes bridge state into the checkboxes without letting their own callbacks fire back —
        // SetPrivileges logs unconditionally, so an echo here would be a log line per frame.
        private void SyncUguiShellMcpToggles(UguiShellMcpHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            bool previous = handle.Syncing;
            handle.Syncing = true;
            try
            {
                if (handle.WritesToggle != null && handle.WritesToggle.isOn != McpBridge.AllowWrites)
                {
                    handle.WritesToggle.isOn = McpBridge.AllowWrites;
                }
                if (handle.UnsafeToggle != null && handle.UnsafeToggle.isOn != McpBridge.AllowUnsafe)
                {
                    handle.UnsafeToggle.isOn = McpBridge.AllowUnsafe;
                }
            }
            finally
            {
                handle.Syncing = previous;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Sub-tab bar (rebuilt whenever the set of pages changes)
        // ----------------------------------------------------------------------------------------

        private void RebuildUguiShellMcpTabBar(int preferredIndex)
        {
            UguiShellMcpHandle handle = this.uguiShellMcp;
            if (handle == null || handle.BarHost == null)
            {
                return;
            }

            try
            {
                // Deactivate the whole previous generation before destroying it — Destroy lands at
                // the end of the frame and an active leftover would overlap (and out-click) the new
                // bar until then.
                for (int i = handle.BarHost.transform.childCount - 1; i >= 0; i--)
                {
                    GameObject old = handle.BarHost.transform.GetChild(i).gameObject;
                    old.SetActive(false);
                    Object.Destroy(old);
                }

                List<string> labels = new List<string>();
                List<GameObject> contents = new List<GameObject>();
                labels.Add(this.L("Bridge"));
                contents.Add(handle.BridgePage);

                int index = Mathf.Clamp(preferredIndex, 0, labels.Count - 1);

                GameObject generation = this.CreateUguiGo("Bar" + (++handle.BarGeneration), handle.BarHost.transform);
                PlaceUguiTopLeft(generation, 0f, 0f, handle.BarWidth, 36f);

                string[] labelArray = labels.ToArray();
                handle.Bar = this.CreateUguiTabBar(generation.transform, 0f, 0f, 100f, 34f, 4f,
                    labelArray, null, contents.ToArray(), index, null,
                    this.ComputeUguiShellSubTabWidths(labelArray, handle.BarWidth, 4f), 11.5f);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[AgentTab] sub-tab bar rebuild failed: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame
        // ----------------------------------------------------------------------------------------

        // Only the Bridge page has anything live, and only while it is the visible page — the status
        // line is a string build and the toggle sync is two comparisons, but neither is worth doing
        // for a tab nobody is looking at.
        partial void ProcessUguiShellMcpOnUpdate()
        {
            UguiShellMcpHandle handle = this.uguiShellMcp;
            UguiShellHandle shell = this.uguiShell;
            if (handle == null || shell == null || handle.ErrorCount >= 3
                || shell.ActiveIndex != UguiShellMcpTabIndex
                || !this.IsUguiWindowVisible(shell.Window))
            {
                return;
            }

            if (handle.Bar != null && handle.Bar.ActiveIndex != 0)
            {
                return; // Bridge is not the selected inner tab; nothing here is live
            }

            try
            {
                this.ReviseUguiShellMcpNoteHeight(handle);
                this.SyncUguiShellMcpToggles(handle);
                this.SetUguiLabelText(handle.StatusValue, this.BuildMcpBridgeStatusText());
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[AgentTab] status refresh error (" + handle.ErrorCount
                              + "/3, disabled at 3): " + ex.Message);
            }
        }

        private string BuildMcpBridgeStatusText()
        {
            if (!McpBridge.Listening)
            {
                return this.L("Not listening") + " — " + McpBridge.Status;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
            sb.Append("127.0.0.1:").Append(McpBridge.Port);
            sb.Append("  ·  ").Append(McpBridge.OpsServed).Append(' ').Append(this.L("ops served"));

            int plugins = PluginHost.LoadedCount;
            if (plugins > 0)
            {
                sb.Append("  ·  ").Append(plugins).Append(' ').Append(this.L("plugin(s) loaded"));
            }

            int pages = this.mcpPluginPages.Count;
            if (pages > 0)
            {
                sb.Append("  ·  ").Append(pages).Append(' ').Append(this.L("plugin page(s)"));
            }

            return sb.ToString();
        }
    }
}
#endif
