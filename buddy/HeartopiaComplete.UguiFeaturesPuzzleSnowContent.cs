using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features rounds 2+3 of 8 (migration plan item 11), bundled
    // in one file because both are tiny: the PUZZLE sub-tab — DrawPuzzleTab→DrawPuzzleSection
    // (PuzzleNetFeature.cs:57-122), automationSubTab == 6 — and the SNOW SCULPTING sub-tab — the
    // inline automationSubTab == 2 branch (HeartopiaComplete.Gui.cs:1029-1078). Sub-tab display
    // order is the tabs list at HeartopiaComplete.cs:2378-2385 ({"Main","Food & Repair",
    // "Snow Sculpting","Auto Buy","Auto Sell","Mass Cook","Puzzle","Pet Care"}), whose display
    // indices match automationSubTab 0-7 exactly — so Snow Sculpting = display 2, Puzzle =
    // display 6 (Gui.cs:1291-1293 routes == 6 to DrawPuzzleTab). The remaining five subs
    // (Food & Repair 1, Auto Buy 3, Auto Sell 4, Mass Cook 5, Pet Care 7) are separate future
    // rounds and keep the shell placeholder. NOT to be confused with the already-shipped SAND
    // Sculpture round (New Features tab, UguiSandSculptureContent.cs) — different feature.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same methods (all
    //    this.-accessible partial-class state; ZERO backend interop additions). Two independent
    //    rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesPuzzleSubIndex / UguiShellHobbySnowSubIndex,
    //    declared with their siblings in UguiShellTabIndices.cs), never label comparison. Both
    //    processors gate on IsUguiShellFeaturesSubTabActive (Puzzle) and
    //    IsUguiShellHobbySubTabActive (Snow).
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs these builders).
    //
    // PUZZLE UGUI (2026-11 sand-like refresh — BuildUguiShellFeaturesPuzzleContent):
    //  Flat scroll column (UguiSandSculptureContent idiom): localized header, Auto Puzzle toggle +
    //  run-status label, auto-place / auto-collect toggles, radius slider [1..30] + value label,
    //  plain status box (pieces/sent, puzzle status, conditional place/collect lines),
    //  primary "Move unfinished puzzles to backpack" button + conditional move-status label.
    //  Auto Puzzle still routes through SetPuzzleAutoEnabled(notify:true); place/collect are field
    //  writes with attempt/timer resets and green/red notifications (sand idiom).
    //  puzzleUiOpenLogged is deliberately NOT touched (IMGUI first-draw breadcrumb only).
    //
    // SNOW SCULPTING source nuances verified against the branch, replayed exactly:
    //  - The toggle label used to read "? Auto Snow Sculpture". That ? really is U+003F in
    //    the branch we ported from, and it was reproduced faithfully - but the branch was
    //    ALREADY WRONG. The localization tables carried "❄️ Auto Snow Sculpture"
    //    translated into all five languages right beside it, so the ? is a mangled snowflake
    //    and someone had patched the TABLE to match the mangling instead of fixing the label.
    //    The emoji is not restorable either: LiberationSans SDF is pinned for Latin and draws
    //    no pictographs, and no other live label in the mod uses one - the tables hold only
    //    ★ ✓ →. So the label is the plain "Auto Snow Sculpture" key, which already
    //    existed, is already translated, and is the name the keybind row uses for this feature.
    //  - Header goes through this.L ("AUTO SNOW SCULPTURE", :1032, bare GUI.skin.label →
    //    body-label role); the toggle is flag-only + a 2-color notification (:1036-1039,
    //    unlocalized message "Auto Snow Sculpture Enabled/Disabled", green/red) — no method
    //    call, no field resets.
    //  - The conditional status box (:1043, shown only while autoSnowEnabled) is a plain
    //    unstyled GUI.Box("") with the DEFAULT skin — the same "first GUI.Box" convention Sand
    //    Sculpture's round established for the kit (UguiSandSculptureContent.cs header):
    //    theme-derived ControlFill @ 0.55 alpha + a faint NEUTRAL gray ring (0.88,0.92,0.97,
    //    0.16) — visually distinct from the accent-/hairline-ringed themed cards. Its two
    //    lines are unlocalized interpolations: "Round: {n}/20  (total {m})" — TWO spaces
    //    before "(total", :1045 — and "API: {status}" (:1046), default-label color → uiText.
    //  - "Move snowballs to backpack" (:1054-1066, themePrimaryButtonStyle → primary button):
    //    TryMoveSnowballsWarehouseToBackpack(out moveStatus); on EITHER outcome the out-string
    //    is stored into snowMoveSnowballsStatus and shown as the notification — green
    //    (0.45,1,0.55) on true, red (1,0.55,0.55) on false. The trailing status label
    //    (:1069-1074, muted subTabText @ 0.92, wrapped 11) renders only while non-empty.
    //  - Two conditional blocks → relayout-on-signature (Foraging's idiom): box slot
    //    (+122 shown / +20 hidden) → button 280x32 (+38) → optional status label (+40);
    //    content height = final cursor + 16 (the source returns the bare cursor, no own pad).
    //  - 2026-07-25 additions (NEW functionality — no IMGUI source, kit idioms; the "zero
    //    backend additions" ground rule no longer holds for these): bag snowball counter
    //    (GetSnowballBagCountThrottled, 1s-throttled AuraMono GetUsableItemCount), snowball
    //    budget InputField (snowballUseLimit, 0 = all, IceSkating parse-clamp-writeback),
    //    two persisted pacing sliders (snowStartDelaySeconds / snowNextCycleDelaySeconds,
    //    0.05s snap), and the QTE-successes slider (snowQteSuccessCount 0-20 whole, = perfect
    //    reports per sculpture; legit session is 20 QTEs, so 20 = max quality, default).
    //    Fixed rows between toggle and box: bag 88 (+24) → limit 112 (+30) → sliders
    //    142/168/194 (+26 each) → box slot base moves 88→220, box 80→102 (3rd line
    //    "Balls used"). Relayout signature unchanged.
    //
    // Cross-surface sync cadence: toggles re-sync per gated frame via SetIsOnWithoutNotify
    // (SyncUguiToggleFromField); Puzzle's 3-way run status is a zero-alloc literal pick, and
    // Snow's move-status text is a raw field reference — both per-frame via the cached-string
    // compare. The allocating composite builds fed by background machinery (Puzzle
    // PIECES/SENT/puzzleStatus, Snow Round/API lines) ride the 0.5s slow tick (Sand
    // Sculpture's established idiom for exactly this shape). Handlers guard on "value actually
    // changed" (the kit checkbox fires onChanged once at build by design — the guard absorbs
    // it). Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ========================================================================================
        // PUZZLE (display sub-index 6)
        // ========================================================================================

        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellFeaturesPuzzleHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float BoxInnerW;

            public Toggle AutoToggle;             // routes through SetPuzzleAutoEnabled(value, true)

            public GameObject RunStatusLabel;     // 3-way literal pick (per-frame, zero-alloc)
            public string RunStatusShown;

            public Toggle AutoPlaceToggle;        // puzzleAutoPlace (flag + attempts reset)
            public Toggle AutoCollectToggle;      // puzzleAutoCollect (flag + timer reset)
            public Toggle SuppressUiToggle;       // puzzleSuppressUiPanels (Begin + Reward suppress)

            public Slider RadiusSlider;           // puzzleScanRadiusMeters, whole metres [1,30]
            public GameObject RadiusLabel;
            public string RadiusShown;

            public GameObject CountsLine;         // "Pieces: N   Sent: M" (0.5s tick)
            public string CountsShown;
            public GameObject StatusLine;         // wrapped puzzleStatus (0.5s tick)
            public string StatusShown;
            public GameObject PlaceLine;          // conditional (puzzleLastPlaceStatus non-empty)
            public string PlaceShown;
            public bool PlaceLineVisible;
            public GameObject CollectLine;        // conditional (puzzleLastCollectStatus non-empty)
            public string CollectShown;
            public bool CollectLineVisible;
            public bool CollectBelowPlace;        // collect slot: 94 under Place, else 66

            public GameObject MoveStatusLabel;    // conditional (puzzleMoveBoxesStatus non-empty)
            public string MoveShown;
            public bool MoveStatusVisible;

            public float NextSlowSyncAt;          // 0.5s tick (file header)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesPuzzleHandle uguiShellFeaturesPuzzle;

        // Content heights: button row end + 30 pad; the wrapped move-status label adds +44 (sand idiom).
        // +38 vs the pre-suppress layout for the Suppress puzzle UI panels toggle row.
        private const float UguiFeaturesPuzzleHeightBase = 438f;
        private const float UguiFeaturesPuzzleHeightWithMove = 482f;

        // ----------------------------------------------------------------------------------------
        // Live text builders
        // ----------------------------------------------------------------------------------------

        // The drawer's exact 3-way literal pick (PuzzleNetFeature.cs:100, raw GUI.Label —
        // unlocalized on purpose).
        private string BuildUguiFeaturesPuzzleRunStatusText()
        {
            return this.puzzleSolveRunning
                ? "Solving..."
                : (this.puzzleAutoEnabled ? "Waiting for puzzle target..." : "Disabled");
        }

        private string BuildUguiFeaturesPuzzleRadiusText()
        {
            return this.L("Puzzle scan radius") + ": "
                + Mathf.RoundToInt(this.GetPuzzleScanRadiusMeters()) + " m";
        }

        private string BuildUguiFeaturesPuzzleCountsText()
        {
            return "Pieces: " + this.puzzlePieces.Count + "   Sent: " + this.puzzleSentCount;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // Flat Sand Sculpture layout (UguiSandSculptureContent.cs): header, Auto Puzzle toggle +
        // run status, auto-place / auto-collect toggles, radius slider, plain status box (counts,
        // status, conditional place/collect lines), primary "Move unfinished puzzles to backpack"
        // button and a conditional move-status label. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesPuzzleContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesPuzzle = null;

            UguiShellFeaturesPuzzleHandle handle = new UguiShellFeaturesPuzzleHandle();
            GameObject block = this.CreateUguiGo("FeaturesPuzzleContent", parent);
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

            float contentWidth = w - 22f;      // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // full-width elements at x=8, 8px right margin

            Color bodyColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Header (bold 14 in the TEXT color, sand idiom) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header",
                this.L("PUZZLE"), 14f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, 460f, 24f);

            // -------- Auto Puzzle toggle + run status --------
            handle.AutoToggle = this.CreateUguiCheckbox(scrollContent, "AutoPuzzle",
                this.L("Auto Puzzle"), this.puzzleAutoEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPuzzleAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 8f, 42f, 250f, 30f);

            handle.RunStatusShown = this.BuildUguiFeaturesPuzzleRunStatusText();
            handle.RunStatusLabel = this.CreateUguiLabel(scrollContent, "RunStatus",
                handle.RunStatusShown, 12f, bodyColor, false);
            PlaceUguiTopLeft(handle.RunStatusLabel, 268f, 46f, Mathf.Max(120f, panelW - 268f), 22f);

            // -------- Auto-place toggle (resets puzzlePlaceAttempts) --------
            handle.AutoPlaceToggle = this.CreateUguiCheckbox(scrollContent, "AutoPlacePuzzle",
                this.L("Auto-place puzzle from backpack"), this.puzzleAutoPlace,
                new System.Action<bool>(this.OnUguiFeaturesPuzzleAutoPlaceToggled));
            PlaceUguiTopLeft(handle.AutoPlaceToggle.gameObject, 8f, 80f, 420f, 30f);

            // -------- Auto-collect toggle (resets puzzleCollectNextAt) --------
            handle.AutoCollectToggle = this.CreateUguiCheckbox(scrollContent, "AutoCollectPuzzle",
                this.L("Auto-collect finished puzzles"), this.puzzleAutoCollect,
                new System.Action<bool>(this.OnUguiFeaturesPuzzleAutoCollectToggled));
            PlaceUguiTopLeft(handle.AutoCollectToggle.gameObject, 8f, 118f, 420f, 30f);

            // -------- Suppress Begin/Reward panels (QuietPopups-style event swallow) --------
            handle.SuppressUiToggle = this.CreateUguiCheckbox(scrollContent, "SuppressPuzzleUi",
                this.L("Suppress puzzle UI panels"), this.puzzleSuppressUiPanels,
                new System.Action<bool>(this.OnUguiFeaturesPuzzleSuppressUiToggled));
            PlaceUguiTopLeft(handle.SuppressUiToggle.gameObject, 8f, 156f, 420f, 30f);

            // -------- Scan radius row (label 220x22, slider at +240, 200x18) --------
            handle.RadiusShown = this.BuildUguiFeaturesPuzzleRadiusText();
            handle.RadiusLabel = this.CreateUguiLabel(scrollContent, "RadiusLabel",
                handle.RadiusShown, 12f, bodyColor, false);
            PlaceUguiTopLeft(handle.RadiusLabel, 8f, 198f, 220f, 22f);
            handle.RadiusSlider = this.CreateUguiSlider(scrollContent, "RadiusSlider",
                PuzzleScanRadiusMin, PuzzleScanRadiusMax, Mathf.Round(this.GetPuzzleScanRadiusMeters()),
                true, new System.Action<float>(this.OnUguiFeaturesPuzzleRadiusChanged));
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 248f, 204f, 200f, 18f);

            // -------- Plain status box (sand idiom: ControlFill @ 0.55 + neutral gray ring) --------
            GameObject statusBox = this.CreateUguiGo("StatusBox", scrollContent);
            PlaceUguiTopLeft(statusBox, 8f, 236f, panelW, 130f);
            Color boxFill = this.UguiKitControlFill();
            this.AddUguiImage(statusBox, new Color(boxFill.r, boxFill.g, boxFill.b, 0.55f), true, 1f);
            this.AddUguiRingOverlay(statusBox, new Color(0.88f, 0.92f, 0.97f, 0.16f), 1f);

            float boxInnerW = panelW - 20f;
            handle.BoxInnerW = boxInnerW;

            handle.CountsShown = this.BuildUguiFeaturesPuzzleCountsText();
            handle.CountsLine = this.CreateUguiLabel(statusBox.transform, "CountsLine",
                handle.CountsShown, 12f, bodyColor, false);
            PlaceUguiTopLeft(handle.CountsLine, 10f, 8f, boxInnerW, 20f);

            handle.StatusShown = this.L("Status") + ": " + this.puzzleStatus;
            handle.StatusLine = this.CreateUguiLabel(statusBox.transform, "StatusLine",
                handle.StatusShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLine);
            PlaceUguiTopLeft(handle.StatusLine, 10f, 32f, boxInnerW, 34f);

            handle.PlaceLineVisible = !string.IsNullOrEmpty(this.puzzleLastPlaceStatus);
            handle.PlaceShown = this.L("Place") + ": " + this.puzzleLastPlaceStatus;
            handle.PlaceLine = this.CreateUguiLabel(statusBox.transform, "PlaceLine",
                handle.PlaceShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.PlaceLine);
            PlaceUguiTopLeft(handle.PlaceLine, 10f, 66f, boxInnerW, 30f);
            handle.PlaceLine.SetActive(handle.PlaceLineVisible);

            handle.CollectLineVisible = !string.IsNullOrEmpty(this.puzzleLastCollectStatus);
            handle.CollectBelowPlace = handle.PlaceLineVisible;
            handle.CollectShown = this.L("Collect") + ": " + this.puzzleLastCollectStatus;
            handle.CollectLine = this.CreateUguiLabel(statusBox.transform, "CollectLine",
                handle.CollectShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.CollectLine);
            PlaceUguiTopLeft(handle.CollectLine, 10f, handle.CollectBelowPlace ? 94f : 66f, boxInnerW, 30f);
            handle.CollectLine.SetActive(handle.CollectLineVisible);

            // -------- Move button (primary, NOT a toggle) + conditional wrapped status label --------
            GameObject moveButton = this.CreateUguiPrimaryButton(scrollContent, "MoveUnfinishedPuzzles",
                this.L("Move unfinished puzzles to backpack"),
                new System.Action(this.OnUguiFeaturesPuzzleMoveBoxesClicked));
            PlaceUguiTopLeft(moveButton, 8f, 376f, 280f, 32f);

            handle.MoveShown = this.puzzleMoveBoxesStatus;
            handle.MoveStatusLabel = this.CreateUguiLabel(scrollContent, "MoveStatus",
                handle.MoveShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.MoveStatusLabel);
            PlaceUguiTopLeft(handle.MoveStatusLabel, 8f, 416f, panelW, 36f);
            handle.MoveStatusVisible = !string.IsNullOrEmpty(handle.MoveShown);
            handle.MoveStatusLabel.SetActive(handle.MoveStatusVisible);

            this.SetUguiScrollContentHeight(scrollContent,
                handle.MoveStatusVisible ? UguiFeaturesPuzzleHeightWithMove : UguiFeaturesPuzzleHeightBase);

            handle.Root = block;
            this.uguiShellFeaturesPuzzle = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesPuzzleOnUpdate()
        {
            UguiShellFeaturesPuzzleHandle handle = this.uguiShellFeaturesPuzzle;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesPuzzleSubIndex))
            {
                return;
            }

            try
            {
                // Toggle/slider re-sync (external IMGUI edits, incl. ForceStopPuzzleAuto and
                // auto-place self-disable paths) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.puzzleAutoEnabled);
                this.SyncUguiToggleFromField(handle.AutoPlaceToggle, this.puzzleAutoPlace);
                this.SyncUguiToggleFromField(handle.AutoCollectToggle, this.puzzleAutoCollect);
                this.SyncUguiToggleFromField(handle.SuppressUiToggle, this.puzzleSuppressUiPanels);
                if (handle.RadiusSlider != null)
                {
                    float radius = Mathf.Round(this.GetPuzzleScanRadiusMeters());
                    if (Mathf.Abs(handle.RadiusSlider.value - radius) > 0.0005f)
                    {
                        handle.RadiusSlider.SetValueWithoutNotify(radius);
                    }
                }

                // 3-way run status: zero-alloc literal pick — per frame.
                this.SyncUguiSelfLabelText(handle.RunStatusLabel, ref handle.RunStatusShown,
                    this.BuildUguiFeaturesPuzzleRunStatusText());

                // Counters/statuses change from background machinery and allocate on build —
                // 0.5s tick (Sand Sculpture idiom).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.RadiusLabel, ref handle.RadiusShown,
                        this.BuildUguiFeaturesPuzzleRadiusText());
                    this.SyncUguiSelfLabelText(handle.CountsLine, ref handle.CountsShown,
                        this.BuildUguiFeaturesPuzzleCountsText());
                    this.SyncUguiSelfLabelText(handle.StatusLine, ref handle.StatusShown,
                        this.L("Status") + ": " + this.puzzleStatus);

                    // Conditional lines: text synced even while hidden (cheap, stays ready);
                    // visibility + the 66/94 slot ride cached flags (box is fixed-height).
                    bool placeVisible = !string.IsNullOrEmpty(this.puzzleLastPlaceStatus);
                    this.SyncUguiSelfLabelText(handle.PlaceLine, ref handle.PlaceShown,
                        this.L("Place") + ": " + this.puzzleLastPlaceStatus);
                    if (handle.PlaceLineVisible != placeVisible)
                    {
                        handle.PlaceLineVisible = placeVisible;
                        handle.PlaceLine.SetActive(placeVisible);
                    }

                    bool collectVisible = !string.IsNullOrEmpty(this.puzzleLastCollectStatus);
                    this.SyncUguiSelfLabelText(handle.CollectLine, ref handle.CollectShown,
                        this.L("Collect") + ": " + this.puzzleLastCollectStatus);
                    if (handle.CollectLineVisible != collectVisible)
                    {
                        handle.CollectLineVisible = collectVisible;
                        handle.CollectLine.SetActive(collectVisible);
                    }
                    if (handle.CollectBelowPlace != placeVisible)
                    {
                        handle.CollectBelowPlace = placeVisible;
                        PlaceUguiTopLeft(handle.CollectLine, 10f, placeVisible ? 94f : 66f,
                            handle.BoxInnerW, 30f);
                    }

                    // Move-status label: raw field ref; visibility + content-height swap.
                    bool moveVisible = !string.IsNullOrEmpty(this.puzzleMoveBoxesStatus);
                    this.SyncUguiSelfLabelText(handle.MoveStatusLabel, ref handle.MoveShown,
                        this.puzzleMoveBoxesStatus);
                    if (handle.MoveStatusVisible != moveVisible)
                    {
                        handle.MoveStatusVisible = moveVisible;
                        handle.MoveStatusLabel.SetActive(moveVisible);
                        this.SetUguiScrollContentHeight(handle.ScrollContent,
                            moveVisible ? UguiFeaturesPuzzleHeightWithMove : UguiFeaturesPuzzleHeightBase);
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Puzzle content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — all guarded on "value actually changed": the kit checkbox fires
        // onChanged once at build (by design) and these handlers post notifications.
        // ----------------------------------------------------------------------------------------

        // PuzzleNetFeature.cs:98-104 — on an ACTUAL change, route through the backend method with
        // notify: true, exactly like the IMGUI block. SetPuzzleAutoEnabled owns everything else
        // (field write, timer/probe resets, status, log, green/red notification, StopPuzzleSolve
        // on disable) — nothing reproduced here.
        private void OnUguiFeaturesPuzzleAutoToggled(bool value)
        {
            if (value == this.puzzleAutoEnabled)
            {
                return;
            }
            this.SetPuzzleAutoEnabled(value, true);
        }

        // Reset puzzlePlaceAttempts, THEN notification (sand auto-place idiom).
        private void OnUguiFeaturesPuzzleAutoPlaceToggled(bool value)
        {
            if (value == this.puzzleAutoPlace)
            {
                return;
            }
            this.puzzleAutoPlace = value;
            this.puzzlePlaceAttempts = 0;
            this.AddMenuNotification(
                this.L("Auto-place puzzle from backpack") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // Reset puzzleCollectNextAt, THEN notification (sand auto-collect idiom).
        private void OnUguiFeaturesPuzzleAutoCollectToggled(bool value)
        {
            if (value == this.puzzleAutoCollect)
            {
                return;
            }
            this.puzzleAutoCollect = value;
            this.puzzleCollectNextAt = 0f;
            this.AddMenuNotification(
                this.L("Auto-collect finished puzzles") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // Routes through SetPuzzleSuppressUiPanels so hooks + suppress-forward apply immediately.
        private void OnUguiFeaturesPuzzleSuppressUiToggled(bool value)
        {
            if (value == this.puzzleSuppressUiPanels)
            {
                return;
            }
            this.SetPuzzleSuppressUiPanels(value, true);
        }

        // Whole metres, clamped to the feature range, equal-guard, refresh the value label.
        private void OnUguiFeaturesPuzzleRadiusChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value), PuzzleScanRadiusMin, PuzzleScanRadiusMax);
            if (Mathf.Abs(rounded - this.puzzleScanRadiusMeters) <= 0.0001f)
            {
                return;
            }
            this.puzzleScanRadiusMeters = rounded;
            UguiShellFeaturesPuzzleHandle handle = this.uguiShellFeaturesPuzzle;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.RadiusLabel, ref handle.RadiusShown,
                    this.BuildUguiFeaturesPuzzleRadiusText());
            }
        }

        // The SAME out-string is stored into puzzleMoveBoxesStatus AND shown as the notification
        // on both outcomes; green on true, red on false (snow/sand move-button idiom).
        private void OnUguiFeaturesPuzzleMoveBoxesClicked()
        {
            if (this.TryMoveUnfinishedPuzzleBoxesWarehouseToBackpack(out string moveStatus))
            {
                this.puzzleMoveBoxesStatus = moveStatus;
                this.AddMenuNotification(moveStatus, UguiSandSculptureOkColor);
            }
            else
            {
                this.puzzleMoveBoxesStatus = moveStatus;
                this.AddMenuNotification(moveStatus, UguiSandSculptureOffColor);
            }
        }

        // ========================================================================================
        // SNOW SCULPTING (display sub-index 2)
        // ========================================================================================

        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellFeaturesSnowHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;                  // full-width role (source's 520 column)

            public Toggle AutoToggle;             // flag + 2-color notification only
            public Toggle CollectToggle;          // Auto collect snowballs (persisted flag)

            // 2026-07-25 additions (new functionality, no IMGUI source): bag counter, snowball
            // budget field, and the two pacing sliders. All fixed-position (built between the
            // toggle and the status box), so the relayout signature is unchanged.
            public GameObject BagCountLabel;      // "Snowballs in bag: N" (0.5s tick, 1s scan throttle)
            public string BagCountShown;
            public InputField LimitField;         // budget, 0 = all (parse-clamp-writeback idiom)
            public string LimitSeen;
            public Slider StartDelaySlider;       // snowStartDelaySeconds (persisted)
            public GameObject StartDelayLabel;
            public string StartDelayShown;
            public Slider NextDelaySlider;        // snowNextCycleDelaySeconds (persisted)
            public GameObject NextDelayLabel;
            public string NextDelayShown;
            public Slider QteCountSlider;         // snowQteSuccessCount 0-20 (persisted)
            public GameObject QteCountLabel;
            public string QteCountShown;

            // Auto collect snowballs (fixed rows, same reason as above: the relayout signature
            // only packs the conditional rows below them, so these need no new bit).
            public GameObject CollectStatusLabel; // "Snow machines: N | bag N | collected N" / "Collector: off" (0.5s tick)
            public string CollectStatusShown;
            public Slider ScanSlider;             // snowMachineScanSeconds (persisted)
            public GameObject ScanLabel;
            public string ScanShown;
            public Slider RadiusSlider;           // snowMachineScanRadius (persisted)
            public GameObject RadiusLabel;
            public string RadiusShown;

            public GameObject StatusBox;          // conditional plain box (autoSnowEnabled)
            public GameObject RoundLine;          // "Round: {n}/20  (total {m})" (0.5s tick)
            public string RoundShown;
            public GameObject ApiLine;            // "API: {status}" (0.5s tick)
            public string ApiShown;
            public GameObject BallsLine;          // "Balls used: X ..." (0.5s tick)
            public string BallsShown;

            public GameObject MoveButton;         // repositions with the box's visibility
            public GameObject MoveStatusLabel;    // conditional (snowMoveSnowballsStatus non-empty)
            public string MoveStatusShown;

            public int LayoutSignature = -1;      // packed autoSnowEnabled/has-move-status
            public float NextSlowSyncAt;          // 0.5s tick for the box lines (file header)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesSnowHandle uguiShellFeaturesSnow;

        // ----------------------------------------------------------------------------------------
        // Live text builders — IMGUI strings verbatim (both raw unlocalized interpolations)
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFeaturesSnowRoundText()
        {
            // Gui.cs:1045 shape — TWO spaces before "(total"; the /20 literal became the
            // QTE-successes round target (2026-07-25).
            return $"Round: {this.snowApiRoundCount}/{this.SnowApiRoundTarget}  (total {this.snowClickCount})";
        }

        private string BuildUguiFeaturesSnowApiText()
        {
            // Gui.cs:1046.
            return $"API: {this.snowSculptureLastActionStatus}";
        }

        private string BuildUguiFeaturesSnowBagCountText()
        {
            int count = this.GetSnowballBagCountThrottled();
            return this.LF("Snowballs in bag: {0}", count < 0 ? "?" : count.ToString());
        }

        private string BuildUguiFeaturesSnowStartDelayText()
        {
            return this.LF("Start delay: {0}s", this.snowStartDelaySeconds.ToString("F2"));
        }

        private string BuildUguiFeaturesSnowNextDelayText()
        {
            return this.LF("Next-cycle delay: {0}s", this.snowNextCycleDelaySeconds.ToString("F2"));
        }

        private string BuildUguiFeaturesSnowQteCountText()
        {
            return this.LF("QTE successes: {0}", this.snowQteSuccessCount);
        }

        // Auto collect status. The bag number is the same 1s-throttled read the sculpture bag
        // counter uses, so this line costs nothing extra on the 0.5s tick. The collector itself
        // does not stop on a bag count.
        private string BuildUguiFeaturesSnowCollectText()
        {
            if (!this.autoSnowCollectEnabled)
            {
                return this.L("Collector: off");
            }

            int bag = this.GetSnowballBagCountThrottled();
            return this.LF("Snow machines: {0} | bag {1} | collected {2}",
                this.snowAutoCollectMachinesSeen,
                bag < 0 ? "?" : bag.ToString(),
                this.snowballsCollectedThisRun);
        }

        private string BuildUguiFeaturesSnowScanIntervalText()
        {
            return this.LF("Scan interval: {0}s", this.snowMachineScanSeconds.ToString("F2"));
        }

        private string BuildUguiFeaturesSnowScanRadiusText()
        {
            return this.LF("Scan radius: {0} m", Mathf.RoundToInt(this.snowMachineScanRadius));
        }

        private string BuildUguiFeaturesSnowBallsText()
        {
            return this.snowballUseLimit > 0
                ? this.LF("Balls used: {0} / {1}", this.snowballsUsedThisRun, this.snowballUseLimit)
                : this.LF("Balls used: {0} (no limit)", this.snowballsUsedThisRun);
        }

        // Conditional rows only: the 3-line status box (autoSnowEnabled) and the move-status label. The
        // auto-collect rows and the sculpture controls above the box are fixed-height rows built in
        // the builder, so they need no bit here — the relayout starts its cursor at the box slot (350).
        private int ComputeUguiFeaturesSnowLayoutSignature()
        {
            return (this.autoSnowEnabled ? 1 : 0)
                 | (!string.IsNullOrEmpty(this.snowMoveSnowballsStatus) ? 2 : 0);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of the automationSubTab == 2 branch: header, toggle, conditional plain
        // status box (2 live lines), move button, conditional move-status label. All controls —
        // including conditionally-visible ones — are built ONCE here in IMGUI source order;
        // RelayoutUguiShellFeaturesSnow owns the conditional positions/visibility (the y-cursor
        // accumulation analog). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesSnowSculptingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesSnow = null;

            UguiShellFeaturesSnowHandle handle = new UguiShellFeaturesSnowHandle();
            GameObject block = this.CreateUguiGo("FeaturesSnowSculptingContent", parent);
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

            float contentWidth = w - 22f;          // viewport insets: 4 left + 18 right
            handle.PanelW = contentWidth - 16f;    // full-width elements at x=8, 8px right margin

            // Source text roles: box lines/header = bare GUI.skin.label → uiText; move status =
            // subTabText @ 0.92 wordWrap (Gui.cs:1071-1072), built verbatim.
            Color textColor = this.UguiKitTextColor();
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Header (:1032 — localized, bare label role, 360x30) --------
            GameObject header = this.CreateUguiBodyLabel(scrollContent, "Header",
                this.L("AUTO SNOW SCULPTURE"), 13f);
            PlaceUguiTopLeft(header, 8f, 8f, 360f, 30f);

            // -------- Auto collect snowballs sits above Auto Snow Sculpture. Own toggle, own
            // persisted flag; it does not imply the sculptor and it does not stop on a bag count.
            handle.CollectToggle = this.CreateUguiCheckbox(scrollContent, "AutoSnowCollect",
                this.L("Auto collect snowballs"), this.autoSnowCollectEnabled,
                new System.Action<bool>(this.OnUguiFeaturesSnowAutoCollectToggled));
            PlaceUguiTopLeft(handle.CollectToggle.gameObject, 8f, 48f, 360f, 30f);

            handle.CollectStatusShown = this.BuildUguiFeaturesSnowCollectText();
            handle.CollectStatusLabel = this.CreateUguiLabel(scrollContent, "CollectStatus",
                handle.CollectStatusShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.CollectStatusLabel, 8f, 88f, handle.PanelW, 20f);

            handle.ScanShown = this.BuildUguiFeaturesSnowScanIntervalText();
            handle.ScanLabel = this.CreateUguiLabel(scrollContent, "ScanIntervalLabel",
                handle.ScanShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.ScanLabel, 8f, 112f, 200f, 22f);
            handle.ScanSlider = this.CreateUguiSlider(scrollContent, "ScanIntervalSlider",
                SnowMachineScanMin, SnowMachineScanMax, this.snowMachineScanSeconds,
                false, new System.Action<float>(this.OnUguiFeaturesSnowScanIntervalChanged));
            PlaceUguiTopLeft(handle.ScanSlider.gameObject, 216f, 115f, handle.PanelW - 216f, 20f);

            handle.RadiusShown = this.BuildUguiFeaturesSnowScanRadiusText();
            handle.RadiusLabel = this.CreateUguiLabel(scrollContent, "ScanRadiusLabel",
                handle.RadiusShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.RadiusLabel, 8f, 138f, 200f, 22f);
            handle.RadiusSlider = this.CreateUguiSlider(scrollContent, "ScanRadiusSlider",
                SnowMachineRadiusMin, SnowMachineRadiusMax, this.snowMachineScanRadius,
                true, new System.Action<float>(this.OnUguiFeaturesSnowScanRadiusChanged));
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 216f, 141f, handle.PanelW - 216f, 20f);

            // -------- Auto Snow Sculpture toggle, then its own controls (bag, limit, pacing,
            // QTE, status box). Flag-only + notification, 360x30. The "? " prefix is dropped.
            handle.AutoToggle = this.CreateUguiCheckbox(scrollContent, "AutoSnow",
                this.L("Auto Snow Sculpture"), this.autoSnowEnabled,
                new System.Action<bool>(this.OnUguiFeaturesSnowAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 8f, 178f, 360f, 30f);

            handle.BagCountShown = this.BuildUguiFeaturesSnowBagCountText();
            handle.BagCountLabel = this.CreateUguiLabel(scrollContent, "BagCount",
                handle.BagCountShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.BagCountLabel, 8f, 218f, handle.PanelW, 20f);

            GameObject limitCaption = this.CreateUguiLabel(scrollContent, "LimitCaption",
                this.L("Snowball limit (0 = all)"), 12f, textColor, false);
            PlaceUguiTopLeft(limitCaption, 8f, 242f, 170f, 22f);
            handle.LimitSeen = this.snowballUseLimit.ToString();
            handle.LimitField = this.CreateUguiInputField(scrollContent, "LimitField",
                handle.LimitSeen, 6, new System.Action<string>(this.OnUguiFeaturesSnowLimitEdited));
            PlaceUguiTopLeft(handle.LimitField.gameObject, 186f, 242f, 80f, 22f);

            handle.StartDelayShown = this.BuildUguiFeaturesSnowStartDelayText();
            handle.StartDelayLabel = this.CreateUguiLabel(scrollContent, "StartDelayLabel",
                handle.StartDelayShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.StartDelayLabel, 8f, 272f, 200f, 22f);
            handle.StartDelaySlider = this.CreateUguiSlider(scrollContent, "StartDelaySlider",
                SnowStartDelayMin, SnowStartDelayMax, this.snowStartDelaySeconds,
                false, new System.Action<float>(this.OnUguiFeaturesSnowStartDelayChanged));
            PlaceUguiTopLeft(handle.StartDelaySlider.gameObject, 216f, 275f, handle.PanelW - 216f, 20f);

            handle.NextDelayShown = this.BuildUguiFeaturesSnowNextDelayText();
            handle.NextDelayLabel = this.CreateUguiLabel(scrollContent, "NextDelayLabel",
                handle.NextDelayShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.NextDelayLabel, 8f, 298f, 200f, 22f);
            handle.NextDelaySlider = this.CreateUguiSlider(scrollContent, "NextDelaySlider",
                SnowNextCycleDelayMin, SnowNextCycleDelayMax, this.snowNextCycleDelaySeconds,
                false, new System.Action<float>(this.OnUguiFeaturesSnowNextDelayChanged));
            PlaceUguiTopLeft(handle.NextDelaySlider.gameObject, 216f, 301f, handle.PanelW - 216f, 20f);

            handle.QteCountShown = this.BuildUguiFeaturesSnowQteCountText();
            handle.QteCountLabel = this.CreateUguiLabel(scrollContent, "QteCountLabel",
                handle.QteCountShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.QteCountLabel, 8f, 324f, 200f, 22f);
            handle.QteCountSlider = this.CreateUguiSlider(scrollContent, "QteCountSlider",
                SnowQteSuccessMin, SnowQteSuccessMax, this.snowQteSuccessCount,
                true, new System.Action<float>(this.OnUguiFeaturesSnowQteCountChanged));
            PlaceUguiTopLeft(handle.QteCountSlider.gameObject, 216f, 327f, handle.PanelW - 216f, 20f);

            // -------- Conditional plain status box (:1043 — DEFAULT GUI.skin.box; Sand
            // Sculpture's plain-box convention: ControlFill @ 0.55 + neutral gray ring). 3
            // lines, 102 high; sits below the sculpture controls at 350. ----
            handle.StatusBox = this.CreateUguiGo("StatusBox", scrollContent);
            PlaceUguiTopLeft(handle.StatusBox, 8f, 350f, handle.PanelW, 102f);
            Color boxFill = this.UguiKitControlFill();
            this.AddUguiImage(handle.StatusBox, new Color(boxFill.r, boxFill.g, boxFill.b, 0.55f), true, 1f);
            this.AddUguiRingOverlay(handle.StatusBox, new Color(0.88f, 0.92f, 0.97f, 0.16f), 1f);

            // Lines at box-local +10/+8, +10/+30, +10/+52, 400x22, fontSize 12 (:1044-1046 — the
            // 400 width is absolute in the source, kept).
            handle.RoundShown = this.BuildUguiFeaturesSnowRoundText();
            handle.RoundLine = this.CreateUguiLabel(handle.StatusBox.transform, "RoundLine",
                handle.RoundShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.RoundLine, 10f, 8f, 400f, 22f);

            handle.ApiShown = this.BuildUguiFeaturesSnowApiText();
            handle.ApiLine = this.CreateUguiLabel(handle.StatusBox.transform, "ApiLine",
                handle.ApiShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.ApiLine, 10f, 30f, 400f, 22f);

            handle.BallsShown = this.BuildUguiFeaturesSnowBallsText();
            handle.BallsLine = this.CreateUguiLabel(handle.StatusBox.transform, "BallsLine",
                handle.BallsShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.BallsLine, 10f, 52f, 400f, 22f);

            // -------- Move button (:1054 — themePrimaryButtonStyle, 280x32; position owned by
            // the relayout because the box above it comes and goes) --------
            handle.MoveButton = this.CreateUguiPrimaryButton(scrollContent, "MoveSnowballs",
                this.L("Move snowballs to backpack"),
                new System.Action(this.OnUguiFeaturesSnowMoveSnowballsClicked));

            // -------- Conditional move-status label (:1069-1074 — wrapped 11, muted) --------
            handle.MoveStatusShown = this.snowMoveSnowballsStatus;
            handle.MoveStatusLabel = this.CreateUguiLabel(scrollContent, "MoveStatus",
                handle.MoveStatusShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.MoveStatusLabel);

            handle.LayoutSignature = this.ComputeUguiFeaturesSnowLayoutSignature();
            this.RelayoutUguiShellFeaturesSnow(handle);

            handle.Root = block;
            this.uguiShellFeaturesSnow = handle;
            return block;
        }

        // Positions the conditional rows from the CURRENT autoSnowEnabled / move-status state.
        // Fixed rows above the cursor: header 8 → collect toggle 48 → collect status 88 →
        // scan/radius 112/138 → Auto Snow toggle 178 → bag 218 → limit 242 → pacing 272/298/324
        // → box slot 350 (+122 shown, 102-high 3-line box / +20 hidden) → button (+38) →
        // optional status label (+40). Reposition/SetActive only; nothing is rebuilt.
        private void RelayoutUguiShellFeaturesSnow(UguiShellFeaturesSnowHandle handle)
        {
            bool autoOn = this.autoSnowEnabled;
            bool hasMoveStatus = !string.IsNullOrEmpty(this.snowMoveSnowballsStatus);

            float yCur = 350f;
            SetUguiGoActive(handle.StatusBox, autoOn);
            if (autoOn)
            {
                PlaceUguiTopLeft(handle.StatusBox, 8f, yCur, handle.PanelW, 102f);
                yCur += 122f;
            }
            else
            {
                yCur += 20f;
            }

            if (handle.MoveButton != null)
            {
                PlaceUguiTopLeft(handle.MoveButton, 8f, yCur, 280f, 32f);
            }
            yCur += 38f;

            SetUguiGoActive(handle.MoveStatusLabel, hasMoveStatus);
            if (hasMoveStatus)
            {
                PlaceUguiTopLeft(handle.MoveStatusLabel, 8f, yCur, handle.PanelW, 36f);
                yCur += 40f;
            }

            // The source returns the bare cursor (no own pad, :1077); +16 scroll comfort margin
            // (round-1 convention).
            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 16f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesSnowSculptingOnUpdate()
        {
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellHobbySubTabActive(UguiShellHobbySnowSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-sync (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.autoSnowEnabled);
                this.SyncUguiToggleFromField(handle.CollectToggle, this.autoSnowCollectEnabled);

                // Move-status text: raw field reference, zero-alloc — per frame. Synced even
                // while hidden (cheap, stays ready — Sand idiom).
                this.SyncUguiSelfLabelText(handle.MoveStatusLabel, ref handle.MoveStatusShown,
                    this.snowMoveSnowballsStatus);

                // The box lines change from the feature's background machinery and their
                // texts are allocating interpolations — 0.5s tick (file header). The bag counter
                // rides the same tick (its backing read is 1s-throttled internally), and the
                // sliders/field re-sync WithoutNotify against external edits (config load).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.RoundLine, ref handle.RoundShown,
                        this.BuildUguiFeaturesSnowRoundText());
                    this.SyncUguiSelfLabelText(handle.ApiLine, ref handle.ApiShown,
                        this.BuildUguiFeaturesSnowApiText());
                    this.SyncUguiSelfLabelText(handle.BallsLine, ref handle.BallsShown,
                        this.BuildUguiFeaturesSnowBallsText());
                    this.SyncUguiSelfLabelText(handle.BagCountLabel, ref handle.BagCountShown,
                        this.BuildUguiFeaturesSnowBagCountText());
                    this.SyncUguiSelfLabelText(handle.CollectStatusLabel, ref handle.CollectStatusShown,
                        this.BuildUguiFeaturesSnowCollectText());
                    this.SyncUguiSelfLabelText(handle.ScanLabel, ref handle.ScanShown,
                        this.BuildUguiFeaturesSnowScanIntervalText());
                    if (handle.ScanSlider != null
                        && Mathf.Abs(handle.ScanSlider.value - this.snowMachineScanSeconds) > 0.0005f)
                    {
                        handle.ScanSlider.SetValueWithoutNotify(this.snowMachineScanSeconds);
                    }
                    this.SyncUguiSelfLabelText(handle.RadiusLabel, ref handle.RadiusShown,
                        this.BuildUguiFeaturesSnowScanRadiusText());
                    if (handle.RadiusSlider != null
                        && Mathf.Abs(handle.RadiusSlider.value - this.snowMachineScanRadius) > 0.0005f)
                    {
                        handle.RadiusSlider.SetValueWithoutNotify(this.snowMachineScanRadius);
                    }
                    this.SyncUguiSelfLabelText(handle.StartDelayLabel, ref handle.StartDelayShown,
                        this.BuildUguiFeaturesSnowStartDelayText());
                    this.SyncUguiSelfLabelText(handle.NextDelayLabel, ref handle.NextDelayShown,
                        this.BuildUguiFeaturesSnowNextDelayText());
                    SyncUguiInputFieldFromBackingField(handle.LimitField, ref handle.LimitSeen,
                        this.snowballUseLimit.ToString());
                    if (handle.StartDelaySlider != null
                        && Mathf.Abs(handle.StartDelaySlider.value - this.snowStartDelaySeconds) > 0.0005f)
                    {
                        handle.StartDelaySlider.SetValueWithoutNotify(this.snowStartDelaySeconds);
                    }
                    if (handle.NextDelaySlider != null
                        && Mathf.Abs(handle.NextDelaySlider.value - this.snowNextCycleDelaySeconds) > 0.0005f)
                    {
                        handle.NextDelaySlider.SetValueWithoutNotify(this.snowNextCycleDelaySeconds);
                    }
                    this.SyncUguiSelfLabelText(handle.QteCountLabel, ref handle.QteCountShown,
                        this.BuildUguiFeaturesSnowQteCountText());
                    if (handle.QteCountSlider != null
                        && Mathf.Abs(handle.QteCountSlider.value - this.snowQteSuccessCount) > 0.0005f)
                    {
                        handle.QteCountSlider.SetValueWithoutNotify(this.snowQteSuccessCount);
                    }
                }

                // Conditional-layout signature (status box + move-status label).
                int signature = this.ComputeUguiFeaturesSnowLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesSnow(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Snow Sculpting content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // ----------------------------------------------------------------------------------------

        // Gui.cs:1034-1039 — flag write + the unlocalized 2-color notification; no method call,
        // no field resets. The equal-guard is the UGUI analog of IMGUI's prev-vs-new check (and
        // absorbs the kit checkbox's build-time onChanged fire).
        private void OnUguiFeaturesSnowAutoToggled(bool value)
        {
            if (value == this.autoSnowEnabled)
            {
                return;
            }
            this.autoSnowEnabled = value;
            this.AddMenuNotification(
                $"Auto Snow Sculpture {(this.autoSnowEnabled ? "Enabled" : "Disabled")}",
                this.autoSnowEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Auto collect snowballs — the same flag + 2-color notification shape as the toggle above,
        // plus the persist the plan asks for (autoSnowCollectEnabled IS a saved setting, unlike
        // autoSnowEnabled). Clearing per-machine state is NOT duplicated here: the collector's own
        // falling edge in ProcessSnowAutoCollectOnUpdate owns it, so a config-file change clears too.
        private void OnUguiFeaturesSnowAutoCollectToggled(bool value)
        {
            if (value == this.autoSnowCollectEnabled)
            {
                return;
            }
            this.autoSnowCollectEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
            this.AddMenuNotification(
                $"Auto Collect Snowballs {(this.autoSnowCollectEnabled ? "Enabled" : "Disabled")}",
                this.autoSnowCollectEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Pacing sliders (2026-07-25): snap to the nearest 0.05 s (the Pictures delay-slider
        // granularity), equal-guard, persist via SaveKeybinds(false), refresh the value label.
        private void OnUguiFeaturesSnowStartDelayChanged(float value)
        {
            float snapped = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, SnowStartDelayMin, SnowStartDelayMax);
            if (Mathf.Abs(snapped - this.snowStartDelaySeconds) <= 0.0001f)
            {
                return;
            }
            this.snowStartDelaySeconds = snapped;
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.StartDelayLabel, ref handle.StartDelayShown,
                    this.BuildUguiFeaturesSnowStartDelayText());
            }
        }

        private void OnUguiFeaturesSnowNextDelayChanged(float value)
        {
            float snapped = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, SnowNextCycleDelayMin, SnowNextCycleDelayMax);
            if (Mathf.Abs(snapped - this.snowNextCycleDelaySeconds) <= 0.0001f)
            {
                return;
            }
            this.snowNextCycleDelaySeconds = snapped;
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.NextDelayLabel, ref handle.NextDelayShown,
                    this.BuildUguiFeaturesSnowNextDelayText());
            }
        }

        // QTE-successes slider (2026-07-25): whole numbers 0-20, equal-guard, persist. Fewer
        // perfects = lower accumulated score = lower-tier sculpture (legit session = 20 QTEs,
        // so 20 = the original always-max-quality behavior).
        private void OnUguiFeaturesSnowQteCountChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value), SnowQteSuccessMin, SnowQteSuccessMax);
            if (rounded == this.snowQteSuccessCount)
            {
                return;
            }
            this.snowQteSuccessCount = rounded;
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.QteCountLabel, ref handle.QteCountShown,
                    this.BuildUguiFeaturesSnowQteCountText());
            }
        }

        // Collector scan-interval slider: snap to the nearest 0.05 s like the pacing sliders, clamp
        // to the feature's range (the tick clamps again defensively), equal-guard, persist.
        private void OnUguiFeaturesSnowScanIntervalChanged(float value)
        {
            float snapped = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, SnowMachineScanMin, SnowMachineScanMax);
            if (Mathf.Abs(snapped - this.snowMachineScanSeconds) <= 0.0001f)
            {
                return;
            }
            this.snowMachineScanSeconds = snapped;
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.ScanLabel, ref handle.ScanShown,
                    this.BuildUguiFeaturesSnowScanIntervalText());
            }
        }

        // Collector scan-radius slider: whole metres only (the slider is built with wholeNumbers),
        // clamp to the feature's range, equal-guard, persist.
        private void OnUguiFeaturesSnowScanRadiusChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value), SnowMachineRadiusMin, SnowMachineRadiusMax);
            if (Mathf.Abs(rounded - this.snowMachineScanRadius) <= 0.0001f)
            {
                return;
            }
            this.snowMachineScanRadius = rounded;
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle != null)
            {
                this.SyncUguiSelfLabelText(handle.RadiusLabel, ref handle.RadiusShown,
                    this.BuildUguiFeaturesSnowScanRadiusText());
            }
        }

        // Snowball budget (2026-07-25) — the IceSkating ChallengeScore parse-clamp-writeback
        // idiom: persist only on an actual value change; non-numeric text is left as typed
        // (Seen cache) so the 0.5s re-sync doesn't clobber in-progress editing.
        private void OnUguiFeaturesSnowLimitEdited(string text)
        {
            UguiShellFeaturesSnowHandle handle = this.uguiShellFeaturesSnow;
            if (handle == null)
            {
                return;
            }
            string raw = text ?? string.Empty;
            if (int.TryParse(raw, out int parsed))
            {
                int prev = this.snowballUseLimit;
                this.snowballUseLimit = Mathf.Clamp(parsed, 0, 999999);
                if (this.snowballUseLimit != prev)
                {
                    try { this.SaveKeybinds(false); } catch { }
                }
                string normalized = this.snowballUseLimit.ToString();
                handle.LimitSeen = normalized;
                if (handle.LimitField != null
                    && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { handle.LimitField.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                handle.LimitSeen = raw;
            }
        }

        // Gui.cs:1054-1066 — the SAME out-string is stored into snowMoveSnowballsStatus AND shown
        // as the notification on BOTH outcomes; green on true, red on false. The full two-branch
        // shape is kept verbatim (not collapsed) to mirror the source exactly.
        private void OnUguiFeaturesSnowMoveSnowballsClicked()
        {
            if (this.TryMoveSnowballsWarehouseToBackpack(out string moveStatus))
            {
                this.snowMoveSnowballsStatus = moveStatus;
                this.AddMenuNotification(moveStatus, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.snowMoveSnowballsStatus = moveStatus;
                this.AddMenuNotification(moveStatus, new Color(1f, 0.55f, 0.55f));
            }
        }
    }
}
