using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 8 of 8 (migration plan item 12): the
    // HOMELAND FARM sub-tab — DrawHomelandFarmTab (HomelandFarmFeature.cs:22216-22519) plus its
    // two private layout helpers DrawHomelandFarmStorageSourceToggle (:21988-22024, the 3-way
    // Backpack/Warehouse/Both picker) and DrawHomelandFarmInventorySelector (:22026-22057, the
    // wrap-around prev/next paginator). With this round every New Features sub except Daily
    // Quests (display 1) has real UGUI content.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same methods (all
    //    directly on HeartopiaComplete; ZERO changes to HomelandFarmFeature.cs). Two independent
    //    rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellHomelandFarmSubIndex = 2, declared with their siblings in UguiShellTabIndices.cs;
    //    AnimalCareFeature.cs:39-42 dispatcher: newFeaturesSubTab == 2 → DrawHomelandFarmTab),
    //    never label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // PERSISTENCE MODEL — this tab is NOT the "flag + SaveKeybinds(false)" idiom. The source
    // draw function calls SaveKeybinds ZERO times (verified line by line), and so does this
    // file. Per control:
    //  - Auto-fertilize checkbox → bespoke this.PersistHomelandFarmAutoFertilizeSetting(), and
    //    ONLY behind the source's explicit `hasSelectedFertilizer &&` WRITE guard (:22267-22271
    //    — the guard is on the write, not just the enabled state, because a GUI.enabled-disabled
    //    IMGUI toggle can still report a change; reproduced verbatim in the click handler, which
    //    re-evaluates TryHomelandFarmGetSelectedFertilizer at click time).
    //  - Water-radius slider → DEBOUNCED bespoke save (:22320-22337): the per-gated-frame
    //    processor runs the source's exact state machine over the SAME shared fields
    //    (homelandFarmWaterRadiusLastSeen/SavePending/SaveAt) — first-draw sentinel
    //    (LastSeen < 0f adopts without scheduling), change re-arms SaveAt = now +
    //    HomelandFarmRadiusSaveDebounceSeconds, and PersistHomelandFarmRadius() fires only when
    //    the timer lapses. Both surfaces run the identical idempotent machine, exactly like the
    //    IMGUI twin re-running it every repaint.
    //  - Seed/fert storage pickers + inventory selectors → NO persistence at all: picking just
    //    writes the in-memory field (:22351-22354 / :22388-22391 — a guarded plain assignment,
    //    zero side effects). Do not add a save.
    //  - Event Diagnostics toggle → field write + this.OnHomelandFarmEventDiagToggled() only
    //    (:22484-22490).
    //  - Event-driven auto-farm toggle → the simplest shape in the whole migration: an
    //    unconditional flag assignment (:22496-22497). No guard, no save, no cascade.
    //
    // NEW local primitives this round (kept here until a second consumer appears — the
    // SetUguiButtonLabel precedent):
    //  - CreateUguiHomelandFarmStorageSegment + ApplyUguiHomelandFarmSegmentState: the plain
    //    3-way picker. Reuses the SHAPE of Radar's CreateUguiRadarSegmentButton (handle +
    //    build-time click wire + cached-diff Apply), but the visuals map the SOURCE styles —
    //    active = themePrimaryButtonStyle → kit primary tier (accent fill, text-on-accent, no
    //    ring), inactive = plain GUI.skin.button → kit secondary tier (control fill +
    //    UguiKitSecondaryRing) — with no "[ON]" suffix (that is Radar's own look, not this
    //    picker's). Labels are static (localized once at build), so Apply diffs only the
    //    selected bit. Built via one shared picker builder, used twice (seeds, fertilizer).
    //  - CreateUguiHomelandFarmInventorySelector + SyncUguiHomelandFarmSelector: the prev/next
    //    paginator. Both buttons WRAP (prev = (i - 1 + count) % count, next = (i + 1) % count —
    //    source math verbatim, :22046/:22052) after the source's own Mathf.Clamp re-clamp
    //    (:22041, which also writes the clamped index back to the field when a refresh shrank
    //    the list). Empty list = ONLY the empty-state label, no buttons (:22035-22039), mirrored
    //    with SetUguiGoActive swaps. Geometry replays the source rects relative to the row width
    //    (prev 0/28w, label 34/(w-124), next (w-84)/28w, counter (w-50)/50w).
    //
    // Start vs Stop (auto card): the source's two buttons occupy the EXACT same Rect
    // (autoRect.x+16, autoY, width-32, 32 — :22278 vs :22290) but differ in style (danger vs
    // primary), label, and enabled condition — so this file pre-builds BOTH kit buttons at that
    // spot and SetActive-swaps on homelandFarmAutoRunning (chosen over a single mutating button:
    // the kit bakes tier colors at creation, and a swap keeps both closures trivial).
    //
    // Deliberate deviations (established precedents, do not "fix"):
    //  - The two DrawSwitchToggle rows render as kit CHECKBOXES, not sliding switches
    //    (Settings-round precedent, restated by Bag/Warehouse: CreateUguiSwitch's pill visuals
    //    ignore SetIsOnWithoutNotify and it replays onChanged once per theme rebuild — the
    //    checkbox follows WithoutNotify re-syncs for free). Their labels are the source's RAW
    //    UNLOCALIZED literals ("Event Diagnostics (log)" / "Event-driven auto-farm (no
    //    rescan)" — :22485/:22497 pass them straight, no L()).
    //  - The auto-fertilize GUI.Toggle's leading " " spacer (:22266) is an IMGUI spacing hack;
    //    the kit checkbox spaces structurally, so the label is L("homeland_farm.auto_fertilize")
    //    without it.
    //  - Cards adapt to the cell width (panelW) with the source's 16px insets (Radar-Settings
    //    precedent for 520px cards); the ops grid splits the inner width into two equal columns
    //    (source: fixed 230px cols inside a fixed 520px card), and the slider's 0.55/0.45 label
    //    split is kept as a ratio.
    //
    // Layout replays the source y-cursor chain verbatim (content top margin 8 standing in for
    // startY, the Foraging convention):
    //   auto card      y=8    h=176  (capture 38, status 43, hint 74, checkbox 98, start/stop 126)
    //   y += 188   →   radius card  y=196 h=88  (labels 34, slider 54)
    //   y += 100   →   crops card   y=296 h=160 (storage lbl 36, picker 58, refresh 90, sel 122)
    //   y += 172   →   fert card    y=468 h=160 (same rows; refresh 140w, count lbl at 166)
    //   y += 172   →   ops card     y=640 h=272 (rows 40/78/116/154 two-col + 192 full-width)
    //   y += 284   →   event diag   y=924 (h28); y += 34 → event-driven y=958 (h28)
    //   y += 38    →   status card  y=996 h=52  (label 16,10, wrapped 11pt @ .82)
    //   y += 62    →   stop button  y=1058 (160x30); + 40 → content height 1098.
    // NOTE (2026-07-22): the radius card is 88, not the source's 70 — its slider runs y=54..74 and
    // was overflowing the card. Everything from the crops card down is therefore +18 off the IMGUI
    // y-cursor; the 12px inter-card gap is unchanged. Don't "restore" these to source parity.
    //
    // Per-gated-frame cadence (shell visible + New Features tab + Homeland Farm sub, matching
    // the source running these on every IMGUI repaint of the tab):
    //  - this.EnsureHomelandFarmWarmupStarted() (source top-of-draw, :22218 — idempotent kick).
    //  - The eager fertilizer auto-refresh-if-empty (:22256-22259 — source re-checks every
    //    repaint; the per-gated-frame check is the correct equivalent).
    //  - The radius debounce state machine (above).
    //  - Control re-syncs: button interactables recomputed live (IsHomelandFarmBusy +
    //    IsHomelandFarmWarmupReady + TryHomelandFarmGetSelectedFertilizer change from background
    //    activity — the Animal Care live-gate lesson), checkbox re-syncs via
    //    SetIsOnWithoutNotify, slider via SetValueWithoutNotify, 6 segment states (cached-diff),
    //    both selectors (list identity/count/selection all change after refreshes), the
    //    Start/Stop active swap, and the cached-string label diffs — including the status card's
    //    KEY-then-localize shape (:22504-22507): pick the translation key first
    //    (IsHomelandFarmWarmupReady() ? homelandFarmLastStatus ?? "homeland_farm.status_idle"
    //    : "homeland_farm.status_warming"), then ONE L() call on the picked key — not three
    //    separately-localized branches. homelandFarmLastStatus itself stores KEYS (e.g.
    //    "homeland_farm.status_stopped" written by the stop handlers, :22281/:22514) and is
    //    only ever turned into display text at this single L() site.
    //  Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiHomelandFarmSegmentHandle
        {
            public GameObject Root;
            public Image Bg;
            public Image Ring;                    // may be null (ring sprite unavailable)
            public GameObject Label;
            public int AppliedSelected = -1;      // -1 = never applied (labels are static)
        }

        private sealed class UguiHomelandFarmSelectorHandle
        {
            public GameObject PrevButton;
            public GameObject NextButton;
            public GameObject ItemLabel;
            public string ItemShown;
            public GameObject CountLabel;
            public string CountShown;
            public GameObject EmptyLabel;
            public Func<List<HomelandFarmInventoryItem>> GetItems;
            public Func<int> GetIndex;
            public Action<int> SetIndex;
            public string EmptyText;              // localized once at build (source :22037 fallback)
        }

        private sealed class UguiShellNewFeaturesHomelandFarmHandle
        {
            public GameObject Root;

            // AUTO FARMING card
            public GameObject CaptureButton;      // farmInteractive && !running
            public GameObject CapturedLabel;
            public string CapturedShown;
            public Toggle AutoFertilizeToggle;    // live-gated AND write-guarded (file header)
            public GameObject AutoStartButton;    // primary — shown while NOT running
            public GameObject AutoStopButton;     // danger — shown while running (same rect)

            // FARM RADIUS card
            public Slider RadiusSlider;
            public GameObject RadiusValueLabel;
            public string RadiusValueShown;

            // CROPS card
            public UguiHomelandFarmSegmentHandle[] SeedSegments;   // Backpack/Warehouse/Both
            public GameObject RefreshSeedsButton;
            public GameObject SeedsCachedLabel;
            public string SeedsCachedShown;
            public UguiHomelandFarmSelectorHandle SeedSelector;

            // FERTILIZER card
            public UguiHomelandFarmSegmentHandle[] FertSegments;
            public GameObject RefreshFertilizersButton;
            public GameObject FertCachedLabel;
            public string FertCachedShown;
            public UguiHomelandFarmSelectorHandle FertSelector;

            // OPERATIONS card — all 9 gated on farmInteractive alone
            public GameObject[] OpsButtons;

            // Trailing toggles + status + stop
            public Toggle EventDiagToggle;
            public Toggle EventDrivenToggle;
            public GameObject StatusLabel;
            public string StatusShown;
            public GameObject StopButton;         // homelandFarmCoroutine != null

            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesHomelandFarmHandle uguiShellNewFeaturesHomelandFarm;

        // ----------------------------------------------------------------------------------------
        // Small local helpers
        // ----------------------------------------------------------------------------------------

        // Toggle analog of SetUguiButtonInteractable (self-diffed property write). Toggle is a
        // Selectable, so interactable = false greys the targetGraphic via the default ColorBlock,
        // the same way disabled kit buttons dim.
        private void SetUguiHomelandFarmToggleInteractable(Toggle tog, bool interactable)
        {
            try
            {
                if (tog != null && tog.interactable != interactable)
                {
                    tog.interactable = interactable;
                }
            }
            catch { }
        }

        // The source's section/hint label style: bold 11 in uiText @ 0.9 (sectionStyle,
        // HomelandFarmFeature.cs:22225-22226).
        private GameObject CreateUguiHomelandFarmSectionLabel(Transform parent, string name, string text)
        {
            Color textColor = this.UguiKitTextColor();
            GameObject lbl = this.CreateUguiLabel(parent, name, text, 11f,
                new Color(textColor.r, textColor.g, textColor.b, 0.9f), false);
            this.TrySetUguiLabelBold(lbl);
            return lbl;
        }

        // ----------------------------------------------------------------------------------------
        // Storage-source segment primitive (3-way Backpack/Warehouse/Both picker)
        // ----------------------------------------------------------------------------------------

        // One segment box — the Radar segment SHAPE with this picker's own style mapping (file
        // header): the click just reports the segment's value; the caller's handler owns the
        // field write. Label localized once at build (the source labels are static too).
        private UguiHomelandFarmSegmentHandle CreateUguiHomelandFarmStorageSegment(Transform parent,
            string name, string label, System.Action onClick)
        {
            UguiHomelandFarmSegmentHandle seg = new UguiHomelandFarmSegmentHandle();
            GameObject go = this.CreateUguiGo(name, parent);
            seg.Root = go;

            seg.Bg = this.AddUguiImage(go, this.UguiKitControlFill(), true, 1.5f);
            seg.Bg.raycastTarget = true;
            this.AddUguiRingOverlay(go, UguiKitSecondaryRing, 1.5f);
            try
            {
                Transform ringT = go.transform.Find("Ring");
                seg.Ring = (ringT != null) ? ringT.GetComponent<Image>() : null;
            }
            catch { }

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = seg.Bg;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            GameObject lbl = this.CreateUguiLabel(go.transform, "Label", label, 12f,
                this.UguiKitTextColor(), true);
            this.TrySetUguiLabelBold(lbl);
            StretchUguiFill(lbl, 2f, 0f, 2f, 0f);
            seg.Label = lbl;
            return seg;
        }

        // No-ops until the selected bit actually changed (runs per gated frame for 6 segments).
        // Selected = the kit PRIMARY tier (accent fill, text-on-accent, ring hidden — primary
        // buttons carry no ring); unselected = the kit SECONDARY tier (control fill +
        // UguiKitSecondaryRing) — the UGUI mapping of themePrimaryButtonStyle vs GUI.skin.button
        // (DrawHomelandFarmStorageSourceToggle, HomelandFarmFeature.cs:22013-22016). Colors read
        // live at apply time (LOD highlight precedent).
        private void ApplyUguiHomelandFarmSegmentState(UguiHomelandFarmSegmentHandle seg, bool selected)
        {
            if (seg == null)
            {
                return;
            }
            int selectedBit = selected ? 1 : 0;
            if (seg.AppliedSelected == selectedBit)
            {
                return;
            }
            seg.AppliedSelected = selectedBit;

            Color accent = this.UguiKitAccent();
            if (seg.Bg != null)
            {
                seg.Bg.color = selected ? accent : this.UguiKitControlFill();
            }
            if (seg.Ring != null)
            {
                seg.Ring.color = selected ? Color.clear : UguiKitSecondaryRing;
            }
            this.SetUguiLabelColor(seg.Label, selected
                ? this.GetUiTextOnAccent(accent)
                : this.UguiKitTextColor());
        }

        // The whole 3-way picker row: values/labels/geometry from the source arrays (:21996-22010
        // — 100x24 buttons, 8px gaps → a fixed 316px strip, well inside the inner width).
        // onPick(value) is the caller's field-write handler; selection visuals come from the
        // per-frame Apply pass (and the build-time seed below), so an IMGUI-twin pick restyles
        // these segments the same way our own clicks do.
        private UguiHomelandFarmSegmentHandle[] CreateUguiHomelandFarmStoragePicker(Transform parent,
            string namePrefix, float x, float y, System.Action<HomelandFarmStorageSource> onPick)
        {
            HomelandFarmStorageSource[] values =
            {
                HomelandFarmStorageSource.Backpack,
                HomelandFarmStorageSource.Warehouse,
                HomelandFarmStorageSource.Both
            };
            string[] labels =
            {
                this.L("homeland_farm.storage_backpack"),
                this.L("homeland_farm.storage_warehouse"),
                this.L("homeland_farm.storage_both")
            };

            UguiHomelandFarmSegmentHandle[] segments = new UguiHomelandFarmSegmentHandle[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                HomelandFarmStorageSource value = values[i]; // per-iteration copy for the closure
                segments[i] = this.CreateUguiHomelandFarmStorageSegment(parent, namePrefix + i,
                    labels[i], new System.Action(() =>
                    {
                        if (onPick != null)
                        {
                            onPick(value);
                        }
                    }));
                PlaceUguiTopLeft(segments[i].Root, x + i * 108f, y, 100f, 24f);
            }
            return segments;
        }

        // ----------------------------------------------------------------------------------------
        // Inventory selector primitive (wrap-around prev/next paginator)
        // ----------------------------------------------------------------------------------------

        // DrawHomelandFarmInventorySelector's row (:22026-22057) as persistent widgets. The row
        // container is placed by the caller; children replay the source rects relative to rowW.
        // Empty-vs-populated visibility and all texts are owned by SyncUguiHomelandFarmSelector.
        private UguiHomelandFarmSelectorHandle CreateUguiHomelandFarmInventorySelector(Transform parent,
            string name, float x, float y, float rowW, string emptyText,
            Func<List<HomelandFarmInventoryItem>> getItems, Func<int> getIndex, Action<int> setIndex)
        {
            UguiHomelandFarmSelectorHandle sel = new UguiHomelandFarmSelectorHandle();
            sel.GetItems = getItems;
            sel.GetIndex = getIndex;
            sel.SetIndex = setIndex;
            sel.EmptyText = emptyText;

            GameObject row = this.CreateUguiGo(name, parent);
            PlaceUguiTopLeft(row, x, y, rowW, 24f);

            // Prev/next are plain default-style buttons in the source (GUI.Button with no style
            // override) → kit secondary tier.
            sel.PrevButton = this.CreateUguiSecondaryButton(row.transform, "Prev",
                this.L("homeland_farm.prev"),
                new System.Action(() => this.OnUguiHomelandFarmSelectorStep(sel, -1)));
            PlaceUguiTopLeft(sel.PrevButton, 0f, 0f, 28f, 24f);

            sel.ItemShown = string.Empty;
            sel.ItemLabel = this.CreateUguiBodyLabel(row.transform, "Item", string.Empty, 13f);
            PlaceUguiTopLeft(sel.ItemLabel, 34f, 0f, rowW - 124f, 24f);

            sel.NextButton = this.CreateUguiSecondaryButton(row.transform, "Next",
                this.L("homeland_farm.next"),
                new System.Action(() => this.OnUguiHomelandFarmSelectorStep(sel, +1)));
            PlaceUguiTopLeft(sel.NextButton, rowW - 84f, 0f, 28f, 24f);

            sel.CountShown = string.Empty;
            sel.CountLabel = this.CreateUguiBodyLabel(row.transform, "Count", string.Empty, 13f);
            PlaceUguiTopLeft(sel.CountLabel, rowW - 50f, 0f, 50f, 24f);

            sel.EmptyLabel = this.CreateUguiBodyLabel(row.transform, "Empty", emptyText, 13f);
            PlaceUguiTopLeft(sel.EmptyLabel, 0f, 0f, rowW, 24f);
            SetUguiGoActive(sel.EmptyLabel, false);

            return sel;
        }

        // Both wrap around — the source math verbatim (:22046 prev, :22052 next), applied after
        // the source's own draw-top re-clamp (:22041 — the list may have shrunk since the index
        // was written).
        private void OnUguiHomelandFarmSelectorStep(UguiHomelandFarmSelectorHandle sel, int direction)
        {
            if (sel == null || sel.GetItems == null || sel.GetIndex == null || sel.SetIndex == null)
            {
                return;
            }
            List<HomelandFarmInventoryItem> items = sel.GetItems();
            if (items == null || items.Count == 0)
            {
                return;
            }
            int index = Mathf.Clamp(sel.GetIndex(), 0, items.Count - 1);
            index = (direction < 0)
                ? (index - 1 + items.Count) % items.Count
                : (index + 1) % items.Count;
            sel.SetIndex(index);
        }

        // Per-frame mirror of the selector draw: empty list = only the empty label (:22035-22039);
        // otherwise re-clamp (writing the clamped index back to the field, as the source's
        // ref-param clamp does), selected .Label or the empty text when the slot is null
        // (:22042-22043), and the "N/Total" counter (:22055). interactive = the card's
        // GUI.enabled = farmInteractive wrap (:22348/:22385 — labels stay painted either way).
        private void SyncUguiHomelandFarmSelector(UguiHomelandFarmSelectorHandle sel, bool interactive)
        {
            if (sel == null)
            {
                return;
            }
            List<HomelandFarmInventoryItem> items = (sel.GetItems != null) ? sel.GetItems() : null;
            bool hasItems = items != null && items.Count > 0;

            SetUguiGoActive(sel.EmptyLabel, !hasItems);
            SetUguiGoActive(sel.PrevButton, hasItems);
            SetUguiGoActive(sel.NextButton, hasItems);
            SetUguiGoActive(sel.ItemLabel, hasItems);
            SetUguiGoActive(sel.CountLabel, hasItems);
            if (!hasItems)
            {
                return;
            }

            int index = Mathf.Clamp(sel.GetIndex(), 0, items.Count - 1);
            if (index != sel.GetIndex())
            {
                sel.SetIndex(index); // the source clamp writes through the ref param (:22041)
            }

            HomelandFarmInventoryItem selected = items[index];
            this.SyncUguiSelfLabelText(sel.ItemLabel, ref sel.ItemShown,
                (selected != null) ? selected.Label : sel.EmptyText);
            this.SyncUguiSelfLabelText(sel.CountLabel, ref sel.CountShown,
                (index + 1) + "/" + items.Count);
            this.SetUguiButtonInteractable(sel.PrevButton, interactive);
            this.SetUguiButtonInteractable(sel.NextButton, interactive);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawHomelandFarmTab: six always-visible cards + two free toggle rows +
        // a status card + a stop button in a transparent scroll view, every position replaying
        // the IMGUI cursor chain ONCE at build time (all card heights fixed — the only dynamic
        // visibility is the Start/Stop same-rect swap and the selectors' empty-state swap, both
        // owned by the sync pass). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesHomelandFarmContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesHomelandFarm = null;

            UguiShellNewFeaturesHomelandFarmHandle handle = new UguiShellNewFeaturesHomelandFarmHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesHomelandFarmContent", parent);
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

            float contentWidth = w - 22f;   // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // panels at x=8, 8px right margin
            float rowW = panelW - 32f;      // the source's width-32 inner width

            // -------- 1. AUTO FARMING card (y=8, h=176 — :22232-22298) --------
            GameObject autoCard = this.CreateUguiSettingsMainPanel(scrollContent, "AutoPanel",
                this.L("homeland_farm.auto_section"));
            PlaceUguiTopLeft(autoCard, 8f, 8f, panelW, 176f);

            handle.CaptureButton = this.CreateUguiPrimaryButton(autoCard.transform, "CaptureButton",
                this.L("homeland_farm.auto_capture"),
                new System.Action(this.OnUguiHomelandFarmCaptureClicked));
            PlaceUguiTopLeft(handle.CaptureButton, 16f, 38f, 200f, 28f);

            handle.CapturedShown = string.Empty; // sync pass below paints the real text
            handle.CapturedLabel = this.CreateUguiHomelandFarmSectionLabel(autoCard.transform,
                "CapturedLabel", string.Empty);
            PlaceUguiTopLeft(handle.CapturedLabel, 226f, 43f, panelW - 242f, 18f);

            GameObject hint = this.CreateUguiHomelandFarmSectionLabel(autoCard.transform, "HintLabel",
                this.L("homeland_farm.auto_hint"));
            PlaceUguiTopLeft(hint, 16f, 74f, rowW, 18f);

            handle.AutoFertilizeToggle = this.CreateUguiCheckbox(autoCard.transform, "AutoFertilize",
                this.L("homeland_farm.auto_fertilize"), this.homelandFarmAutoFertilizeEnabled,
                new System.Action<bool>(this.OnUguiHomelandFarmAutoFertilizeToggled));
            PlaceUguiTopLeft(handle.AutoFertilizeToggle.gameObject, 16f, 98f, rowW, 22f);

            // Start/Stop pair — same rect, SetActive-swapped (file header).
            handle.AutoStopButton = this.CreateUguiDangerButton(autoCard.transform, "AutoStopButton",
                this.L("homeland_farm.auto_stop"),
                new System.Action(this.OnUguiHomelandFarmAutoStopClicked));
            PlaceUguiTopLeft(handle.AutoStopButton, 16f, 126f, rowW, 32f);

            handle.AutoStartButton = this.CreateUguiPrimaryButton(autoCard.transform, "AutoStartButton",
                this.L("homeland_farm.auto_start"),
                new System.Action(this.OnUguiHomelandFarmAutoStartClicked));
            PlaceUguiTopLeft(handle.AutoStartButton, 16f, 126f, rowW, 32f);

            // -------- 2. FARM RADIUS card (y=196 — :22300-22337) --------
            // 2026-07-22: h was 70, but the slider inside runs y=54..74 — it OVERFLOWED the card by
            // 4px and sat visually welded to its bottom edge. 88 clears the slider and leaves the
            // same ~14px breathing room the other cards have. Every scrollContent child below moved
            // down by the same +18 so the uniform 12px inter-card gap is preserved.
            GameObject radiusCard = this.CreateUguiSettingsMainPanel(scrollContent, "RadiusPanel",
                this.L("homeland_farm.radius_section"));
            PlaceUguiTopLeft(radiusCard, 8f, 196f, panelW, 88f);

            // sliderLabelStyle: bold 11 @ 0.78 (:22303-22304).
            Color textColor = this.UguiKitTextColor();
            GameObject sliderName = this.CreateUguiLabel(radiusCard.transform, "SliderLabel",
                this.L("homeland_farm.radius_slider_label"), 11f,
                new Color(textColor.r, textColor.g, textColor.b, 0.78f), false);
            this.TrySetUguiLabelBold(sliderName);
            PlaceUguiTopLeft(sliderName, 16f, 34f, rowW * 0.55f, 18f);

            // valueLabelStyle: 12pt, right-aligned (:22301-22302).
            handle.RadiusValueShown = string.Empty;
            handle.RadiusValueLabel = this.CreateUguiLabel(radiusCard.transform, "SliderValue",
                string.Empty, 12f, textColor, false);
            this.TrySetUguiLabelRightAligned(handle.RadiusValueLabel);
            PlaceUguiTopLeft(handle.RadiusValueLabel, 16f + rowW * 0.55f, 34f, rowW * 0.45f, 18f);

            // wholeNumbers = the UGUI twin of the source's Mathf.Round snap (:22315); the change
            // handler Rounds again anyway (belt and braces, and source-verbatim).
            handle.RadiusSlider = this.CreateUguiSlider(radiusCard.transform, "RadiusSlider",
                HomelandFarmMinWaterRadius, HomelandFarmMaxWaterRadius, this.homelandFarmWaterRadius,
                true, new System.Action<float>(this.OnUguiHomelandFarmRadiusChanged));
            PlaceUguiTopLeft(handle.RadiusSlider.gameObject, 16f, 54f, rowW, 20f);

            // -------- 3. CROPS card (y=278, h=160 — :22340-22375) --------
            GameObject cropsCard = this.CreateUguiSettingsMainPanel(scrollContent, "CropsPanel",
                this.L("homeland_farm.sow_section"));
            PlaceUguiTopLeft(cropsCard, 8f, 296f, panelW, 160f);

            GameObject seedStorageLabel = this.CreateUguiHomelandFarmSectionLabel(cropsCard.transform,
                "SeedStorageLabel", this.L("homeland_farm.seed_storage"));
            PlaceUguiTopLeft(seedStorageLabel, 16f, 36f, rowW, 18f);

            handle.SeedSegments = this.CreateUguiHomelandFarmStoragePicker(cropsCard.transform,
                "SeedStorageSeg", 16f, 58f,
                new System.Action<HomelandFarmStorageSource>(this.OnUguiHomelandFarmSeedStoragePicked));

            handle.RefreshSeedsButton = this.CreateUguiPrimaryButton(cropsCard.transform, "RefreshSeeds",
                this.L("homeland_farm.refresh_seeds"),
                new System.Action(this.OnUguiHomelandFarmRefreshSeedsClicked));
            PlaceUguiTopLeft(handle.RefreshSeedsButton, 16f, 90f, 120f, 24f);
            this.TrySetUguiButtonLabelSize(handle.RefreshSeedsButton, 12f);

            handle.SeedsCachedShown = string.Empty;
            handle.SeedsCachedLabel = this.CreateUguiHomelandFarmSectionLabel(cropsCard.transform,
                "SeedsCachedLabel", string.Empty);
            PlaceUguiTopLeft(handle.SeedsCachedLabel, 146f, 94f, 200f, 18f);

            handle.SeedSelector = this.CreateUguiHomelandFarmInventorySelector(cropsCard.transform,
                "SeedSelector", 16f, 122f, rowW, this.L("homeland_farm.no_seeds"),
                () => this.homelandFarmScannedSeeds,
                () => this.homelandFarmSelectedSeedIndex,
                v => this.homelandFarmSelectedSeedIndex = v);

            // -------- 4. FERTILIZER card (y=450, h=160 — :22377-22412) --------
            GameObject fertCard = this.CreateUguiSettingsMainPanel(scrollContent, "FertPanel",
                this.L("homeland_farm.fertilize_section"));
            PlaceUguiTopLeft(fertCard, 8f, 468f, panelW, 160f);

            GameObject fertStorageLabel = this.CreateUguiHomelandFarmSectionLabel(fertCard.transform,
                "FertStorageLabel", this.L("homeland_farm.fert_storage"));
            PlaceUguiTopLeft(fertStorageLabel, 16f, 36f, rowW, 18f);

            handle.FertSegments = this.CreateUguiHomelandFarmStoragePicker(fertCard.transform,
                "FertStorageSeg", 16f, 58f,
                new System.Action<HomelandFarmStorageSource>(this.OnUguiHomelandFarmFertStoragePicked));

            handle.RefreshFertilizersButton = this.CreateUguiPrimaryButton(fertCard.transform,
                "RefreshFertilizers", this.L("homeland_farm.refresh_fertilizers"),
                new System.Action(this.OnUguiHomelandFarmRefreshFertilizersClicked));
            PlaceUguiTopLeft(handle.RefreshFertilizersButton, 16f, 90f, 140f, 24f);
            this.TrySetUguiButtonLabelSize(handle.RefreshFertilizersButton, 12f);

            handle.FertCachedShown = string.Empty;
            handle.FertCachedLabel = this.CreateUguiHomelandFarmSectionLabel(fertCard.transform,
                "FertCachedLabel", string.Empty);
            PlaceUguiTopLeft(handle.FertCachedLabel, 166f, 94f, 220f, 18f);

            handle.FertSelector = this.CreateUguiHomelandFarmInventorySelector(fertCard.transform,
                "FertSelector", 16f, 122f, rowW, this.L("homeland_farm.no_fertilizers"),
                () => this.homelandFarmScannedFertilizers,
                () => this.homelandFarmSelectedFertilizerIndex,
                v => this.homelandFarmSelectedFertilizerIndex = v);

            // -------- 5. OPERATIONS card (y=622, h=272 — :22414-22478) --------
            // Column/order mapping verified against source: rows alternate col1/col2 —
            // (Water In Radius | Harvest All), (Weed All | Collect Plant Seeds),
            // (Collect Dormant | Sow), (Collect Flowers | Fertilize), then the full-width
            // diagnostics row. Two equal columns split the inner width (file header deviation).
            GameObject opsCard = this.CreateUguiSettingsMainPanel(scrollContent, "OpsPanel",
                this.L("homeland_farm.operations_section"));
            PlaceUguiTopLeft(opsCard, 8f, 640f, panelW, 272f);

            float colW = (rowW - 12f) / 2f;
            float col1 = 16f;
            float col2 = 16f + colW + 12f;
            string[] opsLabels =
            {
                "homeland_farm.water_in_radius", "homeland_farm.harvest_crops_all",
                "homeland_farm.weed_all", "homeland_farm.collect_plant_seeds_all",
                "homeland_farm.collect_dormant", "homeland_farm.sow",
                "homeland_farm.collect_flowers", "homeland_farm.fertilize"
            };
            System.Action[] opsActions =
            {
                new System.Action(this.OnUguiHomelandFarmWaterInRadiusClicked),
                new System.Action(this.OnUguiHomelandFarmHarvestCropsClicked),
                new System.Action(this.OnUguiHomelandFarmWeedAllClicked),
                new System.Action(this.OnUguiHomelandFarmCollectPlantSeedsClicked),
                new System.Action(this.OnUguiHomelandFarmCollectDormantClicked),
                new System.Action(this.OnUguiHomelandFarmSowClicked),
                new System.Action(this.OnUguiHomelandFarmCollectFlowersClicked),
                new System.Action(this.OnUguiHomelandFarmFertilizeClicked)
            };
            handle.OpsButtons = new GameObject[9];
            for (int i = 0; i < 8; i++)
            {
                float bx = (i % 2 == 0) ? col1 : col2;
                float by = 40f + (i / 2) * 38f; // rows 40/78/116/154 (buttonH 30 + gapY 8)
                handle.OpsButtons[i] = this.CreateUguiPrimaryButton(opsCard.transform, "Ops" + i,
                    this.L(opsLabels[i]), opsActions[i]);
                PlaceUguiTopLeft(handle.OpsButtons[i], bx, by, colW, 30f);
                this.TrySetUguiButtonLabelSize(handle.OpsButtons[i], 12f);
            }
            handle.OpsButtons[8] = this.CreateUguiPrimaryButton(opsCard.transform, "Ops8",
                this.L("homeland_farm.log_water_radius"),
                new System.Action(this.OnUguiHomelandFarmLogWaterRadiusClicked));
            PlaceUguiTopLeft(handle.OpsButtons[8], 16f, 192f, rowW, 30f);
            this.TrySetUguiButtonLabelSize(handle.OpsButtons[8], 12f);

            // -------- 6+7. The two trailing toggles (y=906 / y=940 — :22480-22499) --------
            // Kit checkboxes with the source's RAW literals (file header). Event Diagnostics'
            // handler keeps the source change guard; Event-driven is a bare flag assignment.
            handle.EventDiagToggle = this.CreateUguiCheckbox(scrollContent, "EventDiagToggle",
                "Event Diagnostics (log)", this.homelandFarmEventDiagEnabled,
                new System.Action<bool>(this.OnUguiHomelandFarmEventDiagToggled));
            PlaceUguiTopLeft(handle.EventDiagToggle.gameObject, 8f, 924f, panelW, 28f);

            handle.EventDrivenToggle = this.CreateUguiCheckbox(scrollContent, "EventDrivenToggle",
                "Event-driven auto-farm (no rescan)", this.homelandFarmAutoEventDriven,
                new System.Action<bool>(this.OnUguiHomelandFarmEventDrivenToggled));
            PlaceUguiTopLeft(handle.EventDrivenToggle.gameObject, 8f, 958f, panelW, 28f);

            // -------- 8. Status card (y=978, h=52 — :22501-22508) --------
            // Headerless panel (the source box carries no label — Animal Care's "" precedent).
            GameObject statusCard = this.CreateUguiSettingsMainPanel(scrollContent, "StatusPanel",
                string.Empty);
            PlaceUguiTopLeft(statusCard, 8f, 996f, panelW, 52f);

            // statusStyle: 11pt, wordWrap, uiText @ 0.82 (:22227-22228). Text painted by the
            // sync pass (key-then-localize — file header).
            handle.StatusShown = string.Empty;
            handle.StatusLabel = this.CreateUguiLabel(statusCard.transform, "StatusText",
                string.Empty, 11f,
                new Color(textColor.r, textColor.g, textColor.b, 0.82f), false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, 16f, 10f, rowW, 36f);

            // -------- 9. Stop button (y=1040 — :22510-22515) --------
            handle.StopButton = this.CreateUguiPrimaryButton(scrollContent, "StopButton",
                this.L("homeland_farm.stop"),
                new System.Action(this.OnUguiHomelandFarmStopClicked));
            PlaceUguiTopLeft(handle.StopButton, 8f, 1058f, 160f, 30f);

            // Full cursor replay: the source returns y + 40 → 1080 (file header chain).
            this.SetUguiScrollContentHeight(scrollContent, 1098f);

            // Seed every dynamic state once (texts, interactables, segment styles, selector
            // rows, Start/Stop swap) via the same pass the processor runs. The warmup kick,
            // eager fert refresh, and the radius debounce machine deliberately stay OUT of the
            // builder — they are draw-cadence behaviors and belong to the gated processor only.
            this.SyncUguiHomelandFarmDynamicState(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesHomelandFarm = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesHomelandFarmOnUpdate()
        {
            UguiShellNewFeaturesHomelandFarmHandle handle = this.uguiShellNewFeaturesHomelandFarm;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellHomelandFarmSubIndex))
            {
                return;
            }

            try
            {
                // Source top-of-draw (:22218) — cheap idempotent warmup kick, once per gated frame.
                this.EnsureHomelandFarmWarmupStarted();

                // Eager fertilizer auto-refresh-if-empty (:22256-22259) — the source re-checks on
                // every repaint of the tab; the per-gated-frame check is the equivalent cadence.
                if (this.homelandFarmScannedFertilizers.Count == 0)
                {
                    this.RefreshHomelandFarmFertilizers();
                }

                // The radius debounce state machine, source-verbatim over the SHARED fields
                // (:22320-22337): first-draw sentinel adopts without scheduling; a change re-arms
                // the deadline; the save fires only after the value settles.
                if (this.homelandFarmWaterRadiusLastSeen < 0f)
                {
                    // First draw — adopt the loaded value without scheduling a save.
                    this.homelandFarmWaterRadiusLastSeen = this.homelandFarmWaterRadius;
                }
                else if (this.homelandFarmWaterRadius != this.homelandFarmWaterRadiusLastSeen)
                {
                    this.homelandFarmWaterRadiusLastSeen = this.homelandFarmWaterRadius;
                    this.homelandFarmWaterRadiusSavePending = true;
                    this.homelandFarmWaterRadiusSaveAt = Time.realtimeSinceStartup
                        + HomelandFarmRadiusSaveDebounceSeconds;
                }

                if (this.homelandFarmWaterRadiusSavePending
                    && Time.realtimeSinceStartup >= this.homelandFarmWaterRadiusSaveAt)
                {
                    this.homelandFarmWaterRadiusSavePending = false;
                    this.PersistHomelandFarmRadius();
                }

                this.SyncUguiHomelandFarmDynamicState(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/HomelandFarm content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Every dynamic bit of the tab, recomputed like the IMGUI drawer's own per-repaint reads.
        // Live conditions are re-evaluated in FULL each pass (busy/warmup/fertilizer-selected all
        // change from background activity — the Animal Care live-gate lesson); label writes go
        // through cached-string diffs; interactable/SetActive writes self-diff.
        private void SyncUguiHomelandFarmDynamicState(UguiShellNewFeaturesHomelandFarmHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            // :22229-22230 + :22261.
            bool busy = this.IsHomelandFarmBusy();
            bool farmInteractive = !busy && this.IsHomelandFarmWarmupReady();
            bool hasSelectedFertilizer = this.TryHomelandFarmGetSelectedFertilizer(out _);

            // -------- AUTO FARMING --------
            this.SetUguiButtonInteractable(handle.CaptureButton,
                farmInteractive && !this.homelandFarmAutoRunning); // :22239
            this.SyncUguiSelfLabelText(handle.CapturedLabel, ref handle.CapturedShown,
                this.homelandFarmAutoCaptured
                    ? this.LF("homeland_farm.auto_captured", this.homelandFarmAutoPlanterCount)
                    : this.L("homeland_farm.auto_not_captured")); // :22246-22250

            this.SyncUguiToggleFromField(handle.AutoFertilizeToggle, this.homelandFarmAutoFertilizeEnabled);
            this.SetUguiHomelandFarmToggleInteractable(handle.AutoFertilizeToggle,
                farmInteractive && !this.homelandFarmAutoRunning && hasSelectedFertilizer); // :22262

            // Start vs Stop — same rect, swapped on the running flag (:22276-22296). The Stop
            // button is unconditionally clickable in the source (GUI.enabled = true there).
            bool running = this.homelandFarmAutoRunning;
            SetUguiGoActive(handle.AutoStopButton, running);
            SetUguiGoActive(handle.AutoStartButton, !running);
            if (!running)
            {
                this.SetUguiButtonInteractable(handle.AutoStartButton,
                    farmInteractive
                    && this.homelandFarmAutoCaptured
                    && this.homelandFarmAutoPlanterCount > 0
                    && this.homelandFarmScannedSeeds.Count > 0); // :22286-22289
            }

            // -------- FARM RADIUS --------
            if (handle.RadiusSlider != null
                && handle.RadiusSlider.value != this.homelandFarmWaterRadius)
            {
                handle.RadiusSlider.SetValueWithoutNotify(this.homelandFarmWaterRadius);
            }
            this.SyncUguiSelfLabelText(handle.RadiusValueLabel, ref handle.RadiusValueShown,
                $"{this.homelandFarmWaterRadius:F0}m"); // :22313

            // -------- CROPS --------
            if (handle.SeedSegments != null && handle.SeedSegments.Length == 3)
            {
                this.ApplyUguiHomelandFarmSegmentState(handle.SeedSegments[0],
                    this.homelandFarmSeedStorage == HomelandFarmStorageSource.Backpack);
                this.ApplyUguiHomelandFarmSegmentState(handle.SeedSegments[1],
                    this.homelandFarmSeedStorage == HomelandFarmStorageSource.Warehouse);
                this.ApplyUguiHomelandFarmSegmentState(handle.SeedSegments[2],
                    this.homelandFarmSeedStorage == HomelandFarmStorageSource.Both);
                for (int i = 0; i < handle.SeedSegments.Length; i++)
                {
                    this.SetUguiButtonInteractable(handle.SeedSegments[i].Root, farmInteractive); // :22348
                }
            }
            this.SetUguiButtonInteractable(handle.RefreshSeedsButton, farmInteractive);
            this.SyncUguiSelfLabelText(handle.SeedsCachedLabel, ref handle.SeedsCachedShown,
                this.homelandFarmScannedSeeds.Count > 0
                    ? this.LF("homeland_farm.cached_seeds", this.homelandFarmScannedSeeds.Count)
                    : this.L("homeland_farm.press_refresh_seeds")); // :22361-22365
            this.SyncUguiHomelandFarmSelector(handle.SeedSelector, farmInteractive);

            // -------- FERTILIZER --------
            if (handle.FertSegments != null && handle.FertSegments.Length == 3)
            {
                this.ApplyUguiHomelandFarmSegmentState(handle.FertSegments[0],
                    this.homelandFarmFertStorage == HomelandFarmStorageSource.Backpack);
                this.ApplyUguiHomelandFarmSegmentState(handle.FertSegments[1],
                    this.homelandFarmFertStorage == HomelandFarmStorageSource.Warehouse);
                this.ApplyUguiHomelandFarmSegmentState(handle.FertSegments[2],
                    this.homelandFarmFertStorage == HomelandFarmStorageSource.Both);
                for (int i = 0; i < handle.FertSegments.Length; i++)
                {
                    this.SetUguiButtonInteractable(handle.FertSegments[i].Root, farmInteractive); // :22385
                }
            }
            this.SetUguiButtonInteractable(handle.RefreshFertilizersButton, farmInteractive);
            this.SyncUguiSelfLabelText(handle.FertCachedLabel, ref handle.FertCachedShown,
                this.homelandFarmScannedFertilizers.Count > 0
                    ? this.LF("homeland_farm.cached_fertilizers", this.homelandFarmScannedFertilizers.Count)
                    : this.L("homeland_farm.press_refresh_fertilizers")); // :22398-22402
            this.SyncUguiHomelandFarmSelector(handle.FertSelector, farmInteractive);

            // -------- OPERATIONS (all 9 on farmInteractive alone, :22427) --------
            if (handle.OpsButtons != null)
            {
                for (int i = 0; i < handle.OpsButtons.Length; i++)
                {
                    this.SetUguiButtonInteractable(handle.OpsButtons[i], farmInteractive);
                }
            }

            // -------- Trailing toggles (always enabled in the source) --------
            this.SyncUguiToggleFromField(handle.EventDiagToggle, this.homelandFarmEventDiagEnabled);
            this.SyncUguiToggleFromField(handle.EventDrivenToggle, this.homelandFarmAutoEventDriven);

            // -------- Status card: pick the KEY, then ONE L() call (:22504-22507) --------
            string statusKey = this.IsHomelandFarmWarmupReady()
                ? (this.homelandFarmLastStatus ?? "homeland_farm.status_idle")
                : "homeland_farm.status_warming";
            this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown, this.L(statusKey));

            // -------- Stop button (:22510) --------
            this.SetUguiButtonInteractable(handle.StopButton, this.homelandFarmCoroutine != null);
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // NO SaveKeybinds calls anywhere in this file (file header).
        // ----------------------------------------------------------------------------------------

        // :22240-22243.
        private void OnUguiHomelandFarmCaptureClicked()
        {
            this.CaptureHomelandFarmAutoPlanters();
        }

        // :22261-22271 — the WRITE guard re-evaluates hasSelectedFertilizer at click time and
        // gates BOTH the field write and the bespoke persist on it (never SaveKeybinds). When the
        // guard fails, the field stays unchanged and the per-frame SyncUguiToggleFromField snaps
        // the checkbox back — the UGUI analog of IMGUI's next-repaint re-read.
        private void OnUguiHomelandFarmAutoFertilizeToggled(bool value)
        {
            bool hasSelectedFertilizer = this.TryHomelandFarmGetSelectedFertilizer(out _);
            if (hasSelectedFertilizer && value != this.homelandFarmAutoFertilizeEnabled)
            {
                this.homelandFarmAutoFertilizeEnabled = value;
                this.PersistHomelandFarmAutoFertilizeSetting();
            }
        }

        // :22278-22282 — stop + write the status KEY (localized only at display time).
        private void OnUguiHomelandFarmAutoStopClicked()
        {
            this.StopHomelandFarmCoroutine();
            this.homelandFarmLastStatus = "homeland_farm.status_stopped";
        }

        // :22290-22293 — StartHomelandFarmAuto is internally guarded, so a same-frame race click
        // against the once-per-frame interactable sync is harmless (Animal Care precedent).
        private void OnUguiHomelandFarmAutoStartClicked()
        {
            this.StartHomelandFarmAuto();
        }

        // :22315 — the field write; Mathf.Round mirrors the source snap (wholeNumbers already
        // snaps UGUI-side). The debounce machine in the processor observes the change; the value
        // label follows via the per-frame cached diff.
        private void OnUguiHomelandFarmRadiusChanged(float value)
        {
            this.homelandFarmWaterRadius = Mathf.Round(value);
        }

        // :22349-22354 — a guarded plain field write, ZERO other side effects, NO persistence.
        private void OnUguiHomelandFarmSeedStoragePicked(HomelandFarmStorageSource value)
        {
            if (this.homelandFarmSeedStorage != value)
            {
                this.homelandFarmSeedStorage = value;
            }
        }

        // :22386-22391 — same shape for the fertilizer source.
        private void OnUguiHomelandFarmFertStoragePicked(HomelandFarmStorageSource value)
        {
            if (this.homelandFarmFertStorage != value)
            {
                this.homelandFarmFertStorage = value;
            }
        }

        // :22356-22359.
        private void OnUguiHomelandFarmRefreshSeedsClicked()
        {
            this.RefreshHomelandFarmSeeds();
        }

        // :22393-22396.
        private void OnUguiHomelandFarmRefreshFertilizersClicked()
        {
            this.RefreshHomelandFarmFertilizers();
        }

        // Operations (:22428-22475) — each button routes straight to its Start*/Log* method with
        // the source's exact arguments.
        private void OnUguiHomelandFarmWaterInRadiusClicked()
        {
            this.StartHomelandFarmWater(HomelandFarmWaterMode.InRadius, silent: false);
        }

        private void OnUguiHomelandFarmHarvestCropsClicked()
        {
            this.StartHomelandFarmHarvestCrops(silent: false);
        }

        private void OnUguiHomelandFarmWeedAllClicked()
        {
            this.StartHomelandFarmWeedAll(silent: false);
        }

        private void OnUguiHomelandFarmCollectPlantSeedsClicked()
        {
            this.StartHomelandFarmCollectPlantSeeds(silent: false);
        }

        private void OnUguiHomelandFarmCollectDormantClicked()
        {
            this.StartHomelandFarmPickFlowers(dormantOnly: true, silent: false);
        }

        private void OnUguiHomelandFarmSowClicked()
        {
            this.StartHomelandFarmSowAll(silent: false);
        }

        private void OnUguiHomelandFarmCollectFlowersClicked()
        {
            this.StartHomelandFarmPickFlowers(dormantOnly: false, silent: false);
        }

        private void OnUguiHomelandFarmFertilizeClicked()
        {
            this.StartHomelandFarmFertilizeAll(silent: false);
        }

        private void OnUguiHomelandFarmLogWaterRadiusClicked()
        {
            this.LogHomelandFarmRadiusWaterDiagnostics();
        }

        // :22484-22490 — the source's change guard kept verbatim: field write + the bespoke
        // OnHomelandFarmEventDiagToggled() call, nothing else (no save — file header).
        private void OnUguiHomelandFarmEventDiagToggled(bool value)
        {
            if (value == this.homelandFarmEventDiagEnabled)
            {
                return;
            }
            this.homelandFarmEventDiagEnabled = value;
            this.OnHomelandFarmEventDiagToggled();
        }

        // :22496-22497 — the flag-only shape: an unconditional assignment, no guard, no save,
        // no cascade (the simplest control in this whole migration; do not add anything).
        private void OnUguiHomelandFarmEventDrivenToggled(bool value)
        {
            this.homelandFarmAutoEventDriven = value;
        }

        // :22510-22515 — same stop + status-KEY write as the auto card's Stop.
        private void OnUguiHomelandFarmStopClicked()
        {
            this.StopHomelandFarmCoroutine();
            this.homelandFarmLastStatus = "homeland_farm.status_stopped";
        }
    }
}
