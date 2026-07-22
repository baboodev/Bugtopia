using System;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, round 4 (migration plan: cosmic-waddling-rainbow.md):
    // Self→Building sub-tab + the floating Building Move Panel, together as one atomic unit —
    // the plan's Guiding Principle 4 case. The IMGUI DrawFreePlacementControls
    // (BuildingFreeRotateFeature.cs:232) draws the SAME function into two places per frame
    // (DrawBuildingTab and the floating DrawBuildingMovePanelWindow); retained-mode UGUI cannot
    // share GameObjects between two parents, so this file splits it into:
    //
    //  BUILDER — BuildUguiFreePlacementControls constructs ONE independent widget tree per
    //    consumer. Called twice: once by BuildUguiShellSelfBuildingContent (the shell's Building
    //    sub-tab cell) and once by BuildUguiBuildingMovePanel (the new floating window). The two
    //    trees never share GameObjects — only the handle CLASS shape.
    //
    //  BIND — the field-read/write/notify/apply behavior lives in ONE set of shared handlers
    //    (OnUguiBuildingFreeAngleToggled … OnUguiBuildingJogStepClicked) that BOTH trees' UI
    //    events route into, plus ONE shared per-instance render sync
    //    (SyncUguiFreePlacementControls) each consumer's per-frame processor runs for its own
    //    tree. There is exactly one implementation of "what happens when Angle toggles"; the
    //    trees differ only in which GameObjects get refreshed.
    //
    // Ground rules (same as rounds 1-3):
    //  - The IMGUI drawers (DrawBuildingTab / DrawFreePlacementControls / DrawBuildingMovePanel*
    //    / DrawBuildingAxisRow / DrawBuildingRotRow) and the state driver
    //    (UpdateBuildingMovePanelState) stay fully functional and untouched — this file only
    //    READS the same fields (buildingFreeAngleEnabled, buildingMovePanelGodMode,
    //    buildingMovePanelObjPos/Yaw/HasPos, the static buildingIgnoreSurfaceLimit /
    //    buildingIgnoreRangeHeight, …) and CALLS the same action methods (TryNudgeFocused /
    //    TryRotateFocused / TrySetBuildingPlaneHeight / AddMenuNotification).
    //  - Wiring is by STATIC display-position index (UguiShellSelfTabIndex = 0 +
    //    UguiShellSelfBuildingSubIndex = 1, declared next to their round-1/2/3 siblings in
    //    UguiShellTabIndices.cs). Self's other four sub-tabs are NOT migrated this round.
    //  - Toggles are kit CHECKBOXES (round-2 deviation note applies — the IMGUI source here uses
    //    raw GUI.Toggle, not the switch pill, so checkboxes are also the closer visual match).
    //  - Notification strings/colors are copied verbatim from DrawFreePlacementControls:323-346
    //    (angle+grid share ONE combined text; the other three have their own; unlocalized).
    //
    // The delta-jog contract (DrawBuildingAxisRow/DrawBuildingRotRow — do NOT simplify):
    //  - The backing float (e.g. buildingFreeX) may legitimately sit OUTSIDE the slider's
    //    nominal range: the −/+ buttons apply an UNCLAMPED step straight to the field, and the
    //    slider only ever DISPLAYS a clamped view of it.
    //  - UGUI mapping: every refresh pushes the CLAMPED view via Slider.SetValueWithoutNotify
    //    (NEVER .value = — that fires the event), so onValueChanged only ever fires from a
    //    genuine user drag. The drag handler mirrors IMGUI's adoption guard: it rounds the
    //    slider output and adopts it ONLY when it differs from the clamped view of the current
    //    field value (Mathf.Approximately) — a drag parked exactly on the clamp edge leaves an
    //    out-of-range field untouched, exactly like `if (!Approximately(sliderOut, clamped))`.
    //  - After any change, the shared apply computes delta = value - applied, stores
    //    applied = value, and calls TryNudgeFocused(axisUnit * delta) / TryRotateFocused(axis,
    //    delta). The Plane row is the deliberate exception: it compares value vs
    //    buildingPlaneHeightApplied but sends the ABSOLUTE height via TrySetBuildingPlaneHeight
    //    (it sets a plane, it does not nudge an object).
    //  - Step sources differ per row family and are not conflated: Height/X/Z use
    //    clamp(buildingFreeGridCell, 0.01..0.25); rotation rows use a fixed 15°; Plane a fixed
    //    0.5 m (both slider snap and buttons).
    //
    // Floating window (Part C):
    //  - Kit window (CreateUguiWindow, draggable via its 24px top strip — the UGUI analog of
    //    IMGUI's GUI.DragWindow top strip). sortingOrder 29350 — mod band (20000..30000),
    //    distinct from Overlay(29300)/Shell(29400)/PoC(29500), below the Dropdown ceiling.
    //  - No close button — visibility is entirely state-driven, same as IMGUI (verified:
    //    DrawBuildingMovePanel has no user-facing close affordance).
    //  - Visibility = buildingMovePanelActive && !showMenu && !(UGUI shell visible): the same
    //    anti-redundancy rule IMGUI applies for its own menu, extended to the second menu system
    //    (whichever menu is open already shows its own copy of these controls). The IMGUI
    //    window's own condition is untouched.
    //  - Height mirrors IMGUI's content-fit swap (368/186): relayout-on-signature-change — when
    //    buildingMovePanelGodMode flips, the god rows SetActive-toggle and the window resizes
    //    between UguiBuildingMovePanelBaseH/GodH with the TOP edge pinned (panel pivot is
    //    center, so half the height delta is folded back into anchoredPosition), then re-clamps.
    //  - Input ownership: NEW floating registry entry "UguiBuildingMovePanel"
    //    (isModal=false, pointer-over via the kit's IsUguiWindowPointerOver). The existing
    //    "BuildingMovePanel" entry (the IMGUI window's own, fed by buildingMovePanelMouseOver)
    //    is deliberately not reused or modified.
    //  - Scale follows the shared persisted uiScale (IMGUI parity: DrawBuildingMovePanel applies
    //    the GetUiScale() matrix) — the Phase 2e shell idiom: seed at build, per-frame re-sync
    //    only on change. Theme reload: own "UguiBuildingMovePanel" rebuilder (destroy + rebuild,
    //    state-preserving) because this window lives outside the shell's rebuilder.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Geometry constants
        // ----------------------------------------------------------------------------------------

        private const float UguiBuildingMovePanelW = 380f;
        private const float UguiBuildingMovePanelTitleH = 24f;   // drag strip (IMGUI: 22px)
        private const float UguiBuildingMovePanelBaseH = 206f;   // strip 24 + coord + 5 rows
        private const float UguiBuildingMovePanelGodH = 402f;    // + the 7 god-mode jog rows
        private const int UguiBuildingMovePanelSortingOrder = 29350; // Overlay 29300 < this < Shell 29400

        private const float UguiFreePlacementRowStep = 28f;
        private const float UguiFreePlacementBaseRowsH = 5f * UguiFreePlacementRowStep;  // 140
        private const float UguiFreePlacementGodRowsH = 7f * UguiFreePlacementRowStep;   // 196
        private const float UguiFreePlacementFullH = UguiFreePlacementBaseRowsH + UguiFreePlacementGodRowsH;

        // ----------------------------------------------------------------------------------------
        // Jog-row model (Height / X / Z / Plane / rX / rY / rZ — DrawFreePlacementControls:271-321)
        // ----------------------------------------------------------------------------------------

        private const int UguiBuildingJogRowHeightId = 0;
        private const int UguiBuildingJogRowXId = 1;
        private const int UguiBuildingJogRowZId = 2;
        private const int UguiBuildingJogRowPlaneId = 3;
        private const int UguiBuildingJogRowRotXId = 4;
        private const int UguiBuildingJogRowRotYId = 5;
        private const int UguiBuildingJogRowRotZId = 6;
        private const int UguiBuildingJogRowCount = 7;

        private const int UguiBuildingJogKindAxis = 0;   // 2-decimal rounding, grid-cell step, TryNudgeFocused delta
        private const int UguiBuildingJogKindPlane = 1;  // 0.5m snap, absolute TrySetBuildingPlaneHeight
        private const int UguiBuildingJogKindRot = 2;    // whole degrees, 15° step, TryRotateFocused delta

        private struct UguiBuildingJogRowDef
        {
            public string LabelKey;
            public bool Localize;   // IMGUI localizes "Height"/"Plane" but not X/Z/rX/rY/rZ
            public float Lo;
            public float Hi;
            public int Kind;

            public UguiBuildingJogRowDef(string labelKey, bool localize, float lo, float hi, int kind)
            {
                this.LabelKey = labelKey;
                this.Localize = localize;
                this.Lo = lo;
                this.Hi = hi;
                this.Kind = kind;
            }
        }

        // Row order copied from DrawFreePlacementControls: Height, X, Z, Plane, rX, rY, rZ.
        private static readonly UguiBuildingJogRowDef[] UguiBuildingJogRowDefs = new UguiBuildingJogRowDef[]
        {
            new UguiBuildingJogRowDef("Height", true, 0f, 8f, UguiBuildingJogKindAxis),
            new UguiBuildingJogRowDef("X", false, -8f, 8f, UguiBuildingJogKindAxis),
            new UguiBuildingJogRowDef("Z", false, -8f, 8f, UguiBuildingJogKindAxis),
            new UguiBuildingJogRowDef("Plane", true, 0f, 24f, UguiBuildingJogKindPlane),
            new UguiBuildingJogRowDef("rX", false, -180f, 180f, UguiBuildingJogKindRot),
            new UguiBuildingJogRowDef("rY", false, -180f, 180f, UguiBuildingJogKindRot),
            new UguiBuildingJogRowDef("rZ", false, -180f, 180f, UguiBuildingJogKindRot)
        };

        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — one per widget tree, never singletons for the tree itself)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiBuildingJogRowHandle
        {
            public Slider Slider;
            public GameObject ValueLabel;
            public string ValueShown;   // SetText only on change (TMP re-layout hygiene)
        }

        private sealed class UguiFreePlacementControlsHandle
        {
            public GameObject Root;
            public Toggle AngleToggle;
            public Slider AngleSlider;
            public GameObject AngleValueLabel;
            public string AngleValueShown;
            public Toggle GridToggle;
            public Slider GridSlider;
            public GameObject GridValueLabel;
            public string GridValueShown;
            public Toggle SurfaceToggle;
            public Toggle RangeToggle;
            public Toggle OverlapToggle;
            public GameObject GodRowsRoot;
            public readonly UguiBuildingJogRowHandle[] JogRows = new UguiBuildingJogRowHandle[UguiBuildingJogRowCount];
            public bool GodRowsVisible;
        }

        private sealed class UguiShellSelfBuildingHandle
        {
            public GameObject Root;
            public UguiFreePlacementControlsHandle Controls;
            public int ErrorCount;      // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private sealed class UguiBuildingMovePanelHandle
        {
            public UguiWindowHandle Window;
            public UguiFreePlacementControlsHandle Controls;
            public GameObject CoordLabel;
            public string CoordShown;
            public float LastSyncedUiScale = -1f;  // Phase 2e shell scale-sync idiom
            public int ErrorCount;
        }

        private UguiShellSelfBuildingHandle uguiShellSelfBuilding;
        private UguiBuildingMovePanelHandle uguiBuildingMovePanel;
        private bool uguiBuildingMovePanelBuildFailed;

        // ----------------------------------------------------------------------------------------
        // BUILDER — one independent Free Placement widget tree per call (Guiding Principle 4).
        // The container is always built at full (god-mode) height; the god rows sub-container is
        // SetActive-driven by the shared sync. All UI events route into the shared BIND handlers.
        // ----------------------------------------------------------------------------------------

        private UguiFreePlacementControlsHandle BuildUguiFreePlacementControls(Transform parent, float x, float y, float w)
        {
            UguiFreePlacementControlsHandle c = new UguiFreePlacementControlsHandle();
            GameObject root = this.CreateUguiGo("FreePlacementControls", parent);
            PlaceUguiTopLeft(root, x, y, w, UguiFreePlacementFullH);
            c.Root = root;

            Color text = this.UguiKitTextColor();
            Color valueColor = new Color(text.r, text.g, text.b, 0.9f); // IMGUI `val` style alpha

            float sliderX = 120f;
            float sliderW = w - 186f;      // leaves 58px value + 2x28px buttons + gaps on jog rows
            float valueX = w - 60f;

            // Row 1 — Angle: checkbox + 1..90 whole-number slider + "{step}°" value.
            float rowY = 0f;
            c.AngleToggle = this.CreateUguiCheckbox(root.transform, "AngleToggle",
                this.L("Angle"), this.buildingFreeAngleEnabled,
                new System.Action<bool>(this.OnUguiBuildingFreeAngleToggled));
            PlaceUguiTopLeft(c.AngleToggle.gameObject, 0f, rowY, 112f, 24f);
            c.AngleSlider = this.CreateUguiSlider(root.transform, "AngleSlider",
                1f, 90f, Mathf.Clamp(this.buildingFreeAngleStep, 1, 90), true,
                new System.Action<float>(this.OnUguiBuildingAngleStepChanged));
            PlaceUguiTopLeft(c.AngleSlider.gameObject, sliderX, rowY + 3f, sliderW, 20f);
            c.AngleValueShown = this.buildingFreeAngleStep + "°";
            c.AngleValueLabel = this.CreateUguiLabel(root.transform, "AngleValue", c.AngleValueShown, 11f, valueColor, false);
            PlaceUguiTopLeft(c.AngleValueLabel, valueX, rowY + 3f, 58f, 20f);

            // Row 2 — Grid: checkbox + 0.01..0.25 slider (2-decimal) + "{cell:0.00}m" value.
            rowY += UguiFreePlacementRowStep;
            c.GridToggle = this.CreateUguiCheckbox(root.transform, "GridToggle",
                this.L("Grid"), this.buildingFreeGridEnabled,
                new System.Action<bool>(this.OnUguiBuildingFreeGridToggled));
            PlaceUguiTopLeft(c.GridToggle.gameObject, 0f, rowY, 112f, 24f);
            c.GridSlider = this.CreateUguiSlider(root.transform, "GridSlider",
                0.01f, 0.25f, Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f), false,
                new System.Action<float>(this.OnUguiBuildingGridCellChanged));
            PlaceUguiTopLeft(c.GridSlider.gameObject, sliderX, rowY + 3f, sliderW, 20f);
            c.GridValueShown = this.buildingFreeGridCell.ToString("0.00") + "m";
            c.GridValueLabel = this.CreateUguiLabel(root.transform, "GridValue", c.GridValueShown, 11f, valueColor, false);
            PlaceUguiTopLeft(c.GridValueLabel, valueX, rowY + 3f, 58f, 20f);

            // Rows 3-5 — the three plain checkboxes. buildingIgnoreSurfaceLimit and
            // buildingIgnoreRangeHeight are STATIC fields (the detour hooks read them without an
            // instance); bypassOverlapEnabled is an instance field — same split as IMGUI.
            rowY += UguiFreePlacementRowStep;
            c.SurfaceToggle = this.CreateUguiCheckbox(root.transform, "SurfaceToggle",
                this.L("No surface limit"), buildingIgnoreSurfaceLimit,
                new System.Action<bool>(this.OnUguiBuildingSurfaceLimitToggled));
            PlaceUguiTopLeft(c.SurfaceToggle.gameObject, 0f, rowY, w, 24f);

            rowY += UguiFreePlacementRowStep;
            c.RangeToggle = this.CreateUguiCheckbox(root.transform, "RangeToggle",
                this.L("No range/height limit"), buildingIgnoreRangeHeight,
                new System.Action<bool>(this.OnUguiBuildingRangeHeightToggled));
            PlaceUguiTopLeft(c.RangeToggle.gameObject, 0f, rowY, w, 24f);

            rowY += UguiFreePlacementRowStep;
            c.OverlapToggle = this.CreateUguiCheckbox(root.transform, "OverlapToggle",
                this.L("Bypass Overlap"), this.bypassOverlapEnabled,
                new System.Action<bool>(this.OnUguiBuildingBypassOverlapToggled));
            PlaceUguiTopLeft(c.OverlapToggle.gameObject, 0f, rowY, w, 24f);

            // God-mode jog rows — one sub-container the sync SetActive-toggles as a unit.
            GameObject godRoot = this.CreateUguiGo("GodRows", root.transform);
            PlaceUguiTopLeft(godRoot, 0f, UguiFreePlacementBaseRowsH, w, UguiFreePlacementGodRowsH);
            c.GodRowsRoot = godRoot;
            for (int rowId = 0; rowId < UguiBuildingJogRowCount; rowId++)
            {
                c.JogRows[rowId] = this.BuildUguiBuildingJogRow(godRoot.transform, rowId,
                    rowId * UguiFreePlacementRowStep, w, valueColor);
            }

            c.GodRowsVisible = this.buildingMovePanelGodMode;
            SetUguiGoActive(godRoot, c.GodRowsVisible);
            return c;
        }

        // ONE generic jog row: label + slider + value + "−"/"+" — covers the axis, plane and
        // rotation rows (they differ only in range/rounding/format/step/apply, all owned by the
        // shared bind layer via rowId).
        private UguiBuildingJogRowHandle BuildUguiBuildingJogRow(Transform parent, int rowId, float y, float w, Color valueColor)
        {
            UguiBuildingJogRowDef def = UguiBuildingJogRowDefs[rowId];
            UguiBuildingJogRowHandle row = new UguiBuildingJogRowHandle();
            GameObject rowGo = this.CreateUguiGo("Jog" + def.LabelKey, parent);
            PlaceUguiTopLeft(rowGo, 0f, y, w, 24f);

            GameObject label = this.CreateUguiLabel(rowGo.transform, "Label",
                def.Localize ? this.L(def.LabelKey) : def.LabelKey, 11f, valueColor, false);
            PlaceUguiTopLeft(label, 0f, 3f, 52f, 20f);

            int rowIdCopy = rowId; // capture a copy for the closures
            float value = this.GetUguiBuildingJogValue(rowId);
            row.Slider = this.CreateUguiSlider(rowGo.transform, "Slider",
                def.Lo, def.Hi, this.ComputeUguiBuildingJogClampedView(rowId, value),
                def.Kind == UguiBuildingJogKindRot,
                new System.Action<float>(v => this.OnUguiBuildingJogSliderChanged(rowIdCopy, v)));
            PlaceUguiTopLeft(row.Slider.gameObject, 56f, 3f, w - 186f, 20f);

            row.ValueShown = this.FormatUguiBuildingJogValue(rowId, value);
            row.ValueLabel = this.CreateUguiLabel(rowGo.transform, "Value", row.ValueShown, 11f, valueColor, false);
            PlaceUguiTopLeft(row.ValueLabel, w - 122f, 3f, 58f, 20f);

            GameObject minus = this.CreateUguiSecondaryButton(rowGo.transform, "Minus", "-",
                new System.Action(() => this.OnUguiBuildingJogStepClicked(rowIdCopy, false)));
            PlaceUguiTopLeft(minus, w - 60f, 1f, 28f, 22f);
            GameObject plus = this.CreateUguiSecondaryButton(rowGo.transform, "Plus", "+",
                new System.Action(() => this.OnUguiBuildingJogStepClicked(rowIdCopy, true)));
            PlaceUguiTopLeft(plus, w - 28f, 1f, 28f, 22f);

            return row;
        }

        // ----------------------------------------------------------------------------------------
        // BIND — shared field access + side effects. ONE implementation per control; both widget
        // trees' events land here. Notification strings/colors verbatim from
        // DrawFreePlacementControls:323-346. Every handler guards on "value actually changed" so
        // a redundant event (or the WithoutNotify re-syncs, which never fire these) cannot
        // replay side effects.
        // ----------------------------------------------------------------------------------------

        private void OnUguiBuildingFreeAngleToggled(bool value)
        {
            if (value == this.buildingFreeAngleEnabled)
            {
                return;
            }
            this.buildingFreeAngleEnabled = value;
            this.NotifyUguiBuildingAngleGridChanged();
        }

        private void OnUguiBuildingFreeGridToggled(bool value)
        {
            if (value == this.buildingFreeGridEnabled)
            {
                return;
            }
            this.buildingFreeGridEnabled = value;
            this.NotifyUguiBuildingAngleGridChanged();
        }

        // IMGUI fires ONE combined notification when either the angle or grid toggle changed.
        private void NotifyUguiBuildingAngleGridChanged()
        {
            this.AddMenuNotification(
                "Free build: angle=" + (this.buildingFreeAngleEnabled ? "on" : "off")
                + " grid=" + (this.buildingFreeGridEnabled ? "on" : "off"),
                new Color(0.45f, 1f, 0.55f));
        }

        // Slider-only writes — IMGUI adopts these unconditionally (no notification, no clamp
        // beyond the slider range) so the guards here exist purely to skip no-op events.
        private void OnUguiBuildingAngleStepChanged(float value)
        {
            int step = Mathf.RoundToInt(value);
            if (step == this.buildingFreeAngleStep)
            {
                return;
            }
            this.buildingFreeAngleStep = step;
        }

        private void OnUguiBuildingGridCellChanged(float value)
        {
            float cell = Mathf.Round(value * 100f) / 100f;
            if (Mathf.Abs(cell - this.buildingFreeGridCell) <= 0.0001f)
            {
                return;
            }
            this.buildingFreeGridCell = cell;
        }

        // buildingIgnoreSurfaceLimit / buildingIgnoreRangeHeight are STATIC (no this.) — the
        // detour apply/undo is driven from UpdateBuildingFreeSnapOverrides (OnUpdate), not here,
        // exactly like the IMGUI toggle.
        private void OnUguiBuildingSurfaceLimitToggled(bool value)
        {
            if (value == buildingIgnoreSurfaceLimit)
            {
                return;
            }
            buildingIgnoreSurfaceLimit = value;
            this.AddMenuNotification(
                "Surface limit " + (buildingIgnoreSurfaceLimit ? "off (place anywhere)" : "on"),
                new Color(0.45f, 1f, 0.55f));
        }

        private void OnUguiBuildingRangeHeightToggled(bool value)
        {
            if (value == buildingIgnoreRangeHeight)
            {
                return;
            }
            buildingIgnoreRangeHeight = value;
            this.AddMenuNotification(
                "Range/height limit " + (buildingIgnoreRangeHeight ? "off (place anywhere)" : "on"),
                new Color(0.45f, 1f, 0.55f));
        }

        private void OnUguiBuildingBypassOverlapToggled(bool value)
        {
            if (value == this.bypassOverlapEnabled)
            {
                return;
            }
            this.bypassOverlapEnabled = value;
            this.AddMenuNotification(
                "Bypass Overlap " + (this.bypassOverlapEnabled ? "Enabled" : "Disabled"),
                this.bypassOverlapEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // --- jog rows -----------------------------------------------------------------------

        private float GetUguiBuildingJogValue(int rowId)
        {
            switch (rowId)
            {
                case UguiBuildingJogRowHeightId: return this.buildingFloorHeight;
                case UguiBuildingJogRowXId: return this.buildingFreeX;
                case UguiBuildingJogRowZId: return this.buildingFreeZ;
                case UguiBuildingJogRowPlaneId: return this.buildingPlaneHeight;
                case UguiBuildingJogRowRotXId: return this.buildingRotX;
                case UguiBuildingJogRowRotYId: return this.buildingRotY;
                case UguiBuildingJogRowRotZId: return this.buildingRotZ;
                default: return 0f;
            }
        }

        private void SetUguiBuildingJogValue(int rowId, float value)
        {
            switch (rowId)
            {
                case UguiBuildingJogRowHeightId: this.buildingFloorHeight = value; break;
                case UguiBuildingJogRowXId: this.buildingFreeX = value; break;
                case UguiBuildingJogRowZId: this.buildingFreeZ = value; break;
                case UguiBuildingJogRowPlaneId: this.buildingPlaneHeight = value; break;
                case UguiBuildingJogRowRotXId: this.buildingRotX = value; break;
                case UguiBuildingJogRowRotYId: this.buildingRotY = value; break;
                case UguiBuildingJogRowRotZId: this.buildingRotZ = value; break;
            }
        }

        // The clamped VIEW the slider displays — per-kind rounding copied from the IMGUI rows:
        // axis rows Round(Clamp(v)·100)/100, plane Round(Clamp(v)/0.5)·0.5, rot Round(Clamp(v)).
        private float ComputeUguiBuildingJogClampedView(int rowId, float value)
        {
            UguiBuildingJogRowDef def = UguiBuildingJogRowDefs[rowId];
            float clamped = Mathf.Clamp(value, def.Lo, def.Hi);
            switch (def.Kind)
            {
                case UguiBuildingJogKindPlane: return Mathf.Round(clamped / 0.5f) * 0.5f;
                case UguiBuildingJogKindRot: return Mathf.Round(clamped);
                default: return Mathf.Round(clamped * 100f) / 100f;
            }
        }

        // Rounding applied to a genuine drag's slider output (same per-kind rules).
        private float RoundUguiBuildingJogSliderOutput(int rowId, float sliderValue)
        {
            switch (UguiBuildingJogRowDefs[rowId].Kind)
            {
                case UguiBuildingJogKindPlane: return Mathf.Round(sliderValue / 0.5f) * 0.5f;
                case UguiBuildingJogKindRot: return Mathf.Round(sliderValue);
                default: return Mathf.Round(sliderValue * 100f) / 100f;
            }
        }

        // Value text shows the UNCLAMPED backing value (IMGUI parity — the label is how an
        // out-of-range value stays visible while the slider pins at its edge).
        private string FormatUguiBuildingJogValue(int rowId, float value)
        {
            switch (UguiBuildingJogRowDefs[rowId].Kind)
            {
                case UguiBuildingJogKindPlane: return value.ToString("0.0") + "m";
                case UguiBuildingJogKindRot: return value.ToString("0") + "°";
                default: return value.ToString("0.00") + "m";
            }
        }

        // Genuine drag only: refreshes push the clamped view via SetValueWithoutNotify, so this
        // never fires from a re-sync. Adoption guard mirrors DrawBuildingAxisRow/DrawBuildingRotRow:
        // adopt the (rounded) slider output ONLY when it differs from the clamped view of the
        // CURRENT field value — otherwise an intentionally out-of-range field would be snapped
        // back into range by a drag parked on the clamp edge.
        private void OnUguiBuildingJogSliderChanged(int rowId, float sliderValue)
        {
            try
            {
                if (rowId < 0 || rowId >= UguiBuildingJogRowCount)
                {
                    return;
                }
                float value = this.GetUguiBuildingJogValue(rowId);
                float clamped = this.ComputeUguiBuildingJogClampedView(rowId, value);
                float rounded = this.RoundUguiBuildingJogSliderOutput(rowId, sliderValue);
                if (Mathf.Approximately(rounded, clamped))
                {
                    return; // fed-back clamped view / edge-parked drag — not an adoption
                }
                this.SetUguiBuildingJogValue(rowId, rounded);
                this.ApplyUguiBuildingJogPending(rowId);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Building jog slider error (row " + rowId + "): " + ex.Message);
            }
        }

        // −/+ buttons: UNCLAMPED step straight onto the backing field (may push it beyond the
        // slider range and it sticks). Step sources per family, verbatim from IMGUI: axis =
        // clamp(grid cell, 0.01..0.25) with 2-decimal rounding; plane = 0.5 (no rounding
        // needed); rotation = 15° (no rounding — IMGUI does value -= 15f raw).
        private void OnUguiBuildingJogStepClicked(int rowId, bool positive)
        {
            try
            {
                if (rowId < 0 || rowId >= UguiBuildingJogRowCount)
                {
                    return;
                }
                float value = this.GetUguiBuildingJogValue(rowId);
                float sign = positive ? 1f : -1f;
                switch (UguiBuildingJogRowDefs[rowId].Kind)
                {
                    case UguiBuildingJogKindPlane:
                        value += sign * 0.5f;
                        break;
                    case UguiBuildingJogKindRot:
                        value += sign * 15f;
                        break;
                    default:
                        float step = Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f);
                        value = Mathf.Round((value + sign * step) * 100f) / 100f;
                        break;
                }
                this.SetUguiBuildingJogValue(rowId, value);
                this.ApplyUguiBuildingJogPending(rowId);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Building jog step error (row " + rowId + "): " + ex.Message);
            }
        }

        // The shared value-vs-applied apply — the trailing block of each IMGUI row, verbatim:
        // axis/rot rows compute a DELTA against their *Applied twin and nudge/rotate the focused
        // object; the Plane row compares against buildingPlaneHeightApplied but sends the
        // ABSOLUTE height (it sets the build plane, not an object delta).
        private void ApplyUguiBuildingJogPending(int rowId)
        {
            switch (rowId)
            {
                case UguiBuildingJogRowHeightId:
                    if (!Mathf.Approximately(this.buildingFloorHeight, this.buildingFloorHeightApplied))
                    {
                        float d = this.buildingFloorHeight - this.buildingFloorHeightApplied;
                        this.buildingFloorHeightApplied = this.buildingFloorHeight;
                        this.TryNudgeFocused(new Vector3(0f, 1f, 0f) * d);
                    }
                    break;
                case UguiBuildingJogRowXId:
                    if (!Mathf.Approximately(this.buildingFreeX, this.buildingFreeXApplied))
                    {
                        float d = this.buildingFreeX - this.buildingFreeXApplied;
                        this.buildingFreeXApplied = this.buildingFreeX;
                        this.TryNudgeFocused(new Vector3(1f, 0f, 0f) * d);
                    }
                    break;
                case UguiBuildingJogRowZId:
                    if (!Mathf.Approximately(this.buildingFreeZ, this.buildingFreeZApplied))
                    {
                        float d = this.buildingFreeZ - this.buildingFreeZApplied;
                        this.buildingFreeZApplied = this.buildingFreeZ;
                        this.TryNudgeFocused(new Vector3(0f, 0f, 1f) * d);
                    }
                    break;
                case UguiBuildingJogRowPlaneId:
                    if (!Mathf.Approximately(this.buildingPlaneHeight, this.buildingPlaneHeightApplied))
                    {
                        this.buildingPlaneHeightApplied = this.buildingPlaneHeight;
                        this.TrySetBuildingPlaneHeight(this.buildingPlaneHeight);
                    }
                    break;
                case UguiBuildingJogRowRotXId:
                    if (!Mathf.Approximately(this.buildingRotX, this.buildingRotXApplied))
                    {
                        float d = this.buildingRotX - this.buildingRotXApplied;
                        this.buildingRotXApplied = this.buildingRotX;
                        this.TryRotateFocused(new Vector3(1f, 0f, 0f), d);
                    }
                    break;
                case UguiBuildingJogRowRotYId:
                    if (!Mathf.Approximately(this.buildingRotY, this.buildingRotYApplied))
                    {
                        float d = this.buildingRotY - this.buildingRotYApplied;
                        this.buildingRotYApplied = this.buildingRotY;
                        this.TryRotateFocused(new Vector3(0f, 1f, 0f), d);
                    }
                    break;
                case UguiBuildingJogRowRotZId:
                    if (!Mathf.Approximately(this.buildingRotZ, this.buildingRotZApplied))
                    {
                        float d = this.buildingRotZ - this.buildingRotZApplied;
                        this.buildingRotZApplied = this.buildingRotZ;
                        this.TryRotateFocused(new Vector3(0f, 0f, 1f), d);
                    }
                    break;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Shared per-instance render sync — run each frame by whichever consumer's surface is
        // active. Toggles via SetIsOnWithoutNotify, sliders via SetValueWithoutNotify (round-2
        // idiom: an external re-sync must never replay a control's own side effects — and for the
        // jog rows it is the mechanism that keeps onValueChanged drag-only). Also drives the
        // god-mode rows' SetActive off buildingMovePanelGodMode (the 0.1s
        // UpdateBuildingMovePanelState poll owns that field; read-only here).
        // ----------------------------------------------------------------------------------------

        private void SyncUguiFreePlacementControls(UguiFreePlacementControlsHandle c)
        {
            if (c == null || c.Root == null)
            {
                return;
            }

            this.SyncUguiToggleFromField(c.AngleToggle, this.buildingFreeAngleEnabled);
            this.SyncUguiToggleFromField(c.GridToggle, this.buildingFreeGridEnabled);
            this.SyncUguiToggleFromField(c.SurfaceToggle, buildingIgnoreSurfaceLimit);
            this.SyncUguiToggleFromField(c.RangeToggle, buildingIgnoreRangeHeight);
            this.SyncUguiToggleFromField(c.OverlapToggle, this.bypassOverlapEnabled);

            if (c.AngleSlider != null && Mathf.RoundToInt(c.AngleSlider.value) != this.buildingFreeAngleStep)
            {
                c.AngleSlider.SetValueWithoutNotify(this.buildingFreeAngleStep);
            }
            string angleText = this.buildingFreeAngleStep + "°";
            if (!string.Equals(angleText, c.AngleValueShown, StringComparison.Ordinal))
            {
                c.AngleValueShown = angleText;
                this.SetUguiLabelText(c.AngleValueLabel, angleText);
            }

            if (c.GridSlider != null && Mathf.Abs(c.GridSlider.value - this.buildingFreeGridCell) > 0.0005f)
            {
                c.GridSlider.SetValueWithoutNotify(this.buildingFreeGridCell);
            }
            string gridText = this.buildingFreeGridCell.ToString("0.00") + "m";
            if (!string.Equals(gridText, c.GridValueShown, StringComparison.Ordinal))
            {
                c.GridValueShown = gridText;
                this.SetUguiLabelText(c.GridValueLabel, gridText);
            }

            bool god = this.buildingMovePanelGodMode;
            if (god != c.GodRowsVisible)
            {
                c.GodRowsVisible = god;
                SetUguiGoActive(c.GodRowsRoot, god);
            }
            if (god)
            {
                for (int rowId = 0; rowId < UguiBuildingJogRowCount; rowId++)
                {
                    this.RefreshUguiBuildingJogRow(c, rowId);
                }
            }
        }

        private void RefreshUguiBuildingJogRow(UguiFreePlacementControlsHandle c, int rowId)
        {
            UguiBuildingJogRowHandle row = (c != null && rowId >= 0 && rowId < c.JogRows.Length)
                ? c.JogRows[rowId]
                : null;
            if (row == null)
            {
                return;
            }

            float value = this.GetUguiBuildingJogValue(rowId);
            float clamped = this.ComputeUguiBuildingJogClampedView(rowId, value);
            if (row.Slider != null && Mathf.Abs(row.Slider.value - clamped) > 0.001f)
            {
                row.Slider.SetValueWithoutNotify(clamped); // NEVER .value = — that fires the event
            }
            string text = this.FormatUguiBuildingJogValue(rowId, value);
            if (!string.Equals(text, row.ValueShown, StringComparison.Ordinal))
            {
                row.ValueShown = text;
                this.SetUguiLabelText(row.ValueLabel, text);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Part B — Self→Building sub-tab content (consumer 1 of the builder)
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellSelfSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellSelfTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellSelfTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellSelfTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // UGUI mirror of DrawBuildingTab: "Free Placement" header (IMGUI headerStyle is bold 14
        // in the PRIMARY text color — Spawn Vehicle round precedent) + one builder instance.
        // Handle assigned LAST (Research idiom) so a mid-build exception can never leave a
        // half-built handle syncing.
        private GameObject BuildUguiShellSelfBuildingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfBuilding = null;

            UguiShellSelfBuildingHandle handle = new UguiShellSelfBuildingHandle();
            GameObject block = this.CreateUguiGo("SelfBuildingContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            GameObject header = this.CreateUguiLabel(block.transform, "Header",
                this.L("Free Placement"), 14f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, pad, 12f, w - pad * 2f, 22f);

            handle.Controls = this.BuildUguiFreePlacementControls(block.transform, pad, 44f, w - pad * 2f);

            handle.Root = block;
            this.uguiShellSelfBuilding = handle;
            return block;
        }

        // Called every frame from ProcessUguiShellOnUpdate; skips in a few comparisons unless
        // the shell is visible ON Self→Building.
        private void ProcessUguiShellSelfBuildingOnUpdate()
        {
            UguiShellSelfBuildingHandle handle = this.uguiShellSelfBuilding;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfBuildingSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiFreePlacementControls(handle.Controls);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Building sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Part C — floating Building Move Panel (consumer 2 of the builder)
        // ----------------------------------------------------------------------------------------

        // Built lazily on the first frame it should show (no user toggle exists — visibility is
        // entirely state-driven). Registers its own theme rebuilder and its own FLOATING
        // input-ownership surface ("UguiBuildingMovePanel"); the IMGUI window's existing
        // "BuildingMovePanel" registration is untouched.
        private void BuildUguiBuildingMovePanel()
        {
            this.uguiBuildingMovePanel = null;
            UguiBuildingMovePanelHandle handle = null;
            try
            {
                handle = new UguiBuildingMovePanelHandle();
                handle.Window = this.CreateUguiWindow(
                    "BugtopiaUguiBuildingMovePanel",
                    null, // compact strip owned below — the kit's 18pt title is oversized here
                    null,
                    new Vector2(UguiBuildingMovePanelW, UguiBuildingMovePanelBaseH),
                    UguiBuildingMovePanelSortingOrder,
                    UguiBuildingMovePanelTitleH);
                Transform panelT = handle.Window.PanelRt;

                // IMGUI window body: bold 12 title, 11pt live coordinate line, controls below.
                GameObject title = this.CreateUguiLabel(panelT, "Title",
                    this.L("Free Placement"), 12f, this.UguiKitTextColor(), false);
                this.TrySetUguiLabelBold(title);
                PlaceUguiTopLeft(title, 12f, 3f, UguiBuildingMovePanelW - 24f, 18f);

                Color text = this.UguiKitTextColor();
                handle.CoordLabel = this.CreateUguiLabel(panelT, "Coords", "",
                    11f, new Color(text.r, text.g, text.b, 0.95f), false);
                PlaceUguiTopLeft(handle.CoordLabel, 12f, 26f, UguiBuildingMovePanelW - 24f, 18f);

                handle.Controls = this.BuildUguiFreePlacementControls(panelT, 14f, 50f, UguiBuildingMovePanelW - 28f);

                // Content-fit height for the CURRENT god state (BuildUguiFreePlacementControls
                // already applied the god rows' visibility), then scale + spawn position.
                this.ApplyUguiBuildingMovePanelHeight(handle);

                handle.LastSyncedUiScale = this.GetUiScale();
                this.SetUguiWindowScale(handle.Window, handle.LastSyncedUiScale);

                // Spawn near the top-left like IMGUI's default rect (14, 150), converted to the
                // centered-pivot canvas space at the current scale, then clamped.
                float s = Mathf.Max(handle.Window.Scale, 0.1f);
                float halfW = Screen.width / s * 0.5f;
                float halfH = Screen.height / s * 0.5f;
                handle.Window.PanelRt.anchoredPosition = new Vector2(
                    -halfW + UguiBuildingMovePanelW * 0.5f + 14f,
                    halfH - 150f - handle.Window.Size.y * 0.5f);
                this.ClampUguiWindowPosition(handle.Window);

                this.uguiBuildingMovePanel = handle;

                // Live theme reload — this window lives OUTSIDE the shell, so it needs its own
                // rebuilder (idempotent by name).
                this.RegisterUguiThemeRebuilder("UguiBuildingMovePanel",
                    new System.Action(this.RebuildUguiBuildingMovePanelForTheme));

                // Input ownership: FLOATING surface (not modal), pointer-over via the kit's
                // standard window hit test. Closures read the LIVE field on every call — never
                // capture the handle (theme rebuilds replace it).
                this.RegisterInputOwnershipSurface("UguiBuildingMovePanel", false,
                    () => this.uguiBuildingMovePanel != null
                        && this.IsUguiWindowVisible(this.uguiBuildingMovePanel.Window),
                    () => this.uguiBuildingMovePanel != null
                        && this.IsUguiWindowPointerOver(this.uguiBuildingMovePanel.Window));

                ModLogger.Msg("[UguiShell] Building Move Panel built (sortingOrder "
                    + UguiBuildingMovePanelSortingOrder + ", state-driven visibility)");
            }
            catch (Exception ex)
            {
                this.uguiBuildingMovePanelBuildFailed = true;
                try
                {
                    if (handle != null && handle.Window != null && handle.Window.Root != null)
                    {
                        Object.Destroy(handle.Window.Root);
                    }
                }
                catch { }
                this.uguiBuildingMovePanel = null;
                ModLogger.Msg("[UguiShell] Building Move Panel build failed: " + ex.Message);
            }
        }

        // IMGUI parity for the content-fit height swap (368 god / 186 normal): resize between
        // the two fixed heights with the TOP edge pinned (panel pivot is center — fold half the
        // delta back into anchoredPosition), then re-clamp. No-ops while the height matches.
        private void ApplyUguiBuildingMovePanelHeight(UguiBuildingMovePanelHandle handle)
        {
            UguiWindowHandle win = (handle != null) ? handle.Window : null;
            if (win == null || win.PanelRt == null)
            {
                return;
            }
            bool god = handle.Controls != null && handle.Controls.GodRowsVisible;
            float target = god ? UguiBuildingMovePanelGodH : UguiBuildingMovePanelBaseH;
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

        // Called every frame from OnUpdate (HeartopiaComplete.cs, next to the other UGUI
        // processors — NOT inside ProcessUguiShellOnUpdate, which early-returns until the shell
        // is first built; this window must work with the shell never opened). Owns ONLY the new
        // UGUI panel's visibility: buildingMovePanelActive && !showMenu (the IMGUI gate,
        // unchanged over there) extended with "UGUI shell not visible" — the same
        // anti-redundancy rule applied to the second menu system.
        private void ProcessUguiBuildingMovePanelOnUpdate()
        {
            try
            {
                bool shellOpen = this.uguiShell != null && this.IsUguiWindowVisible(this.uguiShell.Window);
                bool show = this.buildingMovePanelActive && !shellOpen; // showMenu retired (Phase 5)

                UguiBuildingMovePanelHandle handle = this.uguiBuildingMovePanel;
                if (handle == null)
                {
                    if (!show || this.uguiBuildingMovePanelBuildFailed)
                    {
                        return; // nothing to show, or already failed once this session
                    }
                    this.BuildUguiBuildingMovePanel();
                    handle = this.uguiBuildingMovePanel;
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

                this.ProcessUguiWindowFrame(handle.Window); // title-strip drag (kit driver)

                // Phase 2e scale re-sync — compare the RAW GetUiScale() against the last pushed
                // value (SetUguiWindowScale logs unconditionally; only call on a real change).
                float targetScale = this.GetUiScale();
                if (!Mathf.Approximately(targetScale, handle.LastSyncedUiScale))
                {
                    handle.LastSyncedUiScale = targetScale;
                    this.SetUguiWindowScale(handle.Window, targetScale);
                }

                // Live coordinate readout — fields refreshed by the untouched 0.1s
                // UpdateBuildingMovePanelState poll; format string verbatim from
                // DrawBuildingMovePanelWindow.
                string coordText = this.buildingMovePanelHasPos
                    ? string.Format("X {0:0.00}  Y {1:0.00}  Z {2:0.00}  {3:0}°",
                        this.buildingMovePanelObjPos.x, this.buildingMovePanelObjPos.y,
                        this.buildingMovePanelObjPos.z, this.buildingMovePanelObjYaw)
                    : "(" + this.L("no object") + ")";
                if (!string.Equals(coordText, handle.CoordShown, StringComparison.Ordinal))
                {
                    handle.CoordShown = coordText;
                    this.SetUguiLabelText(handle.CoordLabel, coordText);
                }

                this.SyncUguiFreePlacementControls(handle.Controls);
                this.ApplyUguiBuildingMovePanelHeight(handle);
            }
            catch (Exception ex)
            {
                UguiBuildingMovePanelHandle handle = this.uguiBuildingMovePanel;
                if (handle != null)
                {
                    handle.ErrorCount++;
                    ModLogger.Msg("[UguiShell] Building Move Panel frame error (" + handle.ErrorCount
                        + "/3, disabled at 3): " + ex.Message);
                }
                else
                {
                    this.uguiBuildingMovePanelBuildFailed = true;
                    ModLogger.Msg("[UguiShell] Building Move Panel frame error (build disabled): " + ex.Message);
                }
            }
        }

        // Theme-change rebuild (destroy + reconstruct so every Image/label re-reads the live ui*
        // theme fields), preserving position/scale/visibility — the shell rebuilder's shape,
        // minus tab state this window doesn't have.
        private void RebuildUguiBuildingMovePanelForTheme()
        {
            try
            {
                UguiBuildingMovePanelHandle old = this.uguiBuildingMovePanel;
                if (old == null)
                {
                    return; // never built — nothing stale
                }

                UguiWindowRestoreState state = this.CaptureUguiWindowState(old.Window);
                try
                {
                    if (old.Window != null && old.Window.Root != null)
                    {
                        Object.Destroy(old.Window.Root);
                    }
                }
                catch { }
                this.uguiBuildingMovePanel = null;

                this.BuildUguiBuildingMovePanel();
                if (this.uguiBuildingMovePanel == null)
                {
                    ModLogger.Msg("[UguiShell] Building Move Panel theme rebuild failed — panel not recreated");
                    return;
                }
                this.RestoreUguiWindowState(this.uguiBuildingMovePanel.Window, state);
                ModLogger.Msg("[UguiShell] Building Move Panel rebuilt for theme change");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Building Move Panel theme rebuild error: " + ex.Message);
            }
        }
    }
}
