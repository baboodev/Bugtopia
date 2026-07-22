using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 7 of 8 (migration plan item 12): the
    // SEA CLEAN sub-tab — newFeaturesSubTab == 7 (AnimalCareFeature.cs:64-66 dispatcher) →
    // DrawSeaCleanQteTab (SeaCleanQteFeature.cs:891-1040), replayed here top-to-bottom. The tab
    // reads toggle fields owned by three sibling backends (SeaCleanBannerHideFeature.cs,
    // LittleWhaleFinderFeature.cs, CorruptionCleanseFeature.cs) — none of which has a drawer of
    // its own — plus the mod-wide farmState/autoFarmStatus pair.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend it touches stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same action methods (all directly
    //    on HeartopiaComplete via the feature partials; ZERO backend interop additions:
    //    StartLittleWhaleTeleport, SaveKeybinds, AddMenuNotification, FormatKeybindLabel + the
    //    fields/consts).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellSeaCleanSubIndex = 7, declared with their siblings in UguiShellTabIndices.cs),
    //    never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the drawer, replayed exactly:
    //  - SAVE ASYMMETRY (the round's main trap): the master "Auto Sea Clean" toggle (:911-918)
    //    posts the green/red toast and does NOT SaveKeybinds — genuinely absent in the source,
    //    not an oversight; do NOT add one. "Auto-Cleanse Corrupted" (:997-1005) posts the SAME
    //    toast palette AND SaveKeybinds(false), in that order. The three middle toggles
    //    (:936-960 — No Delays / Hide Banner / Little Whale Finder) are flag + save only, no
    //    toast. Toast palette = the source literals green (0.45,1,0.55) / red (1,0.55,0.55)
    //    (NOT Extra's 1,0.5,0.4 red), and the toast strings are UNLOCALIZED interpolations.
    //  - RADIUS SLIDER (:926-933): snaps to the NEAREST 0.5 — Mathf.Round(value * 2f) / 2f
    //    (not Ice Skating's nearest-50, not tenths) — over [SeaCleanAutoRadiusMin=1,
    //    SeaCleanAutoRadiusMax=20]; SaveKeybinds(false) fires only when the snapped value
    //    differs from the stored one by > 0.0001f (the source's exact epsilon). Value label
    //    LF("Clean radius: {0:F1}m") sits LEFT of the slider on the same row (label 230 wide at
    //    left, slider 200x20 at left+240, y+2). The UGUI handler stores the snapped float; the
    //    per-frame epsilon re-sync pulls the handle onto it (Sand Sculpture's snap idiom).
    //  - TWO independent TWO-LEVEL conditionals, each with its own inner level:
    //      * Little Whale block (:963-992) — level 1 littleWhaleFinderEnabled shows the
    //        figurine status label (+26); level 2 littleWhalePresent ADDITIONALLY shows the
    //        primary "TELEPORT TO FIGURINE" button, and the block's tail advance is
    //        present ? 36 : 4 (:991) — the two levels are INDEPENDENT, never flattened.
    //      * Sea Clean status block (:1025-1037) — level 1 seaCleanQteEnabled shows the
    //        "Cleaned this session: {n}" counter (+24); level 2 non-empty seaCleanAutoLastStatus
    //        ADDITIONALLY shows the "Status: " line (panelW x40, +44).
    //    Plus ONE single-level conditional between them (:1013-1017): the "Cleansing now: "
    //    label, visible only while farmState == AutoFarmState.CleansingCorruption (enum field
    //    owned by Auto Farm — read-only here), its text LIVE (autoFarmStatus re-read every
    //    gated frame while visible, raw-reference diffed).
    //  - FIGURINE STATUS (:965-979) is a live THREE-WAY text: present + Camera.main → distance
    //    variant (styleNo = littleWhaleActiveConfigId - LittleWhaleConfigIdFirst + 1, meters
    //    shown (int)-truncated); present without camera → no-meters variant; not present → the
    //    "not found" hint. Recomposed on a (present|styleNo, intDist) tuple diff — whole-meter
    //    granularity while swimming, allocation-free otherwise (the Sanrio scene-row idiom).
    //    Only computed while the finder toggle shows the label, same as the source.
    //  - HOTKEY LINE (:1019-1023): read-only text — LF("Hotkey: {0} (rebind in Settings >
    //    Keybinds)", FormatKeybindLabel(seaCleanQteHotkey)); no control. KeyCode-cached so a
    //    rebind from Settings → Keybinds recomposes it live.
    //  - HEIGHT: CalculateSeaCleanQteTabHeight() returns a hardcoded 640f — a rough first-frame
    //    IMGUI estimate, deliberately NOT copied. The scroll-content height comes from the real
    //    relayout below (Food & Repair / Extra precedent): full cursor replay over whichever
    //    conditional blocks are currently visible (420 with everything off and fallback
    //    paragraph heights, up to 576 with everything on).
    //  - LOCALIZATION: DrawSwitchToggle and DrawPrimaryActionButton L() their labels internally
    //    (UiKitPrimitives.cs:749/:731), so the kit checkbox/button here get this.L(...) once at
    //    the call site (the source's own L("TELEPORT TO FIGURINE") into DrawPrimaryActionButton
    //    double-L's — one L is the intent, the Extra-round convention). Header/hints/status
    //    composites all go through L/LF at their call sites, matching the drawer; the two
    //    enable/disable toasts are raw interpolations (no L in the source — mirrored).
    //  - Text roles: header = bold 14 in uiText (:897-898); EVERY other label uses the drawer's
    //    bodyStyle = fontSize 12 wordWrap in uiSubTabText @ 0.92 (:900-906). Toggles are kit
    //    checkboxes (the kit carries the switch-row look); teleport = Primary tier (:986).
    //  - WRAPPED PARAGRAPHS (auto-clean hint :921-923 500x76+82, Aura Farm hint :1008-1010
    //    500x34+38): heights measured via the Pictures round's proven
    //    MeasureUguiPicturesWrappedHeight with the source rect heights (76/34) as fallbacks;
    //    advance = height + 6 / height + 4 (the source's rect+6 / rect+4 cursor steps). Both
    //    labels are ALWAYS active, so the 0.5s tick retries a failed measure whenever this
    //    sub-tab is visible (spike build-time caveat).
    //
    // Positions replay the drawer's cursor chain verbatim (content top margin 8 standing in for
    // startY, x=8 for the source's uniform left=40; fixed widths 460/360/240/230/200 kept, wide
    // 500 roles panelW-mapped — the Animal Care convention):
    //   header y=8 (460x24 bold 14)                                    (+34)
    //   toggle Auto Sea Clean y=42 (360x30)                            (+40)
    //   hint y=82 (panelW x hintH, source 76)                          (+hintH+6, source 82)
    //   radius row: label (230x22) | slider x=248 y+2 (200x20)         (+28)
    //   toggles No Delays / Hide Banner / Little Whale (360x30)        (+36 each)
    //   [figurine status (panelW x22) (+26); [teleport 240x30]; +36 present / +4 not]
    //   toggle Auto-Cleanse Corrupted (360x30)                         (+36)
    //   aura hint (panelW x auraH, source 34)                          (+auraH+4, source 38)
    //   [cleansing label (panelW x22)                                  (+26)]
    //   hotkey label (panelW x22)                                      (+26)
    //   [counter (panelW x22) (+24); [status (panelW x40) (+44)]]
    //   content height = final cursor + 20 (:1039 return y + 20).
    // Header + master toggle are static; the relayout owns everything from the hint down
    // (hintH can change on a measure retry) plus the conditional SetActives, re-run when the
    // layout signature changes — packed visibility bits (finderEnabled, finderEnabled&&present,
    // cleansing-active, qteEnabled, qteEnabled&&status-non-empty; hidden levels masked to 0,
    // the Extra convention) plus the two measured heights.
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Sea
    // Clean sub-tab) — 5 toggle re-syncs (SetIsOnWithoutNotify), the slider epsilon re-sync +
    // its float-cached value label, the figurine tuple diff (while shown), the cleansing
    // raw-ref diff (while shown), the hotkey KeyCode diff, the counter int diff + status
    // raw-ref diff (while shown), then the layout-signature check. The 0.5s tick carries only
    // the wrapped-paragraph measure retries. Per-frame sync disabled after 3 consecutive
    // errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesSeaCleanHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;

            // Master toggle — toast-only, NO SaveKeybinds (file header: the save asymmetry)
            public Toggle SeaCleanToggle;

            // Auto-clean hint paragraph (wrapped; measured, fallback 76 — the source rect)
            public GameObject HintLabel;
            public float HintH;
            public bool HintMeasureOk;

            // Clean radius row (nearest-0.5-snapped slider + live value label)
            public GameObject RadiusLabel;
            public float RadiusShownValue;    // float cache gating the label rebuild
            public Slider RadiusSlider;

            // The three flag+save toggles
            public Toggle NoDelayToggle;
            public Toggle HideBannerToggle;
            public Toggle LittleWhaleToggle;

            // Little Whale block — TWO independent visibility levels (file header)
            public GameObject FigurineStatusLabel;  // level 1: littleWhaleFinderEnabled
            public int FigurineSig;                 // (present | styleNo<<1); -1 = never composed
            public int FigurineDistInt;             // (int) meters; -1 no camera; MinValue absent
            public GameObject TeleportButton;       // level 2: finderEnabled AND littleWhalePresent

            // Auto-Cleanse toggle (toast + save) + its hint paragraph (measured, fallback 34)
            public Toggle AutoCleanseToggle;
            public GameObject AuraHintLabel;
            public float AuraHintH;
            public bool AuraHintMeasureOk;

            // Single-level conditional: LIVE "Cleansing now: " + autoFarmStatus
            public GameObject CleansingLabel;
            public string CleansingRawSeen;         // raw autoFarmStatus reference last composed

            // Read-only hotkey line (recomposes on rebind)
            public GameObject HotkeyLabel;
            public KeyCode HotkeyShown;

            // Sea Clean status block — the second two-level conditional (file header)
            public GameObject CounterLabel;         // level 1: seaCleanQteEnabled
            public int CounterShown;                // int cache gating the rebuild
            public GameObject StatusLabel;          // level 2: enabled AND non-empty status
            public string StatusRawSeen;            // raw seaCleanAutoLastStatus ref last composed

            // Layout signature — the exact values the last relayout used
            public int LayoutPacked = -1;
            public float LayoutHintH = -1f;
            public float LayoutAuraHintH = -1f;

            public float NextSlowSyncAt;            // 0.5s tick (measure retries only)
            public int ErrorCount;                  // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesSeaCleanHandle uguiShellNewFeaturesSeaClean;

        // The toast palette — the source's exact literals (:917/:1003; note the 0.55-red, not
        // Extra's 0.5/0.4 red).
        private static readonly Color UguiSeaCleanOnColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color UguiSeaCleanOffColor = new Color(1f, 0.55f, 0.55f);

        // ----------------------------------------------------------------------------------------
        // Small helpers
        // ----------------------------------------------------------------------------------------

        // The two hint paragraphs' texts, one copy each (builder + measurer both re-read via L
        // so a language change after a failed first measure re-measures the CURRENT string).
        private string BuildUguiSeaCleanHintText()
        {
            return this.L("Fully automatic ocean cleanup: swim near pollutants — the sea cleaner is equipped automatically (only from empty hands) and solo pollutants in range are cleaned instantly, nearest first. QTEs are auto-passed too (the public boss never misses when you clean it manually). Shared/body pollution has no client lever and is skipped.");
        }

        private string BuildUguiSeaCleanAuraHintText()
        {
            return this.L("Aura Farm: when the Corrupted debuff lands (Contamination radar on), teleport to the nearest cleansing coral and hold until it clears.");
        }

        // Measurement wrapper for the two wrapped paragraphs — reuses the Pictures round's
        // MeasureUguiPicturesWrappedHeight (the migration's one proven GetPreferredValues path)
        // with this round's texts/fallbacks. big=true → the auto-clean hint (fallback 76);
        // false → the Aura Farm hint (fallback 34) — both the source rect heights.
        private float MeasureUguiSeaCleanHintHeight(UguiShellNewFeaturesSeaCleanHandle handle, bool big, out bool ok)
        {
            if (big)
            {
                return this.MeasureUguiPicturesWrappedHeight(handle.HintLabel,
                    this.BuildUguiSeaCleanHintText(),
                    handle.PanelW, handle.HintH > 0f ? handle.HintH : 76f, out ok);
            }
            return this.MeasureUguiPicturesWrappedHeight(handle.AuraHintLabel,
                this.BuildUguiSeaCleanAuraHintText(),
                handle.PanelW, handle.AuraHintH > 0f ? handle.AuraHintH : 34f, out ok);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawSeaCleanQteTab: everything flat on one transparent scroll view (the
        // drawer paints no card chrome — Sand Sculpture's flat-tab precedent). Header + master
        // toggle positioned here once; everything below belongs to the relayout, which ALWAYS
        // runs at the end of the builder. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesSeaCleanContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesSeaClean = null;

            UguiShellNewFeaturesSeaCleanHandle handle = new UguiShellNewFeaturesSeaCleanHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesSeaCleanContent", parent);
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
            handle.PanelW = panelW;

            // The drawer's two text roles (file header): header = uiText, everything else =
            // the bodyStyle color uiSubTabText @ 0.92 (:906).
            Color headerColor = this.UguiKitTextColor();
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Header (:908 — 460x24, bold 14; STATIC) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header",
                this.L("Auto Sea Clean"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, 460f, 24f);

            // -------- Master toggle (:911-918 — toast only, NO save; STATIC) --------
            handle.SeaCleanToggle = this.CreateUguiCheckbox(scrollContent, "AutoSeaClean",
                this.L("Auto Sea Clean"), this.seaCleanQteEnabled,
                new System.Action<bool>(this.OnUguiSeaCleanQteToggled));
            PlaceUguiTopLeft(handle.SeaCleanToggle.gameObject, 8f, 42f, 360f, 30f);

            // -------- Auto-clean hint (:921-923 — wrapped; height owned by the relayout) --------
            handle.HintLabel = this.CreateUguiLabel(scrollContent, "Hint",
                this.BuildUguiSeaCleanHintText(), 12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.HintLabel);

            // -------- Radius row (:926-933 — label + nearest-0.5 slider) --------
            handle.RadiusShownValue = this.seaCleanAutoRadius;
            handle.RadiusLabel = this.CreateUguiLabel(scrollContent, "RadiusLabel",
                this.LF("Clean radius: {0:F1}m", this.seaCleanAutoRadius), 12f, mutedColor, false);
            handle.RadiusSlider = this.CreateUguiSlider(scrollContent, "RadiusSlider",
                SeaCleanAutoRadiusMin, SeaCleanAutoRadiusMax, this.seaCleanAutoRadius, false,
                new System.Action<float>(this.OnUguiSeaCleanRadiusChanged));

            // -------- The three flag+save toggles (:936-960) --------
            handle.NoDelayToggle = this.CreateUguiCheckbox(scrollContent, "CleanNoDelay",
                this.L("Clean Without Delays"), this.seaCleanCleanNoDelay,
                new System.Action<bool>(this.OnUguiSeaCleanNoDelayToggled));
            handle.HideBannerToggle = this.CreateUguiCheckbox(scrollContent, "HideBanner",
                this.L("Hide Crystal Clear Banner"), this.hideSeaCleanBannerEnabled,
                new System.Action<bool>(this.OnUguiSeaCleanHideBannerToggled));
            handle.LittleWhaleToggle = this.CreateUguiCheckbox(scrollContent, "LittleWhaleFinder",
                this.L("Little Whale Finder"), this.littleWhaleFinderEnabled,
                new System.Action<bool>(this.OnUguiSeaCleanLittleWhaleToggled));

            // -------- Little Whale block (:963-992 — two INDEPENDENT levels, file header) ------
            handle.FigurineSig = -1;                 // never composed
            handle.FigurineDistInt = int.MinValue;
            handle.FigurineStatusLabel = this.CreateUguiLabel(scrollContent, "FigurineStatus",
                string.Empty, 12f, mutedColor, false);
            handle.FigurineStatusLabel.SetActive(false);
            handle.TeleportButton = this.CreateUguiPrimaryButton(scrollContent, "TeleportFigurine",
                this.L("TELEPORT TO FIGURINE"),
                new System.Action(this.OnUguiSeaCleanLittleWhaleTeleportClicked));
            handle.TeleportButton.SetActive(false);

            // -------- Auto-Cleanse toggle (:997-1005 — toast AND save) + hint (:1008-1010) -----
            handle.AutoCleanseToggle = this.CreateUguiCheckbox(scrollContent, "AutoCleanse",
                this.L("Auto-Cleanse Corrupted"), this.autoCleanseCorruptedEnabled,
                new System.Action<bool>(this.OnUguiSeaCleanAutoCleanseToggled));
            handle.AuraHintLabel = this.CreateUguiLabel(scrollContent, "AuraHint",
                this.BuildUguiSeaCleanAuraHintText(), 12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.AuraHintLabel);

            // -------- Cleansing label (:1013-1017 — single-level conditional, LIVE text) -------
            handle.CleansingRawSeen = this.autoFarmStatus;
            handle.CleansingLabel = this.CreateUguiLabel(scrollContent, "CleansingNow",
                this.L("Cleansing now: ") + this.autoFarmStatus, 12f, mutedColor, false);
            handle.CleansingLabel.SetActive(false);

            // -------- Hotkey line (:1019-1023 — read-only) --------
            handle.HotkeyShown = this.seaCleanQteHotkey;
            handle.HotkeyLabel = this.CreateUguiLabel(scrollContent, "HotkeyHint",
                this.LF("Hotkey: {0} (rebind in Settings > Keybinds)", FormatKeybindLabel(this.seaCleanQteHotkey)),
                12f, mutedColor, false);

            // -------- Sea Clean status block (:1025-1037 — the second two-level shape) ---------
            handle.CounterShown = this.seaCleanAutoKillCount;
            handle.CounterLabel = this.CreateUguiLabel(scrollContent, "SessionCounter",
                this.LF("Cleaned this session: {0}", this.seaCleanAutoKillCount), 12f, mutedColor, false);
            handle.CounterLabel.SetActive(false);
            handle.StatusRawSeen = this.seaCleanAutoLastStatus;
            handle.StatusLabel = this.CreateUguiLabel(scrollContent, "SeaCleanStatus",
                this.L("Status: ") + this.seaCleanAutoLastStatus, 12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            handle.StatusLabel.SetActive(false);

            // First measurements (may run while this sub-tab is inactive; a rejected measure
            // keeps the source-rect fallback and the slow tick retries once actually visible —
            // the Pictures spike caveat).
            bool ok;
            handle.HintH = this.MeasureUguiSeaCleanHintHeight(handle, true, out ok);
            handle.HintMeasureOk = ok;
            handle.AuraHintH = this.MeasureUguiSeaCleanHintHeight(handle, false, out ok);
            handle.AuraHintMeasureOk = ok;

            // Seed pass: figurine text from the live backend state (only while its level-1 gate
            // shows it — the source computes it inside the if), then the first layout.
            if (this.littleWhaleFinderEnabled)
            {
                this.SyncUguiSeaCleanFigurineStatus(handle);
            }
            this.RelayoutUguiShellNewFeaturesSeaClean(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesSeaClean = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the drawer's cursor chain from the hint down (header + master
        // toggle are static; hintH can change on a measure retry), SetActives both two-level
        // conditional regions + the cleansing label, and stores the signature it laid out with.
        // The scroll-content height is the REAL final cursor + 20 (:1039) — deliberately NOT the
        // source's hardcoded CalculateSeaCleanQteTabHeight() = 640 (file header).
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellNewFeaturesSeaClean(UguiShellNewFeaturesSeaCleanHandle handle)
        {
            float panelW = handle.PanelW;

            // Static region above: header y=8 (+34), master toggle y=42 (+40) → hint top y=82.
            float yCur = 82f;
            PlaceUguiTopLeft(handle.HintLabel, 8f, yCur, panelW, handle.HintH);
            yCur += handle.HintH + 6f;                 // :924 — rect 76 + advance 82

            // Radius row (:926-929 — label at left, slider at left+240, y+2).
            PlaceUguiTopLeft(handle.RadiusLabel, 8f, yCur, 230f, 22f);
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 248f, yCur + 2f, 200f, 20f);
            yCur += 28f;                               // :934

            PlaceUguiTopLeft(handle.NoDelayToggle.gameObject, 8f, yCur, 360f, 30f);
            yCur += 36f;                               // :942
            PlaceUguiTopLeft(handle.HideBannerToggle.gameObject, 8f, yCur, 360f, 30f);
            yCur += 36f;                               // :952
            PlaceUguiTopLeft(handle.LittleWhaleToggle.gameObject, 8f, yCur, 360f, 30f);
            yCur += 36f;                               // :961

            // Little Whale block — the two INDEPENDENT levels (:963-992): the label needs
            // level 1 only; the button needs BOTH; the tail advance is present ? 36 : 4.
            bool finderOn = this.littleWhaleFinderEnabled;
            bool present = this.littleWhalePresent;
            SetUguiGoActive(handle.FigurineStatusLabel, finderOn);
            SetUguiGoActive(handle.TeleportButton, finderOn && present);
            if (finderOn)
            {
                PlaceUguiTopLeft(handle.FigurineStatusLabel, 8f, yCur, panelW, 22f);
                yCur += 26f;                           // :980
                if (present)
                {
                    PlaceUguiTopLeft(handle.TeleportButton, 8f, yCur, 240f, 30f);  // :986
                }
                yCur += present ? 36f : 4f;            // :991 — the two-level advance
            }

            PlaceUguiTopLeft(handle.AutoCleanseToggle.gameObject, 8f, yCur, 360f, 30f);
            yCur += 36f;                               // :1006

            PlaceUguiTopLeft(handle.AuraHintLabel, 8f, yCur, panelW, handle.AuraHintH);
            yCur += handle.AuraHintH + 4f;             // :1011 — rect 34 + advance 38

            // Single-level conditional (:1013-1017).
            bool cleansing = this.farmState == HeartopiaComplete.AutoFarmState.CleansingCorruption;
            SetUguiGoActive(handle.CleansingLabel, cleansing);
            if (cleansing)
            {
                PlaceUguiTopLeft(handle.CleansingLabel, 8f, yCur, panelW, 22f);
                yCur += 26f;                           // :1016
            }

            PlaceUguiTopLeft(handle.HotkeyLabel, 8f, yCur, panelW, 22f);
            yCur += 26f;                               // :1023

            // Sea Clean status block — the second two-level shape (:1025-1037).
            bool enabled = this.seaCleanQteEnabled;
            bool haveStatus = !string.IsNullOrEmpty(this.seaCleanAutoLastStatus);
            SetUguiGoActive(handle.CounterLabel, enabled);
            SetUguiGoActive(handle.StatusLabel, enabled && haveStatus);
            if (enabled)
            {
                PlaceUguiTopLeft(handle.CounterLabel, 8f, yCur, panelW, 22f);
                yCur += 24f;                           // :1030
                if (haveStatus)
                {
                    PlaceUguiTopLeft(handle.StatusLabel, 8f, yCur, panelW, 40f);
                    yCur += 44f;                       // :1035
                }
            }

            // The REAL height (:1039 return y + 20) — never the hardcoded 640 (file header).
            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f);

            handle.LayoutPacked = this.ComputeUguiSeaCleanLayoutPacked();
            handle.LayoutHintH = handle.HintH;
            handle.LayoutAuraHintH = handle.AuraHintH;
        }

        // Packed layout drivers (file header): the five visibility bits, with each inner level
        // masked to 0 while its outer level hides it (the Extra convention — hidden state churn
        // must not trigger relayouts).
        private int ComputeUguiSeaCleanLayoutPacked()
        {
            int packed = (this.littleWhaleFinderEnabled ? 1 : 0)
                | (this.farmState == HeartopiaComplete.AutoFarmState.CleansingCorruption ? 4 : 0)
                | (this.seaCleanQteEnabled ? 8 : 0);
            if (this.littleWhaleFinderEnabled && this.littleWhalePresent)
            {
                packed |= 2;
            }
            if (this.seaCleanQteEnabled && !string.IsNullOrEmpty(this.seaCleanAutoLastStatus))
            {
                packed |= 16;
            }
            return packed;
        }

        // ----------------------------------------------------------------------------------------
        // Figurine status — the live three-way text (:965-979), recomposed on a
        // (present|styleNo, intDist) tuple change; (int) truncation means whole-meter
        // granularity while swimming (the Sanrio scene-row idiom). Called only while the
        // finder toggle shows the label, same as the source's enclosing if.
        // ----------------------------------------------------------------------------------------

        private void SyncUguiSeaCleanFigurineStatus(UguiShellNewFeaturesSeaCleanHandle handle)
        {
            bool present = this.littleWhalePresent;
            int sig = 0;
            int distInt = int.MinValue;
            int styleNo = 0;
            if (present)
            {
                styleNo = this.littleWhaleActiveConfigId - LittleWhaleConfigIdFirst + 1;   // :970
                sig = 1 | (styleNo << 1);
                Camera figCam = Camera.main;                                               // :968
                distInt = figCam != null
                    ? (int)Vector3.Distance(figCam.transform.position, this.littleWhaleLastPos)
                    : -1;                                                                  // :969
            }
            if (sig == handle.FigurineSig && distInt == handle.FigurineDistInt)
            {
                return;
            }
            handle.FigurineSig = sig;
            handle.FigurineDistInt = distInt;

            string text;
            if (present)
            {
                text = distInt >= 0
                    ? this.LF("Figurine located (style {0}) — {1}m away", styleNo, distInt)  // :972
                    : this.LF("Figurine located (style {0})", styleNo);                      // :973
            }
            else
            {
                text = this.L("Figurine not found — take the daily task and enter the canyon"); // :977
            }
            this.SetUguiLabelText(handle.FigurineStatusLabel, text);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesSeaCleanOnUpdate()
        {
            UguiShellNewFeaturesSeaCleanHandle handle = this.uguiShellNewFeaturesSeaClean;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellSeaCleanSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.SeaCleanToggle, this.seaCleanQteEnabled);
                this.SyncUguiToggleFromField(handle.NoDelayToggle, this.seaCleanCleanNoDelay);
                this.SyncUguiToggleFromField(handle.HideBannerToggle, this.hideSeaCleanBannerEnabled);
                this.SyncUguiToggleFromField(handle.LittleWhaleToggle, this.littleWhaleFinderEnabled);
                this.SyncUguiToggleFromField(handle.AutoCleanseToggle, this.autoCleanseCorruptedEnabled);

                // Radius re-sync: pulls the handle onto the nearest-0.5-snapped field after a
                // drag AND mirrors external IMGUI edits (epsilon diff, WithoutNotify — Sand
                // idiom). The value label rebuilds only when the snapped float changed.
                if (handle.RadiusSlider != null
                    && Mathf.Abs(handle.RadiusSlider.value - this.seaCleanAutoRadius) > 0.0005f)
                {
                    handle.RadiusSlider.SetValueWithoutNotify(this.seaCleanAutoRadius);
                }
                if (handle.RadiusShownValue != this.seaCleanAutoRadius)
                {
                    handle.RadiusShownValue = this.seaCleanAutoRadius;
                    this.SetUguiLabelText(handle.RadiusLabel,
                        this.LF("Clean radius: {0:F1}m", this.seaCleanAutoRadius));
                }

                // Figurine status — only while its level-1 gate shows it (source :963).
                if (this.littleWhaleFinderEnabled)
                {
                    this.SyncUguiSeaCleanFigurineStatus(handle);
                }

                // Cleansing label — LIVE re-read every gated frame while visible (:1013-1016);
                // the raw-ref cache keeps unchanged frames alloc-free.
                if (this.farmState == HeartopiaComplete.AutoFarmState.CleansingCorruption)
                {
                    string cleansingRaw = this.autoFarmStatus;
                    if (!ReferenceEquals(cleansingRaw, handle.CleansingRawSeen))
                    {
                        handle.CleansingRawSeen = cleansingRaw;
                        this.SetUguiLabelText(handle.CleansingLabel,
                            this.L("Cleansing now: ") + cleansingRaw);
                    }
                }

                // Hotkey line — recomposes on a rebind from Settings → Keybinds (:1019-1022).
                if (handle.HotkeyShown != this.seaCleanQteHotkey)
                {
                    handle.HotkeyShown = this.seaCleanQteHotkey;
                    this.SetUguiLabelText(handle.HotkeyLabel,
                        this.LF("Hotkey: {0} (rebind in Settings > Keybinds)",
                            FormatKeybindLabel(this.seaCleanQteHotkey)));
                }

                // Counter + status — while the enabled block shows them (:1025-1036).
                if (this.seaCleanQteEnabled)
                {
                    if (handle.CounterShown != this.seaCleanAutoKillCount)
                    {
                        handle.CounterShown = this.seaCleanAutoKillCount;
                        this.SetUguiLabelText(handle.CounterLabel,
                            this.LF("Cleaned this session: {0}", this.seaCleanAutoKillCount));
                    }
                    string statusRaw = this.seaCleanAutoLastStatus;
                    if (!string.IsNullOrEmpty(statusRaw)
                        && !ReferenceEquals(statusRaw, handle.StatusRawSeen))
                    {
                        handle.StatusRawSeen = statusRaw;
                        this.SetUguiLabelText(handle.StatusLabel, this.L("Status: ") + statusRaw);
                    }
                }

                // 0.5s tick — wrapped-paragraph measure retries (both labels always active on
                // this tab, so retrying whenever the sub-tab is visible is meaningful).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    if (!handle.HintMeasureOk)
                    {
                        bool ok;
                        handle.HintH = this.MeasureUguiSeaCleanHintHeight(handle, true, out ok);
                        handle.HintMeasureOk = ok;
                    }
                    if (!handle.AuraHintMeasureOk)
                    {
                        bool ok;
                        handle.AuraHintH = this.MeasureUguiSeaCleanHintHeight(handle, false, out ok);
                        handle.AuraHintMeasureOk = ok;
                    }
                }

                // Layout signature — packed visibility bits + the two measured heights.
                if (handle.LayoutPacked != this.ComputeUguiSeaCleanLayoutPacked()
                    || handle.LayoutHintH != handle.HintH
                    || handle.LayoutAuraHintH != handle.AuraHintH)
                {
                    this.RelayoutUguiShellNewFeaturesSeaClean(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/SeaClean content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same
        // order). All guard on "value actually changed": the kit checkbox fires onChanged once
        // at build (by design) — the guard absorbs it, and WithoutNotify re-syncs never fire
        // events at all.
        // ----------------------------------------------------------------------------------------

        // :911-918 — toast ONLY. NO SaveKeybinds — genuinely absent in the source (verified;
        // contrast with the Auto-Cleanse toggle below, which DOES save); do not "fix" it. The
        // toast string is an unlocalized interpolation, the palette the source literals. Then
        // an immediate refresh of the block this toggle gates (Extra's Sanrio-toggle idiom) so
        // the counter/status region reacts this same frame.
        private void OnUguiSeaCleanQteToggled(bool value)
        {
            if (value == this.seaCleanQteEnabled)
            {
                return;
            }
            this.seaCleanQteEnabled = value;
            this.AddMenuNotification(
                $"Auto Sea Clean {(value ? "Enabled" : "Disabled")}",
                value ? UguiSeaCleanOnColor : UguiSeaCleanOffColor);

            UguiShellNewFeaturesSeaCleanHandle handle = this.uguiShellNewFeaturesSeaClean;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                this.RelayoutUguiShellNewFeaturesSeaClean(handle);
            }
            catch { }
        }

        // :928-933 — the EXACT source snap: nearest 0.5 via Mathf.Round(value * 2f) / 2f, and
        // SaveKeybinds(false) fires only when the snapped value differs from the stored one by
        // more than the source's 0.0001f epsilon. The per-frame re-sync pulls the slider handle
        // onto the snapped value.
        private void OnUguiSeaCleanRadiusChanged(float value)
        {
            float snapped = Mathf.Round(value * 2f) / 2f;
            if (Mathf.Abs(snapped - this.seaCleanAutoRadius) > 0.0001f)
            {
                this.seaCleanAutoRadius = snapped;
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // :936-941 — flag + save only.
        private void OnUguiSeaCleanNoDelayToggled(bool value)
        {
            if (value == this.seaCleanCleanNoDelay)
            {
                return;
            }
            this.seaCleanCleanNoDelay = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // :946-951 — flag + save only (the SeaCleanBannerHideFeature.cs field).
        private void OnUguiSeaCleanHideBannerToggled(bool value)
        {
            if (value == this.hideSeaCleanBannerEnabled)
            {
                return;
            }
            this.hideSeaCleanBannerEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // :955-960 — flag + save only (the LittleWhaleFinderFeature.cs field); then an
        // immediate figurine-text seed + relayout so the block it gates reacts this same
        // frame (Extra's Sanrio-toggle idiom).
        private void OnUguiSeaCleanLittleWhaleToggled(bool value)
        {
            if (value == this.littleWhaleFinderEnabled)
            {
                return;
            }
            this.littleWhaleFinderEnabled = value;
            try { this.SaveKeybinds(false); } catch { }

            UguiShellNewFeaturesSeaCleanHandle handle = this.uguiShellNewFeaturesSeaClean;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                if (value)
                {
                    this.SyncUguiSeaCleanFigurineStatus(handle);
                }
                this.RelayoutUguiShellNewFeaturesSeaClean(handle);
            }
            catch { }
        }

        // :984-989 — straight to the teleport (the button only shows while the figurine is
        // present; StartLittleWhaleTeleport carries its own guards, same as the IMGUI twin).
        private void OnUguiSeaCleanLittleWhaleTeleportClicked()
        {
            this.StartLittleWhaleTeleport();
        }

        // :997-1005 — toast AND SaveKeybinds(false), in the source's order (the toast first).
        // This is the saving half of the round's save asymmetry (file header).
        private void OnUguiSeaCleanAutoCleanseToggled(bool value)
        {
            if (value == this.autoCleanseCorruptedEnabled)
            {
                return;
            }
            this.autoCleanseCorruptedEnabled = value;
            this.AddMenuNotification(
                $"Auto-Cleanse Corrupted {(value ? "Enabled" : "Disabled")}",
                value ? UguiSeaCleanOnColor : UguiSeaCleanOffColor);
            try { this.SaveKeybinds(false); } catch { }
        }
    }
}
