using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT (migration plan: cosmic-waddling-rainbow.md, round 1).
    // First real content migrated into the Phase 2 shell: Settings→About (static labels) and the
    // Research tab (live instrument list + actions). Deliberately the simplest tabs, to prove the
    // shell-to-content wiring pattern before the harder ones. Lives in its own file so the shell
    // file stays pure chrome/navigation — 11 more tabs of content are coming and must not all
    // accumulate there.
    //
    // Ground rules for this round (from the plan):
    //  - The IMGUI drawers (DrawAboutTab, DrawResearchTab) stay fully functional and untouched;
    //    this file only READS the same data fields and CALLS the same action methods. Two
    //    independent rendering paths over one backend.
    //  - Wiring into BuildUguiShell is by STATIC display-position index, never by comparing
    //    localized label strings (labels vary by language; indices don't). The constants below are
    //    re-derived from UguiShellInternalTabIds = {0,2,3,8,4,5,6,9,7} + the tabLabels order:
    //    display position 7 carries internal id 9 = IMGUI selectedTab 9 = Research (no sub-tabs);
    //    display position 8 carries internal id 7 = Settings, whose sub array
    //    {"Main","Keybinds","UI Theme","About","Logging"} has About at sub index 3.
    //  - Research refresh is NOT the LIVE rail's per-frame raw-signature diff: a "researching" row
    //    displays a countdown interpolated from Time.unscaledTime (same as IMGUI), so a raw-data
    //    signature would look frozen between 5s server polls. Instead the refresh is throttled to
    //    ~1/sec (nextStatusOverlayFpsRefreshAt idiom) and each tick recomputes the FULL per-
    //    instrument display (label + status text + color + busy) and diffs a signature built from
    //    those COMPUTED values — catching structural changes, status transitions AND countdown
    //    ticks, while a genuinely idle list rebuilds nothing. ResearchFormatRemaining is minute-
    //    granular, so even an active countdown rebuilds rows ~1/min, not 1/sec.
    //  - The bottom static section (2 shortcut buttons + footer hint) is built once but
    //    REPOSITIONED on every row rebuild — its Y depends on the row count, exactly like the
    //    IMGUI drawer's y-cursor accumulation.
    //  - This content lives inside the already-registered modal shell: no input-ownership entries,
    //    no theme registration of its own (the shell's "UguiShell" rebuilder rebuilds the whole
    //    window, which re-runs these builders with fresh theme colors).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Display-position wiring constants — see file header for the re-derivation. These match
        // positions in BuildUguiShell's tabLabels/subTabLabels arrays, NOT internal selectedTab ids.
        private const int UguiShellResearchTabIndex = 7;        // Research (internal id 9)
        private const int UguiShellSettingsTabIndex = 8;        // Settings (internal id 7)
        private const int UguiShellSettingsMainSubIndex = 0;    // "Main" within Settings' subs (round 2)
        private const int UguiShellSettingsKeybindsSubIndex = 1; // "Keybinds" within Settings' subs — matches
                                                                 // settingsSubTab == 1 (HeartopiaComplete.cs:2424;
                                                                 // item 9, HeartopiaComplete.UguiKeybindsContent.cs)
        private const int UguiShellSettingsUiThemeSubIndex = 2; // "UI Theme" within Settings' subs — matches
                                                                // settingsSubTab == 2 (HeartopiaComplete.cs:2425;
                                                                // item 10, HeartopiaComplete.UguiThemeContent.cs)
        private const int UguiShellSettingsAboutSubIndex = 3;   // "About" within Settings' subs
        private const int UguiShellSettingsLoggingSubIndex = 4; // "Logging" within Settings' subs (round 2)

        // Self→Building + Building Move Panel (round 4, HeartopiaComplete.UguiBuildingContent.cs):
        // display position 0 carries internal id 0 (UguiShellInternalTabIds[0]) = IMGUI
        // selectedTab 0 = Self, whose sub array {"Main","Building","Fun","Privacy","Game UI"}
        // has Building at sub index 1 — matching selfSubTab == 1 → DrawBuildingTab
        // (HeartopiaComplete.Gui.cs:1709 / GetActiveTopSubTabs, HeartopiaComplete.cs:2359).
        private const int UguiShellSelfTabIndex = 0;                 // Self (internal id 0)
        private const int UguiShellSelfBuildingSubIndex = 1;         // "Building" within Self's subs

        // Self's four remaining sub-tabs (round 5, HeartopiaComplete.UguiSelfContent.cs): the
        // same sub array's display indices match selfSubTab's own values exactly — Main = 0 →
        // DrawSelfTab's selfSubTab == 0 branch, Fun = 2 → DrawSelfFunTab, Privacy = 3 →
        // DrawPrivacyBlockExtraTab, Game UI = 4 → DrawSelfGameUiTab (HeartopiaComplete.Gui.cs:
        // 1584/1714/1719/1724).
        private const int UguiShellSelfMainSubIndex = 0;             // "Main" within Self's subs
        private const int UguiShellSelfFunSubIndex = 2;              // "Fun" within Self's subs
        private const int UguiShellSelfPrivacySubIndex = 3;          // "Privacy" within Self's subs
        private const int UguiShellSelfGameUiSubIndex = 4;           // "Game UI" within Self's subs

        // Bag/Warehouse (Phase 3 item 5, HeartopiaComplete.UguiBagWarehouseContent.cs): display
        // position 6 carries internal id 6 (UguiShellInternalTabIds[6]) = IMGUI selectedTab 6 =
        // DrawBulkSelectorTab (HeartopiaComplete.Gui.cs:1305). No sub-tabs (subTabLabels[6] is
        // empty) — wired in BuildUguiShell's no-subs branch, same shape as Research.
        private const int UguiShellBagWarehouseTabIndex = 6;         // Bag / Warehouse (internal id 6)

        // Teleport (round 3, HeartopiaComplete.UguiTeleportContent.cs): display position 5
        // carries internal id 5 = IMGUI selectedTab 5, and its nine sub-tab display indices
        // match teleportSubTab's own 0-8 exactly (SetTeleportSubTab, HeartopiaComplete.Teleport.cs).
        private const int UguiShellTeleportTabIndex = 5;             // Teleport (internal id 5)
        private const int UguiShellTeleportHomeSubIndex = 0;         // "Home"
        private const int UguiShellTeleportAnimalCareSubIndex = 1;   // "Animal Care"
        private const int UguiShellTeleportNpcsSubIndex = 2;         // "NPCs"
        private const int UguiShellTeleportLocationsSubIndex = 3;    // "Locations"
        private const int UguiShellTeleportEventsSubIndex = 4;       // "Events"
        private const int UguiShellTeleportHouseSubIndex = 5;        // "House"
        private const int UguiShellTeleportCustomSubIndex = 6;       // "Custom"
        private const int UguiShellTeleportXyzSubIndex = 7;          // "XYZ"
        private const int UguiShellTeleportSpawnVehicleSubIndex = 8; // "Spawn Vehicle"

        // Resource Gathering (Phase 3 item 6 — split into four rounds, ALL SHIPPED; round 1 =
        // Foraging, HeartopiaComplete.UguiForagingContent.cs; round 2 = Fishing,
        // HeartopiaComplete.UguiFishingContent.cs; round 3 = Insects,
        // HeartopiaComplete.UguiInsectsContent.cs; round 4 = Birds,
        // HeartopiaComplete.UguiBirdsContent.cs): display position 1 carries internal id 2
        // (UguiShellInternalTabIds[1]) = IMGUI selectedTab 2 = DrawAutoFarmTab, whose sub array
        // {"Foraging","Fishing","Insects","Birds"} display indices match autoFarmSubTab's own
        // 0-3 exactly (SetAutoFarmSubTab, HeartopiaComplete.Farm.cs:112); Foraging = 0 →
        // DrawAutoFarmTab's default branch (Farm.cs:137); Fishing = 1 → the
        // AutoFishingFarm.DrawSection + FishingRouteFeature.DrawSection flow; Insects = 2 →
        // InsectNetFarm.DrawSection (Farm.cs:128-131); Birds = 3 → BirdNetFarm.DrawSection
        // (Farm.cs:132-135) — with it, this tab's sub range is fully covered.
        private const int UguiShellResourceGatheringTabIndex = 1;    // Resource Gathering (internal id 2)
        private const int UguiShellForagingSubIndex = 0;             // "Foraging" within its subs
        private const int UguiShellFishingSubIndex = 1;              // "Fishing" within its subs
        private const int UguiShellInsectsSubIndex = 2;              // "Insects" within its subs
        private const int UguiShellBirdsSubIndex = 3;                // "Birds" within its subs

        // Radar (Phase 3 item 7, HeartopiaComplete.UguiRadarContent.cs): display position 4
        // carries internal id 4 (UguiShellInternalTabIds[4]) = IMGUI selectedTab 4 = DrawRadarTab
        // (HeartopiaComplete.UiKit.cs:531), and its sub array {"Main","Settings"} display indices
        // match radarSubTab's own 0/1 exactly (HeartopiaComplete.cs:2402-2403; DrawRadarTab
        // dispatches radarSubTab == 1 → DrawRadarSettingsTab, default → the main radar tab).
        private const int UguiShellRadarTabIndex = 4;                // Radar (internal id 4)
        private const int UguiShellRadarMainSubIndex = 0;            // "Main" within Radar's subs
        private const int UguiShellRadarSettingsSubIndex = 1;        // "Settings" within Radar's subs

        // New Features (Phase 3 item 12 — split into rounds like Resource Gathering; round 1 =
        // Animal Care, HeartopiaComplete.UguiNewFeaturesContent.cs): display position 3 carries
        // internal id 8 (UguiShellInternalTabIds[3] — the array literal is {0,2,3,8,4,5,6,9,7},
        // UguiShell.cs) = IMGUI selectedTab 8 = DrawNewFeaturesTab, whose sub array
        // {"Animal Care","Daily Quests",homeland_farm.title,pictures.title,"Ice Skating",
        // extra.title,"Sand Sculpture","Sea Clean"} display indices match newFeaturesSubTab's
        // own 0-7 exactly (GetActiveTopSubTabs, HeartopiaComplete.cs:2387-2399); Animal Care =
        // 0 → DrawAnimalCareTab (AnimalCareFeature.cs:21-25 dispatcher, :387 drawer); round 2 =
        // Sand Sculpture = 6 → DrawSandSculptureTab (AnimalCareFeature.cs:59-62 dispatcher,
        // SandSculptureFeature.cs:2586 drawer; HeartopiaComplete.UguiSandSculptureContent.cs);
        // round 4 = Pictures = 3 → DrawPicturesTab (AnimalCareFeature.cs:44-46 dispatcher,
        // PicturesDecryptFeature.cs:75 drawer; HeartopiaComplete.UguiPicturesContent.cs);
        // round 5 = Ice Skating = 4 → DrawIceSkatingExtrasTab (AnimalCareFeature.cs:49-52
        // dispatcher, IceSkatingSequenceFeature.cs:100 drawer chaining into DrawExtraTab,
        // AutoIceSkatingFeature.cs:4041; HeartopiaComplete.UguiIceSkatingContent.cs); Extra =
        // 5 → DrawExtraFeaturesTab (AnimalCareFeature.cs:54-57 dispatcher, :72 drawer chaining
        // DrawCarpetStampSection + DrawSanrioGachaSection;
        // HeartopiaComplete.UguiExtraContent.cs); round 7 = Sea Clean = 7 → DrawSeaCleanQteTab
        // (AnimalCareFeature.cs:64-66 dispatcher, SeaCleanQteFeature.cs:891 drawer;
        // HeartopiaComplete.UguiSeaCleanContent.cs).
        // Round 8 = Homeland Farm = 2 → DrawHomelandFarmTab (AnimalCareFeature.cs:39-42
        // dispatcher, HomelandFarmFeature.cs:22216 drawer;
        // HeartopiaComplete.UguiHomelandFarmContent.cs).
        // Final round = Daily Quests = 1 → the newFeaturesSubTab == 1 chain
        // (AnimalCareFeature.cs:28-37 dispatcher: DrawDailyQuestSubmitControls →
        // DrawDailyClaimsControls → DrawBirdPhotoSubmitControls → DrawQuestAssistantTab;
        // HeartopiaComplete.UguiDailyQuestsContent.cs) — with it the whole sub range (0-7)
        // has real content.
        private const int UguiShellNewFeaturesTabIndex = 3;          // New Features (internal id 8)
        private const int UguiShellAnimalCareSubIndex = 0;           // "Animal Care" within its subs
        private const int UguiShellDailyQuestsSubIndex = 1;          // "Daily Quests" within its subs
        private const int UguiShellSandSculptureSubIndex = 6;        // "Sand Sculpture" within its subs
        private const int UguiShellPicturesSubIndex = 3;             // "Pictures" within its subs
        private const int UguiShellIceSkatingSubIndex = 4;           // "Ice Skating" within its subs
        private const int UguiShellExtraSubIndex = 5;                // "Extra" within its subs
        private const int UguiShellSeaCleanSubIndex = 7;             // "Sea Clean" within its subs
        private const int UguiShellHomelandFarmSubIndex = 2;         // "Homeland Farm" within its subs

        // Features (Phase 3 item 11 — split into rounds like Resource Gathering; round 1 = Main,
        // HeartopiaComplete.UguiFeaturesMainContent.cs): display position 2 carries internal id 3
        // (UguiShellInternalTabIds[2] — the array literal is {0,2,3,8,4,5,6,9,7}, UguiShell.cs) =
        // IMGUI selectedTab 3 = DrawAutomationTab, whose sub array {"Main","Food & Repair",
        // "Snow Sculpting","Auto Buy","Auto Sell","Mass Cook","Puzzle","Pet Care"} display
        // indices match automationSubTab's own 0-7 exactly (GetActiveTopSubTabs,
        // HeartopiaComplete.cs:2376-2386); Main = 0 → DrawAutomationTab's automationSubTab == 0
        // branch (HeartopiaComplete.Gui.cs:438). Rounds 2+3 = Snow Sculpting = 2 → the inline
        // automationSubTab == 2 branch (Gui.cs:1029-1078) and Puzzle = 6 → DrawPuzzleTab
        // (Gui.cs:1291-1293 dispatcher, PuzzleNetFeature.cs:57 drawer) — both in
        // HeartopiaComplete.UguiFeaturesPuzzleSnowContent.cs. Round 4 = Auto Buy = 3 → the
        // inline automationSubTab == 3 branch (Gui.cs:1080-1279,
        // HeartopiaComplete.UguiFeaturesAutoBuyContent.cs). Round 6 = Food & Repair = 1 → the
        // inline automationSubTab == 1 branch (Gui.cs:584-1027,
        // HeartopiaComplete.UguiFeaturesFoodRepairContent.cs). Round 7 = Auto Sell = 4 →
        // DrawAutoSellTab (Gui.cs:1281-1284 dispatcher, HeartopiaComplete.AutoSell.cs:51-404
        // drawer; HeartopiaComplete.UguiFeaturesAutoSellContent.cs). Round 8 = Mass Cook = 5
        // → DrawMassCookTab (Gui.cs:1286-1289 dispatcher, HeartopiaComplete.NetCook.cs:34-404
        // drawer; HeartopiaComplete.UguiFeaturesMassCookContent.cs). FINAL round = Pet Care = 7
        // → DrawPetPlayTab (Gui.cs:1296-1299 dispatcher, PetPlayFeature.cs:223-627 drawer +
        // PetFeedFeature.cs:4503-4571 favorite-foods table;
        // HeartopiaComplete.UguiFeaturesPetCareContent.cs) — Features fully migrated.
        private const int UguiShellFeaturesTabIndex = 2;             // Features (internal id 3)
        private const int UguiShellFeaturesMainSubIndex = 0;         // "Main" within its subs
        private const int UguiShellFeaturesFoodRepairSubIndex = 1;   // "Food & Repair" within its subs
        private const int UguiShellFeaturesSnowSculptingSubIndex = 2; // "Snow Sculpting" within its subs
        private const int UguiShellFeaturesAutoBuySubIndex = 3;      // "Auto Buy" within its subs
        private const int UguiShellFeaturesAutoSellSubIndex = 4;     // "Auto Sell" within its subs
        private const int UguiShellFeaturesMassCookSubIndex = 5;     // "Mass Cook" within its subs
        private const int UguiShellFeaturesPuzzleSubIndex = 6;       // "Puzzle" within its subs
        private const int UguiShellFeaturesPetCareSubIndex = 7;      // "Pet Care" within its subs

        // ----------------------------------------------------------------------------------------
        // Settings → About (static content — built once, no per-frame logic at all)
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAboutTab (HeartopiaComplete.Config.cs): title, intro line, four
        // heading+body pairs, version line. Text copied verbatim (the IMGUI drawer does not
        // localize About, so neither does this). Role mapping: title/headings = header labels
        // (IMGUI headings use the uiHeader color), intro/bodies/version = muted labels (IMGUI
        // bodyStyle uses the subTabText color). Y advances mirror the IMGUI drawer's cursor.
        private GameObject BuildUguiShellAboutContent(Transform parent, float x, float y, float w, float h)
        {
            GameObject block = this.CreateUguiGo("AboutContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            float innerW = w - pad * 2f;
            float yCur = 16f;

            GameObject title = this.CreateUguiHeaderLabel(block.transform, "Title", "Bugtopia", 18f);
            PlaceUguiTopLeft(title, pad, yCur, innerW, 28f);
            yCur += 30f;

            GameObject intro = this.CreateUguiMutedLabel(block.transform, "Intro",
                "Automation and utility mod for Heartopia.", 12f);
            this.TrySetUguiLabelWrapped(intro);
            PlaceUguiTopLeft(intro, pad, yCur, innerW, 40f);
            yCur += 44f;

            GameObject h1 = this.CreateUguiHeaderLabel(block.transform, "WhatHeading", "What it does", 13f);
            PlaceUguiTopLeft(h1, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b1 = this.CreateUguiMutedLabel(block.transform, "WhatBody",
                "Farming, gathering, teleport, radar, bag tools, and other QoL helpers — from one in-game menu. Press Insert to open it.",
                12f);
            this.TrySetUguiLabelWrapped(b1);
            PlaceUguiTopLeft(b1, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h2 = this.CreateUguiHeaderLabel(block.transform, "OpenHeading", "Open & free", 13f);
            PlaceUguiTopLeft(h2, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b2 = this.CreateUguiMutedLabel(block.transform, "OpenBody",
                "Bugtopia will always stay open-source and free for everyone.", 12f);
            this.TrySetUguiLabelWrapped(b2);
            PlaceUguiTopLeft(b2, pad, yCur, innerW, 40f);
            yCur += 46f;

            GameObject h3 = this.CreateUguiHeaderLabel(block.transform, "CreditsHeading", "Credits", 13f);
            PlaceUguiTopLeft(h3, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b3 = this.CreateUguiMutedLabel(block.transform, "CreditsBody",
                "Based on Heartopia Helper by Rayyy2.\nThank you to everyone who shares ideas for new features.",
                12f);
            this.TrySetUguiLabelWrapped(b3);
            PlaceUguiTopLeft(b3, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h4 = this.CreateUguiHeaderLabel(block.transform, "DisclaimerHeading", "Disclaimer", 13f);
            PlaceUguiTopLeft(h4, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b4 = this.CreateUguiMutedLabel(block.transform, "DisclaimerBody",
                "For educational and research use only. Use at your own risk; you are responsible for any account actions taken by the game operator.",
                12f);
            this.TrySetUguiLabelWrapped(b4);
            PlaceUguiTopLeft(b4, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject version = this.CreateUguiMutedLabel(block.transform, "Version",
                $"Version {ModBuildVersion.Display} · bugtopia.dll", 12f);
            PlaceUguiTopLeft(version, pad, yCur, innerW, 20f);

            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Research tab (live content — throttled computed-signature refresh)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellResearchContentHandle
        {
            public GameObject Root;
            public GameObject StatusLabel;          // "Status: {researchDiagStatus}" — set every tick
            public Transform RowsRoot;              // instrument rows are rebuilt under this
            public readonly List<GameObject> Rows = new List<GameObject>();
            public GameObject StoreButton;          // static — repositioned per row count
            public GameObject ConsoleButton;        // static — repositioned per row count
            public GameObject FooterLabel;          // static — repositioned per row count
            public float BlockWidth;
            public float RowsTopY;                  // block-local y where the dynamic region starts
            public string RowsSignature;            // computed-display signature (null = repopulate)
            public float NextRefreshAt;             // ~1/sec throttle
            public int ErrorCount;                  // refresh disabled at 3 (LIVE rail idiom)
        }

        private UguiShellResearchContentHandle uguiShellResearchContent;

        // One instrument row's fully-computed display state. Computed by
        // ComputeUguiShellResearchRowDisplays (the EXACT DrawResearchTab per-instrument logic) and
        // consumed by both the signature builder and the row builder, so the diff can never
        // disagree with what would be rendered.
        private struct UguiResearchRowDisplay
        {
            public int StaticId;
            public string Analyzer;
            public string Status;
            public Color StatusColor;
            public bool Busy;
        }

        // UGUI mirror of DrawResearchTab (ResearchDiagnosticsFeature.cs): header, status line,
        // "Instruments" header, per-instrument rows (or the loading hint), two panel-shortcut
        // buttons, footer help line. Static chrome is built here once; rows + bottom-section
        // positions are (re)applied by RebuildUguiShellResearchRows. Assigns the handle field
        // LAST so a mid-build exception (caught by the shell's per-tab guard) can never leave a
        // half-built handle refreshing.
        private GameObject BuildUguiShellResearchContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellResearchContent = null;

            UguiShellResearchContentHandle handle = new UguiShellResearchContentHandle();
            GameObject block = this.CreateUguiGo("ResearchContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            Color muted = this.UguiKitMutedColor();
            Color monoColor = new Color(muted.r, muted.g, muted.b, 0.85f); // IMGUI monoStyle alpha

            GameObject header = this.CreateUguiHeaderLabel(block.transform, "Header", this.L("Research Institute"), 14f);
            PlaceUguiTopLeft(header, pad, 12f, w - pad * 2f, 22f);

            // Status can run long (panel-open results) — wrapped, two lines of room.
            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status",
                this.LF("Status: {0}", this.researchDiagStatus), 11f, monoColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, pad, 40f, w - pad * 2f, 30f);

            GameObject instruments = this.CreateUguiHeaderLabel(block.transform, "Instruments", this.L("Instruments"), 14f);
            PlaceUguiTopLeft(instruments, pad, 76f, w - pad * 2f, 20f);

            GameObject rowsRoot = this.CreateUguiGo("Rows", block.transform);
            PlaceUguiTopLeft(rowsRoot, 0f, 0f, w, h); // rows use block-local coordinates inside
            handle.RowsRoot = rowsRoot.transform;
            handle.RowsTopY = 102f;
            handle.BlockWidth = w;

            handle.StoreButton = this.CreateUguiSecondaryButton(block.transform, "StoreButton",
                this.L("RESEARCH STORE"),
                new System.Action(() => this.StartResearchOpenPanelDirect(ResearchShopPanelTypeName, "Research Store")));
            handle.ConsoleButton = this.CreateUguiSecondaryButton(block.transform, "ConsoleButton",
                this.L("CONTROL CONSOLE"),
                new System.Action(() => this.StartResearchOpenPanelDirect(ResearchControlPanelTypeName, "Control Console")));

            handle.FooterLabel = this.CreateUguiLabel(block.transform, "Footer",
                this.L("Live from the server-sync cache. SELECT ITEM opens that analyzer's research picker (busy analyzers are locked until they finish). Everything is prepared automatically when you open this tab."),
                11f, monoColor, false);
            this.TrySetUguiLabelWrapped(handle.FooterLabel);

            handle.Root = block;

            // Populate immediately (no empty first frame) and seed the signature so the first
            // throttled tick only rebuilds if something actually changed. Also positions the
            // bottom static section for the initial row count.
            List<UguiResearchRowDisplay> rows = this.ComputeUguiShellResearchRowDisplays();
            handle.RowsSignature = BuildUguiShellResearchSignature(rows);
            this.RebuildUguiShellResearchRows(handle, rows);

            this.uguiShellResearchContent = handle;
            return block;
        }

        // True while the UGUI shell is visible and showing the Research tab — the UGUI-side half
        // of ProcessResearchMonitorOnUpdate's tab-open edge detect (its IMGUI half is
        // selectedTab == 9 && showMenu; without this, opening Research via the shell would never
        // fire the one-shot prepare, and SELECT ITEM / the panels could misbehave first-click).
        private bool IsUguiShellResearchTabActive()
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                return shell != null && shell.ActiveIndex == UguiShellResearchTabIndex
                    && this.IsUguiWindowVisible(shell.Window);
            }
            catch
            {
                return false;
            }
        }

        // Called every frame from ProcessUguiShellOnUpdate. Skips in a few comparisons unless the
        // shell is visible ON the Research tab; then throttles to ~1/sec. Each tick always
        // refreshes the status line (one cheap SetText) and rebuilds the rows only when the
        // computed-display signature changed. NextRefreshAt stops advancing while the tab is
        // hidden, so switching back refreshes on the first frame — never shows a stale countdown.
        private void ProcessUguiShellResearchContentOnUpdate()
        {
            UguiShellHandle shell = this.uguiShell;
            UguiShellResearchContentHandle handle = this.uguiShellResearchContent;
            if (shell == null || handle == null || handle.Root == null || handle.ErrorCount >= 3
                || shell.ActiveIndex != UguiShellResearchTabIndex || !this.IsUguiWindowVisible(shell.Window))
            {
                return;
            }

            if (Time.unscaledTime < handle.NextRefreshAt)
            {
                return;
            }
            handle.NextRefreshAt = Time.unscaledTime + 1f;

            try
            {
                this.SetUguiLabelText(handle.StatusLabel, this.LF("Status: {0}", this.researchDiagStatus));

                List<UguiResearchRowDisplay> rows = this.ComputeUguiShellResearchRowDisplays();
                string signature = BuildUguiShellResearchSignature(rows);
                if (!string.Equals(signature, handle.RowsSignature, StringComparison.Ordinal))
                {
                    handle.RowsSignature = signature;
                    this.RebuildUguiShellResearchRows(handle, rows);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Research content refresh error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Per-instrument display computation — copied EXACTLY from DrawResearchTab (clock
        // interpolation + the idle / done / researching 3-case), returning data instead of
        // painting. Returns null in the "loading" state (no snapshot yet / empty list).
        private List<UguiResearchRowDisplay> ComputeUguiShellResearchRowDisplays()
        {
            if (!this.researchMonitorHasSnapshot || this.researchMonitorSnapshot.Count == 0)
            {
                return null;
            }

            // Interpolate the game clock forward from the last poll so the countdowns tick smoothly.
            long estNowTicks = this.researchMonitorClockTicks;
            bool clockSane = estNowTicks >= ResearchSaneClockTicksFloor;
            if (clockSane)
            {
                estNowTicks += (long)((Time.unscaledTime - this.researchMonitorClockSampledAt) * TimeSpan.TicksPerSecond);
            }

            List<UguiResearchRowDisplay> rows = new List<UguiResearchRowDisplay>(this.researchMonitorSnapshot.Count);
            for (int i = 0; i < this.researchMonitorSnapshot.Count; i++)
            {
                ResearchInstrumentSnapshot inst = this.researchMonitorSnapshot[i];
                UguiResearchRowDisplay row = new UguiResearchRowDisplay();
                row.StaticId = inst.StaticId;
                row.Analyzer = this.LF("Analyzer {0}  ·  Lv {1}", inst.StaticId - 2000, inst.Level);

                if (inst.ResearchingItemId <= 0)
                {
                    row.Busy = false;
                    row.Status = this.L("idle");
                    row.StatusColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.7f);
                }
                else if (inst.CompleteTicks > 0L && clockSane && inst.CompleteTicks <= estNowTicks)
                {
                    // Research finished — the slot is free to pick a new item.
                    row.Busy = false;
                    row.Status = this.LF("DONE · {0}", this.ResearchResolveItemName(inst.ResearchingItemId));
                    row.StatusColor = new Color(0.45f, 1f, 0.55f);
                }
                else
                {
                    row.Busy = true;
                    string remain = (inst.CompleteTicks > 0L && clockSane)
                        ? ResearchFormatRemaining(inst.CompleteTicks - estNowTicks)
                        : "?";
                    row.Status = this.LF("researching {0} · {1}", this.ResearchResolveItemName(inst.ResearchingItemId), remain);
                    row.StatusColor = new Color(1f, 0.85f, 0.45f);
                }

                rows.Add(row);
            }
            return rows;
        }

        // Signature over the COMPUTED display values (never the raw snapshot structs — see file
        // header). Status text embeds the minute-granular countdown; StatusColor is fully implied
        // by Status+Busy, so it needs no separate component.
        private static string BuildUguiShellResearchSignature(List<UguiResearchRowDisplay> rows)
        {
            if (rows == null)
            {
                return "loading";
            }

            StringBuilder sb = new StringBuilder(rows.Count * 48);
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append(rows[i].StaticId).Append('|')
                  .Append(rows[i].Analyzer).Append('|')
                  .Append(rows[i].Status).Append('|')
                  .Append(rows[i].Busy ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        // Destroys + rebuilds the instrument rows (rows == null paints the loading hint instead),
        // then REPOSITIONS the static bottom section from the resulting y-cursor — the UGUI
        // equivalent of the IMGUI drawer's y accumulation. Only called when the computed
        // signature changed (or from the initial build) — never unconditionally per tick.
        private void RebuildUguiShellResearchRows(UguiShellResearchContentHandle handle, List<UguiResearchRowDisplay> rows)
        {
            for (int i = 0; i < handle.Rows.Count; i++)
            {
                GameObject row = handle.Rows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.Rows.Clear();

            Transform content = handle.RowsRoot;
            if (content == null)
            {
                return;
            }

            const float pad = 16f;
            float w = handle.BlockWidth;
            float yCur = handle.RowsTopY;
            Color muted = this.UguiKitMutedColor();

            if (rows == null)
            {
                GameObject loading = this.CreateUguiLabel(content, "Loading",
                    this.L("Loading instrument data… (enter the main town — the list fills in a moment)."),
                    11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
                this.TrySetUguiLabelWrapped(loading);
                PlaceUguiTopLeft(loading, pad, yCur, w - pad * 2f, 34f);
                handle.Rows.Add(loading);
                yCur += 38f;
            }
            else
            {
                const float buttonW = 110f;
                float buttonX = w - pad - buttonW;
                float statusW = buttonX - 160f - 6f;
                for (int i = 0; i < rows.Count; i++)
                {
                    UguiResearchRowDisplay row = rows[i];

                    GameObject analyzer = this.CreateUguiBodyLabel(content, "Analyzer" + i, row.Analyzer, 12f);
                    PlaceUguiTopLeft(analyzer, pad, yCur, 140f, 22f);
                    handle.Rows.Add(analyzer);

                    GameObject status = this.CreateUguiLabel(content, "Status" + i, row.Status, 11f, row.StatusColor, false);
                    PlaceUguiTopLeft(status, 160f, yCur + 1f, statusW, 20f);
                    handle.Rows.Add(status);

                    // A busy analyzer can't take a new item — the button is DISABLED, not merely
                    // click-guarded (Unity's default ColorBlock dims the background for free).
                    // Rebuilds fire on any busy transition (busy is part of the signature), so the
                    // interactable state can never go stale.
                    int staticIdCopy = row.StaticId; // capture a copy for the click closure
                    GameObject select = this.CreateUguiSecondaryButton(content, "Select" + i, this.L("SELECT ITEM"),
                        new System.Action(() => this.StartResearchOpenInstrumentPanelForStaticId(staticIdCopy)));
                    PlaceUguiTopLeft(select, buttonX, yCur - 1f, buttonW, 26f);
                    this.SetUguiButtonInteractable(select, !row.Busy);
                    handle.Rows.Add(select);

                    yCur += 32f;
                }

                yCur += 8f;
            }

            // Static bottom section — repositioned, not rebuilt.
            PlaceUguiTopLeft(handle.StoreButton, pad, yCur, 200f, 30f);
            PlaceUguiTopLeft(handle.ConsoleButton, pad + 210f, yCur, 200f, 30f);
            yCur += 38f;
            PlaceUguiTopLeft(handle.FooterLabel, pad, yCur, w - pad * 2f, 60f);
        }

        // ----------------------------------------------------------------------------------------
        // Small label helper (first needed by this round's wrapped body/footer text)
        // ----------------------------------------------------------------------------------------

        // Kit labels default to single-line MidlineLeft; About bodies and the Research status/
        // footer lines need multi-line wrapping inside a fixed-width rect (the IMGUI drawers use
        // wordWrap styles for the same strings). Top-left alignment matches IMGUI's UpperLeft.
        private void TrySetUguiLabelWrapped(GameObject label)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.enableWordWrapping = true;
                    tmp.alignment = TextAlignmentOptions.TopLeft;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txt.alignment = TextAnchor.UpperLeft;
                }
            }
            catch { }
        }
    }
}
