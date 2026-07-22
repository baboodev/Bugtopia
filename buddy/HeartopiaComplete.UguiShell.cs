using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 2 navigational chrome (migration plan: cosmic-waddling-rainbow.md).
    // The REAL 9-tab structure of the mod menu as UGUI, built from UguiKit factories: left
    // sidebar (icon + label per main tab, vertical), a single 56px top row holding the sidebar-
    // width logo block ("Bugtopia" + build version) SIDE BY SIDE with the per-tab header
    // title/subtitle (IMGUI parity: logoRect and headerRect share the same y0..56 strip — there
    // is NO generic window title; the kit window is created with a null title and the shell owns
    // the strip, which doubles as the drag region), per-tab sub-tab bar, placeholder content
    // areas. The configurable menu hotkey (keyToggleMenu, default Insert) toggles it — Phase 5
    // retired the IMGUI menu and the F10 dev shortcut with it. Actual tab CONTENT is Phase 3 —
    // this round only proves the navigation shape for the real 9×(0-9) structure.
    //
    // Source of truth mirrored here (do not invent tabs/orders):
    //  - Display order + labels: navLabels in DrawWindow (HeartopiaComplete.UiKit.cs:376).
    //  - Internal selectedTab ids: navIndices (UiKit.cs:377) = {0,2,3,8,4,5,6,9,7} — carried in
    //    UguiShellInternalTabIds so Phase 3 content wiring can map 1:1 onto the IMGUI fields.
    //  - Icon indices: NavIconPngBase64 order (HeartopiaComplete.NavIcons.cs) — display order
    //    0..8 maps 1:1 onto icon index.
    //  - Sub-tab sets: GetActiveTopSubTabs (HeartopiaComplete.cs:2332) — Self 5, Resource
    //    Gathering 4, Features 8, New Features 8, Radar 2, Teleport 9, Bag/Warehouse 0,
    //    Research 0, Settings 5. Localized labels use the same this.L(...) keys IMGUI uses.
    //  - Header/subtitle strings: GetSelectedTabHeader/GetSelectedTabSubtitle (Gui.cs:378/392).
    //
    // Two-level switching design: ONE flat CreateUguiTabBar instance PER main tab, living inside
    // that tab's content container and SetActive-swapped along with it. Chosen over
    // teardown/rebuild-on-switch because: (a) zero Destroy churn, (b) each bar's own ActiveIndex
    // preserves that tab's sub-selection across main-tab switches exactly like IMGUI's per-tab
    // *SubTab fields, (c) it validates that the kit's flat tab bar composes into nested
    // structures by plain instantiation — no new "nested bar" concept needed. The sidebar is
    // vertical, which the horizontal bar factory doesn't do, so it is its own small construction
    // from kit primitives with per-instance state in UguiShellHandle (no singleton fields).
    //
    // Deliberately NOT in this round (separate Phase 2 follow-ups): live theme reload, the
    // input-ownership registry, Status Overlay/Quick Status rail, toast sizing, InputField spike,
    // and the real persisted-scale decision — the shell has NO scale keys yet on purpose
    // (EnableUguiWindowScaleKeys is the PoC's ephemeral test path, not the real feature).
    //
    // Phase 2d addition — persistent LIVE rail: the 240px right column IMGUI paints via
    // DrawQuickStatusPanel (HeartopiaComplete.UiKit.cs:544, called OUTSIDE the tab switch) is now
    // a persistent sibling of the TabContents containers — always visible regardless of
    // ActiveIndex. Only the per-tab BODY placeholders narrow to make room (contentColW); the
    // container (and therefore the sub-tab bar inside it) keeps the full mainW, exactly like
    // IMGUI's full-width subTabRect. Entry rows rebuild only when a cheap signature over
    // CollectLiveFeatureStatusEntries() changes (see ProcessUguiShellLiveRailOnUpdate — never
    // unconditionally per frame); the FPS footer shares the statusOverlay* smoothing fields with
    // the IMGUI overlay/rail. Unlike IMGUI's "+N more" truncation the entry list really scrolls
    // (CreateUguiScrollView) — a deliberate UGUI improvement. Theme reload: the rail is built by
    // BuildUguiShell, so the existing "UguiShell" rebuilder covers it (fresh handle → null
    // signature → repopulated on build).
    //
    // Phase 2e addition — shell scale: the shell tracks the SAME persisted uiScale the IMGUI menu
    // uses (Settings→Main "UI Scale" slider → GetUiScale()), the precedent Phase 2d set for the
    // Status Overlay. During the migration IMGUI tabs and this shell are one "menu" in the user's
    // mental model, so there is deliberately NO second shell-only scale setting or control — the
    // shell only FOLLOWS the shared value. Unlike the Overlay (fixed-position, recomputes its
    // placement from scratch every rebuild — direct canvas.scaleFactor assignment is enough
    // there), the shell is DRAGGABLE, so it must go through SetUguiWindowScale for the position
    // re-clamp a scale change requires. Applied once at build (no 1.0x first-frame pop) and
    // re-synced by ProcessUguiShellScaleOnUpdate, which calls SetUguiWindowScale only when
    // GetUiScale() actually changed — SetUguiWindowScale logs unconditionally, so an every-frame
    // call would spam at 60fps. (EnableUguiWindowScaleKeys remains the PoC's ephemeral test
    // harness; the shell still binds no scale keys.)
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellHandle
        {
            public UguiWindowHandle Window;
            public readonly List<Image> NavRowBgs = new List<Image>();
            public readonly List<GameObject> NavRowLabels = new List<GameObject>();
            public readonly List<Image> NavRowIcons = new List<Image>(); // entry may be null
            public readonly List<GameObject> TabContents = new List<GameObject>();
            public readonly List<UguiTabBarHandle> SubTabBars = new List<UguiTabBarHandle>(); // null = tab has no sub-tabs
            public GameObject HeaderTitle;
            public GameObject HeaderSubtitle;
            public string[] TabHeaders;
            public string[] TabSubtitles;
            public int ActiveIndex = -1;

            // LIVE rail (persistent right column — see file header, Phase 2d).
            public GameObject LiveRailRoot;
            public Transform LiveRailRowsContent; // scroll content the entry rows rebuild into
            public readonly List<GameObject> LiveRailRows = new List<GameObject>();
            public GameObject LiveRailChipGo;     // repositioned per text width like IMGUI's chipRect
            public Image LiveRailChipBg;
            public GameObject LiveRailChipLabel;
            public GameObject LiveRailFpsValue;
            public string LiveRailSignature;      // null = repopulate on next refresh
            public int LiveRailErrorCount;        // per-frame refresh disabled at 3 (DragErrorCount idiom)

            // Phase 2e shell-scale sync: the last RAW GetUiScale() value pushed into
            // SetUguiWindowScale. Compared pre-snap on purpose — see ProcessUguiShellScaleOnUpdate.
            public float LastSyncedUiScale = -1f;
        }

        private UguiShellHandle uguiShell;
        private bool uguiShellBuildFailed;

        // Internal selectedTab id per display position (navIndices) — unused by the placeholder
        // chrome itself, but Phase 3 binds real content per INTERNAL id, so the mapping ships
        // with the shell instead of being rediscovered later.
        private static readonly int[] UguiShellInternalTabIds = new int[] { 0, 2, 3, 8, 4, 5, 6, 9, 7 };

        private const float UguiShellWindowW = 960f;
        private const float UguiShellWindowH = 640f;
        private const float UguiShellSidebarW = 190f;

        // ----------------------------------------------------------------------------------------
        // Entry points (keyToggleMenu — see OnUpdate hotkey block)
        // ----------------------------------------------------------------------------------------

        private void ToggleUguiShell()
        {
            try
            {
                if (this.uguiShell == null)
                {
                    if (this.uguiShellBuildFailed)
                    {
                        return; // already failed once this session; don't retry every keypress
                    }

                    this.BuildUguiShell();
                    if (this.uguiShell == null)
                    {
                        this.uguiShellBuildFailed = true;
                        ModLogger.Msg("[UguiShell] build failed — see errors above");
                        return;
                    }

                    if (EventSystem.current == null)
                    {
                        ModLogger.Msg("[UguiShell] WARNING: no EventSystem in scene — shell will render but not receive clicks");
                    }

                    this.SetUguiWindowVisible(this.uguiShell.Window, true);
                    // Live theme reload: rebuild the shell (state-preserving) when the IMGUI
                    // theme editor changes colors. Registration is idempotent by name.
                    this.RegisterUguiThemeRebuilder("UguiShell", new System.Action(this.RebuildUguiShellForTheme));
                    // Input ownership: the shell is the real-menu replacement, so it carries the
                    // exact blocking weight showMenu does — MODAL (full-screen click block +
                    // movement block). The closure reads the LIVE uguiShell field on every call,
                    // so theme-reload rebuilds (which replace the handle) are picked up; never
                    // capture the handle object itself.
                    this.RegisterInputOwnershipSurface("UguiShell", true,
                        () => this.uguiShell != null && this.IsUguiWindowVisible(this.uguiShell.Window),
                        null);
                    ModLogger.Msg("[UguiShell] shell built and shown (menu hotkey toggles)");
                    return;
                }

                bool show = !this.IsUguiWindowVisible(this.uguiShell.Window);
                this.SetUguiWindowVisible(this.uguiShell.Window, show);
                ModLogger.Msg("[UguiShell] shell " + (show ? "shown" : "hidden"));
            }
            catch (Exception ex)
            {
                this.uguiShellBuildFailed = true;
                ModLogger.Msg("[UguiShell] toggle error: " + ex.Message);
            }
        }

        // Called from OnUpdate every frame — drives window drag (frame driver no-ops while hidden).
        private void ProcessUguiShellOnUpdate()
        {
            if (this.uguiShell == null)
            {
                return;
            }
            this.ProcessUguiWindowFrame(this.uguiShell.Window);
            this.ProcessUguiShellScaleOnUpdate();
            this.ProcessUguiShellLiveRailOnUpdate();
            this.ProcessUguiShellResearchContentOnUpdate();
            this.ProcessUguiShellSettingsMainOnUpdate();
            this.ProcessUguiShellSettingsLoggingOnUpdate();
            this.ProcessUguiShellSettingsKeybindsOnUpdate();
            this.ProcessUguiShellThemeOnUpdate();
            this.ProcessUguiShellTeleportContentOnUpdate();
            this.ProcessUguiShellSelfBuildingOnUpdate();
            this.ProcessUguiShellSelfMainOnUpdate();
            this.ProcessUguiShellSelfFunOnUpdate();
            this.ProcessUguiShellSelfPrivacyOnUpdate();
            this.ProcessUguiShellSelfGameUiOnUpdate();
            this.ProcessUguiShellBagWarehouseOnUpdate();
            this.ProcessUguiShellForagingOnUpdate();
            this.ProcessUguiShellFishingOnUpdate();
            this.ProcessUguiShellInsectsOnUpdate();
            this.ProcessUguiShellBirdsOnUpdate();
            this.ProcessUguiShellNewFeaturesAnimalCareOnUpdate();
            this.ProcessUguiShellNewFeaturesSandSculptureOnUpdate();
            this.ProcessUguiShellNewFeaturesPicturesOnUpdate();
            this.ProcessUguiShellNewFeaturesIceSkatingOnUpdate();
            this.ProcessUguiShellNewFeaturesExtraOnUpdate();
            this.ProcessUguiShellNewFeaturesSeaCleanOnUpdate();
            this.ProcessUguiShellNewFeaturesHomelandFarmOnUpdate();
            this.ProcessUguiShellNewFeaturesDailyQuestsOnUpdate();
            this.ProcessUguiShellFeaturesMainOnUpdate();
            this.ProcessUguiShellFeaturesFoodRepairOnUpdate();
            this.ProcessUguiShellFeaturesSnowSculptingOnUpdate();
            this.ProcessUguiShellFeaturesAutoBuyOnUpdate();
            this.ProcessUguiShellFeaturesAutoSellOnUpdate();
            this.ProcessUguiShellFeaturesMassCookOnUpdate();
            this.ProcessUguiShellFeaturesPuzzleOnUpdate();
            this.ProcessUguiShellFeaturesPetCareOnUpdate();
            this.ProcessUguiShellRadarMainOnUpdate();
            this.ProcessUguiShellRadarSettingsOnUpdate();
        }

        // Phase 2e: live re-sync of the shell scale while the (still-IMGUI) Settings→Main
        // "UI Scale" slider edits the shared persisted uiScale. Reading GetUiScale() every frame
        // is a few clamps — cheap; calling SetUguiWindowScale every frame is NOT (it logs via
        // ModLogger unconditionally, i.e. 60fps log spam), so it only runs on an actual change.
        // The comparison is against the RAW value last pushed (LastSyncedUiScale), NOT against
        // shell.Window.Scale: SetUguiWindowScale snaps to 0.1 steps while GetUiScale() can return
        // off-grid values when fit-capped by the screen (its fitScale min() is unsnapped), so a
        // raw-vs-snapped comparison would re-fire — and re-log — every frame in that state.
        // Deliberately not gated on window visibility: syncing while hidden costs the same and
        // means reopening never shows a stale-scale frame. Screen-size changes re-fire the sync
        // automatically because GetUiScale() folds the screen fit into its result (same idea as
        // the Status Overlay's metricsChanged check).
        private void ProcessUguiShellScaleOnUpdate()
        {
            UguiShellHandle shell = this.uguiShell;
            if (shell == null || shell.Window == null)
            {
                return;
            }
            try
            {
                float target = this.GetUiScale();
                if (!Mathf.Approximately(target, shell.LastSyncedUiScale))
                {
                    shell.LastSyncedUiScale = target;
                    this.SetUguiWindowScale(shell.Window, target);
                }
            }
            catch
            {
                // GetUiScale is pure field math and SetUguiWindowScale logs its own failures
                // internally — nothing useful to add per-frame here.
            }
        }

        // LIVE rail refresh. CollectLiveFeatureStatusEntries() has no dirty signal (dozens of
        // scattered toggle call sites), so a cheap per-frame signature over the entries decides
        // when to rebuild the row GameObjects — never unconditionally (that would be real
        // GameObject churn for no benefit). The FPS footer label updates every frame (single
        // SetText, shared statusOverlay* smoothing fields — never a second smoothing state).
        private void ProcessUguiShellLiveRailOnUpdate()
        {
            UguiShellHandle shell = this.uguiShell;
            if (shell == null || shell.LiveRailRoot == null || shell.LiveRailErrorCount >= 3
                || !this.IsUguiWindowVisible(shell.Window))
            {
                return;
            }

            try
            {
                List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
                string signature = this.BuildLiveFeatureStatusSignature(entries);
                if (!string.Equals(signature, shell.LiveRailSignature, StringComparison.Ordinal))
                {
                    shell.LiveRailSignature = signature;
                    this.RebuildUguiShellLiveRailRows(shell, entries);
                }

                this.TickStatusOverlayFpsShared();
                this.SetUguiLabelText(shell.LiveRailFpsValue, this.GetStatusOverlayFpsDisplayText());
            }
            catch (Exception ex)
            {
                shell.LiveRailErrorCount++;
                ModLogger.Msg("[UguiShell] LIVE rail refresh error (" + shell.LiveRailErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Theme-change rebuild (registered via RegisterUguiThemeRebuilder): destroy + rebuild so
        // every Image/label re-reads the live ui* theme fields, preserving window position/scale/
        // visibility, the selected main tab AND each main tab's own sub-tab selection.
        private void RebuildUguiShellForTheme()
        {
            try
            {
                if (this.uguiShell == null)
                {
                    return; // never built — nothing stale
                }

                UguiWindowRestoreState state = this.CaptureUguiWindowState(this.uguiShell.Window);
                int mainTab = this.uguiShell.ActiveIndex;
                List<int> subSelections = new List<int>();
                for (int i = 0; i < this.uguiShell.SubTabBars.Count; i++)
                {
                    UguiTabBarHandle bar = this.uguiShell.SubTabBars[i];
                    subSelections.Add((bar != null) ? bar.ActiveIndex : 0);
                }

                try
                {
                    if (this.uguiShell.Window != null && this.uguiShell.Window.Root != null)
                    {
                        Object.Destroy(this.uguiShell.Window.Root);
                    }
                }
                catch { }
                this.uguiShell = null;

                this.BuildUguiShell();
                if (this.uguiShell == null)
                {
                    this.uguiShellBuildFailed = true;
                    ModLogger.Msg("[UguiShell] theme rebuild failed — shell not recreated");
                    return;
                }

                for (int i = 0; i < this.uguiShell.SubTabBars.Count && i < subSelections.Count; i++)
                {
                    if (this.uguiShell.SubTabBars[i] != null)
                    {
                        this.SelectUguiTab(this.uguiShell.SubTabBars[i], subSelections[i]);
                    }
                }
                this.SelectUguiShellTab(this.uguiShell, mainTab);
                this.RestoreUguiWindowState(this.uguiShell.Window, state);
                ModLogger.Msg("[UguiShell] rebuilt for theme change");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] theme rebuild error: " + ex.Message);
            }
        }

        // Close button + sidebar footer both route here (IMGUI: both set showMenu = false).
        private void OnUguiShellCloseClicked()
        {
            try
            {
                if (this.uguiShell != null)
                {
                    this.SetUguiWindowVisible(this.uguiShell.Window, false);
                    ModLogger.Msg("[UguiShell] hidden via close/footer click (menu hotkey reopens)");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] close error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------------------------------

        private void BuildUguiShell()
        {
            UguiShellHandle shell = null;
            try
            {
                // Display-order data tables — mirrors navLabels / GetActiveTopSubTabs /
                // GetSelectedTabHeader / GetSelectedTabSubtitle. Nothing below hardcodes a count;
                // every loop runs off these arrays.
                string[] tabLabels = new string[]
                {
                    this.L("Self"), this.L("Resource Gathering"), this.L("Features"),
                    this.L("New Features"), this.L("Radar"), this.L("Teleport"),
                    this.L("Bag / Warehouse"), this.L("Research"), this.L("Settings")
                };
                string[] tabSubtitles = new string[]
                {
                    this.L("Player, camera and input"), this.L("Aura farm and collection"),
                    this.L("Automation and helpers"), this.L("Experiments"),
                    this.L("World scanner"), this.L("Locations and travel"),
                    this.L("Bulk item tools"), this.L("Institute tools"),
                    this.L("Menu, theme and hotkeys")
                };
                string[][] subTabLabels = new string[][]
                {
                    new string[] { "Main", "Building", "Fun", "Privacy", "Game UI" },
                    new string[] { "Foraging", "Fishing", "Insects", "Birds" },
                    new string[] { "Main", "Food & Repair", "Snow Sculpting", "Auto Buy", "Auto Sell", "Mass Cook", "Puzzle", "Pet Care" },
                    new string[] { "Animal Care", "Daily Quests", this.L("homeland_farm.title"), this.L("pictures.title"), "Ice Skating", this.L("extra.title"), this.L("Sand Sculpture"), "Sea Clean" },
                    new string[] { "Main", "Settings" },
                    new string[] { "Home", "Animal Care", "NPCs", "Locations", "Events", "House", "Custom", "XYZ", "Spawn Vehicle" },
                    new string[0], // Bag / Warehouse — no sub-tabs
                    new string[0], // Research — no sub-tabs
                    new string[] { "Main", "Keybinds", "UI Theme", "About", "Logging" }
                };

                shell = new UguiShellHandle();
                shell.TabHeaders = tabLabels;
                shell.TabSubtitles = tabSubtitles;

                // Null title: the shell owns the top strip itself — ONE 56px row (matching
                // IMGUI's logoRect/headerRect, both y0..56) instead of a generic window title
                // stacked above a second per-tab header. The 56px strip is still the drag region.
                shell.Window = this.CreateUguiWindow(
                    "BugtopiaUguiShell",
                    null,
                    null,
                    new Vector2(UguiShellWindowW, UguiShellWindowH),
                    29400, // below the PoC (29500) and the Dropdown-popup ceiling (30000)
                    56f);
                // Shell scale = the shared persisted uiScale (file header, Phase 2e). Applied at
                // build time so the very first visible frame is already at the user's scale
                // instead of defaulting to 1.0x and visibly popping a moment later. The sync
                // cache is seeded with the RAW GetUiScale() value — the per-frame check compares
                // against it, never against the snapped window scale.
                shell.LastSyncedUiScale = this.GetUiScale();
                this.SetUguiWindowScale(shell.Window, shell.LastSyncedUiScale);
                Transform panelT = shell.Window.PanelRt;
                Transform topRow = shell.Window.TitleBarRt;
                float mainX = UguiShellSidebarW;
                float mainW = UguiShellWindowW - UguiShellSidebarW - 32f; // 16px padding both sides
                // 2026-07-22: was 70 (56px top row + a 14px gap, from IMGUI's subTabRect =
                // headerRect.yMax + 12). Reported as too much dead space above the tabs, and it
                // was: the header subtitle ends at y=47, so 70 left 23px of empty band before the
                // sub-tab row. Now flush with the top row's bottom edge — which is also exactly
                // where the sidebar starts, so the tab row's top now lines up with the sidebar's,
                // and the subtitle still keeps 9px of clearance. Everything below (container,
                // per-tab content at +36, LIVE rail at +36) is expressed relative to this, so they
                // all rise together and contentH gains the reclaimed 14px.
                float contentTop = 56f;
                float contentH = UguiShellWindowH - contentTop - 14f;
                // IMGUI body split (UiKit.cs:499-503): the 240px LIVE rail + 14px gap come out of
                // the BODY only — the sub-tab bar keeps the full mainW (container width unchanged).
                float contentColW = mainW - 240f - 14f;

                // Top row, left: compact logo block scoped to the sidebar width — IMGUI parity
                // (DrawWindow: "Bugtopia" bold 15 at +16/+9, "build X" muted 10 at +16/+28).
                GameObject logo = this.CreateUguiLabel(topRow, "Logo", "Bugtopia", 15f, this.UguiKitTextColor(), false);
                this.TrySetUguiLabelBold(logo);
                PlaceUguiTopLeft(logo, 16f, 9f, UguiShellSidebarW - 32f, 20f);
                GameObject logoBuild = this.CreateUguiMutedLabel(topRow, "LogoBuild", "build " + ModBuildVersion.Display, 10f);
                PlaceUguiTopLeft(logoBuild, 16f, 28f, UguiShellSidebarW - 32f, 16f);

                // Top row, right: per-tab header in the SAME row (IMGUI: title bold 17 primary
                // text color at +22/+8, subtitle muted 11 at +22/+31 — primary, NOT accent).
                shell.HeaderTitle = this.CreateUguiLabel(topRow, "HeaderTitle", "", 17f, this.UguiKitTextColor(), false);
                this.TrySetUguiLabelBold(shell.HeaderTitle);
                PlaceUguiTopLeft(shell.HeaderTitle, mainX + 22f, 8f, mainW - 22f, 24f);
                shell.HeaderSubtitle = this.CreateUguiMutedLabel(topRow, "HeaderSubtitle", "", 11f);
                PlaceUguiTopLeft(shell.HeaderSubtitle, mainX + 22f, 31f, mainW - 22f, 16f);

                // Drag region: IMGUI reserves the right ~130px of the top strip for the close
                // button (GUI.DragWindow rect = width - 130). Mirror that by narrowing the kit's
                // TitleBar drag rect, then park the close button in the freed corner — a press on
                // the button can then never double as a drag start.
                shell.Window.TitleBarRt.sizeDelta = new Vector2(UguiShellWindowW - 130f, 56f);

                // Close button — IMGUI parity: closeRect = (xMax - 20 - 30, y + 13, 30, 30),
                // GUI.skin.button look (= the Secondary tier), "×" label, hides the menu.
                GameObject closeBtn = this.CreateUguiSecondaryButton(panelT, "CloseButton", "×",
                    new System.Action(this.OnUguiShellCloseClicked));
                PlaceUguiTopLeft(closeBtn, UguiShellWindowW - 50f, 13f, 30f, 30f);

                // Sidebar column below the top row (plain rect on purpose — a sliced sprite would
                // round all four corners; IMGUI uses a left-rounded-only shape, deferred as cosmetic).
                GameObject sidebar = this.CreateUguiGo("Sidebar", panelT);
                PlaceUguiTopLeft(sidebar, 0f, 56f, UguiShellSidebarW, UguiShellWindowH - 56f);
                Image sidebarBg = this.AddUguiImage(sidebar, this.UguiKitPanelBg(), false, 1f);
                sidebarBg.raycastTarget = true; // sidebar clicks must not leak to the game

                // Sidebar footer — IMGUI parity (DrawWindow: "Baboodev", bold 12, primary text
                // color, centered in the bottom 56px of the sidebar; clicking it hides the menu).
                GameObject footer = this.CreateUguiGo("SidebarFooter", sidebar.transform);
                PlaceUguiTopLeft(footer, 0f, UguiShellWindowH - 56f - 56f, UguiShellSidebarW, 56f);
                Image footerHit = this.AddUguiImage(footer, new Color(0f, 0f, 0f, 0f), false, 1f);
                footerHit.raycastTarget = true;
                Button footerBtn = footer.AddComponent<Button>();
                footerBtn.targetGraphic = footerHit;
                footerBtn.onClick.AddListener(new System.Action(this.OnUguiShellCloseClicked));
                GameObject footerName = this.CreateUguiLabel(footer.transform, "Name", "Baboodev", 12f, this.UguiKitTextColor(), true);
                this.TrySetUguiLabelBold(footerName);
                StretchUguiFill(footerName, 0f, 0f, 0f, 0f);

                // Per-tab: sidebar nav row + content container (+ its own sub-tab bar when the
                // tab has sub-tabs). Each piece guarded so one broken tab can't kill the shell.
                for (int i = 0; i < tabLabels.Length; i++)
                {
                    int tabIndex = i; // capture a copy for the click closure
                    try
                    {
                        // --- Sidebar nav row ---
                        GameObject row = this.CreateUguiGo("Nav_" + tabLabels[i], sidebar.transform);
                        PlaceUguiTopLeft(row, 8f, 10f + i * 40f, UguiShellSidebarW - 16f, 36f);
                        Image rowBg = this.AddUguiImage(row, new Color(0f, 0f, 0f, 0f), true, 1.5f);
                        rowBg.raycastTarget = true;
                        Button rowBtn = row.AddComponent<Button>();
                        rowBtn.targetGraphic = rowBg;

                        Image rowIcon = this.CreateUguiIcon(row.transform, i, 16f, this.UguiKitMutedColor());
                        float labelLeft = 12f;
                        if (rowIcon != null)
                        {
                            RectTransform iconRt = rowIcon.rectTransform;
                            iconRt.anchorMin = new Vector2(0f, 0.5f);
                            iconRt.anchorMax = new Vector2(0f, 0.5f);
                            iconRt.pivot = new Vector2(0f, 0.5f);
                            iconRt.anchoredPosition = new Vector2(10f, 0f);
                            labelLeft = 34f;
                        }
                        GameObject rowLabel = this.CreateUguiLabel(row.transform, "Label", tabLabels[i], 13f, this.UguiKitMutedColor(), false);
                        StretchUguiFill(rowLabel, labelLeft, 2f, 4f, 2f);
                        rowBtn.onClick.AddListener(new System.Action(() => this.SelectUguiShellTab(this.uguiShell, tabIndex)));

                        shell.NavRowBgs.Add(rowBg);
                        shell.NavRowLabels.Add(rowLabel);
                        shell.NavRowIcons.Add(rowIcon);

                        // --- Content container ---
                        GameObject container = this.CreateUguiGo("TabContent_" + tabLabels[i], panelT);
                        PlaceUguiTopLeft(container, mainX + 16f, contentTop, mainW, contentH);
                        shell.TabContents.Add(container);

                        string[] subs = subTabLabels[i];
                        if (subs != null && subs.Length > 0)
                        {
                            GameObject[] subContents = new GameObject[subs.Length];
                            for (int j = 0; j < subs.Length; j++)
                            {
                                // Phase 3 content wiring is by STATIC display-position index
                                // (labels are localized — never match on them). Round 1:
                                // Settings→About; round 2: Settings→Main + Settings→Logging
                                // (HeartopiaComplete.UguiPhase3SettingsContent.cs); round 4:
                                // Self→Building (HeartopiaComplete.UguiBuildingContent.cs);
                                // round 5: Self's other four sub-tabs
                                // (HeartopiaComplete.UguiSelfContent.cs); every other cell stays
                                // a placeholder.
                                if (i == UguiShellSelfTabIndex && j == UguiShellSelfBuildingSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSelfBuildingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSelfTabIndex && j == UguiShellSelfMainSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSelfMainContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSelfTabIndex && j == UguiShellSelfFunSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSelfFunContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSelfTabIndex && j == UguiShellSelfPrivacySubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSelfPrivacyContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSelfTabIndex && j == UguiShellSelfGameUiSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSelfGameUiContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSettingsTabIndex && j == UguiShellSettingsAboutSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellAboutContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSettingsTabIndex && j == UguiShellSettingsMainSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSettingsMainContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSettingsTabIndex && j == UguiShellSettingsLoggingSubIndex)
                                {
                                    subContents[j] = this.BuildUguiShellSettingsLoggingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSettingsTabIndex && j == UguiShellSettingsKeybindsSubIndex)
                                {
                                    // Phase 3 item 9: Settings→Keybinds
                                    // (HeartopiaComplete.UguiKeybindsContent.cs).
                                    subContents[j] = this.BuildUguiShellKeybindsContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellSettingsTabIndex && j == UguiShellSettingsUiThemeSubIndex)
                                {
                                    // Phase 3 item 10: Settings→UI Theme (the HSV picker;
                                    // HeartopiaComplete.UguiThemeContent.cs).
                                    subContents[j] = this.BuildUguiShellThemeContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellTeleportTabIndex)
                                {
                                    // Round 3: all nine Teleport sub-tabs — dispatched by sub
                                    // display index inside HeartopiaComplete.UguiTeleportContent.cs.
                                    subContents[j] = this.BuildUguiShellTeleportSubContent(j,
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellResourceGatheringTabIndex && j == UguiShellForagingSubIndex)
                                {
                                    // Phase 3 item 6, round 1 of 4: Foraging
                                    // (HeartopiaComplete.UguiForagingContent.cs).
                                    subContents[j] = this.BuildUguiShellForagingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellResourceGatheringTabIndex && j == UguiShellFishingSubIndex)
                                {
                                    // Phase 3 item 6, round 2 of 4: Fishing
                                    // (HeartopiaComplete.UguiFishingContent.cs).
                                    subContents[j] = this.BuildUguiShellFishingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellResourceGatheringTabIndex && j == UguiShellInsectsSubIndex)
                                {
                                    // Phase 3 item 6, round 3 of 4: Insects
                                    // (HeartopiaComplete.UguiInsectsContent.cs).
                                    subContents[j] = this.BuildUguiShellInsectsContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellResourceGatheringTabIndex && j == UguiShellBirdsSubIndex)
                                {
                                    // Phase 3 item 6, round 4 of 4: Birds
                                    // (HeartopiaComplete.UguiBirdsContent.cs). Resource
                                    // Gathering's whole sub range (0-3) now has real content —
                                    // the else placeholder below only serves OTHER tabs' subs.
                                    subContents[j] = this.BuildUguiShellBirdsContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellRadarTabIndex && j == UguiShellRadarMainSubIndex)
                                {
                                    // Phase 3 item 7: Radar main sub-tab
                                    // (HeartopiaComplete.UguiRadarContent.cs).
                                    subContents[j] = this.BuildUguiShellRadarMainContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellRadarTabIndex && j == UguiShellRadarSettingsSubIndex)
                                {
                                    // Phase 3 item 7: Radar Settings sub-tab — with it, Radar's
                                    // whole sub range (0-1) has real content.
                                    subContents[j] = this.BuildUguiShellRadarSettingsContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellAnimalCareSubIndex)
                                {
                                    // Phase 3 item 12, round 1 of 8: Animal Care
                                    // (HeartopiaComplete.UguiNewFeaturesContent.cs). New
                                    // Features' remaining subs (1-5, 7) stay on the else
                                    // placeholder until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellNewFeaturesAnimalCareContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellSandSculptureSubIndex)
                                {
                                    // Phase 3 item 12, round 2 of 8: Sand Sculpture
                                    // (HeartopiaComplete.UguiSandSculptureContent.cs).
                                    subContents[j] = this.BuildUguiShellNewFeaturesSandSculptureContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellPicturesSubIndex)
                                {
                                    // Phase 3 item 12, round 4 of 8: Pictures
                                    // (HeartopiaComplete.UguiPicturesContent.cs). New Features'
                                    // remaining subs (1, 2, 4, 5, 7) stay on the else
                                    // placeholder until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellNewFeaturesPicturesContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellIceSkatingSubIndex)
                                {
                                    // Phase 3 item 12, round 5 of 8: Ice Skating
                                    // (HeartopiaComplete.UguiIceSkatingContent.cs). New
                                    // Features' remaining subs (1, 2, 5, 7) stay on the else
                                    // placeholder until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellNewFeaturesIceSkatingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellExtraSubIndex)
                                {
                                    // Phase 3 item 12: Extra
                                    // (HeartopiaComplete.UguiExtraContent.cs). New Features'
                                    // remaining subs (1, 2, 7) stay on the else placeholder
                                    // until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellNewFeaturesExtraContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellSeaCleanSubIndex)
                                {
                                    // Phase 3 item 12, round 7 of 8: Sea Clean
                                    // (HeartopiaComplete.UguiSeaCleanContent.cs). New Features'
                                    // remaining subs (1, 2) stay on the else placeholder until
                                    // their own rounds ship.
                                    subContents[j] = this.BuildUguiShellNewFeaturesSeaCleanContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellHomelandFarmSubIndex)
                                {
                                    // Phase 3 item 12, round 8 of 8: Homeland Farm
                                    // (HeartopiaComplete.UguiHomelandFarmContent.cs). New
                                    // Features' remaining sub (1, Daily Quests) stays on the
                                    // else placeholder until its own round ships.
                                    subContents[j] = this.BuildUguiShellNewFeaturesHomelandFarmContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellNewFeaturesTabIndex && j == UguiShellDailyQuestsSubIndex)
                                {
                                    // Phase 3 item 12, final round: Daily Quests
                                    // (HeartopiaComplete.UguiDailyQuestsContent.cs). New
                                    // Features' whole sub range (0-7) now has real content —
                                    // the else placeholder below only serves OTHER tabs' subs.
                                    subContents[j] = this.BuildUguiShellNewFeaturesDailyQuestsContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesMainSubIndex)
                                {
                                    // Phase 3 item 11, round 1 of 8: Features → Main
                                    // (HeartopiaComplete.UguiFeaturesMainContent.cs). Features'
                                    // remaining subs (1, 3, 4, 5, 7) stay on the else
                                    // placeholder until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellFeaturesMainContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesSnowSculptingSubIndex)
                                {
                                    // Phase 3 item 11, rounds 2+3 of 8: Features → Snow
                                    // Sculpting (HeartopiaComplete.UguiFeaturesPuzzleSnowContent.cs).
                                    subContents[j] = this.BuildUguiShellFeaturesSnowSculptingContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesFoodRepairSubIndex)
                                {
                                    // Phase 3 item 11, round 6 of 8: Features → Food & Repair
                                    // (HeartopiaComplete.UguiFeaturesFoodRepairContent.cs).
                                    // Features' remaining subs (5, 7) stay on the else
                                    // placeholder until their own rounds ship.
                                    subContents[j] = this.BuildUguiShellFeaturesFoodRepairContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesAutoBuySubIndex)
                                {
                                    // Phase 3 item 11, round 4 of 8: Features → Auto Buy
                                    // (HeartopiaComplete.UguiFeaturesAutoBuyContent.cs).
                                    subContents[j] = this.BuildUguiShellFeaturesAutoBuyContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesAutoSellSubIndex)
                                {
                                    // Phase 3 item 11, round 7 of 8: Features → Auto Sell
                                    // (HeartopiaComplete.UguiFeaturesAutoSellContent.cs).
                                    subContents[j] = this.BuildUguiShellFeaturesAutoSellContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesMassCookSubIndex)
                                {
                                    // Phase 3 item 11, round 8 of 8: Features → Mass Cook
                                    // (HeartopiaComplete.UguiFeaturesMassCookContent.cs).
                                    subContents[j] = this.BuildUguiShellFeaturesMassCookContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesPuzzleSubIndex)
                                {
                                    // Phase 3 item 11, rounds 2+3 of 8: Features → Puzzle
                                    // (HeartopiaComplete.UguiFeaturesPuzzleSnowContent.cs).
                                    subContents[j] = this.BuildUguiShellFeaturesPuzzleContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else if (i == UguiShellFeaturesTabIndex && j == UguiShellFeaturesPetCareSubIndex)
                                {
                                    // Phase 3 item 11, FINAL round: Features → Pet Care
                                    // (HeartopiaComplete.UguiFeaturesPetCareContent.cs) —
                                    // Features' last sub; the tab is fully migrated.
                                    subContents[j] = this.BuildUguiShellFeaturesPetCareContent(
                                        container.transform, 0f, 36f, contentColW, contentH - 36f);
                                }
                                else
                                {
                                    subContents[j] = this.BuildUguiShellPlaceholder(
                                        container.transform, tabLabels[i], subs[j],
                                        0f, 36f, contentColW, contentH - 36f);
                                }
                            }
                            // One flat kit tab bar per main tab; width per label like the IMGUI
                            // segmented control (Teleport's 9 labels can't fit uniformly).
                            UguiTabBarHandle subBar = this.CreateUguiTabBar(
                                container.transform, 0f, 0f, 100f, 26f, 4f,
                                subs, null, subContents, 0, null,
                                this.ComputeUguiShellSubTabWidths(subs), 11.5f);
                            shell.SubTabBars.Add(subBar);
                        }
                        else
                        {
                            // Phase 3 round 1: Research (display position 7, internal id 9);
                            // Phase 3 item 5: Bag/Warehouse (display position 6, internal id 6,
                            // HeartopiaComplete.UguiBagWarehouseContent.cs). Index match, not
                            // label match — see the sub-tab branch above.
                            if (i == UguiShellResearchTabIndex)
                            {
                                this.BuildUguiShellResearchContent(container.transform, 0f, 0f, contentColW, contentH);
                            }
                            else if (i == UguiShellBagWarehouseTabIndex)
                            {
                                this.BuildUguiShellBagWarehouseContent(container.transform, 0f, 0f, contentColW, contentH);
                            }
                            else
                            {
                                this.BuildUguiShellPlaceholder(container.transform, tabLabels[i], null, 0f, 0f, contentColW, contentH);
                            }
                            shell.SubTabBars.Add(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Keep list indices aligned with the tab index even on failure.
                        while (shell.NavRowBgs.Count <= i) shell.NavRowBgs.Add(null);
                        while (shell.NavRowLabels.Count <= i) shell.NavRowLabels.Add(null);
                        while (shell.NavRowIcons.Count <= i) shell.NavRowIcons.Add(null);
                        while (shell.TabContents.Count <= i) shell.TabContents.Add(null);
                        while (shell.SubTabBars.Count <= i) shell.SubTabBars.Add(null);
                        ModLogger.Msg("[UguiShell] tab '" + tabLabels[i] + "' build failed: " + ex.Message);
                    }
                }

                // Persistent LIVE rail — built ONCE as a sibling of the TabContents containers
                // (NOT inside the per-tab loop), always visible regardless of ActiveIndex,
                // matching IMGUI calling DrawQuickStatusPanel outside the tab-content switch.
                // Y starts 36px BELOW contentTop, not at it: the per-tab sub-tab bar (a child of
                // `container`, deliberately left at the full mainW so it matches IMGUI's
                // full-width subTabRect — see contentColW above) occupies local y=0..36 at that
                // same height, and IMGUI's own railRect starts at bodyTop (subTabRect.yMax + 14),
                // BELOW the sub-tab row, not beside it. Starting the rail at contentTop instead
                // put it at the SAME height as the sub-tab row, so any tab with enough sub-tabs
                // to render past x=704 (Features' 8, Teleport's 9) visually overlapped the rail's
                // own header/chip — reported live, confirmed by re-deriving IMGUI's bodyTop
                // formula by hand. Guarded: a broken rail must not kill the shell.
                try
                {
                    this.BuildUguiShellLiveRail(shell, panelT, mainX + 16f + contentColW + 14f, contentTop + 36f, 240f, contentH - 36f);
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiShell] LIVE rail build failed: " + ex.Message);
                }

                this.uguiShell = shell;
                this.SelectUguiShellTab(shell, 0);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] BuildUguiShell error: " + ex.Message);
                try
                {
                    if (shell != null && shell.Window != null && shell.Window.Root != null)
                    {
                        Object.Destroy(shell.Window.Root);
                    }
                }
                catch { }
                this.uguiShell = null;
            }
        }

        // Placeholder content block (Phase 3 replaces these with real migrated tab content).
        private GameObject BuildUguiShellPlaceholder(Transform parent, string tabLabel, string subTabLabel,
            float x, float y, float w, float h)
        {
            GameObject block = this.CreateUguiGo("Placeholder_" + (subTabLabel ?? "Main"), parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            string what = string.IsNullOrEmpty(subTabLabel) ? tabLabel : (tabLabel + " / " + subTabLabel);
            GameObject title = this.CreateUguiBodyLabel(block.transform, "What", "Content for: " + what, 14f);
            PlaceUguiTopLeft(title, 16f, 12f, w - 32f, 22f);
            GameObject note = this.CreateUguiMutedLabel(block.transform, "Note", "Placeholder — real content migrates here in Phase 3.", 12f);
            PlaceUguiTopLeft(note, 16f, 38f, w - 32f, 18f);
            return block;
        }

        // Persistent LIVE rail — UGUI mirror of DrawQuickStatusPanel (UiKitPrimitives.cs:1157):
        // "LIVE" overline + active-count chip, scrollable entry list, FPS footer. Static chrome is
        // built here once; chip/rows/FPS are (re)populated by RebuildUguiShellLiveRailRows.
        private void BuildUguiShellLiveRail(UguiShellHandle shell, Transform parent, float x, float y, float w, float h)
        {
            GameObject rail = this.CreateUguiGo("LiveRail", parent);
            PlaceUguiTopLeft(rail, x, y, w, h);
            // IMGUI: DrawExentriSectionPanel = 5.5% white hairline ring + panel fill.
            this.AddUguiImage(rail, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(rail, new Color(1f, 1f, 1f, 0.055f), 1f);

            Color textMuted = this.UguiKitMutedColor();

            // "LIVE" overline (IMGUI: bold 11 muted @ +16/+14).
            GameObject overline = this.CreateUguiLabel(rail.transform, "Overline", this.L("LIVE"), 11f,
                new Color(textMuted.r, textMuted.g, textMuted.b, 0.95f), false);
            this.TrySetUguiLabelBold(overline);
            PlaceUguiTopLeft(overline, 16f, 14f, 100f, 18f);

            // Active-count chip: capsule + centered label; color/width/position applied on refresh
            // (IMGUI resizes chipRect from the text every frame — here only when entries change).
            GameObject chip = this.CreateUguiGo("Chip", rail.transform);
            PlaceUguiTopLeft(chip, w - 14f - 70f, 12f, 70f, 20f);
            shell.LiveRailChipBg = this.AddUguiImage(chip, new Color(1f, 1f, 1f, 0.05f), true, 1f);
            GameObject chipLabel = this.CreateUguiLabel(chip.transform, "Label", "", 10f, textMuted, true);
            StretchUguiFill(chipLabel, 0f, 0f, 0f, 0f);
            shell.LiveRailChipGo = chip;
            shell.LiveRailChipLabel = chipLabel;

            // Entry list: a REAL scroll view instead of IMGUI's "+N more" truncation (deliberate
            // UGUI improvement). The kit scroll view paints its own panel/content backgrounds;
            // the rail wants the flat IMGUI-rail look, so both are made transparent here —
            // raycastTarget stays on (wheel/drag events must still land on the viewport).
            Transform rowsContent;
            GameObject scroll = this.CreateUguiScrollView(rail.transform, "Entries", 10f, out rowsContent);
            PlaceUguiTopLeft(scroll, 8f, 40f, w - 16f, h - 86f); // 40..h-46 (footer starts at h-40)
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (rowsContent != null && rowsContent.parent != null)
                {
                    Image viewportBg = rowsContent.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear; // alpha-0 Image still raycasts (click-blocker idiom)
                    }
                }
            }
            catch { }
            shell.LiveRailRowsContent = rowsContent;

            // Footer: hairline + FPS readout (IMGUI: footer strip at yMax-40).
            GameObject footerLine = this.CreateUguiGo("FooterLine", rail.transform);
            PlaceUguiTopLeft(footerLine, 11f, h - 40f, w - 22f, 1f);
            this.AddUguiImage(footerLine, new Color(1f, 1f, 1f, 0.05f), false, 1f);

            GameObject fpsLabel = this.CreateUguiLabel(rail.transform, "FpsLabel", this.L("FPS"), 10f,
                new Color(textMuted.r, textMuted.g, textMuted.b, 0.95f), false);
            this.TrySetUguiLabelBold(fpsLabel);
            PlaceUguiTopLeft(fpsLabel, 16f, h - 28f, 60f, 16f);

            GameObject fpsValue = this.CreateUguiLabel(rail.transform, "FpsValue",
                this.GetStatusOverlayFpsDisplayText(), 13f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(fpsValue);
            this.TrySetUguiLabelRightAligned(fpsValue);
            PlaceUguiTopLeft(fpsValue, w - 90f, h - 30f, 74f, 18f);
            shell.LiveRailFpsValue = fpsValue;

            shell.LiveRailRoot = rail;

            // Populate immediately (no empty first frame) and seed the signature so the first
            // ProcessUguiShellLiveRailOnUpdate only rebuilds if something actually changed.
            List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
            shell.LiveRailSignature = this.BuildLiveFeatureStatusSignature(entries);
            this.RebuildUguiShellLiveRailRows(shell, entries);
        }

        // Destroys and rebuilds the entry rows + updates the chip. Only called when the entries
        // signature changed (or from the initial build) — never unconditionally per frame.
        private void RebuildUguiShellLiveRailRows(UguiShellHandle shell, List<LiveFeatureStatusEntry> entries)
        {
            Color live = new Color(this.uiSuccessR, this.uiSuccessG, this.uiSuccessB);
            Color textMuted = this.UguiKitMutedColor();

            // Chip — IMGUI strings/colors verbatim (DrawQuickStatusPanel): "{0} active"/"standby",
            // live-tinted capsule when count > 0, faint white + muted text otherwise.
            string chipText = entries.Count > 0 ? this.LF("{0} active", entries.Count) : this.L("standby");
            if (shell.LiveRailChipBg != null)
            {
                shell.LiveRailChipBg.color = entries.Count > 0
                    ? new Color(live.r, live.g, live.b, 0.13f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
            this.SetUguiLabelText(shell.LiveRailChipLabel, chipText);
            this.SetUguiLabelColor(shell.LiveRailChipLabel, entries.Count > 0 ? live : textMuted);
            if (shell.LiveRailChipGo != null)
            {
                // IMGUI glyph-count sizing (CalcSize under-measures in-game — see DrawQuickStatusPanel).
                float chipWidth = Mathf.Min(30f + (chipText.Length * 7.5f), 240f - 110f);
                PlaceUguiTopLeft(shell.LiveRailChipGo, 240f - 14f - chipWidth, 12f, chipWidth, 20f);
            }

            for (int i = 0; i < shell.LiveRailRows.Count; i++)
            {
                GameObject row = shell.LiveRailRows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            shell.LiveRailRows.Clear();

            Transform content = shell.LiveRailRowsContent;
            if (content == null)
            {
                return;
            }

            Color textPrimary = this.UguiKitTextColor();
            Color valueMuted = new Color(textMuted.r, textMuted.g, textMuted.b, 0.98f);
            // Rail 240 → scroll view 224 → viewport insets 4 left + 18 right → content width 202.
            const float rowW = 202f;
            float yCursor = 6f;

            if (entries.Count == 0)
            {
                GameObject none = this.CreateUguiLabel(content, "None", this.L("No active features"), 12f,
                    new Color(textMuted.r, textMuted.g, textMuted.b, 0.6f), false);
                PlaceUguiTopLeft(none, 10f, yCursor + 4f, rowW - 20f, 22f);
                shell.LiveRailRows.Add(none);
                yCursor += 30f;
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    LiveFeatureStatusEntry entry = entries[i];

                    // Static live-colored dot (8px, radius 4 = circle). The IMGUI sine-pulse halo
                    // is deliberately not ported — read-only chrome, not a parity widget.
                    GameObject dot = this.CreateUguiGo("Dot" + i, content);
                    PlaceUguiTopLeft(dot, 10f, yCursor + 6f, 8f, 8f);
                    this.AddUguiImage(dot, live, true, 2.5f);
                    shell.LiveRailRows.Add(dot);

                    GameObject label = this.CreateUguiLabel(content, "Label" + i, this.L(entry.Label), 13f, textPrimary, false);
                    this.TrySetUguiLabelBold(label);
                    PlaceUguiTopLeft(label, 28f, yCursor, rowW - 34f, 18f);
                    shell.LiveRailRows.Add(label);
                    yCursor += 19f;

                    if (!string.IsNullOrWhiteSpace(entry.Summary))
                    {
                        GameObject summary = this.CreateUguiLabel(content, "Summary" + i, this.L(entry.Summary), 11f, valueMuted, false);
                        PlaceUguiTopLeft(summary, 28f, yCursor, rowW - 34f, 16f);
                        shell.LiveRailRows.Add(summary);
                        yCursor += 17f;
                    }

                    List<LiveFeatureStatusDetail> details = entry.Details;
                    if (details != null)
                    {
                        for (int j = 0; j < details.Count; j++)
                        {
                            LiveFeatureStatusDetail detail = details[j];
                            GameObject line = this.CreateUguiLabel(content, "Detail" + i + "_" + j,
                                this.L(detail.Label) + ": " + this.L(detail.Value), 11f, valueMuted, false);
                            PlaceUguiTopLeft(line, 28f, yCursor, rowW - 34f, 16f);
                            shell.LiveRailRows.Add(line);
                            yCursor += 16f;
                        }
                    }

                    yCursor += 9f;
                }
            }

            this.SetUguiScrollContentHeight(content, yCursor + 6f);
        }

        // IMGUI sizes sub-tab buttons by label length (clamp(26 + len*7, 64, 138)); same idea
        // scaled down for the shell's narrower main area.
        private float[] ComputeUguiShellSubTabWidths(string[] labels)
        {
            float[] widths = new float[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int len = string.IsNullOrEmpty(labels[i]) ? 0 : labels[i].Length;
                widths[i] = Mathf.Clamp(22f + len * 6f, 56f, 120f);
            }
            return widths;
        }

        // ----------------------------------------------------------------------------------------
        // Selection
        // ----------------------------------------------------------------------------------------

        private void SelectUguiShellTab(UguiShellHandle shell, int index)
        {
            if (shell == null)
            {
                return;
            }

            try
            {
                if (index == shell.ActiveIndex)
                {
                    return; // re-clicking the active tab is a no-op
                }
                shell.ActiveIndex = index;
                ModLogger.Msg("[UguiShell] main tab -> " + index
                    + " (internal id " + ((index >= 0 && index < UguiShellInternalTabIds.Length) ? UguiShellInternalTabIds[index] : -1) + ")");

                for (int i = 0; i < shell.TabContents.Count; i++)
                {
                    GameObject content = shell.TabContents[i];
                    if (content != null)
                    {
                        content.SetActive(i == index);
                    }
                }

                Color accent = this.UguiKitAccent();
                Color onAccent = this.GetUiTextOnAccent(accent);
                Color muted = this.UguiKitMutedColor();
                Color inactive = new Color(0f, 0f, 0f, 0f); // sidebar rows are flat until active
                for (int i = 0; i < shell.NavRowBgs.Count; i++)
                {
                    bool active = i == index;
                    Image bg = shell.NavRowBgs[i];
                    if (bg != null)
                    {
                        bg.color = active ? accent : inactive;
                    }
                    if (i < shell.NavRowLabels.Count)
                    {
                        this.SetUguiLabelColor(shell.NavRowLabels[i], active ? onAccent : muted);
                    }
                    if (i < shell.NavRowIcons.Count && shell.NavRowIcons[i] != null)
                    {
                        shell.NavRowIcons[i].color = active ? onAccent : muted;
                    }
                }

                if (shell.TabHeaders != null && index >= 0 && index < shell.TabHeaders.Length)
                {
                    this.SetUguiLabelText(shell.HeaderTitle, shell.TabHeaders[index]);
                }
                if (shell.TabSubtitles != null && index >= 0 && index < shell.TabSubtitles.Length)
                {
                    this.SetUguiLabelText(shell.HeaderSubtitle, shell.TabSubtitles[index]);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] tab switch error: " + ex.Message);
            }
        }
    }
}
