using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // Plugins sidebar tab. Pages a plugin adds are sub-tabs next to Installed, including the
    // MCP Bridge page from BugtopiaMcp. The row is omitted while the plugins folder has no dll.
    //
    // A page is a model, not GameObjects: AddPage works before the menu has been opened, and a
    // theme rebuild destroys the shell and renders the model again. Callbacks sit behind a host
    // trampoline that RevokeAll cuts, so a button cannot pin a plugin's load context.
    public partial class HeartopiaComplete
    {
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
                    ModLogger.Warning("[Plugin] plugin click handler threw: " + ex.Message);
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
                    ModLogger.Warning("[Plugin] plugin toggle handler threw: " + ex.Message);
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
                    ModLogger.Warning("[Plugin] plugin slider handler threw: " + ex.Message);
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
                        ModLogger.Warning("[Plugin] plugin '" + this.PluginId + "' page '" + this.Title
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
        // Plugin pages — public entry points (called from PluginHostFeature's HostApi)
        // ----------------------------------------------------------------------------------------

        internal Plugins.IPluginPage AddPluginPage(string pluginId, string title)
        {
            string safeTitle = string.IsNullOrWhiteSpace(title) ? pluginId : title.Trim();
            McpPluginPage page = new McpPluginPage(this, pluginId, safeTitle);
            this.mcpPluginPages.Add(page);

            try
            {
                this.RenderMcpPluginPage(page);
                this.RebuildUguiShellPluginsTabBar(this.uguiShellPlugins?.Bar?.ActiveIndex ?? 0);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] could not show page '" + safeTitle + "' for plugin '"
                                  + pluginId + "': " + ex.Message);
            }

            ModLogger.Msg("[Plugin] plugin '" + pluginId + "' added page '" + safeTitle + "'");
            return page;
        }

        // Called from HostApi.RevokeAll — i.e. on unload AND on a failed load. Cutting the callbacks
        // is the part that matters; destroying the GameObjects is only tidiness.
        internal int RemovePluginPages(string pluginId)
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
                this.RebuildUguiShellPluginsTabBar(0);
                ModLogger.Msg("[Plugin] removed " + removed + " page(s) belonging to plugin '" + pluginId + "'");
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
            this.RebuildUguiShellPluginsTabBar(0);
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
            UguiShellPluginsHandle handle = this.uguiShellPlugins;
            if (handle == null || handle.PagesHost == null || page == null || page.Removed || page.Root != null)
            {
                return; // no shell yet: the model stands, and the Plugins tab renders it when the shell exists
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
                ModLogger.Warning("[Plugin] element render failed on page '" + page.Title + "': " + ex.Message);
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



        private sealed class UguiShellPluginsHandle
        {
            public GameObject BarHost;
            public GameObject PagesHost;
            public GameObject InstalledPage;
            public Transform ListContent;
            public UguiTabBarHandle Bar;
            public int BarGeneration;
            public float BarWidth;
            public float PageW;
            public float PageH;
            public int SeenGeneration = -1;
            public readonly List<GameObject> TickLabels = new List<GameObject>();
            public readonly List<string> TickIds = new List<string>();
            public readonly List<string> TickSuffix = new List<string>();
            public readonly List<Toggle> EnabledToggles = new List<Toggle>();
            public readonly List<string> EnabledIds = new List<string>();
            public int ErrorCount;
        }

        private UguiShellPluginsHandle uguiShellPlugins;

        // Whether the shell that is currently built is showing the Plugins sidebar row. Compared
        // with PluginHost.HasDiskPlugins when the menu opens, so an empty folder does not keep the
        // tab and a dll dropped later does not stay invisible.
        private bool uguiShellPluginsTabIncluded;

        internal void SyncPluginsTabWithFolder()
        {
            if (this.uguiShell == null || !this.IsUguiWindowVisible(this.uguiShell.Window))
            {
                return;
            }

            if (this.uguiShellPluginsTabIncluded == PluginHost.HasDiskPlugins())
            {
                return;
            }

            this.RebuildUguiShellForTheme();
        }

        private bool IsUguiShellPluginsTabActive()
        {
            UguiShellHandle shell = this.uguiShell;
            return shell != null
                && shell.ActiveIndex == UguiShellPluginsTabIndex
                && this.IsUguiWindowVisible(shell.Window);
        }

        internal void PluginToast(string message)
        {
            this.AddMenuNotification(message, Color.white);
        }

        private GameObject BuildUguiShellPluginsContent(Transform parent, float x, float y, float w, float h,
            float barWidth)
        {
            this.uguiShellPlugins = null;

            UguiShellPluginsHandle handle = new UguiShellPluginsHandle();
            handle.BarWidth = barWidth;
            handle.PageW = w;
            handle.PageH = h - 44f;

            GameObject barHost = this.CreateUguiGo("PluginTabBarHost", parent);
            PlaceUguiTopLeft(barHost, x, y, barWidth, 36f);
            handle.BarHost = barHost;

            GameObject pagesHost = this.CreateUguiGo("PluginPages", parent);
            PlaceUguiTopLeft(pagesHost, x, y + 44f, w, handle.PageH);
            handle.PagesHost = pagesHost;

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

            this.uguiShellPlugins = handle;
            handle.InstalledPage = this.BuildPluginInstalledPage(handle);
            for (int i = 0; i < this.mcpPluginPages.Count; i++)
            {
                try
                {
                    this.RenderMcpPluginPage(this.mcpPluginPages[i]);
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[Plugin] page '" + this.mcpPluginPages[i].Title + "' render failed: "
                                      + ex.Message);
                }
            }

            this.RebuildUguiShellPluginsTabBar(0);
            return pagesHost;
        }

        private GameObject BuildPluginInstalledPage(UguiShellPluginsHandle handle)
        {
            float w = handle.PageW;
            float h = handle.PageH;
            GameObject root = this.CreateUguiGo("Installed", handle.PagesHost.transform);
            PlaceUguiTopLeft(root, 0f, 0f, w, h);
            this.AddUguiImage(root, this.UguiKitContentBg(), true, 1f);

            Transform content;
            GameObject scroll = this.CreateUguiScrollView(root.transform, "Rows", 10f, out content);
            PlaceUguiTopLeft(scroll, 8f, 8f, w - 16f, h - 16f);
            this.MakeUguiScrollViewFlat(scroll, content);
            handle.ListContent = content;
            this.RefreshPluginRows(handle);
            root.SetActive(false);
            return root;
        }

        private void RefreshPluginRows(UguiShellPluginsHandle handle)
        {
            if (handle == null || handle.ListContent == null)
            {
                return;
            }

            for (int i = handle.ListContent.childCount - 1; i >= 0; i--)
            {
                GameObject old = handle.ListContent.GetChild(i).gameObject;
                old.SetActive(false);
                Object.Destroy(old);
            }

            handle.TickLabels.Clear();
            handle.TickIds.Clear();
            handle.TickSuffix.Clear();
            handle.EnabledToggles.Clear();
            handle.EnabledIds.Clear();

            float width = handle.PageW - 16f - 22f;
            float y = 8f;
            const float pad = 8f;

            GameObject reload = this.CreateUguiSecondaryButton(handle.ListContent, "Reload", this.L("Reload"),
                () => PluginHost.ReloadFromDisk());
            PlaceUguiTopLeft(reload, pad, y, 140f, 28f);
            y += 36f;

            GameObject note = this.CreateUguiMutedLabel(handle.ListContent, "Folder", PluginHost.FolderPath(), 11f);
            PlaceUguiTopLeft(note, pad, y, width - 12f, 32f);
            y += 36f;

            List<PluginHost.PluginRow> rows = new List<PluginHost.PluginRow>();
            PluginHost.CopyRows(rows);
            if (rows.Count == 0)
            {
                GameObject empty = this.CreateUguiMutedLabel(handle.ListContent, "Empty",
                    this.L("No plugins. Drop a dll into the folder above and press Reload."), 12f);
                PlaceUguiTopLeft(empty, pad, y, width - 12f, 36f);
                y += 40f;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                PluginHost.PluginRow row = rows[i];
                GameObject title = this.CreateUguiBodyLabel(handle.ListContent, "Id", row.Id, 13f);
                PlaceUguiTopLeft(title, pad, y, width * 0.45f, 20f);

                string detail = row.Loaded ? row.Ticks + " ticks · " + row.Detail : row.Detail;
                GameObject line = this.CreateUguiMutedLabel(handle.ListContent, "Detail", detail, 11f);
                PlaceUguiTopLeft(line, pad, y + 20f, width - 12f, 18f);
                if (row.Loaded)
                {
                    handle.TickLabels.Add(line);
                    handle.TickIds.Add(row.Id);
                    handle.TickSuffix.Add(row.Detail ?? string.Empty);
                }

                float buttonX = width - 150f;
                if (row.HasDll)
                {
                    string id = row.Id;
                    Toggle toggle = this.CreateUguiCheckbox(handle.ListContent, "Enabled", this.L("Enabled"),
                        row.Enabled, value => PluginHost.SetDiskEnabled(id, value));
                    PlaceUguiTopLeft(toggle.gameObject, buttonX, y, 140f, 24f);
                    handle.EnabledToggles.Add(toggle);
                    handle.EnabledIds.Add(id);
                }

                if (row.Quarantined && !row.Loaded)
                {
                    string id = row.Id;
                    GameObject force = this.CreateUguiSecondaryButton(handle.ListContent, "Force", this.L("Load anyway"),
                        () =>
                        {
                            if (!PluginHost.TryForceLoad(id, out string error) && !string.IsNullOrEmpty(error))
                            {
                                ModLogger.Warning("[Plugin] '" + id + "' force load failed: " + error);
                            }
                        });
                    PlaceUguiTopLeft(force, buttonX, y + 26f, 140f, 26f);
                    y += 28f;
                }

                y += 46f;
            }

            this.SetUguiScrollContentHeight(handle.ListContent, y + 8f);
            handle.SeenGeneration = PluginHost.Generation;
        }

        private void RebuildUguiShellPluginsTabBar(int preferredIndex)
        {
            UguiShellPluginsHandle handle = this.uguiShellPlugins;
            if (handle == null || handle.BarHost == null)
            {
                return;
            }

            try
            {
                for (int i = handle.BarHost.transform.childCount - 1; i >= 0; i--)
                {
                    GameObject old = handle.BarHost.transform.GetChild(i).gameObject;
                    old.SetActive(false);
                    Object.Destroy(old);
                }

                List<string> labels = new List<string>();
                List<GameObject> contents = new List<GameObject>();
                labels.Add(this.L("Installed"));
                contents.Add(handle.InstalledPage);
                for (int i = 0; i < this.mcpPluginPages.Count; i++)
                {
                    McpPluginPage page = this.mcpPluginPages[i];
                    if (page.Root == null)
                    {
                        continue;
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
                ModLogger.Warning("[Plugin] sub-tab bar rebuild failed: " + ex.Message);
            }
        }

        private void ProcessUguiShellPluginsOnUpdate()
        {
            UguiShellPluginsHandle handle = this.uguiShellPlugins;
            if (handle == null || handle.ErrorCount >= 3 || !this.IsUguiShellPluginsTabActive())
            {
                return;
            }

            try
            {
                bool rebuilt = handle.SeenGeneration != PluginHost.Generation;
                if (rebuilt)
                {
                    this.RefreshPluginRows(handle);
                }

                // Installed is sub-tab 0. Same checkmark repair as Settings: after this page is
                // shown again, Image.OnEnable paints the mark opaque. SyncUguiToggleFromField
                // forces the mark's GameObject active state from the real .enabled file.
                if (handle.Bar != null && handle.Bar.ActiveIndex == 0)
                {
                    for (int i = 0; i < handle.EnabledToggles.Count && i < handle.EnabledIds.Count; i++)
                    {
                        this.SyncUguiToggleFromField(handle.EnabledToggles[i],
                            PluginHost.IsDiskEnabled(handle.EnabledIds[i]));
                    }
                }

                if (rebuilt || (Time.frameCount % 15) != 0)
                {
                    return;
                }

                for (int i = 0; i < handle.TickIds.Count && i < handle.TickLabels.Count && i < handle.TickSuffix.Count; i++)
                {
                    if (!PluginHost.TryGetTicks(handle.TickIds[i], out long ticks, out string error))
                    {
                        continue;
                    }

                    string text = ticks + " ticks · " + handle.TickSuffix[i];
                    if (!string.IsNullOrEmpty(error))
                    {
                        text += " · " + error;
                    }

                    this.SetUguiLabelText(handle.TickLabels[i], text);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Warning("[Plugin] list refresh failed (" + handle.ErrorCount + "/3): " + ex.Message);
            }
        }
    }
}
