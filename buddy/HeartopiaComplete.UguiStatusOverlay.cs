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
    // UGUI STATUS OVERLAY — Phase 2d (migration plan: cosmic-waddling-rainbow.md). The UGUI twin
    // of DrawStatusOverlay (HeartopiaComplete.UiKitPrimitives.cs:1352) + its caller block in
    // HeartopiaComplete.Gui.cs:106-125: a fixed-position (left edge, vertically centered,
    // clamped on-screen), content-sized readout driven by the persisted showStatusOverlay flag —
    // completely independent of showMenu/the shell.
    //
    // Deliberately NOT built through CreateUguiWindow: that factory bakes in center-anchoring and
    // a draggable title bar this overlay must not have (the IMGUI version has no drag, no title
    // bar, no close button). Instead: a minimal dedicated Canvas + panel, in the spirit of
    // EnsureModClickBlockerOverlay (HeartopiaComplete.CameraInput.cs), composed from individual
    // kit label/image factories.
    //
    // Hard invariants:
    //  - sortingOrder 29300 — inside the mod band (above the 20000 click-blocker, below the
    //    30000 Dropdown-popup ceiling; see the HeartopiaComplete.UguiKit.cs file header), and
    //    distinct from Shell (29400) / PoC (29500).
    //  - ZERO raycast surface: no GraphicRaycaster on the canvas AND raycastTarget=false on every
    //    Image/label (the kit factories default to false and nothing here opts in). IMGUI's
    //    version is pure rendered pixels — a raycastable UGUI twin would newly intercept clicks
    //    meant for the game world, a regression IMGUI could never cause.
    //  - NOT registered with the input-ownership registry (HeartopiaComplete.CameraInput.cs) —
    //    read-only chrome with nothing to protect, exactly like the IMGUI version was never part
    //    of UpdateGameUiClickBlockState/ShouldBlockGameplayInput.
    //  - Sizing reuses GetStatusOverlayWidth()/GetStatusOverlayHeight() verbatim and the canvas
    //    scaleFactor = GetUiScale() (the same value IMGUI's GUI.matrix uses), so 1 canvas unit
    //    = 1 IMGUI logical unit and the ox/oy clamp formula ports 1:1.
    //
    // Refresh strategy: content (and size/position) rebuilds only when a cheap signature over
    // CollectLiveFeatureStatusEntries() changes OR the screen size / UI scale changes; the FPS
    // footer label updates every frame from the SAME statusOverlaySmoothedFps/DisplayedFps/
    // nextStatusOverlayFpsRefreshAt fields both IMGUI drawers use (one smoothing state, ever).
    // Theme reload: registers its own "UguiStatusOverlay" rebuilder (idempotent by name) — unlike
    // the shell's LIVE rail it is not part of any window's build, so nothing else would refresh it.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private GameObject uguiStatusOverlayRoot;
        private Canvas uguiStatusOverlayCanvas;
        private RectTransform uguiStatusOverlayPanelRt;
        private readonly List<GameObject> uguiStatusOverlayContent = new List<GameObject>(); // direct panel children
        private GameObject uguiStatusOverlayFpsValue;
        private string uguiStatusOverlaySignature;
        private bool uguiStatusOverlayBuildFailed;
        private int uguiStatusOverlayErrorCount; // driver disabled at 3 (DragErrorCount idiom)
        private float uguiStatusOverlayLastScale = -1f;
        private int uguiStatusOverlayLastScreenW = -1;
        private int uguiStatusOverlayLastScreenH = -1;

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (wired next to ProcessUguiShellOnUpdate/ProcessUguiPocOnUpdate in
        // OnUpdate). Also called directly by the Settings "Show Status Overlay" toggle handler
        // (HeartopiaComplete.Config.cs) so the flip lands the same frame — one shared path for
        // lazy-build, visibility reconcile and content refresh. Reconciling against the LIVE
        // showStatusOverlay field every frame also covers the non-toggle mutation paths
        // (config load / reset) for free.
        // ----------------------------------------------------------------------------------------
        private void ProcessUguiStatusOverlayOnUpdate()
        {
            if (this.uguiStatusOverlayErrorCount >= 3)
            {
                return;
            }

            try
            {
                if (!this.showStatusOverlay)
                {
                    if (this.uguiStatusOverlayRoot != null && this.uguiStatusOverlayRoot.activeSelf)
                    {
                        this.uguiStatusOverlayRoot.SetActive(false);
                    }
                    return;
                }

                if (this.uguiStatusOverlayRoot == null)
                {
                    if (this.uguiStatusOverlayBuildFailed)
                    {
                        return; // already failed once this session; don't retry every frame
                    }

                    this.BuildUguiStatusOverlayRoot();
                    if (this.uguiStatusOverlayRoot == null)
                    {
                        this.uguiStatusOverlayBuildFailed = true;
                        ModLogger.Msg("[UguiStatusOverlay] build failed — see errors above");
                        return;
                    }

                    // Live theme reload — own registration (idempotent by name): this canvas is
                    // not part of the shell/PoC builds, so no other rebuilder covers it.
                    this.RegisterUguiThemeRebuilder("UguiStatusOverlay", new System.Action(this.RebuildUguiStatusOverlayForTheme));
                }

                if (!this.uguiStatusOverlayRoot.activeSelf)
                {
                    this.uguiStatusOverlayRoot.SetActive(true);
                }

                // Screen/scale reads are trivial — recheck alongside the entries diff each frame.
                float scale = this.GetUiScale();
                bool metricsChanged = scale != this.uguiStatusOverlayLastScale
                    || Screen.width != this.uguiStatusOverlayLastScreenW
                    || Screen.height != this.uguiStatusOverlayLastScreenH;
                if (metricsChanged && this.uguiStatusOverlayCanvas != null)
                {
                    this.uguiStatusOverlayCanvas.scaleFactor = scale;
                }

                List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
                string signature = this.BuildLiveFeatureStatusSignature(entries);
                if (metricsChanged || !string.Equals(signature, this.uguiStatusOverlaySignature, StringComparison.Ordinal))
                {
                    this.uguiStatusOverlaySignature = signature;
                    this.uguiStatusOverlayLastScale = scale;
                    this.uguiStatusOverlayLastScreenW = Screen.width;
                    this.uguiStatusOverlayLastScreenH = Screen.height;
                    this.RebuildUguiStatusOverlayContent(entries);
                }

                // FPS footer: cheap single-label update every frame, shared smoothing fields.
                this.TickStatusOverlayFpsShared();
                this.SetUguiLabelText(this.uguiStatusOverlayFpsValue, this.GetStatusOverlayFpsDisplayText());
            }
            catch (Exception ex)
            {
                this.uguiStatusOverlayErrorCount++;
                ModLogger.Msg("[UguiStatusOverlay] update error (" + this.uguiStatusOverlayErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Theme-change rebuild (registered via RegisterUguiThemeRebuilder): destroy + rebuild so
        // every Image/label re-reads the live ui* theme fields. No window state to preserve (the
        // overlay is fixed-position, content-sized); nulling the signature/metrics forces the
        // driver to rebuild immediately — or lazily on next enable if currently hidden.
        private void RebuildUguiStatusOverlayForTheme()
        {
            try
            {
                if (this.uguiStatusOverlayRoot == null)
                {
                    return; // never built — nothing stale (a first build reads fresh colors)
                }

                try { Object.Destroy(this.uguiStatusOverlayRoot); } catch { }
                this.uguiStatusOverlayRoot = null;
                this.uguiStatusOverlayCanvas = null;
                this.uguiStatusOverlayPanelRt = null;
                this.uguiStatusOverlayFpsValue = null;
                this.uguiStatusOverlayContent.Clear();
                this.uguiStatusOverlaySignature = null;
                this.uguiStatusOverlayLastScale = -1f;
                this.uguiStatusOverlayLastScreenW = -1;
                this.uguiStatusOverlayLastScreenH = -1;

                this.ProcessUguiStatusOverlayOnUpdate(); // rebuild now if it should be showing
                ModLogger.Msg("[UguiStatusOverlay] rebuilt for theme change");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiStatusOverlay] theme rebuild error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------------------------------

        private void BuildUguiStatusOverlayRoot()
        {
            GameObject go = null;
            try
            {
                this.EnsureUguiFonts();

                go = new GameObject("BugtopiaUguiStatusOverlay");
                Object.DontDestroyOnLoad(go);
                go.SetActive(false);

                Canvas canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 29300; // mod band (20000..30000); Shell=29400, PoC=29500
                canvas.scaleFactor = this.GetUiScale();
                // NO GraphicRaycaster — this canvas can never participate in pointer raycasts,
                // which (with raycastTarget=false everywhere) is the hard guarantee that this
                // read-only readout never intercepts a click meant for the game world.

                GameObject panel = this.CreateUguiGo("Panel", go.transform);
                // Placed (and populated) by RebuildUguiStatusOverlayContent.

                this.uguiStatusOverlayRoot = go;
                this.uguiStatusOverlayCanvas = canvas;
                this.uguiStatusOverlayPanelRt = panel.GetComponent<RectTransform>();
                this.uguiStatusOverlaySignature = null; // force first content build
                this.uguiStatusOverlayLastScale = -1f;
                this.uguiStatusOverlayLastScreenW = -1;
                this.uguiStatusOverlayLastScreenH = -1;
                ModLogger.Msg("[UguiStatusOverlay] built (sortingOrder 29300, no raycaster)");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiStatusOverlay] BuildUguiStatusOverlayRoot error: " + ex.Message);
                try
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
                catch { }
                this.uguiStatusOverlayRoot = null;
                this.uguiStatusOverlayCanvas = null;
                this.uguiStatusOverlayPanelRt = null;
            }
        }

        // Rebuilds the panel content, mirroring DrawStatusOverlay's actual layout (header
        // "Bugtopia" + ACTIVE/STANDBY badge, feature rows, detail sub-rows, dividers, FPS footer)
        // and re-derives size/position from the EXISTING IMGUI functions/formula verbatim
        // (GetStatusOverlayWidth/Height + the Gui.cs:106-125 ox/oy clamp).
        private void RebuildUguiStatusOverlayContent(List<LiveFeatureStatusEntry> entries)
        {
            RectTransform panelRt = this.uguiStatusOverlayPanelRt;
            if (panelRt == null)
            {
                return;
            }

            for (int i = 0; i < this.uguiStatusOverlayContent.Count; i++)
            {
                GameObject old = this.uguiStatusOverlayContent[i];
                if (old != null)
                {
                    try { Object.Destroy(old); } catch { }
                }
            }
            this.uguiStatusOverlayContent.Clear();
            this.uguiStatusOverlayFpsValue = null;

            // --- Size + position: IMGUI caller block ported 1:1 (canvas units = logical units
            // because scaleFactor = GetUiScale()). ---
            float ow = this.GetStatusOverlayWidth();
            float oh = this.GetStatusOverlayHeight();
            float screenW = this.GetLogicalScreenWidth();
            float screenH = this.GetLogicalScreenHeight();
            float ox = Mathf.Clamp(16f, 8f, screenW - ow - 8f);
            float oy = Mathf.Clamp((screenH - oh) * 0.5f, 72f, screenH - oh - 24f);
            GameObject panelGo = panelRt.gameObject;
            PlaceUguiTopLeft(panelGo, ox, oy, ow, oh);

            // --- Palette: DrawStatusOverlay's literals + live theme text/accent fields. ---
            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.98f);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.88f);
            Color separator = new Color(1f, 1f, 1f, 0.06f);
            Color overlayFill = new Color(0.08f, 0.10f, 0.13f, 0.94f);
            Color overlayHeaderFill = new Color(0.10f, 0.12f, 0.17f, 0.98f);
            Color overlayFooterFill = new Color(0.07f, 0.08f, 0.11f, 0.98f);
            Color overlayBorder = new Color(0.165f, 0.205f, 0.27f, 0.9f);
            Color badgeFill = new Color(this.uiAccentR * 0.42f, this.uiAccentG * 0.42f, this.uiAccentB * 0.58f, 0.98f);
            Color badgeIdleFill = new Color(0.17f, 0.20f, 0.27f, 0.98f);
            bool hasActiveSystems = entries.Count > 0;

            // --- IMGUI rect math in panel-local coords: Gui.cs passes DrawStatusOverlay an inner
            // rect inset 10 from the overlay rect; the drawer expands it back by 6 → the visible
            // frame is the overlay rect inset by 4 on all sides. ---
            float frameX = 4f;
            float frameY = 4f;
            float frameW = ow - 8f;
            float frameH = oh - 8f;
            float headerH = 38f;
            float bodyX = frameX + 10f;                  // 14
            float bodyY = frameY + 1f + headerH + 8f;    // 51
            float bodyW = frameW - 20f;                  // ow - 28
            float footerY = frameY + frameH - 37f;       // oh - 41
            float footerW = frameW - 2f;                 // ow - 10

            // Frame: rounded fill + border ring (fallback branch of the IMGUI drawer; radius 10).
            GameObject frame = this.CreateUguiGo("Frame", panelRt);
            PlaceUguiTopLeft(frame, frameX, frameY, frameW, frameH);
            this.AddUguiImage(frame, overlayFill, true, 1f);
            this.AddUguiRingOverlay(frame, overlayBorder, 1f);
            this.uguiStatusOverlayContent.Add(frame);

            // Header strip + hairline under it.
            GameObject header = this.CreateUguiGo("Header", panelRt);
            PlaceUguiTopLeft(header, frameX + 1f, frameY + 1f, frameW - 2f, headerH);
            this.AddUguiImage(header, overlayHeaderFill, true, 1f);
            this.uguiStatusOverlayContent.Add(header);

            GameObject headerLine = this.CreateUguiGo("HeaderLine", panelRt);
            PlaceUguiTopLeft(headerLine, bodyX, bodyY - 4f, bodyW, 1f);
            this.AddUguiImage(headerLine, separator, false, 1f);
            this.uguiStatusOverlayContent.Add(headerLine);

            GameObject title = this.CreateUguiLabel(panelRt, "Title", "Bugtopia", 13f, textPrimary, false);
            this.TrySetUguiLabelBold(title);
            PlaceUguiTopLeft(title, frameX + 1f + 12f, frameY + 1f + 8f, 116f, 22f);
            this.uguiStatusOverlayContent.Add(title);

            // ACTIVE/STANDBY badge capsule.
            GameObject badge = this.CreateUguiGo("Badge", panelRt);
            PlaceUguiTopLeft(badge, frameX + 1f + frameW - 2f - 82f, frameY + 1f + 8f, 70f, 22f);
            this.AddUguiImage(badge, hasActiveSystems ? badgeFill : badgeIdleFill, true, 1f);
            GameObject badgeLabel = this.CreateUguiLabel(badge.transform, "Label",
                hasActiveSystems ? this.L("ACTIVE") : this.L("STANDBY"), 8f, textPrimary, true);
            this.TrySetUguiLabelBold(badgeLabel);
            StretchUguiFill(badgeLabel, 0f, 0f, 0f, 0f);
            this.uguiStatusOverlayContent.Add(badge);

            // Body rows — same cursor flow as the drawer's drawFeature/drawDetail/finishBlock.
            float rowY = bodyY;
            if (!hasActiveSystems)
            {
                GameObject idle = this.CreateUguiLabel(panelRt, "Idle", this.L("All systems idle"), 10f, textMuted, false);
                PlaceUguiTopLeft(idle, bodyX + 8f, rowY + 8f, bodyW - 16f, 22f);
                this.uguiStatusOverlayContent.Add(idle);
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    LiveFeatureStatusEntry entry = entries[i];

                    GameObject featureLabel = this.CreateUguiLabel(panelRt, "Feature" + i,
                        this.L(entry.Label), 11f, textPrimary, false);
                    this.TrySetUguiLabelBold(featureLabel);
                    PlaceUguiTopLeft(featureLabel, bodyX + 8f, rowY + 2f, 112f, 20f);
                    this.uguiStatusOverlayContent.Add(featureLabel);

                    GameObject featureValue = this.CreateUguiLabel(panelRt, "FeatureValue" + i,
                        this.L(entry.Summary), 10f, textPrimary, false);
                    this.TrySetUguiLabelRightAligned(featureValue);
                    PlaceUguiTopLeft(featureValue, bodyX + 120f, rowY + 2f, bodyW - 128f, 20f);
                    this.uguiStatusOverlayContent.Add(featureValue);
                    rowY += 26f;

                    List<LiveFeatureStatusDetail> details = entry.Details;
                    if (details != null)
                    {
                        for (int j = 0; j < details.Count; j++)
                        {
                            LiveFeatureStatusDetail detail = details[j];

                            GameObject detailLabel = this.CreateUguiLabel(panelRt, "Detail" + i + "_" + j,
                                this.L(detail.Label), 10f, textMuted, false);
                            PlaceUguiTopLeft(detailLabel, bodyX + 18f, rowY + 1f, 92f, 18f);
                            this.uguiStatusOverlayContent.Add(detailLabel);

                            GameObject detailValue = this.CreateUguiLabel(panelRt, "DetailValue" + i + "_" + j,
                                this.L(detail.Value), 10f, textPrimary, false);
                            this.TrySetUguiLabelRightAligned(detailValue);
                            PlaceUguiTopLeft(detailValue, bodyX + 110f, rowY + 1f, bodyW - 118f, 18f);
                            this.uguiStatusOverlayContent.Add(detailValue);
                            rowY += 22f;
                        }
                    }

                    if (i < entries.Count - 1)
                    {
                        rowY += 6f;
                        GameObject divider = this.CreateUguiGo("Divider" + i, panelRt);
                        PlaceUguiTopLeft(divider, bodyX + 2f, rowY - 2f, bodyW - 4f, 1f);
                        this.AddUguiImage(divider, separator, false, 1f);
                        this.uguiStatusOverlayContent.Add(divider);
                        rowY += 8f;
                    }
                }
            }

            // Footer strip + hairline + FPS readout.
            GameObject footer = this.CreateUguiGo("Footer", panelRt);
            PlaceUguiTopLeft(footer, frameX + 1f, footerY, footerW, 36f);
            this.AddUguiImage(footer, overlayFooterFill, true, 1f);
            this.uguiStatusOverlayContent.Add(footer);

            GameObject footerLine = this.CreateUguiGo("FooterLine", panelRt);
            PlaceUguiTopLeft(footerLine, frameX + 1f, footerY, footerW, 1f);
            this.AddUguiImage(footerLine, separator, false, 1f);
            this.uguiStatusOverlayContent.Add(footerLine);

            GameObject fpsLabel = this.CreateUguiLabel(panelRt, "FpsLabel", this.L("FPS"), 10f, textMuted, false);
            this.TrySetUguiLabelBold(fpsLabel);
            PlaceUguiTopLeft(fpsLabel, frameX + 1f + 12f, footerY + 9f, 60f, 20f);
            this.uguiStatusOverlayContent.Add(fpsLabel);

            GameObject fpsValue = this.CreateUguiLabel(panelRt, "FpsValue",
                this.GetStatusOverlayFpsDisplayText(), 11f, textPrimary, false);
            this.TrySetUguiLabelBold(fpsValue);
            this.TrySetUguiLabelRightAligned(fpsValue);
            PlaceUguiTopLeft(fpsValue, frameX + 1f + 72f, footerY + 8f, footerW - 84f, 22f);
            this.uguiStatusOverlayContent.Add(fpsValue);
            this.uguiStatusOverlayFpsValue = fpsValue;
        }

        // ----------------------------------------------------------------------------------------
        // Shared helpers — used by BOTH this overlay and the shell's LIVE rail
        // (HeartopiaComplete.UguiShell.cs), so the two UGUI consumers can never drift apart.
        // ----------------------------------------------------------------------------------------

        // Cheap change-detection signature over the live entries (labels + summaries + detail
        // label/values, raw untranslated strings). The entries list is small (~24 capacity hint);
        // building this every frame is far cheaper than unconditional GameObject rebuilds.
        private string BuildLiveFeatureStatusSignature(List<LiveFeatureStatusEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(entries.Count * 32);
            for (int i = 0; i < entries.Count; i++)
            {
                LiveFeatureStatusEntry entry = entries[i];
                sb.Append(entry.Label).Append('\u001f').Append(entry.Summary).Append('\u001e');
                List<LiveFeatureStatusDetail> details = entry.Details;
                if (details != null)
                {
                    for (int j = 0; j < details.Count; j++)
                    {
                        sb.Append(details[j].Label).Append('\u001f').Append(details[j].Value).Append('\u001e');
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // The exact FPS smoothing/throttle block both IMGUI drawers run (DrawQuickStatusPanel /
        // DrawStatusOverlay), operating on the SAME shared fields — the UGUI pieces call this
        // instead of duplicating state, so all four consumers always display the same reading.
        // Running it more than once per frame only tightens the lerp slightly (IMGUI already runs
        // it once per GUI event); the displayed value still refreshes at most every 0.35s.
        private void TickStatusOverlayFpsShared()
        {
            float currentFps = Time.unscaledDeltaTime > 0.0001f ? (1f / Time.unscaledDeltaTime) : this.statusOverlaySmoothedFps;
            if (this.statusOverlaySmoothedFps <= 0f)
            {
                this.statusOverlaySmoothedFps = currentFps;
            }
            else if (currentFps > 0f)
            {
                this.statusOverlaySmoothedFps = Mathf.Lerp(this.statusOverlaySmoothedFps, currentFps, 0.05f);
            }

            if (Time.unscaledTime >= this.nextStatusOverlayFpsRefreshAt)
            {
                this.statusOverlayDisplayedFps = this.statusOverlaySmoothedFps;
                this.nextStatusOverlayFpsRefreshAt = Time.unscaledTime + 0.35f;
            }
        }

        private string GetStatusOverlayFpsDisplayText()
        {
            return this.statusOverlayDisplayedFps > 0f ? Mathf.RoundToInt(this.statusOverlayDisplayedFps).ToString() : "--";
        }

        // Right-aligned label variant (the kit's CreateUguiLabel only does left/centered) — the
        // IMGUI overlay right-aligns every value column (detailValueStyle/footerValueStyle).
        private void TrySetUguiLabelRightAligned(GameObject label)
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
                    tmp.alignment = TextAlignmentOptions.MidlineRight;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.alignment = TextAnchor.MiddleRight;
                }
            }
            catch { }
        }
    }
}
