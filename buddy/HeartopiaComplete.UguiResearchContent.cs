using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Research tab content (live instrument list + panel shortcuts).
    //
    // Refresh is NOT the LIVE rail's raw-signature diff: a "researching" row shows a countdown
    // interpolated from Time.unscaledTime, so a raw-data signature would look frozen between the
    // 5s server polls. Instead it throttles to ~1/sec and diffs a signature built from the FULLY
    // COMPUTED per-instrument display (label + status text + color + busy) — catching structural
    // changes, status transitions AND countdown ticks, while a genuinely idle list rebuilds
    // nothing. ResearchFormatRemaining is minute-granular, so even an active countdown rebuilds
    // rows ~1/min rather than every second.
    //
    // The bottom static section (2 shortcut buttons + footer hint) is built once but REPOSITIONED
    // on every row rebuild — its Y depends on the row count.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Research tab (live content — throttled computed-signature refresh)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellResearchContentHandle
        {
            public GameObject Root;
            public GameObject StatusLabel;          // "Status: {researchDiagStatus}" — set every tick
            public Transform RowsRoot;              // instrument rows are rebuilt under this
            public readonly List<GameObject> Rows = new List<GameObject>();
            public GameObject StoreButton;          // static — repositioned per row count
            public GameObject ConsoleButton;        // static — repositioned per row count
            public GameObject FooterLabel;          // static — repositioned per row count
            public float BlockWidth;
            public float RowsTopY;                  // block-local y where the dynamic region starts
            public string RowsSignature;            // computed-display signature (null = repopulate)
            public float NextRefreshAt;             // ~1/sec throttle
            public int ErrorCount;                  // refresh disabled at 3 (LIVE rail idiom)
        }

        private UguiShellResearchContentHandle uguiShellResearchContent;

        // One instrument row's fully-computed display state. Computed by
        // ComputeUguiShellResearchRowDisplays (the EXACT DrawResearchTab per-instrument logic) and
        // consumed by both the signature builder and the row builder, so the diff can never
        // disagree with what would be rendered.
        private struct UguiResearchRowDisplay
        {
            public int StaticId;
            public string Analyzer;
            public string Status;
            public Color StatusColor;
            public bool Busy;
        }

        // UGUI mirror of DrawResearchTab (ResearchDiagnosticsFeature.cs): header, status line,
        // "Instruments" header, per-instrument rows (or the loading hint), two panel-shortcut
        // buttons, footer help line. Static chrome is built here once; rows + bottom-section
        // positions are (re)applied by RebuildUguiShellResearchRows. Assigns the handle field
        // LAST so a mid-build exception (caught by the shell's per-tab guard) can never leave a
        // half-built handle refreshing.
        private GameObject BuildUguiShellResearchContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellResearchContent = null;

            UguiShellResearchContentHandle handle = new UguiShellResearchContentHandle();
            GameObject block = this.CreateUguiGo("ResearchContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            Color muted = this.UguiKitMutedColor();
            Color monoColor = new Color(muted.r, muted.g, muted.b, 0.85f); // IMGUI monoStyle alpha

            GameObject header = this.CreateUguiHeaderLabel(block.transform, "Header", this.L("Research Institute"), 14f);
            PlaceUguiTopLeft(header, pad, 12f, w - pad * 2f, 22f);

            // Status can run long (panel-open results) — wrapped, two lines of room.
            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status",
                this.LF("Status: {0}", this.researchDiagStatus), 11f, monoColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, pad, 40f, w - pad * 2f, 30f);

            GameObject instruments = this.CreateUguiHeaderLabel(block.transform, "Instruments", this.L("Instruments"), 14f);
            PlaceUguiTopLeft(instruments, pad, 76f, w - pad * 2f, 20f);

            GameObject rowsRoot = this.CreateUguiGo("Rows", block.transform);
            PlaceUguiTopLeft(rowsRoot, 0f, 0f, w, h); // rows use block-local coordinates inside
            handle.RowsRoot = rowsRoot.transform;
            handle.RowsTopY = 102f;
            handle.BlockWidth = w;

            handle.StoreButton = this.CreateUguiSecondaryButton(block.transform, "StoreButton",
                this.L("RESEARCH STORE"),
                new System.Action(() => this.StartResearchOpenPanelDirect(ResearchShopPanelTypeName, "Research Store")));
            handle.ConsoleButton = this.CreateUguiSecondaryButton(block.transform, "ConsoleButton",
                this.L("CONTROL CONSOLE"),
                new System.Action(() => this.StartResearchOpenPanelDirect(ResearchControlPanelTypeName, "Control Console")));

            handle.FooterLabel = this.CreateUguiLabel(block.transform, "Footer",
                this.L("Live from the server-sync cache. SELECT ITEM opens that analyzer's research picker (busy analyzers are locked until they finish). Everything is prepared automatically when you open this tab."),
                11f, monoColor, false);
            this.TrySetUguiLabelWrapped(handle.FooterLabel);

            handle.Root = block;

            // Populate immediately (no empty first frame) and seed the signature so the first
            // throttled tick only rebuilds if something actually changed. Also positions the
            // bottom static section for the initial row count.
            List<UguiResearchRowDisplay> rows = this.ComputeUguiShellResearchRowDisplays();
            handle.RowsSignature = BuildUguiShellResearchSignature(rows);
            this.RebuildUguiShellResearchRows(handle, rows);

            this.uguiShellResearchContent = handle;
            return block;
        }

        // True while the UGUI shell is visible and showing the Research tab — the UGUI-side half
        // of ProcessResearchMonitorOnUpdate's tab-open edge detect (its IMGUI half is
        // selectedTab == 9 && showMenu; without this, opening Research via the shell would never
        // fire the one-shot prepare, and SELECT ITEM / the panels could misbehave first-click).
        private bool IsUguiShellResearchTabActive()
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                return shell != null && shell.ActiveIndex == UguiShellResearchTabIndex
                    && this.IsUguiWindowVisible(shell.Window);
            }
            catch
            {
                return false;
            }
        }

        // Called every frame from ProcessUguiShellOnUpdate. Skips in a few comparisons unless the
        // shell is visible ON the Research tab; then throttles to ~1/sec. Each tick always
        // refreshes the status line (one cheap SetText) and rebuilds the rows only when the
        // computed-display signature changed. NextRefreshAt stops advancing while the tab is
        // hidden, so switching back refreshes on the first frame — never shows a stale countdown.
        private void ProcessUguiShellResearchContentOnUpdate()
        {
            UguiShellHandle shell = this.uguiShell;
            UguiShellResearchContentHandle handle = this.uguiShellResearchContent;
            if (shell == null || handle == null || handle.Root == null || handle.ErrorCount >= 3
                || shell.ActiveIndex != UguiShellResearchTabIndex || !this.IsUguiWindowVisible(shell.Window))
            {
                return;
            }

            if (Time.unscaledTime < handle.NextRefreshAt)
            {
                return;
            }
            handle.NextRefreshAt = Time.unscaledTime + 1f;

            try
            {
                this.SetUguiLabelText(handle.StatusLabel, this.LF("Status: {0}", this.researchDiagStatus));

                List<UguiResearchRowDisplay> rows = this.ComputeUguiShellResearchRowDisplays();
                string signature = BuildUguiShellResearchSignature(rows);
                if (!string.Equals(signature, handle.RowsSignature, StringComparison.Ordinal))
                {
                    handle.RowsSignature = signature;
                    this.RebuildUguiShellResearchRows(handle, rows);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Research content refresh error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Per-instrument display computation — copied EXACTLY from DrawResearchTab (clock
        // interpolation + the idle / done / researching 3-case), returning data instead of
        // painting. Returns null in the "loading" state (no snapshot yet / empty list).
        private List<UguiResearchRowDisplay> ComputeUguiShellResearchRowDisplays()
        {
            if (!this.researchMonitorHasSnapshot || this.researchMonitorSnapshot.Count == 0)
            {
                return null;
            }

            // Interpolate the game clock forward from the last poll so the countdowns tick smoothly.
            long estNowTicks = this.researchMonitorClockTicks;
            bool clockSane = estNowTicks >= ResearchSaneClockTicksFloor;
            if (clockSane)
            {
                estNowTicks += (long)((Time.unscaledTime - this.researchMonitorClockSampledAt) * TimeSpan.TicksPerSecond);
            }

            List<UguiResearchRowDisplay> rows = new List<UguiResearchRowDisplay>(this.researchMonitorSnapshot.Count);
            for (int i = 0; i < this.researchMonitorSnapshot.Count; i++)
            {
                ResearchInstrumentSnapshot inst = this.researchMonitorSnapshot[i];
                UguiResearchRowDisplay row = new UguiResearchRowDisplay();
                row.StaticId = inst.StaticId;
                row.Analyzer = this.LF("Analyzer {0}  ·  Lv {1}", inst.StaticId - 2000, inst.Level);

                if (inst.ResearchingItemId <= 0)
                {
                    row.Busy = false;
                    row.Status = this.L("idle");
                    row.StatusColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.7f);
                }
                else if (inst.CompleteTicks > 0L && clockSane && inst.CompleteTicks <= estNowTicks)
                {
                    // Research finished — the slot is free to pick a new item.
                    row.Busy = false;
                    row.Status = this.LF("DONE · {0}", this.ResearchResolveItemName(inst.ResearchingItemId));
                    row.StatusColor = new Color(0.45f, 1f, 0.55f);
                }
                else
                {
                    row.Busy = true;
                    string remain = (inst.CompleteTicks > 0L && clockSane)
                        ? ResearchFormatRemaining(inst.CompleteTicks - estNowTicks)
                        : "?";
                    row.Status = this.LF("researching {0} · {1}", this.ResearchResolveItemName(inst.ResearchingItemId), remain);
                    row.StatusColor = new Color(1f, 0.85f, 0.45f);
                }

                rows.Add(row);
            }
            return rows;
        }

        // Signature over the COMPUTED display values (never the raw snapshot structs — see file
        // header). Status text embeds the minute-granular countdown; StatusColor is fully implied
        // by Status+Busy, so it needs no separate component.
        private static string BuildUguiShellResearchSignature(List<UguiResearchRowDisplay> rows)
        {
            if (rows == null)
            {
                return "loading";
            }

            StringBuilder sb = new StringBuilder(rows.Count * 48);
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append(rows[i].StaticId).Append('|')
                  .Append(rows[i].Analyzer).Append('|')
                  .Append(rows[i].Status).Append('|')
                  .Append(rows[i].Busy ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        // Destroys + rebuilds the instrument rows (rows == null paints the loading hint instead),
        // then REPOSITIONS the static bottom section from the resulting y-cursor — the UGUI
        // equivalent of the IMGUI drawer's y accumulation. Only called when the computed
        // signature changed (or from the initial build) — never unconditionally per tick.
        private void RebuildUguiShellResearchRows(UguiShellResearchContentHandle handle, List<UguiResearchRowDisplay> rows)
        {
            for (int i = 0; i < handle.Rows.Count; i++)
            {
                GameObject row = handle.Rows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.Rows.Clear();

            Transform content = handle.RowsRoot;
            if (content == null)
            {
                return;
            }

            const float pad = 16f;
            float w = handle.BlockWidth;
            float yCur = handle.RowsTopY;
            Color muted = this.UguiKitMutedColor();

            if (rows == null)
            {
                GameObject loading = this.CreateUguiLabel(content, "Loading",
                    this.L("Loading instrument data… (enter the main town — the list fills in a moment)."),
                    11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
                this.TrySetUguiLabelWrapped(loading);
                PlaceUguiTopLeft(loading, pad, yCur, w - pad * 2f, 34f);
                handle.Rows.Add(loading);
                yCur += 38f;
            }
            else
            {
                const float buttonW = 110f;
                float buttonX = w - pad - buttonW;
                float statusW = buttonX - 160f - 6f;
                for (int i = 0; i < rows.Count; i++)
                {
                    UguiResearchRowDisplay row = rows[i];

                    GameObject analyzer = this.CreateUguiBodyLabel(content, "Analyzer" + i, row.Analyzer, 12f);
                    PlaceUguiTopLeft(analyzer, pad, yCur, 140f, 22f);
                    handle.Rows.Add(analyzer);

                    GameObject status = this.CreateUguiLabel(content, "Status" + i, row.Status, 11f, row.StatusColor, false);
                    PlaceUguiTopLeft(status, 160f, yCur + 1f, statusW, 20f);
                    handle.Rows.Add(status);

                    // A busy analyzer can't take a new item — the button is DISABLED, not merely
                    // click-guarded (Unity's default ColorBlock dims the background for free).
                    // Rebuilds fire on any busy transition (busy is part of the signature), so the
                    // interactable state can never go stale.
                    int staticIdCopy = row.StaticId; // capture a copy for the click closure
                    GameObject select = this.CreateUguiSecondaryButton(content, "Select" + i, this.L("SELECT ITEM"),
                        new System.Action(() => this.StartResearchOpenInstrumentPanelForStaticId(staticIdCopy)));
                    PlaceUguiTopLeft(select, buttonX, yCur - 1f, buttonW, 26f);
                    this.SetUguiButtonInteractable(select, !row.Busy);
                    handle.Rows.Add(select);

                    yCur += 32f;
                }

                yCur += 8f;
            }

            // Static bottom section — repositioned, not rebuilt.
            PlaceUguiTopLeft(handle.StoreButton, pad, yCur, 200f, 30f);
            PlaceUguiTopLeft(handle.ConsoleButton, pad + 210f, yCur, 200f, 30f);
            yCur += 38f;
            PlaceUguiTopLeft(handle.FooterLabel, pad, yCur, w - pad * 2f, 60f);
        }
    }
}
