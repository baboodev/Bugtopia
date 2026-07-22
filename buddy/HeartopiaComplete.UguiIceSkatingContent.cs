using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 5 of 8 (migration plan item 12): the
    // ICE SKATING sub-tab — newFeaturesSubTab == 4 (AnimalCareFeature.cs:49-52 dispatcher). The
    // IMGUI content is TWO drawers chained in one flow — the only New Features sub built from
    // two feature files: DrawIceSkatingExtrasTab (IceSkatingSequenceFeature.cs:100-155, the
    // network-sequence sender) falls through into DrawExtraTab (AutoIceSkatingFeature.cs:
    // 4041-4170, the auto-bot controls) at :154. Both replayed here top-to-bottom in that order.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same action methods
    //    (all directly on HeartopiaComplete via the two feature partials; ZERO backend interop
    //    additions this round).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellIceSkatingSubIndex = 4, declared with their siblings in UguiShellTabIndices.cs),
    //    never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against both drawers, replayed exactly:
    //  - BUSY GATE (:110-111/:150): bool busy = iceSkatingSequenceCoroutine != null, a plain
    //    GUI.enabled = !busy ... = true SPAN — it really greys out (NOT the always-clickable
    //    conflict-toast pattern some rounds use), and it covers the TWO buttons AND the THREE
    //    int fields between the writes. First round to gate InputField.interactable (a local
    //    self-diffing helper below — the kit's SetUguiButtonInteractable is Button-typed). The
    //    coroutine ref nulls from background completion (drill done :314, challenge done :474,
    //    fail :482 — all three exits re-verified), so the processor recomputes the gate every
    //    gated frame and the controls re-enable on their own. Both Start* methods are also
    //    internally guarded (coroutine-null check + "already running" toast), so a same-frame
    //    race click is harmless — same as the IMGUI twin.
    //  - SAVE ASYMMETRY across the three int fields (the round's main trap): Challenge Score
    //    (:117-128) DOES SaveKeybinds(false) on an actual value change (prev-vs-new compare);
    //    the two Runs fields (:130-135, :143-148) deliberately do NOT save — run counts are
    //    ephemeral session values. Verified against the source; no save call added.
    //  - NO status/log label for the network-sequence part: iceSkatingSequenceLastStatus and
    //    BuildIceSkatingSequenceUiLogText() are loader-log/internal only — grep confirms zero
    //    IMGUI draw sites. Failure feedback is the AddMenuNotification toast fired inside
    //    IceSkatingSequenceFail (:481) — already handled by the shared toast pipeline; nothing
    //    drawn here. Do not add a label the source doesn't have.
    //  - Ultimate-cost slider (:4095-4106): rounds to the NEAREST 50 —
    //    Mathf.RoundToInt(newMinScore / 50f) * 50 (not a plain round, not tenths, not 0.5s) —
    //    and AutoIceSkatingInvalidateMaxUltimateCache() + SaveKeybinds(false) fire only when
    //    the ROUNDED value differs from the stored int. Range [0, 2000]
    //    (AutoIceSkatingMinUltimateScoreSliderMax). The UGUI handler stores the snapped int;
    //    the per-frame epsilon re-sync then pulls the slider handle onto it (Sand Sculpture's
    //    snap idiom).
    //  - Master toggle cascade (:4059-4084) replayed verbatim including the two raw field
    //    writes on ENABLE only (autoIceSkatingReflectionRetryAt = -999f,
    //    autoIceSkatingLastLoggedStatus = "") and the helper order; the DISABLE path calls
    //    AutoIceSkatingSetStatus("Disabled.", force: true) where enable does not. Both paths
    //    end in SaveKeybinds(false). The four remaining toggles (:4110-4158) are flag+save
    //    only.
    //  - Conditional logs line (:4163-4167): shown iff AutoIceSkatingLogsEnabled — a computed
    //    property over MasterLogAutoIceSkating (AutoIceSkatingFeature.cs:13), which is a LIVE
    //    static bool toggled from Settings → Logging, NOT a compile-time constant. The
    //    processor re-reads the SAME property every gated frame; on a transition the label
    //    SetActives and the scroll content height swaps 464 ↔ 488 (the line is LAST — nothing
    //    below it ever moves, so no relayout machinery).
    //  - Localization split: part 1 goes through L() at every call site
    //    (ice_skating.send_score_title / .challenge_button / .challenge_score / .runs /
    //    .perfect_drill); part 2's strings are RAW source literals (plain GUI.Label /
    //    DrawWrappedSwitchToggle, no L() anywhere in :4041-4170) — mirrored exactly.
    //  - Text roles: every label in BOTH drawers renders uiText at alpha 1 (part 1 header sets
    //    it explicitly :106; part 2's labelStyle = new Color(r,g,b) :4047) → UguiKitTextColor()
    //    throughout. Headers 14 bold; part 2 body labels keep the source's explicit fontSize
    //    14 (:4050); the part-1 field captions are default-skin GUI.Labels → the 12f caption
    //    convention (Auto Buy round). Challenge button = themePrimaryButtonStyle (:112) →
    //    primary; Perfect Drill = GUI.skin.button (:138) → secondary.
    //  - Int fields: the established live-parse-clamp-writeback InputField shape (Auto Buy's
    //    MaxPerItem single-field form of Foraging's core): TryParse → Mathf.Clamp → int field,
    //    text re-normalized via SetTextWithoutNotify when it differs (never .text — that would
    //    re-fire), failed parses keep the int and the raw text until the 0.5s external re-sync
    //    restores it (gentler than the IMGUI twin's per-frame value.ToString() rebind, same
    //    steady state). DrawIceSkatingIntField's geometry (:157-168): label rect.x/labelWidth,
    //    field at +labelWidth+4 — labelWidth 50 for Challenge Score, default 40 for both Runs.
    //
    // Positions replay the two drawers' cursor chain verbatim (content top margin 8 standing in
    // for startY; free elements at x=8 mirroring both sources' uniform left=40; part-2 520-wide
    // roles panelW-mapped per the Animal Care convention, everything else absolute):
    //   PART 1 — header y=8 (400x24)                                   (+30)
    //   challenge row y=38: primary 200x32; score label 214 (50) field 268 (78); runs label
    //     354 (40) field 398 (60), fields at row+4 h=22                (+36)
    //   drill row y=74: secondary 280x32; runs label/field 354/398 at row+4  (+40 → 114)
    //   PART 2 (DrawExtraTab(114) → +8) — header y=122 (panelW x24)    (+28)
    //   toggle Auto y=150 (panelW x28)                                 (+36)
    //   hint y=186 (panelW x44, wrapped)                               (+48)
    //   min-score label y=234 (panelW x22)                             (+24)
    //   slider y=258 (320x20, range 0..2000)                           (+30)
    //   toggles OnlyX2/Last30s/PerfectMove y=288/320/352 (panelW x28)  (+32 each)
    //   toggle PreferNew y=384                                         (+40 — source's wide step)
    //   status y=424 (panelW x22)                                      (+24)
    //   [logs y=448 (panelW x22), iff AutoIceSkatingLogsEnabled]       (+24)
    //   → content height 448+16 = 464 without the logs line, 472+16 = 488 with it.
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Ice
    // Skating sub-tab) re-sync the 5 toggles (SetIsOnWithoutNotify), the busy gate (2 buttons +
    // 3 fields — recomputed live, see above), the slider (epsilon diff, WithoutNotify) + its
    // value label and the Status line (both LIVE but zero-alloc steady-state: an int / raw
    // string-ref cache gates the composite "caption: value" rebuild, so unchanged frames
    // allocate nothing). The 3 InputFields ride the 0.5s tick for external IMGUI edits
    // (SyncUguiInputFieldFromBackingField diffs the *Seen caches so in-progress UGUI typing is
    // never clobbered), and the logs-line flag is checked every gated frame (cheap static
    // read). Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesIceSkatingHandle
        {
            public GameObject Root;
            public Transform ScrollContent;   // for the 464/488 height swap on the logs flag

            // PART 1 — network-sequence sender: 2 busy-gated buttons + 3 busy-gated int fields
            public GameObject ChallengeButton;
            public GameObject DrillButton;
            public InputField ChallengeScoreField;  // live parse-clamp-writeback; SAVES on change
            public string ChallengeScoreSeen;       // external-change sync cache (int-derived text)
            public InputField ChallengeRunsField;   // live parse-clamp-writeback; NO save (source)
            public string ChallengeRunsSeen;
            public InputField DrillRunsField;       // live parse-clamp-writeback; NO save (source)
            public string DrillRunsSeen;

            // PART 2 — auto-bot: master toggle (cascade) + 4 flag+save toggles
            public Toggle AutoToggle;
            public Toggle OnlyX2Toggle;
            public Toggle Last30sToggle;
            public Toggle PerfectMoveToggle;
            public Toggle PreferNewToggle;

            // Ultimate-cost row (nearest-50-snapped slider + live value label)
            public Slider MinScoreSlider;
            public GameObject MinScoreLabel;
            public int MinScoreShownValue;    // int cache gating the composite label rebuild
            public string MinScoreShownText;

            // Live status line ("Status: " + autoIceSkatingLastStatus, every gated frame)
            public GameObject StatusLabel;
            public string StatusRawSeen;      // RAW field cache — composite rebuilt only on diff
            public string StatusShownText;

            // Conditional logs line (AutoIceSkatingLogsEnabled — live static, file header)
            public GameObject LogsHintLabel;
            public bool LogsHintVisible;

            public float NextSlowSyncAt;      // 0.5s tick for the 3 InputField external re-syncs
            public int ErrorCount;            // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesIceSkatingHandle uguiShellNewFeaturesIceSkating;

        // Content heights for the two logs-line states (file header cursor replay).
        private const float UguiIceSkatingContentHeightBase = 464f;
        private const float UguiIceSkatingContentHeightWithLogs = 488f;

        // ----------------------------------------------------------------------------------------
        // Small helpers
        // ----------------------------------------------------------------------------------------

        // The source busy gate (:110): a plain coroutine-null check. Recomputed on every call —
        // the ref nulls from background completion/failure (:314/:474/:482), never cache it.
        private bool IsUguiIceSkatingSequenceBusy()
        {
            return this.iceSkatingSequenceCoroutine != null;
        }

        // InputField twin of the kit's SetUguiButtonInteractable (self-diffed write): the IMGUI
        // GUI.enabled = !busy span (:111-:150) covers the three int fields, not just the buttons.
        private static void SetUguiIceSkatingFieldInteractable(InputField field, bool interactable)
        {
            if (field == null)
            {
                return;
            }
            try
            {
                if (field.interactable != interactable)
                {
                    field.interactable = interactable;
                }
            }
            catch { }
        }

        // AutoIceSkatingFeature.cs:4093 — RAW literal caption + the live int (no L(), file header).
        private string BuildUguiIceSkatingMinScoreText()
        {
            return "Ultimate cost (min score): " + this.autoIceSkatingMinUltimateScore;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawIceSkatingExtrasTab + DrawExtraTab stacked (file header): part-1
        // header/buttons/int-fields, then part-2 header/toggles/slider/status flat on the scroll
        // content (neither source drawer draws card chrome), every position replaying the IMGUI
        // cursor chain ONCE at build time. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesIceSkatingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesIceSkating = null;

            UguiShellNewFeaturesIceSkatingHandle handle = new UguiShellNewFeaturesIceSkatingHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesIceSkatingContent", parent);
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

            // Both drawers render uiText at alpha 1 (file header) — one color for everything.
            Color textColor = this.UguiKitTextColor();
            bool busy = this.IsUguiIceSkatingSequenceBusy();

            // ==================== PART 1 — network-sequence sender ====================

            // -------- Header (:105-108 — 400x24, bold 14, L()) --------
            GameObject seqHeader = this.CreateUguiLabel(scrollContent, "SeqHeader",
                this.L("ice_skating.send_score_title"), 14f, textColor, false);
            this.TrySetUguiLabelBold(seqHeader);
            PlaceUguiTopLeft(seqHeader, 8f, 8f, 400f, 24f);

            // -------- Challenge row (:112-135 — primary 200x32; fields at row+4, h=22) --------
            handle.ChallengeButton = this.CreateUguiPrimaryButton(scrollContent, "ChallengeButton",
                this.L("ice_skating.challenge_button"),
                new System.Action(this.OnUguiIceSkatingChallengeClicked));
            PlaceUguiTopLeft(handle.ChallengeButton, 8f, 38f, 200f, 32f);

            // Challenge Score — DrawIceSkatingIntField(left+206, +4, 132, 22, labelWidth 50):
            // label 214 (50w), field 268 (78w). THE saving field (:125-128).
            GameObject scoreCaption = this.CreateUguiLabel(scrollContent, "ChallengeScoreCaption",
                this.L("ice_skating.challenge_score"), 12f, textColor, false);
            PlaceUguiTopLeft(scoreCaption, 214f, 42f, 50f, 22f);
            handle.ChallengeScoreSeen = this.iceSkatingChallengeEndScore.ToString();
            handle.ChallengeScoreField = this.CreateUguiInputField(scrollContent, "ChallengeScoreField",
                handle.ChallengeScoreSeen, 8,
                new System.Action<string>(this.OnUguiIceSkatingChallengeScoreEdited));
            PlaceUguiTopLeft(handle.ChallengeScoreField.gameObject, 268f, 42f, 78f, 22f);

            // Challenge Runs — DrawIceSkatingIntField(left+346, +4, 104, 22, default 40):
            // label 354 (40w), field 398 (60w). NO save (:130-135, file header).
            GameObject challengeRunsCaption = this.CreateUguiLabel(scrollContent, "ChallengeRunsCaption",
                this.L("ice_skating.runs"), 12f, textColor, false);
            PlaceUguiTopLeft(challengeRunsCaption, 354f, 42f, 40f, 22f);
            handle.ChallengeRunsSeen = this.iceSkatingSequenceRunCount.ToString();
            handle.ChallengeRunsField = this.CreateUguiInputField(scrollContent, "ChallengeRunsField",
                handle.ChallengeRunsSeen, 8,
                new System.Action<string>(this.OnUguiIceSkatingChallengeRunsEdited));
            PlaceUguiTopLeft(handle.ChallengeRunsField.gameObject, 398f, 42f, 60f, 22f);

            // -------- Perfect Drill row (:138-148 — plain GUI.skin.button → secondary 280x32) ----
            handle.DrillButton = this.CreateUguiSecondaryButton(scrollContent, "PerfectDrillButton",
                this.L("ice_skating.perfect_drill"),
                new System.Action(this.OnUguiIceSkatingPerfectDrillClicked));
            PlaceUguiTopLeft(handle.DrillButton, 8f, 74f, 280f, 32f);

            // Drill Runs — same geometry as Challenge Runs, one row down. NO save (:143-148).
            GameObject drillRunsCaption = this.CreateUguiLabel(scrollContent, "DrillRunsCaption",
                this.L("ice_skating.runs"), 12f, textColor, false);
            PlaceUguiTopLeft(drillRunsCaption, 354f, 78f, 40f, 22f);
            handle.DrillRunsSeen = this.iceSkatingPerfectDrillRunCount.ToString();
            handle.DrillRunsField = this.CreateUguiInputField(scrollContent, "DrillRunsField",
                handle.DrillRunsSeen, 8,
                new System.Action<string>(this.OnUguiIceSkatingDrillRunsEdited));
            PlaceUguiTopLeft(handle.DrillRunsField.gameObject, 398f, 78f, 60f, 22f);

            // Initial busy gate — the full :111-:150 GUI.enabled span (2 buttons + 3 fields).
            this.SetUguiButtonInteractable(handle.ChallengeButton, !busy);
            this.SetUguiButtonInteractable(handle.DrillButton, !busy);
            SetUguiIceSkatingFieldInteractable(handle.ChallengeScoreField, !busy);
            SetUguiIceSkatingFieldInteractable(handle.ChallengeRunsField, !busy);
            SetUguiIceSkatingFieldInteractable(handle.DrillRunsField, !busy);

            // ==================== PART 2 — auto-bot (DrawExtraTab, all RAW literals) ====================

            // -------- Header (:4056 — bold 14; source 520 → panelW) --------
            GameObject botHeader = this.CreateUguiLabel(scrollContent, "BotHeader",
                "Auto Ice Skating (bot)", 14f, textColor, false);
            this.TrySetUguiLabelBold(botHeader);
            PlaceUguiTopLeft(botHeader, 8f, 122f, panelW, 24f);

            // -------- Master toggle (:4059-4084 — the cascade lives in the handler) --------
            handle.AutoToggle = this.CreateUguiCheckbox(scrollContent, "AutoIceSkating",
                "Auto Ice Skating", this.autoIceSkatingEnabled,
                new System.Action<bool>(this.OnUguiIceSkatingAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 8f, 150f, panelW, 28f);

            // -------- Static hint (:4087-4089 — fontSize 14 wordWrap, 520x44 → panelW) --------
            GameObject hint = this.CreateUguiLabel(scrollContent, "Hint",
                "Automatically chains skate tricks at perfect timing. You still control movement.",
                14f, textColor, false);
            this.TrySetUguiLabelWrapped(hint);
            PlaceUguiTopLeft(hint, 8f, 186f, panelW, 44f);

            // -------- Ultimate cost row (:4093-4106 — label +24, slider 320x20, snap-to-50) ----
            handle.MinScoreShownValue = this.autoIceSkatingMinUltimateScore;
            handle.MinScoreShownText = this.BuildUguiIceSkatingMinScoreText();
            handle.MinScoreLabel = this.CreateUguiLabel(scrollContent, "MinScoreLabel",
                handle.MinScoreShownText, 14f, textColor, false);
            PlaceUguiTopLeft(handle.MinScoreLabel, 8f, 234f, panelW, 22f);
            handle.MinScoreSlider = this.CreateUguiSlider(scrollContent, "MinScoreSlider",
                0f, AutoIceSkatingMinUltimateScoreSliderMax, this.autoIceSkatingMinUltimateScore, false,
                new System.Action<float>(this.OnUguiIceSkatingMinUltimateScoreChanged));
            PlaceUguiTopLeft(handle.MinScoreSlider.gameObject, 8f, 258f, 320f, 20f);

            // -------- Four flag+save toggles (:4110-4158) --------
            handle.OnlyX2Toggle = this.CreateUguiCheckbox(scrollContent, "OnlyX2Ultimate",
                "Only x2 ultimate (skip x1)", this.autoIceSkatingOnlyX2Ultimate,
                new System.Action<bool>(this.OnUguiIceSkatingOnlyX2Toggled));
            PlaceUguiTopLeft(handle.OnlyX2Toggle.gameObject, 8f, 288f, panelW, 28f);

            handle.Last30sToggle = this.CreateUguiCheckbox(scrollContent, "Last30sUltimate",
                "Last 30s ultimate (x1 when timer < 30s)", this.autoIceSkatingLast30sUltimate,
                new System.Action<bool>(this.OnUguiIceSkatingLast30sToggled));
            PlaceUguiTopLeft(handle.Last30sToggle.gameObject, 8f, 320f, panelW, 28f);

            handle.PerfectMoveToggle = this.CreateUguiCheckbox(scrollContent, "PerfectMove",
                "Perfect move (off: chain moves as soon as available)", this.autoIceSkatingPerfectMove,
                new System.Action<bool>(this.OnUguiIceSkatingPerfectMoveToggled));
            PlaceUguiTopLeft(handle.PerfectMoveToggle.gameObject, 8f, 352f, panelW, 28f);

            handle.PreferNewToggle = this.CreateUguiCheckbox(scrollContent, "PreferNewMove",
                "Prefer new move (prioritize unused tricks)", this.autoIceSkatingPreferNewMove,
                new System.Action<bool>(this.OnUguiIceSkatingPreferNewToggled));
            PlaceUguiTopLeft(handle.PreferNewToggle.gameObject, 8f, 384f, panelW, 28f);

            // -------- Live status line (:4161 — "Status: " + field, LIVE, file header) --------
            handle.StatusRawSeen = this.autoIceSkatingLastStatus;
            handle.StatusShownText = "Status: " + handle.StatusRawSeen;
            handle.StatusLabel = this.CreateUguiLabel(scrollContent, "StatusLabel",
                handle.StatusShownText, 14f, textColor, false);
            PlaceUguiTopLeft(handle.StatusLabel, 8f, 424f, panelW, 22f);

            // -------- Conditional logs line (:4163-4167 — iff AutoIceSkatingLogsEnabled) --------
            handle.LogsHintVisible = AutoIceSkatingLogsEnabled;
            handle.LogsHintLabel = this.CreateUguiLabel(scrollContent, "LogsHint",
                "Logs: BepInEx/LogOutput.log or MelonLoader/Latest.log", 14f, textColor, false);
            PlaceUguiTopLeft(handle.LogsHintLabel, 8f, 448f, panelW, 22f);
            handle.LogsHintLabel.SetActive(handle.LogsHintVisible);

            // Full cursor replay (file header): 464 without the logs line, 488 with it.
            this.SetUguiScrollContentHeight(scrollContent, handle.LogsHintVisible
                ? UguiIceSkatingContentHeightWithLogs
                : UguiIceSkatingContentHeightBase);

            handle.Root = block;
            this.uguiShellNewFeaturesIceSkating = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesIceSkatingOnUpdate()
        {
            UguiShellNewFeaturesIceSkatingHandle handle = this.uguiShellNewFeaturesIceSkating;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellIceSkatingSubIndex))
            {
                return;
            }

            try
            {
                // Busy gate — recomputed EVERY gated frame (the coroutine nulls from background
                // completion; a disabled control must re-enable on its own). Covers the same 5
                // controls as the source's GUI.enabled span; every write self-diffs.
                bool busy = this.IsUguiIceSkatingSequenceBusy();
                this.SetUguiButtonInteractable(handle.ChallengeButton, !busy);
                this.SetUguiButtonInteractable(handle.DrillButton, !busy);
                SetUguiIceSkatingFieldInteractable(handle.ChallengeScoreField, !busy);
                SetUguiIceSkatingFieldInteractable(handle.ChallengeRunsField, !busy);
                SetUguiIceSkatingFieldInteractable(handle.DrillRunsField, !busy);

                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.autoIceSkatingEnabled);
                this.SyncUguiToggleFromField(handle.OnlyX2Toggle, this.autoIceSkatingOnlyX2Ultimate);
                this.SyncUguiToggleFromField(handle.Last30sToggle, this.autoIceSkatingLast30sUltimate);
                this.SyncUguiToggleFromField(handle.PerfectMoveToggle, this.autoIceSkatingPerfectMove);
                this.SyncUguiToggleFromField(handle.PreferNewToggle, this.autoIceSkatingPreferNewMove);

                // Slider re-sync: pulls the handle onto the nearest-50-snapped field after a
                // drag AND mirrors external IMGUI edits (epsilon diff, WithoutNotify — Sand
                // idiom). The value label rebuilds only when the int actually changed.
                if (handle.MinScoreSlider != null
                    && Mathf.Abs(handle.MinScoreSlider.value - this.autoIceSkatingMinUltimateScore) > 0.0005f)
                {
                    handle.MinScoreSlider.SetValueWithoutNotify(this.autoIceSkatingMinUltimateScore);
                }
                if (handle.MinScoreShownValue != this.autoIceSkatingMinUltimateScore)
                {
                    handle.MinScoreShownValue = this.autoIceSkatingMinUltimateScore;
                    this.SyncUguiSelfLabelText(handle.MinScoreLabel, ref handle.MinScoreShownText,
                        this.BuildUguiIceSkatingMinScoreText());
                }

                // Status line — LIVE every gated frame; the raw-ref cache keeps unchanged
                // frames alloc-free (file header).
                string statusRaw = this.autoIceSkatingLastStatus;
                if (!string.Equals(statusRaw, handle.StatusRawSeen, StringComparison.Ordinal))
                {
                    handle.StatusRawSeen = statusRaw;
                    this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShownText,
                        "Status: " + statusRaw);
                }

                // Logs line — the SAME live property the IMGUI branch checks (:4163); on a
                // transition swap visibility + the content height (the line is last — nothing
                // below it moves).
                bool logsVisible = AutoIceSkatingLogsEnabled;
                if (handle.LogsHintVisible != logsVisible)
                {
                    handle.LogsHintVisible = logsVisible;
                    if (handle.LogsHintLabel != null)
                    {
                        handle.LogsHintLabel.SetActive(logsVisible);
                    }
                    this.SetUguiScrollContentHeight(handle.ScrollContent, logsVisible
                        ? UguiIceSkatingContentHeightWithLogs
                        : UguiIceSkatingContentHeightBase);
                }

                // InputField external re-syncs (IMGUI-twin edits) — 0.5s tick; diffed against
                // the *Seen caches so in-progress UGUI typing is never clobbered.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    SyncUguiInputFieldFromBackingField(handle.ChallengeScoreField,
                        ref handle.ChallengeScoreSeen, this.iceSkatingChallengeEndScore.ToString());
                    SyncUguiInputFieldFromBackingField(handle.ChallengeRunsField,
                        ref handle.ChallengeRunsSeen, this.iceSkatingSequenceRunCount.ToString());
                    SyncUguiInputFieldFromBackingField(handle.DrillRunsField,
                        ref handle.DrillRunsSeen, this.iceSkatingPerfectDrillRunCount.ToString());
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/IceSkating content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // Toggle handlers guard on "value actually changed": the kit checkbox fires onChanged
        // once at build (by design) — the guard absorbs it, and WithoutNotify re-syncs never
        // fire events at all. InputFields set .text before wiring, so they have no build fire.
        // ----------------------------------------------------------------------------------------

        // IceSkatingSequenceFeature.cs:112-115 — straight to the Start method (internally
        // guarded: coroutine-null check + "already running" toast, so a race click is safe).
        private void OnUguiIceSkatingChallengeClicked()
        {
            this.StartIceSkatingNetworkSequence();
        }

        // :138-141 — same shape over the drill sequence.
        private void OnUguiIceSkatingPerfectDrillClicked()
        {
            this.StartIceSkatingPerfectDrillSequence();
        }

        // :117-128 — parse-clamp [0, IceSkatingChallengeEndScoreMax] + SaveKeybinds(false) ONLY
        // on an actual value change (the source's prev-vs-new compare). The ONE saving field.
        private void OnUguiIceSkatingChallengeScoreEdited(string text)
        {
            UguiShellNewFeaturesIceSkatingHandle handle = this.uguiShellNewFeaturesIceSkating;
            if (handle == null)
            {
                return;
            }
            string raw = text ?? string.Empty;
            if (int.TryParse(raw, out int parsed))
            {
                int prev = this.iceSkatingChallengeEndScore;
                this.iceSkatingChallengeEndScore = Mathf.Clamp(parsed, 0, IceSkatingChallengeEndScoreMax);
                if (this.iceSkatingChallengeEndScore != prev)
                {
                    try { this.SaveKeybinds(false); } catch { }
                }
                string normalized = this.iceSkatingChallengeEndScore.ToString();
                handle.ChallengeScoreSeen = normalized;
                if (handle.ChallengeScoreField != null
                    && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { handle.ChallengeScoreField.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                handle.ChallengeScoreSeen = raw;
            }
        }

        // :130-135 — parse-clamp [1, IceSkatingSequenceMaxRunCount]. NO SaveKeybinds — the
        // source deliberately does not persist run counts (file header); do not add one.
        private void OnUguiIceSkatingChallengeRunsEdited(string text)
        {
            UguiShellNewFeaturesIceSkatingHandle handle = this.uguiShellNewFeaturesIceSkating;
            if (handle == null)
            {
                return;
            }
            string raw = text ?? string.Empty;
            if (int.TryParse(raw, out int parsed))
            {
                this.iceSkatingSequenceRunCount = Mathf.Clamp(parsed, 1, IceSkatingSequenceMaxRunCount);
                string normalized = this.iceSkatingSequenceRunCount.ToString();
                handle.ChallengeRunsSeen = normalized;
                if (handle.ChallengeRunsField != null
                    && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { handle.ChallengeRunsField.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                handle.ChallengeRunsSeen = raw;
            }
        }

        // :143-148 — identical contract over the drill run count. NO save (source).
        private void OnUguiIceSkatingDrillRunsEdited(string text)
        {
            UguiShellNewFeaturesIceSkatingHandle handle = this.uguiShellNewFeaturesIceSkating;
            if (handle == null)
            {
                return;
            }
            string raw = text ?? string.Empty;
            if (int.TryParse(raw, out int parsed))
            {
                this.iceSkatingPerfectDrillRunCount = Mathf.Clamp(parsed, 1, IceSkatingSequenceMaxRunCount);
                string normalized = this.iceSkatingPerfectDrillRunCount.ToString();
                handle.DrillRunsSeen = normalized;
                if (handle.DrillRunsField != null
                    && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { handle.DrillRunsField.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                handle.DrillRunsSeen = raw;
            }
        }

        // AutoIceSkatingFeature.cs:4059-4084 — the master cascade, verbatim: enable writes the
        // two retry/log-dedupe fields then resets trackers + invalidates the cache + logs;
        // disable resets + invalidates + sets "Disabled." + logs. Both paths save.
        private void OnUguiIceSkatingAutoToggled(bool value)
        {
            if (value == this.autoIceSkatingEnabled)
            {
                return;
            }
            this.autoIceSkatingEnabled = value;
            if (value)
            {
                this.autoIceSkatingReflectionRetryAt = -999f;
                this.autoIceSkatingLastLoggedStatus = string.Empty;
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingInvalidateMaxUltimateCache();
                this.AutoIceSkatingLog("enabled", force: true);
            }
            else
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingInvalidateMaxUltimateCache();
                this.AutoIceSkatingSetStatus("Disabled.", force: true);
                this.AutoIceSkatingLog("disabled", force: true);
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        // :4095-4106 — the EXACT source rounding: nearest 50 via Mathf.RoundToInt(v / 50f) * 50,
        // and the cache invalidation + save fire only when the ROUNDED value differs.
        private void OnUguiIceSkatingMinUltimateScoreChanged(float value)
        {
            int roundedMinScore = Mathf.RoundToInt(value / 50f) * 50;
            if (roundedMinScore != this.autoIceSkatingMinUltimateScore)
            {
                this.autoIceSkatingMinUltimateScore = roundedMinScore;
                this.AutoIceSkatingInvalidateMaxUltimateCache();
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // :4110-4119 — flag + save only, no cascade.
        private void OnUguiIceSkatingOnlyX2Toggled(bool value)
        {
            if (value == this.autoIceSkatingOnlyX2Ultimate)
            {
                return;
            }
            this.autoIceSkatingOnlyX2Ultimate = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // :4123-4132 — flag + save only.
        private void OnUguiIceSkatingLast30sToggled(bool value)
        {
            if (value == this.autoIceSkatingLast30sUltimate)
            {
                return;
            }
            this.autoIceSkatingLast30sUltimate = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // :4136-4145 — flag + save only.
        private void OnUguiIceSkatingPerfectMoveToggled(bool value)
        {
            if (value == this.autoIceSkatingPerfectMove)
            {
                return;
            }
            this.autoIceSkatingPerfectMove = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // :4149-4158 — flag + save only.
        private void OnUguiIceSkatingPreferNewToggled(bool value)
        {
            if (value == this.autoIceSkatingPreferNewMove)
            {
                return;
            }
            this.autoIceSkatingPreferNewMove = value;
            try { this.SaveKeybinds(false); } catch { }
        }
    }
}
