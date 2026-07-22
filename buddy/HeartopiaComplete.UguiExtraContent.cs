using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round (migration plan item 12): the EXTRA
    // sub-tab — DrawExtraFeaturesTab (AnimalCareFeature.cs:72-94), newFeaturesSubTab == 5
    // (AnimalCareFeature.cs:54-57 dispatcher). Three source files stack vertically into this ONE
    // sub-tab, in the source's own order:
    //   1. AnimalCareFeature.cs:72-94   — header + the Open Craft Panel button (toast-only
    //      feedback; NO persistent status field exists for it and none is invented here);
    //   2. CarpetStampFeature.cs:466-555 — DrawCarpetStampSection (scan/step controls + the
    //      scan-result list with per-row CONDITIONAL tails);
    //   3. SanrioGachaFinderFeature.cs:848-967 — DrawSanrioGachaSection (toggle-gated block:
    //      hint, daily counter, 3 fixed Star Town rows, the frame-resorted placed-machine list,
    //      overflow + empty-state).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same action methods
    //    (all directly on HeartopiaComplete via the three feature partials; ZERO backend
    //    additions: TryOpenCraftPanel, TryCarpetStampScan/StepOn/StepOff, CarpetStampLog,
    //    StartSanrioGachaTeleport, SaveKeybinds, AddMenuNotification + the fields/consts).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellExtraSubIndex = 5, declared with their siblings in UguiPhase3Content.cs),
    //    never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the drawers, replayed exactly:
    //  - LOCALIZATION SPLIT: part 1 localizes both strings (extra.title / craft.open dot-keys);
    //    part 2 (Carpet Stamp) localizes NOTHING — every header/hint/button/status/row string is
    //    an unlocalized source literal, kept verbatim; part 3 localizes nearly everything via
    //    L/LF, including the leading-spaces key "  ✓ collected today" and the L("live") /
    //    L("map point") fragments. DrawSwitchToggle and DrawSecondaryActionButton L() their
    //    labels internally (UiKitPrimitives.cs:744-763), so the kit checkbox/buttons here get
    //    this.L(...) once at the call site (Sand Sculpture's convention; the source's own
    //    L("Teleport") into DrawSecondaryActionButton double-L's — one L is the intent).
    //  - "Step On Nearest" (CarpetStampFeature.cs:485-511) is a LINEAR FIRST-MATCH over
    //    carpetStampScanResults for HasSkills — NOT nearest-by-distance despite its label. The
    //    quirk is reproduced as-is (including the "nothing steppable" status + toast +
    //    CarpetStampLog branch), NOT fixed: the scan itself may or may not order by distance,
    //    and "fixing" the button silently is a design decision for the user, not a migration's.
    //  - Craft/scan/step feedback: every action writes carpetStampStatus (craft: NO status at
    //    all) and posts AddMenuNotification with the two sections' shared literal palette —
    //    green (0.45,1,0.55) ok / red (1,0.5,0.4) fail — with the source's exact "Carpet scan: "
    //    / "Carpet step: " prefixes.
    //  - CARPET ROW SHAPE: each row is "label + EITHER two small buttons OR a '(scan only)'
    //    label". Built on the EXISTING CreateUguiListRow shape (c) (label + 2 trailing
    //    Secondary-tier 55px buttons — the Garage-rows shape, UguiKit.cs:1473) EXTENDED PER ROW
    //    with one plain "(scan only)" label added into the row root over the buttons' right-
    //    aligned slot; the per-row sync SetActives buttons vs label by HasSkills. The kit
    //    primitive itself is untouched (no new shape variant) — the conditional tail is this
    //    file's own composition, which keeps the shared builder stable for the sibling rounds
    //    porting in parallel. Buttons sit right-aligned (the kit row convention, Teleport
    //    precedent) rather than at the source's fixed x=375/435 — same content, adaptive slot.
    //  - Row CLICK closures capture the SLOT INDEX and read the live list at click time (bounds-
    //    guarded), exactly like the IMGUI buttons act on the entry at that index of the live
    //    list — a pooled row never holds a stale entry copy.
    //  - SANRIO TOGGLE GATE (:868-871 — `if (!enabled) return y + 4`): everything below the
    //    toggle is ONE SanrioDetails container SetActive'd by the relayout — a true hide of the
    //    whole block (hint, counter, scene rows, placed list, overflow, empty-state), not a
    //    skip in a build loop. Toggle change = flag + SaveKeybinds(false) ONLY (:860-865, no
    //    notification), guarded on actual change (kit checkbox build-fire idiom).
    //  - PLACED LIST RESORT (:917-926): the source Clear()s sanrioPlacedSorted, refills it from
    //    the sanrioPlacedMachines dictionary and re-Sorts by squared distance to Camera.main
    //    EVERY DRAWN FRAME while the toggle is on (verified — it is a genuine frame-driven
    //    resort, not change-gated). Reproduced at the same cadence: every gated frame with the
    //    toggle on, the SHARED sorted list (both surfaces one truth — the IMGUI twin refills it
    //    before reading anyway, so cross-surface writes cannot corrupt either) is rebuilt and
    //    re-sorted, then diffed against the pooled rows. The sort comparison is a build-time
    //    cached Comparison reading a camPos field (identical ordering, no per-frame closure
    //    alloc — the one divergence from the IMGUI line, which allocates its lambda per frame).
    //    Sorting only happens when Camera.main exists, same as the source's `if (cam != null)`.
    //  - NO ICONS anywhere in this round (verified — part 3 renders no textures/sprites), and
    //    the Star Town block is a FIXED-COUNT loop over SanrioSceneMachineCount = 3
    //    (SanrioGachaFinderFeature.cs:39, compile-time const) — 3 always-active rows, plain
    //    static build, not a pooled list. Scene machine 302506-08 rows and placed rows share
    //    one row shape: CreateUguiListRow shape (b) (label + one right-aligned 130px Secondary
    //    "Teleport" button; visible only when Present — placed rows' button is unconditional),
    //    label recolored to the source's muted bodyStyle (uiSubTabText @ 0.92, :854-855 — the
    //    kit row label defaults to full text color; SetUguiLabelColor re-applies the source
    //    role).
    //  - Row text recomposition rides per-row VALUE-TUPLE diffs (netId/HasSkills/Distance/label
    //    ref for carpet — Distance is a SCAN-TIME SNAPSHOT stored in the entry, not live;
    //    present/live/doneBit/(int)dist for scene rows; netId/(int)dist/done for placed rows —
    //    (int) truncation means a row recomposes only on whole-meter changes while moving), so
    //    the every-gated-frame read stays allocation-free until something displayed actually
    //    changes. Counters and overflow labels ride int caches the same way.
    //  - WRAPPED PARAGRAPHS (hint :873-876 500x62+66, empty-state :959-965 500x34+38, both
    //    bodyStyle wordWrap): heights measured via the Pictures round's proven
    //    MeasureUguiPicturesWrappedHeight (GetPreferredValues width-constrained flavor, Ceil+4,
    //    sanity gate) with the source rect heights (62/34) as fallbacks; advance = height + 4
    //    (the source's rect+4 cursor step). The spike's build-time caveat applies (built on a
    //    non-active sub-tab, possibly inside an inactive details container): a rejected measure
    //    keeps the fallback and the 0.5s tick retries while the sub-tab is visible AND the
    //    toggle is on (a hidden details block can't awaken its TMP components — retrying while
    //    hidden is pointless; enabling makes the next tick measure for real).
    //
    // Positions replay the source cursor chains verbatim (content top margin 8 standing in for
    // startY, x=8 for the source's uniform left=40; fixed widths 460/360/210/200/130/55 kept,
    // wide 500/520 roles panelW-mapped — the Animal Care convention):
    //   extra header y=8 (460x24 bold 14)                      (+34)
    //   craft button y=42 (200x34 PRIMARY)                     (+42)
    //   carpet header y=84 (460x24 bold 14)                    (+28)
    //   carpet hint y=112 (panelW x20)                         (+24)
    //   Scan y=136 (200x30 PRIMARY) | Step On Nearest x=218 (200x30 Secondary)   (+36)
    //   carpet status y=172 (panelW x20)                       (+26)
    //   carpet rows top y=198 — FIXED (rows h=22, pitch 24, ≤ CarpetStampMaxRowsShown=12)
    //   [overflow +22]  → +8 (section return)  → +14 (the dispatcher's inter-section gap)
    //   sanrio header (460x24)  (+30)  toggle (360x30)  (+36)
    //   [details: hint (panelW x measured, +H+4); counter (+26); 3 scene rows (h=26, pitch 28 —
    //    the source's 22-label/26-button-at-y-4 pair enclosed in one 26px row, same 28 pitch);
    //    placed counter (+26); ≤ SanrioPlacedRowsShown=8 placed rows (pitch 28); [overflow +24];
    //    [empty-state +H+4]; +8 (section return)] | toggle off: +4 (:870)
    //   content height = final cursor + 20 (DrawExtraFeaturesTab:93; DrawNewFeaturesTab adds 0).
    // Everything through the carpet status line is static (built-once positions); the carpet
    // overflow and the whole Sanrio section flow — RelayoutUguiShellNewFeaturesExtra owns those
    // positions and the details SetActive, re-run when the layout signature changes (packed
    // counts/flags: shown carpet rows + overflow, enabled, shown placed rows + overflow, empty-
    // state visibility, scene Present/Live bits + the done mask — per this round's brief — plus
    // the two measured heights).
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Extra
    // sub-tab) — toggle re-sync (WithoutNotify), carpet status raw-reference diff, carpet row
    // tuple diffs, and (toggle on) the placed-list resort + scene/placed row tuple diffs +
    // counter/overflow int diffs, then the layout-signature check. The 0.5s tick carries only
    // the wrapped-paragraph measure retries. Per-frame sync disabled after 3 consecutive errors
    // (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesExtraHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;
            public Color MutedColor;              // the Sanrio bodyStyle color (grow-time rows)

            // -------- Carpet Stamp --------
            public GameObject CarpetStatusLabel;
            public string CarpetStatusRaw;        // raw carpetStampStatus reference last composed
            public readonly List<UguiListRowHandle> CarpetRows = new List<UguiListRowHandle>();
            public readonly List<GameObject> CarpetScanOnlyLabels = new List<GameObject>();
            public readonly List<uint> CarpetRowNetId = new List<uint>();
            public readonly List<bool> CarpetRowHasSkills = new List<bool>();
            public readonly List<float> CarpetRowDist = new List<float>();
            public readonly List<string> CarpetRowLabelRef = new List<string>();
            public GameObject CarpetOverflowLabel;
            public int CarpetOverflowCount = -1;  // -1 = never composed

            // -------- Sanrio Gacha --------
            public GameObject SanrioHeader;
            public Toggle SanrioToggle;
            public GameObject SanrioDetails;      // SetActive gate over the whole below-toggle block
            public GameObject SanrioHintLabel;
            public float SanrioHintH;             // measured (fallback 62 — the source rect)
            public bool SanrioHintMeasureOk;
            public GameObject SanrioCounterLabel;
            public int SanrioCounterShownTotal = -1;
            public readonly UguiListRowHandle[] SanrioSceneRows = new UguiListRowHandle[SanrioSceneMachineCount];
            public readonly int[] SanrioSceneSig = new int[SanrioSceneMachineCount];   // present|live|done bits
            public readonly int[] SanrioSceneDist = new int[SanrioSceneMachineCount];  // (int)dist, sentinel MinValue
            public GameObject SanrioPlacedCounterLabel;
            public int SanrioPlacedCounterShown = -1;
            public readonly List<UguiListRowHandle> SanrioPlacedRows = new List<UguiListRowHandle>();
            public readonly List<uint> SanrioPlacedNetId = new List<uint>();
            public readonly List<int> SanrioPlacedDistInt = new List<int>();
            public readonly List<bool> SanrioPlacedDone = new List<bool>();
            public GameObject SanrioPlacedOverflowLabel;
            public int SanrioPlacedOverflowCount = -1;
            public GameObject SanrioEmptyLabel;
            public float SanrioEmptyH;            // measured (fallback 34 — the source rect)
            public bool SanrioEmptyMeasureOk;

            // Layout signature — the exact values the last relayout used
            public int LayoutPacked = -1;
            public float LayoutHintH = -1f;
            public float LayoutEmptyH = -1f;

            public float NextSlowSyncAt;          // 0.5s tick (measure retries only)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesExtraHandle uguiShellNewFeaturesExtra;

        // Both sections' notification palette — the sources' exact literals
        // (AnimalCareFeature.cs:85, CarpetStampFeature.cs:482/502/509/530/537).
        private static readonly Color UguiExtraOkColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color UguiExtraFailColor = new Color(1f, 0.5f, 0.4f);

        // Carpet rows' fixed region top (everything above it is static — file header cursor).
        private const float UguiExtraCarpetRowsTopY = 198f;

        // Cached sort state for the per-frame placed resort (file header: identical ordering to
        // the source's per-frame lambda, without its per-frame closure allocation).
        private Vector3 uguiExtraSanrioSortCamPos;
        private Comparison<SanrioPlacedMachine> uguiExtraSanrioSortComparison;

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawExtraFeaturesTab: the three source sections stacked in one
        // transparent scroll view (the sources draw no card chrome anywhere — flat labels/
        // buttons/rows, Sand Sculpture's flat-tab precedent). Static chrome positioned here once;
        // the two dynamic list regions + everything below the carpet list belong to the relayout.
        // Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesExtraContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesExtra = null;

            UguiShellNewFeaturesExtraHandle handle = new UguiShellNewFeaturesExtraHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesExtraContent", parent);
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

            // The two text roles this round needs beyond the kit defaults (file header):
            // headers = bold 14 in uiText (all three sections build the same headerStyle), and
            // the Sanrio bodyStyle = 12 wordWrap in uiSubTabText @ 0.92 (:854-855).
            Color headerColor = this.UguiKitTextColor();
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
            handle.MutedColor = mutedColor;

            // ==================== Part 1 — Extra header + Open Craft Panel ====================

            // AnimalCareFeature.cs:77-79 — bold 14 uiText (NOT the kit header color).
            GameObject extraHeader = this.CreateUguiLabel(scrollContent, "ExtraHeader",
                this.L("extra.title"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(extraHeader);
            PlaceUguiTopLeft(extraHeader, 8f, 8f, 460f, 24f);

            // :82-86 — themePrimaryButtonStyle → kit Primary tier; toast-only feedback (file
            // header: no status field exists and none is invented).
            GameObject craftButton = this.CreateUguiPrimaryButton(scrollContent, "OpenCraftButton",
                this.L("craft.open"), new System.Action(this.OnUguiExtraOpenCraftClicked));
            PlaceUguiTopLeft(craftButton, 8f, 42f, 200f, 34f);

            // ==================== Part 2 — Carpet Stamp (UNLOCALIZED literals) ====================

            // CarpetStampFeature.cs:470-472.
            GameObject carpetHeader = this.CreateUguiLabel(scrollContent, "CarpetHeader",
                "Carpet Stamp (Slippery Rug)", 14f, headerColor, false);
            this.TrySetUguiLabelBold(carpetHeader);
            PlaceUguiTopLeft(carpetHeader, 8f, 84f, 460f, 24f);

            // :475 — a plain default GUI.Label → kit body label (the Radar credits mapping).
            GameObject carpetHint = this.CreateUguiBodyLabel(scrollContent, "CarpetHint",
                "Scan party carpets on the map, send a single step-on (server speed buff).", 13f);
            PlaceUguiTopLeft(carpetHint, 8f, 112f, panelW, 20f);

            // :478-483 primary Scan / :485 plain-button Step On Nearest → Secondary tier.
            GameObject scanButton = this.CreateUguiPrimaryButton(scrollContent, "ScanCarpetsButton",
                "Scan Carpets", new System.Action(this.OnUguiExtraCarpetScanClicked));
            PlaceUguiTopLeft(scanButton, 8f, 136f, 200f, 30f);
            GameObject stepNearestButton = this.CreateUguiSecondaryButton(scrollContent, "StepOnNearestButton",
                "Step On Nearest", new System.Action(this.OnUguiExtraCarpetStepOnNearestClicked));
            PlaceUguiTopLeft(stepNearestButton, 218f, 136f, 200f, 30f);

            // :514 — live status line ("Status: " prefix is a source literal).
            handle.CarpetStatusRaw = this.carpetStampStatus;
            handle.CarpetStatusLabel = this.CreateUguiBodyLabel(scrollContent, "CarpetStatus",
                "Status: " + this.carpetStampStatus, 13f);
            PlaceUguiTopLeft(handle.CarpetStatusLabel, 8f, 172f, panelW, 20f);

            // :548-551 — overflow label (position/visibility owned by the relayout).
            handle.CarpetOverflowLabel = this.CreateUguiBodyLabel(scrollContent, "CarpetOverflow", "", 13f);
            handle.CarpetOverflowLabel.SetActive(false);

            // Rows themselves are pooled on demand by SyncUguiExtraCarpetRows (fixed region top —
            // nothing above them ever moves).

            // ==================== Part 3 — Sanrio Gacha Machines ====================

            // :857 — header (positions from here down are owned by the relayout — the carpet
            // list above changes their y).
            handle.SanrioHeader = this.CreateUguiLabel(scrollContent, "SanrioHeader",
                this.L("Sanrio Gacha Machines"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(handle.SanrioHeader);

            // :860-865 — DrawSwitchToggle (L()s internally → one L here); flag + save only.
            handle.SanrioToggle = this.CreateUguiCheckbox(scrollContent, "SanrioFinderToggle",
                this.L("Sanrio Gacha Finder"), this.sanrioGachaFinderEnabled,
                new System.Action<bool>(this.OnUguiExtraSanrioFinderToggled));

            // :868-871 — everything below lives in ONE container the relayout SetActives.
            GameObject details = this.CreateUguiGo("SanrioDetails", scrollContent);
            PlaceUguiTopLeft(details, 0f, 0f, contentWidth, 10f); // children use content coords
            handle.SanrioDetails = details;

            // :873-876 — the wrapped hint paragraph (measured height, fallback = source rect 62).
            handle.SanrioHintLabel = this.CreateUguiLabel(details.transform, "SanrioHint",
                this.L("Finds every SANRIO gacha machine around you and pins it on the game map: the three event machines in Star Town plus machines placed by players in their homes (found while you roam; remembered for the session). Touching each machine drops a capsule reward once per day — up to 5 per day."),
                12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.SanrioHintLabel);

            // :880-883 — bold daily counter (text seeded by the first sync pass).
            handle.SanrioCounterLabel = this.CreateUguiLabel(details.transform, "SanrioCounter",
                "", 12f, mutedColor, false);
            this.TrySetUguiLabelBold(handle.SanrioCounterLabel);

            // :888-914 — the three FIXED Star Town rows (shape (b): label + 130px Secondary
            // Teleport, hidden while !Present; muted label per the source bodyStyle).
            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                int slot = i; // capture a copy for the click closure
                UguiListRowHandle row = this.CreateUguiListRow(details.transform, "SanrioScene" + i,
                    8f, 0f, panelW, 26f,
                    "", null, null, false, true, null,
                    new UguiListRowButtonSpec[]
                    {
                        new UguiListRowButtonSpec
                        {
                            Label = this.L("Teleport"), Tier = UguiListRowTierSecondary,
                            Width = 130f, Enabled = true,
                            OnClick = new System.Action(() => this.OnUguiExtraSanrioSceneTeleportClicked(slot))
                        }
                    });
                this.SetUguiLabelColor(row.Label, mutedColor);
                handle.SanrioSceneRows[i] = row;
                handle.SanrioSceneSig[i] = -1;              // never composed
                handle.SanrioSceneDist[i] = int.MinValue;
            }

            // :928-930 — placed-machines counter.
            handle.SanrioPlacedCounterLabel = this.CreateUguiLabel(details.transform, "SanrioPlacedCounter",
                "", 12f, mutedColor, false);

            // :952-957 — placed overflow (visibility/position owned by the relayout).
            handle.SanrioPlacedOverflowLabel = this.CreateUguiLabel(details.transform, "SanrioPlacedOverflow",
                "", 12f, mutedColor, false);
            handle.SanrioPlacedOverflowLabel.SetActive(false);

            // :959-965 — empty-state paragraph (wrapped; measured, fallback = source rect 34).
            handle.SanrioEmptyLabel = this.CreateUguiLabel(details.transform, "SanrioEmpty",
                this.L("Event machines stand in Star Town (event runs 2026-07-17 – 2026-08-23); player-placed ones are discovered as you roam homes and plazas."),
                12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.SanrioEmptyLabel);
            handle.SanrioEmptyLabel.SetActive(false);

            // Placed rows are pooled on demand by SyncUguiExtraSanrioPlacedRows.

            // Cached placed-sort comparison (file header — the source's :924-925 ordering with a
            // field-read camPos instead of a per-frame closure capture).
            this.uguiExtraSanrioSortComparison = delegate (SanrioPlacedMachine a, SanrioPlacedMachine b)
            {
                return (a.Pos - this.uguiExtraSanrioSortCamPos).sqrMagnitude
                    .CompareTo((b.Pos - this.uguiExtraSanrioSortCamPos).sqrMagnitude);
            };

            // First measurements (may run while this sub-tab — or the details block — is
            // inactive; a rejected measure keeps the source-rect fallback and the slow tick
            // retries once actually visible, the Pictures spike caveat).
            bool ok;
            handle.SanrioHintH = this.MeasureUguiExtraHintHeight(handle, true, out ok);
            handle.SanrioHintMeasureOk = ok;
            handle.SanrioEmptyH = this.MeasureUguiExtraHintHeight(handle, false, out ok);
            handle.SanrioEmptyMeasureOk = ok;

            // Seed pass: rows/counters/status from the live backend state, then the first layout.
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            this.SyncUguiExtraCarpetRows(handle);
            if (this.sanrioGachaFinderEnabled)
            {
                this.RefreshUguiExtraSanrioPlacedSorted(cam, camPos);
                this.SyncUguiExtraSanrioSceneRows(handle, cam, camPos);
                this.SyncUguiExtraSanrioPlacedRows(handle, cam, camPos);
            }
            this.RelayoutUguiShellNewFeaturesExtra(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesExtra = handle;
            return block;
        }

        // Measurement wrapper for the two wrapped Sanrio paragraphs — reuses the Pictures
        // round's MeasureUguiPicturesWrappedHeight (same class, the migration's one proven
        // GetPreferredValues path) with this round's texts/fallbacks. hint=true → the big hint
        // (fallback 62); false → the empty-state (fallback 34). Texts re-read via L so a
        // language change after a failed first measure re-measures the CURRENT string.
        private float MeasureUguiExtraHintHeight(UguiShellNewFeaturesExtraHandle handle, bool hint, out bool ok)
        {
            if (hint)
            {
                return this.MeasureUguiPicturesWrappedHeight(handle.SanrioHintLabel,
                    this.L("Finds every SANRIO gacha machine around you and pins it on the game map: the three event machines in Star Town plus machines placed by players in their homes (found while you roam; remembered for the session). Touching each machine drops a capsule reward once per day — up to 5 per day."),
                    handle.PanelW, handle.SanrioHintH > 0f ? handle.SanrioHintH : 62f, out ok);
            }
            return this.MeasureUguiPicturesWrappedHeight(handle.SanrioEmptyLabel,
                this.L("Event machines stand in Star Town (event runs 2026-07-17 – 2026-08-23); player-placed ones are discovered as you roam homes and plazas."),
                handle.PanelW, handle.SanrioEmptyH > 0f ? handle.SanrioEmptyH : 34f, out ok);
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the flowing part of the source cursor (everything from the carpet
        // overflow down; the region above the carpet rows is static), SetActives the details
        // block, and stores the signature values it laid out with.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellNewFeaturesExtra(UguiShellNewFeaturesExtraHandle handle)
        {
            float panelW = handle.PanelW;

            // Carpet rows occupy the fixed region; overflow + everything below flow.
            int carpetTotal = this.carpetStampScanResults.Count;
            int carpetShown = Math.Min(carpetTotal, CarpetStampMaxRowsShown);
            bool carpetOverflow = carpetTotal > carpetShown;
            float yCur = UguiExtraCarpetRowsTopY + carpetShown * 24f;
            SetUguiGoActive(handle.CarpetOverflowLabel, carpetOverflow);
            if (carpetOverflow)
            {
                PlaceUguiTopLeft(handle.CarpetOverflowLabel, 8f, yCur, 460f, 20f);
                yCur += 22f;
            }
            yCur += 8f;   // DrawCarpetStampSection:554 return y + 8
            yCur += 14f;  // DrawExtraFeaturesTab:91 — DrawSanrioGachaSection(y + 14)

            // Sanrio header + toggle (:857-866).
            PlaceUguiTopLeft(handle.SanrioHeader, 8f, yCur, 460f, 24f);
            PlaceUguiTopLeft(handle.SanrioToggle.gameObject, 8f, yCur + 30f, 360f, 30f);
            float sy = yCur + 66f; // +30 header advance, +36 toggle advance

            bool enabled = this.sanrioGachaFinderEnabled;
            SetUguiGoActive(handle.SanrioDetails, enabled);
            if (!enabled)
            {
                // :870 return y + 4; DrawExtraFeaturesTab:93 return y + 20.
                this.SetUguiScrollContentHeight(handle.ScrollContent, sy + 4f + 20f);
                handle.LayoutPacked = this.ComputeUguiExtraLayoutPacked();
                handle.LayoutHintH = handle.SanrioHintH;
                handle.LayoutEmptyH = handle.SanrioEmptyH;
                return;
            }

            // Details children (content coords inside the full-size container).
            PlaceUguiTopLeft(handle.SanrioHintLabel, 8f, sy, panelW, handle.SanrioHintH);
            sy += handle.SanrioHintH + 4f;            // :876 rect 62 + advance 66

            PlaceUguiTopLeft(handle.SanrioCounterLabel, 8f, sy, panelW, 22f);
            sy += 26f;                                 // :883

            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                PlaceUguiTopLeft(handle.SanrioSceneRows[i].Root, 8f, sy, panelW, 26f);
                sy += 28f;                             // :913
            }

            PlaceUguiTopLeft(handle.SanrioPlacedCounterLabel, 8f, sy, panelW, 22f);
            sy += 26f;                                 // :930

            int placedTotal = this.sanrioPlacedSorted.Count;
            int placedShown = Math.Min(placedTotal, SanrioPlacedRowsShown);
            for (int i = 0; i < placedShown && i < handle.SanrioPlacedRows.Count; i++)
            {
                PlaceUguiTopLeft(handle.SanrioPlacedRows[i].Root, 8f, sy, panelW, 26f);
                sy += 28f;                             // :950
            }

            bool placedOverflow = placedTotal > placedShown;
            SetUguiGoActive(handle.SanrioPlacedOverflowLabel, placedOverflow);
            if (placedOverflow)
            {
                PlaceUguiTopLeft(handle.SanrioPlacedOverflowLabel, 8f, sy, 460f, 20f);
                sy += 24f;                             // :956
            }

            bool emptyVisible = this.sanrioLocatedCount == 0 && placedTotal == 0;   // :959
            SetUguiGoActive(handle.SanrioEmptyLabel, emptyVisible);
            if (emptyVisible)
            {
                PlaceUguiTopLeft(handle.SanrioEmptyLabel, 8f, sy, panelW, handle.SanrioEmptyH);
                sy += handle.SanrioEmptyH + 4f;        // :964 rect 34 + advance 38
            }

            sy += 8f;    // :967 return y + 8
            this.SetUguiScrollContentHeight(handle.ScrollContent, sy + 20f); // DrawExtraFeaturesTab:93

            handle.LayoutPacked = this.ComputeUguiExtraLayoutPacked();
            handle.LayoutHintH = handle.SanrioHintH;
            handle.LayoutEmptyH = handle.SanrioEmptyH;
        }

        // Packed layout drivers (file header): shown carpet rows + overflow, enabled, shown
        // placed rows + overflow, empty-state visibility, scene Present/Live bits + done mask.
        // Sanrio components read as 0 while the toggle is off — the hidden block can't drive
        // layout, and sanrioPlacedSorted is deliberately NOT read while disabled (the source
        // doesn't refresh it then either).
        private int ComputeUguiExtraLayoutPacked()
        {
            int carpetTotal = this.carpetStampScanResults.Count;
            int carpetShown = Math.Min(carpetTotal, CarpetStampMaxRowsShown);
            int packed = carpetShown
                | ((carpetTotal > carpetShown) ? 1 : 0) << 4
                | (this.sanrioGachaFinderEnabled ? 1 : 0) << 5;
            if (this.sanrioGachaFinderEnabled)
            {
                int placedTotal = this.sanrioPlacedSorted.Count;
                int placedShown = Math.Min(placedTotal, SanrioPlacedRowsShown);
                packed |= placedShown << 6
                    | ((placedTotal > placedShown) ? 1 : 0) << 10
                    | ((this.sanrioLocatedCount == 0 && placedTotal == 0) ? 1 : 0) << 11
                    | (this.sanrioDropSceneDoneMask & 7) << 15;
                for (int i = 0; i < SanrioSceneMachineCount; i++)
                {
                    if (this.sanrioMachines[i].Present)
                    {
                        packed |= 1 << (12 + i);
                    }
                    if (this.sanrioMachines[i].Live)
                    {
                        packed |= 1 << (18 + i);
                    }
                }
            }
            return packed;
        }

        // ----------------------------------------------------------------------------------------
        // Carpet row pool — CreateUguiListRow shape (c) + the per-row "(scan only)" extension
        // label (file header). Rows are index-stable at the fixed region top; clicks read the
        // live list by slot index.
        // ----------------------------------------------------------------------------------------

        private void SyncUguiExtraCarpetRows(UguiShellNewFeaturesExtraHandle handle)
        {
            List<CarpetStampEntry> list = this.carpetStampScanResults;
            int total = list.Count;
            int shown = Math.Min(total, CarpetStampMaxRowsShown);

            for (int i = 0; i < shown; i++)
            {
                if (i >= handle.CarpetRows.Count)
                {
                    // Grow the pool: shape (c) row + the "(scan only)" tail label over the
                    // buttons' right-aligned slot (2x55 + gap = 116 from the right edge).
                    int slot = i; // capture a copy for the click closures
                    UguiListRowHandle row = this.CreateUguiListRow(handle.ScrollContent, "Carpet" + i,
                        8f, UguiExtraCarpetRowsTopY + i * 24f, handle.PanelW, 22f,
                        "", null, null, false, true, null,
                        new UguiListRowButtonSpec[]
                        {
                            new UguiListRowButtonSpec
                            {
                                Label = "On", Tier = UguiListRowTierSecondary, Width = 55f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiExtraCarpetRowStepClicked(slot, true))
                            },
                            new UguiListRowButtonSpec
                            {
                                Label = "Off", Tier = UguiListRowTierSecondary, Width = 55f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiExtraCarpetRowStepClicked(slot, false))
                            }
                        });
                    GameObject scanOnly = this.CreateUguiBodyLabel(row.Root.transform, "ScanOnly",
                        "(scan only)", 12f);
                    PlaceUguiTopLeft(scanOnly, handle.PanelW - 116f, 0f, 116f, 22f);
                    scanOnly.SetActive(false);
                    handle.CarpetRows.Add(row);
                    handle.CarpetScanOnlyLabels.Add(scanOnly);
                    handle.CarpetRowNetId.Add(0U);
                    handle.CarpetRowHasSkills.Add(false);
                    handle.CarpetRowDist.Add(float.NegativeInfinity); // sentinel → first compose
                    handle.CarpetRowLabelRef.Add(null);
                }

                UguiListRowHandle pooled = handle.CarpetRows[i];
                if (pooled.Root != null && !pooled.Root.activeSelf)
                {
                    pooled.Root.SetActive(true);
                }

                CarpetStampEntry entry = list[i];
                bool changed = handle.CarpetRowNetId[i] != entry.NetId
                    || handle.CarpetRowHasSkills[i] != entry.HasSkills
                    || handle.CarpetRowDist[i] != entry.Distance
                    || !ReferenceEquals(handle.CarpetRowLabelRef[i], entry.Label);
                if (changed)
                {
                    handle.CarpetRowNetId[i] = entry.NetId;
                    handle.CarpetRowHasSkills[i] = entry.HasSkills;
                    handle.CarpetRowDist[i] = entry.Distance;
                    handle.CarpetRowLabelRef[i] = entry.Label;

                    // :521-522 — the exact composition (F1 meters or "?").
                    string distText = entry.Distance >= 0f ? entry.Distance.ToString("F1") + "m" : "?";
                    this.SetUguiLabelText(pooled.Label, entry.Label + "  net=" + entry.NetId + "  " + distText);

                    // :524-543 — the conditional tail: two buttons OR the "(scan only)" label.
                    SetUguiGoActive(pooled.Buttons.Count > 0 ? pooled.Buttons[0] : null, entry.HasSkills);
                    SetUguiGoActive(pooled.Buttons.Count > 1 ? pooled.Buttons[1] : null, entry.HasSkills);
                    SetUguiGoActive(handle.CarpetScanOnlyLabels[i], !entry.HasSkills);
                }
            }

            for (int i = shown; i < handle.CarpetRows.Count; i++)
            {
                GameObject root = handle.CarpetRows[i].Root;
                if (root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }
            }

            // :548-551 — overflow text, recomposed only when the hidden count changes.
            int over = total - shown;
            if (over != handle.CarpetOverflowCount)
            {
                handle.CarpetOverflowCount = over;
                if (over > 0)
                {
                    this.SetUguiLabelText(handle.CarpetOverflowLabel,
                        "...and " + over + " more (see log).");
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Sanrio syncs — the shared-list resort (source cadence) + tuple-diffed row/counter text
        // ----------------------------------------------------------------------------------------

        // :917-926 verbatim semantics over the SHARED sorted list (file header): clear, refill
        // from the live dictionary, sort by squared camera distance — every gated frame while
        // the toggle is on; sorting skipped (order = dictionary enumeration) when no camera,
        // exactly like the source's `if (cam != null)`.
        private void RefreshUguiExtraSanrioPlacedSorted(Camera cam, Vector3 camPos)
        {
            this.sanrioPlacedSorted.Clear();
            foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
            {
                this.sanrioPlacedSorted.Add(kv.Value);
            }
            if (cam != null && this.uguiExtraSanrioSortComparison != null)
            {
                this.uguiExtraSanrioSortCamPos = camPos;
                this.sanrioPlacedSorted.Sort(this.uguiExtraSanrioSortComparison);
            }
        }

        // :880-914 — the daily counter + the three fixed Star Town rows. Row text recomposes on
        // (present|live|done, (int)dist) tuple change; the Teleport button shows only while
        // Present (its visibility flips inside the same changed branch).
        private void SyncUguiExtraSanrioSceneRows(UguiShellNewFeaturesExtraHandle handle, Camera cam, Vector3 camPos)
        {
            // :881-882 — "Capsule drops today (tracked): {0}/{1}" (int-cached).
            if (handle.SanrioCounterShownTotal != this.sanrioDropTotalToday)
            {
                handle.SanrioCounterShownTotal = this.sanrioDropTotalToday;
                this.SetUguiLabelText(handle.SanrioCounterLabel,
                    this.LF("Capsule drops today (tracked): {0}/{1}", this.sanrioDropTotalToday, SanrioDropDailyCap));
            }

            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                bool present = this.sanrioMachines[i].Present;
                bool live = this.sanrioMachines[i].Live;
                bool done = (this.sanrioDropSceneDoneMask & (1 << i)) != 0;
                int distInt = int.MinValue;
                if (present && cam != null)
                {
                    distInt = (int)Vector3.Distance(camPos, this.sanrioMachines[i].Pos); // :893/:896 (int) cast
                }
                else if (present)
                {
                    distInt = -1; // located, no camera → the no-meters variant (:897)
                }

                int sig = (present ? 1 : 0) | (live ? 2 : 0) | (done ? 4 : 0);
                if (sig == handle.SanrioSceneSig[i] && distInt == handle.SanrioSceneDist[i])
                {
                    continue;
                }
                handle.SanrioSceneSig[i] = sig;
                handle.SanrioSceneDist[i] = distInt;

                // :890-906 — the exact three-way text + the collected suffix.
                string text;
                if (present)
                {
                    string src = live ? this.L("live") : this.L("map point");
                    text = distInt >= 0
                        ? this.LF("Star Town machine {0}: located ({1}) — {2}m", i + 1, src, distInt)
                        : this.LF("Star Town machine {0}: located ({1})", i + 1, src);
                }
                else
                {
                    text = this.LF("Star Town machine {0}: not found", i + 1);
                }
                if (done)
                {
                    text += this.L("  ✓ collected today");
                }
                this.SetUguiLabelText(handle.SanrioSceneRows[i].Label, text);

                // :908-909 — the Teleport button only exists while Present.
                SetUguiGoActive(handle.SanrioSceneRows[i].Buttons.Count > 0
                    ? handle.SanrioSceneRows[i].Buttons[0] : null, present);
            }
        }

        // :928-957 — placed counter + pooled rows over the freshly-resorted shared list +
        // overflow. Row text recomposes on (netId, (int)dist, done) tuple change.
        private void SyncUguiExtraSanrioPlacedRows(UguiShellNewFeaturesExtraHandle handle, Camera cam, Vector3 camPos)
        {
            List<SanrioPlacedMachine> sorted = this.sanrioPlacedSorted;
            int total = sorted.Count;
            int shown = Math.Min(total, SanrioPlacedRowsShown);

            if (handle.SanrioPlacedCounterShown != total)
            {
                handle.SanrioPlacedCounterShown = total;
                this.SetUguiLabelText(handle.SanrioPlacedCounterLabel,
                    this.LF("Placed machines found this session: {0}", total));
            }

            for (int i = 0; i < shown; i++)
            {
                if (i >= handle.SanrioPlacedRows.Count)
                {
                    // Grow the pool: same shape (b) as the Star Town rows (file header); the
                    // relayout positions it this same frame (row-count growth changes the
                    // packed signature).
                    int slot = i; // capture a copy for the click closure
                    UguiListRowHandle row = this.CreateUguiListRow(handle.SanrioDetails.transform,
                        "SanrioPlaced" + i, 8f, 0f, handle.PanelW, 26f,
                        "", null, null, false, true, null,
                        new UguiListRowButtonSpec[]
                        {
                            new UguiListRowButtonSpec
                            {
                                Label = this.L("Teleport"), Tier = UguiListRowTierSecondary,
                                Width = 130f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiExtraSanrioPlacedTeleportClicked(slot))
                            }
                        });
                    this.SetUguiLabelColor(row.Label, handle.MutedColor);
                    handle.SanrioPlacedRows.Add(row);
                    handle.SanrioPlacedNetId.Add(0U);
                    handle.SanrioPlacedDistInt.Add(int.MinValue); // sentinel → first compose
                    handle.SanrioPlacedDone.Add(false);
                }

                UguiListRowHandle pooled = handle.SanrioPlacedRows[i];
                if (pooled.Root != null && !pooled.Root.activeSelf)
                {
                    pooled.Root.SetActive(true);
                }

                SanrioPlacedMachine placed = sorted[i];
                int distInt = cam != null ? (int)Vector3.Distance(camPos, placed.Pos) : -1; // :936-938 (int) cast
                if (handle.SanrioPlacedNetId[i] == placed.NetId
                    && handle.SanrioPlacedDistInt[i] == distInt
                    && handle.SanrioPlacedDone[i] == placed.DoneToday)
                {
                    continue;
                }
                handle.SanrioPlacedNetId[i] = placed.NetId;
                handle.SanrioPlacedDistInt[i] = distInt;
                handle.SanrioPlacedDone[i] = placed.DoneToday;

                // :937-944 — the exact composition (meters variant, net suffix, done suffix).
                string text = distInt >= 0
                    ? this.LF("Placed machine: {0}m", distInt)
                    : this.L("Placed machine");
                text += "  (net=" + placed.NetId + ")";
                if (placed.DoneToday)
                {
                    text += this.L("  ✓ collected today");
                }
                this.SetUguiLabelText(pooled.Label, text);
            }

            for (int i = shown; i < handle.SanrioPlacedRows.Count; i++)
            {
                GameObject root = handle.SanrioPlacedRows[i].Root;
                if (root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }
            }

            // :952-956 — overflow text (int-cached).
            int over = total - shown;
            if (over != handle.SanrioPlacedOverflowCount)
            {
                handle.SanrioPlacedOverflowCount = over;
                if (over > 0)
                {
                    this.SetUguiLabelText(handle.SanrioPlacedOverflowLabel,
                        this.LF("...and {0} more (all pinned on the map).", over));
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesExtraOnUpdate()
        {
            UguiShellNewFeaturesExtraHandle handle = this.uguiShellNewFeaturesExtra;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellExtraSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-sync (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.SanrioToggle, this.sanrioGachaFinderEnabled);

                // Carpet status — raw-reference diff, composes only on an actual change.
                if (!ReferenceEquals(handle.CarpetStatusRaw, this.carpetStampStatus))
                {
                    handle.CarpetStatusRaw = this.carpetStampStatus;
                    this.SetUguiLabelText(handle.CarpetStatusLabel, "Status: " + this.carpetStampStatus);
                }

                // Carpet rows — fresh read of the live scan list every gated frame (tuple diffs
                // keep the idle path allocation-free; the list itself only mutates on scans,
                // from EITHER surface).
                this.SyncUguiExtraCarpetRows(handle);

                // Sanrio block — only while the toggle is on (the source early-returns before
                // any of this at :868-871; the resort must not run while disabled).
                if (this.sanrioGachaFinderEnabled)
                {
                    Camera cam = Camera.main;                                   // :885-886
                    Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                    this.RefreshUguiExtraSanrioPlacedSorted(cam, camPos);       // EVERY gated frame (file header)
                    this.SyncUguiExtraSanrioSceneRows(handle, cam, camPos);
                    this.SyncUguiExtraSanrioPlacedRows(handle, cam, camPos);
                }

                // 0.5s tick — wrapped-paragraph measure retries (spike caveat; only useful
                // while the details block is actually active, see file header).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    if (this.sanrioGachaFinderEnabled)
                    {
                        if (!handle.SanrioHintMeasureOk)
                        {
                            bool ok;
                            handle.SanrioHintH = this.MeasureUguiExtraHintHeight(handle, true, out ok);
                            handle.SanrioHintMeasureOk = ok;
                        }
                        if (!handle.SanrioEmptyMeasureOk)
                        {
                            bool ok;
                            handle.SanrioEmptyH = this.MeasureUguiExtraHintHeight(handle, false, out ok);
                            handle.SanrioEmptyMeasureOk = ok;
                        }
                    }
                }

                // Layout signature — packed counts/flags + the two measured heights.
                if (handle.LayoutPacked != this.ComputeUguiExtraLayoutPacked()
                    || handle.LayoutHintH != handle.SanrioHintH
                    || handle.LayoutEmptyH != handle.SanrioEmptyH)
                {
                    this.RelayoutUguiShellNewFeaturesExtra(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/Extra content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order)
        // ----------------------------------------------------------------------------------------

        // AnimalCareFeature.cs:82-86 — TryOpenCraftPanel + a pure toast (green ok / red fail);
        // deliberately NO status field (file header).
        private void OnUguiExtraOpenCraftClicked()
        {
            bool ok = this.TryOpenCraftPanel(out string status);
            this.AddMenuNotification(status, ok ? UguiExtraOkColor : UguiExtraFailColor);
        }

        // CarpetStampFeature.cs:478-483 — scan, status write, prefixed toast; then an immediate
        // UGUI refresh so the result list shows this same frame (Teleport click precedent).
        private void OnUguiExtraCarpetScanClicked()
        {
            bool ok = this.TryCarpetStampScan(out string scanStatus);
            this.carpetStampStatus = scanStatus;
            this.AddMenuNotification("Carpet scan: " + scanStatus, ok ? UguiExtraOkColor : UguiExtraFailColor);

            UguiShellNewFeaturesExtraHandle handle = this.uguiShellNewFeaturesExtra;
            if (handle != null && handle.Root != null)
            {
                this.SyncUguiExtraCarpetRows(handle);
                if (handle.LayoutPacked != this.ComputeUguiExtraLayoutPacked())
                {
                    this.RelayoutUguiShellNewFeaturesExtra(handle);
                }
            }
        }

        // :485-511 — the LINEAR FIRST-MATCH walk (deliberately NOT nearest-by-distance despite
        // the label — file header), including the "nothing steppable" status + toast + log.
        private void OnUguiExtraCarpetStepOnNearestClicked()
        {
            CarpetStampEntry nearest = default;
            bool found = false;
            for (int i = 0; i < this.carpetStampScanResults.Count; i++)
            {
                if (this.carpetStampScanResults[i].HasSkills)
                {
                    nearest = this.carpetStampScanResults[i];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                this.carpetStampStatus = "No steppable carpet in the last scan.";
                this.AddMenuNotification(this.carpetStampStatus, UguiExtraFailColor);
                CarpetStampLog("Step On Nearest: nothing steppable in snapshot (scan first).");
            }
            else
            {
                bool ok = this.TryCarpetStampStepOn(nearest, out string stepStatus);
                this.carpetStampStatus = stepStatus;
                this.AddMenuNotification("Carpet step: " + stepStatus, ok ? UguiExtraOkColor : UguiExtraFailColor);
            }
        }

        // :526-538 — the per-row On/Off pair, reading the LIVE list at the clicked slot (bounds-
        // guarded against a same-frame rescan shrinking the list; the buttons only render on
        // HasSkills rows, and TryCarpetStampStepOn/Off carry their own no-skills guard anyway).
        private void OnUguiExtraCarpetRowStepClicked(int index, bool stepOn)
        {
            if (index < 0 || index >= this.carpetStampScanResults.Count)
            {
                return;
            }
            CarpetStampEntry entry = this.carpetStampScanResults[index];
            bool ok;
            string stepStatus;
            if (stepOn)
            {
                ok = this.TryCarpetStampStepOn(entry, out stepStatus);
            }
            else
            {
                ok = this.TryCarpetStampStepOff(entry, out stepStatus);
            }
            this.carpetStampStatus = stepStatus;
            this.AddMenuNotification("Carpet step: " + stepStatus, ok ? UguiExtraOkColor : UguiExtraFailColor);
        }

        // SanrioGachaFinderFeature.cs:860-865 — flag + SaveKeybinds(false) ONLY (no
        // notification), guarded on actual change (kit checkbox build-fire idiom); then an
        // immediate show/hide + relayout so the block reacts this same frame.
        private void OnUguiExtraSanrioFinderToggled(bool value)
        {
            if (value == this.sanrioGachaFinderEnabled)
            {
                return;
            }
            this.sanrioGachaFinderEnabled = value;
            try { this.SaveKeybinds(false); } catch { }

            UguiShellNewFeaturesExtraHandle handle = this.uguiShellNewFeaturesExtra;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                if (value)
                {
                    Camera cam = Camera.main;
                    Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                    this.RefreshUguiExtraSanrioPlacedSorted(cam, camPos);
                    this.SyncUguiExtraSanrioSceneRows(handle, cam, camPos);
                    this.SyncUguiExtraSanrioPlacedRows(handle, cam, camPos);
                }
                this.RelayoutUguiShellNewFeaturesExtra(handle);
            }
            catch { }
        }

        // :908-911 — Star Town teleport (Present-guarded, like the button's own existence).
        private void OnUguiExtraSanrioSceneTeleportClicked(int index)
        {
            if (index < 0 || index >= SanrioSceneMachineCount || !this.sanrioMachines[index].Present)
            {
                return;
            }
            this.StartSanrioGachaTeleport(this.sanrioMachines[index].Pos,
                this.LF("Star Town machine {0}", index + 1));
        }

        // :946-949 — placed-machine teleport, reading the live sorted list at the clicked slot.
        private void OnUguiExtraSanrioPlacedTeleportClicked(int index)
        {
            if (index < 0 || index >= this.sanrioPlacedSorted.Count)
            {
                return;
            }
            this.StartSanrioGachaTeleport(this.sanrioPlacedSorted[index].Pos, this.L("placed machine"));
        }
    }
}
