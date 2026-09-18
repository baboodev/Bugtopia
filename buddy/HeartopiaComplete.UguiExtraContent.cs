using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round (migration plan item 12): the EXTRA
    // sub-tab — DrawExtraFeaturesTab (AnimalCareFeature.cs:72-94), newFeaturesSubTab == 5
    // (AnimalCareFeature.cs:54-57 dispatcher). Three sections stack vertically into this ONE
    // sub-tab:
    //   1. AnimalCareFeature.cs:72-94   — header + the Open Craft Panel button (toast-only
    //      feedback; NO persistent status field exists for it and none is invented here);
    //   2. ClearMissedCallsFeature.cs   — Missed Calls. The one section with NO IMGUI ancestor:
    //      the feature postdates the migration, so it is authored here rather than mirrored.
    //      Header / hint / one primary button / status line — Carpet Stamp's shape, deliberately,
    //      and fixed-height, so it costs the relayout nothing;
    //   3. CarpetStampFeature.cs:466-555 — DrawCarpetStampSection (scan/step controls + the
    //      scan-result list with per-row CONDITIONAL tails).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same action methods
    //    (all directly on HeartopiaComplete via the three feature partials; ZERO backend
    //    additions: TryOpenCraftPanel, TryCarpetStampScan/StepOn/StepOff, CarpetStampLog,
    //    AddMenuNotification + the fields/consts).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellExtraSubIndex = 5, declared with their siblings in UguiShellTabIndices.cs),
    //    never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the drawers, replayed exactly:
    //  - LOCALIZATION SPLIT: part 1 localizes both strings (extra.title / craft.open dot-keys);
    //    part 2 (Carpet Stamp) localizes NOTHING — every header/hint/button/status/row string is
    //    an unlocalized source literal, kept verbatim. DrawSecondaryActionButton L()s its
    //    label internally (UiKitPrimitives.cs:744-763), so the kit buttons here get this.L(...)
    //    once at the call site (Sand Sculpture's convention).
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
    //
    // Positions replay the source cursor chains verbatim (content top margin 8 standing in for
    // startY, x=8 for the source's uniform left=40; fixed widths 460/360/210/200/130/55 kept,
    // wide 500/520 roles panelW-mapped — the Animal Care convention):
    //   extra header y=8 (460x24 bold 14)                      (+34)
    //   craft button y=42 (200x34 PRIMARY)                     (+42)
    //   missed-calls header y=84 (460x24 bold 14)              (+28)
    //   missed-calls hint y=112 (panelW x20)                   (+24)
    //   Clear Missed Calls y=136 (200x30 PRIMARY)              (+36)
    //   missed-calls status y=172 (panelW x20)                 (+26)
    //   carpet header y=198 (460x24 bold 14)                   (+28)
    //   carpet hint y=226 (panelW x20)                         (+24)
    //   Scan y=250 (200x30 PRIMARY) | Step On Nearest x=218 (200x30 Secondary)   (+36)
    //   carpet status y=286 (panelW x20)                       (+26)
    //   carpet rows top y=312 — FIXED (rows h=22, pitch 24, ≤ CarpetStampMaxRowsShown=12)
    //   [overflow +22]  → +8 (section return)
    //   content height = final cursor + 20 (DrawExtraFeaturesTab:93; DrawNewFeaturesTab adds 0).
    // Everything through the carpet status line is static (built-once positions); the carpet
    // overflow and the content height flow — RelayoutUguiShellNewFeaturesExtra owns those, re-run
    // when the layout signature (shown carpet rows + overflow) changes.
    //
    // Cross-surface sync cadence: every gated frame (shell visible + New Features tab + Extra
    // sub-tab) — carpet status raw-reference diff, carpet row tuple diffs, then the layout-
    // signature check. The 0.5s tick carries the missed-calls status re-check. Per-frame sync
    // disabled after 3 consecutive errors (LIVE rail idiom).
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

            // -------- Missed Calls --------
            public GameObject MissedCallsStatusLabel;
            public string MissedCallsStatusShown;  // composed label text last written

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

            // Layout signature — the exact values the last relayout used
            public int LayoutPacked = -1;

            public float NextSlowSyncAt;          // 0.5s tick (missed-calls status)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesExtraHandle uguiShellNewFeaturesExtra;

        // Both sections' notification palette — the sources' exact literals
        // (AnimalCareFeature.cs:85, CarpetStampFeature.cs:482/502/509/530/537).
        private static readonly Color UguiExtraOkColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color UguiExtraFailColor = new Color(1f, 0.5f, 0.4f);

        // Carpet rows' fixed region top (everything above it is static — file header cursor).
        // 198 before the Missed Calls section was inserted above it; that block is fixed-height,
        // so the only layout consequence is this constant and the four carpet chrome positions.
        private const float UguiExtraCarpetRowsTopY = 312f;

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

            // Headers = bold 14 in uiText (every section builds the same headerStyle).
            Color headerColor = this.UguiKitTextColor();

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

            // ==================== Part 2 — Missed Calls ====================
            // ClearMissedCallsFeature.cs — this round's only NEW section (no IMGUI ancestor: the
            // feature postdates the migration). Shape copied from Carpet Stamp below it —
            // header / hint / one primary button / status line — so the tab reads as one thing.
            // Everything is static: the section's height never changes, which is why the carpet
            // block's fixed cursor simply starts 114px lower and the relayout is untouched.

            GameObject missedHeader = this.CreateUguiLabel(scrollContent, "MissedCallsHeader",
                this.L("Missed Calls"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(missedHeader);
            PlaceUguiTopLeft(missedHeader, 8f, 84f, 460f, 24f);

            GameObject missedHint = this.CreateUguiBodyLabel(scrollContent, "MissedCallsHint",
                this.L("Empty the missed-call list on your watch: invites are removed, quest calls only lose their red dot."), 13f);
            PlaceUguiTopLeft(missedHint, 8f, 112f, panelW, 20f);

            GameObject clearMissedButton = this.CreateUguiPrimaryButton(scrollContent, "ClearMissedCallsButton",
                this.L("Clear Missed Calls"), new System.Action(this.OnUguiExtraClearMissedCallsClicked));
            PlaceUguiTopLeft(clearMissedButton, 8f, 136f, 200f, 30f);

            handle.MissedCallsStatusShown = this.LF("Status: {0}", this.GetClearMissedCallsStatus());
            handle.MissedCallsStatusLabel = this.CreateUguiBodyLabel(scrollContent, "MissedCallsStatus",
                handle.MissedCallsStatusShown, 13f);
            PlaceUguiTopLeft(handle.MissedCallsStatusLabel, 8f, 172f, panelW, 20f);

            // ==================== Part 3 — Carpet Stamp ====================

            // CarpetStampFeature.cs:470-472.
            GameObject carpetHeader = this.CreateUguiLabel(scrollContent, "CarpetHeader",
                this.L("Carpet Stamp (Slippery Rug)"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(carpetHeader);
            PlaceUguiTopLeft(carpetHeader, 8f, 198f, 460f, 24f);

            // :475 — a plain default GUI.Label → kit body label (the Radar credits mapping).
            GameObject carpetHint = this.CreateUguiBodyLabel(scrollContent, "CarpetHint",
                this.L("Scan party carpets on the map, send a single step-on (server speed buff)."), 13f);
            PlaceUguiTopLeft(carpetHint, 8f, 226f, panelW, 20f);

            // :478-483 primary Scan / :485 plain-button Step On Nearest → Secondary tier.
            GameObject scanButton = this.CreateUguiPrimaryButton(scrollContent, "ScanCarpetsButton",
                this.L("Scan Carpets"), new System.Action(this.OnUguiExtraCarpetScanClicked));
            PlaceUguiTopLeft(scanButton, 8f, 250f, 200f, 30f);
            GameObject stepNearestButton = this.CreateUguiSecondaryButton(scrollContent, "StepOnNearestButton",
                this.L("Step On Nearest"), new System.Action(this.OnUguiExtraCarpetStepOnNearestClicked));
            PlaceUguiTopLeft(stepNearestButton, 218f, 250f, 200f, 30f);

            // :514 — live status line ("Status: " prefix is a source literal).
            handle.CarpetStatusRaw = this.carpetStampStatus;
            handle.CarpetStatusLabel = this.CreateUguiBodyLabel(scrollContent, "CarpetStatus",
                "Status: " + this.carpetStampStatus, 13f);
            PlaceUguiTopLeft(handle.CarpetStatusLabel, 8f, 286f, panelW, 20f);

            // :548-551 — overflow label (position/visibility owned by the relayout).
            handle.CarpetOverflowLabel = this.CreateUguiBodyLabel(scrollContent, "CarpetOverflow", "", 13f);
            handle.CarpetOverflowLabel.SetActive(false);

            // Rows themselves are pooled on demand by SyncUguiExtraCarpetRows (fixed region top —
            // nothing above them ever moves).

            // Seed pass: rows from the live backend state, then the first layout.
            this.SyncUguiExtraCarpetRows(handle);
            this.RelayoutUguiShellNewFeaturesExtra(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesExtra = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the flowing part of the source cursor (the carpet overflow and the
        // content height; the region above the carpet rows is static) and stores the signature
        // value it laid out with.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellNewFeaturesExtra(UguiShellNewFeaturesExtraHandle handle)
        {
            // Carpet rows occupy the fixed region; the overflow line flows below them.
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
            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f); // DrawExtraFeaturesTab:93

            handle.LayoutPacked = this.ComputeUguiExtraLayoutPacked();
        }

        // Packed layout drivers: shown carpet rows + overflow.
        private int ComputeUguiExtraLayoutPacked()
        {
            int carpetTotal = this.carpetStampScanResults.Count;
            int carpetShown = Math.Min(carpetTotal, CarpetStampMaxRowsShown);
            return carpetShown | ((carpetTotal > carpetShown) ? 1 : 0) << 4;
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
                        this.L("(scan only)"), 12f);
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

                // 0.5s tick — the missed-calls status re-check.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;

                    // Missed-calls status. Composed through LF, so it cannot be reference-diffed
                    // like the carpet's raw field — a value compare on the 0.5s tick instead of a
                    // per-frame string.Format. The click handler writes it immediately, so this is
                    // only the language-switch / external-edit path.
                    string missedStatus = this.LF("Status: {0}", this.GetClearMissedCallsStatus());
                    if (!string.Equals(missedStatus, handle.MissedCallsStatusShown, StringComparison.Ordinal))
                    {
                        handle.MissedCallsStatusShown = missedStatus;
                        this.SetUguiLabelText(handle.MissedCallsStatusLabel, missedStatus);
                    }
                }

                // Layout signature — packed carpet counts/flags.
                if (handle.LayoutPacked != this.ComputeUguiExtraLayoutPacked())
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

        // ClearMissedCallsFeature.cs — one shot: remove the missed invites, mute the quest calls,
        // refresh the red points. Green whenever the pass COMPLETED (an already-empty list is a
        // success), red only on a real refusal; the reason is in the status line and in the log
        // either way. The label is written here rather than waiting for the 0.5s tick, so the
        // result is on screen in the same frame as the click (Carpet scan precedent).
        private void OnUguiExtraClearMissedCallsClicked()
        {
            bool ok = this.ClearMissedCalls();
            string status = this.GetClearMissedCallsStatus();
            this.AddMenuNotification(this.LF("Missed calls: {0}", status),
                ok ? UguiExtraOkColor : UguiExtraFailColor);

            UguiShellNewFeaturesExtraHandle handle = this.uguiShellNewFeaturesExtra;
            if (handle != null && handle.Root != null)
            {
                handle.MissedCallsStatusShown = this.LF("Status: {0}", status);
                this.SetUguiLabelText(handle.MissedCallsStatusLabel, handle.MissedCallsStatusShown);
            }
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

    }
}
