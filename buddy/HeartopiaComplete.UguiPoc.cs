using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI PROOF OF CONCEPT — F9 toggles. Originally hand-built control by control; now constructed
    // ENTIRELY from the reusable factory library in HeartopiaComplete.UguiKit.cs, which is the
    // point: this panel is the kit's validation surface. If the panel still looks/behaves the same
    // as the hand-built version did, the kit covers real needs. All the hard-won interop knowledge
    // (stripped TMP_Dropdown, sorting-order ceilings, scale-aware drag math, generic AddListener
    // AOT risk, ...) lives in the kit's file header now, not here.
    //
    // Layout: 500x600 window (canvas sortingOrder 29500, under the Dropdown-popup 30000 ceiling),
    // draggable title bar, PageUp/PageDown scale 0.5x..3.0x, 3 tabs:
    //   Controls — one of each: Primary button, checkbox Toggle, switch Toggle, Slider, Dropdown,
    //              compact ScrollRect (18 rows)
    //   Scroll   — second full-size ScrollRect instance (24 rows)
    //   About    — placeholder text + the button-tier demo row (Primary / Secondary / Danger)
    //              + the Phase 2e toast auto-sizing spike (text-measured card, GetPreferredValues)
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private UguiWindowHandle uguiPocWindow;
        private bool uguiPocBuildFailed;
        private int uguiPocClickCount;
        private GameObject uguiPocClickCountLabel;
        private GameObject uguiPocSliderValueLabel;
        private GameObject uguiPocDropdownValueLabel;
        private GameObject uguiPocStatusLabel;
        private Dropdown uguiPocDropdown;
        private bool uguiPocDropdownPollFallback;
        private int uguiPocLastDropdownValue = -1;
        private readonly List<string> uguiPocBuildNotes = new List<string>();
        private UguiTabBarHandle uguiPocTabBar;
        private bool uguiPocDiagLogged; // TEMP: one-shot window-bg diagnostics (see ProcessUguiPocOnUpdate)

        // Toast auto-sizing spike (About tab, Phase 2e) — see BuildUguiPocToastSpike.
        private GameObject uguiPocToastCard;
        private GameObject uguiPocToastCardLabel;
        private GameObject uguiPocToastMeasureLabel;
        private int uguiPocToastTextIndex;

        // ----------------------------------------------------------------------------------------
        // Entry points
        // ----------------------------------------------------------------------------------------

        // F9 (see OnUpdate hotkey block). Lazy-create once, then just flip active state.
        private void ToggleUguiPocPanel()
        {
            try
            {
                if (this.uguiPocWindow == null)
                {
                    if (this.uguiPocBuildFailed)
                    {
                        return; // already failed once this session; don't retry every keypress
                    }

                    this.BuildUguiPocPanel();
                    if (this.uguiPocWindow == null)
                    {
                        this.uguiPocBuildFailed = true;
                        ModLogger.Msg("[UguiPoc] panel build failed — see errors above");
                        return;
                    }

                    if (EventSystem.current == null)
                    {
                        ModLogger.Msg("[UguiPoc] WARNING: no EventSystem in scene — panel will render but not receive clicks");
                    }

                    this.SetUguiWindowVisible(this.uguiPocWindow, true);
                    // Live theme reload: rebuild this panel (state-preserving) when the IMGUI
                    // theme editor changes colors. Registration is idempotent by name.
                    this.RegisterUguiThemeRebuilder("UguiPoc", new System.Action(this.RebuildUguiPocPanelForTheme));
                    // Input ownership: the PoC is a small dev/test harness, not a menu takeover —
                    // FLOATING (clicks blocked only while the pointer is over its panel; never
                    // blocks movement). Closures read the LIVE uguiPocWindow field on every call,
                    // so theme-reload rebuilds (which replace the handle) are picked up; never
                    // capture the handle object itself.
                    this.RegisterInputOwnershipSurface("UguiPoc", false,
                        () => this.uguiPocWindow != null && this.IsUguiWindowVisible(this.uguiPocWindow),
                        () => this.IsUguiWindowPointerOver(this.uguiPocWindow));
                    ModLogger.Msg("[UguiPoc] panel built and shown (F9 toggles)");
                    return;
                }

                bool show = !this.IsUguiWindowVisible(this.uguiPocWindow);
                this.SetUguiWindowVisible(this.uguiPocWindow, show);
                ModLogger.Msg("[UguiPoc] panel " + (show ? "shown" : "hidden"));
            }
            catch (Exception ex)
            {
                this.uguiPocBuildFailed = true;
                ModLogger.Msg("[UguiPoc] toggle error: " + ex.Message);
            }
        }

        // Called from OnUpdate every frame. The kit's frame driver handles drag + scale keys; the
        // dropdown poll only runs if listener wiring failed at build time.
        private void ProcessUguiPocOnUpdate()
        {
            if (!this.IsUguiWindowVisible(this.uguiPocWindow))
            {
                return;
            }

            this.ProcessUguiWindowFrame(this.uguiPocWindow);

            // TEMP DIAGNOSTIC (window-bg regression) — logs the render state of the two backdrop
            // layers vs a known-good control image ONCE, the first frame the panel is visible.
            // Remove after the regression is confirmed dead in-game.
            if (!this.uguiPocDiagLogged)
            {
                this.uguiPocDiagLogged = true;
                try
                {
                    this.LogUguiPocImageDiag("WindowSlab", this.uguiPocWindow.BackdropSlab);
                    this.LogUguiPocImageDiag("WindowTint", this.uguiPocWindow.BackdropTint);
                    Image tabBg = (this.uguiPocTabBar != null && this.uguiPocTabBar.ButtonBgs.Count > 0)
                        ? this.uguiPocTabBar.ButtonBgs[0]
                        : null;
                    this.LogUguiPocImageDiag("TabBg0-knownGood", tabBg);
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiPocDiag] diagnostics failed: " + ex.Message);
                }
            }

            if (this.uguiPocDropdownPollFallback)
            {
                try
                {
                    if (this.uguiPocDropdown != null)
                    {
                        int v = this.uguiPocDropdown.value;
                        if (v != this.uguiPocLastDropdownValue)
                        {
                            this.uguiPocLastDropdownValue = v;
                            this.OnUguiPocDropdownChanged(v);
                        }
                    }
                }
                catch
                {
                    // A dead dropdown must never wedge OnUpdate.
                }
            }
        }

        // Theme-change rebuild (registered via RegisterUguiThemeRebuilder): destroy + rebuild so
        // every Image/label re-reads the live ui* theme fields, preserving window position/scale/
        // visibility and the active tab. Demo-control values (slider position, dropdown pick,
        // toggle states) intentionally reset — they are placeholder content; the click counter
        // survives because its backing field does.
        private void RebuildUguiPocPanelForTheme()
        {
            try
            {
                if (this.uguiPocWindow == null)
                {
                    return; // never built — nothing stale
                }

                UguiWindowRestoreState state = this.CaptureUguiWindowState(this.uguiPocWindow);
                int activeTab = (this.uguiPocTabBar != null) ? this.uguiPocTabBar.ActiveIndex : 0;

                try
                {
                    if (this.uguiPocWindow.Root != null)
                    {
                        Object.Destroy(this.uguiPocWindow.Root);
                    }
                }
                catch { }
                this.uguiPocWindow = null;
                this.uguiPocTabBar = null;
                this.uguiPocDropdown = null;
                this.uguiPocDropdownPollFallback = false;
                this.uguiPocToastCard = null;
                this.uguiPocToastCardLabel = null;
                this.uguiPocToastMeasureLabel = null;

                this.BuildUguiPocPanel();
                if (this.uguiPocWindow == null)
                {
                    this.uguiPocBuildFailed = true;
                    ModLogger.Msg("[UguiPoc] theme rebuild failed — panel not recreated");
                    return;
                }

                this.SelectUguiTab(this.uguiPocTabBar, activeTab);
                this.RestoreUguiWindowState(this.uguiPocWindow, state);
                ModLogger.Msg("[UguiPoc] rebuilt for theme change");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiPoc] theme rebuild error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Panel construction — everything below is composition of UguiKit factories.
        // ----------------------------------------------------------------------------------------

        private void BuildUguiPocPanel()
        {
            UguiWindowHandle win = null;
            try
            {
                this.uguiPocBuildNotes.Clear();

                win = this.CreateUguiWindow(
                    "BugtopiaUguiPoc",
                    "UGUI PROOF OF CONCEPT",
                    "UguiKit factories, no prefabs — F9 toggles · drag title · PgUp/PgDn scale",
                    new Vector2(500f, 600f),
                    29500);
                this.EnableUguiWindowScaleKeys(win, KeyCode.PageUp, KeyCode.PageDown);
                Transform panelT = win.PanelRt;

                // Tab content containers — one per tab, the tab bar SetActives between them.
                GameObject controlsTab = this.CreateUguiGo("TabContent_Controls", panelT);
                PlaceUguiTopLeft(controlsTab, 0f, 100f, 500f, 456f);
                GameObject scrollTab = this.CreateUguiGo("TabContent_Scroll", panelT);
                PlaceUguiTopLeft(scrollTab, 0f, 100f, 500f, 456f);
                GameObject aboutTab = this.CreateUguiGo("TabContent_About", panelT);
                PlaceUguiTopLeft(aboutTab, 0f, 100f, 500f, 456f);
                Transform controlsT = controlsTab.transform;

                // Everything in its own try/catch so one broken piece can't kill the panel, and
                // every failure is recorded for the status line (honest per-control reporting).
                this.TryBuildUguiPocControl("TabBar", () =>
                {
                    this.uguiPocTabBar = this.CreateUguiTabBar(
                        panelT, 24f, 64f, 130f, 30f, 6f,
                        new string[] { "Controls", "Scroll", "About" },
                        // NavIcons indices: 2 = Features, 8 = Settings gear; Scroll deliberately
                        // icon-less to confirm mixed tabs align.
                        new int[] { 2, -1, 8 },
                        new GameObject[] { controlsTab, scrollTab, aboutTab },
                        0, null);
                });
                this.TryBuildUguiPocControl("Button", () => this.BuildUguiPocButton(controlsT));
                this.TryBuildUguiPocControl("CheckboxToggle", () => this.BuildUguiPocCheckbox(controlsT));
                this.TryBuildUguiPocControl("SwitchToggle", () => this.BuildUguiPocSwitch(controlsT));
                this.TryBuildUguiPocControl("Slider", () => this.BuildUguiPocSlider(controlsT));
                this.TryBuildUguiPocControl("Dropdown", () => this.BuildUguiPocDropdown(controlsT));
                this.TryBuildUguiPocControl("ScrollRect", () => this.BuildUguiPocScrollList(
                    controlsT, 24f, 224f, 452f, 222f, 18, "ScrollRect (18 rows, RectMask2D clipped)"));
                this.TryBuildUguiPocControl("ScrollRectBig", () => this.BuildUguiPocScrollList(
                    scrollTab.transform, 24f, 8f, 452f, 440f, 24, "Second ScrollRect instance — full-tab size (24 rows)"));
                this.TryBuildUguiPocControl("AboutTab", () => this.BuildUguiPocAboutTab(aboutTab.transform));
                this.TryBuildUguiPocControl("ToastSpike", () => this.BuildUguiPocToastSpike(aboutTab.transform));

                // Status line: font resolution (kit-owned fields) + any per-control failures.
                string tmpName = "MISSING";
                try { if (this.uguiKitTmpFont != null) tmpName = this.uguiKitTmpFont.name; } catch { }
                string legacyName = "MISSING";
                try { if (this.uguiKitLegacyFont != null) legacyName = this.uguiKitLegacyFont.name; } catch { }
                string status = "TMP font: " + tmpName + "  |  Legacy font: " + legacyName;
                if (this.uguiPocBuildNotes.Count > 0)
                {
                    status += "  |  FAILED: " + string.Join(", ", this.uguiPocBuildNotes.ToArray());
                }
                this.uguiPocStatusLabel = this.CreateUguiMutedLabel(panelT, "Status", status, 10f);
                PlaceUguiTopLeft(this.uguiPocStatusLabel, 24f, 568f, 452f, 20f);

                this.uguiPocWindow = win;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiPoc] BuildUguiPocPanel error: " + ex.Message);
                try
                {
                    if (win != null && win.Root != null)
                    {
                        Object.Destroy(win.Root);
                    }
                }
                catch { }
                this.uguiPocWindow = null;
            }
        }

        private void TryBuildUguiPocControl(string name, System.Action build)
        {
            try
            {
                build();
            }
            catch (Exception ex)
            {
                this.uguiPocBuildNotes.Add(name);
                ModLogger.Msg("[UguiPoc] " + name + " build failed: " + ex.Message);
            }
        }

        // TEMP DIAGNOSTIC (window-bg regression) — every read individually guarded so a single
        // broken property can't hide the rest of the line. Remove once root-caused in-game.
        private void LogUguiPocImageDiag(string tag, Image img)
        {
            if (img == null)
            {
                ModLogger.Msg("[UguiPocDiag] " + tag + ": Image NULL");
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
            sb.Append("[UguiPocDiag] ").Append(tag).Append(":");
            try { sb.Append(" enabled=").Append(img.enabled); } catch { sb.Append(" enabled=?"); }
            try { sb.Append(" activeInHierarchy=").Append(img.gameObject.activeInHierarchy); } catch { sb.Append(" activeInHierarchy=?"); }
            try
            {
                Color c = img.color;
                sb.Append(" color=(").Append(c.r.ToString("0.###")).Append(",").Append(c.g.ToString("0.###"))
                  .Append(",").Append(c.b.ToString("0.###")).Append(",").Append(c.a.ToString("0.###")).Append(")");
            }
            catch { sb.Append(" color=?"); }
            try { sb.Append(" sprite=").Append(img.sprite != null ? "set" : "NULL"); } catch { sb.Append(" sprite=?"); }
            try
            {
                if (img.sprite != null)
                {
                    Vector4 b = img.sprite.border;
                    sb.Append(" border=(").Append(b.x).Append(",").Append(b.y).Append(",").Append(b.z).Append(",").Append(b.w).Append(")");
                }
            }
            catch { sb.Append(" border=?"); }
            try { sb.Append(" type=").Append((int)img.type); } catch { sb.Append(" type=?"); }
            try { sb.Append(" ppuMult=").Append(img.pixelsPerUnitMultiplier.ToString("0.###")); } catch { sb.Append(" ppuMult=?"); }
            try { sb.Append(" ppu=").Append(img.pixelsPerUnit.ToString("0.###")); } catch { sb.Append(" ppu=?"); }
            try
            {
                Rect r = img.rectTransform.rect;
                sb.Append(" rect=").Append(r.width.ToString("0.#")).Append("x").Append(r.height.ToString("0.#"));
            }
            catch { sb.Append(" rect=?"); }
            try
            {
                CanvasRenderer cr = img.canvasRenderer;
                if (cr != null)
                {
                    sb.Append(" cull=").Append(cr.cull);
                    sb.Append(" cullTransparentMesh=").Append(cr.cullTransparentMesh);
                    sb.Append(" crAlpha=").Append(cr.GetAlpha().ToString("0.###"));
                    sb.Append(" inheritedAlpha=").Append(cr.GetInheritedAlpha().ToString("0.###"));
                }
                else
                {
                    sb.Append(" canvasRenderer=NULL");
                }
            }
            catch { sb.Append(" canvasRenderer=?"); }
            ModLogger.Msg(sb.ToString());
        }

        // --- Controls tab (positions are tab-container-local) ------------------------------------

        private void BuildUguiPocButton(Transform parent)
        {
            GameObject btn = this.CreateUguiPrimaryButton(parent, "TestButton", "Test Button",
                new System.Action(this.OnUguiPocButtonClicked));
            PlaceUguiTopLeft(btn, 24f, 8f, 150f, 34f);

            // Seed from the field (not "0") so the count survives theme-change rebuilds.
            this.uguiPocClickCountLabel = this.CreateUguiBodyLabel(parent, "ClickCount", "Clicks: " + this.uguiPocClickCount, 14f);
            PlaceUguiTopLeft(this.uguiPocClickCountLabel, 190f, 12f, 286f, 26f);
        }

        private void OnUguiPocButtonClicked()
        {
            this.uguiPocClickCount++;
            ModLogger.Msg("[UguiPoc] Test Button clicked (count=" + this.uguiPocClickCount + ")");
            this.SetUguiLabelText(this.uguiPocClickCountLabel, "Clicks: " + this.uguiPocClickCount);
        }

        private void BuildUguiPocCheckbox(Transform parent)
        {
            Toggle tog = this.CreateUguiCheckbox(parent, "CheckboxToggle", "Checkbox Toggle", true,
                new System.Action<bool>(this.OnUguiPocCheckboxChanged));
            PlaceUguiTopLeft(tog.gameObject, 24f, 56f, 300f, 24f);
        }

        private void OnUguiPocCheckboxChanged(bool value)
        {
            ModLogger.Msg("[UguiPoc] Checkbox toggled: " + value);
        }

        private void BuildUguiPocSwitch(Transform parent)
        {
            Toggle tog = this.CreateUguiSwitch(parent, "SwitchToggle", "Switch Toggle", false,
                new System.Action<bool>(this.OnUguiPocSwitchChanged));
            PlaceUguiTopLeft(tog.gameObject, 24f, 94f, 300f, 24f);
        }

        private void OnUguiPocSwitchChanged(bool value)
        {
            ModLogger.Msg("[UguiPoc] Switch toggled: " + value);
        }

        private void BuildUguiPocSlider(Transform parent)
        {
            Slider slider = this.CreateUguiSlider(parent, "Slider", 0f, 100f, 65f, false,
                new System.Action<float>(this.OnUguiPocSliderChanged));
            PlaceUguiTopLeft(slider.gameObject, 24f, 138f, 300f, 20f);

            // Value readout uses the kit's Value role (accent) — the numeric-display convention.
            this.uguiPocSliderValueLabel = this.CreateUguiValueLabel(parent, "SliderValue", "65", 14f);
            PlaceUguiTopLeft(this.uguiPocSliderValueLabel, 336f, 136f, 140f, 24f);
        }

        private void OnUguiPocSliderChanged(float value)
        {
            this.SetUguiLabelText(this.uguiPocSliderValueLabel, value.ToString("0"));
        }

        private void BuildUguiPocDropdown(Transform parent)
        {
            bool wired;
            Dropdown dd = this.CreateUguiDropdown(parent, "Dropdown",
                new string[] { "Quality: Low", "Quality: Medium", "Quality: High", "Quality: Ultra" },
                0,
                new System.Action<int>(this.OnUguiPocDropdownChanged),
                out wired);
            PlaceUguiTopLeft(dd.gameObject, 24f, 176f, 240f, 32f);

            this.uguiPocDropdown = dd;
            this.uguiPocLastDropdownValue = 0;
            this.uguiPocDropdownPollFallback = !wired; // per-frame value poll takes over if wiring threw
            this.uguiPocDropdownValueLabel = this.CreateUguiMutedLabel(parent, "DropdownValue", "Selected: Quality: Low", 12f);
            PlaceUguiTopLeft(this.uguiPocDropdownValueLabel, 280f, 180f, 196f, 24f);
        }

        private void OnUguiPocDropdownChanged(int index)
        {
            string text = "Selected: #" + index;
            try
            {
                if (this.uguiPocDropdown != null && index >= 0 && index < this.uguiPocDropdown.options.Count)
                {
                    text = "Selected: " + this.uguiPocDropdown.options[index].text;
                }
            }
            catch { }
            this.uguiPocLastDropdownValue = index;
            ModLogger.Msg("[UguiPoc] Dropdown changed: " + text);
            this.SetUguiLabelText(this.uguiPocDropdownValueLabel, text);
        }

        // Header label + kit ScrollView + N text rows. Built twice (compact + full-tab) to prove
        // multi-instance construction stays safe.
        private void BuildUguiPocScrollList(Transform parent, float x, float y, float w, float h, int rowCount, string headerText)
        {
            GameObject header = this.CreateUguiMutedLabel(parent, "ScrollHeader", headerText, 13f);
            PlaceUguiTopLeft(header, x, y, w, 18f);

            const float RowHeight = 26f;
            Transform content;
            GameObject sv = this.CreateUguiScrollView(parent, "ScrollView", rowCount * RowHeight + 8f, out content);
            PlaceUguiTopLeft(sv, x, y + 22f, w, h - 22f);

            for (int i = 0; i < rowCount; i++)
            {
                string rowText = "Scroll row " + (i + 1).ToString("00") + " — clipped by RectMask2D, wheel + drag + scrollbar";
                GameObject rowLabel = (i % 2 == 0)
                    ? this.CreateUguiBodyLabel(content, "Row" + i, rowText, 12f)
                    : this.CreateUguiMutedLabel(content, "Row" + i, rowText, 12f);
                PlaceUguiTopLeft(rowLabel, 10f, 4f + i * RowHeight, w - 52f, RowHeight - 2f);
            }
        }

        // --- About tab: placeholder text + the button-tier demo (validates Secondary/Danger) -----
        private void BuildUguiPocAboutTab(Transform parent)
        {
            GameObject l1 = this.CreateUguiHeaderLabel(parent, "AboutTitle", "About this PoC", 15f);
            PlaceUguiTopLeft(l1, 24f, 12f, 452f, 22f);
            GameObject l2 = this.CreateUguiMutedLabel(parent, "AboutBody1", "This panel is built 100% from HeartopiaComplete.UguiKit.cs factories.", 12f);
            PlaceUguiTopLeft(l2, 24f, 42f, 452f, 18f);
            GameObject l3 = this.CreateUguiMutedLabel(parent, "AboutBody2", "Window drag + scale: kit's polled frame driver, no custom components.", 12f);
            PlaceUguiTopLeft(l3, 24f, 64f, 452f, 18f);
            GameObject l4 = this.CreateUguiMutedLabel(parent, "AboutBody3", "Tab icons: the same base64 PNGs the IMGUI sidebar uses (NavIcons).", 12f);
            PlaceUguiTopLeft(l4, 24f, 86f, 452f, 18f);

            GameObject tiersHeader = this.CreateUguiHeaderLabel(parent, "TiersHeader", "Button tiers (IMGUI parity)", 14f);
            PlaceUguiTopLeft(tiersHeader, 24f, 124f, 452f, 20f);
            GameObject primary = this.CreateUguiPrimaryButton(parent, "TierPrimary", "Primary",
                new System.Action(() => ModLogger.Msg("[UguiPoc] Primary tier clicked")));
            PlaceUguiTopLeft(primary, 24f, 150f, 140f, 30f);
            GameObject secondary = this.CreateUguiSecondaryButton(parent, "TierSecondary", "Secondary",
                new System.Action(() => ModLogger.Msg("[UguiPoc] Secondary tier clicked")));
            PlaceUguiTopLeft(secondary, 172f, 150f, 140f, 30f);
            GameObject danger = this.CreateUguiDangerButton(parent, "TierDanger", "Danger",
                new System.Action(() => ModLogger.Msg("[UguiPoc] Danger tier clicked")));
            PlaceUguiTopLeft(danger, 320f, 150f, 140f, 30f);
        }

        // --- About tab: toast auto-sizing spike (Phase 2e) ---------------------------------------
        // Proves ONE thing the kit does nowhere else yet: a UGUI element sizing itself from
        // runtime TEXT content under this build's IL2CPP interop (everything so far is
        // fixed-rect placement). Mechanism: TMP's GetPreferredValues — a single direct
        // measurement call with no extra layout-system moving parts. Deliberately NOT
        // ContentSizeFitter: its dependency on Unity's internal layout-rebuild pipeline is
        // unverified in this build and pointless risk for card+text. All four GetPreferredValues
        // overloads are compiled into this build (gameassembly-dumps/Unity.TextMeshPro/TMPro/
        // TMP_Text.cs:2927-2951, real RVAs). The measured size is applied to BOTH the label rect
        // AND the card background Image — driving a sibling element's size from the text's own
        // measurement is the part a future toast actually needs (a toast is card+text, not just
        // text). The button cycles preset strings of increasing length so the resize is visibly
        // demonstrable in-game. Scope: sizing proof ONLY — the real notification stack
        // (DrawMenuNotifications / AddMenuNotification) is untouched; porting it is separate,
        // later work.

        private static readonly string[] UguiPocToastTexts = new string[]
        {
            "Saved.",
            "Item obtained: Quality Timber x3",
            "Auto-sell finished: 24 items sold, 3 kept by the allowlist."
        };
        private const float UguiPocToastPadX = 14f;
        private const float UguiPocToastPadY = 10f;

        private void BuildUguiPocToastSpike(Transform parent)
        {
            GameObject header = this.CreateUguiHeaderLabel(parent, "ToastHeader", "Toast auto-sizing spike", 14f);
            PlaceUguiTopLeft(header, 24f, 196f, 452f, 20f);

            GameObject cycleBtn = this.CreateUguiSecondaryButton(parent, "ToastCycle", "Cycle text",
                new System.Action(this.OnUguiPocToastCycleClicked));
            PlaceUguiTopLeft(cycleBtn, 24f, 222f, 120f, 28f);

            // Measurement readout — makes the in-game verification (and any failure) explicit.
            this.uguiPocToastMeasureLabel = this.CreateUguiMutedLabel(parent, "ToastMeasure", "", 11f);
            PlaceUguiTopLeft(this.uguiPocToastMeasureLabel, 156f, 225f, 320f, 22f);

            // Card = content-role bg + secondary ring (the toast look), label as its child. Both
            // sizes come from ApplyUguiPocToastCardText; the build-time rects are throwaway. The
            // ring overlay is a stretched child, so it follows every card resize automatically.
            GameObject card = this.CreateUguiGo("ToastCard", parent);
            PlaceUguiTopLeft(card, 24f, 260f, 120f, 37f);
            this.AddUguiImage(card, this.UguiKitContentBg(), true, 1f);
            this.AddUguiRingOverlay(card, UguiKitSecondaryRing, 1f);
            this.uguiPocToastCard = card;

            this.uguiPocToastCardLabel = this.CreateUguiBodyLabel(card.transform, "Text", "", 13f);
            PlaceUguiTopLeft(this.uguiPocToastCardLabel, UguiPocToastPadX, UguiPocToastPadY, 92f, 17f);

            this.uguiPocToastTextIndex = 0;
            this.ApplyUguiPocToastCardText(UguiPocToastTexts[0]);
        }

        private void OnUguiPocToastCycleClicked()
        {
            this.uguiPocToastTextIndex = (this.uguiPocToastTextIndex + 1) % UguiPocToastTexts.Length;
            this.ApplyUguiPocToastCardText(UguiPocToastTexts[this.uguiPocToastTextIndex]);
        }

        // Sets the card text, measures it, resizes label + card. Order matters: the text is
        // assigned FIRST (flags TMP's own re-parse for the next render pass), then measured via
        // the STRING overload of GetPreferredValues — it parses the passed string directly
        // instead of trusting the component's parse-dirty bookkeeping in the same frame the text
        // changed. Legacy-Text fallback measures via preferredWidth/Height (only reachable if
        // the kit's TMP label creation fell back at build time). Build-time caveat: the first
        // Apply runs while the window root is still inactive (the TMP component has never
        // Awoken) — if that measurement ever misbehaves it fails visibly into the readout label,
        // and the next Cycle click (component alive by then) re-measures; nothing latches broken.
        private void ApplyUguiPocToastCardText(string text)
        {
            try
            {
                if (this.uguiPocToastCard == null || this.uguiPocToastCardLabel == null)
                {
                    return;
                }

                this.SetUguiLabelText(this.uguiPocToastCardLabel, text);

                float textW = 0f;
                float textH = 0f;
                string how = "none";
                TextMeshProUGUI tmp = this.uguiPocToastCardLabel.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    Vector2 pref = tmp.GetPreferredValues(text);
                    textW = pref.x;
                    textH = pref.y;
                    how = "TMP";
                }
                else
                {
                    Text legacy = this.uguiPocToastCardLabel.GetComponent<Text>();
                    if (legacy != null)
                    {
                        textW = legacy.preferredWidth;
                        textH = legacy.preferredHeight;
                        how = "legacy";
                    }
                }

                // Sanity gate: reject non-finite, non-positive, or absurd values (a TMP measure
                // that leaked its internal 32767 "large margin" sentinel would otherwise become a
                // 32k-unit Image). On rejection the card keeps its previous size — never garbage.
                bool sane = textW > 0f && textH > 0f && textW < 2000f && textH < 500f
                    && !float.IsNaN(textW) && !float.IsNaN(textH)
                    && !float.IsInfinity(textW) && !float.IsInfinity(textH);
                if (!sane)
                {
                    string diag = "measure REJECTED (" + how + "): " + textW.ToString("0.##") + " x " + textH.ToString("0.##");
                    this.SetUguiLabelText(this.uguiPocToastMeasureLabel, diag);
                    ModLogger.Msg("[UguiPoc] toast " + diag);
                    return;
                }

                float labelW = Mathf.Ceil(textW);
                float labelH = Mathf.Ceil(textH);
                float cardW = labelW + UguiPocToastPadX * 2f;
                float cardH = labelH + UguiPocToastPadY * 2f;
                RectTransform labelRt = this.uguiPocToastCardLabel.GetComponent<RectTransform>();
                if (labelRt != null)
                {
                    labelRt.sizeDelta = new Vector2(labelW, labelH); // anchors/pivot fixed at build
                }
                RectTransform cardRt = this.uguiPocToastCard.GetComponent<RectTransform>();
                if (cardRt != null)
                {
                    cardRt.sizeDelta = new Vector2(cardW, cardH); // pivot (0,1): grows right/down
                }

                this.SetUguiLabelText(this.uguiPocToastMeasureLabel,
                    how + " " + textW.ToString("0.#") + " x " + textH.ToString("0.#")
                    + "  ->  card " + cardW.ToString("0") + " x " + cardH.ToString("0"));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiPoc] toast card apply failed: " + ex.Message);
                try { this.SetUguiLabelText(this.uguiPocToastMeasureLabel, "apply FAILED: " + ex.Message); } catch { }
            }
        }
    }
}
