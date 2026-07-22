using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 6 of 8 (migration plan item 11): the
    // FOOD & REPAIR sub-tab — the inline automationSubTab == 1 branch (HeartopiaComplete.Gui.cs:
    // 584-1027), display sub-index 1 (the tabs list {"Main","Food & Repair","Snow Sculpting",
    // "Auto Buy","Auto Sell","Mass Cook","Puzzle","Pet Care"} maps display indices to
    // automationSubTab 0-7 exactly). The remaining three subs (Auto Sell 4, Mass Cook 5,
    // Pet Care 7) are separate future rounds and keep the shell placeholder.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same methods (all this.-accessible
    //    partial-class state, incl. the private instance IsBagOpen()/OpenInventory()/
    //    CloseInventory(); ZERO backend interop additions). Two independent rendering paths over
    //    one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesFoodRepairSubIndex = 1, declared with their siblings in
    //    UguiPhase3Content.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellFeaturesSubTabActive function the Main round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the branch, replayed exactly:
    //  - Status card (:607-625): a PLAIN themePanelStyle GUI.Box (not DrawExentriSectionPanel) →
    //    a bare UguiKitPanelBg sliced fill, 320x126, no ring/header. All FOUR stats are LIVE and
    //    re-read every gated frame through the cached-string compare: GetRepairStatusDisplay() /
    //    GetAutoEatStatusDisplay() / GetCurrentToolDurabilityStatusDisplay() are method calls;
    //    "Current Energy" is the RAW cachedFoodRepairEnergyStatusDisplay FIELD (:588 — drawn via
    //    DrawFoodRepairStatusRow, which is just 2 GUI.Labels, AutoEatRepair.cs:3615). That cache
    //    is only rewritten by RefreshFoodRepairUiStatusSnapshot, whose IMGUI-side callers are
    //    gated on showMenu && selectedTab==3 && automationSubTab==1 (HeartopiaComplete.cs:
    //    1216-1219, 1Hz self-throttle) + a force refresh on sub-tab ENTRY (Gui.cs:410-415) — so
    //    the processor replays BOTH for this surface: the throttled call every gated frame and
    //    the (true) force call on the sub-tab-activation rising edge (handle.WasActive), or the
    //    energy stat would freeze whenever only the UGUI shell is open.
    //  - BUSY-GATE buttons (:628-654) — deliberately NOT a disabled-button pattern: GUI.enabled
    //    is never touched in the source; both buttons stay fully clickable at all times and each
    //    click handler branches on !isRepairing && !isAutoEating itself. Free → start + green
    //    (0.45,1,0.55) success toast; busy → a CONFLICT toast instead, with DIFFERENT text AND
    //    color per button: Auto Repair → L("Bag automation already running") amber (1,0.85,0.35);
    //    Eat Selected Food → L("Auto Eat already running") red (1,0.55,0.55). Reproduced with
    //    always-interactable kit buttons + the same branch in each handler (the user is told WHY
    //    nothing happened, never shown a greyed-out button). SetUguiButtonInteractable is never
    //    called on these two. The :632 AutoEatRepairLog call is debug-only and skipped. The Eat
    //    success toast interpolates GetAutoEatFoodOptionLabel(autoEatFoodType) AFTER
    //    StartAutoEat(false), source order (:646-647).
    //  - DrawPrimary/SecondaryActionButton and DrawSwitchToggle localize internally
    //    (UiKitPrimitives.cs:731/:746/:750) — so button/toggle labels here get ONE this.L each.
    //    The hint line (:655) and the three slider labels (:691/:700/:709) are L/LF'd in source →
    //    same here. The 4 toggles are flag + SaveKeybinds(false), no notifications (:658-689).
    //  - Sliders: int-backed fields with per-frame Clamp(RoundToInt(...)) in IMGUI (:691-716) —
    //    the cross-surface contract is the INT, so the kit sliders are wholeNumbers = TRUE
    //    (Pictures Budget / Foraging AreaLoad precedent; a fractional handle would fight the
    //    external int re-sync). Handlers replay RoundToInt + Clamp([1,100]/[1,100]/[1,3]) +
    //    save-on-change; external re-syncs diff via RoundToInt (Settings→Main FpsSlider shape).
    //    Value labels re-sync per frame (Main-round slider-label precedent), side-by-side row
    //    layout per the Main round's established conversion of this tab's stacked IMGUI rows.
    //  - Dropdowns (:760-861): hand-rolled box+buttons → kit CreateUguiDropdown (out-bool
    //    listenerWired + per-frame poll fallback, Birds shape). Option strings are drawn via
    //    GetAutoRepairOptionLabel/GetAutoEatFoodOptionLabel in source (localized internally) —
    //    passed through the same getters at build. The source's dropdown-open panel-growth
    //    reflow (:767-771) is an IMGUI artifact — the stock Dropdown popup overlays (Birds/
    //    Auto-Buy precedent), so rows sit at fixed y (repair 425, food 461; food control 160x40
    //    tall like the source's wrap-capable field). autoRepairDropdownOpen /
    //    autoEatFoodDropdownOpen / customFoodScrollPos are IMGUI-only visual state and are NEVER
    //    written from here (forceOpenShopDropdownOpen precedent).
    //  - MUTUAL-EXCLUSION (:781-788 vs :794-801 — each header click that OPENS one dropdown
    //    explicitly closes the sibling): the stock Dropdown exposes no open event, so the
    //    processor detects open-state EDGES itself: while expanded, Dropdown.Show() parents the
    //    instantiated list as a "Dropdown List" child directly under the control (template's
    //    parent), destroyed ~0.15s after close — so transform.Find("Dropdown List") != null is a
    //    stripping-proof expanded probe (Dropdown.IsExpanded has no internal uGUI callers and was
    //    not risked; get_options/Hide/RefreshShownValue were RVA-verified live in this build's
    //    dump: 0x1438DD0/0x14369D0/0x1436FC0). A newly-expanded dropdown whose sibling is open
    //    (and not itself newly open — guards the impossible both-new frame and the 0.15s fade
    //    ghost) calls sibling.Hide(). In practice Unity's own full-screen blocker already
    //    prevents two open lists (any outside click closes the open one first), so this edge
    //    logic is the explicit IMGUI-parity mechanism plus belt-and-braces for a failed blocker.
    //  - Repair pick (:817-822): autoRepairType = i + SaveKeybinds(false), verbatim. Food pick
    //    (:838-858): autoEatFoodType = i FIRST, then the LAST-OPTION CASCADE exactly when
    //    i == autoEatFoodOptions.Length-1: customFoodPickMode = true; lastClickedBagFood = "";
    //    OpenInventory() (REAL game-UI call — opens the player's bag); the amber (1,0.8,0.4)
    //    L("Custom Food: Scanning your bag...") toast; scannedBagFoods = null;
    //    customFoodScanRetryTime = Time.time + 0.5f. ANY other pick sets customFoodPickMode =
    //    false instead. Both paths end with SaveKeybinds(false). Stock-Dropdown nuance
    //    (accepted, Birds/Auto-Buy precedent): re-picking the ALREADY-selected option fires no
    //    event — so re-entering pick mode with Custom Food already selected needs a hop through
    //    another option (or the IMGUI twin, which re-fires on any option click).
    //  - DYNAMIC last-option label: GetAutoEatFoodOptionLabel(last) becomes "Custom: X" once a
    //    custom food is saved (AutoEatRepair.cs:52-55) — IMGUI redraws it every frame, a stock
    //    Dropdown bakes option text. The processor recomputes it per gated frame (IMGUI-parity
    //    alloc budget) and on change writes options[last].text + RefreshShownValue() (both
    //    RVA-verified) so caption AND list stay correct across UGUI picks and IMGUI-twin edits.
    //  - CUSTOM FOOD PICKER (:863-1011), visible only while customFoodPickMode &&
    //    autoEatFoodType == last: built ONCE, shown via relayout-on-signature. The signature
    //    packs picker visibility + the list's null/empty/count state + the current-selection
    //    label's visibility — all three drive real layout. The per-frame GAME-STATE POLLING
    //    (IsBagOpen is a GameObject.Find) runs ONLY inside the same pickerVisible condition the
    //    source block uses (:864) — zero bag polling while the picker is closed. Scan logic
    //    replayed verbatim incl. the else-if shape (:870-886): bag-open + null list → scan +
    //    "Found {N}" green toast only when N > 0; else a scheduled retry once
    //    customFoodScanRetryTime elapses (retry scan shows NO toast — source asymmetry). Both
    //    surfaces polling concurrently stays idempotent (null-gate + one-shot retry zeroing).
    //  - The scanned list (:889-954): pooled rows (grow-on-demand, rebind-by-diff, deactivate-
    //    not-destroy — the Pictures nested-list idiom; NOT the Bag/Warehouse virtualized grid,
    //    bag food counts are small), index-stable at (0, i*36), inside a kit scroll view with
    //    both bgs cleared (source draws no box behind the list, Pictures precedent). Height =
    //    Min(count*36, 214) (:898). Row visuals: 28x28 icon at (4,+3) — a RawImage bound from
    //    scannedBagFoodTextures (Theme-round RawImage precedent), hidden when no texture (source
    //    skips DrawTexture); name at (38,+4) 12pt — GetFoodDisplayName with the source's exact
    //    "Food " prefix strip (:908-911, culture-default StartsWith replayed as written);
    //    selection highlight = a ±2px oversized box behind the row (:917-921's GUI.color
    //    (0.3,0.7,0.4)-tinted GUI.Box → sliced fill (0.3,0.7,0.4,0.55), the mapped analog).
    //    Rebinds fire on sprite change or wholesale array replacement (reference-compared —
    //    Rescan re-copies textures under the same keys); the selection highlight diffs per
    //    frame (covers IMGUI-twin picks). Name/texture cannot go stale between binds: the scan
    //    fills both caches BEFORE scannedBagFoods is assigned.
    //  - Row click (:937-948), verbatim order: display name resolved BEFORE the caches clear;
    //    autoEatCustomFoodName = sprite; SaveKeybinds(false); green LF("Custom food set to:
    //    {0}") toast; customFoodPickMode = false; scannedBagFoods = null; both dicts cleared;
    //    retry timer zeroed; if (IsBagOpen()) CloseInventory() — really closes the game bag.
    //  - Empty/scanning states (:955-968): "No food items found. Open your bag and try again."
    //    red (1,0.55,0.55) when the array is empty; "Open your bag to scan for food items..."
    //    amber (1,0.85,0.4) while it is still null. "Select Food:" header (:894) amber bold.
    //    These four picker literals + "Rescan"/"Done"/"Cancel" + "Selected: " are RAW
    //    UNLOCALIZED in source (plain GUI.Label/GUI.Button, no L) — kept raw; the picker's
    //    NOTIFICATIONS are L/LF'd in source and stay localized.
    //  - Rescan (:983-989): clears the three scan states + re-arms a 0.25s retry — does NOT
    //    touch customFoodPickMode, does NOT close the bag, no toast. DONE vs CANCEL (:991-999
    //    vs :1001-1009): VERIFIED LINE-FOR-LINE IDENTICAL bodies — customFoodPickMode = false,
    //    scan state cleared, timer zeroed, close-bag-if-open; they differ ONLY in button style
    //    (primary vs danger). Implemented as one shared close method called by both handlers.
    //  - Current-selection label (:971-979): visible only while autoEatCustomFoodName is
    //    non-empty, green (0.45,1,0.55), "Selected: " + the same "Food "-stripped display name;
    //    text re-synced per frame while the picker is visible (it changes on either surface's
    //    pick), visibility by the layout signature.
    //
    // Cross-surface sync cadence: every gated frame — dropdown poll fallbacks FIRST (a user pick
    // lands before the external re-sync could clobber it — Birds order), the sibling-close edge
    // detection, dropdown external re-syncs (SetValueWithoutNotify + LastValue), the dynamic
    // last-option label, the snapshot refresh + 4 status labels, toggle re-syncs
    // (SetIsOnWithoutNotify), slider re-syncs + value labels, then the picker block (scan poll +
    // row sync + selection text, all inside pickerVisible) and the layout-signature check.
    // Everything diffs before writing; per-frame sync disabled after 3 consecutive errors (LIVE
    // rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiFoodRepairFoodRowHandle
        {
            public GameObject Root;        // whole-row Button (transparent hit surface)
            public GameObject Highlight;   // ±2px oversized selection box behind the content
            public GameObject IconGo;      // RawImage carrier — hidden when no texture cached
            public RawImage Icon;
            public GameObject Label;
            public string BoundSprite = "";
            public bool SelectedShown;
        }

        private sealed class UguiShellFeaturesFoodRepairHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // scroll content width (block w minus viewport insets)

            public GameObject RepairStatusValue;  // 4 LIVE stats (file header)
            public string RepairStatusShown;
            public GameObject EatStatusValue;
            public string EatStatusShown;
            public GameObject EnergyValue;
            public string EnergyShown;
            public GameObject DurabilityValue;
            public string DurabilityShown;

            public Toggle RepairTeleportToggle;
            public Toggle RepairOnToastToggle;
            public Toggle EatAutoTriggerToggle;
            public Toggle EatNoAnimationToggle;

            public GameObject EatTriggerLabel;
            public string EatTriggerShown;
            public Slider EatTriggerSlider;
            public GameObject RepairTriggerLabel;
            public string RepairTriggerShown;
            public Slider RepairTriggerSlider;
            public GameObject RepairUsesLabel;
            public string RepairUsesShown;
            public Slider RepairUsesSlider;

            public Dropdown RepairDropdown;
            public bool RepairDropdownListenerWired;
            public int RepairDropdownLastValue;    // poll-fallback change detection
            public bool RepairDropdownWasExpanded; // sibling-close edge detection (file header)
            public Dropdown FoodDropdown;
            public bool FoodDropdownListenerWired;
            public int FoodDropdownLastValue;
            public bool FoodDropdownWasExpanded;
            public string FoodLastOptionShown;     // dynamic "Custom: X" option text cache

            public GameObject PickerRoot;          // the whole conditional block (file header)
            public GameObject PickerHeader;        // "Select Food:" — only with a non-empty list
            public GameObject FoodListScroll;      // nested kit scroll, bgs cleared
            public Transform FoodListContent;
            public readonly List<UguiFoodRepairFoodRowHandle> FoodRows = new List<UguiFoodRepairFoodRowHandle>();
            public string[] FoodsArraySeen;        // wholesale-replacement rebind detector
            public GameObject PickerEmptyLabel;
            public GameObject PickerScanningLabel;
            public GameObject PickerSelectedLabel;
            public string PickerSelectedShown;
            public GameObject RescanButton;
            public GameObject DoneButton;
            public GameObject CancelButton;

            public bool WasActive;                 // sub-tab-entry edge → force snapshot refresh
            public int LayoutSignature = -1;
            public int ErrorCount;                 // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesFoodRepairHandle uguiShellFeaturesFoodRepair;

        // Fixed content-local geometry (see the builder's cursor replay). The picker starts at
        // the repair-dropdown row + 80 — the source's own num += 80 spacing (:867), which covers
        // both dropdown rows (36 + 40) + a 4px gap in the closed-dropdown flow it was written for.
        private const float UguiFoodRepairDropdownRowY = 425f;
        private const float UguiFoodRepairDropdownsBottomY = 501f; // food row 461 + field h 40
        private const float UguiFoodRepairPickerTopY = UguiFoodRepairDropdownRowY + 80f;
        private const float UguiFoodRepairListWidth = 300f;        // :899 scrollViewRect width
        private const float UguiFoodRepairListInnerWidth = UguiFoodRepairListWidth - 22f; // kit viewport insets ≈ the :900 280px content

        // ----------------------------------------------------------------------------------------
        // Live layout signature — picker visibility, list null/empty/count, selection-label
        // visibility (all three drive real layout). Same expression relayout consumes.
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesFoodRepairLayoutSignature()
        {
            bool pickerVisible = this.customFoodPickMode
                && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1;
            if (!pickerVisible)
            {
                return 0;
            }
            string[] foods = this.scannedBagFoods;
            int listState = foods == null ? 0 : (foods.Length == 0 ? 1 : 2);
            bool selectionVisible = !string.IsNullOrEmpty(this.autoEatCustomFoodName);
            int count = foods != null ? foods.Length : 0;
            return 1 | (listState << 1) | (selectionVisible ? 8 : 0) | (count << 4);
        }

        // Expanded probe — see file header (the "Dropdown List" child exists exactly while the
        // stock popup is alive, incl. its ~0.15s close fade; the edge logic tolerates the ghost).
        private static bool IsUguiFoodRepairDropdownExpanded(Dropdown dd)
        {
            if (dd == null)
            {
                return false;
            }
            try
            {
                return dd.transform.Find("Dropdown List") != null;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of the automationSubTab == 1 branch: status card, 2 busy-gated action
        // buttons, hint, 4 toggles, 3 sliders, 2 cross-closing dropdowns, and the conditional
        // Custom Food picker. Everything — including the whole picker block — is built ONCE here
        // in IMGUI source order at FIXED positions (nothing above the picker is conditional);
        // RelayoutUguiShellFeaturesFoodRepair owns the picker-internal cursor + total scroll
        // height. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesFoodRepairContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesFoodRepair = null;

            UguiShellFeaturesFoodRepairHandle handle = new UguiShellFeaturesFoodRepairHandle();
            GameObject block = this.CreateUguiGo("FeaturesFoodRepairContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            // Flat look over the block's ContentBg (Logging idiom) — alpha-0 images still
            // raycast, so wheel/drag scrolling keeps working.
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (scrollContent != null && scrollContent.parent != null)
                {
                    Image viewportBg = scrollContent.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch { }
            handle.ScrollContent = scrollContent;
            handle.ContentWidth = w - 22f; // viewport insets: 4 left + 18 right

            const float rowX = 8f;
            float rowW = handle.ContentWidth - 16f;
            Color textColor = this.UguiKitTextColor();
            // Source stat styles (:591-605): labels 11 bold uiText; values 12 bold uiText @ 0.92
            // (durability value 11 — compactValueStyle).
            Color statValueColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);

            // -------- Status card (:607-625 — plain themePanelStyle box 320x126; file header).
            // Card-local positions replay the source's cardX/topY math shifted by the card
            // origin: labels row 12, values row 30, energy row 54, durability row 78. --------
            GameObject card = this.CreateUguiGo("StatusCard", scrollContent);
            PlaceUguiTopLeft(card, rowX, 8f, 320f, 126f);
            this.AddUguiImage(card, this.UguiKitPanelBg(), true, 1f);

            GameObject repairStatLabel = this.CreateUguiLabel(card.transform, "RepairStatLabel",
                this.L("Repair Status"), 11f, textColor, false);
            this.TrySetUguiLabelBold(repairStatLabel);
            PlaceUguiTopLeft(repairStatLabel, 12f, 12f, 132f, 18f);
            handle.RepairStatusShown = this.GetRepairStatusDisplay();
            handle.RepairStatusValue = this.CreateUguiLabel(card.transform, "RepairStatValue",
                handle.RepairStatusShown, 12f, statValueColor, false);
            this.TrySetUguiLabelBold(handle.RepairStatusValue);
            PlaceUguiTopLeft(handle.RepairStatusValue, 12f, 30f, 132f, 20f);

            GameObject eatStatLabel = this.CreateUguiLabel(card.transform, "EatStatLabel",
                this.L("Eat Status"), 11f, textColor, false);
            this.TrySetUguiLabelBold(eatStatLabel);
            PlaceUguiTopLeft(eatStatLabel, 164f, 12f, 132f, 18f);
            handle.EatStatusShown = this.GetAutoEatStatusDisplay();
            handle.EatStatusValue = this.CreateUguiLabel(card.transform, "EatStatValue",
                handle.EatStatusShown, 12f, statValueColor, false);
            this.TrySetUguiLabelBold(handle.EatStatusValue);
            PlaceUguiTopLeft(handle.EatStatusValue, 164f, 30f, 132f, 20f);

            GameObject energyLabel = this.CreateUguiLabel(card.transform, "EnergyLabel",
                this.L("Current Energy"), 11f, textColor, false);
            this.TrySetUguiLabelBold(energyLabel);
            PlaceUguiTopLeft(energyLabel, 12f, 54f, 108f, 20f);
            handle.EnergyShown = this.cachedFoodRepairEnergyStatusDisplay;
            handle.EnergyValue = this.CreateUguiLabel(card.transform, "EnergyValue",
                handle.EnergyShown, 12f, statValueColor, false);
            this.TrySetUguiLabelBold(handle.EnergyValue);
            PlaceUguiTopLeft(handle.EnergyValue, 124f, 54f, 164f, 20f);

            GameObject durabilityLabel = this.CreateUguiLabel(card.transform, "DurabilityLabel",
                this.L("Tool Durability"), 11f, textColor, false);
            this.TrySetUguiLabelBold(durabilityLabel);
            PlaceUguiTopLeft(durabilityLabel, 12f, 78f, 132f, 18f);
            handle.DurabilityShown = this.GetCurrentToolDurabilityStatusDisplay();
            handle.DurabilityValue = this.CreateUguiLabel(card.transform, "DurabilityValue",
                handle.DurabilityShown, 11f, statValueColor, false);
            this.TrySetUguiLabelBold(handle.DurabilityValue);
            PlaceUguiTopLeft(handle.DurabilityValue, 124f, 78f, 176f, 18f);

            // -------- Action buttons (:628-654 — ALWAYS clickable, busy-gate in the handlers;
            // file header). Primary 120x35 + secondary 125x35 at the source's 20/160 offsets. --
            GameObject repairButton = this.CreateUguiPrimaryButton(scrollContent, "AutoRepairButton",
                this.L("Auto Repair"),
                new System.Action(this.OnUguiFeaturesFoodRepairAutoRepairClicked));
            PlaceUguiTopLeft(repairButton, rowX, 146f, 120f, 35f);
            GameObject eatButton = this.CreateUguiSecondaryButton(scrollContent, "EatFoodButton",
                this.L("Eat Selected Food"),
                new System.Action(this.OnUguiFeaturesFoodRepairEatFoodClicked));
            PlaceUguiTopLeft(eatButton, rowX + 140f, 146f, 125f, 35f);

            // -------- Hint (:655) --------
            GameObject hint = this.CreateUguiBodyLabel(scrollContent, "AutoEatHint",
                this.L("Auto Eat will continue until energy is full."), 13f);
            PlaceUguiTopLeft(hint, rowX, 191f, rowW, 20f);

            // -------- 4 toggles (:658-689 — flag + save, no toasts) --------
            handle.RepairTeleportToggle = this.CreateUguiCheckbox(scrollContent, "RepairTeleportToggle",
                this.L("Repair Teleport Backward"), this.repairTeleportBackEnabled,
                new System.Action<bool>(this.OnUguiFeaturesFoodRepairRepairTeleportToggled));
            PlaceUguiTopLeft(handle.RepairTeleportToggle.gameObject, rowX, 221f, rowW, 24f);
            handle.RepairOnToastToggle = this.CreateUguiCheckbox(scrollContent, "RepairOnToastToggle",
                this.L("Auto Repair on Durability"), this.autoRepairOnToastEnabled,
                new System.Action<bool>(this.OnUguiFeaturesFoodRepairRepairOnToastToggled));
            PlaceUguiTopLeft(handle.RepairOnToastToggle.gameObject, rowX, 251f, rowW, 24f);
            handle.EatAutoTriggerToggle = this.CreateUguiCheckbox(scrollContent, "EatAutoTriggerToggle",
                this.L("Auto Eat Energy Panel"), this.autoEatAutoTriggerEnabled,
                new System.Action<bool>(this.OnUguiFeaturesFoodRepairEatAutoTriggerToggled));
            PlaceUguiTopLeft(handle.EatAutoTriggerToggle.gameObject, rowX, 281f, rowW, 24f);
            handle.EatNoAnimationToggle = this.CreateUguiCheckbox(scrollContent, "EatNoAnimationToggle",
                this.L("Eat Without Animation"), this.autoEatNoAnimationEnabled,
                new System.Action<bool>(this.OnUguiFeaturesFoodRepairEatNoAnimationToggled));
            PlaceUguiTopLeft(handle.EatNoAnimationToggle.gameObject, rowX, 311f, rowW, 24f);

            // -------- 3 sliders (:691-716 — side-by-side rows per the Main-round conversion;
            // wholeNumbers = TRUE, int contract — file header) --------
            float sliderX = rowX + 230f + 10f;
            float sliderW = handle.ContentWidth - sliderX - 8f;

            handle.EatTriggerShown = this.LF("Auto Eat Trigger: {0}% or lower", this.autoEatTriggerPercent);
            handle.EatTriggerLabel = this.CreateUguiBodyLabel(scrollContent, "EatTriggerLabel",
                handle.EatTriggerShown, 13f);
            PlaceUguiTopLeft(handle.EatTriggerLabel, rowX, 341f + 2f, 230f, 20f);
            handle.EatTriggerSlider = this.CreateUguiSlider(scrollContent, "EatTriggerSlider",
                1f, 100f, this.autoEatTriggerPercent, true,
                new System.Action<float>(this.OnUguiFeaturesFoodRepairEatTriggerChanged));
            PlaceUguiTopLeft(handle.EatTriggerSlider.gameObject, sliderX, 341f + 3f, sliderW, 20f);

            handle.RepairTriggerShown = this.LF("Auto Repair Trigger: {0}% or lower", this.autoRepairTriggerPercent);
            handle.RepairTriggerLabel = this.CreateUguiBodyLabel(scrollContent, "RepairTriggerLabel",
                handle.RepairTriggerShown, 13f);
            PlaceUguiTopLeft(handle.RepairTriggerLabel, rowX, 369f + 2f, 230f, 20f);
            handle.RepairTriggerSlider = this.CreateUguiSlider(scrollContent, "RepairTriggerSlider",
                1f, 100f, this.autoRepairTriggerPercent, true,
                new System.Action<float>(this.OnUguiFeaturesFoodRepairRepairTriggerChanged));
            PlaceUguiTopLeft(handle.RepairTriggerSlider.gameObject, sliderX, 369f + 3f, sliderW, 20f);

            handle.RepairUsesShown = this.LF("Repair Kit Uses: {0}", this.autoRepairUseTarget);
            handle.RepairUsesLabel = this.CreateUguiBodyLabel(scrollContent, "RepairUsesLabel",
                handle.RepairUsesShown, 13f);
            PlaceUguiTopLeft(handle.RepairUsesLabel, rowX, 397f + 2f, 230f, 20f);
            handle.RepairUsesSlider = this.CreateUguiSlider(scrollContent, "RepairUsesSlider",
                1f, 3f, this.autoRepairUseTarget, true,
                new System.Action<float>(this.OnUguiFeaturesFoodRepairRepairUsesChanged));
            PlaceUguiTopLeft(handle.RepairUsesSlider.gameObject, sliderX, 397f + 3f, sliderW, 20f);

            // -------- Dropdown rows (:760-803 — labels 13 bold at the source's 78px column,
            // fields at +90; repair 160x28, food 160x40; fixed y — popups overlay) --------
            GameObject repairKitLabel = this.CreateUguiBodyLabel(scrollContent, "RepairKitLabel",
                this.L("Repair Kit"), 13f);
            this.TrySetUguiLabelBold(repairKitLabel);
            PlaceUguiTopLeft(repairKitLabel, rowX, UguiFoodRepairDropdownRowY + 3f, 90f, 22f);
            string[] repairOptions = new string[this.autoRepairOptions.Length];
            for (int i = 0; i < repairOptions.Length; i++)
            {
                repairOptions[i] = this.GetAutoRepairOptionLabel(i);
            }
            int repairInitial = Mathf.Clamp(this.autoRepairType, 0, repairOptions.Length - 1);
            handle.RepairDropdownLastValue = repairInitial;
            bool repairWired;
            handle.RepairDropdown = this.CreateUguiDropdown(scrollContent, "RepairKitDropdown",
                repairOptions, repairInitial,
                new System.Action<int>(this.OnUguiFeaturesFoodRepairRepairKitPicked), out repairWired);
            handle.RepairDropdownListenerWired = repairWired;
            PlaceUguiTopLeft(handle.RepairDropdown.gameObject, rowX + 90f, UguiFoodRepairDropdownRowY, 160f, 28f);

            GameObject foodTypeLabel = this.CreateUguiBodyLabel(scrollContent, "FoodTypeLabel",
                this.L("Food Type"), 13f);
            this.TrySetUguiLabelBold(foodTypeLabel);
            PlaceUguiTopLeft(foodTypeLabel, rowX, 461f + 3f, 90f, 22f);
            string[] foodOptions = new string[this.autoEatFoodOptions.Length];
            for (int i = 0; i < foodOptions.Length; i++)
            {
                foodOptions[i] = this.GetAutoEatFoodOptionLabel(i);
            }
            handle.FoodLastOptionShown = foodOptions[foodOptions.Length - 1];
            int foodInitial = Mathf.Clamp(this.autoEatFoodType, 0, foodOptions.Length - 1);
            handle.FoodDropdownLastValue = foodInitial;
            bool foodWired;
            handle.FoodDropdown = this.CreateUguiDropdown(scrollContent, "FoodTypeDropdown",
                foodOptions, foodInitial,
                new System.Action<int>(this.OnUguiFeaturesFoodRepairFoodTypePicked), out foodWired);
            handle.FoodDropdownListenerWired = foodWired;
            PlaceUguiTopLeft(handle.FoodDropdown.gameObject, rowX + 90f, 461f, 160f, 40f);

            // -------- Custom Food picker (:863-1011 — the conditional block; positions inside
            // are owned by the relayout, everything is created here once) --------
            float pickerW = handle.ContentWidth - 16f;
            GameObject picker = this.CreateUguiGo("CustomFoodPicker", scrollContent);
            PlaceUguiTopLeft(picker, rowX, UguiFoodRepairPickerTopY, pickerW, 200f);
            handle.PickerRoot = picker;

            handle.PickerHeader = this.CreateUguiLabel(picker.transform, "SelectFoodHeader",
                "Select Food:", 13f, new Color(1f, 0.85f, 0.4f), false);
            this.TrySetUguiLabelBold(handle.PickerHeader);

            Transform listContent;
            handle.FoodListScroll = this.CreateUguiScrollView(picker.transform, "FoodList", 10f, out listContent);
            handle.FoodListContent = listContent;
            try
            {
                Image listBg = handle.FoodListScroll.GetComponent<Image>();
                if (listBg != null)
                {
                    listBg.color = Color.clear; // the source draws no box behind the list
                }
                if (listContent != null && listContent.parent != null)
                {
                    Image listVpBg = listContent.parent.GetComponent<Image>();
                    if (listVpBg != null)
                    {
                        listVpBg.color = Color.clear;
                    }
                }
            }
            catch { }

            handle.PickerEmptyLabel = this.CreateUguiLabel(picker.transform, "NoItemsLabel",
                "No food items found. Open your bag and try again.", 13f,
                new Color(1f, 0.55f, 0.55f), false);
            handle.PickerScanningLabel = this.CreateUguiLabel(picker.transform, "ScanningLabel",
                "Open your bag to scan for food items...", 13f,
                new Color(1f, 0.85f, 0.4f), false);

            handle.PickerSelectedShown = string.Empty;
            handle.PickerSelectedLabel = this.CreateUguiLabel(picker.transform, "SelectedLabel",
                handle.PickerSelectedShown, 13f, new Color(0.45f, 1f, 0.55f), false);

            // Rescan/Done = themePrimaryButtonStyle, Cancel = themeDangerButtonStyle in source
            // (:983/:991/:1001); labels are RAW there (plain GUI.Button — file header).
            handle.RescanButton = this.CreateUguiPrimaryButton(picker.transform, "RescanButton",
                "Rescan", new System.Action(this.OnUguiFeaturesFoodRepairRescanClicked));
            handle.DoneButton = this.CreateUguiPrimaryButton(picker.transform, "DoneButton",
                "Done", new System.Action(this.OnUguiFeaturesFoodRepairDoneClicked));
            handle.CancelButton = this.CreateUguiDangerButton(picker.transform, "CancelButton",
                "Cancel", new System.Action(this.OnUguiFeaturesFoodRepairCancelClicked));

            this.SyncUguiFeaturesFoodRepairFoodRows(handle);
            handle.LayoutSignature = this.ComputeUguiFeaturesFoodRepairLayoutSignature();
            this.RelayoutUguiShellFeaturesFoodRepair(handle);

            handle.Root = block;
            this.uguiShellFeaturesFoodRepair = handle;
            return block;
        }

        // Positions the picker's children for the CURRENT state and sets the total scroll height
        // — the UGUI analog of the branch's picker-section num accumulation (Gui.cs:863-1010):
        // [count>0: header 24 (+28) → list Min(count*36,214) (+10)] / [empty or null: one state
        // line (+30)] → [selection non-empty: label (+26)] → buttons 26 at 0/110/220 (+35).
        // Everything above the picker is fixed at build time. Reposition/SetActive only.
        private void RelayoutUguiShellFeaturesFoodRepair(UguiShellFeaturesFoodRepairHandle handle)
        {
            bool pickerVisible = this.customFoodPickMode
                && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1;
            SetUguiGoActive(handle.PickerRoot, pickerVisible);

            if (!pickerVisible)
            {
                this.SetUguiScrollContentHeight(handle.ScrollContent, UguiFoodRepairDropdownsBottomY + 16f);
                return;
            }

            float pickerW = handle.ContentWidth - 16f;
            string[] foods = this.scannedBagFoods;
            int count = foods != null ? foods.Length : 0;
            bool haveList = foods != null && count > 0;
            float py = 0f;

            SetUguiGoActive(handle.PickerHeader, haveList);
            SetUguiGoActive(handle.FoodListScroll, haveList);
            SetUguiGoActive(handle.PickerEmptyLabel, foods != null && count == 0);
            SetUguiGoActive(handle.PickerScanningLabel, foods == null);

            if (haveList)
            {
                PlaceUguiTopLeft(handle.PickerHeader, 0f, py, pickerW, 24f);
                py += 28f;
                float listH = Mathf.Min(count * 36f, 214f); // :898 — buttons stay visible, list scrolls
                PlaceUguiTopLeft(handle.FoodListScroll, 0f, py, UguiFoodRepairListWidth, listH);
                py += listH + 10f;
            }
            else if (foods != null)
            {
                PlaceUguiTopLeft(handle.PickerEmptyLabel, 0f, py, pickerW, 24f);
                py += 30f;
            }
            else
            {
                PlaceUguiTopLeft(handle.PickerScanningLabel, 0f, py, pickerW, 24f);
                py += 30f;
            }

            bool selectionVisible = !string.IsNullOrEmpty(this.autoEatCustomFoodName);
            SetUguiGoActive(handle.PickerSelectedLabel, selectionVisible);
            if (selectionVisible)
            {
                PlaceUguiTopLeft(handle.PickerSelectedLabel, 0f, py, pickerW, 24f);
                py += 26f;
            }

            PlaceUguiTopLeft(handle.RescanButton, 0f, py, 100f, 26f);
            PlaceUguiTopLeft(handle.DoneButton, 110f, py, 100f, 26f);
            PlaceUguiTopLeft(handle.CancelButton, 220f, py, 100f, 26f);
            py += 35f;

            PlaceUguiTopLeft(handle.PickerRoot, 8f, UguiFoodRepairPickerTopY, pickerW, py);
            this.SetUguiScrollContentHeight(handle.ScrollContent, UguiFoodRepairPickerTopY + py + 16f);
        }

        // ----------------------------------------------------------------------------------------
        // Pooled scanned-food rows (grow-on-demand, rebind-by-diff, deactivate-not-destroy —
        // Pictures nested-list idiom; file header)
        // ----------------------------------------------------------------------------------------

        private UguiFoodRepairFoodRowHandle CreateUguiFeaturesFoodRepairFoodRow(
            UguiShellFeaturesFoodRepairHandle handle, int index)
        {
            UguiFoodRepairFoodRowHandle row = new UguiFoodRepairFoodRowHandle();

            GameObject root = this.CreateUguiGo("Row" + index, handle.FoodListContent);
            PlaceUguiTopLeft(root, 0f, index * 36f, UguiFoodRepairListInnerWidth, 34f); // :914 rows, index-stable
            Image hit = this.AddUguiImage(root, new Color(0f, 0f, 0f, 0f), false, 1f);
            hit.raycastTarget = true;
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = hit;
            // Closure over the ROW HANDLE (not the index): the handler reads the row's current
            // BoundSprite, so pool rebinds can never race a click (Research staticIdCopy idiom,
            // pooled variant).
            UguiFoodRepairFoodRowHandle captured = row;
            btn.onClick.AddListener(new System.Action(
                () => this.OnUguiFeaturesFoodRepairFoodRowClicked(captured)));

            // Selection box behind the content, ±2px oversized like the source's :920 rect
            // (clipped by the list viewport the same way IMGUI's scroll view clipped its box).
            GameObject highlight = this.CreateUguiGo("Highlight", root.transform);
            StretchUguiFill(highlight, -2f, -2f, -2f, -2f);
            this.AddUguiImage(highlight, new Color(0.3f, 0.7f, 0.4f, 0.55f), true, 2f);
            highlight.SetActive(false);
            row.Highlight = highlight;

            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            PlaceUguiTopLeft(iconGo, 4f, 3f, 28f, 28f); // :925 iconRect
            RawImage icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            iconGo.SetActive(false);
            row.IconGo = iconGo;
            row.Icon = icon;

            row.Label = this.CreateUguiLabel(root.transform, "Name", "", 12f, this.UguiKitTextColor(), false);
            PlaceUguiTopLeft(row.Label, 38f, 4f, UguiFoodRepairListInnerWidth - 38f - 4f, 30f); // :932 textRect

            row.Root = root;
            return row;
        }

        private void SyncUguiFeaturesFoodRepairFoodRows(UguiShellFeaturesFoodRepairHandle handle)
        {
            string[] foods = this.scannedBagFoods;
            int count = foods != null ? foods.Length : 0;

            // Wholesale array replacement (scan/rescan) forces a full rebind: Rescan re-copies
            // textures under identical sprite keys, so a per-sprite diff alone would keep stale
            // Texture2D references alive on visually-identical rows (file header).
            bool forceRebind = !object.ReferenceEquals(handle.FoodsArraySeen, foods);
            handle.FoodsArraySeen = foods;

            for (int i = 0; i < count; i++)
            {
                if (i >= handle.FoodRows.Count)
                {
                    handle.FoodRows.Add(this.CreateUguiFeaturesFoodRepairFoodRow(handle, i));
                }
                UguiFoodRepairFoodRowHandle row = handle.FoodRows[i];
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }

                string sprite = foods[i] ?? string.Empty;
                if (forceRebind || !string.Equals(row.BoundSprite, sprite, StringComparison.Ordinal))
                {
                    row.BoundSprite = sprite;
                    // :906-911 — display name with the source's exact "Food " prefix strip.
                    string foodName = this.GetFoodDisplayName(sprite);
                    if (foodName.StartsWith("Food "))
                    {
                        foodName = foodName.Substring(5);
                    }
                    this.SetUguiLabelText(row.Label, foodName);

                    Texture2D tex;
                    if (!this.scannedBagFoodTextures.TryGetValue(sprite, out tex))
                    {
                        tex = null;
                    }
                    if (row.Icon != null)
                    {
                        try { row.Icon.texture = tex; } catch { }
                    }
                    SetUguiGoActive(row.IconGo, tex != null); // :926 — no texture, no icon
                }

                // Selection highlight diffs per frame — an IMGUI-twin pick moves it too (:912).
                bool selected = this.autoEatCustomFoodName == sprite;
                if (selected != row.SelectedShown)
                {
                    row.SelectedShown = selected;
                    SetUguiGoActive(row.Highlight, selected);
                }
            }

            for (int i = count; i < handle.FoodRows.Count; i++)
            {
                UguiFoodRepairFoodRowHandle row = handle.FoodRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }

            this.SetUguiScrollContentHeight(handle.FoodListContent, count * 36f); // :900 content chain
        }

        // ----------------------------------------------------------------------------------------
        // Display builders
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFeaturesFoodRepairSelectedText()
        {
            // Gui.cs:975-977 — "Selected: " + the "Food "-stripped display name (raw literal).
            string selectedName = this.GetFoodDisplayName(this.autoEatCustomFoodName);
            if (selectedName.StartsWith("Food "))
            {
                selectedName = selectedName.Substring(5);
            }
            return "Selected: " + selectedName;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesFoodRepairOnUpdate()
        {
            UguiShellFeaturesFoodRepairHandle handle = this.uguiShellFeaturesFoodRepair;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3)
            {
                return;
            }
            if (!this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesFoodRepairSubIndex))
            {
                handle.WasActive = false; // re-arm the entry edge for the next activation
                return;
            }

            try
            {
                // Sub-tab-entry edge — the UGUI half of the IMGUI force refresh on switching
                // onto this sub (Gui.cs:410-415); the per-frame call below then matches the
                // showMenu-gated 1Hz-throttled hook (HeartopiaComplete.cs:1216-1219).
                if (!handle.WasActive)
                {
                    handle.WasActive = true;
                    this.RefreshFoodRepairUiStatusSnapshot(true);
                }
                this.RefreshFoodRepairUiStatusSnapshot();

                // Dropdown poll fallbacks — only when UnityEvent<int> wiring reported failure
                // (Birds precedent). BEFORE the external re-syncs so a user pick lands first.
                if (!handle.RepairDropdownListenerWired && handle.RepairDropdown != null)
                {
                    int v = handle.RepairDropdown.value;
                    if (v != handle.RepairDropdownLastValue)
                    {
                        this.OnUguiFeaturesFoodRepairRepairKitPicked(v); // updates LastValue itself
                    }
                }
                if (!handle.FoodDropdownListenerWired && handle.FoodDropdown != null)
                {
                    int v = handle.FoodDropdown.value;
                    if (v != handle.FoodDropdownLastValue)
                    {
                        this.OnUguiFeaturesFoodRepairFoodTypePicked(v);
                    }
                }

                // Sibling-close edge detection (:781-788/:794-801 parity — file header): a
                // dropdown that JUST became expanded closes an already-open sibling. The
                // !siblingNewly guards keep the impossible both-new frame and the 0.15s close
                // fade ghost from killing the freshly-opened one.
                bool repairOpen = IsUguiFoodRepairDropdownExpanded(handle.RepairDropdown);
                bool foodOpen = IsUguiFoodRepairDropdownExpanded(handle.FoodDropdown);
                bool repairNewly = repairOpen && !handle.RepairDropdownWasExpanded;
                bool foodNewly = foodOpen && !handle.FoodDropdownWasExpanded;
                if (repairNewly && foodOpen && !foodNewly)
                {
                    try { handle.FoodDropdown.Hide(); } catch { }
                }
                else if (foodNewly && repairOpen && !repairNewly)
                {
                    try { handle.RepairDropdown.Hide(); } catch { }
                }
                handle.RepairDropdownWasExpanded = repairOpen;
                handle.FoodDropdownWasExpanded = foodOpen;

                // Dropdown external re-syncs (the IMGUI twin moved the fields) — WithoutNotify +
                // LastValue update (Birds shape).
                if (handle.RepairDropdown != null)
                {
                    int want = Mathf.Clamp(this.autoRepairType, 0, this.autoRepairOptions.Length - 1);
                    if (handle.RepairDropdown.value != want)
                    {
                        handle.RepairDropdown.SetValueWithoutNotify(want);
                        handle.RepairDropdownLastValue = want;
                    }
                }
                if (handle.FoodDropdown != null)
                {
                    int want = Mathf.Clamp(this.autoEatFoodType, 0, this.autoEatFoodOptions.Length - 1);
                    if (handle.FoodDropdown.value != want)
                    {
                        handle.FoodDropdown.SetValueWithoutNotify(want);
                        handle.FoodDropdownLastValue = want;
                    }
                }

                // Dynamic last-option label — "Custom Food" becomes "Custom: X" once a custom
                // food is saved (file header). Recomputed per frame (IMGUI drew it per frame),
                // options[last].text + RefreshShownValue only on change.
                string wantLast = this.GetAutoEatFoodOptionLabel(this.autoEatFoodOptions.Length - 1);
                if (handle.FoodDropdown != null
                    && !string.Equals(wantLast, handle.FoodLastOptionShown, StringComparison.Ordinal))
                {
                    handle.FoodLastOptionShown = wantLast;
                    try
                    {
                        var opts = handle.FoodDropdown.options;
                        int lastIdx = this.autoEatFoodOptions.Length - 1;
                        if (opts != null && opts.Count > lastIdx)
                        {
                            opts[lastIdx].text = wantLast;
                            handle.FoodDropdown.RefreshShownValue();
                        }
                    }
                    catch { }
                }

                // The 4 LIVE stats — read fresh every gated frame, cached-string compare only
                // (file header). "Current Energy" is the RAW field, not a method call (:588).
                this.SyncUguiSelfLabelText(handle.RepairStatusValue, ref handle.RepairStatusShown,
                    this.GetRepairStatusDisplay());
                this.SyncUguiSelfLabelText(handle.EatStatusValue, ref handle.EatStatusShown,
                    this.GetAutoEatStatusDisplay());
                this.SyncUguiSelfLabelText(handle.EnergyValue, ref handle.EnergyShown,
                    this.cachedFoodRepairEnergyStatusDisplay);
                this.SyncUguiSelfLabelText(handle.DurabilityValue, ref handle.DurabilityShown,
                    this.GetCurrentToolDurabilityStatusDisplay());

                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.RepairTeleportToggle, this.repairTeleportBackEnabled);
                this.SyncUguiToggleFromField(handle.RepairOnToastToggle, this.autoRepairOnToastEnabled);
                this.SyncUguiToggleFromField(handle.EatAutoTriggerToggle, this.autoEatAutoTriggerEnabled);
                this.SyncUguiToggleFromField(handle.EatNoAnimationToggle, this.autoEatNoAnimationEnabled);

                // Slider re-syncs — RoundToInt compare against the int fields (Settings→Main
                // FpsSlider shape) + per-frame value labels (Main-round precedent).
                if (handle.EatTriggerSlider != null
                    && Mathf.RoundToInt(handle.EatTriggerSlider.value) != this.autoEatTriggerPercent)
                {
                    handle.EatTriggerSlider.SetValueWithoutNotify(this.autoEatTriggerPercent);
                }
                this.SyncUguiSelfLabelText(handle.EatTriggerLabel, ref handle.EatTriggerShown,
                    this.LF("Auto Eat Trigger: {0}% or lower", this.autoEatTriggerPercent));
                if (handle.RepairTriggerSlider != null
                    && Mathf.RoundToInt(handle.RepairTriggerSlider.value) != this.autoRepairTriggerPercent)
                {
                    handle.RepairTriggerSlider.SetValueWithoutNotify(this.autoRepairTriggerPercent);
                }
                this.SyncUguiSelfLabelText(handle.RepairTriggerLabel, ref handle.RepairTriggerShown,
                    this.LF("Auto Repair Trigger: {0}% or lower", this.autoRepairTriggerPercent));
                if (handle.RepairUsesSlider != null
                    && Mathf.RoundToInt(handle.RepairUsesSlider.value) != this.autoRepairUseTarget)
                {
                    handle.RepairUsesSlider.SetValueWithoutNotify(this.autoRepairUseTarget);
                }
                this.SyncUguiSelfLabelText(handle.RepairUsesLabel, ref handle.RepairUsesShown,
                    this.LF("Repair Kit Uses: {0}", this.autoRepairUseTarget));

                // Custom Food picker — ALL live-game polling stays inside the SAME condition the
                // source block uses (:864), so IsBagOpen (a GameObject.Find) never runs while
                // the picker is closed (file header).
                bool pickerVisible = this.customFoodPickMode
                    && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1;
                if (pickerVisible)
                {
                    // :870-886 verbatim, incl. the else-if shape and the retry branch's missing
                    // "Found {N}" toast (source asymmetry — file header).
                    if (this.IsBagOpen() && this.scannedBagFoods == null)
                    {
                        this.scannedBagFoods = this.ScanBagForFoodItems();
                        if (this.scannedBagFoods.Length > 0)
                        {
                            this.AddMenuNotification(
                                this.LF("Found {0} food item(s) in bag.", this.scannedBagFoods.Length),
                                new Color(0.45f, 1f, 0.55f));
                        }
                    }
                    else if (this.customFoodScanRetryTime > 0f && Time.time >= this.customFoodScanRetryTime)
                    {
                        this.customFoodScanRetryTime = 0f;
                        if (this.IsBagOpen())
                        {
                            this.scannedBagFoods = this.ScanBagForFoodItems();
                        }
                    }

                    this.SyncUguiFeaturesFoodRepairFoodRows(handle);
                    this.SyncUguiSelfLabelText(handle.PickerSelectedLabel, ref handle.PickerSelectedShown,
                        this.BuildUguiFeaturesFoodRepairSelectedText());
                }

                // Conditional-layout signature (picker visibility + list state + selection label).
                int signature = this.ComputeUguiFeaturesFoodRepairLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesFoodRepair(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Food & Repair content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // ----------------------------------------------------------------------------------------

        // Gui.cs:628-640 — the busy-gate branch IN the handler; the button itself is never
        // disabled (file header). Free → StartRepair() + green toast; busy → the AMBER
        // "Bag automation already running" conflict toast. The :632 debug log is skipped.
        private void OnUguiFeaturesFoodRepairAutoRepairClicked()
        {
            if (!this.isRepairing && !this.isAutoEating)
            {
                this.StartRepair();
                this.AddMenuNotification(this.L("Auto Repair started"), new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.AddMenuNotification(this.L("Bag automation already running"), new Color(1f, 0.85f, 0.35f));
            }
        }

        // Gui.cs:642-653 — same shape, DIFFERENT conflict text AND color (RED "Auto Eat already
        // running"). Success toast interpolates the CURRENT food-option label, built after
        // StartAutoEat(false) like the source.
        private void OnUguiFeaturesFoodRepairEatFoodClicked()
        {
            if (!this.isRepairing && !this.isAutoEating)
            {
                this.StartAutoEat(false);
                this.AddMenuNotification(
                    this.LF("Auto Eat started ({0})", this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)),
                    new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.AddMenuNotification(this.L("Auto Eat already running"), new Color(1f, 0.55f, 0.55f));
            }
        }

        // Gui.cs:658-663 — flag + save. The equal-guard is the UGUI analog of IMGUI's
        // prev-vs-new change check (Main-round idiom), same for the three below.
        private void OnUguiFeaturesFoodRepairRepairTeleportToggled(bool value)
        {
            if (value == this.repairTeleportBackEnabled)
            {
                return;
            }
            this.repairTeleportBackEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:667-672.
        private void OnUguiFeaturesFoodRepairRepairOnToastToggled(bool value)
        {
            if (value == this.autoRepairOnToastEnabled)
            {
                return;
            }
            this.autoRepairOnToastEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:675-680.
        private void OnUguiFeaturesFoodRepairEatAutoTriggerToggled(bool value)
        {
            if (value == this.autoEatAutoTriggerEnabled)
            {
                return;
            }
            this.autoEatAutoTriggerEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:683-688.
        private void OnUguiFeaturesFoodRepairEatNoAnimationToggled(bool value)
        {
            if (value == this.autoEatNoAnimationEnabled)
            {
                return;
            }
            this.autoEatNoAnimationEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:692-697 — Clamp(RoundToInt) into the int field, save only on a real change.
        private void OnUguiFeaturesFoodRepairEatTriggerChanged(float value)
        {
            int newValue = Mathf.Clamp(Mathf.RoundToInt(value), 1, 100);
            if (newValue == this.autoEatTriggerPercent)
            {
                return;
            }
            this.autoEatTriggerPercent = newValue;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:701-706.
        private void OnUguiFeaturesFoodRepairRepairTriggerChanged(float value)
        {
            int newValue = Mathf.Clamp(Mathf.RoundToInt(value), 1, 100);
            if (newValue == this.autoRepairTriggerPercent)
            {
                return;
            }
            this.autoRepairTriggerPercent = newValue;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:710-715 — [1,3].
        private void OnUguiFeaturesFoodRepairRepairUsesChanged(float value)
        {
            int newValue = Mathf.Clamp(Mathf.RoundToInt(value), 1, 3);
            if (newValue == this.autoRepairUseTarget)
            {
                return;
            }
            this.autoRepairUseTarget = newValue;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:817-822 — index + save, verbatim. The :820 autoRepairDropdownOpen = false write
        // is IMGUI-only visual state, deliberately NOT reproduced (file header). No equal-guard:
        // the source ran this on ANY option click and both writes are idempotent.
        private void OnUguiFeaturesFoodRepairRepairKitPicked(int index)
        {
            UguiShellFeaturesFoodRepairHandle handle = this.uguiShellFeaturesFoodRepair;
            if (handle != null)
            {
                handle.RepairDropdownLastValue = index;
            }
            this.autoRepairType = index;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:838-858 — index FIRST, then the last-option cascade / customFoodPickMode reset,
        // SaveKeybinds LAST, all in source order. The cascade fires EXACTLY on the last option
        // ("Custom Food"): pick mode + lastClickedBagFood reset + OpenInventory() (the REAL game
        // bag) + the amber scanning toast + scan-state reset + the 0.5s retry arm. Every other
        // pick only clears pick mode. The :841 autoEatFoodDropdownOpen = false write is
        // IMGUI-only visual state, NOT reproduced. The picker's appearance itself is handled by
        // the processor's next signature check.
        private void OnUguiFeaturesFoodRepairFoodTypePicked(int index)
        {
            UguiShellFeaturesFoodRepairHandle handle = this.uguiShellFeaturesFoodRepair;
            if (handle != null)
            {
                handle.FoodDropdownLastValue = index;
            }
            this.autoEatFoodType = index;
            if (index == this.autoEatFoodOptions.Length - 1)
            {
                this.customFoodPickMode = true;
                this.lastClickedBagFood = "";
                this.OpenInventory();
                this.AddMenuNotification(this.L("Custom Food: Scanning your bag..."), new Color(1f, 0.8f, 0.4f));
                // Clear any previous scan so it will scan when the bag opens (:849-851).
                this.scannedBagFoods = null;
                this.customFoodScanRetryTime = Time.time + 0.5f;
            }
            else
            {
                this.customFoodPickMode = false;
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:937-948 — verbatim order; the display name is resolved BEFORE the caches clear
        // (the source resolved it during draw, pre-click — same values by construction).
        private void OnUguiFeaturesFoodRepairFoodRowClicked(UguiFoodRepairFoodRowHandle row)
        {
            if (row == null || string.IsNullOrEmpty(row.BoundSprite))
            {
                return;
            }
            string foodSprite = row.BoundSprite;
            string foodName = this.GetFoodDisplayName(foodSprite);
            if (foodName.StartsWith("Food "))
            {
                foodName = foodName.Substring(5);
            }

            this.autoEatCustomFoodName = foodSprite;
            try { this.SaveKeybinds(false); } catch { }
            this.AddMenuNotification(this.LF("Custom food set to: {0}", foodName), new Color(0.45f, 1f, 0.55f));
            this.customFoodPickMode = false;
            this.scannedBagFoods = null;
            this.scannedBagFoodTextures.Clear();
            this.scannedBagFoodDisplayNames.Clear();
            this.customFoodScanRetryTime = 0f;
            if (this.IsBagOpen())
            {
                this.CloseInventory();
            }
        }

        // Gui.cs:983-989 — clears the scan state and re-arms a 0.25s retry. Does NOT touch
        // customFoodPickMode and does NOT close the bag (unlike Done/Cancel).
        private void OnUguiFeaturesFoodRepairRescanClicked()
        {
            this.scannedBagFoods = null;
            this.scannedBagFoodTextures.Clear();
            this.scannedBagFoodDisplayNames.Clear();
            this.customFoodScanRetryTime = Time.time + 0.25f;
        }

        // Gui.cs:991-999 (Done) and :1001-1009 (Cancel) have LINE-FOR-LINE IDENTICAL bodies —
        // verified against the source; they differ only in button style. One shared close
        // routine keeps that fact explicit.
        private void OnUguiFeaturesFoodRepairDoneClicked()
        {
            this.CloseUguiFeaturesFoodRepairCustomFoodPicker();
        }

        private void OnUguiFeaturesFoodRepairCancelClicked()
        {
            this.CloseUguiFeaturesFoodRepairCustomFoodPicker();
        }

        private void CloseUguiFeaturesFoodRepairCustomFoodPicker()
        {
            this.customFoodPickMode = false;
            this.scannedBagFoods = null;
            this.scannedBagFoodTextures.Clear();
            this.scannedBagFoodDisplayNames.Clear();
            this.customFoodScanRetryTime = 0f;
            if (this.IsBagOpen())
            {
                this.CloseInventory();
            }
        }
    }
}
