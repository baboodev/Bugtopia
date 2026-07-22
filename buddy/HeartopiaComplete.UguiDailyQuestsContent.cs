using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round (migration plan item 12): the DAILY
    // QUESTS sub-tab — newFeaturesSubTab == 1 (AnimalCareFeature.cs:28-37 dispatcher). With it,
    // New Features' whole sub range (0-7) has real content. FOUR source drawers chain vertically
    // in the dispatcher's exact order:
    //   1. DailyQuestSubmitFeature.cs:1716-1754 — DrawDailyQuestSubmitControls (busy-gated
    //      Auto Submit button + Skip 5 Star toggle + status);
    //   2. DailyClaimsFeature.cs:74-159        — DrawDailyClaimsControls (the DAILY CLAIMS card:
    //      8 primary buttons on ONE shared busy gate + status below the card);
    //   3. BirdPhotoSubmitFeature.cs:558-577   — DrawBirdPhotoSubmitControls (one busy-gated
    //      button + status), then the dispatcher's own +40 gap;
    //   4. HeartopiaComplete.QuestAssistant.cs:1228-1307 — DrawQuestAssistantTab (header, four
    //      action buttons — two CONDITIONAL with live counts IN their labels — status, and the
    //      flat two-level quest/condition list).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same action methods
    //    (all directly on HeartopiaComplete via the four feature partials; ZERO backend
    //    additions: StartDailyQuestAutoSubmitItems, StartDailyClaimsAction + the six
    //    DailyClaims*Routine factories, StartWildAnimalClaimAllGifts, StartBirdPhotoAutoSubmit,
    //    QuestAssistantOnDumpButtonClicked, QuestAssistantToggleWindow,
    //    QuestAssistantOnAcceptAllClicked, QuestAssistantCountReadyToSubmit,
    //    QuestAssistantIsDailyQuestSubmitBusy, SaveKeybinds + the fields).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellDailyQuestsSubIndex = 1, declared with their siblings in UguiPhase3Content.cs).
    //    The processor gates on the SAME IsUguiShellNewFeaturesSubTabActive function Animal
    //    Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // THREE DIFFERENT BUSY-GATE EXPRESSIONS — verified against the drawers, never conflated:
    //   part 1 (:1722): dailyQuestSubmitCoroutine || birdPhotoSubmitCoroutine
    //                   || dailyClaimsCoroutine                     (3-way)
    //   part 2 (:101-104): dailyQuestSubmitCoroutine || birdPhotoSubmitCoroutine
    //                   || dailyClaimsCoroutine || wildAnimalGiftCoroutine   (4-WAY — the only
    //                   one that includes wildAnimalGiftCoroutine, because its card hosts the
    //                   Claim Wild Gifts button)
    //   part 3 (:564): birdPhotoSubmitCoroutine || dailyQuestSubmitCoroutine
    //                   || dailyClaimsCoroutine                     (the same 3 fields as part
    //                   1 in a different source order — kept verbatim, it is NOT a 4th field)
    // All three embed live coroutine refs that change from background activity, so the
    // processor recomputes the FULL conditions every gated frame (the Animal Care live-gate
    // precedent); SetUguiButtonInteractable self-diffs the actual Button write.
    //
    // Source nuances verified against the drawers, replayed exactly:
    //  - LOCALIZATION SPLIT: parts 1-3 L() every button/toggle/header label ("Auto Submit
    //    Daily Items", "Skip 5 Star Items", "DAILY CLAIMS", all 8 claim buttons, "Submit Bird
    //    Photo"); part 4 localizes NOTHING — every string in DrawQuestAssistantTab (header,
    //    all four button captions, the empty-state hint) is an unlocalized source literal,
    //    kept verbatim. The part-2 inline status "Wild gift claim started." (:143) is also a
    //    raw literal.
    //  - "CLAIM WILD GIFTS" (:140-144) is DIFFERENT from its 7 card siblings: it does NOT go
    //    through StartDailyClaimsAction — it calls StartWildAnimalClaimAllGifts(silent: false)
    //    directly and writes dailyClaimsLastStatus = "Wild gift claim started." inline. The
    //    other 7 all wrap their own DailyClaims*Routine() in StartDailyClaimsAction. Reproduced
    //    one-for-one; all 8 share the card's single 4-way busy gate and the same Primary tier
    //    (DrawDailyClaimsButton = themePrimaryButtonStyle for all 8).
    //  - Skip 5 Star toggle (:1732-1746): flag + SaveKeybinds(false) in try/catch ONLY (no
    //    notification), guarded on actual change (kit checkbox build-fire idiom). DrawSwitchToggle
    //    L()s internally, so the kit checkbox gets this.L(...) once at the call site.
    //  - QUEST ASSISTANT BUTTON TIERS: only "Dump Active Quests" uses themePrimaryButtonStyle
    //    (:1239) → Primary; the window toggle, Accept All and Submit Ready are plain
    //    GUI.skin.button (:1246/:1254/:1266) → Secondary tier (Extra-round mapping).
    //  - The window toggle button (:1246-1249) is ALWAYS enabled (no busy gate) and its CAPTION
    //    swaps on questAssistantWindowVisible ("Hide Floating Window"/"Show Floating Window") —
    //    re-synced every gated frame plus immediately on click (same-frame feedback).
    //  - "Accept All (N)" (:1251-1260): visible only while questAssistantAvailable != null &&
    //    Count > 0; enabled while questAssistantAcceptAllCoroutine == null; the live count is
    //    part of the BUTTON'S OWN LABEL — recomposed via SetUguiButtonLabel whenever the count
    //    changes (int cache), not a static caption.
    //  - "Submit Ready Items (N)" (:1262-1272): visible only while
    //    QuestAssistantCountReadyToSubmit() > 0 — a LIVE COMPUTED count re-evaluated every
    //    gated frame (the source calls it every OnGUI pass; it walks the snapshot, allocation-
    //    free), never a cached field; enabled while !QuestAssistantIsDailyQuestSubmitBusy()
    //    (that helper happens to equal part 1's gate — the source's own method is called, not a
    //    copy); click → StartDailyQuestAutoSubmitItems(false). Both conditional buttons keep
    //    FIXED slots (each one's position never depends on the other's visibility, mirroring
    //    the source's fixed x=440/x=630).
    //  - NARROW-COLUMN ADAPTATION (Extra-round precedent for source x that cannot fit): the
    //    source draws all four Quest Assistant buttons on ONE row spanning x=left..left+830 —
    //    far wider than the shell's ~446px content column. Dump + window toggle keep the
    //    source's own offsets (x=8/x=218 — they fit); the two conditional count-buttons move to
    //    a SECOND fixed row directly below, which exists (and advances the cursor 40px, one
    //    source row step) only while at least one of them is visible — when both are hidden the
    //    flow closes up exactly like the source's cursor (which never reserved space for them
    //    either; y += 40 covered the one row that remains).
    //  - THE DYNAMIC LIST (:1281-1304): a FLAT, NON-SCROLLED, two-level list — per quest one
    //    header line ("{Name}  [state={State} cat={Category}]  kind={ObjectiveKind}
    //    target={ObjectiveTargetId}", 20px pitch) followed by one line per ConditionSnapshot in
    //    that quest's own Conditions list ("    - {Description} ({Current}/{Needed})", 18px
    //    pitch — the indent is literal spaces IN the text; both flavors are the same statusStyle
    //    at the same x, only the pitch differs). PURE READ-ONLY TEXT — no buttons, no icons.
    //    Built as ONE flat pool of label rows (not two disjoint pools): each sync walks
    //    questAssistantSnapshot in order emitting header-row then condition-rows, growing the
    //    pool on demand and deactivating unused trailing rows; per-slot caches track the text
    //    (string compare — SetText only on change) and the FLAVOR (header vs condition — a
    //    flavor flip or a shown-count change dirties the relayout, since row y positions are a
    //    pure function of the flavor sequence). No overflow cap — the source draws every quest;
    //    the tab's own scroll view (this list is NOT its own scroll region, matching the
    //    source's reliance on the outer tab scroll) handles arbitrary length.
    //  - IDLE-PATH ALLOCATION FREEDOM: the resolver always PUBLISHES A FRESH LIST
    //    (questAssistantSnapshot = resolved — QuestAssistant.cs:353/:391; never mutated in
    //    place after publication), so the row-walk (which composes strings) re-runs only when
    //    the LIST REFERENCE changes; every other gated frame the quest-list sync is one
    //    ReferenceEquals. Counts (available.Count, CountReadyToSubmit()) and status strings are
    //    read/compared every gated frame — allocation-free.
    //  - EMPTY STATE (:1281-1288): while the snapshot is null or empty, one static wrapped hint
    //    ("(no active quests resolved yet — click Refresh, then check bugtopia.log for
    //    [QuestAssistant] lines)" — source literal, fixed 40px rect) shows instead of the list
    //    and the content ends there (the source returns early at y + 40).
    //  - Quest/condition rows are single-line (no wrap): the source's wordWrap statusStyle
    //    inside fixed 20/18px rects clips to one visual line anyway — wrapping at the shell's
    //    narrower width would overlap following rows.
    //
    // Positions replay the source cursor chains verbatim (content top margin 8 standing in for
    // startY, x=8 for the source's uniform left=40; fixed widths 240/300/200/220/180 kept, wide
    // 520/620/640 roles panelW-mapped, card inner metrics panelW-relative — the source's own
    // roles: full-width card buttons = width-32 with a 16px right margin, half-width pairs span
    // 16..right-edge flush with an 8px gap, i.e. halfW = (panelW-24)/2):
    //   part 1: auto-submit y=8 (240x32 PRIMARY)  (+40)  toggle y=48 (300x28)  (+34)
    //           status1 y=82 (panelW x28)  → returns 118
    //   part 2: +8 → card y=126 (panelW x216 = 36 header + 4x34 rows + 32 tall Claim All + 12
    //           pad; kit settings-panel chrome carries the L("DAILY CLAIMS") header); buttons
    //           at card-relative y 36/70/104/138 (28 tall; row 4 = Town Guide | Wild Gifts) and
    //           172 (Claim All, btnH+4=32 — the source's 4px-taller full-width closer);
    //           status2 y=348 (panelW x40)  → returns 392
    //   part 3: bird y=392 (240x32 PRIMARY)  (+40)  status3 y=432 (panelW x28)  → returns 468;
    //           dispatcher +40 → 508
    //   part 4: header y=508 (460x24 bold 14)  (+34)  row1 y=542: Dump (8, 200x32) | Window
    //           (218, 220x32)  (+40)  [row2 y=582: Accept All (8, 180x32) | Submit Ready
    //           (198, 200x32) — conditional, +40 while shown]  status4 (panelW x22)  (+28)
    //           then EITHER the hint (panelW x40, height = y+40) OR the rows (pitch 20/18,
    //           height = final y+20 — DrawQuestAssistantTab:1306; DrawNewFeaturesTab adds 0).
    // Everything through the two button rows is static (built-once positions); status4 and
    // below flow — RelayoutUguiShellNewFeaturesDailyQuests owns those positions and stores the
    // signature it laid out with (packed conditional-button visibilities + empty-state bit +
    // shown row count, PLUS both live counts — per this round's brief — plus the row-sync's
    // flavor-dirty flag).
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Daily
    // Quests sub-tab) — toggle re-sync (WithoutNotify), the THREE busy gates + the three Quest
    // Assistant gates (all recomputed live), the window-caption swap, both conditional buttons'
    // visibility + count labels, the four status labels (cached-string diffs), the quest-list
    // reference check, then the layout-signature check. No 0.5s tier (no measured paragraphs —
    // every height is a source-fixed rect). Per-frame sync disabled after 3 consecutive errors
    // (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesDailyQuestsHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;
            public Color StatusColor;             // the shared statusStyle color (grow-time rows)

            // -------- Part 1: Daily Quest Submit --------
            public GameObject AutoSubmitButton;   // gate 1 (3-way)
            public Toggle SkipFiveStarToggle;
            public GameObject Status1Label;
            public string Status1Shown;

            // -------- Part 2: Daily Claims --------
            public readonly GameObject[] ClaimsButtons = new GameObject[8]; // ONE shared 4-way gate
            public GameObject Status2Label;
            public string Status2Shown;

            // -------- Part 3: Bird Photo Submit --------
            public GameObject BirdPhotoButton;    // gate 3 (3-way, part-1's fields reordered)
            public GameObject Status3Label;
            public string Status3Shown;

            // -------- Part 4: Quest Assistant --------
            public GameObject DumpButton;         // gated on !questAssistantBusy
            public GameObject WindowButton;       // ALWAYS enabled; caption swaps
            public int WindowLabelState = -1;     // -1 never composed; else 0/1 = visible flag
            public GameObject AcceptAllButton;    // conditional; live count in label
            public int AcceptAllShownCount = -1;  // -1 = label never composed
            public GameObject SubmitReadyButton;  // conditional; live count in label
            public int SubmitReadyShownCount = -1;
            public GameObject Status4Label;
            public string Status4Shown;
            public GameObject EmptyHintLabel;

            // THE flat quest/condition row pool (file header): one pool, two flavors.
            public readonly List<GameObject> QuestRows = new List<GameObject>();
            public readonly List<string> QuestRowShown = new List<string>();
            public readonly List<bool> QuestRowIsCond = new List<bool>();
            public int QuestRowsShown;            // active slots after the last walk
            public List<QuestSnapshot> QuestListRef; // list REFERENCE the last walk consumed

            // Layout signature — the exact values the last relayout used
            public int LayoutPacked = -1;
            public int LayoutAcceptCount = -1;
            public int LayoutSubmitCount = -1;

            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesDailyQuestsHandle uguiShellNewFeaturesDailyQuests;

        // Static y anchors (file header cursor): the Quest Assistant button rows — everything
        // above status4 is built-once fixed.
        private const float UguiDailyQuestsQaRow1Y = 542f;
        private const float UguiDailyQuestsQaRow2Y = 582f;

        // ----------------------------------------------------------------------------------------
        // Busy conditions — the THREE EXACT source expressions (file header). Recomputed on
        // every call; never cache the results (live coroutine refs from background activity).
        // ----------------------------------------------------------------------------------------

        // DailyQuestSubmitFeature.cs:1722 — part 1's 3-way gate.
        private bool IsUguiDailyQuestsSubmitBusy()
        {
            return this.dailyQuestSubmitCoroutine != null
                || this.birdPhotoSubmitCoroutine != null
                || this.dailyClaimsCoroutine != null;
        }

        // DailyClaimsFeature.cs:101-104 — part 2's 4-WAY gate (the only one with
        // wildAnimalGiftCoroutine); shared by ALL 8 card buttons.
        private bool IsUguiDailyQuestsClaimsBusy()
        {
            return this.dailyQuestSubmitCoroutine != null
                || this.birdPhotoSubmitCoroutine != null
                || this.dailyClaimsCoroutine != null
                || this.wildAnimalGiftCoroutine != null;
        }

        // BirdPhotoSubmitFeature.cs:564 — part 3's gate: the SAME 3 fields as part 1, kept in
        // the source's own (different) order.
        private bool IsUguiDailyQuestsBirdPhotoBusy()
        {
            return this.birdPhotoSubmitCoroutine != null
                || this.dailyQuestSubmitCoroutine != null
                || this.dailyClaimsCoroutine != null;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of the newFeaturesSubTab == 1 chain: the four source sections stacked in
        // one transparent scroll view (this tab NEEDS its own scroll — the quest list can grow
        // arbitrarily long). Static chrome positioned here once; status4 + everything below it
        // belongs to the relayout. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesDailyQuestsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesDailyQuests = null;

            UguiShellNewFeaturesDailyQuestsHandle handle = new UguiShellNewFeaturesDailyQuestsHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesDailyQuestsContent", parent);
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
            handle.ScrollContent = scrollContent;
            handle.PanelW = panelW;

            // The two text roles beyond the kit defaults (file header): the shared statusStyle
            // (fontSize 11, wordWrap, uiText @ 0.82 — all four drawers build the identical
            // style) and the part-4 header (bold 14 in uiText @ 1.0).
            Color statusColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.82f);
            handle.StatusColor = statusColor;
            Color headerColor = this.UguiKitTextColor();

            // ==================== Part 1 — Daily Quest Submit ====================

            // :1722-1727 — themePrimaryButtonStyle → Primary tier; gate 1 (3-way).
            handle.AutoSubmitButton = this.CreateUguiPrimaryButton(scrollContent, "AutoSubmitButton",
                this.L("Auto Submit Daily Items"), new System.Action(this.OnUguiDailyQuestsAutoSubmitClicked));
            PlaceUguiTopLeft(handle.AutoSubmitButton, 8f, 8f, 240f, 32f);
            this.SetUguiButtonInteractable(handle.AutoSubmitButton, !this.IsUguiDailyQuestsSubmitBusy());

            // :1732-1746 — DrawSwitchToggle (L()s internally → one L here); flag +
            // SaveKeybinds(false) only, guarded on actual change.
            handle.SkipFiveStarToggle = this.CreateUguiCheckbox(scrollContent, "SkipFiveStarToggle",
                this.L("Skip 5 Star Items"), this.dailyQuestSubmitSkipFiveStar,
                new System.Action<bool>(this.OnUguiDailyQuestsSkipFiveStarToggled));
            PlaceUguiTopLeft(handle.SkipFiveStarToggle.gameObject, 8f, 48f, 300f, 28f);

            // :1750-1752 — status (statusStyle; 520 wide role → panelW).
            handle.Status1Shown = this.dailyQuestSubmitLastStatus ?? string.Empty;
            handle.Status1Label = this.CreateUguiLabel(scrollContent, "SubmitStatus",
                handle.Status1Shown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.Status1Label);
            PlaceUguiTopLeft(handle.Status1Label, 8f, 82f, panelW, 28f);

            // ==================== Part 2 — DAILY CLAIMS card ====================

            // :92-98 — themePanelStyle box + card outline + bold header → the kit settings-panel
            // chrome (Animal Care's card mapping); h = 36 + 4x34 + 32 + 12 = 216 (:94).
            GameObject claimsCard = this.CreateUguiSettingsMainPanel(scrollContent, "ClaimsPanel",
                this.L("DAILY CLAIMS"));
            PlaceUguiTopLeft(claimsCard, 8f, 126f, panelW, 216f);

            // Inner metrics: full-width = innerW inside a 16px margin on BOTH sides, and the
            // half-pairs now share exactly that span with an 8px gutter, so every button in the
            // card lines up on the same left and right edges.
            // 2026-07-22: halfW was (panelW - 24) / 2, which put the right column's edge at
            // 16 + halfW + 8 + halfW = panelW — i.e. flush against the panel with NO right margin,
            // overhanging the full-width rows by 16px. That was faithful to the IMGUI source (its
            // own 16+248+8+248 = 520 asymmetry) but reads as broken alignment; deliberately
            // dropping source parity here. (innerW - 8) / 2 lands the pair's right edge on
            // panelW - 16, matching innerW exactly.
            float innerW = panelW - 32f;
            float halfW = (innerW - 8f) * 0.5f;

            // All 8 = DrawDailyClaimsButton = themePrimaryButtonStyle (:161-164) → Primary tier,
            // all on the ONE 4-way gate. Order matches the drawer top-to-bottom.
            handle.ClaimsButtons[0] = this.CreateUguiPrimaryButton(claimsCard.transform, "LogAllStateButton",
                this.L("Log All State"), new System.Action(this.OnUguiDailyQuestsLogAllStateClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[0], 16f, 36f, innerW, 28f);

            handle.ClaimsButtons[1] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimSignInButton",
                this.L("Claim Sign-In Rewards"), new System.Action(this.OnUguiDailyQuestsClaimSignInClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[1], 16f, 70f, halfW, 28f);
            handle.ClaimsButtons[2] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimMailButton",
                this.L("Claim Mail All"), new System.Action(this.OnUguiDailyQuestsClaimMailClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[2], 16f + halfW + 8f, 70f, halfW, 28f);

            handle.ClaimsButtons[3] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimMiniBpButton",
                this.L("Claim Mini BP All"), new System.Action(this.OnUguiDailyQuestsClaimMiniBpClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[3], 16f, 104f, halfW, 28f);
            handle.ClaimsButtons[4] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimBpLoopButton",
                this.L("Claim BP Loop"), new System.Action(this.OnUguiDailyQuestsClaimBpLoopClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[4], 16f + halfW + 8f, 104f, halfW, 28f);

            handle.ClaimsButtons[5] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimTownGuideButton",
                this.L("Claim Town Guide"), new System.Action(this.OnUguiDailyQuestsClaimTownGuideClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[5], 16f, 138f, halfW, 28f);
            // THE EXCEPTION (:140-144, file header): direct StartWildAnimalClaimAllGifts +
            // inline status — NOT StartDailyClaimsAction like its 7 siblings.
            handle.ClaimsButtons[6] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimWildGiftsButton",
                this.L("Claim Wild Gifts"), new System.Action(this.OnUguiDailyQuestsClaimWildGiftsClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[6], 16f + halfW + 8f, 138f, halfW, 28f);

            // :147-150 — the full-width closer, btnH+4 = 32 tall (the source's 4px-taller row).
            handle.ClaimsButtons[7] = this.CreateUguiPrimaryButton(claimsCard.transform, "ClaimAllDailyButton",
                this.L("Claim All Daily"), new System.Action(this.OnUguiDailyQuestsClaimAllClicked));
            PlaceUguiTopLeft(handle.ClaimsButtons[7], 16f, 172f, innerW, 32f);

            bool claimsBusy = this.IsUguiDailyQuestsClaimsBusy();
            for (int i = 0; i < handle.ClaimsButtons.Length; i++)
            {
                this.SetUguiButtonInteractable(handle.ClaimsButtons[i], !claimsBusy);
            }

            // :154-157 — status below the card (card bottom 342 + 6; 520x40 role → panelW x40).
            handle.Status2Shown = this.dailyClaimsLastStatus ?? string.Empty;
            handle.Status2Label = this.CreateUguiLabel(scrollContent, "ClaimsStatus",
                handle.Status2Shown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.Status2Label);
            PlaceUguiTopLeft(handle.Status2Label, 8f, 348f, panelW, 40f);

            // ==================== Part 3 — Bird Photo Submit ====================

            // :564-569 — themePrimaryButtonStyle → Primary tier; gate 3 (part-1's fields,
            // source order kept).
            handle.BirdPhotoButton = this.CreateUguiPrimaryButton(scrollContent, "BirdPhotoButton",
                this.L("Submit Bird Photo"), new System.Action(this.OnUguiDailyQuestsBirdPhotoClicked));
            PlaceUguiTopLeft(handle.BirdPhotoButton, 8f, 392f, 240f, 32f);
            this.SetUguiButtonInteractable(handle.BirdPhotoButton, !this.IsUguiDailyQuestsBirdPhotoBusy());

            // :574-576 — status.
            handle.Status3Shown = this.birdPhotoSubmitLastStatus ?? string.Empty;
            handle.Status3Label = this.CreateUguiLabel(scrollContent, "BirdPhotoStatus",
                handle.Status3Shown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.Status3Label);
            PlaceUguiTopLeft(handle.Status3Label, 8f, 432f, panelW, 28f);

            // ==================== Part 4 — Quest Assistant (UNLOCALIZED literals) ====================

            // :1233-1235 — bold 14 uiText header (NOT the kit header color); dispatcher's +40
            // gap puts it at 508.
            GameObject qaHeader = this.CreateUguiLabel(scrollContent, "QaHeader",
                "Quest Assistant", 14f, headerColor, false);
            this.TrySetUguiLabelBold(qaHeader);
            PlaceUguiTopLeft(qaHeader, 8f, 508f, 460f, 24f);

            // Row 1 — the source's own offsets fit the column (file header): :1238-1242 Dump
            // (themePrimaryButtonStyle → Primary, gated on !questAssistantBusy) and :1246-1249
            // the window toggle (plain GUI.skin.button → Secondary, ALWAYS enabled, caption
            // swaps on questAssistantWindowVisible).
            handle.DumpButton = this.CreateUguiPrimaryButton(scrollContent, "DumpQuestsButton",
                "Dump Active Quests", new System.Action(this.OnUguiDailyQuestsDumpClicked));
            PlaceUguiTopLeft(handle.DumpButton, 8f, UguiDailyQuestsQaRow1Y, 200f, 32f);
            this.SetUguiButtonInteractable(handle.DumpButton, !this.questAssistantBusy);

            handle.WindowButton = this.CreateUguiSecondaryButton(scrollContent, "WindowToggleButton",
                this.questAssistantWindowVisible ? "Hide Floating Window" : "Show Floating Window",
                new System.Action(this.OnUguiDailyQuestsWindowToggleClicked));
            PlaceUguiTopLeft(handle.WindowButton, 218f, UguiDailyQuestsQaRow1Y, 220f, 32f);
            handle.WindowLabelState = this.questAssistantWindowVisible ? 1 : 0;

            // Row 2 — the two conditional count-in-label buttons (file header adaptation), FIXED
            // slots; visibility/labels/gates owned by the sync, the row's 40px advance by the
            // relayout. Both plain GUI.skin.button in source → Secondary tier.
            handle.AcceptAllButton = this.CreateUguiSecondaryButton(scrollContent, "AcceptAllButton",
                "Accept All (0)", new System.Action(this.OnUguiDailyQuestsAcceptAllClicked));
            PlaceUguiTopLeft(handle.AcceptAllButton, 8f, UguiDailyQuestsQaRow2Y, 180f, 32f);
            handle.AcceptAllButton.SetActive(false);

            handle.SubmitReadyButton = this.CreateUguiSecondaryButton(scrollContent, "SubmitReadyButton",
                "Submit Ready Items (0)", new System.Action(this.OnUguiDailyQuestsSubmitReadyClicked));
            PlaceUguiTopLeft(handle.SubmitReadyButton, 198f, UguiDailyQuestsQaRow2Y, 200f, 32f);
            handle.SubmitReadyButton.SetActive(false);

            // :1276-1278 — status (620x22 role → panelW x22; position owned by the relayout).
            handle.Status4Shown = this.questAssistantLastStatus ?? string.Empty;
            handle.Status4Label = this.CreateUguiLabel(scrollContent, "QaStatus",
                handle.Status4Shown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.Status4Label);

            // :1283-1286 — the empty-state hint (source literal; 620x40 role → panelW x40,
            // wrapped — it is ~2 lines at this width; visibility/position owned by the relayout).
            handle.EmptyHintLabel = this.CreateUguiLabel(scrollContent, "QaEmptyHint",
                "(no active quests resolved yet — click Refresh, then check bugtopia.log for [QuestAssistant] lines)",
                11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.EmptyHintLabel);
            handle.EmptyHintLabel.SetActive(false);

            // Quest/condition rows are pooled on demand by SyncUguiDailyQuestsQuestRows.

            // Seed pass: conditional buttons + rows from the live backend state, then the first
            // layout.
            this.SyncUguiDailyQuestsQuestAssistant(handle);
            this.SyncUguiDailyQuestsQuestRows(handle);
            this.RelayoutUguiShellNewFeaturesDailyQuests(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesDailyQuests = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the flowing part of the source cursor (status4 and below; the
        // region above is static), SetActives the empty hint, and stores the signature values
        // it laid out with.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellNewFeaturesDailyQuests(UguiShellNewFeaturesDailyQuestsHandle handle)
        {
            float panelW = handle.PanelW;

            // The conditional second button row advances the cursor one source row step (40)
            // only while at least one of its buttons is visible (file header adaptation).
            int acceptCount = this.questAssistantAvailable != null ? this.questAssistantAvailable.Count : 0;
            int submitCount = this.QuestAssistantCountReadyToSubmit();
            bool condRow = acceptCount > 0 || submitCount > 0;
            float yCur = UguiDailyQuestsQaRow1Y + 40f;   // :1274 y += 40 (row 1)
            if (condRow)
            {
                yCur += 40f;                              // the adapted second row
            }

            // :1278-1279 — status, advance 28.
            PlaceUguiTopLeft(handle.Status4Label, 8f, yCur, panelW, 22f);
            yCur += 28f;

            List<QuestSnapshot> snap = this.questAssistantSnapshot;
            bool emptyVisible = snap == null || snap.Count == 0;    // :1281
            SetUguiGoActive(handle.EmptyHintLabel, emptyVisible);
            float contentHeight;
            if (emptyVisible)
            {
                // :1283-1287 — the hint, then return y + 40.
                PlaceUguiTopLeft(handle.EmptyHintLabel, 8f, yCur, panelW, 40f);
                contentHeight = yCur + 40f;
            }
            else
            {
                // :1290-1304 — the flat list: pitch 20 for header rows, 18 for condition rows
                // (rect height = pitch in the source), positions a pure function of the flavor
                // sequence the pool carries.
                for (int i = 0; i < handle.QuestRowsShown && i < handle.QuestRows.Count; i++)
                {
                    float pitch = handle.QuestRowIsCond[i] ? 18f : 20f;
                    PlaceUguiTopLeft(handle.QuestRows[i], 8f, yCur, panelW, pitch);
                    yCur += pitch;
                }
                contentHeight = yCur + 20f;               // :1306 return y + 20
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, contentHeight);

            handle.LayoutPacked = ComputeUguiDailyQuestsLayoutPacked(
                acceptCount > 0, submitCount > 0, emptyVisible, handle.QuestRowsShown);
            handle.LayoutAcceptCount = acceptCount;
            handle.LayoutSubmitCount = submitCount;
        }

        // Packed layout drivers (file header): both conditional-button visibilities, the
        // empty-state bit, and the flattened shown row count. The two live counts ride
        // alongside as their own signature ints (LayoutAcceptCount/LayoutSubmitCount).
        private static int ComputeUguiDailyQuestsLayoutPacked(bool acceptVisible, bool submitVisible,
            bool emptyVisible, int shownRows)
        {
            return (acceptVisible ? 1 : 0)
                | (submitVisible ? 1 : 0) << 1
                | (emptyVisible ? 1 : 0) << 2
                | shownRows << 3;
        }

        // ----------------------------------------------------------------------------------------
        // Quest Assistant control sync — gates, caption swap, conditional visibility + live
        // count-in-label recomposition. Runs every gated frame (and once at build).
        // ----------------------------------------------------------------------------------------

        private void SyncUguiDailyQuestsQuestAssistant(UguiShellNewFeaturesDailyQuestsHandle handle)
        {
            // :1238 — Dump gate.
            this.SetUguiButtonInteractable(handle.DumpButton, !this.questAssistantBusy);

            // :1246 — caption swap (the window button itself is never disabled).
            int windowState = this.questAssistantWindowVisible ? 1 : 0;
            if (windowState != handle.WindowLabelState)
            {
                handle.WindowLabelState = windowState;
                this.SetUguiButtonLabel(handle.WindowButton,
                    windowState == 1 ? "Hide Floating Window" : "Show Floating Window");
            }

            // :1251-1260 — Accept All: visible while the available list is non-empty; the live
            // count is IN the label (recomposed only when it changes); enabled while the
            // accept-all coroutine is idle.
            int acceptCount = this.questAssistantAvailable != null ? this.questAssistantAvailable.Count : 0;
            SetUguiGoActive(handle.AcceptAllButton, acceptCount > 0);
            if (acceptCount != handle.AcceptAllShownCount)
            {
                handle.AcceptAllShownCount = acceptCount;
                if (acceptCount > 0)
                {
                    this.SetUguiButtonLabel(handle.AcceptAllButton, "Accept All (" + acceptCount + ")");
                }
            }
            this.SetUguiButtonInteractable(handle.AcceptAllButton, this.questAssistantAcceptAllCoroutine == null);

            // :1262-1272 — Submit Ready: the count is LIVE COMPUTED every gated frame (file
            // header — never cached across frames); visible while > 0; enabled via the source's
            // own busy helper.
            int submitCount = this.QuestAssistantCountReadyToSubmit();
            SetUguiGoActive(handle.SubmitReadyButton, submitCount > 0);
            if (submitCount != handle.SubmitReadyShownCount)
            {
                handle.SubmitReadyShownCount = submitCount;
                if (submitCount > 0)
                {
                    this.SetUguiButtonLabel(handle.SubmitReadyButton, "Submit Ready Items (" + submitCount + ")");
                }
            }
            this.SetUguiButtonInteractable(handle.SubmitReadyButton, !this.QuestAssistantIsDailyQuestSubmitBusy());
        }

        // ----------------------------------------------------------------------------------------
        // Quest/condition row pool — ONE flat pool, header and condition flavors interleaved in
        // snapshot order (file header). Returns true when the relayout must re-run (shown count
        // or any slot's flavor changed — row positions depend on exactly those).
        // ----------------------------------------------------------------------------------------

        private bool SyncUguiDailyQuestsQuestRows(UguiShellNewFeaturesDailyQuestsHandle handle)
        {
            // Idle path: the resolver always publishes a FRESH list (file header), so an
            // unchanged reference means nothing displayed can have changed.
            List<QuestSnapshot> snap = this.questAssistantSnapshot;
            if (ReferenceEquals(snap, handle.QuestListRef))
            {
                return false;
            }
            handle.QuestListRef = snap;

            bool layoutDirty = false;
            int slot = 0;
            if (snap != null)
            {
                for (int i = 0; i < snap.Count; i++)
                {
                    // :1293 — the quest header line (composition verbatim).
                    QuestSnapshot q = snap[i];
                    string line = q.Name + "  [state=" + q.State + " cat=" + q.Category
                        + "]  kind=" + q.ObjectiveKind + " target=" + q.ObjectiveTargetId;
                    if (this.SyncUguiDailyQuestsQuestRowSlot(handle, slot++, false, line))
                    {
                        layoutDirty = true;
                    }

                    // :1297-1303 — that quest's OWN condition lines, immediately after it
                    // (indent = literal spaces in the text, per the source).
                    for (int c = 0; c < q.Conditions.Count; c++)
                    {
                        ConditionSnapshot cond = q.Conditions[c];
                        string condLine = "    - " + cond.Description
                            + " (" + cond.Current + "/" + cond.Needed + ")";
                        if (this.SyncUguiDailyQuestsQuestRowSlot(handle, slot++, true, condLine))
                        {
                            layoutDirty = true;
                        }
                    }
                }
            }

            // Deactivate unused trailing rows (standard pool tail).
            for (int i = slot; i < handle.QuestRows.Count; i++)
            {
                GameObject row = handle.QuestRows[i];
                if (row != null && row.activeSelf)
                {
                    row.SetActive(false);
                }
            }

            if (slot != handle.QuestRowsShown)
            {
                handle.QuestRowsShown = slot;
                layoutDirty = true;
            }
            return layoutDirty;
        }

        // One pooled slot: grow on demand, reactivate, retexture on string change, reflavor on
        // header/condition flip. Returns true when the slot's FLAVOR changed (a position
        // driver); pure text changes don't move anything.
        private bool SyncUguiDailyQuestsQuestRowSlot(UguiShellNewFeaturesDailyQuestsHandle handle,
            int slot, bool isCond, string text)
        {
            bool flavorChanged = false;
            if (slot >= handle.QuestRows.Count)
            {
                // Grow the pool: a plain statusStyle label (single-line — file header); the
                // relayout positions it this same pass (growth always dirties the layout via
                // the shown-count change).
                GameObject row = this.CreateUguiLabel(handle.ScrollContent, "QuestRow" + slot,
                    string.Empty, 11f, handle.StatusColor, false);
                handle.QuestRows.Add(row);
                handle.QuestRowShown.Add(null);
                handle.QuestRowIsCond.Add(false);
            }

            GameObject pooled = handle.QuestRows[slot];
            if (pooled != null && !pooled.activeSelf)
            {
                pooled.SetActive(true);
            }

            if (handle.QuestRowIsCond[slot] != isCond)
            {
                handle.QuestRowIsCond[slot] = isCond;
                flavorChanged = true;
            }

            if (!string.Equals(handle.QuestRowShown[slot], text, StringComparison.Ordinal))
            {
                handle.QuestRowShown[slot] = text;
                this.SetUguiLabelText(pooled, text);
            }
            return flavorChanged;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesDailyQuestsOnUpdate()
        {
            UguiShellNewFeaturesDailyQuestsHandle handle = this.uguiShellNewFeaturesDailyQuests;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellDailyQuestsSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-sync (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.SkipFiveStarToggle, this.dailyQuestSubmitSkipFiveStar);

                // The THREE busy gates — the FULL live conditions recomputed EVERY gated frame
                // (file header: coroutine refs change from background activity; a disabled
                // button must re-enable on its own). SetUguiButtonInteractable self-diffs.
                this.SetUguiButtonInteractable(handle.AutoSubmitButton, !this.IsUguiDailyQuestsSubmitBusy());
                bool claimsBusy = this.IsUguiDailyQuestsClaimsBusy();
                for (int i = 0; i < handle.ClaimsButtons.Length; i++)
                {
                    this.SetUguiButtonInteractable(handle.ClaimsButtons[i], !claimsBusy);
                }
                this.SetUguiButtonInteractable(handle.BirdPhotoButton, !this.IsUguiDailyQuestsBirdPhotoBusy());

                // Quest Assistant controls — gates + caption swap + conditional visibility +
                // live count-in-label recomposition.
                this.SyncUguiDailyQuestsQuestAssistant(handle);

                // Status lines — background coroutines rewrite these; cached-string diffs.
                this.SyncUguiSelfLabelText(handle.Status1Label, ref handle.Status1Shown,
                    this.dailyQuestSubmitLastStatus ?? string.Empty);
                this.SyncUguiSelfLabelText(handle.Status2Label, ref handle.Status2Shown,
                    this.dailyClaimsLastStatus ?? string.Empty);
                this.SyncUguiSelfLabelText(handle.Status3Label, ref handle.Status3Shown,
                    this.birdPhotoSubmitLastStatus ?? string.Empty);
                this.SyncUguiSelfLabelText(handle.Status4Label, ref handle.Status4Shown,
                    this.questAssistantLastStatus ?? string.Empty);

                // Quest/condition rows — one ReferenceEquals on the idle path (file header);
                // a fresh resolve rebuilds the pool and reports whether positions moved.
                bool rowsDirty = this.SyncUguiDailyQuestsQuestRows(handle);

                // Layout signature — conditional-button visibilities + empty bit + shown row
                // count (packed) PLUS both live counts, plus the row-sync's flavor verdict.
                List<QuestSnapshot> snap = this.questAssistantSnapshot;
                int acceptCount = this.questAssistantAvailable != null ? this.questAssistantAvailable.Count : 0;
                int submitCount = this.QuestAssistantCountReadyToSubmit();
                int packed = ComputeUguiDailyQuestsLayoutPacked(
                    acceptCount > 0, submitCount > 0, snap == null || snap.Count == 0, handle.QuestRowsShown);
                if (rowsDirty || packed != handle.LayoutPacked
                    || acceptCount != handle.LayoutAcceptCount
                    || submitCount != handle.LayoutSubmitCount)
                {
                    this.RelayoutUguiShellNewFeaturesDailyQuests(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/DailyQuests content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order)
        // ----------------------------------------------------------------------------------------

        // DailyQuestSubmitFeature.cs:1724-1727.
        private void OnUguiDailyQuestsAutoSubmitClicked()
        {
            this.StartDailyQuestAutoSubmitItems(silent: false);
        }

        // DailyQuestSubmitFeature.cs:1732-1746 — flag + SaveKeybinds(false) in try/catch ONLY
        // (no notification), guarded on actual change (kit checkbox build-fire idiom).
        private void OnUguiDailyQuestsSkipFiveStarToggled(bool value)
        {
            if (value == this.dailyQuestSubmitSkipFiveStar)
            {
                return;
            }
            this.dailyQuestSubmitSkipFiveStar = value;
            try
            {
                this.SaveKeybinds(false);
            }
            catch
            {
            }
        }

        // DailyClaimsFeature.cs:107-110.
        private void OnUguiDailyQuestsLogAllStateClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsLogAllStateRoutine());
        }

        // DailyClaimsFeature.cs:113-116.
        private void OnUguiDailyQuestsClaimSignInClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimSignInRoutine());
        }

        // DailyClaimsFeature.cs:118-121.
        private void OnUguiDailyQuestsClaimMailClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimMailRoutine());
        }

        // DailyClaimsFeature.cs:124-127.
        private void OnUguiDailyQuestsClaimMiniBpClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimMiniBpAllRoutine());
        }

        // DailyClaimsFeature.cs:129-132.
        private void OnUguiDailyQuestsClaimBpLoopClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimBpLoopRoutine());
        }

        // DailyClaimsFeature.cs:135-138.
        private void OnUguiDailyQuestsClaimTownGuideClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimTownGuideRoutine());
        }

        // DailyClaimsFeature.cs:140-144 — THE EXCEPTION (file header): direct call + inline
        // unlocalized status literal; deliberately NOT StartDailyClaimsAction.
        private void OnUguiDailyQuestsClaimWildGiftsClicked()
        {
            this.StartWildAnimalClaimAllGifts(silent: false);
            this.dailyClaimsLastStatus = "Wild gift claim started.";
        }

        // DailyClaimsFeature.cs:147-150.
        private void OnUguiDailyQuestsClaimAllClicked()
        {
            this.StartDailyClaimsAction(this.DailyClaimsClaimAllRoutine());
        }

        // BirdPhotoSubmitFeature.cs:566-569.
        private void OnUguiDailyQuestsBirdPhotoClicked()
        {
            this.StartBirdPhotoAutoSubmit(silent: false);
        }

        // QuestAssistant.cs:1239-1242 — the method carries its own busy/min-interval guards, so
        // a same-frame race click is harmless (same as the IMGUI twin).
        private void OnUguiDailyQuestsDumpClicked()
        {
            this.QuestAssistantOnDumpButtonClicked();
        }

        // QuestAssistant.cs:1246-1249 — toggle, then an immediate caption re-sync so the label
        // flips this same frame (Teleport click precedent; the next gated frame would catch it
        // anyway).
        private void OnUguiDailyQuestsWindowToggleClicked()
        {
            this.QuestAssistantToggleWindow();
            UguiShellNewFeaturesDailyQuestsHandle handle = this.uguiShellNewFeaturesDailyQuests;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                int windowState = this.questAssistantWindowVisible ? 1 : 0;
                if (windowState != handle.WindowLabelState)
                {
                    handle.WindowLabelState = windowState;
                    this.SetUguiButtonLabel(handle.WindowButton,
                        windowState == 1 ? "Hide Floating Window" : "Show Floating Window");
                }
            }
            catch { }
        }

        // QuestAssistant.cs:1254-1257 — internally guarded (coroutine + empty-list checks).
        private void OnUguiDailyQuestsAcceptAllClicked()
        {
            this.QuestAssistantOnAcceptAllClicked();
        }

        // QuestAssistant.cs:1266-1269 — the source reuses the daily-submit backend verbatim.
        private void OnUguiDailyQuestsSubmitReadyClicked()
        {
            this.StartDailyQuestAutoSubmitItems(false);
        }
    }
}
