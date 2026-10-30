using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 8 of 8 (migration plan item 11): the
    // MASS COOK sub-tab — DrawMassCookTab (HeartopiaComplete.NetCook.cs:34-404), display
    // sub-index 5 (the tabs list {"Main","Food & Repair","Snow Sculpting","Auto Buy","Auto Sell",
    // "Mass Cook","Puzzle","Pet Care"} maps display indices to automationSubTab 0-7 exactly;
    // dispatcher: Gui.cs:1286-1289 `automationSubTab == 5 → DrawMassCookTab`).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    ZERO changes to HeartopiaComplete.NetCook.cs; this file only READS the same fields and
    //    CALLS the same methods (all this.-accessible partial-class state). Two independent
    //    rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesMassCookSubIndex = 5, declared with their siblings in
    //    UguiShellTabIndices.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellFeaturesSubTabActive function the Main round established — no new gate.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //  - The IMGUI panel-height formula (Gui.cs:289-297: 540/620/840 by state) is a set of
    //    hardcoded estimates, none of them real — this port computes its scroll height from the
    //    relayout cursor like every prior conditional-content round.
    //
    // Source nuances verified against DrawMassCookTab, replayed exactly:
    //  - Header pill (:89-90): "RUNNING" (themeTopTabActiveStyle ?? themePrimaryButtonStyle box,
    //    green 0.45,1,0.55 text) vs "READY" (themeTopTabStyle ?? themePanelStyle box, muted
    //    uiText@0.78 text) — LIVE, re-synced every gated frame. Mapped: accent vs control-fill
    //    Image color flip + label text/color flip (the CreateUguiSwitch pillImg-recolor idiom —
    //    no button-internals mutation needed, the pill is this file's own Image).
    //  - Capture Stoves (:94-117): busy-gated via GUI.enabled on netCookCaptureInProgress ||
    //    netCookCaptureCoroutine != null || Time.unscaledTime < nextNetCookCaptureAllowedAt —
    //    time-dependent, so SetUguiButtonInteractable re-evaluates EVERY gated frame. Click →
    //    TryCaptureNetCookFromCurrentTarget() with the BRANCHING toast: success + expanding
    //    (netCookCaptureCoroutine != null checked AFTER the call) → amber (1,0.85,0.45)
    //    "Expanding stove capture..."; success otherwise → green (0.45,1,0.55) netCookStatus with
    //    the "Mass cook stoves captured" blank-fallback; failure → red (1,0.55,0.55)
    //    netCookStatus ?? "Capture failed.".
    //  - Reset Capture (:119-123): ALWAYS enabled, no busy gate; style flips danger when
    //    netCookEnabled, else default. No kit button re-tiers at runtime, so the flip is a
    //    same-rect SetActive PAIR SWAP (secondary + danger twins, one handler) — same for the
    //    Start/Stop button below (primary + danger). Click → ResetNetCookCaptureContext(
    //    "Captured stoves reset. Capture stoves again.") + the fixed amber (1,0.75,0.45) toast.
    //  - Clean Up Finished Food (:126-133): busy-gated on netCookCleanupCoroutine != null →
    //    StartNetCookCleanupSweep().
    //  - The 5 toggles (:135-208) and their SAVE ASYMMETRY: "Auto Cook Assist" (cascade: closes the
    //    recipe + stove-type dropdowns, clears the assist watch list when turned off, one of two
    //    status strings, SAVES), "Remember Stoves" (status, SAVES),
    //    "Capture Own" (status, SAVES), "Capture Radius" (status, SAVES) — but "Status
    //    Diagnostics (log)" has NO SaveKeybinds call in source (verified absent; the flag is
    //    session-only). Its cascade instead does real work, file-log-only, no toast: OFF →
    //    netCookStatusDiagLastLogAt.Clear() + netCookStatusDiagSessionAnnounced = false +
    //    nextNetCookDiagHeartbeatAt = 0f + the OFF ModLogger line; ON →
    //    EnsureNetCookStatusDiagHooks() + the long ON ModLogger line. Reproduced verbatim, and
    //    deliberately NOT given a save call. All toggle labels get this.L (the IMGUI twin is
    //    DrawSwitchToggle, which localizes internally — UiKitPrimitives.cs:750); everything else
    //    on this tab is raw GUI.Label/GUI.Button strings and stays UNlocalized.
    //  - ASSIST MODE card (shown while Auto Cook Assist is on, :210-225): height is DYNAMIC in source —
    //    Mathf.Max(32f, statusStyle.CalcHeight(desc, w-24)) + 36 + 12. Ported with the Pictures
    //    round's MeasureUguiPicturesWrappedHeight (TMP GetPreferredValues, Ceil+4), fallback 32f
    //    = the source's own Max floor, retried on gated frames until the TMP component has
    //    Awoken (built-inactive caveat). Height feeds the layout signature.
    //  - RECIPE dropdown (always branch, :227-293): a hand-rolled header (GetNetCookSelectedRecipeLabel
    //    caption — LIVE per gated frame, capture can auto-select a recipe — + accent "^"/"v"
    //    arrow) toggling the SHARED netCookRecipeDropdownOpen flag. Shared on purpose (unlike
    //    Food & Repair's stock-Dropdown round, whose IMGUI open-flags had no UGUI counterpart):
    //    here the flag IS the panel's model on both surfaces — the Auto Cook Assist cascade and the
    //    pick-cascade close both surfaces at once, and cross-surface open/close lands via the
    //    layout signature. EnsureNetCookRecipeCache() runs per gated frame in this branch (:228
    //    runs it per repaint; self-caching).
    //  - The OPEN panel (:243-293) is the Teleport-NPC searchable-list shape: a search InputField
    //    LIVE per keystroke (onValueChanged + the .text-vs-last-applied gated poll compare as
    //    wiring insurance AND external-change detector — the uguiPocDropdownPollFallback idiom),
    //    64-char limit, writing it ALSO resets netCookRecipeScrollPos = Vector2.zero (:258 —
    //    shared cascade, keeps the IMGUI twin's scroll sane; the UGUI list scrolls to top too).
    //    The rows feed from GetVisibleNetCookRecipeEntries() — called as-is, never reimplemented
    //    (it filters by the shared search text AND by cooker type, and re-sorts; IMGUI calls it
    //    every repaint while open, so binding every gated frame while open is parity cost and
    //    also catches cache/cooker-type changes from background captures). Rows are POOLED
    //    (grow-on-demand, rebind-by-diff, deactivate-not-destroy — the Pictures/Food & Repair
    //    nested-list idiom; recipe catalogs are hundreds of entries, per-keystroke destroy+
    //    rebuild would stutter). Row = selection-flipped box (accent vs control fill; label
    //    GetUiTextOnAccent on accent — the kit's text-on-accent rule — vs uiText) + the entry
    //    label with the source's exact blank-fallback "Recipe " + Key (:287). Click closures
    //    capture the ROW HANDLE, not an index/list (GetVisibleNetCookRecipeEntries returns a
    //    REUSED cleared-per-call list — nothing from it may outlive the bind). Empty search →
    //    the "No recipes match your search." row (:269).
    //  - Recipe pick cascade (:280-285), verbatim order: netCookRecipeId = Key;
    //    netCookRecipeDropdownOpen = false; netCookCookQuantity = 1 AND its text mirror
    //    netCookCookQuantityInput = "1" (BOTH — the int and the string); nextNetCookMaxRefreshAt
    //    = 0f; netCookStatus = "Selected recipe: " + RAW Value (no blank-fallback here — only
    //    the row LABEL falls back). NO save call (verified absent).
    //  - Move Ingredients / Use All Ingredients (:295-313): flag + nextNetCookMaxRefreshAt = 0f
    //    + SAVE, no status/toast.
    //  - DISH LIMIT row (:315-333): RefreshNetCookMaxCookQuantity() every gated frame (per-repaint
    //    in source; self-throttled via nextNetCookMaxRefreshAt) → "Ingredients max: N" or
    //    "Ingredients max: —" at ≤ 0. The quantity box is a FREE-TEXT string field
    //    (netCookCookQuantityInput, 6-char limit) — NO inline parsing/clamping in the draw path;
    //    a change assigns the raw text and calls SyncNetCookCookQuantityFromInput(), which owns
    //    the parsing AND normalizes the string back (parse-fail → 1) — the gated poll then
    //    pushes the normalized value into the field, the UGUI analog of IMGUI's next-repaint
    //    snap. Same poll doubles as the IMGUI-twin external-edit sync.
    //  - Start/Stop: ONE of TWO captions by netCookEnabled — "STOP MASS COOK" on the danger twin,
    //    "START MASS COOK" on the primary twin. The captions are FIXED: Auto Cook Assist is the
    //    checkbox above and never moves these buttons; pair swap by netCookEnabled. Click →
    //    StopNetCookInternal("Disabled") when enabled, else StartNetCookInternal().
    //  - Settings card (:352-377) and the SLIDER ASYMMETRY: COOK DELAY = float [0.25,10] rounded
    //    to the NEAREST 0.01 (Mathf.Round(v*100)/100 — :363), "{0:F2}s", epsilon-saved (>0.0001),
    //    NO status side effect; SCAN RADIUS = float [NetCookMinScanRadiusMeters,
    //    NetCookMaxScanRadiusMeters] rounded to a WHOLE number (plain Mathf.Round — :371, so the
    //    kit slider is wholeNumbers=true), "{0:F0}m", epsilon-saved AND sets netCookStatus
    //    ("Scan radius set to {0:F0}m. Capture stoves again to refresh targets.") on change.
    //    Only the radius slider touches the status string — verified.
    //  - Status card (:379-402): STOVES = netCookTargets.Count and SENT = netCookSentCount, both
    //    LIVE ints per gated frame; the status line is NOT "always netCookStatus" — a readiness
    //    fallback (hasCapturedStoves × hasRecipe) shows ONLY while netCookStatus is
    //    blank/whitespace, else the live netCookStatus (:400).
    //    Reproduced as a builder evaluated per gated frame. (The label gets 34px instead of the
    //    source's 28 — TMP clips where IMGUI overflowed; two 12pt lines stay inside the card.)
    //
    // Cross-surface sync cadence: every gated frame — busy-gate interactables, pill, button-pair
    // swaps + captions, 7 toggle re-syncs (SetIsOnWithoutNotify), recipe-branch work EVERY frame
    // (cache ensure, header caption/arrow, search poll pair, row bind while open,
    // max refresh + label, qty poll pair), slider re-syncs + value labels, status-card stats +
    // text, assist-card measure retry while assist is on, then the layout-signature check. Everything diffs before
    // writing; per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiMassCookRecipeRowHandle
        {
            public GameObject Root;
            public Image Fill;             // control fill / accent when selected (:277)
            public GameObject Label;
            public int BoundId = int.MinValue;
            public string BoundValue;      // RAW entry value — the pick-status string uses this
            public string BoundDisplay;    // shown label (blank-fallback applied)
            public bool SelectedShown;
            public Image Icon;             // dish icon — same game pipeline as the Bag/Warehouse grid
            public int IconTexId;          // diffed so a bound row only re-assigns the sprite on change
            // Recents are hoisted to the top of the list and marked with this badge rather than
            // separated by a group header: rows are pooled at a FIXED step (UguiMassCookRecipeRowStep),
            // so a header occupying its own slot would desync every index below it.
            public GameObject Badge;
            public bool BadgeShown;
        }

        // STOVE TYPE picker row (see HeartopiaComplete.NetCookStoveType.cs). Same pooled-row shape as
        // the recipe list; BoundRecipeCookerType 0 is the "Auto (majority)" row.
        private sealed class UguiMassCookStoveTypeRowHandle
        {
            public GameObject Root;
            public Image Fill;
            public GameObject Label;
            public int BoundRecipeCookerType = int.MinValue;
            public string BoundDisplay;
            public bool SelectedShown;
        }

        private sealed class UguiShellFeaturesMassCookHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // block w minus kit viewport insets

            public Image PillBg;                  // RUNNING/READY box — color flip
            public GameObject PillLabel;
            public int PillShownState = -1;       // -1 forces the first apply

            public GameObject CaptureButton;      // busy-gated per gated frame (time-dependent)
            public GameObject ResetButtonDefault; // secondary twin — visible while !netCookEnabled
            public GameObject ResetButtonDanger;  // danger twin — visible while netCookEnabled
            public GameObject CleanupButton;      // busy-gated on the cleanup coroutine

            public Toggle AutoAssistToggle;
            public Toggle RememberStovesToggle;
            public Toggle CaptureOwnToggle;
            public Toggle CaptureRadiusToggle;
            public Toggle StatusDiagToggle;
            public Toggle ManualIngredientsToggle;
            public Toggle CookableOnlyToggle;
            public readonly List<GameObject> SlotTiles = new List<GameObject>();
            public readonly List<Image> SlotTileFills = new List<Image>();
            public readonly List<Image> SlotTileIcons = new List<Image>();
            public readonly List<int> SlotTileIconTexIds = new List<int>();
            public readonly List<GameObject> SlotTileLabels = new List<GameObject>();
            public readonly List<string> SlotTileLabelShown = new List<string>();
            public readonly List<int> SlotTileActiveShown = new List<int>();  // -1 unknown, 0/1 diffed
            public GameObject SlotBackButton;
            public int SlotBackCellShown = -1;   // which cell the Back tile sits in
            public int SlotAreaRowsShown = -1;   // drives the one re-place the grid needs

            public GameObject AssistCard;         // shown while Auto Cook Assist is on
            public GameObject AssistDescLabel;
            public float AssistTextHeight = 32f;  // Max(32, measured) — source fallback floor
            public bool AssistTextMeasured;       // retry until the TMP component has Awoken

            // STOVE TYPE picker — only built/shown when the capture census saw 2+ recipe cooker
            // types in range (mixed kitchen); hidden entirely otherwise, so the single-type layout
            // is byte-for-byte what it was before the feature.
            public GameObject StoveTypeLabel;
            public GameObject StoveTypeHeader;
            public GameObject StoveTypeHeaderValue;
            public string StoveTypeHeaderShown;
            public GameObject StoveTypeArrow;
            public string StoveTypeArrowShown;
            public GameObject StoveTypePanel;
            public GameObject StoveTypeListScroll;
            public Transform StoveTypeListContent;
            public readonly List<UguiMassCookStoveTypeRowHandle> StoveTypeRows = new List<UguiMassCookStoveTypeRowHandle>();
            public int StoveTypeCensusVersionShown = -1;   // rebind rows only when the census moves
            public int StoveTypeSelectionShown = int.MinValue;

            public GameObject RecipeLabel;        // recipe branch — "RECIPE"
            public GameObject RecipeHeader;       // dropdown header box (whole-header button)
            public GameObject RecipeHeaderValue;
            public string RecipeHeaderShown;
            public GameObject RecipeArrow;        // "^" open / "v" closed
            public string RecipeArrowShown;
            public GameObject RecipePanel;        // the open-state searchable panel
            public InputField RecipeSearchField;
            public string RecipeSearchApplied;    // NPC-search idiom (poll + external detection)
            public GameObject RecipeListScroll;
            public Transform RecipeListContent;
            public readonly List<UguiMassCookRecipeRowHandle> RecipeRows = new List<UguiMassCookRecipeRowHandle>();
            public GameObject RecipeEmptyLabel;   // "No recipes match your search."

            public Toggle MoveIngredientsToggle;
            public Toggle UseAllIngredientsToggle;
            public Toggle UseUniversalIngredientToggle;
            public GameObject DishLimitLabel;     // static caption
            public GameObject DishMaxLabel;       // "Ingredients max: ..." — live
            public string DishMaxShown;
            public InputField QtyField;
            public string QtyApplied;             // poll + normalization push-back cache

            public GameObject StartButton;        // primary twin — visible while !netCookEnabled
            public GameObject StopButton;         // danger twin — visible while netCookEnabled
            public string StartShown;             // caption caches (fixed captions; diffed before writing)
            public string StopShown;

            public GameObject RadiusCard;         // top of the tab — scan radius
            public GameObject DelayCard;          // above the recipe block — cook delay
            public GameObject DelayValueLabel;
            public string DelayShown;
            public Slider DelaySlider;
            public GameObject RadiusValueLabel;
            public string RadiusShown;
            public Slider RadiusSlider;

            public GameObject StatusCard;
            public GameObject StovesValueLabel;
            public string StovesShown;
            public GameObject SentValueLabel;
            public string SentShown;
            public GameObject StatusTextLabel;
            public string StatusTextShown;

            public int LayoutSignature = -1;
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesMassCookHandle uguiShellFeaturesMassCook;

        // Content-local fixed geometry — the source's num cursor (left 40, controlWidth 470)
        // re-based to 8. Scan Radius was pulled out of the old two-slider settings card and put
        // at the top, because it governs CAPTURE and capture is the first thing you do here:
        // header 8 (+36) → radius card 44 (+64) → capture row 108 (+50) → cleanup 158 (+50) →
        // the seven toggles 208/246/284/322/360/398/436 (+38 each) → conditional region 474.
        // Everything from 474 down is owned by the relayout (both branches shift it).
        private const float UguiMassCookConditionalTopY = 474f;
        private const float UguiMassCookSliderCardHeight = 56f;
        private const float UguiMassCookRecipePanelHeight = 300f;
        private const float UguiMassCookRecipeRowStep = 28f;       // :275 — 24-tall rows stepping 28
        // Stove Type panel: at most 17 rows (16 recipe cooker types + Auto), no search field, so it
        // sizes to its content and caps instead of taking the recipe list's fixed 260.
        private const float UguiMassCookStoveTypeRowStep = 28f;
        private const float UguiMassCookStoveTypeMaxPanelHeight = 176f;

        // Recipe picker grid. Same tile shape as the Bag/Warehouse cells, minus the virtualization:
        // that pool exists because a bag holds hundreds of stacks, while a cooker type offers a few
        // dozen dishes. The existing row pool already grows on demand and is never destroyed, which
        // is all this needs.
        private const int UguiMassCookRecipeColumns = 5;
        private const float UguiMassCookRecipeCellH = 92f;
        private const float UguiMassCookRecipeCellGap = 4f;

        // Cell width is DERIVED from the list's inner width rather than fixed, so the column count
        // is the only thing to change when the grid is made denser — a hardcoded width silently
        // leaves a ragged margin (or overflows) the moment either number moves.
        private static float UguiMassCookRecipeCellWidth(float innerW)
        {
            float w = (innerW - (UguiMassCookRecipeColumns - 1) * UguiMassCookRecipeCellGap)
                / UguiMassCookRecipeColumns;
            return Mathf.Max(36f, Mathf.Floor(w));
        }

        // Slot tiles: one per material slot of the selected recipe, laid out on the same column
        // grid as the dishes so the two read as one surface. Compact — icon over a name, no room
        // for anything else at a fifth of the panel width.
        private const float UguiMassCookSlotAreaTop = 40f;   // just under the search row
        private const float UguiMassCookSlotTileH = 52f;
        private const float UguiMassCookSlotTileIcon = 24f;
        private const int UguiMassCookMaxSlotButtons = 12;

        // With no slot tiles the grid starts right under the search row — the old fixed 72 left a
        // band of dead panel whenever Pick Ingredients was off, which is most of the time.
        // Grid content lives inside the scroll view (x 4 plus 4 of viewport inset); the slot tiles
        // sit directly on the panel, so they start at 8 to line their columns up with the dishes.
        private const float UguiMassCookSlotAreaX = 8f;

        private static float UguiMassCookGridInnerWidth(UguiShellFeaturesMassCookHandle handle)
        {
            return handle.ContentWidth - 16f - 8f - 22f;   // panel rowW-8 minus kit viewport insets
        }

        private static void PlaceUguiSlotTile(GameObject go, int cell, float cellW)
        {
            PlaceUguiTopLeft(go,
                UguiMassCookSlotAreaX + (cell % UguiMassCookRecipeColumns) * (cellW + UguiMassCookRecipeCellGap),
                UguiMassCookSlotAreaTop + (cell / UguiMassCookRecipeColumns) * (UguiMassCookSlotTileH + UguiMassCookRecipeCellGap),
                cellW, UguiMassCookSlotTileH);
        }

        private static float UguiMassCookRecipeGridTopFor(int slotRows)
        {
            if (slotRows <= 0)
            {
                return UguiMassCookSlotAreaTop;
            }

            return UguiMassCookSlotAreaTop + slotRows * (UguiMassCookSlotTileH + UguiMassCookRecipeCellGap) + 2f;
        }

        // ----------------------------------------------------------------------------------------
        // Live layout signature — branch, dropdown-open, measured assist-card height (all three
        // drive real layout). Visible-row COUNT is deliberately absent: the open panel is a fixed
        // 260 and the rows scroll inside it.
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesMassCookLayoutSignature(UguiShellFeaturesMassCookHandle handle)
        {
            // The Stove Type row count enters the signature because the open panel sizes to it (0
            // rows also encodes "picker hidden", so no separate visibility bit is needed).
            int stoveTypeRows = this.GetUguiFeaturesMassCookStoveTypeRowCount();
            return (this.netCookAutoAssist ? 1 : 0)
                 | (this.netCookRecipeDropdownOpen ? 2 : 0)
                 | (this.netCookCookerTypeDropdownOpen ? 4 : 0)
                 | ((stoveTypeRows & 0x3F) << 3)
                 | (Mathf.CeilToInt(handle.AssistTextHeight) << 9)
                 // Same reasoning as the stove rows: the recipe panel grows by the slot area, so
                 // the area's row count is a layout input. Read straight off the last slot read —
                 // never refresh from here, that would fire AuraMono from the signature path.
                 | ((this.GetUguiMassCookSlotAreaRowCount() & 0x7) << 20);
        }

        // Cells the slot area shows: one per material slot, plus Back while the picker is open.
        private int GetUguiMassCookSlotAreaCellCount()
        {
            if (!this.netCookSlotManualMode || this.netCookRecipeId <= 0)
            {
                return 0;
            }

            int slots = Mathf.Min(this.netCookSlotInfoBuffer.Count, UguiMassCookMaxSlotButtons);
            if (slots <= 0)
            {
                return 0;
            }

            return slots + (this.netCookSlotPickerIndex >= 0 ? 1 : 0);
        }

        private int GetUguiMassCookSlotAreaRowCount()
        {
            int cells = this.GetUguiMassCookSlotAreaCellCount();
            return (cells + UguiMassCookRecipeColumns - 1) / UguiMassCookRecipeColumns;
        }

        // The panel grows by exactly what the slot tiles occupy, so turning Pick Ingredients on
        // never costs the dish grid a row.
        private float GetUguiFeaturesMassCookRecipePanelHeight()
        {
            return UguiMassCookRecipePanelHeight
                + (UguiMassCookRecipeGridTopFor(this.GetUguiMassCookSlotAreaRowCount()) - UguiMassCookSlotAreaTop);
        }

        // Auto row + one row per census group; 0 while the picker is hidden.
        private int GetUguiFeaturesMassCookStoveTypeRowCount()
        {
            return this.ShouldShowNetCookCookerTypePicker() ? (this.netCookScannedCookerTypes.Count + 1) : 0;
        }

        private float GetUguiFeaturesMassCookStoveTypePanelHeight()
        {
            int rows = this.GetUguiFeaturesMassCookStoveTypeRowCount();
            return Mathf.Min(rows * UguiMassCookStoveTypeRowStep + 16f, UguiMassCookStoveTypeMaxPanelHeight);
        }

        // The status card's fallback-vs-live conditional (:397-401) — NOT "always netCookStatus".
        private string BuildUguiFeaturesMassCookStatusText()
        {
            bool hasCapturedStoves = this.netCookTargets.Count > 0;
            bool hasRecipe = this.netCookRecipeId > 0;
            string readiness = hasCapturedStoves
                ? (hasRecipe ? "Ready to cook." : "Select a recipe to continue.")
                : "Capture stoves to begin.";
            return string.IsNullOrWhiteSpace(this.netCookStatus) ? readiness : this.netCookStatus;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawMassCookTab: header + pill, capture/reset row, cleanup, 5 toggles,
        // the conditional region (assist card shown when Auto Cook Assist is on, then the recipe
        // dropdown + ingredient toggles + dish limit always), start/stop, settings card, status
        // card — everything built ONCE;
        // RelayoutUguiShellFeaturesMassCook owns positions from the conditional region down and
        // the total scroll height. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesMassCookContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesMassCook = null;

            UguiShellFeaturesMassCookHandle handle = new UguiShellFeaturesMassCookHandle();
            GameObject block = this.CreateUguiGo("FeaturesMassCookContent", parent);
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
            float halfW = (rowW - 10f) * 0.5f;    // :93 — (controlWidth - rowGap) * 0.5
            // Source style colors (:43-85): header/value/stat-value = white; small/stat labels =
            // uiText @ 0.78; status/option text = uiText.
            Color mutedTextColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.78f);
            Color textColor = this.UguiKitTextColor();

            // -------- Header + pill (:87-91) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header", this.L("MASS COOK"), 15f, Color.white, false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, rowX, 8f, rowW - 96f, 30f);

            GameObject pill = this.CreateUguiGo("StatusPill", scrollContent);
            PlaceUguiTopLeft(pill, rowX + rowW - 86f, 11f, 86f, 24f); // header y + 3 (:89)
            handle.PillBg = this.AddUguiImage(pill, this.UguiKitControlFill(), true, 1.5f);
            handle.PillLabel = this.CreateUguiLabel(pill.transform, "Label", this.L("READY"), 11f, mutedTextColor, true);
            this.TrySetUguiLabelBold(handle.PillLabel);
            StretchUguiFill(handle.PillLabel, 2f, 0f, 2f, 0f);

            // -------- SCAN RADIUS (first control: it decides what Capture below will take) -----
            handle.RadiusCard = this.CreateUguiGo("RadiusCard", scrollContent);
            this.AddUguiImage(handle.RadiusCard, this.UguiKitPanelBg(), true, 1f);
            PlaceUguiTopLeft(handle.RadiusCard, rowX, 44f, rowW, UguiMassCookSliderCardHeight);
            float sliderCardW = rowW - 24f;

            GameObject radiusLabel = this.CreateUguiLabel(handle.RadiusCard.transform, "RadiusLabel",
                this.L("SCAN RADIUS"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(radiusLabel);
            PlaceUguiTopLeft(radiusLabel, 12f, 10f, sliderCardW * 0.55f, 18f);
            handle.RadiusShown = string.Format("{0:F0}m", this.netCookScanRadiusMeters);
            handle.RadiusValueLabel = this.CreateUguiLabel(handle.RadiusCard.transform, "RadiusValue",
                handle.RadiusShown, 12f, Color.white, false);
            PlaceUguiTopLeft(handle.RadiusValueLabel, 12f + sliderCardW * 0.55f, 10f, sliderCardW * 0.45f, 18f);
            // wholeNumbers=true — the source's plain Mathf.Round contract (:371, file header).
            handle.RadiusSlider = this.CreateUguiSlider(handle.RadiusCard.transform, "RadiusSlider",
                NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters, this.netCookScanRadiusMeters, true,
                new System.Action<float>(this.OnUguiFeaturesMassCookRadiusChanged));
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 12f, 30f, sliderCardW, 20f);

            // -------- Capture / Reset row (:93-124) --------
            handle.CaptureButton = this.CreateUguiPrimaryButton(scrollContent, "CaptureButton",
                this.L("Capture Stoves"), new System.Action(this.OnUguiFeaturesMassCookCaptureClicked));
            PlaceUguiTopLeft(handle.CaptureButton, rowX, 108f, halfW, 36f);

            // Style-flip pair (file header): same rect, same handler, SetActive by netCookEnabled.
            handle.ResetButtonDefault = this.CreateUguiSecondaryButton(scrollContent, "ResetButtonDefault",
                this.L("Reset Capture"), new System.Action(this.OnUguiFeaturesMassCookResetClicked));
            PlaceUguiTopLeft(handle.ResetButtonDefault, rowX + halfW + 10f, 108f, halfW, 36f);
            handle.ResetButtonDanger = this.CreateUguiDangerButton(scrollContent, "ResetButtonDanger",
                this.L("Reset Capture"), new System.Action(this.OnUguiFeaturesMassCookResetClicked));
            PlaceUguiTopLeft(handle.ResetButtonDanger, rowX + halfW + 10f, 108f, halfW, 36f);

            // -------- Clean Up Finished Food (:126-133) --------
            handle.CleanupButton = this.CreateUguiPrimaryButton(scrollContent, "CleanupButton",
                this.L("Clean Up Finished Food"), new System.Action(this.OnUguiFeaturesMassCookCleanupClicked));
            PlaceUguiTopLeft(handle.CleanupButton, rowX, 158f, rowW, 36f);

            // -------- The five toggles (:135-208) — DrawSwitchToggle localizes, so L() here --------
            handle.AutoAssistToggle = this.CreateUguiCheckbox(scrollContent, "AutoAssistToggle",
                this.L("Auto Cook Assist"), this.netCookAutoAssist,
                new System.Action<bool>(this.OnUguiFeaturesMassCookAutoAssistToggled));
            PlaceUguiTopLeft(handle.AutoAssistToggle.gameObject, rowX, 208f, rowW, 24f);
            handle.RememberStovesToggle = this.CreateUguiCheckbox(scrollContent, "RememberStovesToggle",
                this.L("Remember Stoves"), this.netCookRememberStoves,
                new System.Action<bool>(this.OnUguiFeaturesMassCookRememberStovesToggled));
            PlaceUguiTopLeft(handle.RememberStovesToggle.gameObject, rowX, 246f, rowW, 24f);
            handle.CaptureOwnToggle = this.CreateUguiCheckbox(scrollContent, "CaptureOwnToggle",
                this.L("Capture Own"), this.netCookCaptureOwnOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCaptureOwnToggled));
            PlaceUguiTopLeft(handle.CaptureOwnToggle.gameObject, rowX, 284f, rowW, 24f);
            handle.CaptureRadiusToggle = this.CreateUguiCheckbox(scrollContent, "CaptureRadiusToggle",
                this.L("Capture Radius"), this.netCookCaptureRadiusOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCaptureRadiusToggled));
            PlaceUguiTopLeft(handle.CaptureRadiusToggle.gameObject, rowX, 322f, rowW, 24f);
            handle.StatusDiagToggle = this.CreateUguiCheckbox(scrollContent, "StatusDiagToggle",
                this.L("Status Diagnostics (log)"), this.netCookStatusDiagEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMassCookStatusDiagToggled));
            PlaceUguiTopLeft(handle.StatusDiagToggle.gameObject, rowX, 360f, rowW, 24f);

            // Manual ingredients: off keeps the shipped behaviour (the game's AutoFill decides).
            handle.ManualIngredientsToggle = this.CreateUguiCheckbox(scrollContent, "ManualIngredientsToggle",
                this.L("Pick Ingredients"), this.netCookSlotManualMode,
                new System.Action<bool>(this.OnUguiFeaturesMassCookManualIngredientsToggled));
            PlaceUguiTopLeft(handle.ManualIngredientsToggle.gameObject, rowX, 398f, rowW, 24f);

            handle.CookableOnlyToggle = this.CreateUguiCheckbox(scrollContent, "CookableOnlyToggle",
                this.L("Only What I Can Cook"), this.netCookCookableOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCookableOnlyToggled));
            PlaceUguiTopLeft(handle.CookableOnlyToggle.gameObject, rowX, 436f, rowW, 24f);

            // -------- ASSIST MODE card (shown while Auto Cook Assist is on; height via relayout) --------
            string assistModeDescription = this.L("Handles cooking mini-game prompts and auto-collects finished food. It will not prepare or start cooking.");
            handle.AssistCard = this.CreateUguiGo("AssistCard", scrollContent);
            this.AddUguiImage(handle.AssistCard, this.UguiKitPanelBg(), true, 1f);
            GameObject assistTitle = this.CreateUguiLabel(handle.AssistCard.transform, "AssistTitle",
                this.L("ASSIST MODE"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(assistTitle);
            PlaceUguiTopLeft(assistTitle, 12f, 8f, rowW - 24f, 18f);
            handle.AssistDescLabel = this.CreateUguiLabel(handle.AssistCard.transform, "AssistDesc",
                assistModeDescription, 12f, textColor, false);
            this.TrySetUguiLabelWrapped(handle.AssistDescLabel);
            PlaceUguiTopLeft(handle.AssistDescLabel, 12f, 28f, rowW - 24f, 32f);
            // Source: Max(32, CalcHeight(desc, w-24)) (:214). Try now; the processor retries
            // while !ok (built-inactive TMP caveat — Pictures precedent).
            {
                bool measured;
                float measuredH = this.MeasureUguiPicturesWrappedHeight(handle.AssistDescLabel,
                    assistModeDescription, rowW - 24f, 32f, out measured);
                handle.AssistTextHeight = Mathf.Max(32f, measuredH);
                handle.AssistTextMeasured = measured;
            }

            // -------- STOVE TYPE label + dropdown header + panel (mixed-kitchen picker) --------
            // Same hand-rolled dropdown shape as RECIPE below (header box + accent arrow + a panel
            // of pooled rows), minus the search field: at most 17 entries. Built unconditionally,
            // shown only when the census has 2+ types — the relayout owns visibility and positions.
            handle.StoveTypeLabel = this.CreateUguiLabel(scrollContent, "StoveTypeLabel", this.L("STOVE TYPE"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(handle.StoveTypeLabel);

            handle.StoveTypeHeader = this.CreateUguiGo("StoveTypeHeader", scrollContent);
            Image stoveTypeHeaderBg = this.AddUguiImage(handle.StoveTypeHeader, this.UguiKitControlFill(), true, 1.5f);
            stoveTypeHeaderBg.raycastTarget = true;
            Button stoveTypeHeaderBtn = handle.StoveTypeHeader.AddComponent<Button>();
            stoveTypeHeaderBtn.targetGraphic = stoveTypeHeaderBg;
            this.WireUguiClick(stoveTypeHeaderBtn.onClick, new System.Action(this.OnUguiFeaturesMassCookStoveTypeHeaderClicked));
            handle.StoveTypeHeaderShown = this.GetNetCookSelectedCookerTypeLabel();
            handle.StoveTypeHeaderValue = this.CreateUguiLabel(handle.StoveTypeHeader.transform, "Value",
                handle.StoveTypeHeaderShown, 12f, Color.white, false);
            this.TrySetUguiLabelBold(handle.StoveTypeHeaderValue);
            StretchUguiFill(handle.StoveTypeHeaderValue, 12f, 1f, 34f, 1f);
            handle.StoveTypeArrowShown = this.netCookCookerTypeDropdownOpen ? "^" : "v";
            handle.StoveTypeArrow = this.CreateUguiLabel(handle.StoveTypeHeader.transform, "Arrow",
                handle.StoveTypeArrowShown, 12f, this.UguiKitAccent(), true);
            this.TrySetUguiLabelBold(handle.StoveTypeArrow);
            RectTransform stoveTypeArrowRt = handle.StoveTypeArrow.GetComponent<RectTransform>();
            stoveTypeArrowRt.anchorMin = new Vector2(1f, 0.5f);
            stoveTypeArrowRt.anchorMax = new Vector2(1f, 0.5f);
            stoveTypeArrowRt.pivot = new Vector2(1f, 0.5f);
            stoveTypeArrowRt.anchoredPosition = new Vector2(-8f, 0f);
            stoveTypeArrowRt.sizeDelta = new Vector2(16f, 34f);

            handle.StoveTypePanel = this.CreateUguiGo("StoveTypePanel", scrollContent);
            this.AddUguiImage(handle.StoveTypePanel, this.UguiKitPanelBg(), true, 1f);
            Transform stoveTypeListContent;
            handle.StoveTypeListScroll = this.CreateUguiScrollView(handle.StoveTypePanel.transform, "StoveTypeList",
                10f, out stoveTypeListContent);
            PlaceUguiTopLeft(handle.StoveTypeListScroll, 4f, 4f, rowW - 8f,
                UguiMassCookStoveTypeMaxPanelHeight - 8f); // relayout resizes to the live row count
            handle.StoveTypeListContent = stoveTypeListContent;
            try
            {
                Image stoveTypeListBg = handle.StoveTypeListScroll.GetComponent<Image>();
                if (stoveTypeListBg != null)
                {
                    stoveTypeListBg.color = Color.clear; // the panel itself is the box
                }
                if (stoveTypeListContent != null && stoveTypeListContent.parent != null)
                {
                    Image stoveTypeVpBg = stoveTypeListContent.parent.GetComponent<Image>();
                    if (stoveTypeVpBg != null)
                    {
                        stoveTypeVpBg.color = Color.clear;
                    }
                }
            }
            catch { }

            // -------- RECIPE label + dropdown header (:229-241 — recipe branch) --------
            handle.RecipeLabel = this.CreateUguiLabel(scrollContent, "RecipeLabel", this.L("RECIPE"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(handle.RecipeLabel);

            handle.RecipeHeader = this.CreateUguiGo("RecipeHeader", scrollContent);
            Image recipeHeaderBg = this.AddUguiImage(handle.RecipeHeader, this.UguiKitControlFill(), true, 1.5f);
            recipeHeaderBg.raycastTarget = true;
            Button recipeHeaderBtn = handle.RecipeHeader.AddComponent<Button>();
            recipeHeaderBtn.targetGraphic = recipeHeaderBg;
            this.WireUguiClick(recipeHeaderBtn.onClick, new System.Action(this.OnUguiFeaturesMassCookRecipeHeaderClicked));
            handle.RecipeHeaderShown = this.GetNetCookSelectedRecipeLabel();
            handle.RecipeHeaderValue = this.CreateUguiLabel(handle.RecipeHeader.transform, "Value",
                handle.RecipeHeaderShown, 12f, Color.white, false);
            this.TrySetUguiLabelBold(handle.RecipeHeaderValue);
            StretchUguiFill(handle.RecipeHeaderValue, 12f, 1f, 34f, 1f); // :239 — value inset, arrow clear
            handle.RecipeArrowShown = this.netCookRecipeDropdownOpen ? "^" : "v";
            handle.RecipeArrow = this.CreateUguiLabel(handle.RecipeHeader.transform, "Arrow",
                handle.RecipeArrowShown, 12f, this.UguiKitAccent(), true);
            this.TrySetUguiLabelBold(handle.RecipeArrow);
            RectTransform arrowRt = handle.RecipeArrow.GetComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1f, 0.5f);   // :240 — 16 wide at xMax - 24
            arrowRt.anchorMax = new Vector2(1f, 0.5f);
            arrowRt.pivot = new Vector2(1f, 0.5f);
            arrowRt.anchoredPosition = new Vector2(-8f, 0f);
            arrowRt.sizeDelta = new Vector2(16f, 34f);

            // -------- The searchable panel (:243-293 — open state; Teleport-NPC search shape,
            // pooled rows per the file header) --------
            handle.RecipePanel = this.CreateUguiGo("RecipePanel", scrollContent);
            this.AddUguiImage(handle.RecipePanel, this.UguiKitPanelBg(), true, 1f);

            GameObject searchLabel = this.CreateUguiLabel(handle.RecipePanel.transform, "SearchLabel",
                this.L("Search"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(searchLabel);
            PlaceUguiTopLeft(searchLabel, 18f, 12f, 52f, 20f); // :250-253 panel-local
            handle.RecipeSearchApplied = this.netCookRecipeSearchText ?? string.Empty;
            handle.RecipeSearchField = this.CreateUguiInputField(handle.RecipePanel.transform, "SearchField",
                handle.RecipeSearchApplied, 64,
                new System.Action<string>(this.OnUguiFeaturesMassCookRecipeSearchChanged));
            PlaceUguiTopLeft(handle.RecipeSearchField.gameObject, 74f, 11f, rowW - 90f, 22f);

            // Slot tiles: one per material slot of the selected recipe, each showing the ingredient
            // that will actually go in (the pinned one, or whatever AutoFill chose), on the same
            // column grid as the dishes. Built once at the maximum count and shown or hidden per
            // sync — the slot count changes with every recipe, and building GameObjects inside an
            // open panel on each recipe click would churn the hierarchy.
            float slotCellW = UguiMassCookRecipeCellWidth(UguiMassCookGridInnerWidth(handle));
            for (int i = 0; i < UguiMassCookMaxSlotButtons; i++)
            {
                int slotIndex = i;
                GameObject tile = this.CreateUguiGo("SlotTile" + i, handle.RecipePanel.transform);
                Image tileFill = this.AddUguiImage(tile, this.UguiKitControlFill(), true, 1.5f);
                tileFill.raycastTarget = true;
                Button tileBtn = tile.AddComponent<Button>();
                tileBtn.targetGraphic = tileFill;
                this.WireUguiClick(tileBtn.onClick,
                    new System.Action(() => this.OnUguiFeaturesMassCookSlotButtonClicked(slotIndex)));
                PlaceUguiSlotTile(tile, i, slotCellW);
                SetUguiGoActive(tile, false);

                // Icon starts disabled: the sprite lands asynchronously and an enabled Image with
                // no sprite paints a white square.
                GameObject tileIconGo = this.CreateUguiGo("Icon", tile.transform);
                Image tileIcon = this.AddUguiImage(tileIconGo, Color.white, false, 0f);
                tileIcon.raycastTarget = false;   // the whole tile is the button
                tileIcon.enabled = false;
                try { tileIcon.preserveAspect = true; } catch { }
                PlaceUguiTopLeft(tileIconGo, (slotCellW - UguiMassCookSlotTileIcon) * 0.5f, 2f,
                    UguiMassCookSlotTileIcon, UguiMassCookSlotTileIcon);

                GameObject tileLabel = this.CreateUguiLabel(tile.transform, "Name", "", 9f,
                    this.UguiKitTextColor(), true);
                PlaceUguiTopLeft(tileLabel, 2f, UguiMassCookSlotTileIcon + 3f, slotCellW - 4f, 18f);

                handle.SlotTiles.Add(tile);
                handle.SlotTileFills.Add(tileFill);
                handle.SlotTileIcons.Add(tileIcon);
                handle.SlotTileIconTexIds.Add(0);
                handle.SlotTileLabels.Add(tileLabel);
                handle.SlotTileLabelShown.Add(null);
                handle.SlotTileActiveShown.Add(-1);
            }

            // Back rides in the slot area as one more cell, right after the last slot, so leaving
            // the item grid is where the eye already is and no row of its own is spent on it.
            handle.SlotBackButton = this.CreateUguiSecondaryButton(handle.RecipePanel.transform,
                "SlotBack", this.L("Back"), new System.Action(this.OnUguiFeaturesMassCookSlotBackClicked));
            PlaceUguiSlotTile(handle.SlotBackButton, 0, slotCellW);
            SetUguiGoActive(handle.SlotBackButton, false);

            Transform recipeListContent;
            handle.RecipeListScroll = this.CreateUguiScrollView(handle.RecipePanel.transform, "RecipeList",
                10f, out recipeListContent);
            PlaceUguiTopLeft(handle.RecipeListScroll, 4f, UguiMassCookRecipeGridTopFor(0), rowW - 8f,
                UguiMassCookRecipePanelHeight - UguiMassCookRecipeGridTopFor(0) - 4f);
            handle.RecipeListContent = recipeListContent;
            try
            {
                Image listBg = handle.RecipeListScroll.GetComponent<Image>();
                if (listBg != null)
                {
                    listBg.color = Color.clear; // the panel itself is the box (:247)
                }
                if (recipeListContent != null && recipeListContent.parent != null)
                {
                    Image listVpBg = recipeListContent.parent.GetComponent<Image>();
                    if (listVpBg != null)
                    {
                        listVpBg.color = Color.clear;
                    }
                }
            }
            catch { }

            handle.RecipeEmptyLabel = this.CreateUguiLabel(recipeListContent, "EmptyLabel",
                this.L("No recipes match your search."), 11f, textColor, false);
            this.TrySetUguiLabelBold(handle.RecipeEmptyLabel);
            PlaceUguiTopLeft(handle.RecipeEmptyLabel, 8f, 6f, rowW - 8f - 22f - 16f, 22f); // :269
            handle.RecipeEmptyLabel.SetActive(false);

            // -------- Ingredient toggles + dish limit + quantity (:295-333 — recipe branch;
            // positions owned by the relayout, the open panel shifts them) --------
            handle.MoveIngredientsToggle = this.CreateUguiCheckbox(scrollContent, "MoveIngredientsToggle",
                this.L("Move Ingredients"), this.netCookMoveIngredients,
                new System.Action<bool>(this.OnUguiFeaturesMassCookMoveIngredientsToggled));
            handle.UseAllIngredientsToggle = this.CreateUguiCheckbox(scrollContent, "UseAllIngredientsToggle",
                this.L("Use All Ingredients"), this.netCookUseAllIngredients,
                new System.Action<bool>(this.OnUguiFeaturesMassCookUseAllIngredientsToggled));
            // Mod-only row (no IMGUI twin): fills the slots real ingredients could not cover with the
            // Universal Ingredient. Own full-width row so its longer label is not clipped.
            handle.UseUniversalIngredientToggle = this.CreateUguiCheckbox(scrollContent, "UseUniversalIngredientToggle",
                this.L("Use Universal Ingredient"), this.netCookUseUniversalIngredient,
                new System.Action<bool>(this.OnUguiFeaturesMassCookUseUniversalIngredientToggled));

            handle.DishLimitLabel = this.CreateUguiLabel(scrollContent, "DishLimitLabel",
                this.L("DISH LIMIT (0 = unlimited)"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(handle.DishLimitLabel);
            handle.DishMaxShown = this.netCookMaxCookQuantity > 0
                ? ("Ingredients max: " + this.netCookMaxCookQuantity)
                : "Ingredients max: —";
            handle.DishMaxLabel = this.CreateUguiLabel(scrollContent, "DishMaxLabel",
                handle.DishMaxShown, 12f, Color.white, false); // source right-aligns; left at the right column here

            handle.QtyApplied = this.netCookCookQuantityInput ?? "1";
            handle.QtyField = this.CreateUguiInputField(scrollContent, "QtyField",
                handle.QtyApplied, 6,
                new System.Action<string>(this.OnUguiFeaturesMassCookQtyChanged));

            // -------- Start/Stop pair (:336-350 — file header) --------
            handle.StartShown = this.L("START MASS COOK");
            handle.StartButton = this.CreateUguiPrimaryButton(scrollContent, "StartButton",
                handle.StartShown, new System.Action(this.OnUguiFeaturesMassCookStartStopClicked));
            handle.StopShown = this.L("STOP MASS COOK");
            handle.StopButton = this.CreateUguiDangerButton(scrollContent, "StopButton",
                handle.StopShown, new System.Action(this.OnUguiFeaturesMassCookStartStopClicked));

            // -------- Cook delay card (placed by the relayout, above the recipe block) --------
            // Unconditional on purpose: the delay paces mass cook (the assist loop runs its own
            // delays), and it must stay reachable whether the assist card is shown or not.
            handle.DelayCard = this.CreateUguiGo("DelayCard", scrollContent);
            this.AddUguiImage(handle.DelayCard, this.UguiKitPanelBg(), true, 1f);

            GameObject delayLabel = this.CreateUguiLabel(handle.DelayCard.transform, "DelayLabel",
                this.L("COOK DELAY"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(delayLabel);
            PlaceUguiTopLeft(delayLabel, 12f, 10f, sliderCardW * 0.55f, 18f);
            handle.DelayShown = string.Format("{0:F2}s", this.netCookInterval);
            handle.DelayValueLabel = this.CreateUguiLabel(handle.DelayCard.transform, "DelayValue",
                handle.DelayShown, 12f, Color.white, false);
            PlaceUguiTopLeft(handle.DelayValueLabel, 12f + sliderCardW * 0.55f, 10f, sliderCardW * 0.45f, 18f);
            handle.DelaySlider = this.CreateUguiSlider(handle.DelayCard.transform, "DelaySlider",
                0.25f, 10f, this.netCookInterval, false,
                new System.Action<float>(this.OnUguiFeaturesMassCookDelayChanged));
            PlaceUguiTopLeft(handle.DelaySlider.gameObject, 12f, 30f, sliderCardW, 20f);

            // -------- Status card (:379-402 — 118 tall; children card-local) --------
            handle.StatusCard = this.CreateUguiGo("StatusCard", scrollContent);
            this.AddUguiImage(handle.StatusCard, this.UguiKitPanelBg(), true, 1f);

            GameObject statusTitle = this.CreateUguiLabel(handle.StatusCard.transform, "StatusTitle",
                this.L("STATUS"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(statusTitle);
            PlaceUguiTopLeft(statusTitle, 12f, 8f, rowW - 24f, 18f);

            float statW = (rowW - 36f) / 2f; // :386
            GameObject stovesBox = this.CreateUguiGo("StovesBox", handle.StatusCard.transform);
            PlaceUguiTopLeft(stovesBox, 12f, 32f, statW, 42f);
            this.AddUguiImage(stovesBox, this.UguiKitControlFill(), true, 1f);
            GameObject stovesCaption = this.CreateUguiLabel(stovesBox.transform, "Caption",
                this.L("STOVES"), 10f, mutedTextColor, true);
            this.TrySetUguiLabelBold(stovesCaption);
            PlaceUguiTopLeft(stovesCaption, 0f, 4f, statW, 16f);
            handle.StovesShown = this.netCookTargets.Count.ToString();
            handle.StovesValueLabel = this.CreateUguiLabel(stovesBox.transform, "Value",
                handle.StovesShown, 13f, Color.white, true);
            this.TrySetUguiLabelBold(handle.StovesValueLabel);
            PlaceUguiTopLeft(handle.StovesValueLabel, 0f, 20f, statW, 18f);

            GameObject sentBox = this.CreateUguiGo("SentBox", handle.StatusCard.transform);
            PlaceUguiTopLeft(sentBox, 12f + statW + 12f, 32f, statW, 42f);
            this.AddUguiImage(sentBox, this.UguiKitControlFill(), true, 1f);
            GameObject sentCaption = this.CreateUguiLabel(sentBox.transform, "Caption",
                this.L("SENT"), 10f, mutedTextColor, true);
            this.TrySetUguiLabelBold(sentCaption);
            PlaceUguiTopLeft(sentCaption, 0f, 4f, statW, 16f);
            handle.SentShown = this.netCookSentCount.ToString();
            handle.SentValueLabel = this.CreateUguiLabel(sentBox.transform, "Value",
                handle.SentShown, 13f, Color.white, true);
            this.TrySetUguiLabelBold(handle.SentValueLabel);
            PlaceUguiTopLeft(handle.SentValueLabel, 0f, 20f, statW, 18f);

            handle.StatusTextShown = this.BuildUguiFeaturesMassCookStatusText();
            handle.StatusTextLabel = this.CreateUguiLabel(handle.StatusCard.transform, "StatusText",
                handle.StatusTextShown, 12f, textColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusTextLabel);
            PlaceUguiTopLeft(handle.StatusTextLabel, 12f, 82f, rowW - 24f, 34f); // 28 in source — file header

            // Prime the state-swapped visuals, then lay out for the current state.
            this.SyncUguiFeaturesMassCookPill(handle);
            this.SyncUguiFeaturesMassCookEnableButtons(handle);
            handle.LayoutSignature = this.ComputeUguiFeaturesMassCookLayoutSignature(handle);
            this.RelayoutUguiShellFeaturesMassCook(handle);

            handle.Root = block;
            this.uguiShellFeaturesMassCook = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — positions everything from the conditional region down and sets the total
        // scroll height, mirroring the source's num accumulation (:210-402). Reposition/SetActive
        // only; per-frame text/value syncs stay in the processor.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellFeaturesMassCook(UguiShellFeaturesMassCookHandle handle)
        {
            const float rowX = 8f;
            float rowW = handle.ContentWidth - 16f;
            float halfW = (rowW - 10f) * 0.5f;
            bool assist = this.netCookAutoAssist;
            bool open = this.netCookRecipeDropdownOpen;
            float yCur = UguiMassCookConditionalTopY;

            bool stoveTypeVisible = this.ShouldShowNetCookCookerTypePicker();
            bool stoveTypeOpen = stoveTypeVisible && this.netCookCookerTypeDropdownOpen;

            // The assist card adds height above the mass-cook controls; it never replaces them.
            SetUguiGoActive(handle.AssistCard, assist);
            SetUguiGoActive(handle.StoveTypeLabel, stoveTypeVisible);
            SetUguiGoActive(handle.StoveTypeHeader, stoveTypeVisible);
            SetUguiGoActive(handle.StoveTypePanel, stoveTypeOpen);
            SetUguiGoActive(handle.RecipeLabel, true);
            SetUguiGoActive(handle.RecipeHeader, true);
            SetUguiGoActive(handle.RecipePanel, open);
            if (handle.MoveIngredientsToggle != null)
            {
                SetUguiGoActive(handle.MoveIngredientsToggle.gameObject, true);
            }
            if (handle.UseAllIngredientsToggle != null)
            {
                SetUguiGoActive(handle.UseAllIngredientsToggle.gameObject, true);
            }
            if (handle.UseUniversalIngredientToggle != null)
            {
                SetUguiGoActive(handle.UseUniversalIngredientToggle.gameObject, true);
            }
            SetUguiGoActive(handle.DishLimitLabel, true);
            SetUguiGoActive(handle.DishMaxLabel, true);
            if (handle.QtyField != null)
            {
                SetUguiGoActive(handle.QtyField.gameObject, true);
            }

            // Cook delay first and outside the card show/hide — the delay paces both loops.
            PlaceUguiTopLeft(handle.DelayCard, rowX, yCur, rowW, UguiMassCookSliderCardHeight);
            yCur += UguiMassCookSliderCardHeight + 8f;

            if (assist)
            {
                // :212-224 — card 36 + textH + 12; cursor += Ceil(cardH) + 12. The card sits between
                // the delay slider and the ingredient toggles — extra height, never a replacement.
                SetUguiGoActive(handle.AssistCard, true);
                float textH = handle.AssistTextHeight;
                float cardH = 36f + textH + 12f;
                PlaceUguiTopLeft(handle.AssistCard, rowX, yCur, rowW, cardH);
                PlaceUguiTopLeft(handle.AssistDescLabel, 12f, 28f, rowW - 24f, textH);
                yCur += Mathf.Ceil(cardH) + 12f;
            }

            // Every control sits ABOVE the two dropdowns, so the recipe grid is the last thing
            // before START and nothing the user has to reach for is hidden below a panel that can
            // be 300+ tall. This block runs whether assist is on or off.
            if (handle.MoveIngredientsToggle != null)
            {
                PlaceUguiTopLeft(handle.MoveIngredientsToggle.gameObject, rowX, yCur, halfW, 24f);
            }
            if (handle.UseAllIngredientsToggle != null)
            {
                PlaceUguiTopLeft(handle.UseAllIngredientsToggle.gameObject, rowX + halfW + 10f, yCur, halfW, 24f);
            }
            yCur += 30f;
            if (handle.UseUniversalIngredientToggle != null)
            {
                PlaceUguiTopLeft(handle.UseUniversalIngredientToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 38f;
            PlaceUguiTopLeft(handle.DishLimitLabel, rowX, yCur, rowW * 0.42f, 18f);
            PlaceUguiTopLeft(handle.DishMaxLabel, rowX + rowW * 0.58f, yCur, rowW * 0.42f, 18f);
            yCur += 20f;
            if (handle.QtyField != null)
            {
                PlaceUguiTopLeft(handle.QtyField.gameObject, rowX, yCur, rowW * 0.42f, 32f);
            }
            yCur += 42f;

            if (stoveTypeVisible)
            {
                PlaceUguiTopLeft(handle.StoveTypeLabel, rowX, yCur, rowW, 18f);
                yCur += 20f;
                PlaceUguiTopLeft(handle.StoveTypeHeader, rowX, yCur, rowW, 36f);
                yCur += 46f;
                if (stoveTypeOpen)
                {
                    float stoveTypePanelH = this.GetUguiFeaturesMassCookStoveTypePanelHeight();
                    PlaceUguiTopLeft(handle.StoveTypePanel, rowX, yCur - 6f, rowW, stoveTypePanelH);
                    PlaceUguiTopLeft(handle.StoveTypeListScroll, 4f, 4f, rowW - 8f, stoveTypePanelH - 8f);
                    yCur += stoveTypePanelH + 8f;
                }
            }

            PlaceUguiTopLeft(handle.RecipeLabel, rowX, yCur, rowW, 18f);
            yCur += 20f;
            PlaceUguiTopLeft(handle.RecipeHeader, rowX, yCur, rowW, 36f);
            yCur += 46f;
            if (open)
            {
                // :246 — panel at header yMax + 4 = yCur - 6; cursor += panel + 8.
                float recipePanelH = this.GetUguiFeaturesMassCookRecipePanelHeight();
                PlaceUguiTopLeft(handle.RecipePanel, rowX, yCur - 6f, rowW, recipePanelH);
                yCur += recipePanelH + 8f;
            }

            PlaceUguiTopLeft(handle.StartButton, rowX, yCur, rowW, 38f);
            PlaceUguiTopLeft(handle.StopButton, rowX, yCur, rowW, 38f);
            yCur += 52f;

            PlaceUguiTopLeft(handle.StatusCard, rowX, yCur, rowW, 118f);
            yCur += 132f;

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f); // :404 — return num + 20
        }

        private void RefreshUguiFeaturesMassCookLayout(UguiShellFeaturesMassCookHandle handle)
        {
            int signature = this.ComputeUguiFeaturesMassCookLayoutSignature(handle);
            if (signature != handle.LayoutSignature)
            {
                handle.LayoutSignature = signature;
                this.RelayoutUguiShellFeaturesMassCook(handle);
            }
        }

        // ----------------------------------------------------------------------------------------
        // State-swapped visuals (pill + the two style-flip button pairs) — file header
        // ----------------------------------------------------------------------------------------

        private void SyncUguiFeaturesMassCookPill(UguiShellFeaturesMassCookHandle handle)
        {
            int state = this.netCookEnabled ? 1 : 0;
            if (state == handle.PillShownState)
            {
                return;
            }
            handle.PillShownState = state;
            bool on = state == 1;
            try
            {
                if (handle.PillBg != null)
                {
                    handle.PillBg.color = on ? this.UguiKitAccent() : this.UguiKitControlFill();
                }
            }
            catch { }
            this.SetUguiLabelText(handle.PillLabel, on ? this.L("RUNNING") : this.L("READY"));
            this.SetUguiLabelColor(handle.PillLabel, on
                ? new Color(0.45f, 1f, 0.55f)
                : new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.78f));
        }

        private void SyncUguiFeaturesMassCookEnableButtons(UguiShellFeaturesMassCookHandle handle)
        {
            bool on = this.netCookEnabled;
            SetUguiGoActive(handle.ResetButtonDanger, on);
            SetUguiGoActive(handle.ResetButtonDefault, !on);
            SetUguiGoActive(handle.StopButton, on);
            SetUguiGoActive(handle.StartButton, !on);

            // Captions are fixed: the Start/Stop pair (and the Mass Cook hotkey) control mass cook
            // only. Auto Cook Assist is the checkbox above and never moves these captions.
            string startText = this.L("START MASS COOK");
            if (!string.Equals(startText, handle.StartShown, StringComparison.Ordinal))
            {
                handle.StartShown = startText;
                this.SetUguiButtonLabel(handle.StartButton, startText);
            }
            string stopText = this.L("STOP MASS COOK");
            if (!string.Equals(stopText, handle.StopShown, StringComparison.Ordinal))
            {
                handle.StopShown = stopText;
                this.SetUguiButtonLabel(handle.StopButton, stopText);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Pooled recipe rows (grow-on-demand, rebind-by-diff, deactivate-not-destroy) — bound
        // every gated frame while the panel is open (parity: :261 re-filters per repaint).
        // ----------------------------------------------------------------------------------------

        private UguiMassCookRecipeRowHandle CreateUguiFeaturesMassCookRecipeRow(
            UguiShellFeaturesMassCookHandle handle, int index, float innerW)
        {
            UguiMassCookRecipeRowHandle row = new UguiMassCookRecipeRowHandle();

            float cellW = UguiMassCookRecipeCellWidth(innerW);

            GameObject root = this.CreateUguiGo("Recipe" + index, handle.RecipeListContent);
            PlaceUguiTopLeft(root,
                (index % UguiMassCookRecipeColumns) * (cellW + UguiMassCookRecipeCellGap),
                (index / UguiMassCookRecipeColumns) * (UguiMassCookRecipeCellH + UguiMassCookRecipeCellGap),
                cellW, UguiMassCookRecipeCellH);
            row.Fill = this.AddUguiImage(root, this.UguiKitControlFill(), true, 1.5f);
            row.Fill.raycastTarget = true;
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = row.Fill;
            // Closure over the ROW HANDLE, not an index or list entry — the visible list is a
            // reused per-call buffer (file header; Food & Repair pooled-row idiom).
            UguiMassCookRecipeRowHandle captured = row;
            this.WireUguiClick(btn.onClick, new System.Action(
                () => this.OnUguiFeaturesMassCookRecipeRowClicked(captured)));

            // Dish icon, left gutter. Sprite arrives asynchronously from the game's own atlas
            // (same path the Bag/Warehouse grid uses), so the Image starts disabled and is only
            // switched on once a texture actually lands — an enabled Image with a null sprite
            // draws a white box.
            // Icon centred in the upper half of the tile. It starts disabled: the sprite arrives
            // asynchronously from the game's atlas, and an enabled Image with no sprite paints a
            // white square.
            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            row.Icon = this.AddUguiImage(iconGo, Color.white, false, 0f);
            row.Icon.raycastTarget = false;   // the whole tile is the button
            row.Icon.enabled = false;
            try { row.Icon.preserveAspect = true; } catch { }
            PlaceUguiTopLeft(iconGo, (cellW - 42f) * 0.5f, 8f, 42f, 42f);

            // Name under the icon, centred, wrapping to the two lines the tile has room for, and
            // anchored to the BOTTOM of its box. TrySetUguiLabelWrapped would have forced TopLeft
            // (overriding the centred flag above), which left short names hanging under the icon
            // while long ones reached the cell floor — a row of tiles with no shared baseline.
            row.Label = this.CreateUguiLabel(root.transform, "Name", "", 10f, this.UguiKitTextColor(), true);
            PlaceUguiTopLeft(row.Label, 2f, 52f, cellW - 4f, 36f);
            this.TrySetUguiLabelWrappedBottom(row.Label);

            row.Badge = this.CreateUguiLabel(root.transform, "Badge", this.L("RECENT"), 8f,
                this.UguiKitAccent(), true);
            PlaceUguiTopLeft(row.Badge, cellW - UguiMassCookRecipeBadgeWidth - 2f, 1f,
                UguiMassCookRecipeBadgeWidth, 14f);
            SetUguiGoActive(row.Badge, false);
            row.BadgeShown = false;

            row.Root = root;
            return row;
        }

        private const float UguiMassCookRecipeBadgeWidth = 46f;

        // Dish icons come from the same request-once game pipeline the Bag/Warehouse and Auto Sell
        // grids use, and land in the same texture store. A CookingRecipe's staticId IS an entity id
        // (CookingRecipe.name is TableData.GetEntity(staticId).name), so the item icon resolver
        // takes it unchanged — no separate recipe-art lookup exists or is needed.
        private bool TryGetNetCookRecipeTexture(int recipeId, out Texture2D texture)
        {
            texture = null;
            if (recipeId <= 0)
            {
                return false;
            }

            string key = "netcookrecipe:" + recipeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (this.autoSellBagItemTextures.TryGetValue(key, out texture) && texture != null)
            {
                return true;
            }

            // Miss: kick the async load once. Rows show the name alone until it lands.
            //
            // Not RequestGameItemIconByStaticId: that resolves through
            // RewardUtility.GetIconName, which routes by EntityType and returns null for cooking
            // recipes (measured: every recipe id came back unresolved). CookPanel does not use it
            // either. The game draws a dish with `new AtlasSpriteID(recipeDetail.staticId)`, whose
            // GetIconString calls TableData.GetIconId(id) directly, and that one always answers
            // (it falls back to id.ToString()). Same source, so the same sprite key.
            if (this.TryResolveNetCookRecipeIconName(recipeId, out string iconName))
            {
                this.RequestGameItemIconByIconName(iconName, key);
            }

            texture = null;
            return false;
        }

        // TableData.GetIconId(int) — static, one arg, returns the sprite name for any entity id.
        private unsafe bool TryResolveNetCookRecipeIconName(int recipeId, out string iconName)
        {
            iconName = string.Empty;
            if (recipeId <= 0)
            {
                return false;
            }

            if (this.netCookRecipeIconNames.TryGetValue(recipeId, out string cached))
            {
                iconName = cached ?? string.Empty;
                return !string.IsNullOrEmpty(iconName);
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                // TableData sits in EcsClient with NO namespace, which rules out the two convenience
                // helpers: FindAuraMonoClassByFullName has nothing to split, and
                // FindAuraMonoClassAcrossLoadedAssemblies rejects an empty nameSpace on its first
                // line. Go through the image the way DailyQuestSubmitFeature does.
                IntPtr tableDataClass = IntPtr.Zero;
                IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                    if (tableDataClass == IntPtr.Zero)
                    {
                        tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                    }
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassByFullName("EcsClient.TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    if (this.netCookIconDiag.Add(recipeId))
                    {
                        ModLogger.Msg("[NetCookIcon] " + recipeId + ": TableData class not resolved.");
                    }
                    return false;
                }

                // Arity 1 picks GetIconId(int); the arity-3 overload takes an EntityType and is the
                // one whose routing fails for recipes.
                IntPtr getIconIdMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetIconId", 1);
                if (getIconIdMethod == IntPtr.Zero)
                {
                    if (this.netCookIconDiag.Add(recipeId))
                    {
                        ModLogger.Msg("[NetCookIcon] " + recipeId + ": GetIconId(1) not found; probing other arities: "
                            + "a0=" + (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetIconId", 0) != IntPtr.Zero)
                            + " a2=" + (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetIconId", 2) != IntPtr.Zero)
                            + " a3=" + (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetIconId", 3) != IntPtr.Zero)
                            + " | GetEntity(1)=" + (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 1) != IntPtr.Zero));
                    }
                    return false;
                }

                int id = recipeId;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                IntPtr nameObj = auraMonoRuntimeInvoke(getIconIdMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                string raw = string.Empty;
                bool readOk = nameObj != IntPtr.Zero && this.TryReadMonoString(nameObj, out raw);
                if (exc != IntPtr.Zero || !readOk || string.IsNullOrWhiteSpace(raw))
                {
                    if (this.netCookIconDiag.Add(recipeId))
                    {
                        ModLogger.Msg("[NetCookIcon] " + recipeId + ": GetIconId invoked but no name"
                            + " (exc=" + (exc != IntPtr.Zero) + " nullRet=" + (nameObj == IntPtr.Zero)
                            + " readOk=" + readOk + ").");
                    }
                    // Negative result cached too: a recipe with no icon must not re-invoke every
                    // frame the dropdown redraws.
                    this.netCookRecipeIconNames[recipeId] = string.Empty;
                    return false;
                }

                iconName = raw.Trim();
                this.netCookRecipeIconNames[recipeId] = iconName;
                if (this.netCookIconDiag.Add(recipeId))
                {
                    ModLogger.Msg("[NetCookIcon] " + recipeId + ": iconName='" + iconName + "'");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (this.netCookIconDiag.Add(recipeId))
                {
                    ModLogger.Msg("[NetCookIcon] " + recipeId + ": " + ex.GetType().Name + ": " + ex.Message);
                }
                this.netCookRecipeIconNames[recipeId] = string.Empty;
                return false;
            }
        }

        private readonly Dictionary<int, string> netCookRecipeIconNames = new Dictionary<int, string>();
        private readonly HashSet<int> netCookIconDiag = new HashSet<int>();

        // Candidates for the slot the picker is parked on, shaped like the recipe entries so the
        // grid does not need a second code path. Re-read on a short interval rather than per sync:
        // the bag changes while the panel is open so a long-lived cache would offer items that are
        // gone, but the read is an AuraMono invoke that also re-inits the shared recipe detail, and
        // this panel repaints EVERY frame. The label list below is rebuilt per call regardless —
        // that part is free and keeps the search box responsive.
        private readonly List<KeyValuePair<int, string>> netCookSlotCandidateEntries = new List<KeyValuePair<int, string>>(32);
        private readonly List<NetCookSlotCandidate> netCookSlotCandidateBuffer = new List<NetCookSlotCandidate>(32);
        private const float NetCookSlotCandidateRefreshSeconds = 0.5f;
        private float nextNetCookSlotCandidateRefreshAt = 0f;
        private int netCookSlotCandidateBufferSlot = -1;
        private int netCookSlotCandidateBufferRecipe = 0;

        private void EnsureUguiMassCookSlotCandidates()
        {
            // A different slot or dish is a different question — answer it now, never on the next
            // interval, or the grid would show the previous slot's items for half a second.
            bool targetChanged = this.netCookSlotCandidateBufferSlot != this.netCookSlotPickerIndex
                || this.netCookSlotCandidateBufferRecipe != this.netCookRecipeId;
            if (!targetChanged && Time.unscaledTime < this.nextNetCookSlotCandidateRefreshAt)
            {
                return;
            }

            bool targetMoved = targetChanged;
            this.netCookSlotCandidateBufferSlot = this.netCookSlotPickerIndex;
            this.netCookSlotCandidateBufferRecipe = this.netCookRecipeId;
            this.nextNetCookSlotCandidateRefreshAt = Time.unscaledTime + NetCookSlotCandidateRefreshSeconds;
            if (!this.TryListNetCookSlotCandidates(this.netCookRecipeId, this.netCookSlotPickerIndex,
                    this.netCookSlotCandidateBuffer, out string candidateStatus))
            {
                this.netCookSlotCandidateBuffer.Clear();
                // An empty picker is the one failure the player sees and cannot explain, and the
                // reason was being dropped on the floor with `out _`. Log it once per slot rather
                // than per refresh — this runs twice a second for as long as the picker is open.
                if (targetMoved)
                {
                    this.NetCookLog("slot " + this.netCookSlotPickerIndex + " candidates empty for recipe "
                        + this.netCookRecipeId + " (moveIngredients=" + this.netCookMoveIngredients
                        + "): " + candidateStatus);
                }
            }
        }

        private List<KeyValuePair<int, string>> GetUguiMassCookSlotCandidateEntries()
        {
            this.netCookSlotCandidateEntries.Clear();
            if (this.netCookSlotPickerIndex < 0)
            {
                return this.netCookSlotCandidateEntries;
            }

            this.EnsureUguiMassCookSlotCandidates();
            if (this.netCookSlotCandidateBuffer.Count <= 0)
            {
                return this.netCookSlotCandidateEntries;
            }

            string search = (this.netCookRecipeSearchText ?? string.Empty).Trim();
            for (int i = 0; i < this.netCookSlotCandidateBuffer.Count; i++)
            {
                NetCookSlotCandidate c = this.netCookSlotCandidateBuffer[i];
                string label = c.Name;
                if (c.StarRate > 0)
                {
                    label += " " + c.StarRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "*";
                }
                if (c.Count > 0)
                {
                    label += " x" + c.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Bag stock reads "xN", warehouse stock "+N" — two counts on a 80px tile leave no
                // room for a word, and the pair is the same everywhere the mod shows both stores.
                if (c.WarehouseCount > 0)
                {
                    label += " +" + c.WarehouseCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (search.Length > 0 && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                this.netCookSlotCandidateEntries.Add(new KeyValuePair<int, string>(c.StaticId, label));
            }

            return this.netCookSlotCandidateEntries;
        }

        // "Only What I Can Cook" lives HERE and not in GetVisibleNetCookRecipeEntries, because that
        // list is also the oracle the capture path and the Stove Type switch consult to decide which
        // recipe stays selected. Filtering it there would let a stock shortage silently overwrite
        // the recipe the user picked for a menu. Hiding rows is a view concern, so the view owns it.
        private readonly List<KeyValuePair<int, string>> netCookCookableVisibleEntries = new List<KeyValuePair<int, string>>(256);

        private List<KeyValuePair<int, string>> GetUguiMassCookRecipeGridEntries()
        {
            List<KeyValuePair<int, string>> visible = this.GetVisibleNetCookRecipeEntries();
            if (!this.netCookCookableOnly)
            {
                return visible;
            }

            // Measuring is budgeted per frame; until the first sweep has been all the way round,
            // show the list unfiltered rather than dropping rows a few at a time.
            this.RefreshNetCookCookableCache(visible);
            if (!this.IsNetCookCookableFilterReady())
            {
                return visible;
            }

            this.netCookCookableVisibleEntries.Clear();
            for (int i = 0; i < visible.Count; i++)
            {
                if (this.IsNetCookRecipeCookable(visible[i].Key))
                {
                    this.netCookCookableVisibleEntries.Add(visible[i]);
                }
            }

            return this.netCookCookableVisibleEntries;
        }

        private void SyncUguiFeaturesMassCookRecipeRows(UguiShellFeaturesMassCookHandle handle)
        {
            // Two data sources through one grid: recipes normally, and a slot's candidate items
            // while the picker is parked on a slot. Same cells, same icons, same selection diff —
            // only the list and what a click means change.
            List<KeyValuePair<int, string>> visible = this.netCookSlotPickerIndex >= 0
                ? this.GetUguiMassCookSlotCandidateEntries()
                : this.GetUguiMassCookRecipeGridEntries();
            int count = visible.Count;
            float innerW = UguiMassCookGridInnerWidth(handle);

            for (int i = 0; i < count; i++)
            {
                if (i >= handle.RecipeRows.Count)
                {
                    handle.RecipeRows.Add(this.CreateUguiFeaturesMassCookRecipeRow(handle, i, innerW));
                }
                UguiMassCookRecipeRowHandle row = handle.RecipeRows[i];
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }

                KeyValuePair<int, string> entry = visible[i];
                // :287 — the row LABEL gets the blank-fallback; the raw Value is kept for the
                // pick-status string (:285 uses it unfallbacked).
                string display = string.IsNullOrWhiteSpace(entry.Value)
                    ? ("Recipe " + entry.Key)
                    : entry.Value;
                if (row.BoundId != entry.Key
                    || !string.Equals(row.BoundDisplay, display, StringComparison.Ordinal))
                {
                    row.BoundId = entry.Key;
                    row.BoundValue = entry.Value;
                    row.BoundDisplay = display;
                    this.SetUguiLabelText(row.Label, display);
                }

                // Icon: diffed on texture identity, not presence — the async load replaces the
                // texture for an already-bound row, and re-assigning a Sprite every frame would
                // allocate a wrapper per frame for every visible row.
                Texture2D rowTex;
                this.TryGetNetCookRecipeTexture(entry.Key, out rowTex);
                int rowTexId = (rowTex != null) ? rowTex.GetInstanceID() : 0;
                if (rowTexId != row.IconTexId)
                {
                    row.IconTexId = rowTexId;
                    if (row.Icon != null)
                    {
                        if (rowTex != null)
                        {
                            row.Icon.sprite = this.GetOrCreateUguiTransferSprite(rowTex);
                            row.Icon.enabled = row.Icon.sprite != null;
                        }
                        else
                        {
                            row.Icon.sprite = null;
                            row.Icon.enabled = false;
                        }
                    }
                }

                // "Recent" is a property of recipes; item candidates have no such notion.
                bool isRecent = this.netCookSlotPickerIndex < 0
                    && this.netCookRecentRecipeIds.Contains(entry.Key);
                if (isRecent != row.BadgeShown)
                {
                    row.BadgeShown = isRecent;
                    SetUguiGoActive(row.Badge, isRecent);
                }

                // Selection diffs per bind (an IMGUI-twin pick or capture auto-select moves it).
                // In slot-picker mode the highlighted cell is the slot's preferred ITEM,
                // not the selected recipe.
                bool selected = this.netCookSlotPickerIndex >= 0
                    ? entry.Key == this.GetNetCookSlotPreference(this.netCookRecipeId, this.netCookSlotPickerIndex)
                    : entry.Key == this.netCookRecipeId;
                if (selected != row.SelectedShown)
                {
                    row.SelectedShown = selected;
                    try
                    {
                        if (row.Fill != null)
                        {
                            row.Fill.color = selected ? this.UguiKitAccent() : this.UguiKitControlFill();
                        }
                    }
                    catch { }
                    this.SetUguiLabelColor(row.Label, selected
                        ? this.GetUiTextOnAccent(this.UguiKitAccent())
                        : this.UguiKitTextColor());
                }
            }

            for (int i = count; i < handle.RecipeRows.Count; i++)
            {
                UguiMassCookRecipeRowHandle row = handle.RecipeRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }

            this.SyncUguiFeaturesMassCookSlotRow(handle);
            SetUguiGoActive(handle.RecipeEmptyLabel, count <= 0); // :267-270
            int gridRows = (count + UguiMassCookRecipeColumns - 1) / UguiMassCookRecipeColumns;
            this.SetUguiScrollContentHeight(handle.RecipeListContent,
                Mathf.Max(1f, gridRows * (UguiMassCookRecipeCellH + UguiMassCookRecipeCellGap)));
        }

        private void ResetUguiFeaturesMassCookRecipeListScroll(UguiShellFeaturesMassCookHandle handle)
        {
            try
            {
                if (handle.RecipeListContent != null)
                {
                    RectTransform rt = handle.RecipeListContent.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
                    }
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Pooled STOVE TYPE rows — row 0 is "Auto", the rest mirror the capture census
        // (netCookScannedCookerTypes). Rebound only when the census version or the pick moves.
        // ----------------------------------------------------------------------------------------

        private UguiMassCookStoveTypeRowHandle CreateUguiFeaturesMassCookStoveTypeRow(
            UguiShellFeaturesMassCookHandle handle, int index, float innerW)
        {
            UguiMassCookStoveTypeRowHandle row = new UguiMassCookStoveTypeRowHandle();

            GameObject root = this.CreateUguiGo("StoveType" + index, handle.StoveTypeListContent);
            PlaceUguiTopLeft(root, 0f, index * UguiMassCookStoveTypeRowStep, innerW, 24f);
            row.Fill = this.AddUguiImage(root, this.UguiKitControlFill(), true, 1.5f);
            row.Fill.raycastTarget = true;
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = row.Fill;
            UguiMassCookStoveTypeRowHandle captured = row;
            this.WireUguiClick(btn.onClick, new System.Action(
                () => this.OnUguiFeaturesMassCookStoveTypeRowClicked(captured)));

            row.Label = this.CreateUguiLabel(root.transform, "Name", "", 11f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(row.Label);
            PlaceUguiTopLeft(row.Label, 8f, 1f, innerW - 16f, 22f);

            row.Root = root;
            return row;
        }

        private void SyncUguiFeaturesMassCookStoveTypeRows(UguiShellFeaturesMassCookHandle handle)
        {
            int count = this.GetUguiFeaturesMassCookStoveTypeRowCount();
            float innerW = handle.ContentWidth - 16f - 8f - 22f;
            int selected = this.netCookPreferredCookerType; // 0 selects the Auto row

            for (int i = 0; i < count; i++)
            {
                if (i >= handle.StoveTypeRows.Count)
                {
                    handle.StoveTypeRows.Add(this.CreateUguiFeaturesMassCookStoveTypeRow(handle, i, innerW));
                }
                UguiMassCookStoveTypeRowHandle row = handle.StoveTypeRows[i];
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }

                int boundType;
                string display;
                if (i == 0)
                {
                    boundType = 0;
                    display = this.L("Auto (most stoves)");
                }
                else
                {
                    NetCookCookerTypeGroup group = this.netCookScannedCookerTypes[i - 1];
                    boundType = group.RecipeCookerType;
                    display = this.GetNetCookCookerTypeGroupLabel(group);
                    if (group.NearestDistance >= 0f)
                    {
                        display += "  ·  " + group.NearestDistance.ToString("F1") + "m";
                    }
                }

                if (row.BoundRecipeCookerType != boundType
                    || !string.Equals(row.BoundDisplay, display, StringComparison.Ordinal))
                {
                    row.BoundRecipeCookerType = boundType;
                    row.BoundDisplay = display;
                    this.SetUguiLabelText(row.Label, display);
                }

                bool isSelected = boundType == selected;
                if (isSelected != row.SelectedShown)
                {
                    row.SelectedShown = isSelected;
                    try
                    {
                        if (row.Fill != null)
                        {
                            row.Fill.color = isSelected ? this.UguiKitAccent() : this.UguiKitControlFill();
                        }
                    }
                    catch { }
                    this.SetUguiLabelColor(row.Label, isSelected
                        ? this.GetUiTextOnAccent(this.UguiKitAccent())
                        : this.UguiKitTextColor());
                }
            }

            for (int i = count; i < handle.StoveTypeRows.Count; i++)
            {
                UguiMassCookStoveTypeRowHandle row = handle.StoveTypeRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }

            this.SetUguiScrollContentHeight(handle.StoveTypeListContent,
                Mathf.Max(1f, count * UguiMassCookStoveTypeRowStep));
            handle.StoveTypeCensusVersionShown = this.netCookScannedCookerTypesVersion;
            handle.StoveTypeSelectionShown = selected;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesMassCookOnUpdate()
        {
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesMassCookSubIndex))
            {
                return;
            }

            try
            {
                // Busy gates — re-evaluated every gated frame (:94-97 is time-dependent).
                bool captureBusy = this.netCookCaptureInProgress
                    || this.netCookCaptureCoroutine != null
                    || Time.unscaledTime < this.nextNetCookCaptureAllowedAt;
                this.SetUguiButtonInteractable(handle.CaptureButton, !captureBusy);
                this.SetUguiButtonInteractable(handle.CleanupButton, this.netCookCleanupCoroutine == null);

                // Pill + the two style-flip pairs (netCookEnabled also moves from hotkeys/stops).
                this.SyncUguiFeaturesMassCookPill(handle);
                this.SyncUguiFeaturesMassCookEnableButtons(handle);

                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoAssistToggle, this.netCookAutoAssist);
                this.SyncUguiToggleFromField(handle.RememberStovesToggle, this.netCookRememberStoves);
                this.SyncUguiToggleFromField(handle.CaptureOwnToggle, this.netCookCaptureOwnOnly);
                this.SyncUguiToggleFromField(handle.CaptureRadiusToggle, this.netCookCaptureRadiusOnly);
                this.SyncUguiToggleFromField(handle.StatusDiagToggle, this.netCookStatusDiagEnabled);
                this.SyncUguiToggleFromField(handle.ManualIngredientsToggle, this.netCookSlotManualMode);
                this.SyncUguiToggleFromField(handle.CookableOnlyToggle, this.netCookCookableOnly);
                this.SyncUguiToggleFromField(handle.MoveIngredientsToggle, this.netCookMoveIngredients);
                this.SyncUguiToggleFromField(handle.UseAllIngredientsToggle, this.netCookUseAllIngredients);
                this.SyncUguiToggleFromField(handle.UseUniversalIngredientToggle, this.netCookUseUniversalIngredient);

                // Assist-card measure retry (built-inactive TMP caveat — file header), independent of
                // the recipe block below: with assist on the card is EXTRA height, not a branch. The
                // description is constant, so this stops as soon as one measure succeeds, and the
                // layout signature (assist bit + height bits) triggers the relayout.
                if (this.netCookAutoAssist && !handle.AssistTextMeasured)
                {
                    bool measured;
                    float measuredH = this.MeasureUguiPicturesWrappedHeight(handle.AssistDescLabel,
                        "Handles cooking mini-game prompts and auto-collects finished food. It will not prepare or start cooking.",
                        handle.ContentWidth - 16f - 24f, 32f, out measured);
                    if (measured)
                    {
                        handle.AssistTextMeasured = true;
                        handle.AssistTextHeight = Mathf.Max(32f, measuredH);
                    }
                }

                // STOVE TYPE / RECIPE / dish-limit processing — always runs, assist on or off. The
                // braces are a plain scope (parity of the old else branch), not a branch.
                {
                    // STOVE TYPE picker — caption/arrow live; rows rebind only when the census
                    // version or the pick moves (a background expansion can add a type mid-open).
                    if (this.ShouldShowNetCookCookerTypePicker())
                    {
                        this.SyncUguiSelfLabelText(handle.StoveTypeHeaderValue, ref handle.StoveTypeHeaderShown,
                            this.GetNetCookSelectedCookerTypeLabel());
                        this.SyncUguiSelfLabelText(handle.StoveTypeArrow, ref handle.StoveTypeArrowShown,
                            this.netCookCookerTypeDropdownOpen ? "^" : "v");
                        if (this.netCookCookerTypeDropdownOpen
                            && (handle.StoveTypeCensusVersionShown != this.netCookScannedCookerTypesVersion
                                || handle.StoveTypeSelectionShown != this.netCookPreferredCookerType))
                        {
                            this.SyncUguiFeaturesMassCookStoveTypeRows(handle);
                        }
                    }
                    else if (this.netCookCookerTypeDropdownOpen)
                    {
                        // The census shrank to a single type under an open panel — close it so the
                        // relayout does not leave an orphaned box behind the recipe row.
                        this.netCookCookerTypeDropdownOpen = false;
                    }

                    // :228 — per-repaint in source; self-caching after the first success.
                    this.EnsureNetCookRecipeCache();

                    // Header caption + arrow (capture can auto-select a recipe — :239 is live).
                    this.SyncUguiSelfLabelText(handle.RecipeHeaderValue, ref handle.RecipeHeaderShown,
                        this.GetNetCookSelectedRecipeLabel());
                    this.SyncUguiSelfLabelText(handle.RecipeArrow, ref handle.RecipeArrowShown,
                        this.netCookRecipeDropdownOpen ? "^" : "v");

                    // Search field poll pair (Teleport-NPC idiom): a missed onValueChanged lands
                    // via the first branch; an IMGUI-twin edit of the shared field via the second.
                    InputField searchField = handle.RecipeSearchField;
                    if (searchField != null)
                    {
                        string uiText = searchField.text ?? string.Empty;
                        if (!string.Equals(uiText, handle.RecipeSearchApplied, StringComparison.Ordinal))
                        {
                            this.ApplyUguiFeaturesMassCookRecipeSearch(handle, uiText);
                        }
                        else
                        {
                            string fieldText = this.netCookRecipeSearchText ?? string.Empty;
                            if (!string.Equals(fieldText, handle.RecipeSearchApplied, StringComparison.Ordinal))
                            {
                                handle.RecipeSearchApplied = fieldText;
                                try { searchField.SetTextWithoutNotify(fieldText); } catch { }
                                // External edit — the shared cascade already ran (or is IMGUI's own);
                                // just snap our list to the top like the source's scroll reset.
                                this.ResetUguiFeaturesMassCookRecipeListScroll(handle);
                            }
                        }
                    }

                    // Row bind every gated frame while open — parity with :261 (also catches
                    // cooker-type/cache changes from background captures and selection moves).
                    if (this.netCookRecipeDropdownOpen)
                    {
                        this.SyncUguiFeaturesMassCookRecipeRows(handle);
                    }

                    // :315-321 — per-frame refresh (self-throttled) + the — fallback at <= 0.
                    this.RefreshNetCookMaxCookQuantity();
                    this.SyncUguiSelfLabelText(handle.DishMaxLabel, ref handle.DishMaxShown,
                        this.netCookMaxCookQuantity > 0
                            ? ("Ingredients max: " + this.netCookMaxCookQuantity)
                            : "Ingredients max: —");

                    // Quantity poll pair — second branch also pushes SyncNetCookCookQuantityFromInput's
                    // normalization back into the field (IMGUI next-repaint snap analog).
                    InputField qtyField = handle.QtyField;
                    if (qtyField != null)
                    {
                        string uiText = qtyField.text ?? string.Empty;
                        if (!string.Equals(uiText, handle.QtyApplied, StringComparison.Ordinal))
                        {
                            this.ApplyUguiFeaturesMassCookQty(handle, uiText);
                        }
                        else
                        {
                            string fieldText = this.netCookCookQuantityInput ?? string.Empty;
                            if (!string.Equals(fieldText, handle.QtyApplied, StringComparison.Ordinal))
                            {
                                handle.QtyApplied = fieldText;
                                try { qtyField.SetTextWithoutNotify(fieldText); } catch { }
                            }
                        }
                    }
                }

                // Slider re-syncs (external IMGUI edits) + value labels. The delay compare uses
                // the save epsilon; with the fields grid-snapped (0.01 / whole) a matching slider
                // stays put and a drag snaps to the grid like the IMGUI twin redraws it.
                if (handle.DelaySlider != null
                    && Mathf.Abs(handle.DelaySlider.value - this.netCookInterval) > 0.0001f)
                {
                    handle.DelaySlider.SetValueWithoutNotify(this.netCookInterval);
                }
                this.SyncUguiSelfLabelText(handle.DelayValueLabel, ref handle.DelayShown,
                    string.Format("{0:F2}s", this.netCookInterval));
                if (handle.RadiusSlider != null
                    && Mathf.Abs(handle.RadiusSlider.value - this.netCookScanRadiusMeters) > 0.0001f)
                {
                    handle.RadiusSlider.SetValueWithoutNotify(this.netCookScanRadiusMeters);
                }
                this.SyncUguiSelfLabelText(handle.RadiusValueLabel, ref handle.RadiusShown,
                    string.Format("{0:F0}m", this.netCookScanRadiusMeters));

                // Status card — LIVE stats + the fallback-vs-live status text (file header).
                this.SyncUguiSelfLabelText(handle.StovesValueLabel, ref handle.StovesShown,
                    this.netCookTargets.Count.ToString());
                this.SyncUguiSelfLabelText(handle.SentValueLabel, ref handle.SentShown,
                    this.netCookSentCount.ToString());
                this.SyncUguiSelfLabelText(handle.StatusTextLabel, ref handle.StatusTextShown,
                    this.BuildUguiFeaturesMassCookStatusText());

                // Conditional-layout signature (branch, dropdown-open, assist height).
                this.RefreshUguiFeaturesMassCookLayout(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Mass Cook content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // ----------------------------------------------------------------------------------------

        // :98-116 — the branching capture toast; "expanding" is checked AFTER the call.
        private void OnUguiFeaturesMassCookCaptureClicked()
        {
            try
            {
                if (this.RequestNetCookCapture(out bool captureQueued, out string requestStatus))
                {
                    bool expandingCapture = this.netCookCaptureCoroutine != null;
                    string captureNotice = expandingCapture
                        ? "Expanding stove capture..."
                        : this.netCookStatus;
                    if (string.IsNullOrWhiteSpace(captureNotice))
                    {
                        captureNotice = "Mass cook stoves captured";
                    }
                    this.AddMenuNotification(captureNotice,
                        expandingCapture ? new Color(1f, 0.85f, 0.45f) : new Color(0.45f, 1f, 0.55f));
                }
                else if (captureQueued)
                {
                    // Not a failure — the click is remembered and runs by itself once the world is up.
                    this.AddMenuNotification(
                        string.IsNullOrWhiteSpace(requestStatus) ? "Capture queued." : requestStatus,
                        new Color(1f, 0.85f, 0.45f));
                }
                else
                {
                    this.AddMenuNotification(this.netCookStatus ?? "Capture failed.", new Color(1f, 0.55f, 0.55f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook capture error: " + ex.Message);
            }
        }

        // :119-123 — always enabled; fixed amber toast.
        private void OnUguiFeaturesMassCookResetClicked()
        {
            try
            {
                this.ResetNetCookCaptureContext("Captured stoves reset. Capture stoves again.");
                this.AddMenuNotification(this.L("Mass cook captured stoves reset"), new Color(1f, 0.75f, 0.45f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook reset error: " + ex.Message);
            }
        }

        // :126-133.
        private void OnUguiFeaturesMassCookCleanupClicked()
        {
            try
            {
                this.StartNetCookCleanupSweep();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook cleanup error: " + ex.Message);
            }
        }

        // Auto Cook Assist toggle. Independent of mass cook: it does not start or stop anything, does
        // not touch the Start/Stop pair, and does not add stoves to netCookTargets. Off drops the
        // watch list so a stale phase-3 assist object cannot send ContinueCooking on the next enable;
        // the next enable rebuilds it fresh through SyncNetCookAssistWatchList. The equal-guard is the
        // UGUI analog of IMGUI's prev-vs-new check.
        private void OnUguiFeaturesMassCookAutoAssistToggled(bool value)
        {
            if (value == this.netCookAutoAssist)
            {
                return;
            }

            this.netCookAutoAssist = value;
            this.netCookRecipeDropdownOpen = false;
            this.netCookCookerTypeDropdownOpen = false;
            if (!this.netCookAutoAssist)
            {
                this.ClearNetCookAssistWatchList();
                if (!this.netCookEnabled)
                {
                    this.netCookStatus = "Auto cook assist off.";
                }
            }
            else if (!this.netCookEnabled)
            {
                this.netCookStatus = this.netCookTargets.Count > 0 || this.netCookScannedTargets.Count > 0
                    ? "Auto cook assist watching captured stoves."
                    : "Capture stoves to assist active cooking.";
            }

            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle != null && handle.Root != null)
            {
                this.RefreshUguiFeaturesMassCookLayout(handle); // click-responsive card show/hide
            }
        }

        // :148-158.
        private void OnUguiFeaturesMassCookRememberStovesToggled(bool value)
        {
            if (value == this.netCookRememberStoves)
            {
                return;
            }
            this.netCookRememberStoves = value;
            this.netCookStatus = this.netCookRememberStoves
                ? "Permanent Stove Memory ON: captured stoves are reused on every start (no re-scan). Use Reset Capture to forget."
                : "Permanent Stove Memory OFF: each start re-scans nearby stoves.";
            try { this.SaveKeybinds(false); } catch { }
        }

        // :160-170.
        private void OnUguiFeaturesMassCookCaptureOwnToggled(bool value)
        {
            if (value == this.netCookCaptureOwnOnly)
            {
                return;
            }
            this.netCookCaptureOwnOnly = value;
            this.netCookStatus = this.netCookCaptureOwnOnly
                ? "Capture Own ON: only stoves inside your own field/plot are captured."
                : "Capture Own OFF: stoves are captured regardless of plot owner.";
            try { this.SaveKeybinds(false); } catch { }
        }

        // :172-182.
        private void OnUguiFeaturesMassCookCaptureRadiusToggled(bool value)
        {
            if (value == this.netCookCaptureRadiusOnly)
            {
                return;
            }
            this.netCookCaptureRadiusOnly = value;
            this.netCookStatus = this.netCookCaptureRadiusOnly
                ? "Capture Radius ON: capture uses only the live radius scan (session registry ignored)."
                : "Capture Radius OFF: capture may reuse the session stove registry.";
            try { this.SaveKeybinds(false); } catch { }
        }

        // :184-207 — NO SaveKeybinds (verified absent in source — the flag is session-only, file
        // header); the cascade is field/dict mutations + a ModLogger line, no toast.
        // Slots of the selected recipe. NOT per frame: reading them runs InitCookingRecipeDetail,
        // which re-runs the game's AutoFill over the whole bag. A recipe change (or a pin, which
        // zeroes the key) re-reads at once; otherwise a slow interval keeps the "what AutoFill
        // chose" icons honest as the bag changes under the open panel.
        private readonly List<NetCookSlotInfo> netCookSlotInfoBuffer = new List<NetCookSlotInfo>(16);
        private int netCookSlotInfoRecipeId = 0;
        private float nextNetCookSlotInfoRefreshAt = 0f;
        private const float NetCookSlotInfoRefreshSeconds = 1f;

        private void EnsureUguiMassCookSlotInfo()
        {
            // Deliberately NOT "or the buffer is empty": a recipe whose slots cannot be read
            // would then re-run InitCookingRecipeDetail every single frame. A failed read
            // simply retries on the interval like any other refresh.
            bool recipeChanged = this.netCookSlotInfoRecipeId != this.netCookRecipeId;
            if (!recipeChanged && Time.unscaledTime < this.nextNetCookSlotInfoRefreshAt)
            {
                return;
            }

            this.netCookSlotInfoRecipeId = this.netCookRecipeId;
            this.nextNetCookSlotInfoRefreshAt = Time.unscaledTime + NetCookSlotInfoRefreshSeconds;
            this.TryReadNetCookRecipeSlots(this.netCookRecipeId, this.netCookSlotInfoBuffer, out _);
        }

        // One tile per material slot: the ingredient that will actually be used, pinned or not.
        // The tile the picker is parked on is painted with the accent, exactly like a selected dish
        // — same affordance, so there is nothing new to learn.
        private void SyncUguiFeaturesMassCookSlotRow(UguiShellFeaturesMassCookHandle handle)
        {
            bool show = this.netCookSlotManualMode && this.netCookRecipeId > 0;
            if (show)
            {
                this.EnsureUguiMassCookSlotInfo();
            }

            int slotCount = show ? Mathf.Min(this.netCookSlotInfoBuffer.Count, UguiMassCookMaxSlotButtons) : 0;
            for (int i = 0; i < handle.SlotTiles.Count; i++)
            {
                bool on = i < slotCount;
                SetUguiGoActive(handle.SlotTiles[i], on);
                if (!on)
                {
                    continue;
                }

                NetCookSlotInfo info = this.netCookSlotInfoBuffer[i];
                bool pinned = info.PreferredStaticId > 0;

                // Pinned wins; otherwise show what AutoFill put there, so an untouched slot still
                // tells you which ingredient it is about to spend rather than sitting blank.
                int shownStaticId = pinned ? info.PreferredStaticId : info.FilledStaticId;

                Texture2D slotTex = null;
                if (shownStaticId > 0)
                {
                    this.TryGetNetCookRecipeTexture(shownStaticId, out slotTex);
                }

                int slotTexId = slotTex != null ? slotTex.GetInstanceID() : 0;
                if (slotTexId != handle.SlotTileIconTexIds[i])
                {
                    handle.SlotTileIconTexIds[i] = slotTexId;
                    Image icon = handle.SlotTileIcons[i];
                    if (icon != null)
                    {
                        icon.sprite = slotTex != null ? this.GetOrCreateUguiTransferSprite(slotTex) : null;
                        icon.enabled = icon.sprite != null;
                    }
                }

                // "*" marks a pinned slot — the accent is already spoken for by the open picker,
                // and the kit has no safe glyph set beyond ASCII for a second marker.
                string label;
                if (shownStaticId > 0 && this.TryResolveNetCookItemName(shownStaticId, out string itemName))
                {
                    label = pinned ? ("* " + itemName) : itemName;
                }
                else
                {
                    label = "#" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!string.Equals(label, handle.SlotTileLabelShown[i], StringComparison.Ordinal))
                {
                    handle.SlotTileLabelShown[i] = label;
                    this.SetUguiLabelText(handle.SlotTileLabels[i], label);
                }

                int active = this.netCookSlotPickerIndex == i ? 1 : 0;
                if (active != handle.SlotTileActiveShown[i])
                {
                    handle.SlotTileActiveShown[i] = active;
                    try
                    {
                        if (handle.SlotTileFills[i] != null)
                        {
                            handle.SlotTileFills[i].color = active == 1
                                ? this.UguiKitAccent()
                                : this.UguiKitControlFill();
                        }
                    }
                    catch { }
                    this.SetUguiLabelColor(handle.SlotTileLabels[i], active == 1
                        ? this.GetUiTextOnAccent(this.UguiKitAccent())
                        : this.UguiKitTextColor());
                }
            }

            bool backShown = show && this.netCookSlotPickerIndex >= 0;
            SetUguiGoActive(handle.SlotBackButton, backShown);
            if (backShown && handle.SlotBackCellShown != slotCount)
            {
                handle.SlotBackCellShown = slotCount;
                PlaceUguiSlotTile(handle.SlotBackButton, slotCount,
                    UguiMassCookRecipeCellWidth(UguiMassCookGridInnerWidth(handle)));
            }

            // The grid starts under whatever the slot area occupies — zero rows means it starts
            // right under the search box instead of leaving a dead band.
            int cells = slotCount + (backShown ? 1 : 0);
            int slotRows = (cells + UguiMassCookRecipeColumns - 1) / UguiMassCookRecipeColumns;
            if (slotRows != handle.SlotAreaRowsShown)
            {
                handle.SlotAreaRowsShown = slotRows;
                float gridTop = UguiMassCookRecipeGridTopFor(slotRows);
                PlaceUguiTopLeft(handle.RecipeListScroll, 4f, gridTop, handle.ContentWidth - 16f - 8f,
                    this.GetUguiFeaturesMassCookRecipePanelHeight() - gridTop - 4f);
            }
        }

        private void OnUguiFeaturesMassCookSlotButtonClicked(int slotIndex)
        {
            this.netCookSlotPickerIndex = this.netCookSlotPickerIndex == slotIndex ? -1 : slotIndex;
            this.netCookStatus = this.netCookSlotPickerIndex >= 0
                ? "Pick an ingredient for slot " + (slotIndex + 1) + "."
                : "Recipe list.";

            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle != null && handle.Root != null)
            {
                this.SyncUguiFeaturesMassCookRecipeRows(handle);
                this.SyncUguiFeaturesMassCookSlotRow(handle);
            }
        }

        private void OnUguiFeaturesMassCookSlotBackClicked()
        {
            this.netCookSlotPickerIndex = -1;
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle != null && handle.Root != null)
            {
                this.SyncUguiFeaturesMassCookRecipeRows(handle);
                this.SyncUguiFeaturesMassCookSlotRow(handle);
            }
        }

        private void OnUguiFeaturesMassCookCookableOnlyToggled(bool value)
        {
            this.netCookCookableOnly = value;
            // Turning it on must measure now rather than on the next interval, or the list keeps
            // showing what it was showing until the throttle expires.
            this.nextNetCookCookableRefreshAt = 0f;
            this.SaveKeybinds();

            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle != null && handle.Root != null)
            {
                this.SyncUguiFeaturesMassCookRecipeRows(handle);
            }
        }

        private void OnUguiFeaturesMassCookManualIngredientsToggled(bool value)
        {
            this.netCookSlotManualMode = value;
            // Leaving manual mode with the grid parked on a slot would strand the picker on a view
            // whose slot row is no longer drawn.
            this.netCookSlotPickerIndex = -1;
            this.SaveKeybinds();
        }

        private void OnUguiFeaturesMassCookStatusDiagToggled(bool value)
        {
            if (value == this.netCookStatusDiagEnabled)
            {
                return;
            }
            this.netCookStatusDiagEnabled = value;
            if (!this.netCookStatusDiagEnabled)
            {
                this.netCookStatusDiagLastLogAt.Clear();
                this.netCookStatusDiagSessionAnnounced = false;
                this.nextNetCookDiagHeartbeatAt = 0f;
                try { ModLogger.Msg("[NetCookDiag] Status diagnostics OFF."); } catch { }
            }
            else
            {
                this.EnsureNetCookStatusDiagHooks();
                try
                {
                    ModLogger.Msg("[NetCookDiag] Status diagnostics ON. Capture stoves, then start Mass Cook. "
                        + "Logs: BepInEx/LogOutput.log and BepInEx/UserData/bugtopia.log. "
                        + "Watch for textId=7, EntityRemoveEvent, TARGET REMOVED.");
                }
                catch { }
            }
        }

        // STOVE TYPE header — mutually exclusive with the recipe panel: both are absolutely
        // positioned in the same scroll column, so two open boxes would overlap.
        private void OnUguiFeaturesMassCookStoveTypeHeaderClicked()
        {
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle == null || handle.Root == null)
            {
                return;
            }

            try
            {
                if (!this.ShouldShowNetCookCookerTypePicker())
                {
                    return;
                }

                this.netCookCookerTypeDropdownOpen = !this.netCookCookerTypeDropdownOpen;
                if (this.netCookCookerTypeDropdownOpen)
                {
                    this.netCookRecipeDropdownOpen = false;
                }
                this.SyncUguiSelfLabelText(handle.StoveTypeArrow, ref handle.StoveTypeArrowShown,
                    this.netCookCookerTypeDropdownOpen ? "^" : "v");
                if (this.netCookCookerTypeDropdownOpen)
                {
                    this.SyncUguiFeaturesMassCookStoveTypeRows(handle);
                }
                this.RefreshUguiFeaturesMassCookLayout(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook stove type header error: " + ex.Message);
            }
        }

        // A pick rebuilds the working set from the capture snapshot, swaps the recipe cache and
        // kicks the deferred expansion for the new type (HeartopiaComplete.NetCookStoveType.cs).
        private void OnUguiFeaturesMassCookStoveTypeRowClicked(UguiMassCookStoveTypeRowHandle row)
        {
            if (row == null || row.BoundRecipeCookerType == int.MinValue)
            {
                return;
            }

            try
            {
                bool applied = this.ApplyNetCookPreferredCookerType(row.BoundRecipeCookerType, out string status);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    this.AddMenuNotification(status, applied
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.55f));
                }

                UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
                if (handle != null && handle.Root != null)
                {
                    this.SyncUguiFeaturesMassCookStoveTypeRows(handle);
                    this.SyncUguiSelfLabelText(handle.StoveTypeHeaderValue, ref handle.StoveTypeHeaderShown,
                        this.GetNetCookSelectedCookerTypeLabel());
                    this.SyncUguiSelfLabelText(handle.StoveTypeArrow, ref handle.StoveTypeArrowShown,
                        this.netCookCookerTypeDropdownOpen ? "^" : "v");
                    this.RefreshUguiFeaturesMassCookLayout(handle); // both panels close this frame
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook stove type pick error: " + ex.Message);
            }
        }

        // :235-237 — the SHARED open flag (file header); relayout on the click frame.
        private void OnUguiFeaturesMassCookRecipeHeaderClicked()
        {
            this.netCookRecipeDropdownOpen = !this.netCookRecipeDropdownOpen;
            if (this.netCookRecipeDropdownOpen)
            {
                this.netCookCookerTypeDropdownOpen = false; // never two open boxes in one column
            }
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            this.SyncUguiSelfLabelText(handle.RecipeArrow, ref handle.RecipeArrowShown,
                this.netCookRecipeDropdownOpen ? "^" : "v");
            if (this.netCookRecipeDropdownOpen)
            {
                try { this.SyncUguiFeaturesMassCookRecipeRows(handle); } catch { }
            }
            this.RefreshUguiFeaturesMassCookLayout(handle);
        }

        // :255-259 — the search cascade: shared text + the shared IMGUI scroll reset; the UGUI
        // list snaps to the top too, then rebinds immediately (per-keystroke live filter).
        private void ApplyUguiFeaturesMassCookRecipeSearch(UguiShellFeaturesMassCookHandle handle, string text)
        {
            handle.RecipeSearchApplied = text;
            this.netCookRecipeSearchText = text;
            this.netCookRecipeScrollPos = Vector2.zero;
            this.ResetUguiFeaturesMassCookRecipeListScroll(handle);
            if (this.netCookRecipeDropdownOpen)
            {
                this.SyncUguiFeaturesMassCookRecipeRows(handle);
            }
        }

        private void OnUguiFeaturesMassCookRecipeSearchChanged(string value)
        {
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, handle.RecipeSearchApplied, StringComparison.Ordinal))
                {
                    return; // the gated poll already applied it (or a redundant event)
                }
                this.ApplyUguiFeaturesMassCookRecipeSearch(handle, text);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook recipe search error: " + ex.Message);
            }
        }

        // :280-285, verbatim order — recipe id, close the dropdown, reset BOTH the quantity int
        // AND its string mirror, zero the refresh throttle, the raw-Value status. NO save call
        // (verified absent).
        private void OnUguiFeaturesMassCookRecipeRowClicked(UguiMassCookRecipeRowHandle row)
        {
            if (row == null || row.BoundId == int.MinValue)
            {
                return;
            }

            // While the grid is showing a slot's candidates, a cell is an ITEM, not a recipe:
            // picking one records the preference for that slot and returns to the recipe list.
            if (this.netCookSlotPickerIndex >= 0)
            {
                try
                {
                    int slot = this.netCookSlotPickerIndex;
                    // Clicking the already-preferred item clears it, which is how a slot goes back
                    // to automatic without a separate control.
                    int current = this.GetNetCookSlotPreference(this.netCookRecipeId, slot);
                    int next = current == row.BoundId ? 0 : row.BoundId;
                    this.SetNetCookSlotPreference(this.netCookRecipeId, slot, next);
                    // The slot cache is keyed by recipe, and the recipe did not change: without
                    // this the row keeps showing the previous pick until the panel is reopened.
                    this.netCookSlotInfoRecipeId = 0;
                    this.netCookSlotPickerIndex = -1;
                    this.netCookStatus = next > 0
                        ? "Slot " + (slot + 1) + ": " + row.BoundValue
                        : "Slot " + (slot + 1) + " back to automatic.";

                    UguiShellFeaturesMassCookHandle h = this.uguiShellFeaturesMassCook;
                    if (h != null && h.Root != null)
                    {
                        this.SyncUguiFeaturesMassCookRecipeRows(h);
                    }
                }
                catch (Exception ex)
                {
                    this.NetCookLog("slot pick failed: " + ex.Message);
                }

                return;
            }

            try
            {
                this.netCookRecipeId = row.BoundId;
                // An explicit pick is what the Stove Type switch restores later for this menu.
                this.RememberNetCookRecipeForActiveMenu();
                // Picking ingredients starts by picking the dish, so keep the panel open for the
                // slot row instead of making the user reopen it.
                this.netCookRecipeDropdownOpen = this.netCookSlotManualMode;
                this.netCookSlotInfoRecipeId = 0;
                this.netCookCookQuantity = 1;
                this.netCookCookQuantityInput = "1";
                this.nextNetCookMaxRefreshAt = 0f;
                this.netCookStatus = "Selected recipe: " + row.BoundValue;

                UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
                if (handle != null && handle.Root != null)
                {
                    this.RefreshUguiFeaturesMassCookLayout(handle); // panel closes this frame
                }
                // The qty field picks the "1" up via the gated poll's external branch.
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook recipe pick error: " + ex.Message);
            }
        }

        // :296-303 / :305-312 — flag + refresh-throttle zero + SAVE, no status/toast.
        private void OnUguiFeaturesMassCookMoveIngredientsToggled(bool value)
        {
            if (value == this.netCookMoveIngredients)
            {
                return;
            }
            this.netCookMoveIngredients = value;
            this.nextNetCookMaxRefreshAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiFeaturesMassCookUseAllIngredientsToggled(bool value)
        {
            if (value == this.netCookUseAllIngredients)
            {
                return;
            }
            this.netCookUseAllIngredients = value;
            this.nextNetCookMaxRefreshAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Mod-only toggle (no source twin), and deliberately NOT saved — like "Status Diagnostics" it
        // is session-only, so a paid item is never spent because a toggle survived a restart. The
        // throttle reset matters: the "Ingredients max" figure grows/shrinks with the universal stock.
        private void OnUguiFeaturesMassCookUseUniversalIngredientToggled(bool value)
        {
            if (value == this.netCookUseUniversalIngredient)
            {
                return;
            }
            this.netCookUseUniversalIngredient = value;
            this.nextNetCookMaxRefreshAt = 0f;
        }

        // :327-332 — raw text into the string mirror, then the backend's own parser; NO local
        // parse/clamp logic (file header). Its normalization returns via the gated poll.
        private void ApplyUguiFeaturesMassCookQty(UguiShellFeaturesMassCookHandle handle, string text)
        {
            handle.QtyApplied = text;
            this.netCookCookQuantityInput = text;
            this.SyncNetCookCookQuantityFromInput();
        }

        private void OnUguiFeaturesMassCookQtyChanged(string value)
        {
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, handle.QtyApplied, StringComparison.Ordinal))
                {
                    return;
                }
                this.ApplyUguiFeaturesMassCookQty(handle, text);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook quantity input error: " + ex.Message);
            }
        }

        // :339-349.
        private void OnUguiFeaturesMassCookStartStopClicked()
        {
            try
            {
                if (this.netCookEnabled)
                {
                    this.StopNetCookInternal("Disabled");
                }
                else
                {
                    this.StartNetCookInternal();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Mass Cook start/stop error: " + ex.Message);
            }
        }

        // :362-364 — nearest 0.01 (Round(v*100)/100), epsilon-save, NO status side effect.
        private void OnUguiFeaturesMassCookDelayChanged(float value)
        {
            float rounded = Mathf.Round(value * 100f) / 100f;
            if (Math.Abs(rounded - this.netCookInterval) > 0.0001f)
            {
                this.netCookInterval = rounded;
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // :370-376 — WHOLE-number round (plain Mathf.Round — deliberately different from the
        // delay slider), epsilon-save, AND the status string (only this slider sets one).
        private void OnUguiFeaturesMassCookRadiusChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (Math.Abs(rounded - this.netCookScanRadiusMeters) > 0.0001f)
            {
                this.netCookScanRadiusMeters = rounded;
                this.netCookStatus = $"Scan radius set to {this.netCookScanRadiusMeters:F0}m. Capture stoves again to refresh targets.";
                try { this.SaveKeybinds(false); } catch { }
            }
        }
    }
}
