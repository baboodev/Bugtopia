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
    // UGUI SHELL — display-position wiring constants for every tab and sub-tab.
    //
    // These are positions in BuildUguiShell's tabLabels / subTabLabels arrays (UguiShell.cs), NOT
    // the old internal selectedTab ids. Content files and the shell's per-tab gates both index off
    // these, so they live in one place rather than in whichever content file happened to need them
    // first. 6+ files depend on them.
    //
    // Wiring is ALWAYS by index, never by comparing localized label strings — labels change with
    // the language, indices do not.
    //
    // HISTORICAL NOTE: the per-constant comments below cite IMGUI drawers (DrawBuildingTab,
    // DrawAutoFarmTab, Gui.cs line numbers, ...). That code was deleted when the IMGUI menu was
    // retired; the references are kept only because they record WHY each index has the value it
    // does. Do not expect those symbols to exist.
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
    }
}
