using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 2 of 8 (migration plan item 12): the
    // SAND SCULPTURE sub-tab — DrawSandSculptureTab (SandSculptureFeature.cs:2586-2687),
    // newFeaturesSubTab == 6 (AnimalCareFeature.cs:59-62 dispatcher). The remaining six subs
    // (Daily Quests, Homeland Farm, Pictures, Ice Skating, Extra, Sea Clean — display 1-5,7)
    // stay on the shell placeholder until their own rounds ship.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same action methods (all directly on
    //    HeartopiaComplete via the SandSculptureFeature.cs partial; ZERO backend interop
    //    additions this round: 4 bools, 3 counters/timers, 1 enum, 4 status strings, 2
    //    collections, TryCloseSandModelDialog + SandLogStatus + AddMenuNotification).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellSandSculptureSubIndex = 6, declared with their siblings in
    //    UguiPhase3Content.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the drawer, replayed exactly:
    //  - The tab header is bodyText-colored bold 14 (:2591-2592 — uiText, NOT the uiHeader color
    //    CreateUguiHeaderLabel would use), so it is a plain CreateUguiLabel + TrySetUguiLabelBold.
    //    mutedStyle = uiSubTabText @ 0.92 wordWrap (:2595-2596), bodyStyle = uiText @ 0.95
    //    (:2593-2594) — both built verbatim, not the kit's 1.0-alpha convenience colors.
    //  - Finish delay slider (:2646-2648): value = Mathf.Round(raw * 2f) / 2f — snaps to the
    //    nearest 0.5 SECOND, a THIRD granularity distinct from every sibling round (neither
    //    wholeNumbers nor tenths). The UGUI handler stores the snapped value; the per-frame
    //    epsilon re-sync then pulls the slider handle onto the snapped field, exactly like the
    //    IMGUI twin redrawing from the snapped field each frame.
    //  - Status box (:2651): GUI.Box(rect, string.Empty) with the DEFAULT GUI.skin.box — the
    //    ONLY box in this migration that does NOT pass this.themePanelStyle. That distinction is
    //    preserved, not silently upgraded: no CreateUguiSettingsMainPanel card chrome (no accent
    //    ring, no header slot). First GUI.Box port in the migration, so this file sets the
    //    convention: theme-derived ControlFill at 0.55 alpha + a faint NEUTRAL gray ring —
    //    reads "plain bordered box", visually distinct from the accent-ringed cards, matching
    //    how default skin.box sits apart from the themed panels on the IMGUI surface.
    //  - Inside the box: "State/Done/Collected" combined line and "Status" line are ALWAYS
    //    drawn; "Place"/"Collect" lines only when their string is non-empty (:2659-2667). The
    //    box is a FIXED 130px and the cursor advances an unconditional y += 140 (:2668), so
    //    NOTHING BELOW THE BOX EVER MOVES — no relayout machinery. The only conditional motion
    //    is INSIDE the box: the Collect line renders at box-local y=66 when Place is absent and
    //    y=94 when present (boxTextY += 28 only inside the Place branch). Mirrored with both
    //    labels built once + SetActive on cached flags + a cached-flag 66/94 reposition.
    //  - "Close stuck dialog" (:2678-2683): notification color depends on BOTH out-values —
    //    (dialogClosed && dialogWasOpen) ? green : orange — reproduced as the full two-value
    //    branch ("closed one" vs "none was open"/failed), never simplified to one flag.
    //  - Toggle notifications (:2606-2641): L(label) + ": " + (on ? L("On") : L("Off")), green
    //    (0.45,1,0.55) on / red (1,0.55,0.55) off; Auto-place also resets sandPlaceAttempts=0,
    //    Auto-collect also resets sandCollectNextAt=0f, Prefer-rare notifies only. Handlers
    //    guard on "value actually changed" (Self rounds' idiom) because CreateUguiCheckbox
    //    fires onChanged once at build by design — the guard absorbs it and WithoutNotify
    //    re-syncs never fire events at all.
    //
    // Positions replay the source's cursor math verbatim (content top margin 8 standing in for
    // startY; free elements at x=8 mirroring the source's uniform left=40; widths kept from the
    // source where absolute, panelW-mapped for the two full-width roles 560→panelW / 540→
    // panelW-20, the Animal Care convention):
    //   header y=8            (+34)      toggle Auto y=42 (360x30)      (+38)
    //   hint1 y=80            (+38)      toggle Place y=118 (420x30)    (+38)
    //   toggle Collect y=156  (+38)      toggle Rare y=194 (460x30)     (+30 — source's odd step)
    //   hint2 y=224           (+38)      delay label y=262 / slider y=268 (248,200x18)  (+40)
    //   box y=302 h=130 (lines box-local 8/32/66/[66|94])               (+140 fixed)
    //   buttons y=442 (220x30 at x=8 and x=238)                         (+40, return +20)
    //   → content height 8 + 494 = 502 (DrawNewFeaturesTab adds no extra pad on this branch).
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Sand
    // Sculpture sub-tab) re-sync the 4 toggles (SetIsOnWithoutNotify) and the slider
    // (SetValueWithoutNotify on epsilon diff) + its value label — external IMGUI edits and drag
    // snapping. The four box lines change from the feature's background state machine and their
    // texts are composite builds (L() + interpolation allocate even when unchanged), so they
    // ride the 0.5s slow tick instead (Self Fun's established idiom for exactly this shape),
    // together with the conditional visibility/reposition flags fed by the same strings.
    // Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesSandSculptureHandle
        {
            public GameObject Root;

            // The 4 switch toggles (all notify; three carry an extra field reset)
            public Toggle AutoSandToggle;
            public Toggle AutoPlaceToggle;
            public Toggle AutoCollectToggle;
            public Toggle PreferRareToggle;

            // Finish delay row (0.5s-snapped slider + live value label)
            public GameObject DelayLabel;
            public string DelayShown;
            public Slider DelaySlider;

            // Plain status box lines (State/Status always visible; Place/Collect conditional)
            public GameObject StateLine;
            public string StateShown;
            public GameObject StatusLine;
            public string StatusShown;
            public GameObject PlaceLine;
            public string PlaceShown;
            public bool PlaceLineVisible;
            public GameObject CollectLine;
            public string CollectShown;
            public bool CollectLineVisible;
            public bool CollectBelowPlace;    // true → Collect at box-local 94, else 66
            public float BoxInnerW;           // cached for the 66/94 reposition

            public float NextSlowSyncAt;      // 0.5s tick for the box lines (file header)
            public int ErrorCount;            // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesSandSculptureHandle uguiShellNewFeaturesSandSculpture;

        // Notification palette — the source's exact literals (:2598-2599, :2682).
        private static readonly Color UguiSandSculptureOkColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color UguiSandSculptureOffColor = new Color(1f, 0.55f, 0.55f);
        private static readonly Color UguiSandSculptureWarnColor = new Color(1f, 0.75f, 0.45f);

        // ----------------------------------------------------------------------------------------
        // Live text builders (shared by builder + processor so both surfaces render one truth)
        // ----------------------------------------------------------------------------------------

        // :2646 — "Finish delay: {0:F1}s" (two localized halves? No — single L() + interpolation).
        private string BuildUguiSandSculptureDelayText()
        {
            return this.L("Finish delay") + $": {this.sandFinishDelaySeconds:F1}s";
        }

        // :2654 — three live values in ONE label, three-space separators, each caption localized
        // separately. sandApiState is the SandApiState enum (interpolation → ToString), exactly
        // as the IMGUI line renders it.
        private string BuildUguiSandSculptureStateLineText()
        {
            return this.L("State") + $": {this.sandApiState}   "
                + this.L("Done") + $": {this.sandSculpturesDone}   "
                + this.L("Collected") + $": {this.sandCollectedCount}";
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawSandSculptureTab: header, 4 toggles, 2 hints, delay slider row, the
        // plain status box, 2 action buttons — flat on the scroll content (the source draws no
        // card chrome anywhere on this tab), every position replaying the IMGUI cursor chain ONCE
        // at build time. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesSandSculptureContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesSandSculpture = null;

            UguiShellNewFeaturesSandSculptureHandle handle = new UguiShellNewFeaturesSandSculptureHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesSandSculptureContent", parent);
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

            // The source's three text roles, built verbatim (file header): header = uiText bold,
            // body = uiText @ 0.95, muted = uiSubTabText @ 0.92 + wordWrap.
            Color bodyColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Header (:2601 — 460x24, bold 14 in the TEXT color, not uiHeader) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header",
                this.L("Auto Sand Sculpture"), 14f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, 460f, 24f);

            // -------- Master toggle (:2604-2610, 360x30) --------
            handle.AutoSandToggle = this.CreateUguiCheckbox(scrollContent, "AutoSand",
                this.L("Auto Sand Sculpture"), this.autoSandEnabled,
                new System.Action<bool>(this.OnUguiSandSculptureAutoToggled));
            PlaceUguiTopLeft(handle.AutoSandToggle.gameObject, 8f, 42f, 360f, 30f);

            // -------- Hint 1 (:2612 — 540x32 muted, word-wrapped) --------
            GameObject hint1 = this.CreateUguiLabel(scrollContent, "Hint1",
                this.L("Places a base, sculpts the correct model, and repeats — fully automatic."),
                11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(hint1);
            PlaceUguiTopLeft(hint1, 8f, 80f, panelW - 20f, 32f);

            // -------- Auto-place toggle (:2615-2622, 420x30; resets sandPlaceAttempts) --------
            handle.AutoPlaceToggle = this.CreateUguiCheckbox(scrollContent, "AutoPlaceBase",
                this.L("Auto-place base from backpack"), this.sandAutoPlaceBase,
                new System.Action<bool>(this.OnUguiSandSculptureAutoPlaceToggled));
            PlaceUguiTopLeft(handle.AutoPlaceToggle.gameObject, 8f, 118f, 420f, 30f);

            // -------- Auto-collect toggle (:2625-2632, 420x30; resets sandCollectNextAt) --------
            handle.AutoCollectToggle = this.CreateUguiCheckbox(scrollContent, "AutoCollect",
                this.L("Auto-collect finished sculptures"), this.sandAutoCollect,
                new System.Action<bool>(this.OnUguiSandSculptureAutoCollectToggled));
            PlaceUguiTopLeft(handle.AutoCollectToggle.gameObject, 8f, 156f, 420f, 30f);

            // -------- Prefer-rare toggle (:2635-2641, 460x30; notification only) --------
            handle.PreferRareToggle = this.CreateUguiCheckbox(scrollContent, "PreferRare",
                this.L("Prefer rare / uncollected models"), this.sandPreferRareUncollected,
                new System.Action<bool>(this.OnUguiSandSculpturePreferRareToggled));
            PlaceUguiTopLeft(handle.PreferRareToggle.gameObject, 8f, 194f, 460f, 30f);

            // -------- Hint 2 (:2643 — after the source's odd += 30 step) --------
            GameObject hint2 = this.CreateUguiLabel(scrollContent, "Hint2",
                this.L("When a draft allows several models, pick the one missing from your 5★ collection (rarest first)."),
                11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(hint2);
            PlaceUguiTopLeft(hint2, 8f, 224f, panelW - 20f, 32f);

            // -------- Finish delay row (:2646-2648 — label 220x22, slider at +240, 200x18) --------
            handle.DelayShown = this.BuildUguiSandSculptureDelayText();
            handle.DelayLabel = this.CreateUguiLabel(scrollContent, "DelayLabel",
                handle.DelayShown, 12f, bodyColor, false);
            PlaceUguiTopLeft(handle.DelayLabel, 8f, 262f, 220f, 22f);
            handle.DelaySlider = this.CreateUguiSlider(scrollContent, "DelaySlider",
                0f, 30f, this.sandFinishDelaySeconds, false,
                new System.Action<float>(this.OnUguiSandSculptureFinishDelayChanged));
            PlaceUguiTopLeft(handle.DelaySlider.gameObject, 248f, 268f, 200f, 18f);

            // -------- Plain status box (:2651 — DEFAULT GUI.skin.box, see file header) --------
            // Deliberately NOT CreateUguiSettingsMainPanel: no accent ring, no header slot.
            GameObject statusBox = this.CreateUguiGo("StatusBox", scrollContent);
            PlaceUguiTopLeft(statusBox, 8f, 302f, panelW, 130f);
            Color boxFill = this.UguiKitControlFill();
            this.AddUguiImage(statusBox, new Color(boxFill.r, boxFill.g, boxFill.b, 0.55f), true, 1f);
            this.AddUguiRingOverlay(statusBox, new Color(0.88f, 0.92f, 0.97f, 0.16f), 1f);

            float boxInnerW = panelW - 20f; // source: labels at +10 inside a 560 box, width 540
            handle.BoxInnerW = boxInnerW;

            // Combined State/Done/Collected line (:2653-2655 — bodyStyle, always visible).
            handle.StateShown = this.BuildUguiSandSculptureStateLineText();
            handle.StateLine = this.CreateUguiLabel(statusBox.transform, "StateLine",
                handle.StateShown, 12f, bodyColor, false);
            PlaceUguiTopLeft(handle.StateLine, 10f, 8f, boxInnerW, 20f);

            // Status line (:2657 — mutedStyle, always visible).
            handle.StatusShown = this.L("Status") + $": {this.sandLastActionStatus}";
            handle.StatusLine = this.CreateUguiLabel(statusBox.transform, "StatusLine",
                handle.StatusShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLine);
            PlaceUguiTopLeft(handle.StatusLine, 10f, 32f, boxInnerW, 34f);

            // Place line (:2659-2663 — CONDITIONAL on a non-empty string; box-local y=66).
            handle.PlaceLineVisible = !string.IsNullOrEmpty(this.sandLastPlaceStatus);
            handle.PlaceShown = this.L("Place") + $": {this.sandLastPlaceStatus}";
            handle.PlaceLine = this.CreateUguiLabel(statusBox.transform, "PlaceLine",
                handle.PlaceShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.PlaceLine);
            PlaceUguiTopLeft(handle.PlaceLine, 10f, 66f, boxInnerW, 30f);
            handle.PlaceLine.SetActive(handle.PlaceLineVisible);

            // Collect line (:2664-2667 — CONDITIONAL; y=94 below Place when present, else 66).
            handle.CollectLineVisible = !string.IsNullOrEmpty(this.sandLastCollectStatus);
            handle.CollectBelowPlace = handle.PlaceLineVisible;
            handle.CollectShown = this.L("Collect") + $": {this.sandLastCollectStatus}";
            handle.CollectLine = this.CreateUguiLabel(statusBox.transform, "CollectLine",
                handle.CollectShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.CollectLine);
            PlaceUguiTopLeft(handle.CollectLine, 10f, handle.CollectBelowPlace ? 94f : 66f, boxInnerW, 30f);
            handle.CollectLine.SetActive(handle.CollectLineVisible);

            // -------- Action buttons (:2670-2683 — 220x30 pair, themePrimaryButtonStyle) --------
            GameObject resetButton = this.CreateUguiPrimaryButton(scrollContent, "ResetBlacklistButton",
                this.L("Reset base blacklist"), new System.Action(this.OnUguiSandSculptureResetBlacklistClicked));
            PlaceUguiTopLeft(resetButton, 8f, 442f, 220f, 30f);

            GameObject closeButton = this.CreateUguiPrimaryButton(scrollContent, "CloseDialogButton",
                this.L("Close stuck dialog"), new System.Action(this.OnUguiSandSculptureCloseDialogClicked));
            PlaceUguiTopLeft(closeButton, 238f, 442f, 220f, 30f);

            // Full cursor replay: 8 (top margin standing in for startY) + 494 (the drawer's own
            // return: buttons-row end 474 + 20 pad; DrawNewFeaturesTab adds nothing on sub 6).
            this.SetUguiScrollContentHeight(scrollContent, 502f);

            handle.Root = block;
            this.uguiShellNewFeaturesSandSculpture = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesSandSculptureOnUpdate()
        {
            UguiShellNewFeaturesSandSculptureHandle handle = this.uguiShellNewFeaturesSandSculpture;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellSandSculptureSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoSandToggle, this.autoSandEnabled);
                this.SyncUguiToggleFromField(handle.AutoPlaceToggle, this.sandAutoPlaceBase);
                this.SyncUguiToggleFromField(handle.AutoCollectToggle, this.sandAutoCollect);
                this.SyncUguiToggleFromField(handle.PreferRareToggle, this.sandPreferRareUncollected);

                // Slider re-sync: pulls the handle onto the 0.5s-snapped field after a drag AND
                // mirrors external IMGUI edits (sprint idiom — epsilon diff, WithoutNotify).
                if (handle.DelaySlider != null
                    && Mathf.Abs(handle.DelaySlider.value - this.sandFinishDelaySeconds) > 0.0005f)
                {
                    handle.DelaySlider.SetValueWithoutNotify(this.sandFinishDelaySeconds);
                }
                this.SyncUguiSelfLabelText(handle.DelayLabel, ref handle.DelayShown,
                    this.BuildUguiSandSculptureDelayText());

                // The four box lines change from the feature's background state machine and
                // their texts are allocating composite builds — 0.5s tick (file header).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;

                    this.SyncUguiSelfLabelText(handle.StateLine, ref handle.StateShown,
                        this.BuildUguiSandSculptureStateLineText());
                    this.SyncUguiSelfLabelText(handle.StatusLine, ref handle.StatusShown,
                        this.L("Status") + $": {this.sandLastActionStatus}");

                    // Conditional lines: text synced even while hidden (cheap, stays ready);
                    // visibility + the 66/94 slot ride cached flags (file header — the box is
                    // fixed-height, nothing below it ever moves).
                    bool placeVisible = !string.IsNullOrEmpty(this.sandLastPlaceStatus);
                    this.SyncUguiSelfLabelText(handle.PlaceLine, ref handle.PlaceShown,
                        this.L("Place") + $": {this.sandLastPlaceStatus}");
                    if (handle.PlaceLineVisible != placeVisible)
                    {
                        handle.PlaceLineVisible = placeVisible;
                        handle.PlaceLine.SetActive(placeVisible);
                    }

                    bool collectVisible = !string.IsNullOrEmpty(this.sandLastCollectStatus);
                    this.SyncUguiSelfLabelText(handle.CollectLine, ref handle.CollectShown,
                        this.L("Collect") + $": {this.sandLastCollectStatus}");
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
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/SandSculpture content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // All guarded on "value actually changed": the kit checkbox fires onChanged once at
        // build (by design), and these handlers post notifications — the guard absorbs both the
        // build-time fire and any redundant event.
        // ----------------------------------------------------------------------------------------

        // SandSculptureFeature.cs:2604-2610 — notification only.
        private void OnUguiSandSculptureAutoToggled(bool value)
        {
            if (value == this.autoSandEnabled)
            {
                return;
            }
            this.autoSandEnabled = value;
            this.AddMenuNotification(
                this.L("Auto Sand Sculpture") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // :2615-2622 — reset sandPlaceAttempts, THEN notification.
        private void OnUguiSandSculptureAutoPlaceToggled(bool value)
        {
            if (value == this.sandAutoPlaceBase)
            {
                return;
            }
            this.sandAutoPlaceBase = value;
            this.sandPlaceAttempts = 0;
            this.AddMenuNotification(
                this.L("Auto-place base from backpack") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // :2625-2632 — reset sandCollectNextAt, THEN notification.
        private void OnUguiSandSculptureAutoCollectToggled(bool value)
        {
            if (value == this.sandAutoCollect)
            {
                return;
            }
            this.sandAutoCollect = value;
            this.sandCollectNextAt = 0f;
            this.AddMenuNotification(
                this.L("Auto-collect finished sculptures") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // :2635-2641 — notification only (no field reset).
        private void OnUguiSandSculpturePreferRareToggled(bool value)
        {
            if (value == this.sandPreferRareUncollected)
            {
                return;
            }
            this.sandPreferRareUncollected = value;
            this.AddMenuNotification(
                this.L("Prefer rare / uncollected models") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? UguiSandSculptureOkColor : UguiSandSculptureOffColor);
        }

        // :2647-2648 — the EXACT source rounding: snap to the nearest 0.5s (Round(x*2)/2 — NOT
        // wholeNumbers, NOT tenths). The slider clamps to [0,30] itself, same as the IMGUI twin.
        private void OnUguiSandSculptureFinishDelayChanged(float value)
        {
            this.sandFinishDelaySeconds = Mathf.Round(value * 2f) / 2f;
        }

        // :2670-2676 — clear BOTH collections + reset attempts, then a green notification.
        private void OnUguiSandSculptureResetBlacklistClicked()
        {
            this.sandBaseFailCounts.Clear();
            this.sandBaseBlacklist.Clear();
            this.sandPlaceAttempts = 0;
            this.AddMenuNotification(this.L("Reset base blacklist"), UguiSandSculptureOkColor);
        }

        // :2678-2683 — call, log, then the TWO-VALUE color branch: green only when a dialog was
        // actually open AND got closed; orange otherwise (none open, or the close failed).
        private void OnUguiSandSculptureCloseDialogClicked()
        {
            bool dialogClosed = this.TryCloseSandModelDialog(out bool dialogWasOpen, out string dialogStatus);
            this.SandLogStatus("manual close dialog: " + dialogStatus);
            this.AddMenuNotification(this.L("Close stuck dialog"),
                (dialogClosed && dialogWasOpen) ? UguiSandSculptureOkColor : UguiSandSculptureWarnColor);
        }
    }
}
