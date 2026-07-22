using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI PHASE 4 — the floating QUEST ASSISTANT window: the UGUI port of
    // HeartopiaComplete.QuestAssistantUi.cs DrawQuestAssistantWindow (:110-155, the GUI.Window
    // wrapper — fully superseded by CreateUguiWindow/ProcessUguiWindowFrame + the input-ownership
    // registry, exactly like the Building Move Panel) and DrawQuestAssistantWindowContents
    // (:157-473, ported here). A standalone draggable/collapsible overlay, independent of BOTH
    // menu systems.
    //
    // Ground rules (same as every Phase 3 round):
    //  - ZERO backend changes: HeartopiaComplete.QuestAssistantUi.cs / QuestAssistant.cs and
    //    everything they call stay untouched. This file only READS the same fields
    //    (questAssistantSnapshot / questAssistantAvailable / questAssistantLastStatus /
    //    questAssistantFocusedTaskId / the monitor-active flags / the coroutine gates) and CALLS
    //    the same entry points (QuestAssistantOnDumpButtonClicked, QuestAssistantOnAcceptAllClicked,
    //    StartDailyQuestAutoSubmitItems, QuestAssistantStartCollect/StopCollectMonitor,
    //    QuestAssistantOnTalkToNpcClicked, QuestAssistantOnSubmitToNpcClicked,
    //    QuestAssistantStartCatchBird/StopBirdMonitor, QuestAssistantOnCraftClicked,
    //    QuestAssistantOnGoToAreaClicked, QuestAssistantOnEnterAreaClicked,
    //    QuestAssistantOnHomelandFarmClicked, QuestAssistantOnPurchaseItemClicked,
    //    QuestAssistantOnCatchFishClicked, QuestAssistantToggleWindow). The six monitor ticks are
    //    driven from HeartopiaComplete.cs's central update loop, not from any draw function — they
    //    keep working for both surfaces automatically.
    //  - questAssistantFocusedTaskId is the SHARED focus selection (same field the IMGUI window
    //    writes) — row clicks and the auto-revalidation write it from here too; that is a field
    //    write, not an edit to the source files, and keeps the two windows' selections in step.
    //  - The IMGUI-only visual fields (questAssistantWindowRect/Collapsed/ExpandedHeight/
    //    ScrollPos/MouseOver) are deliberately NOT read or written: collapse here is
    //    presentation-only state with no cross-surface data dependency, so this window tracks its
    //    own Collapsed/ExpandedHeight in its handle (the standard "IMGUI-only visual state, not
    //    reproduced" pattern).
    //
    // Window chrome:
    //  - Kit window (CreateUguiWindow, null title — compact 30px header owned here). sortingOrder
    //    29360 — mod band (20000..30000): Overlay 29300 < Building panel 29350 < this < Shell
    //    29400 < PoC 29500, below the Dropdown ceiling.
    //  - DRAG-HOTSPOT FIX (this round's real risk, resolved deliberately): the kit's polled
    //    ProcessUguiWindowDrag hit-tests a raw Input.GetMouseButtonDown(0) against the WHOLE
    //    TitleBarRt with no EventSystem awareness — Building Move Panel's strip has no buttons so
    //    it never met this, but here Refresh/collapse/× live in the header, and a click on any of
    //    them would ALSO satisfy the drag hit-test and start a tiny spurious drag on that same
    //    click. Resolution: the TitleBarRt is narrowed right after construction to a dedicated
    //    drag hotspot covering only the title-TEXT area — (0, 0, W-132, 30), stopping 4px short
    //    of the Refresh button at x = W-128 (the leftmost header button in either state) — so the
    //    drag hit-test can never fire over the button strip. Cost: the small header gaps between/
    //    right of the buttons stop being drag handles (IMGUI let them drag because its buttons
    //    consumed their own events first); the 288px title area remains a comfortable handle.
    //  - COLLAPSE (the round's one new kit-level capability): the header collapse button shrinks
    //    the window to just its 30px header row (QuestAssistantCollapsedHeight in source) and
    //    back to the REMEMBERED expanded height, mirroring QuestAssistantToggleCollapse (:86-99)
    //    on this window's own handle state. The resize follows ApplyUguiBuildingMovePanelHeight
    //    exactly: adjust PanelRt.sizeDelta, fold half the height delta into anchoredPosition.y so
    //    the TOP edge stays pinned (panel pivot is center), then re-clamp. Everything below the
    //    header lives under one "Body" GameObject that SetActive-toggles with the collapse; the
    //    title, collapse and × buttons sit OUTSIDE Body, so both header buttons keep working
    //    while collapsed (source draws them before its collapsed early-return, :171-180).
    //  - VISIBILITY GATE — questAssistantWindowVisible ALONE. Verified against the source:
    //    DrawQuestAssistantWindow (:110-116) has NO showMenu/shell check at all (unlike the
    //    Building panel, whose IMGUI twin carried its own !showMenu gate that its UGUI copy
    //    replicates), so no menu-state suppression is invented here — the IMGUI and UGUI windows
    //    deliberately coexist during the soak-test period, like every prior round.
    //  - Scale re-syncs to the shared persisted GetUiScale() on change (Phase 2e idiom); the
    //    PoC-only EnableUguiWindowScaleKeys is NOT wired. Theme reload: own
    //    "UguiQuestAssistantWindow" rebuilder (destroy + rebuild, state-preserving incl. the
    //    collapse state). Input ownership: NEW floating (non-modal) surface
    //    "UguiQuestAssistantWindow"; the IMGUI window's own registration is untouched.
    //
    // Content (all strings are unlocalized source literals, kept verbatim):
    //  - Refresh (:191-194, Secondary — plain GUI.skin.button, ungated: the dump method carries
    //    its own busy/min-interval guards), live status line (:196-198, fontSize 10, wrapped,
    //    uiText @ 0.8).
    //  - The two conditional rows (:200-227) FLOW like the source cursor (unlike the Daily
    //    Quests tab's fixed-slot adaptation — this window has the room to replay the original
    //    layout): "Available to accept: N" + Accept All (visible while questAssistantAvailable
    //    is non-empty, enabled while questAssistantAcceptAllCoroutine == null) then "Ready to
    //    submit: N" + Submit Items (visible while QuestAssistantCountReadyToSubmit() > 0 — a
    //    LIVE COMPUTED count every gated frame — enabled via the source's own busy helper); each
    //    visible row adds 24px, and the list/detail positions below re-flow (relayout gated on
    //    the packed visibility signature).
    //  - THE CLICKABLE LIST (:229-282): each row is one FULL-WIDTH BUTTON (Auto Sell cell
    //    precedent — whole surface clickable, fill color flips on selection: accent @ 0.85 when
    //    focused, white @ 0.12 otherwise), text "  {Name}   {(cur/needed)}" via the existing
    //    static QuestAssistantSummarizeProgress. Grow-on-demand pool inside a kit scroll view
    //    (transparent chrome — rows carry their own fills); rebind-by-diff keyed on the snapshot
    //    LIST REFERENCE (the resolver always publishes a fresh list — Daily Quests round
    //    precedent) plus the focused id. AUTO-FOCUS REVALIDATION (:246-259) runs EVERY gated
    //    frame — background resolves can drop the focused quest at any time, so an invalid focus
    //    defaults to quests[0].TaskId exactly like the source.
    //  - THE DETAIL PANE (:284-470): edge-only card outline (DrawCardOutline's alpha formula
    //    replayed as a kit ring overlay), bold wrapped quest name, objective-kind line via the
    //    existing static QuestAssistantDescribeObjectiveKind, then condition lines with the
    //    source's DYNAMIC STOPPING CONDITION — the loop stops as soon as the next 32px line
    //    would cross into the action-button zone (dy < actionButtonY - 4), a real available-
    //    space computation, NOT a fixed count: quests with many conditions truncate exactly like
    //    the source.
    //  - THE 9-WAY ACTION DISPATCH (:325-466): 10 pre-built Primary-tier slots (HomelandFarm's
    //    2 sub-states share ONE slot with internal branching, matching the source's single
    //    if-arm), exactly one SetActive per focused kind. Labels/enabled-states are LIVE —
    //    re-evaluated every gated frame through a packed flags signature (collect/bird monitor
    //    on THIS task, the global fish monitor, the purchase coroutine) plus the snapshot/focus
    //    dirty drivers. CatchFish only qualifies with ObjectiveAreaId > 0 (:454); Cook,
    //    CatchInsect and CatchFish-without-area show NO button (:468). HomelandFarm's sow-
    //    blocked sub-state is a DISABLED button with the flower-seeds explainer (:424-430).
    //    Click handlers re-resolve the focused snapshot and re-derive their branch inputs at
    //    click time (a one-frame stale click across a background snapshot swap no-ops instead of
    //    firing the wrong arm — retained-mode guard immediate-mode never needed).
    //  - Kit window chrome stands in for the IMGUI DrawRoundedPanel backdrop (:162-163), same as
    //    every migrated window; per-frame sync disabled after 3 consecutive errors (LIVE rail).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Geometry constants (source rect: 160,160,420,450; collapsed height 30)
        // ----------------------------------------------------------------------------------------

        private const float UguiQaWindowW = 420f;
        private const float UguiQaWindowDefaultExpandedH = 450f;
        private const float UguiQaWindowCollapsedH = 30f;        // QuestAssistantCollapsedHeight
        private const float UguiQaWindowHeaderH = 30f;           // header row = the collapsed height
        private const float UguiQaWindowDragStripW = UguiQaWindowW - 132f; // title-text area only (see header)
        private const int UguiQaWindowSortingOrder = 29360;      // Building panel 29350 < this < Shell 29400

        private const float UguiQaListRowPitch = 22f;            // source rowHeight (:235)
        private const float UguiQaListOuterW = UguiQaWindowW - 24f;      // 396 (:234)
        private const float UguiQaListContentW = UguiQaListOuterW - 22f; // 374 (kit viewport insets 4+18)
        private const float UguiQaListRowW = UguiQaListContentW - 4f;    // 370 (IMGUI "inner - 4" role)

        private const float UguiQaDetailH = 150f;                // source detailHeight (:230)
        private const float UguiQaDetailInnerW = UguiQaListOuterW - 12f; // 384 (x+6 with 6px right margin)
        private const float UguiQaDetailActionY = UguiQaDetailH - 30f;   // 120 — source actionButtonY (:316)
        private const float UguiQaDetailCondStartY = 42f;        // 4 (pad) + 20 (name) + 18 (objective)
        private const float UguiQaDetailCondPitch = 32f;         // source condition line step (:322)

        // Action slot ids — one exclusive slot per ObjectiveKind arm of the source dispatch.
        private const int UguiQaActionCollect = 0;
        private const int UguiQaActionTalkToNpc = 1;
        private const int UguiQaActionSubmitToNpc = 2;
        private const int UguiQaActionCatchBird = 3;
        private const int UguiQaActionCraft = 4;
        private const int UguiQaActionGoToArea = 5;
        private const int UguiQaActionEnterArea = 6;
        private const int UguiQaActionHomelandFarm = 7;          // ONE slot, 2-way internal branch
        private const int UguiQaActionPurchaseItem = 8;
        private const int UguiQaActionCatchFish = 9;             // only with ObjectiveAreaId > 0
        private const int UguiQaActionCount = 10;

        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiQaListRowHandle
        {
            public GameObject Root;
            public Image Bg;                 // fill flips on selection (Auto Sell cell precedent)
            public GameObject Label;
            public string Shown;
            public bool SelectedShown;
            public int TaskId;               // rebound each walk; the click handler reads it live
        }

        private sealed class UguiQuestAssistantWindowHandle
        {
            public UguiWindowHandle Window;
            public GameObject BodyRoot;      // everything the collapse hides
            public GameObject CollapseButton;
            public string CollapseLabelShown;

            public GameObject StatusLabel;
            public string StatusShown;

            // Conditional rows — FLOWING positions owned by the relayout.
            public GameObject AcceptLabel;
            public GameObject AcceptButton;
            public int AcceptShownCount = -1;
            public GameObject SubmitLabel;
            public GameObject SubmitButton;
            public int SubmitShownCount = -1;
            public int LayoutPacked = -1;    // (acceptVisible | submitVisible<<1) the last relayout used

            // The clickable list.
            public GameObject ScrollRoot;
            public Transform ScrollContent;
            public GameObject EmptyLabel;
            public readonly List<UguiQaListRowHandle> Rows = new List<UguiQaListRowHandle>();
            public int RowsShown;
            public List<QuestSnapshot> RowsListRef;   // list REFERENCE the last walk consumed
            public int RowsFocusShown = int.MinValue; // focus id the last walk highlighted
            public float RowsContentHeightShown = -1f;

            // The detail pane.
            public GameObject DetailRoot;
            public GameObject DetailPlaceholder;
            public GameObject DetailNameLabel;
            public string DetailNameShown;
            public GameObject DetailObjectiveLabel;
            public string DetailObjectiveShown;
            public readonly List<GameObject> CondLabels = new List<GameObject>();
            public readonly List<string> CondShown = new List<string>();
            public int CondShownCount;
            public readonly GameObject[] ActionButtons = new GameObject[UguiQaActionCount];
            public readonly string[] ActionLabelShown = new string[UguiQaActionCount];
            public List<QuestSnapshot> DetailListRef;
            public int DetailFocusShown = int.MinValue;
            public int DetailFlagsShown = -1; // packed live-toggle flags (monitors/purchase gate)

            // UGUI-side collapse state (deliberately independent of the IMGUI fields — header).
            public bool Collapsed;
            public float ExpandedHeight = UguiQaWindowDefaultExpandedH;

            public float LastSyncedUiScale = -1f; // Phase 2e shell scale-sync idiom
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiQuestAssistantWindowHandle uguiQuestAssistantWindow;
        private bool uguiQuestAssistantWindowBuildFailed;

        // ----------------------------------------------------------------------------------------
        // Builder — lazy, on the first frame questAssistantWindowVisible is true (Building Move
        // Panel shape). Registers its own theme rebuilder + FLOATING input-ownership surface.
        // ----------------------------------------------------------------------------------------

        private void BuildUguiQuestAssistantWindow()
        {
            this.uguiQuestAssistantWindow = null;
            UguiQuestAssistantWindowHandle handle = null;
            try
            {
                handle = new UguiQuestAssistantWindowHandle();
                handle.Window = this.CreateUguiWindow(
                    "BugtopiaUguiQuestAssistantWindow",
                    null, // compact 30px header owned below — the kit's 18pt title strip is oversized here
                    null,
                    new Vector2(UguiQaWindowW, UguiQaWindowDefaultExpandedH),
                    UguiQaWindowSortingOrder,
                    UguiQaWindowHeaderH);
                Transform panelT = handle.Window.PanelRt;

                // DRAG-HOTSPOT FIX (file header): narrow the kit's drag rect to the title-text
                // area so ProcessUguiWindowDrag's raw hit-test can never swallow a header-button
                // click into a spurious drag. PlaceUguiTopLeft anchored it top-left with pivot
                // (0,1), so shrinking sizeDelta keeps it flush with the window's top-left.
                handle.Window.TitleBarRt.sizeDelta = new Vector2(UguiQaWindowDragStripW, UguiQaWindowHeaderH);

                // ---- Header (outside Body — title + collapse + close work while collapsed) ----

                // :165-167 — bold 13 title in uiText.
                GameObject title = this.CreateUguiLabel(panelT, "Title", "Quest Assistant",
                    13f, this.UguiKitTextColor(), false);
                this.TrySetUguiLabelBold(title);
                PlaceUguiTopLeft(title, 12f, 4f, UguiQaWindowW - 132f, 20f);

                // :177-180 — collapse/expand toggle ("–"/"+"), left of ×; plain GUI.skin.button
                // in source → Secondary tier.
                handle.CollapseLabelShown = "–";
                handle.CollapseButton = this.CreateUguiSecondaryButton(panelT, "CollapseButton",
                    handle.CollapseLabelShown, new System.Action(this.OnUguiQuestAssistantCollapseClicked));
                PlaceUguiTopLeft(handle.CollapseButton, UguiQaWindowW - 52f, 4f, 20f, 20f);

                // :171-174 — close (hide) button, far right.
                GameObject closeBtn = this.CreateUguiSecondaryButton(panelT, "CloseButton", "×",
                    new System.Action(this.OnUguiQuestAssistantCloseClicked));
                PlaceUguiTopLeft(closeBtn, UguiQaWindowW - 28f, 4f, 20f, 20f);

                // ---- Body (everything below — SetActive'd off while collapsed, :182-187) ----

                GameObject body = this.CreateUguiGo("Body", panelT);
                PlaceUguiTopLeft(body, 0f, 0f, UguiQaWindowW, UguiQaWindowDefaultExpandedH);
                handle.BodyRoot = body;

                // :191-194 — Refresh (expanded only — the source draws it AFTER the collapsed
                // early-return; sitting inside Body reproduces that). Ungated: the dump method
                // carries its own busy/min-interval guards (Daily Quests round note).
                GameObject refreshBtn = this.CreateUguiSecondaryButton(body.transform, "RefreshButton",
                    "Refresh", new System.Action(this.OnUguiQuestAssistantRefreshClicked));
                PlaceUguiTopLeft(refreshBtn, UguiQaWindowW - 128f, 4f, 70f, 20f);

                // :196-198 — the shared statusStyle role: fontSize 10, wordWrap, uiText @ 0.8.
                Color text = this.UguiKitTextColor();
                Color statusColor = new Color(text.r, text.g, text.b, 0.8f);
                handle.StatusShown = this.questAssistantLastStatus ?? string.Empty;
                handle.StatusLabel = this.CreateUguiLabel(body.transform, "Status",
                    handle.StatusShown, 10f, statusColor, false);
                this.TrySetUguiLabelWrapped(handle.StatusLabel);
                PlaceUguiTopLeft(handle.StatusLabel, 12f, 26f, UguiQaWindowW - 24f, 16f);

                // :200-227 — the two conditional rows (label + Secondary button each). Built at
                // their y=44 defaults; positions AND visibility owned by the relayout.
                handle.AcceptLabel = this.CreateUguiLabel(body.transform, "AcceptCount",
                    string.Empty, 10f, statusColor, false);
                PlaceUguiTopLeft(handle.AcceptLabel, 12f, 44f, UguiQaWindowW - 150f, 22f);
                handle.AcceptLabel.SetActive(false);
                handle.AcceptButton = this.CreateUguiSecondaryButton(body.transform, "AcceptAllButton",
                    "Accept All", new System.Action(this.OnUguiQuestAssistantAcceptAllClicked));
                PlaceUguiTopLeft(handle.AcceptButton, UguiQaWindowW - 130f, 42f, 118f, 22f);
                handle.AcceptButton.SetActive(false);

                handle.SubmitLabel = this.CreateUguiLabel(body.transform, "SubmitCount",
                    string.Empty, 10f, statusColor, false);
                PlaceUguiTopLeft(handle.SubmitLabel, 12f, 44f, UguiQaWindowW - 150f, 22f);
                handle.SubmitLabel.SetActive(false);
                handle.SubmitButton = this.CreateUguiSecondaryButton(body.transform, "SubmitItemsButton",
                    "Submit Items", new System.Action(this.OnUguiQuestAssistantSubmitItemsClicked));
                PlaceUguiTopLeft(handle.SubmitButton, UguiQaWindowW - 130f, 42f, 118f, 22f);
                handle.SubmitButton.SetActive(false);

                // :233-239 — the quest list scroll view. Transparent chrome (Daily Quests flat-
                // look idiom — alpha-0 images still raycast, so wheel/drag scrolling keeps
                // working); the rows carry their own fills. Positioned by the relayout.
                Transform scrollContent;
                handle.ScrollRoot = this.CreateUguiScrollView(body.transform, "QuestList", 10f, out scrollContent);
                handle.ScrollContent = scrollContent;
                try
                {
                    Image scrollBg = handle.ScrollRoot.GetComponent<Image>();
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

                // :240-243 — the empty-state hint inside the (empty) list area.
                handle.EmptyLabel = this.CreateUguiLabel(scrollContent, "EmptyHint",
                    "(no active quests — click Refresh)", 10f, statusColor, false);
                PlaceUguiTopLeft(handle.EmptyLabel, 2f, 2f, UguiQaListRowW, 20f);
                handle.EmptyLabel.SetActive(false);

                // :284-286 — the bordered detail card: DrawCardOutline is an EDGE-ONLY outline
                // whose alpha is clamp(0.05 + uiPanelAlpha*0.05, 0.05..0.10) — replayed verbatim
                // as a kit ring overlay on an image-less container. Positioned by the relayout.
                GameObject detail = this.CreateUguiGo("DetailCard", body.transform);
                Color edge = new Color(1f, 1f, 1f, Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));
                this.AddUguiRingOverlay(detail, edge, 1f);
                handle.DetailRoot = detail;

                // :301-304 — the no-selection placeholder (same slot as the name; exclusive).
                handle.DetailPlaceholder = this.CreateUguiLabel(detail.transform, "Placeholder",
                    "(select a quest above)", 10f, statusColor, false);
                PlaceUguiTopLeft(handle.DetailPlaceholder, 6f, 4f, UguiQaDetailInnerW, 20f);

                // :308-311 — bold 12 wrapped quest name.
                handle.DetailNameLabel = this.CreateUguiLabel(detail.transform, "QuestName",
                    string.Empty, 12f, this.UguiKitTextColor(), false);
                this.TrySetUguiLabelBold(handle.DetailNameLabel);
                this.TrySetUguiLabelWrapped(handle.DetailNameLabel);
                PlaceUguiTopLeft(handle.DetailNameLabel, 6f, 4f, UguiQaDetailInnerW, 20f);
                handle.DetailNameLabel.SetActive(false);

                // :313 — the objective-kind line.
                handle.DetailObjectiveLabel = this.CreateUguiLabel(detail.transform, "Objective",
                    string.Empty, 10f, statusColor, false);
                this.TrySetUguiLabelWrapped(handle.DetailObjectiveLabel);
                PlaceUguiTopLeft(handle.DetailObjectiveLabel, 6f, 24f, UguiQaDetailInnerW, 16f);
                handle.DetailObjectiveLabel.SetActive(false);

                // Condition-line labels are pooled on demand by the detail rebind (their count is
                // a live space computation, not a constant).

                // :325-466 — the 10 exclusive action slots, all Primary tier (every arm uses
                // themePrimaryButtonStyle in source, including HomelandFarm's disabled sub-state).
                // ONE dispatcher click handler re-derives everything at click time.
                for (int slotId = 0; slotId < UguiQaActionCount; slotId++)
                {
                    int idCopy = slotId; // capture a copy for the closure
                    GameObject actionBtn = this.CreateUguiPrimaryButton(detail.transform, "Action" + idCopy,
                        string.Empty, new System.Action(() => this.OnUguiQuestAssistantActionClicked(idCopy)));
                    PlaceUguiTopLeft(actionBtn, 6f, UguiQaDetailActionY, UguiQaDetailInnerW, 26f);
                    actionBtn.SetActive(false);
                    handle.ActionButtons[slotId] = actionBtn;
                }

                // Seed pass — counts/gates/layout/rows/detail from live backend state so the
                // first shown frame is already correct (every sync takes the local handle).
                this.SyncUguiQuestAssistantConditionalRows(handle);
                this.SyncUguiQuestAssistantRows(handle);
                this.SyncUguiQuestAssistantDetail(handle);

                // Scale, then spawn at the IMGUI default rect (160, 160) converted to the
                // centered-pivot canvas space at the current scale (Building panel conversion).
                handle.LastSyncedUiScale = this.GetUiScale();
                this.SetUguiWindowScale(handle.Window, handle.LastSyncedUiScale);
                float s = Mathf.Max(handle.Window.Scale, 0.1f);
                float halfW = Screen.width / s * 0.5f;
                float halfH = Screen.height / s * 0.5f;
                handle.Window.PanelRt.anchoredPosition = new Vector2(
                    -halfW + UguiQaWindowW * 0.5f + 160f,
                    halfH - 160f - handle.Window.Size.y * 0.5f);
                this.ClampUguiWindowPosition(handle.Window);

                this.uguiQuestAssistantWindow = handle;

                // Live theme reload — this window lives OUTSIDE the shell, so it needs its own
                // rebuilder (idempotent by name).
                this.RegisterUguiThemeRebuilder("UguiQuestAssistantWindow",
                    new System.Action(this.RebuildUguiQuestAssistantWindowForTheme));

                // Input ownership: FLOATING surface (not modal), kit pointer-over hit test.
                // Closures read the LIVE field on every call — never capture the handle (theme
                // rebuilds replace it).
                this.RegisterInputOwnershipSurface("UguiQuestAssistantWindow", false,
                    () => this.uguiQuestAssistantWindow != null
                        && this.IsUguiWindowVisible(this.uguiQuestAssistantWindow.Window),
                    () => this.uguiQuestAssistantWindow != null
                        && this.IsUguiWindowPointerOver(this.uguiQuestAssistantWindow.Window));

                ModLogger.Msg("[UguiShell] Quest Assistant window built (sortingOrder "
                    + UguiQaWindowSortingOrder + ", gate = questAssistantWindowVisible alone)");
            }
            catch (Exception ex)
            {
                this.uguiQuestAssistantWindowBuildFailed = true;
                try
                {
                    if (handle != null && handle.Window != null && handle.Window.Root != null)
                    {
                        Object.Destroy(handle.Window.Root);
                    }
                }
                catch { }
                this.uguiQuestAssistantWindow = null;
                ModLogger.Msg("[UguiShell] Quest Assistant window build failed: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Collapse — mirrors QuestAssistantToggleCollapse (:86-99) on this window's OWN handle
        // state (file header: presentation-only, the IMGUI fields are not touched).
        // ----------------------------------------------------------------------------------------

        private void SetUguiQuestAssistantWindowCollapsed(UguiQuestAssistantWindowHandle handle, bool collapsed)
        {
            if (handle == null || handle.Window == null || handle.Collapsed == collapsed)
            {
                return;
            }
            if (collapsed)
            {
                // :90 — remember the CURRENT height so expanding restores it exactly.
                handle.ExpandedHeight = handle.Window.Size.y;
            }
            handle.Collapsed = collapsed;
            this.ApplyUguiQuestAssistantWindowHeight(handle);
            SetUguiGoActive(handle.BodyRoot, !collapsed);
            string collapseLabel = collapsed ? "+" : "–"; // :177 caption swap
            if (!string.Equals(collapseLabel, handle.CollapseLabelShown, StringComparison.Ordinal))
            {
                handle.CollapseLabelShown = collapseLabel;
                this.SetUguiButtonLabel(handle.CollapseButton, collapseLabel);
            }
        }

        // The resize itself — ApplyUguiBuildingMovePanelHeight's exact shape: adjust sizeDelta,
        // fold half the delta into anchoredPosition.y so the TOP edge stays pinned (panel pivot
        // is center; height grows both ways), then re-clamp. No-ops while the height matches.
        private void ApplyUguiQuestAssistantWindowHeight(UguiQuestAssistantWindowHandle handle)
        {
            UguiWindowHandle win = (handle != null) ? handle.Window : null;
            if (win == null || win.PanelRt == null)
            {
                return;
            }
            float target = handle.Collapsed ? UguiQaWindowCollapsedH : handle.ExpandedHeight;
            if (Mathf.Approximately(win.Size.y, target))
            {
                return;
            }
            float delta = target - win.Size.y;
            win.Size = new Vector2(win.Size.x, target);
            win.PanelRt.sizeDelta = win.Size;
            Vector2 pos = win.PanelRt.anchoredPosition;
            pos.y -= delta * 0.5f; // pin the top edge (y grows upward; height grows both ways)
            win.PanelRt.anchoredPosition = pos;
            this.ClampUguiWindowPosition(win);
        }

        // ----------------------------------------------------------------------------------------
        // Conditional rows + flowing relayout (:200-231 cursor replayed)
        // ----------------------------------------------------------------------------------------

        // Runs every gated frame (and once at build): live counts into the row labels, the two
        // gates (live coroutine refs — SetUguiButtonInteractable self-diffs), and the relayout
        // whenever either row's visibility flips.
        private void SyncUguiQuestAssistantConditionalRows(UguiQuestAssistantWindowHandle handle)
        {
            // :201 / :215 — availableCount is a field read; readyCount is LIVE COMPUTED every
            // gated frame (the source calls it every OnGUI pass; allocation-free snapshot walk).
            int acceptCount = (this.questAssistantAvailable != null) ? this.questAssistantAvailable.Count : 0;
            int readyCount = this.QuestAssistantCountReadyToSubmit();

            if (acceptCount != handle.AcceptShownCount)
            {
                handle.AcceptShownCount = acceptCount;
                if (acceptCount > 0)
                {
                    this.SetUguiLabelText(handle.AcceptLabel, "Available to accept: " + acceptCount); // :204
                }
            }
            if (readyCount != handle.SubmitShownCount)
            {
                handle.SubmitShownCount = readyCount;
                if (readyCount > 0)
                {
                    this.SetUguiLabelText(handle.SubmitLabel, "Ready to submit: " + readyCount); // :218
                }
            }

            // :205 / :219 — the two gates, recomputed live.
            this.SetUguiButtonInteractable(handle.AcceptButton, this.questAssistantAcceptAllCoroutine == null);
            this.SetUguiButtonInteractable(handle.SubmitButton, !this.QuestAssistantIsDailyQuestSubmitBusy());

            int packed = (acceptCount > 0 ? 1 : 0) | (readyCount > 0 ? 1 : 0) << 1;
            if (packed != handle.LayoutPacked)
            {
                this.RelayoutUguiQuestAssistantWindow(handle, acceptCount > 0, readyCount > 0);
            }
        }

        // Replays the source cursor: extraRowTop starts at 44, each visible conditional row adds
        // 24 (:200-227); listTop/listHeight/detailTop re-flow below (:229-231, :284-285).
        private void RelayoutUguiQuestAssistantWindow(UguiQuestAssistantWindowHandle handle,
            bool acceptVisible, bool submitVisible)
        {
            float extraRowTop = 44f;
            SetUguiGoActive(handle.AcceptLabel, acceptVisible);
            SetUguiGoActive(handle.AcceptButton, acceptVisible);
            if (acceptVisible)
            {
                PlaceUguiTopLeft(handle.AcceptLabel, 12f, extraRowTop, UguiQaWindowW - 150f, 22f);
                PlaceUguiTopLeft(handle.AcceptButton, UguiQaWindowW - 130f, extraRowTop - 2f, 118f, 22f);
                extraRowTop += 24f;
            }
            SetUguiGoActive(handle.SubmitLabel, submitVisible);
            SetUguiGoActive(handle.SubmitButton, submitVisible);
            if (submitVisible)
            {
                PlaceUguiTopLeft(handle.SubmitLabel, 12f, extraRowTop, UguiQaWindowW - 150f, 22f);
                PlaceUguiTopLeft(handle.SubmitButton, UguiQaWindowW - 130f, extraRowTop - 2f, 118f, 22f);
                extraRowTop += 24f;
            }

            // :229-231 — the list fills whatever the rows left above the fixed-height detail.
            float listTop = (extraRowTop == 44f) ? 46f : extraRowTop;
            float listHeight = Mathf.Max(40f, handle.ExpandedHeight - listTop - UguiQaDetailH - 12f);
            PlaceUguiTopLeft(handle.ScrollRoot, 12f, listTop, UguiQaListOuterW, listHeight);

            // :284-285.
            float detailTop = listTop + listHeight + 8f;
            PlaceUguiTopLeft(handle.DetailRoot, 12f, detailTop, UguiQaListOuterW, UguiQaDetailH);

            handle.LayoutPacked = (acceptVisible ? 1 : 0) | (submitVisible ? 1 : 0) << 1;
        }

        // ----------------------------------------------------------------------------------------
        // The clickable quest list (:229-282) — pooled full-width row BUTTONS
        // ----------------------------------------------------------------------------------------

        private void SyncUguiQuestAssistantRows(UguiQuestAssistantWindowHandle handle)
        {
            List<QuestSnapshot> quests = this.questAssistantSnapshot;

            // AUTO-FOCUS REVALIDATION (:246-259) — EVERY gated frame, not just on click: the
            // snapshot can change from background resolves at any time, so a focus id no longer
            // present defaults to quests[0].TaskId exactly like the source (which, like here,
            // only revalidates while the list is non-empty).
            if (quests != null && quests.Count > 0)
            {
                bool focusValid = false;
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].TaskId == this.questAssistantFocusedTaskId)
                    {
                        focusValid = true;
                        break;
                    }
                }
                if (!focusValid)
                {
                    this.questAssistantFocusedTaskId = quests[0].TaskId;
                }
            }

            // Idle path: the resolver always publishes a FRESH list (Daily Quests round — never
            // mutated in place after publication), so an unchanged reference + unchanged focus
            // means nothing displayed can have changed. Focus moves without a new list (row
            // click / revalidation), so both are dirty drivers.
            if (ReferenceEquals(quests, handle.RowsListRef)
                && handle.RowsFocusShown == this.questAssistantFocusedTaskId)
            {
                return;
            }
            handle.RowsListRef = quests;
            handle.RowsFocusShown = this.questAssistantFocusedTaskId;

            int count = (quests != null) ? quests.Count : 0;
            SetUguiGoActive(handle.EmptyLabel, count == 0); // :240-243

            // :269-271 — accent @ 0.85 for the focused row, white @ 0.12 otherwise (read live;
            // theme changes rebuild the whole window anyway).
            Color focusFill = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.85f);
            Color normalFill = new Color(1f, 1f, 1f, 0.12f);

            for (int i = 0; i < count; i++)
            {
                QuestSnapshot q = quests[i];
                UguiQaListRowHandle row;
                if (i >= handle.Rows.Count)
                {
                    row = this.BuildUguiQuestAssistantListRow(handle, i, normalFill);
                    handle.Rows.Add(row);
                }
                else
                {
                    row = handle.Rows[i];
                }
                if (row == null || row.Root == null)
                {
                    continue;
                }
                if (!row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }

                row.TaskId = q.TaskId; // the click handler reads this live
                bool isFocused = q.TaskId == this.questAssistantFocusedTaskId; // :264
                if (isFocused != row.SelectedShown)
                {
                    row.SelectedShown = isFocused;
                    if (row.Bg != null)
                    {
                        row.Bg.color = isFocused ? focusFill : normalFill;
                    }
                }

                // :273 — row text verbatim (leading/inner spaces are the source's own indent).
                string rowText = "  " + q.Name + "   " + QuestAssistantSummarizeProgress(q);
                if (!string.Equals(rowText, row.Shown, StringComparison.Ordinal))
                {
                    row.Shown = rowText;
                    this.SetUguiLabelText(row.Label, rowText);
                }
            }

            // Deactivate unused trailing rows (standard pool tail — deactivate, never destroy).
            for (int i = count; i < handle.Rows.Count; i++)
            {
                UguiQaListRowHandle row = handle.Rows[i];
                if (row != null && row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }
            handle.RowsShown = count;

            // :236 — contentHeight = quests.Count * rowHeight.
            float contentHeight = count * UguiQaListRowPitch;
            if (!Mathf.Approximately(contentHeight, handle.RowsContentHeightShown))
            {
                handle.RowsContentHeightShown = contentHeight;
                this.SetUguiScrollContentHeight(handle.ScrollContent, contentHeight);
            }
        }

        // One pooled row: the ENTIRE row is one clickable, color-changing Button surface (Auto
        // Sell grid-cell precedent — not label + trailing buttons). Position is index-fixed
        // (:265 — rowRect y = i * rowHeight, height rowHeight - 2); pool index == data index.
        private UguiQaListRowHandle BuildUguiQuestAssistantListRow(UguiQuestAssistantWindowHandle handle,
            int index, Color normalFill)
        {
            UguiQaListRowHandle row = new UguiQaListRowHandle();
            GameObject root = this.CreateUguiGo("Row" + index, handle.ScrollContent);
            PlaceUguiTopLeft(root, 2f, index * UguiQaListRowPitch, UguiQaListRowW, UguiQaListRowPitch - 2f);
            Image bg = this.AddUguiImage(root, normalFill, true, 2f);
            bg.raycastTarget = true;
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = bg;
            // Capture the row HANDLE (not an index/id copy): rebinds update row.TaskId, and the
            // handler reads it at click time — a recycled slot always reports its current quest.
            System.Action onClick = () => this.OnUguiQuestAssistantRowClicked(row);
            btn.onClick.AddListener(onClick);

            // :267 — fontSize 11, MiddleLeft (kit labels are left-aligned by default).
            GameObject label = this.CreateUguiLabel(root.transform, "Label",
                string.Empty, 11f, this.UguiKitTextColor(), false);
            StretchUguiFill(label, 4f, 0f, 4f, 0f);

            row.Root = root;
            row.Bg = bg;
            row.Label = label;
            return row;
        }

        // :273-276 — clicking a row focuses its quest. The next gated frame recolors the rows
        // and rebinds the detail pane (both keyed on the focus id).
        private void OnUguiQuestAssistantRowClicked(UguiQaListRowHandle row)
        {
            try
            {
                if (row == null || row.Root == null || !row.Root.activeSelf)
                {
                    return;
                }
                this.questAssistantFocusedTaskId = row.TaskId;
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // The detail pane (:284-470)
        // ----------------------------------------------------------------------------------------

        private QuestSnapshot FindUguiQuestAssistantFocusedSnapshot()
        {
            List<QuestSnapshot> quests = this.questAssistantSnapshot;
            if (quests == null)
            {
                return null;
            }
            for (int i = 0; i < quests.Count; i++)
            {
                if (quests[i].TaskId == this.questAssistantFocusedTaskId)
                {
                    return quests[i];
                }
            }
            return null;
        }

        // Runs every gated frame; the rebind itself only runs when a dirty driver moved: the
        // snapshot LIST REFERENCE, the focused id, or the packed live-toggle flags (the labels/
        // enabled-states that can flip WITHOUT a click or a fresh snapshot — monitor stops on
        // their own cadence, the purchase coroutine ends in the background).
        private void SyncUguiQuestAssistantDetail(UguiQuestAssistantWindowHandle handle)
        {
            List<QuestSnapshot> quests = this.questAssistantSnapshot;
            QuestSnapshot focused = null;
            if (quests != null)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].TaskId == this.questAssistantFocusedTaskId)
                    {
                        focused = quests[i];
                        break;
                    }
                }
            }

            int flags = 0;
            if (focused != null)
            {
                if (this.questAssistantCollectMonitorActive
                    && this.questAssistantCollectMonitorTaskId == focused.TaskId)
                {
                    flags |= 1; // :330 — Collect label toggle
                }
                if (this.questAssistantBirdMonitorActive
                    && this.questAssistantBirdMonitorTaskId == focused.TaskId)
                {
                    flags |= 2; // :373 — CatchBird label toggle
                }
                if (this.questAssistantFishMonitorActive)
                {
                    flags |= 4; // :459 — CatchFish label toggle (global flag, per source)
                }
                if (this.questAssistantPurchaseCoroutine != null)
                {
                    flags |= 8; // :445 — PurchaseItem busy gate
                }
            }

            if (ReferenceEquals(quests, handle.DetailListRef)
                && handle.DetailFocusShown == this.questAssistantFocusedTaskId
                && handle.DetailFlagsShown == flags)
            {
                return;
            }
            handle.DetailListRef = quests;
            handle.DetailFocusShown = this.questAssistantFocusedTaskId;
            handle.DetailFlagsShown = flags;

            this.RebindUguiQuestAssistantDetail(handle, focused);
        }

        private void RebindUguiQuestAssistantDetail(UguiQuestAssistantWindowHandle handle, QuestSnapshot focused)
        {
            bool hasFocus = focused != null;
            SetUguiGoActive(handle.DetailPlaceholder, !hasFocus); // :301-304
            SetUguiGoActive(handle.DetailNameLabel, hasFocus);
            SetUguiGoActive(handle.DetailObjectiveLabel, hasFocus);

            int condShown = 0;
            int activeSlot = -1;
            if (hasFocus)
            {
                if (!string.Equals(focused.Name, handle.DetailNameShown, StringComparison.Ordinal))
                {
                    handle.DetailNameShown = focused.Name;
                    this.SetUguiLabelText(handle.DetailNameLabel, focused.Name); // :310
                }
                string objective = "Objective: " + QuestAssistantDescribeObjectiveKind(focused.ObjectiveKind); // :313
                if (!string.Equals(objective, handle.DetailObjectiveShown, StringComparison.Ordinal))
                {
                    handle.DetailObjectiveShown = objective;
                    this.SetUguiLabelText(handle.DetailObjectiveLabel, objective);
                }

                // :316-323 — condition lines with the source's DYNAMIC STOPPING CONDITION: keep
                // emitting 32px lines only while the next one still fits above the action-button
                // zone (dy < actionButtonY - 4). A real available-space computation — a quest
                // with many conditions truncates here exactly like the source; NEVER a fixed
                // count or an unconditional Conditions.Count.
                float dy = UguiQaDetailCondStartY;
                for (int c = 0; c < focused.Conditions.Count && dy < UguiQaDetailActionY - 4f; c++)
                {
                    ConditionSnapshot cond = focused.Conditions[c];
                    string line = "- " + cond.Description + " (" + cond.Current + "/" + cond.Needed + ")"
                        + (cond.Complete ? " (done)" : string.Empty); // :320 verbatim
                    if (condShown >= handle.CondLabels.Count)
                    {
                        Color condColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.8f);
                        GameObject grown = this.CreateUguiLabel(handle.DetailRoot.transform,
                            "Cond" + condShown, string.Empty, 10f, condColor, false);
                        this.TrySetUguiLabelWrapped(grown);
                        handle.CondLabels.Add(grown);
                        handle.CondShown.Add(null);
                    }
                    GameObject slotGo = handle.CondLabels[condShown];
                    if (slotGo != null && !slotGo.activeSelf)
                    {
                        slotGo.SetActive(true);
                    }
                    PlaceUguiTopLeft(slotGo, 6f, dy, UguiQaDetailInnerW, 32f);
                    if (!string.Equals(line, handle.CondShown[condShown], StringComparison.Ordinal))
                    {
                        handle.CondShown[condShown] = line;
                        this.SetUguiLabelText(slotGo, line);
                    }
                    dy += UguiQaDetailCondPitch;
                    condShown++;
                }

                activeSlot = UguiQaActionSlotForKind(focused.ObjectiveKind);
                if (activeSlot == UguiQaActionCatchFish && focused.ObjectiveAreaId <= 0)
                {
                    // :454 — CatchFish WITHOUT an area id shows NO button at all.
                    activeSlot = -1;
                }
            }
            else
            {
                handle.DetailNameShown = null;
                handle.DetailObjectiveShown = null;
            }

            for (int i = condShown; i < handle.CondLabels.Count; i++)
            {
                GameObject slotGo = handle.CondLabels[i];
                if (slotGo != null && slotGo.activeSelf)
                {
                    slotGo.SetActive(false);
                }
            }
            handle.CondShownCount = condShown;

            // Exactly one contextual button (or none) — the source's if/else-if chain as
            // exclusive SetActives over the pre-built slots.
            for (int i = 0; i < UguiQaActionCount; i++)
            {
                SetUguiGoActive(handle.ActionButtons[i], i == activeSlot);
            }
            if (activeSlot >= 0)
            {
                bool interactable;
                string label = this.ComposeUguiQuestAssistantActionLabel(focused, activeSlot, out interactable);
                if (!string.Equals(label, handle.ActionLabelShown[activeSlot], StringComparison.Ordinal))
                {
                    handle.ActionLabelShown[activeSlot] = label;
                    this.SetUguiButtonLabel(handle.ActionButtons[activeSlot], label);
                }
                this.SetUguiButtonInteractable(handle.ActionButtons[activeSlot], interactable);
            }
        }

        // ObjectiveKind → slot (the source :325-466 chain order). Cook, CatchInsect and Unknown
        // deliberately map to NONE (:468 — no button is invented for them); the CatchFish
        // ObjectiveAreaId > 0 gate is applied by the callers.
        private static int UguiQaActionSlotForKind(QuestObjectiveKind kind)
        {
            switch (kind)
            {
                case QuestObjectiveKind.Collect: return UguiQaActionCollect;
                case QuestObjectiveKind.TalkToNpc: return UguiQaActionTalkToNpc;
                case QuestObjectiveKind.SubmitToNpc: return UguiQaActionSubmitToNpc;
                case QuestObjectiveKind.CatchBird: return UguiQaActionCatchBird;
                case QuestObjectiveKind.Craft: return UguiQaActionCraft;
                case QuestObjectiveKind.GoToArea: return UguiQaActionGoToArea;
                case QuestObjectiveKind.EnterArea: return UguiQaActionEnterArea;
                case QuestObjectiveKind.HomelandFarm: return UguiQaActionHomelandFarm;
                case QuestObjectiveKind.PurchaseItem: return UguiQaActionPurchaseItem;
                case QuestObjectiveKind.CatchFish: return UguiQaActionCatchFish;
                default: return -1;
            }
        }

        // Live label + enabled-state per slot — every string verbatim from the source arm cited.
        private string ComposeUguiQuestAssistantActionLabel(QuestSnapshot focused, int slotId, out bool interactable)
        {
            interactable = true;
            switch (slotId)
            {
                case UguiQaActionCollect:
                {
                    // :327-332 — ObjectiveTargetIds when non-empty, else a 1-item fallback list
                    // of ObjectiveTargetId (verbatim), summarized by the existing static helper.
                    List<int> targetIds = (focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0)
                        ? focused.ObjectiveTargetIds
                        : new List<int> { focused.ObjectiveTargetId };
                    bool monitoringThis = this.questAssistantCollectMonitorActive
                        && this.questAssistantCollectMonitorTaskId == focused.TaskId;
                    string itemSummary = QuestAssistantSummarizeItemNames(targetIds);
                    return monitoringThis
                        ? ("Stop farming " + itemSummary)
                        : ("Enable radar for " + itemSummary + " + start farming");
                }
                case UguiQaActionTalkToNpc:
                {
                    // :348-351.
                    int npcStaticId = (focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0)
                        ? focused.ObjectiveTargetIds[0]
                        : focused.ObjectiveTargetId;
                    return "Talk to NPC #" + npcStaticId + " (auto-teleport)";
                }
                case UguiQaActionSubmitToNpc:
                    return "Complete via NPC #" + focused.SubmitNpcId + " — no teleport"; // :364
                case UguiQaActionCatchBird:
                {
                    // :373-374.
                    bool monitoringThis = this.questAssistantBirdMonitorActive
                        && this.questAssistantBirdMonitorTaskId == focused.TaskId;
                    return monitoringThis ? "Stop bird farm" : "Start bird farm (auto-catch + auto-exchange)";
                }
                case UguiQaActionCraft:
                    return "Craft recipe #" + focused.ObjectiveTargetId + " (remote, no Workbench trip)"; // :390-391
                case UguiQaActionGoToArea:
                    return "Teleport to area #" + focused.ObjectiveTargetId + " (Go there)"; // :400-401
                case UguiQaActionEnterArea:
                    return "Enter area #" + focused.ObjectiveTargetId + " (report arrival)"; // :410-411
                case UguiQaActionHomelandFarm:
                {
                    // :420-435 — the nested 2-way branch inside the ONE slot: sow-blocked shows a
                    // DISABLED explainer; otherwise one of the THREE labels by booster/fertilize.
                    bool fertilize = this.QuestAssistantActiveHomelandFarmIsFertilize(focused);
                    bool booster = fertilize
                        && this.QuestAssistantActiveHomelandFarmEffectType(focused) == HomelandFarmFertilizerEffectGrowthRate;
                    bool sowBlocked = !fertilize
                        && !this.QuestAssistantActiveSowSeedsAreCropSeeds(focused, out _, out _);
                    if (sowBlocked)
                    {
                        interactable = false;
                        return "Flower seeds — plant manually (auto-sow is crops-only)";
                    }
                    return booster
                        ? "Apply Growth Booster in radius"
                        : (fertilize ? "Fertilize crops in radius" : "Sow crops in radius");
                }
                case UguiQaActionPurchaseItem:
                    interactable = this.questAssistantPurchaseCoroutine == null; // :445 busy gate
                    return "Buy quest items from shop"; // :446
                case UguiQaActionCatchFish:
                    // :459-461.
                    return this.questAssistantFishMonitorActive
                        ? "Auto-fishing for this quest... (click to stop)"
                        : "Teleport to fishing spot & auto-fish";
                default:
                    return string.Empty;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Click handlers — every backend call is the source arm's own, verbatim
        // ----------------------------------------------------------------------------------------

        // ONE dispatcher for the 10 slots: re-resolves the focused snapshot and re-derives the
        // branch inputs at click time, and no-ops when the focused quest's kind no longer matches
        // the clicked slot (a one-frame stale click across a background snapshot swap — a race
        // the immediate-mode source could never see).
        private void OnUguiQuestAssistantActionClicked(int slotId)
        {
            try
            {
                QuestSnapshot focused = this.FindUguiQuestAssistantFocusedSnapshot();
                if (focused == null)
                {
                    return;
                }
                int expected = UguiQaActionSlotForKind(focused.ObjectiveKind);
                if (expected == UguiQaActionCatchFish && focused.ObjectiveAreaId <= 0)
                {
                    expected = -1; // :454 gate
                }
                if (expected != slotId)
                {
                    return;
                }

                switch (slotId)
                {
                    case UguiQaActionCollect:
                    {
                        // :327-343.
                        List<int> targetIds = (focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0)
                            ? focused.ObjectiveTargetIds
                            : new List<int> { focused.ObjectiveTargetId };
                        bool monitoringThis = this.questAssistantCollectMonitorActive
                            && this.questAssistantCollectMonitorTaskId == focused.TaskId;
                        if (monitoringThis)
                        {
                            this.QuestAssistantStopCollectMonitor("stopped manually");
                        }
                        else
                        {
                            this.QuestAssistantStartCollect(targetIds, focused.TaskId);
                        }
                        break;
                    }
                    case UguiQaActionTalkToNpc:
                    {
                        // :348-355.
                        int npcStaticId = (focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0)
                            ? focused.ObjectiveTargetIds[0]
                            : focused.ObjectiveTargetId;
                        this.QuestAssistantOnTalkToNpcClicked(focused, npcStaticId);
                        break;
                    }
                    case UguiQaActionSubmitToNpc:
                        this.QuestAssistantOnSubmitToNpcClicked(focused); // :367
                        break;
                    case UguiQaActionCatchBird:
                    {
                        // :373-385.
                        bool monitoringThis = this.questAssistantBirdMonitorActive
                            && this.questAssistantBirdMonitorTaskId == focused.TaskId;
                        if (monitoringThis)
                        {
                            this.QuestAssistantStopBirdMonitor("stopped manually");
                        }
                        else
                        {
                            this.QuestAssistantStartCatchBird(focused.TaskId);
                        }
                        break;
                    }
                    case UguiQaActionCraft:
                        this.QuestAssistantOnCraftClicked(focused, focused.ObjectiveTargetId); // :390-394
                        break;
                    case UguiQaActionGoToArea:
                        this.QuestAssistantOnGoToAreaClicked(focused, focused.ObjectiveTargetId); // :400-404
                        break;
                    case UguiQaActionEnterArea:
                        this.QuestAssistantOnEnterAreaClicked(focused, focused.ObjectiveTargetId); // :410-414
                        break;
                    case UguiQaActionHomelandFarm:
                    {
                        // :420-439 — the sow-blocked sub-state's button is disabled, so this is
                        // only reachable in the enabled branch; re-derive + guard anyway (the
                        // same one-frame staleness reasoning as the kind guard above).
                        bool fertilize = this.QuestAssistantActiveHomelandFarmIsFertilize(focused);
                        bool sowBlocked = !fertilize
                            && !this.QuestAssistantActiveSowSeedsAreCropSeeds(focused, out _, out _);
                        if (sowBlocked)
                        {
                            return;
                        }
                        this.QuestAssistantOnHomelandFarmClicked(focused, fertilize);
                        break;
                    }
                    case UguiQaActionPurchaseItem:
                        if (this.questAssistantPurchaseCoroutine != null)
                        {
                            return; // :445 busy gate (button already disabled; defensive)
                        }
                        this.QuestAssistantOnPurchaseItemClicked(focused); // :448
                        break;
                    case UguiQaActionCatchFish:
                        this.QuestAssistantOnCatchFishClicked(focused); // :464 — self-toggles on the monitor flag
                        break;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Quest Assistant action click error (slot " + slotId + "): " + ex.Message);
            }
        }

        // :191-194 — the method carries its own busy/min-interval guards (Daily Quests round note).
        private void OnUguiQuestAssistantRefreshClicked()
        {
            this.QuestAssistantOnDumpButtonClicked();
        }

        // :206-209 — internally guarded (coroutine + empty-list checks).
        private void OnUguiQuestAssistantAcceptAllClicked()
        {
            this.QuestAssistantOnAcceptAllClicked();
        }

        // :220-223 — the source reuses the daily-submit backend verbatim.
        private void OnUguiQuestAssistantSubmitItemsClicked()
        {
            this.StartDailyQuestAutoSubmitItems(false);
        }

        // :171-174 — hide the whole panel; the per-frame processor applies the visibility flip
        // next frame (single owner). Re-open from either menu's "Show Floating Window" button.
        private void OnUguiQuestAssistantCloseClicked()
        {
            this.QuestAssistantToggleWindow();
        }

        // :177-180 — collapse/expand toggle (works while collapsed too — it lives in the header).
        private void OnUguiQuestAssistantCollapseClicked()
        {
            UguiQuestAssistantWindowHandle handle = this.uguiQuestAssistantWindow;
            if (handle == null)
            {
                return;
            }
            try
            {
                this.SetUguiQuestAssistantWindowCollapsed(handle, !handle.Collapsed);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Quest Assistant collapse error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver — called from OnUpdate (HeartopiaComplete.cs), right after
        // ProcessUguiBuildingMovePanelOnUpdate, NOT inside ProcessUguiShellOnUpdate (that early-
        // returns until the shell is first built; this window must work with the shell never
        // opened).
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiQuestAssistantWindowOnUpdate()
        {
            try
            {
                // Visibility = questAssistantWindowVisible ALONE (file header): the IMGUI
                // DrawQuestAssistantWindow has NO showMenu/shell gate to replicate, so none is
                // invented here — both surfaces coexist during the soak-test period.
                bool show = this.questAssistantWindowVisible;

                UguiQuestAssistantWindowHandle handle = this.uguiQuestAssistantWindow;
                if (handle == null)
                {
                    if (!show || this.uguiQuestAssistantWindowBuildFailed)
                    {
                        return; // nothing to show, or already failed once this session
                    }
                    this.BuildUguiQuestAssistantWindow();
                    handle = this.uguiQuestAssistantWindow;
                    if (handle == null)
                    {
                        return;
                    }
                }

                if (handle.ErrorCount >= 3)
                {
                    return;
                }

                if (this.IsUguiWindowVisible(handle.Window) != show)
                {
                    this.SetUguiWindowVisible(handle.Window, show);
                }
                if (!show)
                {
                    return;
                }

                this.ProcessUguiWindowFrame(handle.Window); // narrowed title-strip drag (kit driver)

                // Phase 2e scale re-sync — compare the RAW GetUiScale() against the last pushed
                // value (SetUguiWindowScale logs unconditionally; only call on a real change).
                float targetScale = this.GetUiScale();
                if (!Mathf.Approximately(targetScale, handle.LastSyncedUiScale))
                {
                    handle.LastSyncedUiScale = targetScale;
                    this.SetUguiWindowScale(handle.Window, targetScale);
                }

                if (handle.Collapsed)
                {
                    return; // :182-187 — header-only while collapsed; drag stays live above
                }

                // :196-198 — the live status line.
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.questAssistantLastStatus ?? string.Empty);

                this.SyncUguiQuestAssistantConditionalRows(handle);
                this.SyncUguiQuestAssistantRows(handle);
                this.SyncUguiQuestAssistantDetail(handle);
            }
            catch (Exception ex)
            {
                UguiQuestAssistantWindowHandle handle = this.uguiQuestAssistantWindow;
                if (handle != null)
                {
                    handle.ErrorCount++;
                    ModLogger.Msg("[UguiShell] Quest Assistant window frame error (" + handle.ErrorCount
                        + "/3, disabled at 3): " + ex.Message);
                }
                else
                {
                    this.uguiQuestAssistantWindowBuildFailed = true;
                    ModLogger.Msg("[UguiShell] Quest Assistant window frame error (build disabled): " + ex.Message);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Theme-change rebuild — destroy + reconstruct (state-preserving: position/scale/
        // visibility via the kit capture, PLUS this window's own collapse state; the focus id and
        // every count live in backend fields and survive on their own).
        // ----------------------------------------------------------------------------------------

        private void RebuildUguiQuestAssistantWindowForTheme()
        {
            try
            {
                UguiQuestAssistantWindowHandle old = this.uguiQuestAssistantWindow;
                if (old == null)
                {
                    return; // never built — nothing stale
                }

                UguiWindowRestoreState state = this.CaptureUguiWindowState(old.Window);
                bool collapsed = old.Collapsed;
                float expandedHeight = old.ExpandedHeight;
                try
                {
                    if (old.Window != null && old.Window.Root != null)
                    {
                        Object.Destroy(old.Window.Root);
                    }
                }
                catch { }
                this.uguiQuestAssistantWindow = null;

                this.BuildUguiQuestAssistantWindow();
                UguiQuestAssistantWindowHandle fresh = this.uguiQuestAssistantWindow;
                if (fresh == null)
                {
                    ModLogger.Msg("[UguiShell] Quest Assistant window theme rebuild failed — window not recreated");
                    return;
                }

                // Re-apply the UGUI-side collapse state BEFORE restoring the captured position:
                // the captured anchoredPosition belongs to the OLD geometry, so the new window
                // must be at the same height for the restore to land where the old one sat.
                // Fields are set directly (not via the toggle helper) so the remembered expanded
                // height is never overwritten by the builder's default.
                fresh.ExpandedHeight = expandedHeight;
                fresh.Collapsed = collapsed;
                this.ApplyUguiQuestAssistantWindowHeight(fresh);
                SetUguiGoActive(fresh.BodyRoot, !collapsed);
                if (collapsed)
                {
                    fresh.CollapseLabelShown = "+";
                    this.SetUguiButtonLabel(fresh.CollapseButton, "+");
                }

                this.RestoreUguiWindowState(fresh.Window, state);
                ModLogger.Msg("[UguiShell] Quest Assistant window rebuilt for theme change");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Quest Assistant window theme rebuild error: " + ex.Message);
            }
        }
    }
}
