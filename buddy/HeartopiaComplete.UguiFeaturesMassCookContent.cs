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
    //  - The 5 toggles (:135-208) and their SAVE ASYMMETRY: "Mini Game Only" (cascade: closes the
    //    recipe dropdown, one of two status strings, SAVES), "Remember Stoves" (status, SAVES),
    //    "Capture Own" (status, SAVES), "Capture Radius" (status, SAVES) — but "Status
    //    Diagnostics (log)" has NO SaveKeybinds call in source (verified absent; the flag is
    //    session-only). Its cascade instead does real work, file-log-only, no toast: OFF →
    //    netCookStatusDiagLastLogAt.Clear() + netCookStatusDiagSessionAnnounced = false +
    //    nextNetCookDiagHeartbeatAt = 0f + the OFF ModLogger line; ON →
    //    EnsureNetCookStatusDiagHooks() + the long ON ModLogger line. Reproduced verbatim, and
    //    deliberately NOT given a save call. All toggle labels get this.L (the IMGUI twin is
    //    DrawSwitchToggle, which localizes internally — UiKitPrimitives.cs:750); everything else
    //    on this tab is raw GUI.Label/GUI.Button strings and stays UNlocalized.
    //  - ASSIST MODE card (mini-game branch, :210-225): height is DYNAMIC in source —
    //    Mathf.Max(32f, statusStyle.CalcHeight(desc, w-24)) + 36 + 12. Ported with the Pictures
    //    round's MeasureUguiPicturesWrappedHeight (TMP GetPreferredValues, Ceil+4), fallback 32f
    //    = the source's own Max floor, retried on gated frames until the TMP component has
    //    Awoken (built-inactive caveat). Height feeds the layout signature.
    //  - RECIPE dropdown (else branch, :227-293): a hand-rolled header (GetNetCookSelectedRecipeLabel
    //    caption — LIVE per gated frame, capture can auto-select a recipe — + accent "^"/"v"
    //    arrow) toggling the SHARED netCookRecipeDropdownOpen flag. Shared on purpose (unlike
    //    Food & Repair's stock-Dropdown round, whose IMGUI open-flags had no UGUI counterpart):
    //    here the flag IS the panel's model on both surfaces — the Mini Game Only cascade and the
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
    //  - Start/Stop (:336-350): ONE of FOUR captions by (netCookEnabled, netCookMiniGameOnly) —
    //    "STOP MINI GAME ASSIST"/"STOP MASS COOK" on the danger twin, "START MINI GAME
    //    ASSIST"/"START MASS COOK" on the primary twin — captions re-synced per gated frame
    //    (miniGameOnly flips them live); pair swap by netCookEnabled. Click →
    //    StopNetCookInternal("Disabled") when enabled, else StartNetCookInternal().
    //  - Settings card (:352-377) and the SLIDER ASYMMETRY: COOK DELAY = float [0.25,10] rounded
    //    to the NEAREST 0.01 (Mathf.Round(v*100)/100 — :363), "{0:F2}s", epsilon-saved (>0.0001),
    //    NO status side effect; SCAN RADIUS = float [NetCookMinScanRadiusMeters,
    //    NetCookMaxScanRadiusMeters] rounded to a WHOLE number (plain Mathf.Round — :371, so the
    //    kit slider is wholeNumbers=true), "{0:F0}m", epsilon-saved AND sets netCookStatus
    //    ("Scan radius set to {0:F0}m. Capture stoves again to refresh targets.") on change.
    //    Only the radius slider touches the status string — verified.
    //  - Status card (:379-402): STOVES = netCookTargets.Count and SENT = netCookSentCount, both
    //    LIVE ints per gated frame; the status line is NOT "always netCookStatus" — a 4-way
    //    readiness fallback (netCookMiniGameOnly × hasCapturedStoves × hasRecipe, :397-399) shows
    //    ONLY while netCookStatus is blank/whitespace, else the live netCookStatus (:400).
    //    Reproduced as a builder evaluated per gated frame. (The label gets 34px instead of the
    //    source's 28 — TMP clips where IMGUI overflowed; two 12pt lines stay inside the card.)
    //
    // Cross-surface sync cadence: every gated frame — busy-gate interactables, pill, button-pair
    // swaps + captions, 7 toggle re-syncs (SetIsOnWithoutNotify), recipe-branch work only while
    // !miniGameOnly (cache ensure, header caption/arrow, search poll pair, row bind while open,
    // max refresh + label, qty poll pair), slider re-syncs + value labels, status-card stats +
    // text, assist-measure retry, then the layout-signature check. Everything diffs before
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

            public Toggle MiniGameOnlyToggle;
            public Toggle RememberStovesToggle;
            public Toggle CaptureOwnToggle;
            public Toggle CaptureRadiusToggle;
            public Toggle StatusDiagToggle;
            public Toggle ManualIngredientsToggle;
            public Toggle CookableOnlyToggle;
            public readonly List<Image> SlotButtonIcons = new List<Image>();
            public readonly List<int> SlotButtonIconTexIds = new List<int>();
            public readonly List<GameObject> SlotButtons = new List<GameObject>();
            public GameObject SlotBackButton;

            public GameObject AssistCard;         // mini-game branch
            public GameObject AssistDescLabel;
            public float AssistTextHeight = 32f;  // Max(32, measured) — source fallback floor
            public bool AssistTextMeasured;       // retry until the TMP component has Awoken

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
            public string StartShown;             // caption caches (depend on miniGameOnly)
            public string StopShown;

            public GameObject SettingsCard;
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
        // re-based to 8: header 8 (+42) → capture row 50 (+50) → cleanup 100 (+50) → the five
        // toggles 150/188/226/264/302 (+38 each) → conditional region 340. Everything from 340
        // down is owned by the relayout (both branches shift it).
        // Seven toggles now, stepping 38 from 150: the last one sits at 378, so the
        // conditional block below them starts one step further down than it used to.
        private const float UguiMassCookConditionalTopY = 416f;
        private const float UguiMassCookRecipePanelHeight = 300f;
        private const float UguiMassCookRecipeRowStep = 28f;       // :275 — 24-tall rows stepping 28

        // Recipe picker grid. Same tile shape as the Bag/Warehouse cells, minus the virtualization:
        // that pool exists because a bag holds hundreds of stacks, while a cooker type offers a few
        // dozen dishes. The existing row pool already grows on demand and is never destroyed, which
        // is all this needs.
        private const int UguiMassCookRecipeColumns = 4;
        private const float UguiMassCookRecipeCellW = 78f;
        private const float UguiMassCookRecipeCellH = 92f;
        private const float UguiMassCookRecipeCellGap = 4f;

        // Slot row sits between the search box and the grid, so entering and leaving the
        // picker never moves the grid itself.
        private const float UguiMassCookSlotRowY = 40f;
        private const float UguiMassCookSlotButtonW = 34f;
        private const float UguiMassCookSlotButtonH = 26f;
        private const float UguiMassCookRecipeGridTop = 72f;
        private const int UguiMassCookMaxSlotButtons = 12;

        // ----------------------------------------------------------------------------------------
        // Live layout signature — branch, dropdown-open, measured assist-card height (all three
        // drive real layout). Visible-row COUNT is deliberately absent: the open panel is a fixed
        // 260 and the rows scroll inside it.
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesMassCookLayoutSignature(UguiShellFeaturesMassCookHandle handle)
        {
            return (this.netCookMiniGameOnly ? 1 : 0)
                 | (this.netCookRecipeDropdownOpen ? 2 : 0)
                 | (Mathf.CeilToInt(handle.AssistTextHeight) << 2);
        }

        // The status card's fallback-vs-live conditional (:397-401) — NOT "always netCookStatus".
        private string BuildUguiFeaturesMassCookStatusText()
        {
            bool hasCapturedStoves = this.netCookTargets.Count > 0;
            bool hasRecipe = this.netCookRecipeId > 0;
            string readiness = this.netCookMiniGameOnly
                ? (hasCapturedStoves ? "Ready to assist active cooking mini-games." : "Capture stoves to begin assisting.")
                : (hasCapturedStoves ? (hasRecipe ? "Ready to cook." : "Select a recipe to continue.") : "Capture stoves to begin.");
            return string.IsNullOrWhiteSpace(this.netCookStatus) ? readiness : this.netCookStatus;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawMassCookTab: header + pill, capture/reset row, cleanup, 5 toggles,
        // the two-branch conditional region (assist card vs recipe dropdown + ingredient toggles
        // + dish limit), start/stop, settings card, status card — everything built ONCE;
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

            // -------- Capture / Reset row (:93-124) --------
            handle.CaptureButton = this.CreateUguiPrimaryButton(scrollContent, "CaptureButton",
                this.L("Capture Stoves"), new System.Action(this.OnUguiFeaturesMassCookCaptureClicked));
            PlaceUguiTopLeft(handle.CaptureButton, rowX, 50f, halfW, 36f);

            // Style-flip pair (file header): same rect, same handler, SetActive by netCookEnabled.
            handle.ResetButtonDefault = this.CreateUguiSecondaryButton(scrollContent, "ResetButtonDefault",
                this.L("Reset Capture"), new System.Action(this.OnUguiFeaturesMassCookResetClicked));
            PlaceUguiTopLeft(handle.ResetButtonDefault, rowX + halfW + 10f, 50f, halfW, 36f);
            handle.ResetButtonDanger = this.CreateUguiDangerButton(scrollContent, "ResetButtonDanger",
                this.L("Reset Capture"), new System.Action(this.OnUguiFeaturesMassCookResetClicked));
            PlaceUguiTopLeft(handle.ResetButtonDanger, rowX + halfW + 10f, 50f, halfW, 36f);

            // -------- Clean Up Finished Food (:126-133) --------
            handle.CleanupButton = this.CreateUguiPrimaryButton(scrollContent, "CleanupButton",
                this.L("Clean Up Finished Food"), new System.Action(this.OnUguiFeaturesMassCookCleanupClicked));
            PlaceUguiTopLeft(handle.CleanupButton, rowX, 100f, rowW, 36f);

            // -------- The five toggles (:135-208) — DrawSwitchToggle localizes, so L() here --------
            handle.MiniGameOnlyToggle = this.CreateUguiCheckbox(scrollContent, "MiniGameOnlyToggle",
                this.L("Mini Game Only"), this.netCookMiniGameOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookMiniGameOnlyToggled));
            PlaceUguiTopLeft(handle.MiniGameOnlyToggle.gameObject, rowX, 150f, rowW, 24f);
            handle.RememberStovesToggle = this.CreateUguiCheckbox(scrollContent, "RememberStovesToggle",
                this.L("Remember Stoves"), this.netCookRememberStoves,
                new System.Action<bool>(this.OnUguiFeaturesMassCookRememberStovesToggled));
            PlaceUguiTopLeft(handle.RememberStovesToggle.gameObject, rowX, 188f, rowW, 24f);
            handle.CaptureOwnToggle = this.CreateUguiCheckbox(scrollContent, "CaptureOwnToggle",
                this.L("Capture Own"), this.netCookCaptureOwnOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCaptureOwnToggled));
            PlaceUguiTopLeft(handle.CaptureOwnToggle.gameObject, rowX, 226f, rowW, 24f);
            handle.CaptureRadiusToggle = this.CreateUguiCheckbox(scrollContent, "CaptureRadiusToggle",
                this.L("Capture Radius"), this.netCookCaptureRadiusOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCaptureRadiusToggled));
            PlaceUguiTopLeft(handle.CaptureRadiusToggle.gameObject, rowX, 264f, rowW, 24f);
            handle.StatusDiagToggle = this.CreateUguiCheckbox(scrollContent, "StatusDiagToggle",
                this.L("Status Diagnostics (log)"), this.netCookStatusDiagEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMassCookStatusDiagToggled));
            PlaceUguiTopLeft(handle.StatusDiagToggle.gameObject, rowX, 302f, rowW, 24f);

            // Manual ingredients: off keeps the shipped behaviour (the game's AutoFill decides).
            handle.ManualIngredientsToggle = this.CreateUguiCheckbox(scrollContent, "ManualIngredientsToggle",
                this.L("Pick Ingredients"), this.netCookSlotManualMode,
                new System.Action<bool>(this.OnUguiFeaturesMassCookManualIngredientsToggled));
            PlaceUguiTopLeft(handle.ManualIngredientsToggle.gameObject, rowX, 340f, rowW, 24f);

            handle.CookableOnlyToggle = this.CreateUguiCheckbox(scrollContent, "CookableOnlyToggle",
                this.L("Only What I Can Cook"), this.netCookCookableOnly,
                new System.Action<bool>(this.OnUguiFeaturesMassCookCookableOnlyToggled));
            PlaceUguiTopLeft(handle.CookableOnlyToggle.gameObject, rowX, 378f, rowW, 24f);

            // -------- ASSIST MODE card (:210-225 — mini-game branch; height via relayout) --------
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

            // Slot row: one button per material slot of the selected recipe, plus a back button
            // shown only while the grid is parked on a slot. Built once at max width and shown or
            // hidden per sync — a slot count changes with the recipe, and rebuilding buttons on
            // every recipe click would churn GameObjects inside an open panel.
            for (int i = 0; i < UguiMassCookMaxSlotButtons; i++)
            {
                int slotIndex = i;
                GameObject slotBtn = this.CreateUguiSecondaryButton(handle.RecipePanel.transform,
                    "Slot" + i, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new System.Action(() => this.OnUguiFeaturesMassCookSlotButtonClicked(slotIndex)));
                PlaceUguiTopLeft(slotBtn, 18f + i * (UguiMassCookSlotButtonW + 3f), UguiMassCookSlotRowY,
                    UguiMassCookSlotButtonW, UguiMassCookSlotButtonH);
                SetUguiGoActive(slotBtn, false);
                handle.SlotButtons.Add(slotBtn);

                // The pinned item's icon sits on top of the button; the number underneath shows
                // through whenever a slot has no preference yet. raycastTarget off so the icon does
                // not swallow the click that belongs to the button.
                GameObject slotIconGo = this.CreateUguiGo("SlotIcon" + i, slotBtn.transform);
                Image slotIcon = this.AddUguiImage(slotIconGo, Color.white, false, 0f);
                slotIcon.raycastTarget = false;
                slotIcon.enabled = false;
                try { slotIcon.preserveAspect = true; } catch { }
                PlaceUguiTopLeft(slotIconGo, 3f, 2f, UguiMassCookSlotButtonW - 6f, UguiMassCookSlotButtonH - 4f);
                handle.SlotButtonIcons.Add(slotIcon);
                handle.SlotButtonIconTexIds.Add(0);
            }

            handle.SlotBackButton = this.CreateUguiSecondaryButton(handle.RecipePanel.transform,
                "SlotBack", this.L("Back"), new System.Action(this.OnUguiFeaturesMassCookSlotBackClicked));
            PlaceUguiTopLeft(handle.SlotBackButton, rowW - 90f, UguiMassCookSlotRowY, 70f, UguiMassCookSlotButtonH);
            SetUguiGoActive(handle.SlotBackButton, false);

            Transform recipeListContent;
            handle.RecipeListScroll = this.CreateUguiScrollView(handle.RecipePanel.transform, "RecipeList",
                10f, out recipeListContent);
            PlaceUguiTopLeft(handle.RecipeListScroll, 4f, UguiMassCookRecipeGridTop, rowW - 8f,
                UguiMassCookRecipePanelHeight - UguiMassCookRecipeGridTop - 4f);
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
            handle.StartShown = this.netCookMiniGameOnly ? this.L("START MINI GAME ASSIST") : this.L("START MASS COOK");
            handle.StartButton = this.CreateUguiPrimaryButton(scrollContent, "StartButton",
                handle.StartShown, new System.Action(this.OnUguiFeaturesMassCookStartStopClicked));
            handle.StopShown = this.netCookMiniGameOnly ? this.L("STOP MINI GAME ASSIST") : this.L("STOP MASS COOK");
            handle.StopButton = this.CreateUguiDangerButton(scrollContent, "StopButton",
                handle.StopShown, new System.Action(this.OnUguiFeaturesMassCookStartStopClicked));

            // -------- Settings card (:352-377 — 112 tall; children card-local) --------
            handle.SettingsCard = this.CreateUguiGo("SettingsCard", scrollContent);
            this.AddUguiImage(handle.SettingsCard, this.UguiKitPanelBg(), true, 1f);
            float settingsW = rowW - 24f;

            GameObject delayLabel = this.CreateUguiLabel(handle.SettingsCard.transform, "DelayLabel",
                this.L("COOK DELAY"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(delayLabel);
            PlaceUguiTopLeft(delayLabel, 12f, 10f, settingsW * 0.55f, 18f);
            handle.DelayShown = string.Format("{0:F2}s", this.netCookInterval);
            handle.DelayValueLabel = this.CreateUguiLabel(handle.SettingsCard.transform, "DelayValue",
                handle.DelayShown, 12f, Color.white, false);
            PlaceUguiTopLeft(handle.DelayValueLabel, 12f + settingsW * 0.55f, 10f, settingsW * 0.45f, 18f);
            handle.DelaySlider = this.CreateUguiSlider(handle.SettingsCard.transform, "DelaySlider",
                0.25f, 10f, this.netCookInterval, false,
                new System.Action<float>(this.OnUguiFeaturesMassCookDelayChanged));
            PlaceUguiTopLeft(handle.DelaySlider.gameObject, 12f, 30f, settingsW, 20f);

            GameObject radiusLabel = this.CreateUguiLabel(handle.SettingsCard.transform, "RadiusLabel",
                this.L("SCAN RADIUS"), 11f, mutedTextColor, false);
            this.TrySetUguiLabelBold(radiusLabel);
            PlaceUguiTopLeft(radiusLabel, 12f, 68f, settingsW * 0.55f, 18f);
            handle.RadiusShown = string.Format("{0:F0}m", this.netCookScanRadiusMeters);
            handle.RadiusValueLabel = this.CreateUguiLabel(handle.SettingsCard.transform, "RadiusValue",
                handle.RadiusShown, 12f, Color.white, false);
            PlaceUguiTopLeft(handle.RadiusValueLabel, 12f + settingsW * 0.55f, 68f, settingsW * 0.45f, 18f);
            // wholeNumbers=true — the source's plain Mathf.Round contract (:371, file header).
            handle.RadiusSlider = this.CreateUguiSlider(handle.SettingsCard.transform, "RadiusSlider",
                NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters, this.netCookScanRadiusMeters, true,
                new System.Action<float>(this.OnUguiFeaturesMassCookRadiusChanged));
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 12f, 88f, settingsW, 20f);

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
            bool mini = this.netCookMiniGameOnly;
            bool open = !mini && this.netCookRecipeDropdownOpen;
            float yCur = UguiMassCookConditionalTopY;

            SetUguiGoActive(handle.AssistCard, mini);
            SetUguiGoActive(handle.RecipeLabel, !mini);
            SetUguiGoActive(handle.RecipeHeader, !mini);
            SetUguiGoActive(handle.RecipePanel, open);
            if (handle.MoveIngredientsToggle != null)
            {
                SetUguiGoActive(handle.MoveIngredientsToggle.gameObject, !mini);
            }
            if (handle.UseAllIngredientsToggle != null)
            {
                SetUguiGoActive(handle.UseAllIngredientsToggle.gameObject, !mini);
            }
            if (handle.UseUniversalIngredientToggle != null)
            {
                SetUguiGoActive(handle.UseUniversalIngredientToggle.gameObject, !mini);
            }
            SetUguiGoActive(handle.DishLimitLabel, !mini);
            SetUguiGoActive(handle.DishMaxLabel, !mini);
            if (handle.QtyField != null)
            {
                SetUguiGoActive(handle.QtyField.gameObject, !mini);
            }

            if (mini)
            {
                // :212-224 — card 36 + textH + 12; cursor += Ceil(cardH) + 12.
                float textH = handle.AssistTextHeight;
                float cardH = 36f + textH + 12f;
                PlaceUguiTopLeft(handle.AssistCard, rowX, yCur, rowW, cardH);
                PlaceUguiTopLeft(handle.AssistDescLabel, 12f, 28f, rowW - 24f, textH);
                yCur += Mathf.Ceil(cardH) + 12f;
            }
            else
            {
                PlaceUguiTopLeft(handle.RecipeLabel, rowX, yCur, rowW, 18f);
                yCur += 20f;
                PlaceUguiTopLeft(handle.RecipeHeader, rowX, yCur, rowW, 36f);
                yCur += 46f;
                if (open)
                {
                    // :246 — panel at header yMax + 4 = yCur - 6; cursor += 268.
                    PlaceUguiTopLeft(handle.RecipePanel, rowX, yCur - 6f, rowW, UguiMassCookRecipePanelHeight);
                    yCur += UguiMassCookRecipePanelHeight + 8f;
                }
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
            }

            PlaceUguiTopLeft(handle.StartButton, rowX, yCur, rowW, 38f);
            PlaceUguiTopLeft(handle.StopButton, rowX, yCur, rowW, 38f);
            yCur += 52f;

            PlaceUguiTopLeft(handle.SettingsCard, rowX, yCur, rowW, 112f);
            yCur += 126f;

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

            // :336-338 — captions also depend on miniGameOnly, so both twins re-sync per call.
            string startText = this.netCookMiniGameOnly ? this.L("START MINI GAME ASSIST") : this.L("START MASS COOK");
            if (!string.Equals(startText, handle.StartShown, StringComparison.Ordinal))
            {
                handle.StartShown = startText;
                this.SetUguiButtonLabel(handle.StartButton, startText);
            }
            string stopText = this.netCookMiniGameOnly ? this.L("STOP MINI GAME ASSIST") : this.L("STOP MASS COOK");
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

            GameObject root = this.CreateUguiGo("Recipe" + index, handle.RecipeListContent);
            PlaceUguiTopLeft(root,
                (index % UguiMassCookRecipeColumns) * (UguiMassCookRecipeCellW + UguiMassCookRecipeCellGap),
                (index / UguiMassCookRecipeColumns) * (UguiMassCookRecipeCellH + UguiMassCookRecipeCellGap),
                UguiMassCookRecipeCellW, UguiMassCookRecipeCellH);
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
            PlaceUguiTopLeft(iconGo, (UguiMassCookRecipeCellW - 42f) * 0.5f, 8f, 42f, 42f);

            // Name under the icon, centred, wrapping to the two lines the tile has room for.
            row.Label = this.CreateUguiLabel(root.transform, "Name", "", 10f, this.UguiKitTextColor(), true);
            PlaceUguiTopLeft(row.Label, 2f, 52f, UguiMassCookRecipeCellW - 4f, 36f);
            this.TrySetUguiLabelWrapped(row.Label);

            row.Badge = this.CreateUguiLabel(root.transform, "Badge", this.L("RECENT"), 8f,
                this.UguiKitAccent(), true);
            PlaceUguiTopLeft(row.Badge, UguiMassCookRecipeCellW - UguiMassCookRecipeBadgeWidth - 2f, 1f,
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
        // grid does not need a second code path. Rebuilt per sync rather than cached: the bag
        // changes while the panel is open, and a stale list would offer items that are gone.
        private readonly List<KeyValuePair<int, string>> netCookSlotCandidateEntries = new List<KeyValuePair<int, string>>(32);
        private readonly List<NetCookSlotCandidate> netCookSlotCandidateBuffer = new List<NetCookSlotCandidate>(32);

        private List<KeyValuePair<int, string>> GetUguiMassCookSlotCandidateEntries()
        {
            this.netCookSlotCandidateEntries.Clear();
            if (this.netCookSlotPickerIndex < 0)
            {
                return this.netCookSlotCandidateEntries;
            }

            if (!this.TryListNetCookSlotCandidates(this.netCookSlotPickerIndex, this.netCookSlotCandidateBuffer, out _))
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

                if (search.Length > 0 && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                this.netCookSlotCandidateEntries.Add(new KeyValuePair<int, string>(c.StaticId, label));
            }

            return this.netCookSlotCandidateEntries;
        }

        private void SyncUguiFeaturesMassCookRecipeRows(UguiShellFeaturesMassCookHandle handle)
        {
            // Two data sources through one grid: recipes normally, and a slot's candidate items
            // while the picker is parked on a slot. Same cells, same icons, same selection diff —
            // only the list and what a click means change.
            List<KeyValuePair<int, string>> visible = this.netCookSlotPickerIndex >= 0
                ? this.GetUguiMassCookSlotCandidateEntries()
                : this.GetVisibleNetCookRecipeEntries();
            int count = visible.Count;
            float innerW = handle.ContentWidth - 16f - 8f - 22f; // panel rowW-8 minus kit viewport insets

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
                this.SyncUguiToggleFromField(handle.MiniGameOnlyToggle, this.netCookMiniGameOnly);
                this.SyncUguiToggleFromField(handle.RememberStovesToggle, this.netCookRememberStoves);
                this.SyncUguiToggleFromField(handle.CaptureOwnToggle, this.netCookCaptureOwnOnly);
                this.SyncUguiToggleFromField(handle.CaptureRadiusToggle, this.netCookCaptureRadiusOnly);
                this.SyncUguiToggleFromField(handle.StatusDiagToggle, this.netCookStatusDiagEnabled);
                this.SyncUguiToggleFromField(handle.ManualIngredientsToggle, this.netCookSlotManualMode);
                this.SyncUguiToggleFromField(handle.CookableOnlyToggle, this.netCookCookableOnly);
                this.SyncUguiToggleFromField(handle.MoveIngredientsToggle, this.netCookMoveIngredients);
                this.SyncUguiToggleFromField(handle.UseAllIngredientsToggle, this.netCookUseAllIngredients);
                this.SyncUguiToggleFromField(handle.UseUniversalIngredientToggle, this.netCookUseUniversalIngredient);

                if (this.netCookMiniGameOnly)
                {
                    // Assist-card measure retry (built-inactive TMP caveat — file header). The
                    // description is constant, so this stops as soon as one measure succeeds.
                    if (!handle.AssistTextMeasured)
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
                }
                else
                {
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
                if (this.TryCaptureNetCookFromCurrentTarget())
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

        // :135-146 — cascade: close the recipe dropdown (SHARED flag — both surfaces), one of two
        // status strings, SAVE. The equal-guard is the UGUI analog of IMGUI's prev-vs-new check.
        private void OnUguiFeaturesMassCookMiniGameOnlyToggled(bool value)
        {
            if (value == this.netCookMiniGameOnly)
            {
                return;
            }
            this.netCookMiniGameOnly = value;
            this.netCookRecipeDropdownOpen = false;
            this.netCookStatus = this.netCookMiniGameOnly
                ? "Mini game only mode enabled. Capture stoves to assist active cooking."
                : "Mini game only mode disabled. Select a recipe to mass cook.";
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesMassCookHandle handle = this.uguiShellFeaturesMassCook;
            if (handle != null && handle.Root != null)
            {
                this.RefreshUguiFeaturesMassCookLayout(handle); // click-responsive branch swap
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
        // Slots of the selected recipe, refreshed when the recipe changes rather than per frame:
        // reading them runs InitCookingRecipeDetail, which re-runs the game's AutoFill.
        private readonly List<NetCookSlotInfo> netCookSlotInfoBuffer = new List<NetCookSlotInfo>(16);
        private int netCookSlotInfoRecipeId = 0;

        private void EnsureUguiMassCookSlotInfo()
        {
            if (this.netCookSlotInfoRecipeId == this.netCookRecipeId && this.netCookSlotInfoBuffer.Count > 0)
            {
                return;
            }

            this.netCookSlotInfoRecipeId = this.netCookRecipeId;
            this.TryReadNetCookRecipeSlots(this.netCookRecipeId, this.netCookSlotInfoBuffer, out _);
        }

        private void SyncUguiFeaturesMassCookSlotRow(UguiShellFeaturesMassCookHandle handle)
        {
            bool show = this.netCookSlotManualMode && this.netCookRecipeId > 0;
            if (show)
            {
                this.EnsureUguiMassCookSlotInfo();
            }

            int slotCount = show ? Mathf.Min(this.netCookSlotInfoBuffer.Count, UguiMassCookMaxSlotButtons) : 0;
            for (int i = 0; i < handle.SlotButtons.Count; i++)
            {
                bool on = i < slotCount;
                SetUguiGoActive(handle.SlotButtons[i], on);
                if (!on)
                {
                    continue;
                }

                // A slot with a preference is labelled with a dot so the row shows at a glance which
                // ones are pinned, without needing a legend.
                NetCookSlotInfo info = this.netCookSlotInfoBuffer[i];

                // With an item pinned the button IS the item: its icon replaces the number. Without
                // one it stays a numbered slot, which is also what tells you the slot is free.
                Texture2D slotTex = null;
                if (info.PreferredStaticId > 0)
                {
                    this.TryGetNetCookRecipeTexture(info.PreferredStaticId, out slotTex);
                }

                int slotTexId = slotTex != null ? slotTex.GetInstanceID() : 0;
                if (i < handle.SlotButtonIcons.Count && slotTexId != handle.SlotButtonIconTexIds[i])
                {
                    handle.SlotButtonIconTexIds[i] = slotTexId;
                    Image icon = handle.SlotButtonIcons[i];
                    if (icon != null)
                    {
                        icon.sprite = slotTex != null ? this.GetOrCreateUguiTransferSprite(slotTex) : null;
                        icon.enabled = icon.sprite != null;
                    }
                }

                bool iconShown = i < handle.SlotButtonIcons.Count
                    && handle.SlotButtonIcons[i] != null && handle.SlotButtonIcons[i].enabled;
                string label = iconShown
                    ? string.Empty
                    : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + (info.PreferredStaticId > 0 ? "*" : string.Empty);

                this.SetUguiButtonLabel(handle.SlotButtons[i], label);
            }

            SetUguiGoActive(handle.SlotBackButton, this.netCookSlotPickerIndex >= 0);
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

        // :235-237 — the SHARED open flag (file header); relayout on the click frame.
        private void OnUguiFeaturesMassCookRecipeHeaderClicked()
        {
            this.netCookRecipeDropdownOpen = !this.netCookRecipeDropdownOpen;
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
