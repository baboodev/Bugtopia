using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, item 10: Settings→UI Theme — the hand-drawn HSV color
    // picker (DrawUiThemeTab, HeartopiaComplete.UiKit.cs:1364) plus the DISPLAY / TRANSPARENCY /
    // ACTION cards around it.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer (DrawUiThemeTab) and every backend method it calls stay fully functional
    //    and untouched — this file only READS the same fields and CALLS the same methods. The ONE
    //    sanctioned backend change this round: the IMGUI Reset button's 24-field body moved into
    //    the shared ResetUiThemeToDefaults() (HeartopiaComplete.UiKit.cs) so both surfaces run ONE
    //    implementation (the Fishing round's RemoveCustomSpotAt precedent).
    //  - Wiring is by STATIC display-position index (UguiShellSettingsTabIndex = 8 +
    //    UguiShellSettingsUiThemeSubIndex = 2 — Settings' sub array {"Main","Keybinds","UI Theme",
    //    "About","Logging"}, matching settingsSubTab == 2, HeartopiaComplete.cs:2425), never by
    //    localized label comparison. NOTE: DrawUiThemeTab localizes NOTHING — every string here
    //    ("UI THEME", "DISPLAY", row names, "Current"/"Original", "Hex:", button captions) is a
    //    plain literal in the source, so this twin keeps them unlocalized too.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Change funnel — IMGUI parity, no new flush logic: every edit path (scale slider, 100% reset,
    // SV/hue drag, hex Apply, alpha sliders) ends in ApplyUiThemeChangedClampAndArm(), a verbatim
    // copy of the source's single `changed` block (UiKit.cs:1595-1624): clamp everything, then
    // uiThemeStylesDirty = true + uiThemePendingSaveAt = now + 0.6. Consumption stays where it
    // always was — EnsureThemeStyles() at the top of OnGUI() (runs every frame whether or not the
    // IMGUI menu is open) rebuilds the IMGUI styles, calls MarkUguiKitThemeDirty() (which queues
    // the state-preserving SHELL rebuild — including this very tab — 0.35s after the last change)
    // and flushes SaveUiTheme() on the 0.6s debounce. Reset intentionally does NOT save and does
    // NOT arm the save debounce (uiThemeStylesDirty + uiThemeNextRebuildAt = 0 only — source
    // parity; the reset values stay unsaved until the user clicks Save).
    //
    // The HSV picker — POLLED drag, not pointer-handler components: ClassInjector-registered
    // custom components (IPointerDownHandler/IDragHandler) remain unused/unproven in this codebase
    // (HeartopiaComplete.UguiKit.cs:59 — window drag is polled for exactly that reason), so the
    // drag is the Bag/Warehouse hold-to-repeat idiom applied to continuous position:
    // Input.GetMouseButtonDown(0) inside the SV/hue rect (3-arg RectangleContainsScreenPoint,
    // null camera — the only overload in this build) starts a mode, Input.GetMouseButton(0) holds
    // it, and RectTransformUtility.ScreenPointToLocalPointInRectangle (RVA 0x24C3620, verified
    // compiled in this build's dump) converts the pointer to a 0..1 UV each frame. Two deliberate
    // deviations from the source's raw Event.current dispatch, both documented here so nobody
    // "fixes" them: (a) a drag only STARTS on a press inside the box — the source updates on ANY
    // MouseDrag passing through the rect, which in UGUI would repaint colors while dragging the
    // alpha sliders across the picker; (b) while a drag is held, updates stay Contains-gated
    // (leaving the box pauses, re-entering resumes — source behavior) but the mode stays sticky to
    // its own box (an SV drag can never turn into a hue drag mid-press). While a drag is active
    // the hosting ScrollRect is disabled (restored on release) so the drag cannot double as a
    // scroll — UGUI bubbles drags from non-drag-handling children to the ScrollRect otherwise.
    //
    // Texture lifecycle — the SHARED picker textures, never a reimplementation: every gated frame
    // (and inside the hue-drag branch, source parity) this calls this.EnsureUiPickerTextures(h)
    // and assigns this.uiSvTexture/this.uiHueTexture into the two RawImages when the INSTANCE
    // changed (ReferenceEquals cache). That instance check is also the self-heal: every consumed
    // theme edit runs InvalidateThemeCache (UiKit.cs:141), which DESTROYS both textures and nulls
    // the fields — the next gated frame's EnsureUiPickerTextures call rebuilds them and the cache
    // miss reassigns. Worst case is a one-frame blank during an active drag (Update runs before
    // OnGUI's invalidate within a frame) — the IMGUI picker churns the same textures the same way.
    //
    // "Original" preview: the source recomputes originalColor per frame (IMGUI redraws from
    // scratch, so pre-drag state lives in the frame's local ordering). This retained-mode twin
    // SNAPSHOTS instead: captured once when the picker opens for a target (row click here, or the
    // cross-surface bookkeeping noticing the IMGUI twin opened/retargeted it), invalidated on
    // close. The snapshot + drag mode live on INSTANCE fields, not the handle — theme edits
    // rebuild the whole shell (fresh handle) mid-session, and a handle-held snapshot would reset
    // "Original" to the mid-edit color on every rebuild.
    //
    // UI Scale: the slider/100% button write the SAME shared persisted uiScale as IMGUI and call
    // KeepMenuWindowOnScreen(GetUiScale()) exactly like the source (pure windowRect math — keeps
    // the IMGUI twin's window reachable). The UGUI shell needs no extra call: Phase 2e's
    // ProcessUguiShellScaleOnUpdate watches GetUiScale() per frame and routes changes through
    // SetUguiWindowScale, which re-clamps the shell window itself. One deviation: the change gate
    // compares the NORMALIZED value (source compares raw, so sub-step slider jitter arms a
    // no-op style rebuild + save + shell rebuild); gating on the snapped value keeps the same
    // observable steps without churn. Known cost, accepted: holding a slider still >0.35s after a
    // real change lets the debounced shell rebuild fire mid-hold, dropping that drag (grab again).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-build state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellThemeHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public RectTransform ScrollViewportRt; // drag-start clip guard (picker may be half-scrolled out)
            public ScrollRect Scroll;              // disabled while a picker drag is active
            public float ContentWidth;             // scroll content width (block w minus viewport insets)

            public GameObject HeaderLabel;

            // DISPLAY
            public GameObject DisplayPanel;
            public GameObject ScaleLabel;
            public string ScaleShown;
            public Slider ScaleSlider;

            // THEME COLORS
            public GameObject ColorsPanel;
            public readonly List<Image> RowSwatches = new List<Image>();
            public readonly List<GameObject> RowHighlights = new List<GameObject>();
            public int[] RowTargets;               // internal color-target index per display row
            public Color[] RowSwatchShown;
            public int HighlightShownTarget = -2;  // last (pickerOpen ? target : -1) applied

            // Picker area (child of ColorsPanel — SetActive'd with uiThemePickerOpen)
            public GameObject PickerArea;
            public RawImage SvImage;
            public RectTransform SvRt;
            public RawImage HueImage;
            public RectTransform HueRt;
            public Texture2D SvTextureSeen;        // instance caches — miss = reassign (self-heal)
            public Texture2D HueTextureSeen;
            public GameObject SvCursorV;           // crosshair vertical bar
            public GameObject SvCursorH;           // crosshair horizontal bar
            public GameObject HueCursor;
            public float CursorHShown = -1f;
            public float CursorSShown = -1f;
            public float CursorVShown = -1f;
            public Image CurrentSwatch;
            public Color CurrentSwatchShown;
            public Image OriginalSwatch;
            public Color OriginalSwatchShown;
            public GameObject RgbLabel;
            public string RgbShown;
            public GameObject HsvLabel;
            public string HsvShown;
            public InputField HexField;            // click-time read (Apply); external re-sync via Seen cache
            public string HexSeen;

            // TRANSPARENCY
            public GameObject TransparencyPanel;
            public Slider WindowAlphaSlider;
            public GameObject WindowAlphaValue;
            public string WindowAlphaShown;
            public Slider PanelAlphaSlider;
            public GameObject PanelAlphaValue;
            public string PanelAlphaShown;
            public Slider ContentAlphaSlider;
            public GameObject ContentAlphaValue;
            public string ContentAlphaShown;

            // ACTION
            public GameObject ActionPanel;

            public int LayoutSignature = -1;       // packed pickerOpen + target
            public int ErrorCount;                 // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellThemeHandle uguiShellTheme;

        // Instance-level picker state — survives the theme-rebuilder replacing the handle (see
        // file header). Drag mode: 0 = none, 1 = SV box, 2 = hue strip.
        private Color uguiThemeOriginalColor = Color.white;
        private int uguiThemeOriginalTarget = -1;
        private bool uguiThemeOriginalValid;
        private int uguiThemeDragMode;

        // Display rows — names + internal target indices copied EXACTLY from DrawUiThemeTab
        // (UiKit.cs:1415-1426). Target 4 (Window) is deliberately absent from the picker list: its
        // COLOR is not user-editable, only its alpha (the Window Alpha slider below) — matching
        // GetUiThemeColorTargetValue's 0-8 switch minus the window entry.
        private static readonly string[] UguiThemeColorTargetNames = new string[]
        {
            "Accent", "Header Text", "Success", "Text", "Main Tab Text", "Sub Tab Text", "Panel Bg", "Content Bg"
        };
        private static readonly int[] UguiThemeColorTargetIndices = new int[] { 0, 7, 8, 1, 2, 3, 5, 6 };

        // Picker geometry — SV box and hue strip sizes from the source draw (UiKit.cs:1482-1483;
        // the 190px display height matches there, the textures themselves stay whatever
        // CreateSvTexture/CreateHueTexture bake — 220x180/18x180 — stretched by the RawImage
        // exactly like GUI.DrawTexture stretched them).
        private const float UguiThemeSvWidth = 260f;
        private const float UguiThemeSvHeight = 190f;
        private const float UguiThemeHueWidth = 18f;
        private const float UguiThemePickerAreaHeight = 270f;
        private const float UguiThemeColorRowStep = 34f;

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellThemeContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTheme = null;

            UguiShellThemeHandle handle = new UguiShellThemeHandle();
            GameObject block = this.CreateUguiGo("SettingsUiThemeContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            // Flat look over the block's ContentBg (LIVE rail idiom) — alpha-0 images still
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
            handle.ContentWidth = w - 22f; // viewport insets: 4 left + 18 right
            try
            {
                handle.Scroll = scroll.GetComponent<ScrollRect>();
                if (scrollContent != null && scrollContent.parent != null)
                {
                    handle.ScrollViewportRt = scrollContent.parent.GetComponent<RectTransform>();
                }
            }
            catch { }

            float pad = 12f;
            float panelW = handle.ContentWidth - pad * 2f;

            // IMGUI headerStyle here is bold 14 (NOT localized in the source — parity kept).
            handle.HeaderLabel = this.CreateUguiHeaderLabel(scrollContent, "Title", "UI THEME", 14f);

            // ---------------- DISPLAY ----------------
            // CreateUguiSettingsMainPanel mirrors DrawExentriSectionPanel with the SAME fill/line
            // formula the theme tab passes it — reused, not duplicated (its 11pt header vs the
            // source's 12pt sectionStyle is the accepted sibling-tab look).
            GameObject display = this.CreateUguiSettingsMainPanel(scrollContent, "DisplayPanel", "DISPLAY");
            handle.DisplayPanel = display;

            handle.ScaleShown = "UI Scale: " + Mathf.RoundToInt(this.uiScale * 100f) + "%";
            handle.ScaleLabel = this.CreateUguiBodyLabel(display.transform, "ScaleLabel", handle.ScaleShown, 13f);
            PlaceUguiTopLeft(handle.ScaleLabel, 14f, 46f, 150f, 22f);
            handle.ScaleSlider = this.CreateUguiSlider(display.transform, "ScaleSlider",
                UiScaleMin, UiScaleMax, this.uiScale, false,
                new System.Action<float>(this.OnUguiThemeScaleChanged));
            PlaceUguiTopLeft(handle.ScaleSlider.gameObject, 164f, 46f, panelW - 164f - 94f - 26f, 20f);
            GameObject scaleReset = this.CreateUguiSecondaryButton(display.transform, "ScaleResetButton",
                "100%", new System.Action(this.OnUguiThemeScaleResetClicked));
            PlaceUguiTopLeft(scaleReset, panelW - 14f - 94f, 42f, 94f, 26f);

            // No Font row here on purpose: the UGUI shell's font is HARD-PINNED to LiberationSans
            // SDF in EnsureUguiFonts. A picker was built and then removed — OS fonts are impossible
            // on this build (see the dead-end note in HeartopiaComplete.UguiKit.cs), which left only
            // the game's own assets, and those are outlined and much wider than the metrics every
            // rect here was sized against. Don't re-add it.

            // ---------------- THEME COLORS ----------------
            GameObject colors = this.CreateUguiSettingsMainPanel(scrollContent, "ColorsPanel", "THEME COLORS");
            handle.ColorsPanel = colors;

            int initialHighlight = this.uiThemePickerOpen ? this.uiThemeColorTarget : -1;
            handle.HighlightShownTarget = initialHighlight;
            handle.RowTargets = UguiThemeColorTargetIndices;
            handle.RowSwatchShown = new Color[UguiThemeColorTargetIndices.Length];
            float rowW = panelW - 28f;
            for (int i = 0; i < UguiThemeColorTargetNames.Length; i++)
            {
                int targetCopy = UguiThemeColorTargetIndices[i]; // capture a copy for the click closure
                GameObject row = this.CreateUguiGo("ColorRow" + i, colors.transform);
                PlaceUguiTopLeft(row, 14f, 42f + i * UguiThemeColorRowStep, rowW, 30f);
                // IMGUI: DrawTintedRoundedBox(rowFill 1,1,1,0.09) + DrawCardOutline hairline.
                Image rowBg = this.AddUguiImage(row, new Color(1f, 1f, 1f, 0.09f), true, 1.5f);
                rowBg.raycastTarget = true;
                this.AddUguiRingOverlay(row, UguiKitSecondaryRing, 1.5f);
                Button rowBtn = row.AddComponent<Button>();
                rowBtn.targetGraphic = rowBg;
                rowBtn.onClick.AddListener(new System.Action(() => this.OnUguiThemeColorRowClicked(targetCopy)));

                GameObject rowLabel = this.CreateUguiBodyLabel(row.transform, "Label", UguiThemeColorTargetNames[i], 12f);
                PlaceUguiTopLeft(rowLabel, 12f, 4f, 220f, 22f);

                // Swatch (18x18) inside a 24x24 wrap whose white ring is the "picker open on this
                // row" marker — the UGUI stand-in for the source's four hand-drawn outline lines.
                GameObject swatchWrap = this.CreateUguiGo("Swatch", row.transform);
                PlaceUguiTopLeft(swatchWrap, rowW - 35f, 3f, 24f, 24f);
                GameObject highlight = this.CreateUguiGo("Highlight", swatchWrap.transform);
                StretchUguiFill(highlight, 0f, 0f, 0f, 0f);
                this.AddUguiRingOverlay(highlight, Color.white, 2.5f);
                SetUguiGoActive(highlight, targetCopy == initialHighlight);
                GameObject swatchGo = this.CreateUguiGo("Fill", swatchWrap.transform);
                PlaceUguiTopLeft(swatchGo, 3f, 3f, 18f, 18f);
                Color swatchColor = this.GetUiThemeColorTargetValue(targetCopy);
                swatchColor.a = 1f;
                Image swatchImg = this.AddUguiImage(swatchGo, swatchColor, true, 2.5f);

                handle.RowSwatches.Add(swatchImg);
                handle.RowHighlights.Add(highlight);
                handle.RowSwatchShown[i] = swatchColor;
            }

            this.BuildUguiThemePickerArea(handle, colors.transform, panelW);

            // ---------------- TRANSPARENCY ----------------
            GameObject transparency = this.CreateUguiSettingsMainPanel(scrollContent, "TransparencyPanel", "TRANSPARENCY");
            handle.TransparencyPanel = transparency;

            float alphaSliderW = panelW - 164f - 52f - 14f - 8f;
            GameObject windowAlphaLabel = this.CreateUguiBodyLabel(transparency.transform, "WindowAlphaLabel", "Window Alpha", 13f);
            PlaceUguiTopLeft(windowAlphaLabel, 14f, 44f, 150f, 20f);
            handle.WindowAlphaSlider = this.CreateUguiSlider(transparency.transform, "WindowAlphaSlider",
                0.15f, 1f, this.uiWindowAlpha, false,
                new System.Action<float>(this.OnUguiThemeWindowAlphaChanged));
            PlaceUguiTopLeft(handle.WindowAlphaSlider.gameObject, 164f, 44f, alphaSliderW, 20f);
            handle.WindowAlphaShown = this.uiWindowAlpha.ToString("F2");
            handle.WindowAlphaValue = this.CreateUguiBodyLabel(transparency.transform, "WindowAlphaValue", handle.WindowAlphaShown, 12f);
            PlaceUguiTopLeft(handle.WindowAlphaValue, panelW - 14f - 52f, 44f, 52f, 20f);

            GameObject panelAlphaLabel = this.CreateUguiBodyLabel(transparency.transform, "PanelAlphaLabel", "Panel Alpha", 13f);
            PlaceUguiTopLeft(panelAlphaLabel, 14f, 78f, 150f, 20f);
            handle.PanelAlphaSlider = this.CreateUguiSlider(transparency.transform, "PanelAlphaSlider",
                0.15f, 1f, this.uiPanelAlpha, false,
                new System.Action<float>(this.OnUguiThemePanelAlphaChanged));
            PlaceUguiTopLeft(handle.PanelAlphaSlider.gameObject, 164f, 78f, alphaSliderW, 20f);
            handle.PanelAlphaShown = this.uiPanelAlpha.ToString("F2");
            handle.PanelAlphaValue = this.CreateUguiBodyLabel(transparency.transform, "PanelAlphaValue", handle.PanelAlphaShown, 12f);
            PlaceUguiTopLeft(handle.PanelAlphaValue, panelW - 14f - 52f, 78f, 52f, 20f);

            GameObject contentAlphaLabel = this.CreateUguiBodyLabel(transparency.transform, "ContentAlphaLabel", "Content Alpha", 13f);
            PlaceUguiTopLeft(contentAlphaLabel, 14f, 112f, 150f, 20f);
            handle.ContentAlphaSlider = this.CreateUguiSlider(transparency.transform, "ContentAlphaSlider",
                0.15f, 1f, this.uiContentAlpha, false,
                new System.Action<float>(this.OnUguiThemeContentAlphaChanged));
            PlaceUguiTopLeft(handle.ContentAlphaSlider.gameObject, 164f, 112f, alphaSliderW, 20f);
            handle.ContentAlphaShown = this.uiContentAlpha.ToString("F2");
            handle.ContentAlphaValue = this.CreateUguiBodyLabel(transparency.transform, "ContentAlphaValue", handle.ContentAlphaShown, 12f);
            PlaceUguiTopLeft(handle.ContentAlphaValue, panelW - 14f - 52f, 112f, 52f, 20f);

            // ---------------- ACTION ----------------
            // Panel-only card in the source (no section header) — same panel chrome, no title.
            GameObject action = this.CreateUguiSettingsMainPanel(scrollContent, "ActionPanel", "");
            handle.ActionPanel = action;
            GameObject saveBtn = this.CreateUguiPrimaryButton(action.transform, "SaveButton", "Save",
                new System.Action(this.OnUguiThemeSaveClicked));
            PlaceUguiTopLeft(saveBtn, 14f, 17f, 130f, 32f);
            GameObject loadBtn = this.CreateUguiSecondaryButton(action.transform, "LoadButton", "Load",
                new System.Action(this.OnUguiThemeLoadClicked));
            PlaceUguiTopLeft(loadBtn, 154f, 17f, 130f, 32f);
            GameObject resetBtn = this.CreateUguiDangerButton(action.transform, "ResetButton", "Reset",
                new System.Action(this.OnUguiThemeResetClicked));
            PlaceUguiTopLeft(resetBtn, 294f, 17f, 130f, 32f);

            handle.LayoutSignature = this.ComputeUguiThemeLayoutSignature();
            this.RelayoutUguiShellTheme(handle);

            handle.Root = block;
            this.uguiShellTheme = handle;
            return block;
        }

        // The picker widgets (SV box, hue strip, crosshair/line cursors, Current/Original
        // previews, RGB/HSV readouts, hex row) — all children of one PickerArea GO that the
        // relayout SetActive's with uiThemePickerOpen (Foraging's conditional-block idiom).
        private void BuildUguiThemePickerArea(UguiShellThemeHandle handle, Transform colorsPanel, float panelW)
        {
            GameObject picker = this.CreateUguiGo("PickerArea", colorsPanel);
            PlaceUguiTopLeft(picker, 0f, 42f + UguiThemeColorTargetNames.Length * UguiThemeColorRowStep,
                panelW, UguiThemePickerAreaHeight);
            handle.PickerArea = picker;

            // Seed h/s/v from the CURRENT target color — the same derivation the source runs per
            // frame (UiKit.cs:1471-1475) — and make sure the shared textures exist before the
            // RawImages reference them.
            Color initialColor = this.GetUiThemeColorTargetValue(this.uiThemeColorTarget);
            float h0;
            float s0;
            float v0;
            Color.RGBToHSV(initialColor, out h0, out s0, out v0);
            this.EnsureUiPickerTextures(h0);

            GameObject svGo = this.CreateUguiGo("SvBox", picker.transform);
            PlaceUguiTopLeft(svGo, 14f, 8f, UguiThemeSvWidth, UguiThemeSvHeight);
            RawImage sv = svGo.AddComponent<RawImage>();
            sv.raycastTarget = false; // the polled drag needs no raycasts
            try { sv.texture = this.uiSvTexture; } catch { }
            handle.SvImage = sv;
            handle.SvRt = svGo.GetComponent<RectTransform>();
            handle.SvTextureSeen = this.uiSvTexture;

            // Crosshair — two white bars, source cursor math (UiKit.cs:1506-1510) rebuilt from
            // s/v each sync. raycastTarget stays false (AddUguiImage default).
            handle.SvCursorV = this.CreateUguiGo("CursorV", svGo.transform);
            this.AddUguiImage(handle.SvCursorV, Color.white, false, 1f);
            handle.SvCursorH = this.CreateUguiGo("CursorH", svGo.transform);
            this.AddUguiImage(handle.SvCursorH, Color.white, false, 1f);

            GameObject hueGo = this.CreateUguiGo("HueStrip", picker.transform);
            PlaceUguiTopLeft(hueGo, 14f + UguiThemeSvWidth + 10f, 8f, UguiThemeHueWidth, UguiThemeSvHeight);
            RawImage hue = hueGo.AddComponent<RawImage>();
            hue.raycastTarget = false;
            try { hue.texture = this.uiHueTexture; } catch { }
            handle.HueImage = hue;
            handle.HueRt = hueGo.GetComponent<RectTransform>();
            handle.HueTextureSeen = this.uiHueTexture;

            handle.HueCursor = this.CreateUguiGo("Cursor", hueGo.transform);
            this.AddUguiImage(handle.HueCursor, Color.white, false, 1f);

            this.PositionUguiThemePickerCursors(handle, h0, s0, v0);

            // Previews — swatches right of the hue strip; labels sit ABOVE their swatch (the
            // source's to-the-right labels don't fit this narrower column).
            float previewX = 14f + UguiThemeSvWidth + 10f + UguiThemeHueWidth + 22f;
            Color initialOpaque = initialColor;
            initialOpaque.a = 1f;

            GameObject currentLabel = this.CreateUguiBodyLabel(picker.transform, "CurrentLabel", "Current", 12f);
            PlaceUguiTopLeft(currentLabel, previewX, 8f, 100f, 16f);
            GameObject currentSwatch = this.CreateUguiGo("CurrentSwatch", picker.transform);
            PlaceUguiTopLeft(currentSwatch, previewX, 26f, 72f, 64f);
            handle.CurrentSwatch = this.AddUguiImage(currentSwatch, initialOpaque, true, 1.5f);
            this.AddUguiRingOverlay(currentSwatch, UguiKitSecondaryRing, 1.5f);
            handle.CurrentSwatchShown = initialOpaque;

            Color initialOriginal = this.uguiThemeOriginalValid ? this.uguiThemeOriginalColor : initialOpaque;
            initialOriginal.a = 1f;
            GameObject originalLabel = this.CreateUguiBodyLabel(picker.transform, "OriginalLabel", "Original", 12f);
            PlaceUguiTopLeft(originalLabel, previewX, 98f, 100f, 16f);
            GameObject originalSwatch = this.CreateUguiGo("OriginalSwatch", picker.transform);
            PlaceUguiTopLeft(originalSwatch, previewX, 116f, 72f, 64f);
            handle.OriginalSwatch = this.AddUguiImage(originalSwatch, initialOriginal, true, 1.5f);
            this.AddUguiRingOverlay(originalSwatch, UguiKitSecondaryRing, 1.5f);
            handle.OriginalSwatchShown = initialOriginal;

            // Readouts (plain labels, no interaction — source formats, UiKit.cs:1537-1541).
            handle.RgbShown = this.FormatUguiThemeRgb(initialOpaque);
            handle.RgbLabel = this.CreateUguiBodyLabel(picker.transform, "RgbLabel", handle.RgbShown, 12f);
            PlaceUguiTopLeft(handle.RgbLabel, 14f, 206f, 260f, 18f);
            handle.HsvShown = this.FormatUguiThemeHsv(h0, s0, v0);
            handle.HsvLabel = this.CreateUguiBodyLabel(picker.transform, "HsvLabel", handle.HsvShown, 12f);
            PlaceUguiTopLeft(handle.HsvLabel, 14f + UguiThemeSvWidth + 10f, 206f, 160f, 18f);

            // Hex row — click-time-read InputField (Teleport XYZ precedent): value read on Apply,
            // external changes (drags rewrite uiThemeHexInput) re-synced via the Seen cache.
            GameObject hexLabel = this.CreateUguiBodyLabel(picker.transform, "HexLabel", "Hex:", 12f);
            PlaceUguiTopLeft(hexLabel, 14f, 234f, 40f, 22f);
            handle.HexField = this.CreateUguiInputField(picker.transform, "HexField", this.uiThemeHexInput, 0, null);
            PlaceUguiTopLeft(handle.HexField.gameObject, 56f, 232f, 140f, 26f);
            handle.HexSeen = this.uiThemeHexInput;
            GameObject applyBtn = this.CreateUguiSecondaryButton(picker.transform, "ApplyButton", "Apply",
                new System.Action(this.OnUguiThemeHexApplyClicked));
            PlaceUguiTopLeft(applyBtn, 204f, 232f, 78f, 26f);
        }

        // ----------------------------------------------------------------------------------------
        // Layout (reposition/SetActive only — nothing rebuilt; Settings-Main idiom)
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiThemeLayoutSignature()
        {
            return (this.uiThemePickerOpen ? 1 : 0) | ((this.uiThemeColorTarget & 0xF) << 1);
        }

        private void RelayoutUguiShellTheme(UguiShellThemeHandle handle)
        {
            bool pickerOpen = this.uiThemePickerOpen;
            const float pad = 12f;
            float panelW = handle.ContentWidth - pad * 2f;
            float y = 10f;

            if (handle.HeaderLabel != null)
            {
                PlaceUguiTopLeft(handle.HeaderLabel, pad + 4f, y, panelW - 8f, 22f);
            }
            y += 30f;

            if (handle.DisplayPanel != null)
            {
                PlaceUguiTopLeft(handle.DisplayPanel, pad, y, panelW, 90f);
            }
            y += 90f + 14f;

            SetUguiGoActive(handle.PickerArea, pickerOpen);
            float colorsH = 42f + UguiThemeColorTargetNames.Length * UguiThemeColorRowStep
                + (pickerOpen ? UguiThemePickerAreaHeight : 0f) + 12f;
            if (handle.ColorsPanel != null)
            {
                PlaceUguiTopLeft(handle.ColorsPanel, pad, y, panelW, colorsH);
            }
            y += colorsH + 14f;

            if (handle.TransparencyPanel != null)
            {
                PlaceUguiTopLeft(handle.TransparencyPanel, pad, y, panelW, 158f);
            }
            y += 158f + 14f;

            if (handle.ActionPanel != null)
            {
                PlaceUguiTopLeft(handle.ActionPanel, pad, y, panelW, 66f);
            }
            y += 66f + 14f;

            this.SetUguiScrollContentHeight(handle.ScrollContent, y + 6f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellThemeOnUpdate()
        {
            UguiShellThemeHandle handle = this.uguiShellTheme;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsUiThemeSubIndex))
            {
                // Shell hidden / tab switched / rebuilt mid-drag: never leave the mode stuck or
                // the ScrollRect disabled.
                this.ReleaseUguiThemePickerDrag(handle);
                return;
            }

            try
            {
                // Cross-surface picker-state bookkeeping: the IMGUI twin edits the same
                // uiThemePickerOpen/uiThemeColorTarget fields, so the Original snapshot follows
                // the FIELDS, not just this surface's own clicks.
                if (!this.uiThemePickerOpen)
                {
                    this.uguiThemeOriginalValid = false; // a re-open takes a fresh snapshot
                }
                else if (!this.uguiThemeOriginalValid || this.uguiThemeOriginalTarget != this.uiThemeColorTarget)
                {
                    this.uguiThemeOriginalColor = this.GetUiThemeColorTargetValue(this.uiThemeColorTarget);
                    this.uguiThemeOriginalTarget = this.uiThemeColorTarget;
                    this.uguiThemeOriginalValid = true;
                }

                int signature = this.ComputeUguiThemeLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellTheme(handle);
                }

                this.ProcessUguiThemePickerDrag(handle);
                this.SyncUguiThemePickerVisuals(handle);
                this.SyncUguiThemeControls(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] UI Theme content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // The HSV drag itself (polled — see file header for why not pointer-handler components)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiThemePickerDrag(UguiShellThemeHandle handle)
        {
            if (!this.uiThemePickerOpen || handle.SvRt == null || handle.HueRt == null)
            {
                this.ReleaseUguiThemePickerDrag(handle);
                return;
            }

            if (this.uguiThemeDragMode == 0)
            {
                if (!Input.GetMouseButtonDown(0))
                {
                    return;
                }
                Vector3 m3 = Input.mousePosition;
                Vector2 mouse = new Vector2(m3.x, m3.y);
                // Clip guard: the picker can be scrolled partly out of the viewport; a press on a
                // masked-away pixel must not start a drag.
                if (handle.ScrollViewportRt != null
                    && !RectTransformUtility.RectangleContainsScreenPoint(handle.ScrollViewportRt, mouse, null))
                {
                    return;
                }
                if (RectTransformUtility.RectangleContainsScreenPoint(handle.SvRt, mouse, null))
                {
                    this.uguiThemeDragMode = 1;
                }
                else if (RectTransformUtility.RectangleContainsScreenPoint(handle.HueRt, mouse, null))
                {
                    this.uguiThemeDragMode = 2;
                }
                else
                {
                    return;
                }
            }
            else if (!Input.GetMouseButton(0))
            {
                this.ReleaseUguiThemePickerDrag(handle);
                return;
            }

            // Keep the ScrollRect off for the whole drag (idempotent — a theme rebuild mid-drag
            // hands us a fresh, enabled one).
            try
            {
                if (handle.Scroll != null && handle.Scroll.enabled)
                {
                    handle.Scroll.enabled = false;
                }
            }
            catch { }

            Vector3 p3 = Input.mousePosition;
            Vector2 point = new Vector2(p3.x, p3.y);
            RectTransform rt = (this.uguiThemeDragMode == 1) ? handle.SvRt : handle.HueRt;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, point, null))
            {
                return; // source parity: leaving the box pauses updates; re-entering resumes
            }
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, point, null, out local))
            {
                return;
            }
            Rect rect = rt.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }
            float u = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
            float yFromTop = Mathf.Clamp01((rect.yMax - local.y) / rect.height);

            // Source sequence (UiKit.cs:1471-1519): derive h/s/v from the CURRENT stored color,
            // overwrite the dragged component(s), write back through HSVToRGB. Greys re-derive
            // h=0 — the IMGUI picker has the identical quirk; keep the surfaces in agreement.
            Color currentColor = this.GetUiThemeColorTargetValue(this.uiThemeColorTarget);
            float hh;
            float ss;
            float vv;
            Color.RGBToHSV(currentColor, out hh, out ss, out vv);
            if (this.uguiThemeDragMode == 1)
            {
                ss = u;
                vv = 1f - yFromTop; // top of the box is v = 1
            }
            else
            {
                hh = 1f - yFromTop; // top of the strip is h = 1
                this.EnsureUiPickerTextures(hh); // source parity: SV gradient re-bakes at the new hue (UiKit.cs:1500)
            }
            Color picked = Color.HSVToRGB(hh, ss, vv);
            this.SetUiThemeColorTargetValue(this.uiThemeColorTarget, picked);
            this.uiThemeHexInput = this.ColorToHex(picked);
            this.ApplyUiThemeChangedClampAndArm();
        }

        private void ReleaseUguiThemePickerDrag(UguiShellThemeHandle handle)
        {
            if (this.uguiThemeDragMode == 0)
            {
                return;
            }
            this.uguiThemeDragMode = 0;
            try
            {
                if (handle != null && handle.Scroll != null && !handle.Scroll.enabled)
                {
                    handle.Scroll.enabled = true;
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame visual sync (cached-compare everywhere — no-ops while nothing changes)
        // ----------------------------------------------------------------------------------------

        private string FormatUguiThemeRgb(Color c)
        {
            return "R:" + Mathf.RoundToInt(c.r * 255f) + "  G:" + Mathf.RoundToInt(c.g * 255f)
                + "  B:" + Mathf.RoundToInt(c.b * 255f);
        }

        private string FormatUguiThemeHsv(float h, float s, float v)
        {
            return "H:" + Mathf.RoundToInt(h * 360f) + "  S:" + Mathf.RoundToInt(s * 100f)
                + "  V:" + Mathf.RoundToInt(v * 100f);
        }

        private void PositionUguiThemePickerCursors(UguiShellThemeHandle handle, float h, float s, float v)
        {
            // Source cursor math (UiKit.cs:1506-1512) in box-local coordinates.
            float svX = s * UguiThemeSvWidth;
            float svY = (1f - v) * UguiThemeSvHeight;
            if (handle.SvCursorV != null)
            {
                PlaceUguiTopLeft(handle.SvCursorV, svX - 1f, svY - 6f, 2f, 12f);
            }
            if (handle.SvCursorH != null)
            {
                PlaceUguiTopLeft(handle.SvCursorH, svX - 6f, svY - 1f, 12f, 2f);
            }
            float hueY = (1f - h) * UguiThemeSvHeight;
            if (handle.HueCursor != null)
            {
                PlaceUguiTopLeft(handle.HueCursor, -2f, hueY - 1f, UguiThemeHueWidth + 4f, 2f);
            }
            handle.CursorHShown = h;
            handle.CursorSShown = s;
            handle.CursorVShown = v;
        }

        private void SyncUguiThemePickerVisuals(UguiShellThemeHandle handle)
        {
            // Row swatches — every row re-reads its live target color (the picker, hex Apply,
            // Load and Reset all change them).
            for (int i = 0; i < handle.RowSwatches.Count && i < handle.RowTargets.Length; i++)
            {
                Image swatch = handle.RowSwatches[i];
                if (swatch == null)
                {
                    continue;
                }
                Color c = this.GetUiThemeColorTargetValue(handle.RowTargets[i]);
                c.a = 1f;
                if (handle.RowSwatchShown[i] != c)
                {
                    handle.RowSwatchShown[i] = c;
                    swatch.color = c;
                }
            }

            // Open-row highlight ring.
            int want = this.uiThemePickerOpen ? this.uiThemeColorTarget : -1;
            if (handle.HighlightShownTarget != want)
            {
                handle.HighlightShownTarget = want;
                for (int i = 0; i < handle.RowHighlights.Count && i < handle.RowTargets.Length; i++)
                {
                    SetUguiGoActive(handle.RowHighlights[i], handle.RowTargets[i] == want);
                }
            }

            if (!this.uiThemePickerOpen)
            {
                return;
            }

            // Derive h/s/v from the stored color every frame — IMGUI parity (see drag comment).
            Color color = this.GetUiThemeColorTargetValue(this.uiThemeColorTarget);
            float hh;
            float ss;
            float vv;
            Color.RGBToHSV(color, out hh, out ss, out vv);

            // Shared-texture upkeep + destroyed-texture self-heal: InvalidateThemeCache destroys
            // both textures and nulls the fields on every consumed theme edit; this call rebuilds
            // them and the instance-cache miss below reassigns the RawImages (file header).
            this.EnsureUiPickerTextures(hh);
            if (handle.SvImage != null && !ReferenceEquals(handle.SvTextureSeen, this.uiSvTexture))
            {
                handle.SvTextureSeen = this.uiSvTexture;
                try { handle.SvImage.texture = this.uiSvTexture; } catch { }
            }
            if (handle.HueImage != null && !ReferenceEquals(handle.HueTextureSeen, this.uiHueTexture))
            {
                handle.HueTextureSeen = this.uiHueTexture;
                try { handle.HueImage.texture = this.uiHueTexture; } catch { }
            }

            if (hh != handle.CursorHShown || ss != handle.CursorSShown || vv != handle.CursorVShown)
            {
                this.PositionUguiThemePickerCursors(handle, hh, ss, vv);
            }

            Color current = color;
            current.a = 1f;
            if (handle.CurrentSwatch != null && handle.CurrentSwatchShown != current)
            {
                handle.CurrentSwatchShown = current;
                handle.CurrentSwatch.color = current;
            }
            Color original = this.uguiThemeOriginalValid ? this.uguiThemeOriginalColor : current;
            original.a = 1f;
            if (handle.OriginalSwatch != null && handle.OriginalSwatchShown != original)
            {
                handle.OriginalSwatchShown = original;
                handle.OriginalSwatch.color = original;
            }

            string rgb = this.FormatUguiThemeRgb(current);
            if (!string.Equals(rgb, handle.RgbShown, StringComparison.Ordinal))
            {
                handle.RgbShown = rgb;
                this.SetUguiLabelText(handle.RgbLabel, rgb);
            }
            string hsv = this.FormatUguiThemeHsv(hh, ss, vv);
            if (!string.Equals(hsv, handle.HsvShown, StringComparison.Ordinal))
            {
                handle.HsvShown = hsv;
                this.SetUguiLabelText(handle.HsvLabel, hsv);
            }

            // External hex re-sync (drags rewrite uiThemeHexInput; the IMGUI twin edits it too).
            // Typing is never clobbered — the helper only pushes when the BACKING value moved.
            SyncUguiInputFieldFromBackingField(handle.HexField, ref handle.HexSeen, this.uiThemeHexInput);
        }

        private void SyncUguiThemeControls(UguiShellThemeHandle handle)
        {
            string scaleText = "UI Scale: " + Mathf.RoundToInt(this.uiScale * 100f) + "%";
            if (!string.Equals(scaleText, handle.ScaleShown, StringComparison.Ordinal))
            {
                handle.ScaleShown = scaleText;
                this.SetUguiLabelText(handle.ScaleLabel, scaleText);
            }
            if (handle.ScaleSlider != null && Mathf.Abs(handle.ScaleSlider.value - this.uiScale) > 0.001f)
            {
                handle.ScaleSlider.SetValueWithoutNotify(this.uiScale); // NEVER value = — fires onValueChanged
            }

            string windowText = this.uiWindowAlpha.ToString("F2");
            if (!string.Equals(windowText, handle.WindowAlphaShown, StringComparison.Ordinal))
            {
                handle.WindowAlphaShown = windowText;
                this.SetUguiLabelText(handle.WindowAlphaValue, windowText);
            }
            if (handle.WindowAlphaSlider != null && Mathf.Abs(handle.WindowAlphaSlider.value - this.uiWindowAlpha) > 0.001f)
            {
                handle.WindowAlphaSlider.SetValueWithoutNotify(this.uiWindowAlpha);
            }

            string panelText = this.uiPanelAlpha.ToString("F2");
            if (!string.Equals(panelText, handle.PanelAlphaShown, StringComparison.Ordinal))
            {
                handle.PanelAlphaShown = panelText;
                this.SetUguiLabelText(handle.PanelAlphaValue, panelText);
            }
            if (handle.PanelAlphaSlider != null && Mathf.Abs(handle.PanelAlphaSlider.value - this.uiPanelAlpha) > 0.001f)
            {
                handle.PanelAlphaSlider.SetValueWithoutNotify(this.uiPanelAlpha);
            }

            string contentText = this.uiContentAlpha.ToString("F2");
            if (!string.Equals(contentText, handle.ContentAlphaShown, StringComparison.Ordinal))
            {
                handle.ContentAlphaShown = contentText;
                this.SetUguiLabelText(handle.ContentAlphaValue, contentText);
            }
            if (handle.ContentAlphaSlider != null && Mathf.Abs(handle.ContentAlphaSlider.value - this.uiContentAlpha) > 0.001f)
            {
                handle.ContentAlphaSlider.SetValueWithoutNotify(this.uiContentAlpha);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its DrawUiThemeTab block exactly; all edits funnel into
        // ApplyUiThemeChangedClampAndArm (the source's shared `changed` block).
        // ----------------------------------------------------------------------------------------

        // Verbatim copy of the source's changed-flag consequence block (UiKit.cs:1595-1624):
        // the SAME clamp set (note: header/success/window RGB are NOT clamped there either) and
        // the SAME two deferred-rebuild/save fields, set nowhere else. Consumption is untouched
        // IMGUI plumbing (EnsureThemeStyles — see file header).
        private void ApplyUiThemeChangedClampAndArm()
        {
            this.uiAccentR = Mathf.Clamp01(this.uiAccentR);
            this.uiAccentG = Mathf.Clamp01(this.uiAccentG);
            this.uiAccentB = Mathf.Clamp01(this.uiAccentB);
            this.uiTextR = Mathf.Clamp01(this.uiTextR);
            this.uiTextG = Mathf.Clamp01(this.uiTextG);
            this.uiTextB = Mathf.Clamp01(this.uiTextB);
            this.uiMainTabTextR = Mathf.Clamp01(this.uiMainTabTextR);
            this.uiMainTabTextG = Mathf.Clamp01(this.uiMainTabTextG);
            this.uiMainTabTextB = Mathf.Clamp01(this.uiMainTabTextB);
            this.uiSubTabTextR = Mathf.Clamp01(this.uiSubTabTextR);
            this.uiSubTabTextG = Mathf.Clamp01(this.uiSubTabTextG);
            this.uiSubTabTextB = Mathf.Clamp01(this.uiSubTabTextB);
            this.uiPanelR = Mathf.Clamp01(this.uiPanelR);
            this.uiPanelG = Mathf.Clamp01(this.uiPanelG);
            this.uiPanelB = Mathf.Clamp01(this.uiPanelB);
            this.uiContentR = Mathf.Clamp01(this.uiContentR);
            this.uiContentG = Mathf.Clamp01(this.uiContentG);
            this.uiContentB = Mathf.Clamp01(this.uiContentB);
            this.uiWindowAlpha = Mathf.Clamp(this.uiWindowAlpha, 0.15f, 1f);
            this.uiPanelAlpha = Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f);
            this.uiContentAlpha = Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f);
            this.uiScale = this.NormalizeUiScale(this.uiScale);
            this.uiThemeStylesDirty = true;
            this.uiThemePendingSaveAt = Time.unscaledTime + 0.6f;
        }

        // IMGUI UiKit.cs:1400-1406. Gate deviation: compares the NORMALIZED value so sub-step
        // slider jitter can't arm no-op rebuild+save churn (see file header).
        private void OnUguiThemeScaleChanged(float value)
        {
            float normalized = this.NormalizeUiScale(value);
            if (Math.Abs(normalized - this.uiScale) <= 0.001f)
            {
                return;
            }
            this.uiScale = normalized;
            this.KeepMenuWindowOnScreen(this.GetUiScale());
            this.ApplyUiThemeChangedClampAndArm();
        }

        // IMGUI UiKit.cs:1407-1412 — arms unconditionally on click, source parity.
        private void OnUguiThemeScaleResetClicked()
        {
            this.uiScale = 1f;
            this.KeepMenuWindowOnScreen(this.GetUiScale());
            this.ApplyUiThemeChangedClampAndArm();
        }

        // IMGUI UiKit.cs:1455-1467 — same-row click closes; any other row selects, seeds the hex
        // string and opens. Selection alone never arms the changed funnel (source parity). The
        // Original snapshot is taken HERE on the open transition, once (file header).
        private void OnUguiThemeColorRowClicked(int targetIndex)
        {
            try
            {
                if (this.uiThemeColorTarget == targetIndex && this.uiThemePickerOpen)
                {
                    this.uiThemePickerOpen = false;
                    this.uguiThemeOriginalValid = false;
                }
                else
                {
                    this.uiThemeColorTarget = targetIndex;
                    this.uiThemeHexInput = this.ColorToHex(this.GetUiThemeColorTargetValue(this.uiThemeColorTarget));
                    this.uiThemePickerOpen = true;
                    this.uguiThemeOriginalColor = this.GetUiThemeColorTargetValue(targetIndex);
                    this.uguiThemeOriginalTarget = targetIndex;
                    this.uguiThemeOriginalValid = true;
                }

                UguiShellThemeHandle handle = this.uguiShellTheme;
                if (handle != null && handle.Root != null)
                {
                    // Same-frame relayout (no one-frame lag); the processor's signature check
                    // then no-ops.
                    handle.LayoutSignature = this.ComputeUguiThemeLayoutSignature();
                    this.RelayoutUguiShellTheme(handle);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] UI Theme row click error: " + ex.Message);
            }
        }

        // IMGUI UiKit.cs:1544-1555 — click-time read, parse, and on success write + re-encode the
        // hex string + arm. On parse failure nothing happens (source parity — typed text stays).
        private void OnUguiThemeHexApplyClicked()
        {
            try
            {
                UguiShellThemeHandle handle = this.uguiShellTheme;
                if (handle == null || handle.HexField == null)
                {
                    return;
                }
                string typed = handle.HexField.text ?? string.Empty;
                this.uiThemeHexInput = typed;
                handle.HexSeen = typed; // the user's own text must not bounce back via the re-sync
                Color parsed;
                if (this.TryParseHexColor(this.uiThemeHexInput, out parsed))
                {
                    this.SetUiThemeColorTargetValue(this.uiThemeColorTarget, parsed);
                    this.uiThemeHexInput = this.ColorToHex(parsed); // Seen stays stale → re-sync shows the re-encoded value
                    this.ApplyUiThemeChangedClampAndArm();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] UI Theme hex apply error: " + ex.Message);
            }
        }

        // IMGUI UiKit.cs:1574-1592 — store raw, clamp inside the shared funnel, source gates.
        private void OnUguiThemeWindowAlphaChanged(float value)
        {
            if (Math.Abs(value - this.uiWindowAlpha) <= 0.001f)
            {
                return;
            }
            this.uiWindowAlpha = value;
            this.ApplyUiThemeChangedClampAndArm();
        }

        private void OnUguiThemePanelAlphaChanged(float value)
        {
            if (Math.Abs(value - this.uiPanelAlpha) <= 0.001f)
            {
                return;
            }
            this.uiPanelAlpha = value;
            this.ApplyUiThemeChangedClampAndArm();
        }

        private void OnUguiThemeContentAlphaChanged(float value)
        {
            if (Math.Abs(value - this.uiContentAlpha) <= 0.001f)
            {
                return;
            }
            this.uiContentAlpha = value;
            this.ApplyUiThemeChangedClampAndArm();
        }

        // ACTION card (IMGUI UiKit.cs:1626-1675). Save/Load are direct backend calls; Reset runs
        // the ONE shared implementation both surfaces use — deliberately no SaveUiTheme and no
        // uiThemePendingSaveAt inside it (reset stays unsaved until an explicit Save).
        private void OnUguiThemeSaveClicked()
        {
            try { this.SaveUiTheme(); }
            catch (Exception ex) { ModLogger.Msg("[UguiShell] UI Theme save error: " + ex.Message); }
        }

        private void OnUguiThemeLoadClicked()
        {
            try { this.LoadUiTheme(); }
            catch (Exception ex) { ModLogger.Msg("[UguiShell] UI Theme load error: " + ex.Message); }
        }

        private void OnUguiThemeResetClicked()
        {
            try { this.ResetUiThemeToDefaults(); }
            catch (Exception ex) { ModLogger.Msg("[UguiShell] UI Theme reset error: " + ex.Message); }
        }
    }
}
