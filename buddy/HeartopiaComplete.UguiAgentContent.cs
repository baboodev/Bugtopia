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
    //   1. "Bridge" — the write / unsafe privilege toggles (moved out of Settings → Logging) plus a
    //      live status line. The marker file authorises the CHANNEL; these authorise what it may DO.
    //   2. One sub-tab per SANDBOX PLUGIN PAGE — controls a hot-loaded plugin put there itself, gone
    //      the moment that plugin unloads.
    //
    // ── WHY THIS TAB BUILDS ITS OWN SUB-TAB BAR ──────────────────────────────────────────────────
    // Every other tab declares its sub-tabs as a literal array in BuildUguiShell and gets one flat
    // kit tab bar built once. That cannot work here: the set of pages changes at runtime, several
    // times per session, as plugins come and go. So this tab registers with NO sub-tabs (the shell's
    // no-subs branch) and owns a bar it can rebuild — parented into its own host GameObject, since
    // CreateUguiTabBar parents the buttons straight into whatever Transform it is handed and there
    // would otherwise be no way to tell one generation's buttons from the next.
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

        private enum McpPageElementKind
        {
            Label = 0,
            Note = 1,
            Button = 2,
            Toggle = 3,
            Slider = 4,
        }

        // The cut-able link between a live control and plugin code. Nothing else in this file may
        // hold a plugin delegate directly.
        private sealed class McpPageCallback
        {
            internal Action Click;
            internal Action<bool> Bool;
            internal Action<float> Float;

            internal void InvokeClick()
            {
                Action handler = this.Click;
                if (handler == null)
                {
                    return; // revoked — the control outlived its plugin by a frame
                }
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[AgentTab] plugin click handler threw: " + ex.Message);
                }
            }

            internal void InvokeBool(bool value)
            {
                Action<bool> handler = this.Bool;
                if (handler == null)
                {
                    return;
                }
                try
                {
                    handler(value);
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[AgentTab] plugin toggle handler threw: " + ex.Message);
                }
            }

            internal void InvokeFloat(float value)
            {
                Action<float> handler = this.Float;
                if (handler == null)
                {
                    return;
                }
                try
                {
                    handler(value);
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[AgentTab] plugin slider handler threw: " + ex.Message);
                }
            }

            internal void Revoke()
            {
                this.Click = null;
                this.Bool = null;
                this.Float = null;
            }
        }

        private sealed class McpPageElement
        {
            public McpPageElementKind Kind;
            public string Text;
            public bool BoolValue;
            public float Min;
            public float Max;
            public float Value;
            public bool WholeNumbers;
            public McpPageCallback Callback;
            public GameObject Go;    // the live view; null whenever no shell exists
            public GameObject Label; // slider rows keep their caption separately
        }

        // Handed to the plugin so it can retitle a line later. Holds the element, which is a HOST
        // object — the plugin holding the host is the safe direction.
        private sealed class McpPageLabelHandle : Plugins.IPluginLabel
        {
            private readonly HeartopiaComplete mod;
            private readonly McpPageElement element;

            internal McpPageLabelHandle(HeartopiaComplete mod, McpPageElement element)
            {
                this.mod = mod;
                this.element = element;
            }

            public void SetText(string text)
            {
                if (this.element == null)
                {
                    return;
                }
                this.element.Text = text ?? string.Empty;
                if (this.mod != null && this.element.Go != null)
                {
                    this.mod.SetUguiLabelText(this.element.Go, this.element.Text);
                }
            }
        }

        // A page cap, because "add a row" is exactly the kind of call that ends up in Tick() by
        // accident. Hitting it logs once and stops growing instead of quietly eating the frame rate.
        private const int McpPluginPageElementCap = 150;

        private sealed class McpPluginPage : Plugins.IPluginPage
        {
            private readonly HeartopiaComplete mod;
            internal readonly string PluginId;
            internal string Title;
            internal readonly List<McpPageElement> Elements = new List<McpPageElement>();
            internal GameObject Root;
            internal Transform Content;
            internal float Cursor;
            internal float RowWidth;
            internal bool Removed;
            private bool capReported;

            internal McpPluginPage(HeartopiaComplete mod, string pluginId, string title)
            {
                this.mod = mod;
                this.PluginId = pluginId;
                this.Title = title;
            }

            public int ElementCount => this.Elements.Count;

            public Plugins.IPluginLabel AddLabel(string text)
            {
                return this.Add(new McpPageElement
                {
                    Kind = McpPageElementKind.Label,
                    Text = text ?? string.Empty,
                });
            }

            public Plugins.IPluginLabel AddNote(string text)
            {
                return this.Add(new McpPageElement
                {
                    Kind = McpPageElementKind.Note,
                    Text = text ?? string.Empty,
                });
            }

            public void AddButton(string text, Action onClick)
            {
                this.Add(new McpPageElement
                {
                    Kind = McpPageElementKind.Button,
                    Text = text ?? string.Empty,
                    Callback = new McpPageCallback { Click = onClick },
                });
            }

            public void AddToggle(string text, bool initial, Action<bool> onChanged)
            {
                this.Add(new McpPageElement
                {
                    Kind = McpPageElementKind.Toggle,
                    Text = text ?? string.Empty,
                    BoolValue = initial,
                    Callback = new McpPageCallback { Bool = onChanged },
                });
            }

            public void AddSlider(string label, float min, float max, float initial, bool wholeNumbers,
                                  Action<float> onChanged)
            {
                this.Add(new McpPageElement
                {
                    Kind = McpPageElementKind.Slider,
                    Text = label ?? string.Empty,
                    Min = min,
                    Max = max,
                    Value = Mathf.Clamp(initial, Mathf.Min(min, max), Mathf.Max(min, max)),
                    WholeNumbers = wholeNumbers,
                    Callback = new McpPageCallback { Float = onChanged },
                });
            }

            public void Clear()
            {
                if (this.mod == null)
                {
                    return;
                }
                this.mod.ClearMcpPluginPage(this);
            }

            public void Remove()
            {
                if (this.mod == null)
                {
                    return;
                }
                this.mod.RemoveMcpPluginPage(this);
            }

            private Plugins.IPluginLabel Add(McpPageElement element)
            {
                if (this.Removed)
                {
                    return new McpPageLabelHandle(null, element); // inert, but never null
                }

                if (this.Elements.Count >= McpPluginPageElementCap)
                {
                    if (!this.capReported)
                    {
                        this.capReported = true;
                        ModLogger.Warning("[AgentTab] plugin '" + this.PluginId + "' page '" + this.Title
                                          + "' hit the " + McpPluginPageElementCap
                                          + "-element cap — further elements are ignored. Adding rows"
                                          + " from Tick()? Use IPluginLabel.SetText to update in place.");
                    }
                    return new McpPageLabelHandle(null, element);
                }

                this.Elements.Add(element);
                if (this.mod != null)
                {
                    this.mod.RenderMcpPageElement(this, element);
                }
                return new McpPageLabelHandle(this.mod, element);
            }
        }

        // Every page, in sub-tab order, across all plugins. Host-owned, so it is also what unload
        // walks to cut the trampolines.
        private readonly List<McpPluginPage> mcpPluginPages = new List<McpPluginPage>();

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

            // The shell is being (re)built, so every page's previous view is gone with it. Drop the
            // dangling references BEFORE rendering, or SetUguiLabelText would be writing into
            // destroyed objects.
            for (int i = 0; i < this.mcpPluginPages.Count; i++)
            {
                McpPluginPage page = this.mcpPluginPages[i];
                page.Root = null;
                page.Content = null;
                for (int j = 0; j < page.Elements.Count; j++)
                {
                    page.Elements[j].Go = null;
                    page.Elements[j].Label = null;
                }
            }

            this.uguiShellMcp = handle;

            for (int i = 0; i < this.mcpPluginPages.Count; i++)
            {
                try
                {
                    this.RenderMcpPluginPage(this.mcpPluginPages[i]);
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[AgentTab] page '" + this.mcpPluginPages[i].Title + "' render failed: "
                                      + ex.Message);
                }
            }

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
                for (int i = 0; i < this.mcpPluginPages.Count; i++)
                {
                    McpPluginPage page = this.mcpPluginPages[i];
                    if (page.Root == null)
                    {
                        continue; // never rendered (shell built later) — nothing to switch to
                    }
                    labels.Add(page.Title);
                    contents.Add(page.Root);
                }

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
        // Plugin pages — public entry points (called from PluginHostFeature's HostApi)
        // ----------------------------------------------------------------------------------------

        internal Plugins.IPluginPage AddMcpPluginPage(string pluginId, string title)
        {
            string safeTitle = string.IsNullOrWhiteSpace(title) ? pluginId : title.Trim();
            McpPluginPage page = new McpPluginPage(this, pluginId, safeTitle);
            this.mcpPluginPages.Add(page);

            try
            {
                this.RenderMcpPluginPage(page);
                this.RebuildUguiShellMcpTabBar(this.uguiShellMcp?.Bar?.ActiveIndex ?? 0);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[AgentTab] could not show page '" + safeTitle + "' for plugin '"
                                  + pluginId + "': " + ex.Message);
            }

            ModLogger.Msg("[AgentTab] plugin '" + pluginId + "' added page '" + safeTitle + "'");
            return page;
        }

        // Called from HostApi.RevokeAll — i.e. on unload AND on a failed load. Cutting the callbacks
        // is the part that matters; destroying the GameObjects is only tidiness.
        internal int RemoveMcpPluginPages(string pluginId)
        {
            int removed = 0;
            for (int i = this.mcpPluginPages.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(this.mcpPluginPages[i].PluginId, pluginId, StringComparison.Ordinal))
                {
                    continue;
                }
                this.DestroyMcpPluginPage(this.mcpPluginPages[i]);
                this.mcpPluginPages.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
            {
                this.RebuildUguiShellMcpTabBar(0);
                ModLogger.Msg("[AgentTab] removed " + removed + " page(s) belonging to plugin '" + pluginId + "'");
            }
            return removed;
        }

        private void RemoveMcpPluginPage(McpPluginPage page)
        {
            if (page == null || page.Removed)
            {
                return;
            }
            this.mcpPluginPages.Remove(page);
            this.DestroyMcpPluginPage(page);
            this.RebuildUguiShellMcpTabBar(0);
        }

        private void DestroyMcpPluginPage(McpPluginPage page)
        {
            page.Removed = true;
            for (int i = 0; i < page.Elements.Count; i++)
            {
                page.Elements[i].Callback?.Revoke();
                page.Elements[i].Go = null;
                page.Elements[i].Label = null;
            }
            page.Elements.Clear();

            try
            {
                if (page.Root != null)
                {
                    page.Root.SetActive(false);
                    Object.Destroy(page.Root);
                }
            }
            catch
            {
                // The shell may already be gone (theme rebuild, shutdown) — the model is what counts.
            }
            page.Root = null;
            page.Content = null;
        }

        private void ClearMcpPluginPage(McpPluginPage page)
        {
            if (page == null || page.Removed)
            {
                return;
            }

            for (int i = 0; i < page.Elements.Count; i++)
            {
                McpPageElement element = page.Elements[i];
                element.Callback?.Revoke();
                try
                {
                    if (element.Go != null)
                    {
                        element.Go.SetActive(false);
                        Object.Destroy(element.Go);
                    }
                    if (element.Label != null)
                    {
                        element.Label.SetActive(false);
                        Object.Destroy(element.Label);
                    }
                }
                catch
                {
                }
                element.Go = null;
                element.Label = null;
            }

            page.Elements.Clear();
            page.Cursor = 6f;
            if (page.Content != null)
            {
                this.SetUguiScrollContentHeight(page.Content, 12f);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Plugin pages — rendering
        // ----------------------------------------------------------------------------------------

        private void RenderMcpPluginPage(McpPluginPage page)
        {
            UguiShellMcpHandle handle = this.uguiShellMcp;
            if (handle == null || handle.PagesHost == null || page == null || page.Removed || page.Root != null)
            {
                return; // no shell yet: the model stands, BuildUguiShellMcpContent renders it later
            }

            float w = handle.PageW;
            float h = handle.PageH;

            GameObject root = this.CreateUguiGo("PluginPage_" + page.PluginId, handle.PagesHost.transform);
            PlaceUguiTopLeft(root, 0f, 0f, w, h);
            this.AddUguiImage(root, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            GameObject title = this.CreateUguiHeaderLabel(root.transform, "Title", page.Title, 18f);
            PlaceUguiTopLeft(title, pad, 12f, w - pad * 2f, 26f);

            GameObject owner = this.CreateUguiMutedLabel(root.transform, "Owner",
                this.L("Sandbox plugin") + ": " + page.PluginId, 12f);
            PlaceUguiTopLeft(owner, pad, 40f, w - pad * 2f, 18f);

            Transform content;
            GameObject scroll = this.CreateUguiScrollView(root.transform, "Rows", 12f, out content);
            PlaceUguiTopLeft(scroll, 8f, 64f, w - 16f, h - 72f);
            this.MakeUguiScrollViewFlat(scroll, content);

            page.Root = root;
            page.Content = content;
            // Scroll view 4px left inset + 18px right (scrollbar) — the LIVE rail's arithmetic.
            page.RowWidth = w - 16f - 22f;
            page.Cursor = 6f;
            root.SetActive(false);

            for (int i = 0; i < page.Elements.Count; i++)
            {
                this.RenderMcpPageElement(page, page.Elements[i]);
            }
        }

        // Appends ONE element at the page's running cursor. Called both from the initial render loop
        // and from a live AddX — same path either way, so a row added at frame 900 lands exactly
        // where it would have landed at build time.
        private void RenderMcpPageElement(McpPluginPage page, McpPageElement element)
        {
            if (page == null || element == null || page.Content == null || element.Go != null)
            {
                return;
            }

            try
            {
                float w = page.RowWidth;
                float y = page.Cursor;
                Transform parent = page.Content;

                switch (element.Kind)
                {
                    case McpPageElementKind.Note:
                    {
                        float noteH = EstimateMcpNoteHeight(element.Text, w);
                        element.Go = this.CreateUguiMutedLabel(parent, "Note", element.Text, 12f);
                        this.TrySetUguiLabelWrapped(element.Go);
                        PlaceUguiTopLeft(element.Go, 10f, y, w - 12f, noteH);
                        page.Cursor = y + noteH + 6f;
                        break;
                    }

                    case McpPageElementKind.Button:
                    {
                        McpPageCallback callback = element.Callback;
                        element.Go = this.CreateUguiSecondaryButton(parent, "Button", element.Text,
                            new Action(callback.InvokeClick));
                        PlaceUguiTopLeft(element.Go, 10f, y, Mathf.Min(240f, w - 12f), 28f);
                        page.Cursor = y + 34f;
                        break;
                    }

                    case McpPageElementKind.Toggle:
                    {
                        McpPageCallback callback = element.Callback;
                        Toggle toggle = this.CreateUguiCheckbox(parent, "Toggle", element.Text,
                            element.BoolValue, new Action<bool>(callback.InvokeBool));
                        element.Go = toggle.gameObject;
                        PlaceUguiTopLeft(element.Go, 10f, y, w - 12f, 24f);
                        page.Cursor = y + 30f;
                        break;
                    }

                    case McpPageElementKind.Slider:
                    {
                        McpPageCallback callback = element.Callback;
                        element.Label = this.CreateUguiBodyLabel(parent, "SliderLabel", element.Text, 13f);
                        PlaceUguiTopLeft(element.Label, 10f, y, w - 12f, 18f);
                        Slider slider = this.CreateUguiSlider(parent, "Slider", element.Min, element.Max,
                            element.Value, element.WholeNumbers, new Action<float>(callback.InvokeFloat));
                        element.Go = slider.gameObject;
                        PlaceUguiTopLeft(element.Go, 10f, y + 20f, w - 12f, 20f);
                        page.Cursor = y + 46f;
                        break;
                    }

                    default:
                    {
                        element.Go = this.CreateUguiBodyLabel(parent, "Label", element.Text, 13f);
                        PlaceUguiTopLeft(element.Go, 10f, y, w - 12f, 20f);
                        page.Cursor = y + 24f;
                        break;
                    }
                }

                this.SetUguiScrollContentHeight(page.Content, page.Cursor + 8f);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[AgentTab] element render failed on page '" + page.Title + "': " + ex.Message);
            }
        }

        // Wrapped-note height without asking TMP. MeasureUguiPicturesWrappedHeight is the accurate
        // path but needs the label to have Awoken, and a page can be built while its tab has never
        // been shown — so this estimates instead of returning a wrong small number.
        private static float EstimateMcpNoteHeight(string text, float width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 16f;
            }
            float charsPerLine = Mathf.Max(12f, (width - 12f) / 6.2f);
            int lines = Mathf.CeilToInt(text.Length / charsPerLine);
            return Mathf.Clamp(lines * 16f, 16f, 160f);
        }

        // Flat look over the page's own background — alpha-0 images still raycast, so the wheel and
        // drag scrolling keep working (Settings → Logging uses the same trick).
        private void MakeUguiScrollViewFlat(GameObject scroll, Transform content)
        {
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (content != null && content.parent != null)
                {
                    Image viewportBg = content.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch
            {
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
                return; // a plugin page is showing; nothing here is live
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
