using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, item 5 (migration plan: cosmic-waddling-rainbow.md):
    // Bag / Warehouse. First real consumer of the kit's virtualized icon-grid primitive
    // (CreateUguiVirtualGrid / UpdateUguiVirtualGridAssignments, HeartopiaComplete.UguiKit.cs) —
    // the plan builds that primitive HERE (standalone tab, smallest blast radius), not cold
    // inside Automation later. The Auto Sell tab's independent hand-rolled copy of the same grid
    // math (HeartopiaComplete.AutoSell.cs:271-286) is deliberately untouched this round.
    //
    // Ground rules (same as every previous round):
    //  - The IMGUI drawer (DrawBulkSelectorTab, HeartopiaComplete.Gui.cs:1305) and every backend
    //    method it calls (HeartopiaComplete.Transfer.cs) stay fully functional and untouched —
    //    this file only READS the same transfer* fields and CALLS the same action methods.
    //  - Wiring is by STATIC display-position index (UguiShellBagWarehouseTabIndex = 6, declared
    //    next to its siblings in UguiPhase3Content.cs) in BuildUguiShell's no-subs branch.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Virtualization (the round's actual point, not just visual parity):
    //  - A FIXED pool of cell GameObjects sized to the LARGEST grid viewport this layout allows
    //    (poolRows = Ceil(viewportH / cellH) + 1 buffer row, poolSize = poolRows * 6 columns —
    //    IMGUI's own visibleRowCount formula), built ONCE at construction.
    //  - Content height = rows * cellH via the grid core, so the ScrollRect range matches the
    //    full data set while only poolSize cells exist.
    //  - Each gated frame the core polls the content's anchoredPosition, recomputes
    //    firstVisibleIndex exactly like IMGUI, and repositions/reassigns slots. Cells are NEVER
    //    destroyed or recreated during scrolling or on selection change — a per-slot signature
    //    (BoundIndex + entry fields + selection/batch/qty/hold state) decides which slots rebind;
    //    everything else is untouched. A selection click therefore rebinds exactly the affected
    //    cells (old selected + new selected), not the pool.
    //  - A full data reload (Scan) changes the list reference/count: the same assignment +
    //    signature pass re-covers every slot from index 0 — still zero Destroy/Instantiate.
    //
    // The −/+ stepper hold-repeat (IMGUI: DrawTransferQtyStepButton/BeginTransferQtyHold/
    // UpdateTransferQtyHoldRepeat) is rebuilt here with its OWN state fields — the IMGUI
    // functions are hard-gated on the old menu (!showMenu || selectedTab != 6 cancels), so
    // sharing transferQtyHold* would let the two systems fight. Thresholds are the SAME shared
    // constants (TransferQtyHoldRepeatDelay 0.5s → TransferQtyHoldSlowInterval 0.1s →
    // TransferQtyHoldFastInterval 0.05s after TransferQtyHoldFastAfterSeconds 1s). The steppers
    // are deliberately NOT kit Buttons: Button.onClick fires on pointer UP, but IMGUI steps on
    // MouSE DOWN and then auto-repeats — so press detection is a polled
    // Input.GetMouseButtonDown(0) + RectangleContainsScreenPoint(3-arg, null camera) hit test
    // (the established no-ClassInjector idiom), and the repeat mirrors IMGUI's clone gates plus
    // one pool-specific extra: the hold cancels if the pooled cell is rebound to a different
    // data index underneath it (scrolled away).
    //
    // Deliberate deviations from the IMGUI drawer (established precedents, do not "fix"):
    //  - The source dropdown is the kit's real CreateUguiDropdown instead of IMGUI's hand-rolled
    //    popup rows (Settings→Main precedent); its full-screen blocker also stands in for
    //    IMGUI's "GUI.enabled = !dropdownOpen" around Scan/Transfer. Poll fallback when
    //    UnityEvent<int> wiring reports failure.
    //  - Multi / Full stack render as kit CHECKBOXES, not IMGUI-style sliding switches
    //    (CreateUguiSwitch's visuals ignore SetIsOnWithoutNotify — round-2 precedent).
    //  - The grid viewport uses the full remaining block height instead of IMGUI's 276px cap
    //    (the "really scrolls" LIVE-rail precedent).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Fixed layout rows above the dynamic region (block-local Y, mirrors the IMGUI y-cursor):
        // title 12..34, subtitle 38..54, selected card 58..136, source row 146..180 (buttons
        // 143..177), toggles 186..210; dynamic content starts at 220 (IMGUI: toggleRowY + 34).
        private const float UguiTransferDynamicTopY = 220f;
        private const float UguiTransferCellH = 100f;   // IMGUI cellH
        private const int UguiTransferColumns = 6;      // IMGUI columns

        private sealed class UguiTransferCellWidgets
        {
            public Image Bg;
            public Image Ring;               // AddUguiRingOverlay's "Ring" child (may be null)
            public Button CellButton;        // whole-cell click — enabled only while NOT selected
            public GameObject IconZone;      // selected-state click zones (IMGUI iconSelectRect /
            public GameObject NameZone;      //  nameSelectRect — the stepper strip must not select)
            public Image Icon;
            public GameObject InitialsLabel;
            public GameObject StackBadge;
            public GameObject StarBadge;
            public GameObject NameLabel;
            public GameObject QtyLabel;
            public GameObject MinusBtn;      // plain image+label, poll-driven (see file header)
            public Image MinusBg;
            public GameObject PlusBtn;
            public Image PlusBg;
            public bool HasIcon;

            // Last-bound signature — compared field-by-field each gated frame; a mismatch (data
            // change, recycling via BoundIndex, selection/batch/qty/hold change) rebinds JUST
            // this slot. SigIndex == int.MinValue forces a rebind on next sync.
            public int SigIndex = int.MinValue;
            public uint SigNetId;
            public int SigCount;
            public int SigStar;
            public bool SigLocked;
            public bool SigSelected;
            public bool SigInBatch;
            public bool SigShowPick;
            public int SigPickQty;
            public int SigHeldDir;
            public string SigName;
        }

        private sealed class UguiShellBagWarehouseHandle
        {
            public GameObject Root;
            public float BlockW;
            public float BlockH;

            // Chrome
            public GameObject Subtitle;
            public Image SelectedIcon;
            public GameObject SelectedIconFallback;   // the "?" label
            public GameObject SelectedTitle;
            public GameObject SelectedMeta;
            public Dropdown SourceDropdown;
            public bool SourceListenerWired;
            public int SourceLastValue;               // poll-fallback change detection
            public Toggle MultiToggle;
            public Toggle FullStackToggle;
            public GameObject BatchBar;
            public GameObject BatchLabel;
            public GameObject StatusCard;
            public GameObject StatusLabel;
            public GameObject GridCountLabel;
            public GameObject EmptyLabel;             // both empty-state strings share this label

            // Grid
            public UguiVirtualGridHandle Grid;
            public readonly List<UguiTransferCellWidgets> Cells = new List<UguiTransferCellWidgets>();

            // Change guards for the text tick
            public string ShownSubtitle;
            public string ShownSelTitle;
            public string ShownSelMeta;
            public bool ShownSelHasIcon;
            public int ShownSelIconTexId;
            public string ShownStatus;
            public string ShownBatch;
            public string ShownCount;
            public string ShownEmpty;

            public int LayoutSignature = -1;
            public float NextTextSyncAt;              // 0.25s tick (status is primary feedback here)
            public float NextIconRetryAt;             // 0.5s tick — async icon loads land later
            public int ErrorCount;                    // per-frame sync disabled at 3 (LIVE rail idiom)

            // Theme snapshot (the shell rebuild re-runs the builder on theme change).
            public Color CellNormalFill;
            public Color CellActiveFill;              // themeTopTabActiveStyle fill: accent @ 0.18
            public Color RingNormal;                  // DrawCardOutline edge
            public Color RingBatch;                   // themeTopTabActiveStyle ring: accent @ 0.35
            public Color RingSelected;                // stands in for IMGUI's thickness-2 outline
            public Color TextColor;
            public Color AccentColor;
            public Color ControlFill;
            public Color StarColor;
            public Color HeldTint;                    // IMGUI hold tint (0.35, 0.55, 0.65)
        }

        private UguiShellBagWarehouseHandle uguiShellBagWarehouse;

        // Own hold state — NEVER the IMGUI transferQtyHold* fields (see file header).
        private int uguiTransferQtyHoldDirection;
        private int uguiTransferQtyHoldSlot = -1;
        private int uguiTransferQtyHoldItemIndex = -1;
        private uint uguiTransferQtyHoldNetId;
        private float uguiTransferQtyHoldStartedAt;
        private float uguiTransferQtyHoldLastStepAt;

        // Texture2D → Sprite wrappers for the kit's Image cells (same Sprite.Create shape as
        // TryGetUguiIconSprite). Keyed by texture instance id; textures come from the AutoSell
        // icon cache so instances are stable. Survives theme rebuilds on purpose.
        private readonly Dictionary<int, Sprite> uguiTransferSpriteCache = new Dictionary<int, Sprite>();

        // ----------------------------------------------------------------------------------------
        // Gate
        // ----------------------------------------------------------------------------------------

        // True while the UGUI shell is visible and showing the Bag/Warehouse tab (the Research
        // gate shape — this tab has no sub-tabs, so no sub-bar check).
        private bool IsUguiShellBagWarehouseTabActive()
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                return shell != null && shell.ActiveIndex == UguiShellBagWarehouseTabIndex
                    && this.IsUguiWindowVisible(shell.Window);
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawBulkSelectorTab: title + direction subtitle, selected-stack card,
        // source dropdown + Scan/Transfer, Multi/Full-stack toggles, conditional batch bar,
        // status card, then the virtualized grid (or one of the two empty-state lines). Assigns
        // the handle field LAST (Research idiom) so a mid-build exception can never leave a
        // half-built handle syncing.
        private GameObject BuildUguiShellBagWarehouseContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellBagWarehouse = null;
            this.ClearUguiTransferQtyHold();

            UguiShellBagWarehouseHandle handle = new UguiShellBagWarehouseHandle();
            handle.BlockW = w;
            handle.BlockH = h;

            // Theme snapshot — IMGUI style sources named per field.
            Color accent = this.UguiKitAccent();
            handle.CellNormalFill = this.UguiKitContentBg();                       // themeContentStyle
            handle.CellActiveFill = new Color(accent.r, accent.g, accent.b, 0.18f); // themeTopTabActiveStyle fill
            handle.RingNormal = new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));   // DrawCardOutline edge
            handle.RingBatch = new Color(accent.r, accent.g, accent.b, 0.35f);     // themeTopTabActiveStyle ring
            handle.RingSelected = new Color(accent.r, accent.g, accent.b, 0.8f);   // thickness-2 outline stand-in
            handle.TextColor = this.UguiKitTextColor();
            handle.AccentColor = accent;
            handle.ControlFill = this.UguiKitControlFill();
            handle.StarColor = new Color(1f, 0.86f, 0.36f);                        // starBadgeStyle
            handle.HeldTint = new Color(0.35f, 0.55f, 0.65f);                      // DrawTransferQtyStepButton hold tint

            GameObject block = this.CreateUguiGo("BagWarehouseContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            float innerW = w - pad * 2f;
            Color muted = this.UguiKitMutedColor();

            GameObject title = this.CreateUguiHeaderLabel(block.transform, "Title", this.L("Bag / Warehouse"), 15f);
            PlaceUguiTopLeft(title, pad, 12f, innerW, 22f);

            handle.Subtitle = this.CreateUguiMutedLabel(block.transform, "Subtitle", "", 11f);
            PlaceUguiTopLeft(handle.Subtitle, pad, 38f, innerW, 16f);

            // Selected-stack card (IMGUI: themePanelStyle box + DrawCardOutline → PanelBg + ring).
            GameObject card = this.CreateUguiGo("SelectedCard", block.transform);
            PlaceUguiTopLeft(card, pad, 58f, innerW, 78f);
            this.AddUguiImage(card, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(card, handle.RingNormal, 1f);

            GameObject iconBox = this.CreateUguiGo("IconBox", card.transform);
            PlaceUguiTopLeft(iconBox, 12f, 12f, 54f, 54f);
            this.AddUguiImage(iconBox, this.UguiKitContentBg(), true, 1.5f);
            this.AddUguiRingOverlay(iconBox, handle.RingNormal, 1.5f);

            GameObject selIconGo = this.CreateUguiGo("Icon", iconBox.transform);
            PlaceUguiTopLeft(selIconGo, 5f, 5f, 44f, 44f);
            handle.SelectedIcon = selIconGo.AddComponent<Image>();
            handle.SelectedIcon.raycastTarget = false;
            try { handle.SelectedIcon.preserveAspect = true; } catch { }
            selIconGo.SetActive(false);

            handle.SelectedIconFallback = this.CreateUguiLabel(iconBox.transform, "Fallback", "?", 20f, handle.TextColor, true);
            this.TrySetUguiLabelBold(handle.SelectedIconFallback);
            StretchUguiFill(handle.SelectedIconFallback, 0f, 0f, 0f, 0f);

            handle.SelectedTitle = this.CreateUguiBodyLabel(card.transform, "SelTitle", "", 12f);
            this.TrySetUguiLabelBold(handle.SelectedTitle);
            PlaceUguiTopLeft(handle.SelectedTitle, 78f, 10f, innerW - 90f, 22f);
            handle.SelectedMeta = this.CreateUguiMutedLabel(card.transform, "SelMeta", "", 11f);
            this.TrySetUguiLabelWrapped(handle.SelectedMeta);
            PlaceUguiTopLeft(handle.SelectedMeta, 78f, 34f, innerW - 90f, 36f);

            // Source row: dropdown + Scan + Transfer (IMGUI x layout verbatim: dropdown 120x28 at
            // the row top, buttons 130x34 starting 12px right of it, 8px apart, 3px above).
            handle.SourceLastValue = Mathf.Clamp(this.transferScanSource, 0, this.transferScanSourceLabels.Length - 1);
            bool sourceWired;
            handle.SourceDropdown = this.CreateUguiDropdown(block.transform, "SourceDropdown",
                this.transferScanSourceLabels, handle.SourceLastValue,
                new System.Action<int>(this.OnUguiTransferSourcePicked), out sourceWired);
            handle.SourceListenerWired = sourceWired;
            PlaceUguiTopLeft(handle.SourceDropdown.gameObject, pad, 146f, 120f, 28f);

            float buttonsX = pad + 120f + 12f;
            GameObject scanBtn = this.CreateUguiSecondaryButton(block.transform, "ScanButton",
                this.L("Scan Items"), new System.Action(this.OnUguiTransferScanClicked));
            PlaceUguiTopLeft(scanBtn, buttonsX, 143f, 130f, 34f);
            GameObject transferBtn = this.CreateUguiPrimaryButton(block.transform, "TransferButton",
                this.L("Transfer"), new System.Action(this.OnUguiTransferTransferClicked));
            PlaceUguiTopLeft(transferBtn, buttonsX + 130f + 8f, 143f, 130f, 34f);

            // Multi / Full stack (DrawSwitchToggle localizes its labels, so this does too).
            float toggleW = (innerW - 12f) * 0.5f;
            handle.MultiToggle = this.CreateUguiCheckbox(block.transform, "MultiToggle",
                this.L("Multi"), this.transferMultiSelectMode,
                new System.Action<bool>(this.OnUguiTransferMultiChanged));
            PlaceUguiTopLeft(handle.MultiToggle.gameObject, pad, 186f, toggleW, 24f);
            handle.FullStackToggle = this.CreateUguiCheckbox(block.transform, "FullStackToggle",
                this.L("Full stack"), this.transferSelectFullStack,
                new System.Action<bool>(this.OnUguiTransferFullStackChanged));
            PlaceUguiTopLeft(handle.FullStackToggle.gameObject, pad + toggleW + 12f, 186f, toggleW, 24f);

            // Batch bar (conditional — positioned by relayout). "Clear batch" is raw text in the
            // IMGUI drawer (no L()), so it stays raw here.
            handle.BatchBar = this.CreateUguiGo("BatchBar", block.transform);
            this.AddUguiImage(handle.BatchBar, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(handle.BatchBar, handle.RingNormal, 1f);
            handle.BatchLabel = this.CreateUguiBodyLabel(handle.BatchBar.transform, "Label", "", 12f);
            this.TrySetUguiLabelBold(handle.BatchLabel);
            PlaceUguiTopLeft(handle.BatchLabel, 12f, 8f, innerW - 140f, 20f);
            GameObject clearBatchBtn = this.CreateUguiSecondaryButton(handle.BatchBar.transform, "ClearBatch",
                "Clear batch", new System.Action(this.OnUguiTransferClearBatchClicked));
            PlaceUguiTopLeft(clearBatchBtn, innerW - 118f, 5f, 106f, 24f);
            handle.BatchBar.SetActive(false);

            // Status card.
            handle.StatusCard = this.CreateUguiGo("StatusCard", block.transform);
            this.AddUguiImage(handle.StatusCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(handle.StatusCard, handle.RingNormal, 1f);
            handle.StatusLabel = this.CreateUguiLabel(handle.StatusCard.transform, "Label", "", 11f, muted, false);
            PlaceUguiTopLeft(handle.StatusLabel, 12f, 12f, innerW - 24f, 20f);

            handle.GridCountLabel = this.CreateUguiBodyLabel(block.transform, "GridCount", "", 12f);
            this.TrySetUguiLabelBold(handle.GridCountLabel);
            handle.GridCountLabel.SetActive(false);

            handle.EmptyLabel = this.CreateUguiBodyLabel(block.transform, "EmptyState", "", 12f);

            // The grid. Pool is sized for the LARGEST viewport this layout allows — the batch bar
            // hidden (its 40px comes out of the grid when visible; extra slots then simply bind
            // one row past the mask, which the +1 buffer row already accounts for).
            float gridTopNoBatch = UguiTransferDynamicTopY + 52f + 24f; // status card + grid label
            float poolViewportH = Mathf.Max(UguiTransferCellH, h - gridTopNoBatch - 8f);
            handle.Grid = this.CreateUguiVirtualGrid(block.transform, "Grid", w - 16f,
                UguiTransferColumns, UguiTransferCellH, poolViewportH);
            handle.Grid.Root.SetActive(false);
            for (int k = 0; k < handle.Grid.Slots.Count; k++)
            {
                handle.Cells.Add(this.BuildUguiTransferCell(handle, handle.Grid.Slots[k], k));
            }

            // Populate immediately (no empty first frame).
            handle.LayoutSignature = this.ComputeUguiTransferLayoutSignature();
            this.RelayoutUguiShellBagWarehouse(handle);
            this.SyncUguiTransferTexts(handle);
            this.SyncUguiTransferGrid(handle);

            handle.Root = block;
            this.uguiShellBagWarehouse = handle;
            return block;
        }

        // One pooled cell's visuals — built ONCE per slot, then only rebound. Positions are
        // cell-local mirrors of the IMGUI cell rects (iconRect, badges, name, stepper strip).
        private UguiTransferCellWidgets BuildUguiTransferCell(UguiShellBagWarehouseHandle handle,
            UguiVirtualGridSlot slot, int slotIndex)
        {
            UguiTransferCellWidgets w = new UguiTransferCellWidgets();
            GameObject root = slot.Root;
            float cellW = handle.Grid.CellW - 8f;   // inner cell (IMGUI: cellW-8 x cellH-8)

            w.Bg = this.AddUguiImage(root, handle.CellNormalFill, true, 1.5f);
            w.Bg.raycastTarget = true;
            w.CellButton = root.AddComponent<Button>();
            w.CellButton.targetGraphic = w.Bg;
            // No ColorTint transition: the bind pass owns cell colors exactly (IMGUI's
            // GUIStyle.none buttons have no hover/press visuals either).
            w.CellButton.transition = Selectable.Transition.None;
            int slotCopy = slotIndex; // capture a copy for the click closures
            w.CellButton.onClick.AddListener(new System.Action(() => this.OnUguiTransferCellClicked(slotCopy)));

            this.AddUguiRingOverlay(root, handle.RingNormal, 1.5f);
            Transform ringT = root.transform.Find("Ring");
            w.Ring = (ringT != null) ? ringT.GetComponent<Image>() : null;

            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            PlaceUguiTopLeft(iconGo, (cellW - 42f) * 0.5f, 14f, 42f, 36f);
            w.Icon = iconGo.AddComponent<Image>();
            w.Icon.raycastTarget = false;
            try { w.Icon.preserveAspect = true; } catch { }
            iconGo.SetActive(false);

            w.InitialsLabel = this.CreateUguiLabel(root.transform, "Initials", "", 14f, handle.TextColor, true);
            this.TrySetUguiLabelBold(w.InitialsLabel);
            PlaceUguiTopLeft(w.InitialsLabel, (cellW - 42f) * 0.5f, 14f, 42f, 36f);
            w.InitialsLabel.SetActive(false);

            w.StackBadge = this.CreateUguiLabel(root.transform, "Stack", "", 9f, Color.white, false);
            this.TrySetUguiLabelBold(w.StackBadge);
            PlaceUguiTopLeft(w.StackBadge, 4f, 3f, 36f, 18f);
            w.StackBadge.SetActive(false);

            w.StarBadge = this.CreateUguiLabel(root.transform, "Star", "", 9f, handle.StarColor, false);
            this.TrySetUguiLabelBold(w.StarBadge);
            this.TrySetUguiLabelRightAligned(w.StarBadge);
            PlaceUguiTopLeft(w.StarBadge, cellW - 36f, 3f, 32f, 18f);
            w.StarBadge.SetActive(false);

            w.NameLabel = this.CreateUguiLabel(root.transform, "Name", "", 9f, handle.TextColor, true);
            this.TrySetUguiTransferNameWrapped(w.NameLabel);
            PlaceUguiTopLeft(w.NameLabel, 3f, 68f, cellW - 6f, 28f);

            w.QtyLabel = this.CreateUguiLabel(root.transform, "Qty", "", 11f, handle.AccentColor, true);
            this.TrySetUguiLabelBold(w.QtyLabel);
            PlaceUguiTopLeft(w.QtyLabel, (cellW - 22f) * 0.5f, 50f, 22f, 16f);
            w.QtyLabel.SetActive(false);

            w.MinusBtn = this.CreateUguiTransferStepButton(root.transform, "Minus", "-", handle, out w.MinusBg);
            PlaceUguiTopLeft(w.MinusBtn, 5f, 50f, 18f, 16f);
            w.MinusBtn.SetActive(false);
            w.PlusBtn = this.CreateUguiTransferStepButton(root.transform, "Plus", "+", handle, out w.PlusBg);
            PlaceUguiTopLeft(w.PlusBtn, cellW - 23f, 50f, 18f, 16f);
            w.PlusBtn.SetActive(false);

            // Selected-state click zones. IMGUI deliberately shrinks the clickable area to the
            // icon and name strips while a cell is selected, so a click near the qty readout can
            // never re-run SelectTransferTile (which would reset an edited quantity).
            w.IconZone = this.CreateUguiTransferClickZone(root.transform, "IconZone", slotCopy);
            PlaceUguiTopLeft(w.IconZone, 4f, 10f, cellW - 8f, 38f);
            w.IconZone.SetActive(false);
            w.NameZone = this.CreateUguiTransferClickZone(root.transform, "NameZone", slotCopy);
            PlaceUguiTopLeft(w.NameZone, 3f, 66f, cellW - 6f, 30f);
            w.NameZone.SetActive(false);

            return w;
        }

        // Stepper visual: control-fill pill + centered bold label. NO Button component on purpose
        // (press must fire on mouse DOWN + auto-repeat — see the hold notes in the file header);
        // the raycastable Image still blocks the click zones/cell underneath.
        private GameObject CreateUguiTransferStepButton(Transform parent, string name, string label,
            UguiShellBagWarehouseHandle handle, out Image bg)
        {
            GameObject go = this.CreateUguiGo(name, parent);
            bg = this.AddUguiImage(go, handle.ControlFill, true, 2.5f);
            bg.raycastTarget = true;
            GameObject lbl = this.CreateUguiLabel(go.transform, "Label", label, 12f, Color.white, true);
            this.TrySetUguiLabelBold(lbl);
            StretchUguiFill(lbl, 0f, 0f, 0f, 0f);
            return go;
        }

        // Invisible click zone routing to the shared cell-click handler.
        private GameObject CreateUguiTransferClickZone(Transform parent, string name, int slotIndex)
        {
            GameObject go = this.CreateUguiGo(name, parent);
            Image hit = this.AddUguiImage(go, new Color(0f, 0f, 0f, 0f), false, 1f);
            hit.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            int slotCopy = slotIndex;
            btn.onClick.AddListener(new System.Action(() => this.OnUguiTransferCellClicked(slotCopy)));
            return go;
        }

        // Cell name parity: IMGUI itemStyle is UpperCenter + wordWrap. The kit's wrap helper is
        // top-LEFT (About bodies), so this variant keeps the horizontal centering.
        private void TrySetUguiTransferNameWrapped(GameObject label)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.enableWordWrapping = true;
                    tmp.alignment = TextAlignmentOptions.Top;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txt.alignment = TextAnchor.UpperCenter;
                }
            }
            catch { }
        }

        private Sprite GetOrCreateUguiTransferSprite(Texture2D tex)
        {
            if (tex == null)
            {
                return null;
            }
            try
            {
                int id = tex.GetInstanceID();
                Sprite cached;
                if (this.uguiTransferSpriteCache.TryGetValue(id, out cached) && cached != null)
                {
                    return cached;
                }
                Sprite sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    Vector4.zero);
                if (sprite != null)
                {
                    sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    this.uguiTransferSpriteCache[id] = sprite;
                }
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Layout (the dynamic region below the toggles — batch bar / status / grid / empty states)
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiTransferLayoutSignature()
        {
            bool scanned = this.transferItems != null;
            bool hasItems = scanned && this.transferItems.Count > 0;
            return (this.transferBatch.Count > 0 ? 1 : 0) | (scanned ? 2 : 0) | (hasItems ? 4 : 0);
        }

        // Reposition/SetActive only — nothing is rebuilt (the Settings→Main relayout idiom, which
        // is itself the UGUI analog of the IMGUI drawer's y-cursor accumulation).
        private void RelayoutUguiShellBagWarehouse(UguiShellBagWarehouseHandle handle)
        {
            bool batchVisible = this.transferBatch.Count > 0;
            bool scanned = this.transferItems != null;
            bool hasItems = scanned && this.transferItems.Count > 0;

            float w = handle.BlockW;
            float h = handle.BlockH;
            float innerW = w - 32f;
            float yCur = UguiTransferDynamicTopY;

            SetUguiGoActive(handle.BatchBar, batchVisible);
            if (batchVisible)
            {
                PlaceUguiTopLeft(handle.BatchBar, 16f, yCur, innerW, 34f);
                yCur += 40f;
            }

            PlaceUguiTopLeft(handle.StatusCard, 16f, yCur, innerW, 44f);
            yCur += 52f;

            SetUguiGoActive(handle.GridCountLabel, hasItems);
            SetUguiGoActive(handle.Grid != null ? handle.Grid.Root : null, hasItems);
            SetUguiGoActive(handle.EmptyLabel, !hasItems);
            if (hasItems)
            {
                PlaceUguiTopLeft(handle.GridCountLabel, 16f, yCur, innerW, 22f);
                yCur += 24f;
                PlaceUguiTopLeft(handle.Grid.Root, 8f, yCur, w - 16f, Mathf.Max(60f, h - yCur - 8f));
            }
            else
            {
                PlaceUguiTopLeft(handle.EmptyLabel, 16f, yCur, innerW, 24f);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Action handlers — each mirrors its IMGUI block EXACTLY
        // ----------------------------------------------------------------------------------------

        // Shared by the wired listener AND the poll fallback. IMGUI option click: set the source,
        // close the popup (N/A here), clear the scanned list + selection + batch.
        private void OnUguiTransferSourcePicked(int index)
        {
            UguiShellBagWarehouseHandle handle = this.uguiShellBagWarehouse;
            if (handle != null)
            {
                handle.SourceLastValue = index;
            }
            if (index < 0 || index >= this.transferScanSourceLabels.Length || index == this.transferScanSource)
            {
                return;
            }
            this.transferScanSource = index;
            this.transferItems = null;
            this.selectedTransferIndex = -1;
            this.transferBatch.Clear();
            this.RefreshUguiTransferAfterAction(handle);
        }

        // IMGUI "Scan Items" (Gui.cs:1368-1373).
        private void OnUguiTransferScanClicked()
        {
            this.transferItems = this.ScanTransferItems();
            this.selectedTransferIndex = -1;
            this.transferBatch.Clear();
            this.RefreshUguiTransferAfterAction(this.uguiShellBagWarehouse);
        }

        // IMGUI "Transfer" (Gui.cs:1374-1385) — the chunk-vs-direct branch verbatim.
        private void OnUguiTransferTransferClicked()
        {
            Dictionary<uint, int> pendingMap = this.BuildTransferItemMapForSend(out _);
            if (pendingMap != null && pendingMap.Count > TransferBatchMaxCount)
            {
                this.ExecuteTransferItemsChunked();
            }
            else
            {
                this.ExecuteTransferItems();
            }
            this.RefreshUguiTransferAfterAction(this.uguiShellBagWarehouse);
        }

        // IMGUI Multi switch (Gui.cs:1397-1402) — any change clears the batch.
        private void OnUguiTransferMultiChanged(bool value)
        {
            if (value == this.transferMultiSelectMode)
            {
                return;
            }
            this.transferMultiSelectMode = value;
            this.transferBatch.Clear();
            this.RefreshUguiTransferAfterAction(this.uguiShellBagWarehouse);
        }

        // IMGUI Full stack switch (Gui.cs:1403) — flag only, no side effects.
        private void OnUguiTransferFullStackChanged(bool value)
        {
            if (value == this.transferSelectFullStack)
            {
                return;
            }
            this.transferSelectFullStack = value;
        }

        // IMGUI "Clear batch" (Gui.cs:1412-1416).
        private void OnUguiTransferClearBatchClicked()
        {
            this.transferBatch.Clear();
            this.transferStatus = "Batch cleared";
            this.RefreshUguiTransferAfterAction(this.uguiShellBagWarehouse);
        }

        // Whole-cell / icon-zone / name-zone click: resolve the slot's CURRENT bound index and
        // hand off to the one existing selection method (it owns single vs multi/batch semantics).
        private void OnUguiTransferCellClicked(int slotIndex)
        {
            try
            {
                UguiShellBagWarehouseHandle handle = this.uguiShellBagWarehouse;
                if (handle == null || handle.Grid == null || slotIndex < 0 || slotIndex >= handle.Grid.Slots.Count)
                {
                    return;
                }
                int index = handle.Grid.Slots[slotIndex].BoundIndex;
                if (this.transferItems == null || index < 0 || index >= this.transferItems.Count)
                {
                    return;
                }
                TransferItemEntry entry = this.transferItems[index];
                if (entry == null || entry.IsLocked)
                {
                    return; // IMGUI only wires clicks for unlocked cells
                }
                this.SelectTransferTile(entry, index);
                this.RefreshUguiTransferAfterAction(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Bag/Warehouse cell click error: " + ex.Message);
            }
        }

        // Same-frame refresh after a user action (the per-frame processor would catch up a frame
        // later anyway — this just keeps clicks snappy). Everything inside is change-guarded.
        private void RefreshUguiTransferAfterAction(UguiShellBagWarehouseHandle handle)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                int signature = this.ComputeUguiTransferLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellBagWarehouse(handle);
                }
                this.SyncUguiTransferTexts(handle);
                this.SyncUguiTransferGrid(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Bag/Warehouse refresh error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame processor (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellBagWarehouseOnUpdate()
        {
            UguiShellBagWarehouseHandle handle = this.uguiShellBagWarehouse;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3)
            {
                return;
            }
            if (!this.IsUguiShellBagWarehouseTabActive())
            {
                // IMGUI's UpdateTransferQtyHoldRepeat cancels when its menu/tab goes away; ours
                // must too, or a hold could resume when the tab is next shown.
                if (this.uguiTransferQtyHoldDirection != 0)
                {
                    this.ClearUguiTransferQtyHold();
                }
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure.
                if (!handle.SourceListenerWired && handle.SourceDropdown != null)
                {
                    int v = handle.SourceDropdown.value;
                    if (v != handle.SourceLastValue)
                    {
                        this.OnUguiTransferSourcePicked(v); // updates SourceLastValue itself
                    }
                }

                // Cross-surface toggle re-sync (fields are also editable from the IMGUI tab).
                this.SyncUguiToggleFromField(handle.MultiToggle, this.transferMultiSelectMode);
                this.SyncUguiToggleFromField(handle.FullStackToggle, this.transferSelectFullStack);

                // Conditional layout (batch bar / grid vs empty states).
                int signature = this.ComputeUguiTransferLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellBagWarehouse(handle);
                    this.SyncUguiTransferTexts(handle); // a state flip must not show 0.25s-stale text
                }

                // Stepper press/hold BEFORE the grid sync so a step's qty change renders same-frame.
                this.ProcessUguiTransferQtyHold(handle);

                // The virtualized grid: scroll poll + slot assignment + per-slot signature rebinds.
                this.SyncUguiTransferGrid(handle);

                if (Time.unscaledTime >= handle.NextTextSyncAt)
                {
                    handle.NextTextSyncAt = Time.unscaledTime + 0.25f;
                    this.SyncUguiTransferTexts(handle);
                }
                if (Time.unscaledTime >= handle.NextIconRetryAt)
                {
                    handle.NextIconRetryAt = Time.unscaledTime + 0.5f;
                    this.RetryUguiTransferIcons(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Bag/Warehouse content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Text refresh (0.25s tick + after actions/relayout — every write change-guarded)
        // ----------------------------------------------------------------------------------------

        private void SyncUguiTransferTexts(UguiShellBagWarehouseHandle handle)
        {
            // Subtitle — DrawBulkSelectorTab's direction line verbatim (not localized there).
            int sourceStorage = this.GetTransferScanStorageType();
            string destLabel = this.GetTransferTargetStorageType(sourceStorage) == 2 ? "Warehouse" : "Bag";
            string subtitle = "Transfer via BackPackSystem API (no bag UI). Direction: "
                + this.GetTransferScanSourceLabel() + " -> " + destLabel;
            if (!string.Equals(subtitle, handle.ShownSubtitle, StringComparison.Ordinal))
            {
                handle.ShownSubtitle = subtitle;
                this.SetUguiLabelText(handle.Subtitle, subtitle);
            }

            // Selected-stack card — title/meta conditional strings verbatim.
            TransferItemEntry selectedEntry = this.GetSelectedTransferItemEntry();
            string selectedTitle = selectedEntry != null ? selectedEntry.DisplayName : "No stack selected";
            string selectedMeta = selectedEntry != null
                ? ("netId=" + selectedEntry.NetId + "  qty=" + Math.Max(1, selectedEntry.Count)
                    + (selectedEntry.IsLocked ? "  LOCKED" : "")
                    + (selectedEntry.StaticId > 0 ? ("  id=" + selectedEntry.StaticId) : ""))
                : (this.transferBatch.Count > 0
                    ? ("Batch: " + this.transferBatch.Count + " stack(s)")
                    : "Scan and pick a stack");
            if (!string.Equals(selectedTitle, handle.ShownSelTitle, StringComparison.Ordinal))
            {
                handle.ShownSelTitle = selectedTitle;
                this.SetUguiLabelText(handle.SelectedTitle, selectedTitle);
            }
            if (!string.Equals(selectedMeta, handle.ShownSelMeta, StringComparison.Ordinal))
            {
                handle.ShownSelMeta = selectedMeta;
                this.SetUguiLabelText(handle.SelectedMeta, selectedMeta);
            }

            Texture2D selTex = null;
            if (selectedEntry != null)
            {
                this.TryGetTransferItemTexture(selectedEntry, out selTex);
            }
            Sprite selSprite = (selTex != null) ? this.GetOrCreateUguiTransferSprite(selTex) : null;
            bool showSelIcon = selSprite != null;
            int selTexId = showSelIcon ? selTex.GetInstanceID() : 0;
            if (showSelIcon != handle.ShownSelHasIcon || selTexId != handle.ShownSelIconTexId)
            {
                handle.ShownSelHasIcon = showSelIcon;
                handle.ShownSelIconTexId = selTexId;
                if (handle.SelectedIcon != null)
                {
                    if (showSelIcon)
                    {
                        handle.SelectedIcon.sprite = selSprite;
                    }
                    SetUguiGoActive(handle.SelectedIcon.gameObject, showSelIcon);
                }
                SetUguiGoActive(handle.SelectedIconFallback, !showSelIcon);
            }

            // Status card — live (background rescans/transfers update transferStatus).
            string status = this.transferStatus ?? "Idle";
            if (!string.Equals(status, handle.ShownStatus, StringComparison.Ordinal))
            {
                handle.ShownStatus = status;
                this.SetUguiLabelText(handle.StatusLabel, status);
            }

            // Batch bar count (only meaningful while visible).
            if (this.transferBatch.Count > 0)
            {
                string batchText = "Batch selection: " + this.transferBatch.Count + " stack(s) ready to transfer";
                if (!string.Equals(batchText, handle.ShownBatch, StringComparison.Ordinal))
                {
                    handle.ShownBatch = batchText;
                    this.SetUguiLabelText(handle.BatchLabel, batchText);
                }
            }

            // Grid header vs the two empty-state lines — strings verbatim.
            if (this.transferItems != null && this.transferItems.Count > 0)
            {
                string countText = this.LF("{0} stacks ({1})", this.GetTransferScanSourceLabel(), this.transferItems.Count);
                if (!string.Equals(countText, handle.ShownCount, StringComparison.Ordinal))
                {
                    handle.ShownCount = countText;
                    this.SetUguiLabelText(handle.GridCountLabel, countText);
                }
            }
            else
            {
                string empty = this.transferItems != null
                    ? ("No stacks in " + this.GetTransferScanSourceLabel().ToLowerInvariant() + ".")
                    : "Press Scan Items to load inventory from BackPackSystem.";
                if (!string.Equals(empty, handle.ShownEmpty, StringComparison.Ordinal))
                {
                    handle.ShownEmpty = empty;
                    this.SetUguiLabelText(handle.EmptyLabel, empty);
                }
            }

            // Dropdown cross-surface re-sync (transferScanSource is also editable from IMGUI).
            if (handle.SourceDropdown != null)
            {
                int want = Mathf.Clamp(this.transferScanSource, 0, this.transferScanSourceLabels.Length - 1);
                if (handle.SourceDropdown.value != want)
                {
                    handle.SourceDropdown.SetValueWithoutNotify(want);
                    handle.SourceLastValue = want;
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Grid sync (assignment + per-slot signature rebinds)
        // ----------------------------------------------------------------------------------------

        private void SyncUguiTransferGrid(UguiShellBagWarehouseHandle handle)
        {
            UguiVirtualGridHandle grid = handle.Grid;
            if (grid == null || grid.Root == null || !grid.Root.activeSelf)
            {
                return;
            }

            int count = (this.transferItems != null) ? this.transferItems.Count : 0;
            this.UpdateUguiVirtualGridAssignments(grid, count);

            List<UguiTransferCellWidgets> cells = handle.Cells;
            for (int k = 0; k < grid.Slots.Count && k < cells.Count; k++)
            {
                UguiVirtualGridSlot slot = grid.Slots[k];
                UguiTransferCellWidgets w = cells[k];
                int index = slot.BoundIndex;
                if (index < 0)
                {
                    w.SigIndex = int.MinValue;
                    continue;
                }
                TransferItemEntry entry = (this.transferItems != null && index < this.transferItems.Count)
                    ? this.transferItems[index]
                    : null;
                if (entry == null)
                {
                    // IMGUI skips null entries; hide the slot rather than show stale content.
                    if (slot.Root != null && slot.Root.activeSelf)
                    {
                        slot.Root.SetActive(false);
                    }
                    w.SigIndex = int.MinValue;
                    continue;
                }

                bool isSelected = this.selectedTransferIndex == index;
                bool inBatch = this.transferBatch.ContainsKey(entry.NetId);
                bool showPick;
                int pickQty = this.GetTransferTilePickQuantity(entry, index, out showPick);
                int heldDir = (this.uguiTransferQtyHoldDirection != 0 && this.uguiTransferQtyHoldSlot == k)
                    ? this.uguiTransferQtyHoldDirection
                    : 0;

                if (w.SigIndex == index && w.SigNetId == entry.NetId && w.SigCount == entry.Count
                    && w.SigStar == entry.StarRate && w.SigLocked == entry.IsLocked
                    && w.SigSelected == isSelected && w.SigInBatch == inBatch
                    && w.SigShowPick == showPick && w.SigPickQty == pickQty && w.SigHeldDir == heldDir
                    && string.Equals(w.SigName, entry.DisplayName, StringComparison.Ordinal))
                {
                    continue; // unchanged — no rebind, which is the whole point of the pool
                }

                this.BindUguiTransferCell(handle, w, entry, index, isSelected, inBatch, showPick, pickQty, heldDir);
            }
        }

        // Writes one pooled cell's full visual state from an entry — the retained-mode equivalent
        // of IMGUI's per-cell draw block (Gui.cs:1460-1543). Reposition/activation is the grid
        // core's job; this only touches content.
        private void BindUguiTransferCell(UguiShellBagWarehouseHandle handle, UguiTransferCellWidgets w,
            TransferItemEntry entry, int index, bool isSelected, bool inBatch, bool showPick, int pickQty, int heldDir)
        {
            float alpha = entry.IsLocked ? 0.45f : 1f; // IMGUI: GUI.color = 45% while locked

            if (w.Bg != null)
            {
                w.Bg.color = (isSelected || inBatch) ? handle.CellActiveFill : handle.CellNormalFill;
            }
            if (w.Ring != null)
            {
                w.Ring.color = isSelected ? handle.RingSelected : (inBatch ? handle.RingBatch : handle.RingNormal);
            }

            Texture2D tex;
            this.TryGetTransferItemTexture(entry, out tex);
            Sprite sprite = (tex != null) ? this.GetOrCreateUguiTransferSprite(tex) : null;
            bool hasIcon = sprite != null;
            w.HasIcon = hasIcon;
            if (w.Icon != null)
            {
                if (hasIcon)
                {
                    w.Icon.sprite = sprite;
                    w.Icon.color = new Color(1f, 1f, 1f, alpha);
                }
                SetUguiGoActive(w.Icon.gameObject, hasIcon);
            }
            SetUguiGoActive(w.InitialsLabel, !hasIcon);
            if (!hasIcon)
            {
                this.SetUguiLabelText(w.InitialsLabel, this.GetAutoSellItemInitials(entry.DisplayName));
                this.SetUguiLabelColor(w.InitialsLabel,
                    new Color(handle.TextColor.r, handle.TextColor.g, handle.TextColor.b, alpha));
            }

            bool showStack = entry.Count > 0;
            SetUguiGoActive(w.StackBadge, showStack);
            if (showStack)
            {
                this.SetUguiLabelText(w.StackBadge, "x" + entry.Count);
                this.SetUguiLabelColor(w.StackBadge, new Color(1f, 1f, 1f, alpha));
            }

            string starLabel = this.GetTransferTileStarLabel(entry);
            bool showStar = !string.IsNullOrEmpty(starLabel);
            SetUguiGoActive(w.StarBadge, showStar);
            if (showStar)
            {
                this.SetUguiLabelText(w.StarBadge, starLabel);
                this.SetUguiLabelColor(w.StarBadge,
                    new Color(handle.StarColor.r, handle.StarColor.g, handle.StarColor.b, alpha));
            }

            this.SetUguiLabelText(w.NameLabel, entry.DisplayName);
            this.SetUguiLabelColor(w.NameLabel,
                new Color(handle.TextColor.r, handle.TextColor.g, handle.TextColor.b, alpha));

            // Stepper strip + qty readout. IMGUI gates the ENTIRE interactive block (including
            // the not-selected qty label) on !entry.IsLocked.
            bool stepper = isSelected && !entry.IsLocked;
            bool qtyVisible = showPick && !entry.IsLocked;
            SetUguiGoActive(w.QtyLabel, qtyVisible);
            if (qtyVisible)
            {
                this.SetUguiLabelText(w.QtyLabel, pickQty.ToString());
            }
            SetUguiGoActive(w.MinusBtn, stepper);
            SetUguiGoActive(w.PlusBtn, stepper);
            if (stepper)
            {
                if (w.MinusBg != null)
                {
                    w.MinusBg.color = heldDir < 0 ? handle.HeldTint : handle.ControlFill;
                }
                if (w.PlusBg != null)
                {
                    w.PlusBg.color = heldDir > 0 ? handle.HeldTint : handle.ControlFill;
                }
            }

            // Click routing: whole-cell only while NOT selected (and unlocked); the two zones
            // only while selected — exactly IMGUI's rect carving. The bg Image keeps raycasting
            // either way so cell clicks can never leak through to the game.
            SetUguiGoActive(w.IconZone, stepper);
            SetUguiGoActive(w.NameZone, stepper);
            if (w.CellButton != null)
            {
                bool wholeCell = !isSelected && !entry.IsLocked;
                if (w.CellButton.enabled != wholeCell)
                {
                    w.CellButton.enabled = wholeCell;
                }
            }

            w.SigIndex = index;
            w.SigNetId = entry.NetId;
            w.SigCount = entry.Count;
            w.SigStar = entry.StarRate;
            w.SigLocked = entry.IsLocked;
            w.SigSelected = isSelected;
            w.SigInBatch = inBatch;
            w.SigShowPick = showPick;
            w.SigPickQty = pickQty;
            w.SigHeldDir = heldDir;
            w.SigName = entry.DisplayName;
        }

        // 0.5s tick: cells showing initials re-try the texture lookup (the game loads icons
        // async; IMGUI gets the upgrade for free by redrawing every frame). A hit just
        // invalidates that slot's signature — the next grid sync rebinds it with the icon.
        // TryGetTransferItemTexture also re-fires the request-once async load on a miss.
        private void RetryUguiTransferIcons(UguiShellBagWarehouseHandle handle)
        {
            UguiVirtualGridHandle grid = handle.Grid;
            if (grid == null || grid.Root == null || !grid.Root.activeSelf || this.transferItems == null)
            {
                return;
            }
            List<UguiTransferCellWidgets> cells = handle.Cells;
            for (int k = 0; k < grid.Slots.Count && k < cells.Count; k++)
            {
                UguiTransferCellWidgets w = cells[k];
                int index = grid.Slots[k].BoundIndex;
                if (index < 0 || w.HasIcon || w.SigIndex == int.MinValue || index >= this.transferItems.Count)
                {
                    continue;
                }
                TransferItemEntry entry = this.transferItems[index];
                if (entry == null)
                {
                    continue;
                }
                Texture2D tex;
                if (this.TryGetTransferItemTexture(entry, out tex) && tex != null)
                {
                    w.SigIndex = int.MinValue;
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Stepper hold-repeat (own state — see file header for why the IMGUI fields are off-limits)
        // ----------------------------------------------------------------------------------------

        private void ClearUguiTransferQtyHold()
        {
            this.uguiTransferQtyHoldDirection = 0;
            this.uguiTransferQtyHoldSlot = -1;
            this.uguiTransferQtyHoldItemIndex = -1;
            this.uguiTransferQtyHoldNetId = 0U;
        }

        private void ProcessUguiTransferQtyHold(UguiShellBagWarehouseHandle handle)
        {
            UguiVirtualGridHandle grid = handle.Grid;
            bool gridVisible = grid != null && grid.Root != null && grid.Root.activeSelf;

            // Press: first step on mouse DOWN over a stepper of the selected, visible cell
            // (DrawTransferQtyStepButton parity — step now, repeat only after the delay).
            if (this.uguiTransferQtyHoldDirection == 0 && gridVisible && Input.GetMouseButtonDown(0)
                && this.transferItems != null && this.selectedTransferIndex >= 0)
            {
                for (int k = 0; k < grid.Slots.Count && k < handle.Cells.Count; k++)
                {
                    UguiVirtualGridSlot slot = grid.Slots[k];
                    if (slot == null || slot.Root == null || !slot.Root.activeSelf
                        || slot.BoundIndex != this.selectedTransferIndex)
                    {
                        continue;
                    }

                    UguiTransferCellWidgets w = handle.Cells[k];
                    if (w.MinusBtn == null || !w.MinusBtn.activeSelf)
                    {
                        break; // selected cell visible but steppers hidden — nothing to press
                    }
                    int index = slot.BoundIndex;
                    if (index >= this.transferItems.Count)
                    {
                        break;
                    }
                    TransferItemEntry entry = this.transferItems[index];
                    if (entry == null || entry.IsLocked || entry.NetId == 0U)
                    {
                        break;
                    }

                    Vector3 m3 = Input.mousePosition;
                    Vector2 mouse = new Vector2(m3.x, m3.y);
                    // The press must land inside the grid viewport itself: RectMask2D clips
                    // rendering, not rect hit tests, so the +1 buffer row's steppers hang just
                    // past the mask and must not catch clicks aimed below the grid.
                    RectTransform gridRt = grid.Root.GetComponent<RectTransform>();
                    if (gridRt == null || !RectTransformUtility.RectangleContainsScreenPoint(gridRt, mouse, null))
                    {
                        break;
                    }

                    int direction = 0;
                    RectTransform minusRt = w.MinusBtn.GetComponent<RectTransform>();
                    RectTransform plusRt = (w.PlusBtn != null) ? w.PlusBtn.GetComponent<RectTransform>() : null;
                    if (minusRt != null && RectTransformUtility.RectangleContainsScreenPoint(minusRt, mouse, null))
                    {
                        direction = -1;
                    }
                    else if (plusRt != null && RectTransformUtility.RectangleContainsScreenPoint(plusRt, mouse, null))
                    {
                        direction = 1;
                    }
                    if (direction != 0)
                    {
                        this.AdjustTransferTilePickQuantity(entry, index, direction);
                        this.uguiTransferQtyHoldDirection = direction;
                        this.uguiTransferQtyHoldSlot = k;
                        this.uguiTransferQtyHoldItemIndex = index;
                        this.uguiTransferQtyHoldNetId = entry.NetId;
                        this.uguiTransferQtyHoldStartedAt = Time.unscaledTime;
                        // First step applied above; first REPEAT only after one interval past the
                        // delay (BeginTransferQtyHold parity).
                        this.uguiTransferQtyHoldLastStepAt = Time.unscaledTime;
                    }
                    break;
                }
            }

            // Repeat — UpdateTransferQtyHoldRepeat's gates + thresholds verbatim, plus the
            // pool-specific cancel when the held cell is rebound underneath the hold.
            if (this.uguiTransferQtyHoldDirection == 0)
            {
                return;
            }
            if (!Input.GetMouseButton(0))
            {
                this.ClearUguiTransferQtyHold();
                return;
            }
            if (!gridVisible || this.transferItems == null
                || this.uguiTransferQtyHoldItemIndex < 0
                || this.uguiTransferQtyHoldItemIndex >= this.transferItems.Count)
            {
                this.ClearUguiTransferQtyHold();
                return;
            }
            if (this.selectedTransferIndex != this.uguiTransferQtyHoldItemIndex)
            {
                this.ClearUguiTransferQtyHold();
                return;
            }
            TransferItemEntry heldEntry = this.transferItems[this.uguiTransferQtyHoldItemIndex];
            if (heldEntry == null || heldEntry.NetId != this.uguiTransferQtyHoldNetId)
            {
                this.ClearUguiTransferQtyHold();
                return;
            }
            if (this.uguiTransferQtyHoldSlot < 0 || this.uguiTransferQtyHoldSlot >= grid.Slots.Count
                || grid.Slots[this.uguiTransferQtyHoldSlot].BoundIndex != this.uguiTransferQtyHoldItemIndex)
            {
                this.ClearUguiTransferQtyHold(); // scrolled away — the pooled cell now shows other data
                return;
            }

            float heldSeconds = Time.unscaledTime - this.uguiTransferQtyHoldStartedAt;
            if (heldSeconds < TransferQtyHoldRepeatDelay)
            {
                return;
            }
            float repeatSeconds = heldSeconds - TransferQtyHoldRepeatDelay;
            float interval = repeatSeconds >= TransferQtyHoldFastAfterSeconds
                ? TransferQtyHoldFastInterval
                : TransferQtyHoldSlowInterval;
            if (Time.unscaledTime - this.uguiTransferQtyHoldLastStepAt < interval)
            {
                return;
            }
            this.AdjustTransferTilePickQuantity(heldEntry, this.uguiTransferQtyHoldItemIndex, this.uguiTransferQtyHoldDirection);
            this.uguiTransferQtyHoldLastStepAt = Time.unscaledTime;
        }
    }
}
