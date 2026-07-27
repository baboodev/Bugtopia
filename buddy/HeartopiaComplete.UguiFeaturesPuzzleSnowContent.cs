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
    //    UguiShellFeaturesPuzzleSubIndex = 6 / UguiShellFeaturesSnowSculptingSubIndex = 2,
    //    declared with their siblings in UguiShellTabIndices.cs), never label comparison. Both
    //    processors gate on the SAME IsUguiShellFeaturesSubTabActive function the Main round
    //    established — no new gate.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs these builders).
    //
    // PUZZLE source nuances verified against the drawer, replayed exactly:
    //  - Header "PUZZLE" (:91) is an UNLOCALIZED literal in bold 15 WHITE (headerStyle sets
    //    Color.white explicitly, :75-76 — not the uiHeader color CreateUguiHeaderLabel would
    //    use), so it is a plain CreateUguiLabel + bold. "STATUS"/"PIECES"/"SENT" and the 3-way
    //    run status ("Solving..." / "Waiting for puzzle target..." / "Disabled", :100) are raw
    //    unlocalized literals too; only the toggle label goes through L (DrawSwitchToggle
    //    localizes internally, UiKitPrimitives.cs:763).
    //  - The toggle routes through a METHOD, not a raw field write: on an actual change the
    //    IMGUI block calls this.SetPuzzleAutoEnabled(newValue, true) (:101-104). The UGUI
    //    handler calls that same method with notify: true — its internals (attempt-timer +
    //    probe-flag resets, status message, log, the direction-dependent green/red
    //    AddMenuNotification pair, StopPuzzleSolve on disable, :124-153) are NOT reproduced.
    //  - Both cards are themePanelStyle boxes + DrawCardOutline (:94-96, :108-110) → the
    //    established PanelBg fill + white hairline ring at clamp(0.05 + uiPanelAlpha*0.05,
    //    .05, .10) (Radar Range card / Bag-Warehouse selected card precedent). The two
    //    DrawPuzzleStatBox boxes (:161-166) use themeTopTabStyle (NO DrawCardOutline) → the
    //    Radar dropdown-header mapping: controlFill @ clamp(uiPanelAlpha, .25, 1) + the style's
    //    baked white .08 ring only, no extra hairline. mutedTextColor = uiText @ 0.78 (:73);
    //    stat values + header are white; status texts are uiText.
    //  - puzzleUiOpenLogged (:64-68) is deliberately NOT touched: it is a first-IMGUI-draw
    //    diagnostic breadcrumb, and setting the shared once-flag from this surface would
    //    suppress the IMGUI side's log — a cross-surface behavior change this port avoids.
    //  - Layout is FIXED (no conditionals): header y=8 (+42) → toggle card y=50 h=52 (+70) →
    //    status card y=120 h=164 (+170, return +20) → content height 310. Card width: the
    //    source's uniform 520-wide column is a full-width role → panelW; card-internal
    //    positions replay the source's relative formulas (toggle 14/14 250x26, run status at
    //    +284 w-300; caption 12/8; stat boxes at 12 and 24+statW with statW=(w-36)/2, y=34
    //    h=44, caption y+4 / value y+21 centered; status text 12/88 w-24 h=44 wrapped).
    //
    // SNOW SCULPTING source nuances verified against the branch, replayed exactly:
    //  - The toggle label literal is "? Auto Snow Sculpture" — byte-verified U+003F U+0020 ...:
    //    a plain ASCII question mark + space, NOT a mangled Unicode glyph. Reproduced
    //    character-for-character (through L, same as the source's DrawSwitchToggle path).
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

            public Toggle AutoToggle;             // routes through SetPuzzleAutoEnabled(value, true)

            public GameObject RunStatusLabel;     // 3-way literal pick (per-frame, zero-alloc)
            public string RunStatusShown;

            public GameObject PiecesValue;        // stat-box value labels (0.5s tick)
            public string PiecesShown;
            public GameObject SentValue;
            public string SentShown;

            public GameObject StatusLabel;        // wrapped puzzleStatus (0.5s tick)
            public string StatusShown;

            public float NextSlowSyncAt;          // 0.5s tick (file header)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesPuzzleHandle uguiShellFeaturesPuzzle;

        // ----------------------------------------------------------------------------------------
        // Live text builder — the drawer's exact 3-way literal pick (PuzzleNetFeature.cs:100,
        // raw GUI.Label — unlocalized on purpose).
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFeaturesPuzzleRunStatusText()
        {
            return this.puzzleSolveRunning
                ? "Solving..."
                : (this.puzzleAutoEnabled ? "Waiting for puzzle target..." : "Disabled");
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawPuzzleSection: header + toggle card + status card (2 stat boxes +
        // wrapped status), all FIXED positions — no conditional layout anywhere on this sub-tab.
        // Handle assigned LAST (Research idiom).
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

            float contentWidth = w - 22f;      // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // full-width elements at x=8, 8px right margin

            // Style roles replayed from the drawer's style block (PuzzleNetFeature.cs:73-88).
            Color mutedColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.78f);
            Color statusColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            // themePanelStyle box + DrawCardOutline → PanelBg + hairline ring (file header).
            Color cardRing = new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));

            // -------- Header (:90-91 — bold 15 WHITE) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header",
                this.L("PUZZLE"), 15f, Color.white, false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, panelW, 30f);

            // -------- Toggle card (:94-104 — 52px themed panel) --------
            GameObject toggleCard = this.CreateUguiGo("ToggleCard", scrollContent);
            PlaceUguiTopLeft(toggleCard, 8f, 50f, panelW, 52f);
            this.AddUguiImage(toggleCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(toggleCard, cardRing, 1f);

            handle.AutoToggle = this.CreateUguiCheckbox(toggleCard.transform, "AutoPuzzle",
                this.L("Auto Puzzle"), this.puzzleAutoEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPuzzleAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 14f, 14f, 250f, 26f);

            handle.RunStatusShown = this.BuildUguiFeaturesPuzzleRunStatusText();
            handle.RunStatusLabel = this.CreateUguiLabel(toggleCard.transform, "RunStatus",
                handle.RunStatusShown, 12f, statusColor, false);
            PlaceUguiTopLeft(handle.RunStatusLabel, 284f, 17f, panelW - 300f, 22f);

            // -------- Status card (:108-118 — 164px themed panel) --------
            GameObject statusCard = this.CreateUguiGo("StatusCard", scrollContent);
            PlaceUguiTopLeft(statusCard, 8f, 120f, panelW, 164f);
            this.AddUguiImage(statusCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(statusCard, cardRing, 1f);

            GameObject statusCaption = this.CreateUguiLabel(statusCard.transform, "Caption",
                this.L("STATUS"), 11f, mutedColor, false);
            this.TrySetUguiLabelBold(statusCaption);
            PlaceUguiTopLeft(statusCaption, 12f, 8f, panelW - 24f, 18f);

            // Stat boxes (:113-116 — statWidth = (w-36)/2, at x=12 and x=24+statWidth, y=34).
            float statW = (panelW - 36f) / 2f;
            handle.PiecesShown = this.puzzlePieces.Count.ToString();
            handle.PiecesValue = this.CreateUguiFeaturesPuzzleStatBox(statusCard.transform,
                "PiecesBox", 12f, statW, "PIECES", handle.PiecesShown, mutedColor);
            handle.SentShown = this.puzzleSentCount.ToString();
            handle.SentValue = this.CreateUguiFeaturesPuzzleStatBox(statusCard.transform,
                "SentBox", 24f + statW, statW, "SENT", handle.SentShown, mutedColor);

            // Wrapped status text (:118 — statusStyle, uiText, at 12/88 w-24 h=44).
            handle.StatusShown = this.puzzleStatus;
            handle.StatusLabel = this.CreateUguiLabel(statusCard.transform, "StatusText",
                handle.StatusShown, 12f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, 12f, 88f, panelW - 24f, 44f);

            // Full cursor replay: 8 + 42 + 70 + 170 + 20 (the drawer's own return pad) = 310.
            this.SetUguiScrollContentHeight(scrollContent, 310f);

            handle.Root = block;
            this.uguiShellFeaturesPuzzle = handle;
            return block;
        }

        // One DrawPuzzleStatBox (:161-166): themeTopTabStyle box (controlFill @ clamp(panelAlpha,
        // .25, 1) + baked white .08 ring — NO DrawCardOutline hairline on these, unlike the
        // cards), caption bold 10 centered muted at y+4, value bold 13 centered WHITE at y+21.
        // Returns the VALUE label (the live part); the caption is static.
        private GameObject CreateUguiFeaturesPuzzleStatBox(Transform parent, string name,
            float xPos, float statW, string caption, string initialValue, Color mutedColor)
        {
            GameObject box = this.CreateUguiGo(name, parent);
            PlaceUguiTopLeft(box, xPos, 34f, statW, 44f);
            Color controlFill = this.UguiKitControlFill();
            this.AddUguiImage(box, new Color(controlFill.r, controlFill.g, controlFill.b,
                Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f)), true, 1f);
            this.AddUguiRingOverlay(box, new Color(1f, 1f, 1f, 0.08f), 1f);

            GameObject captionGo = this.CreateUguiLabel(box.transform, "Caption",
                caption, 10f, mutedColor, true);
            this.TrySetUguiLabelBold(captionGo);
            PlaceUguiTopLeft(captionGo, 0f, 4f, statW, 16f);

            GameObject valueGo = this.CreateUguiLabel(box.transform, "Value",
                initialValue, 13f, Color.white, true);
            this.TrySetUguiLabelBold(valueGo);
            PlaceUguiTopLeft(valueGo, 0f, 21f, statW, 18f);
            return valueGo;
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
                // Toggle re-sync (external IMGUI edits, incl. ForceStopPuzzleAuto paths) —
                // WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.puzzleAutoEnabled);

                // 3-way run status: zero-alloc literal pick — per frame (file header).
                this.SyncUguiSelfLabelText(handle.RunStatusLabel, ref handle.RunStatusShown,
                    this.BuildUguiFeaturesPuzzleRunStatusText());

                // PIECES/SENT/puzzleStatus change from the background solve machinery and the
                // counters allocate on build — 0.5s tick (file header).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.PiecesValue, ref handle.PiecesShown,
                        this.puzzlePieces.Count.ToString());
                    this.SyncUguiSelfLabelText(handle.SentValue, ref handle.SentShown,
                        this.puzzleSentCount.ToString());
                    this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                        this.puzzleStatus);
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
        // Change handler
        // ----------------------------------------------------------------------------------------

        // PuzzleNetFeature.cs:98-104 — on an ACTUAL change, route through the backend method with
        // notify: true, exactly like the IMGUI block. SetPuzzleAutoEnabled owns everything else
        // (field write, timer/probe resets, status, log, green/red notification, StopPuzzleSolve
        // on disable) — nothing reproduced here. The equal-guard is the UGUI analog of IMGUI's
        // prev-vs-new change check (and absorbs the kit checkbox's build-time onChanged fire).
        private void OnUguiFeaturesPuzzleAutoToggled(bool value)
        {
            if (value == this.puzzleAutoEnabled)
            {
                return;
            }
            this.SetPuzzleAutoEnabled(value, true);
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
            return "Snowballs in bag: " + (count < 0 ? "?" : count.ToString());
        }

        private string BuildUguiFeaturesSnowStartDelayText()
        {
            return "Start delay: " + this.snowStartDelaySeconds.ToString("F2") + "s";
        }

        private string BuildUguiFeaturesSnowNextDelayText()
        {
            return "Next-cycle delay: " + this.snowNextCycleDelaySeconds.ToString("F2") + "s";
        }

        private string BuildUguiFeaturesSnowQteCountText()
        {
            return "QTE successes: " + this.snowQteSuccessCount;
        }

        private string BuildUguiFeaturesSnowBallsText()
        {
            return this.snowballUseLimit > 0
                ? "Balls used: " + this.snowballsUsedThisRun + " / " + this.snowballUseLimit
                : "Balls used: " + this.snowballsUsedThisRun + " (no limit)";
        }

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

            // -------- Toggle (:1034-1039 — the byte-verified "? Auto Snow Sculpture" literal,
            // ASCII '?' + space; see file header. Flag-only + notification, 360x30) --------
            handle.AutoToggle = this.CreateUguiCheckbox(scrollContent, "AutoSnow",
                this.L("? Auto Snow Sculpture"), this.autoSnowEnabled,
                new System.Action<bool>(this.OnUguiFeaturesSnowAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 8f, 48f, 360f, 30f);

            // -------- Bag counter (2026-07-25 addition; 0.5s tick over a 1s-throttled read) -----
            handle.BagCountShown = this.BuildUguiFeaturesSnowBagCountText();
            handle.BagCountLabel = this.CreateUguiLabel(scrollContent, "BagCount",
                handle.BagCountShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.BagCountLabel, 8f, 88f, handle.PanelW, 20f);

            // -------- Snowball budget (0 = all; IceSkating parse-clamp-writeback idiom) --------
            GameObject limitCaption = this.CreateUguiLabel(scrollContent, "LimitCaption",
                this.L("Snowball limit (0 = all)"), 12f, textColor, false);
            PlaceUguiTopLeft(limitCaption, 8f, 112f, 170f, 22f);
            handle.LimitSeen = this.snowballUseLimit.ToString();
            handle.LimitField = this.CreateUguiInputField(scrollContent, "LimitField",
                handle.LimitSeen, 6, new System.Action<string>(this.OnUguiFeaturesSnowLimitEdited));
            PlaceUguiTopLeft(handle.LimitField.gameObject, 186f, 112f, 80f, 22f);

            // -------- Pacing sliders (persisted; label-left + slider-right row shape) --------
            handle.StartDelayShown = this.BuildUguiFeaturesSnowStartDelayText();
            handle.StartDelayLabel = this.CreateUguiLabel(scrollContent, "StartDelayLabel",
                handle.StartDelayShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.StartDelayLabel, 8f, 142f, 200f, 22f);
            handle.StartDelaySlider = this.CreateUguiSlider(scrollContent, "StartDelaySlider",
                SnowStartDelayMin, SnowStartDelayMax, this.snowStartDelaySeconds,
                false, new System.Action<float>(this.OnUguiFeaturesSnowStartDelayChanged));
            PlaceUguiTopLeft(handle.StartDelaySlider.gameObject, 216f, 145f, handle.PanelW - 216f, 20f);

            handle.NextDelayShown = this.BuildUguiFeaturesSnowNextDelayText();
            handle.NextDelayLabel = this.CreateUguiLabel(scrollContent, "NextDelayLabel",
                handle.NextDelayShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.NextDelayLabel, 8f, 168f, 200f, 22f);
            handle.NextDelaySlider = this.CreateUguiSlider(scrollContent, "NextDelaySlider",
                SnowNextCycleDelayMin, SnowNextCycleDelayMax, this.snowNextCycleDelaySeconds,
                false, new System.Action<float>(this.OnUguiFeaturesSnowNextDelayChanged));
            PlaceUguiTopLeft(handle.NextDelaySlider.gameObject, 216f, 171f, handle.PanelW - 216f, 20f);

            handle.QteCountShown = this.BuildUguiFeaturesSnowQteCountText();
            handle.QteCountLabel = this.CreateUguiLabel(scrollContent, "QteCountLabel",
                handle.QteCountShown, 12f, textColor, false);
            PlaceUguiTopLeft(handle.QteCountLabel, 8f, 194f, 200f, 22f);
            handle.QteCountSlider = this.CreateUguiSlider(scrollContent, "QteCountSlider",
                SnowQteSuccessMin, SnowQteSuccessMax, this.snowQteSuccessCount,
                true, new System.Action<float>(this.OnUguiFeaturesSnowQteCountChanged));
            PlaceUguiTopLeft(handle.QteCountSlider.gameObject, 216f, 197f, handle.PanelW - 216f, 20f);

            // -------- Conditional plain status box (:1043 — DEFAULT GUI.skin.box; Sand
            // Sculpture's plain-box convention: ControlFill @ 0.55 + neutral gray ring). Now 3
            // lines (Balls added 2026-07-25) -> 102 high; sits below the new controls at 220. ----
            handle.StatusBox = this.CreateUguiGo("StatusBox", scrollContent);
            PlaceUguiTopLeft(handle.StatusBox, 8f, 220f, handle.PanelW, 102f);
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

        // Positions everything from the CURRENT autoSnowEnabled / move-status state — the UGUI
        // analog of the IMGUI branch's num accumulation (Gui.cs:1031-1077), shifted by the
        // 2026-07-25 fixed rows: header 8 (+40) → toggle 48 (+40) → bag counter 88 (+24) →
        // limit row 112 (+30) → sliders 142/168/194 (+26 each) → box slot 220 (+122 shown,
        // 102-high 3-line box / +20 hidden) → button (+38) → optional status label (+40).
        // Reposition/SetActive only; nothing is rebuilt.
        private void RelayoutUguiShellFeaturesSnow(UguiShellFeaturesSnowHandle handle)
        {
            bool autoOn = this.autoSnowEnabled;
            bool hasMoveStatus = !string.IsNullOrEmpty(this.snowMoveSnowballsStatus);

            float yCur = 220f;
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
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesSnowSculptingSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-sync (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.autoSnowEnabled);

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
