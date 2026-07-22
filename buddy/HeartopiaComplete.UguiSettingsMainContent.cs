using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, round 2 (migration plan: cosmic-waddling-rainbow.md):
    // Settings→Logging and Settings→Main, combined into one round. Split out of
    // HeartopiaComplete.UguiShellTabIndices.cs (which keeps round 1's About/Research plus the
    // display-position constants) so no single content file balloons as the remaining tabs land.
    //
    // Ground rules (same as round 1):
    //  - The IMGUI drawers (DrawLoggingTab, DrawSettingsMainTab, DrawLodSettingsInPerformancePanel)
    //    stay fully functional and untouched — this file only READS the same data fields and CALLS
    //    the same action methods. Two independent rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellSettingsTabIndex +
    //    UguiShellSettingsMainSubIndex/UguiShellSettingsLoggingSubIndex — declared next to their
    //    round-1 siblings in UguiShellTabIndices.cs), never by localized label comparison.
    //  - Both tabs live inside the already-registered modal shell: no input-ownership entries, no
    //    theme registration of their own (the shell's "UguiShell" rebuilder re-runs these builders).
    //
    // Cross-surface sync (both tabs): every backing field here is ALSO editable from the still-live
    // IMGUI twin, so per-frame processors — gated on "shell visible AND Settings tab active AND
    // this exact sub-tab active" (the ProcessUguiShellResearchContentOnUpdate gating shape) —
    // re-sync control state from the live fields. Toggles use Toggle.SetIsOnWithoutNotify (NEVER
    // the isOn setter: that fires onValueChanged and writes the value straight back — idempotent
    // today, a footgun precedent tomorrow); sliders/dropdowns use their SetValueWithoutNotify
    // twins for the same reason. Cheap bool compares run every gated frame; costlier text refreshes
    // (LOD status line reads QualitySettings, custom-ID readout) tick at 0.5s.
    //
    // Deliberate deviations from the IMGUI drawers (do not "fix" without a plan decision):
    //  - Toggles render as kit CHECKBOXES, not IMGUI-style sliding switches: CreateUguiSwitch's
    //    pill/handle visuals are driven from its own onValueChanged closure, so a silent
    //    SetIsOnWithoutNotify re-sync would leave them stale (and it fires onChanged once at build,
    //    which would replay SaveKeybinds/notification side effects on every theme rebuild). The
    //    checkbox's visuals are Toggle.graphic-driven and follow WithoutNotify updates for free.
    //  - Both Main dropdowns use the kit's real CreateUguiDropdown instead of reproducing IMGUI's
    //    hand-rolled accent-highlighted popup rows — accepted visual deviation for a proven
    //    primitive. If UnityEvent<int> wiring reports failure (listenerWired=false — the known
    //    2020.3 no-full-generic-sharing risk) the per-frame processor polls dd.value, exactly the
    //    uguiPocDropdownPollFallback precedent.
    //  - Custom ID's free-text VALUE is read-only here (current value + "(edit via the in-game
    //    menu for now)"): legacy InputField is unproven in this build and is its own deferred task.
    //  - Both tabs scroll (CreateUguiScrollView): IMGUI's tab content scrolls as a whole via
    //    tabScrollPos; the shell's per-tab containers don't, and neither 39 toggle rows nor the
    //    four Main panels fit 520px. Main relayouts (reposition, never rebuild) when a conditional
    //    section appears/disappears — the UGUI analog of the IMGUI drawers' y-cursor accumulation.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Shared gate: is the shell showing a specific Settings sub-tab right now?
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellSettingsSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellSettingsTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellSettingsTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellSettingsTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        private static void SetUguiGoActive(GameObject go, bool active)
        {
            if (go == null)
            {
                return;
            }
            try
            {
                if (go.activeSelf != active)
                {
                    go.SetActive(active);
                }
            }
            catch { }
        }

        // Kit buttons bake a 13pt label; the LOD segmented row (4 buttons in ~98px each) needs the
        // sub-tab-bar size to fit "Game default"/"Performance".
        private void TrySetUguiButtonLabelSize(GameObject buttonGo, float size)
        {
            try
            {
                Transform t = (buttonGo != null) ? buttonGo.transform.Find("Label") : null;
                if (t == null)
                {
                    return;
                }
                TextMeshProUGUI tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.fontSize = size;
                    return;
                }
                Text txt = t.GetComponent<Text>();
                if (txt != null)
                {
                    txt.fontSize = (int)size;
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Settings → Logging (39 MasterLog* toggles, session-only — see HeartopiaComplete.Logging.cs)
        // ----------------------------------------------------------------------------------------

        // One binding per MasterLog* flag — get/set lambdas instead of 39 hand-written toggle
        // blocks, so a field/label mismatch cannot hide in copy-paste. Pairs and ORDER are copied
        // exactly from DrawLoggingTab (HeartopiaComplete.Logging.cs:58-96).
        private struct UguiLoggingToggleBinding
        {
            public Func<bool> Get;
            public Action<bool> Set;
            public string Label;

            public UguiLoggingToggleBinding(Func<bool> get, Action<bool> set, string label)
            {
                this.Get = get;
                this.Set = set;
                this.Label = label;
            }
        }

        private static UguiLoggingToggleBinding[] BuildUguiLoggingToggleBindings()
        {
            return new UguiLoggingToggleBinding[]
            {
                new UguiLoggingToggleBinding(() => MasterLogAuraFarm, v => MasterLogAuraFarm = v, "Aura Farm"),
                new UguiLoggingToggleBinding(() => MasterLogBirdFarm, v => MasterLogBirdFarm = v, "Bird Farm"),
                new UguiLoggingToggleBinding(() => MasterLogBirdFarmCrashTrace, v => MasterLogBirdFarmCrashTrace = v, "Bird Farm Crash Trace"),
                new UguiLoggingToggleBinding(() => MasterLogInsectFarm, v => MasterLogInsectFarm = v, "Insect Farm"),
                new UguiLoggingToggleBinding(() => MasterLogAutoFish, v => MasterLogAutoFish = v, "Auto Fish"),
                new UguiLoggingToggleBinding(() => MasterLogInstantCatch, v => MasterLogInstantCatch = v, "Instant Catch"),
                new UguiLoggingToggleBinding(() => MasterLogAutoFarm, v => MasterLogAutoFarm = v, "Auto Farm"),
                new UguiLoggingToggleBinding(() => MasterLogQuestAssistant, v => MasterLogQuestAssistant = v, "Quest Assistant"),
                new UguiLoggingToggleBinding(() => MasterLogAutoEatRepair, v => MasterLogAutoEatRepair = v, "Auto Eat/Repair"),
                new UguiLoggingToggleBinding(() => MasterLogNpcTeleport, v => MasterLogNpcTeleport = v, "NPC Teleport"),
                new UguiLoggingToggleBinding(() => MasterLogNetCook, v => MasterLogNetCook = v, "NetCook"),
                new UguiLoggingToggleBinding(() => MasterLogNetCookScan, v => MasterLogNetCookScan = v, "NetCook Scan"),
                new UguiLoggingToggleBinding(() => MasterLogPuzzle, v => MasterLogPuzzle = v, "Puzzle"),
                new UguiLoggingToggleBinding(() => MasterLogAutoSell, v => MasterLogAutoSell = v, "Auto Sell"),
                new UguiLoggingToggleBinding(() => MasterLogRadarIconEsp, v => MasterLogRadarIconEsp = v, "Radar Icon ESP"),
                new UguiLoggingToggleBinding(() => MasterLogBubbleRadar, v => MasterLogBubbleRadar = v, "Bubble Radar"),
                new UguiLoggingToggleBinding(() => MasterLogAutoBuy, v => MasterLogAutoBuy = v, "Auto Buy"),
                new UguiLoggingToggleBinding(() => MasterLogForceOpenShop, v => MasterLogForceOpenShop = v, "Force Open Shop"),
                new UguiLoggingToggleBinding(() => MasterLogPetPlay, v => MasterLogPetPlay = v, "Pet Play"),
                new UguiLoggingToggleBinding(() => MasterLogPetFeed, v => MasterLogPetFeed = v, "Pet Feed"),
                new UguiLoggingToggleBinding(() => MasterLogWildAnimalFeed, v => MasterLogWildAnimalFeed = v, "Wild Animal Feed"),
                new UguiLoggingToggleBinding(() => MasterLogHomelandFarm, v => MasterLogHomelandFarm = v, "Homeland Farm"),
                new UguiLoggingToggleBinding(() => MasterLogPadBuild, v => MasterLogPadBuild = v, "Pad Build"),
                new UguiLoggingToggleBinding(() => MasterLogWildAnimalGift, v => MasterLogWildAnimalGift = v, "Wild Animal Gift"),
                new UguiLoggingToggleBinding(() => MasterLogAutoIceSkating, v => MasterLogAutoIceSkating = v, "Auto Ice Skating"),
                new UguiLoggingToggleBinding(() => MasterLogDailyQuestSubmit, v => MasterLogDailyQuestSubmit = v, "Daily Quest Submit"),
                new UguiLoggingToggleBinding(() => MasterLogDailyClaims, v => MasterLogDailyClaims = v, "Daily Claims"),
                new UguiLoggingToggleBinding(() => MasterLogBirdPhotoSubmit, v => MasterLogBirdPhotoSubmit = v, "Bird Photo Submit"),
                new UguiLoggingToggleBinding(() => MasterLogStrangerChat, v => MasterLogStrangerChat = v, "Stranger Chat"),
                new UguiLoggingToggleBinding(() => MasterLogGameEvents, v => MasterLogGameEvents = v, "Game Events"),
                new UguiLoggingToggleBinding(() => MasterLogEntityEvents, v => MasterLogEntityEvents = v, "Entity Events"),
                new UguiLoggingToggleBinding(() => MasterLogGameIcons, v => MasterLogGameIcons = v, "Game Icons"),
                new UguiLoggingToggleBinding(() => MasterLogPersistentHud, v => MasterLogPersistentHud = v, "Persistent HUD"),
                new UguiLoggingToggleBinding(() => MasterLogSandSculpture, v => MasterLogSandSculpture = v, "Sand Sculpture"),
                new UguiLoggingToggleBinding(() => MasterLogShowOffBypass, v => MasterLogShowOffBypass = v, "Show-Off Bypass"),
                new UguiLoggingToggleBinding(() => MasterLogSnowSculpture, v => MasterLogSnowSculpture = v, "Snow Sculpture"),
                new UguiLoggingToggleBinding(() => MasterLogSeaCleanQte, v => MasterLogSeaCleanQte = v, "Sea Clean QTE"),
                new UguiLoggingToggleBinding(() => MasterLogCorruptionCleanse, v => MasterLogCorruptionCleanse = v, "Corruption Cleanse"),
                new UguiLoggingToggleBinding(() => MasterLogUnderwaterRadar, v => MasterLogUnderwaterRadar = v, "Underwater Radar")
            };
        }

        private sealed class UguiShellSettingsLoggingHandle
        {
            public GameObject Root;
            public UguiLoggingToggleBinding[] Bindings;
            public readonly List<Toggle> Toggles = new List<Toggle>();
            public int ErrorCount; // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellSettingsLoggingHandle uguiShellSettingsLogging;

        // UGUI mirror of DrawLoggingTab: localized title, verbatim (unlocalized — IMGUI parity)
        // intro line, then the 39 toggle rows inside a scroll view. Assigns the handle field LAST
        // (Research idiom) so a mid-build exception can never leave a half-built handle syncing.
        private GameObject BuildUguiShellSettingsLoggingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSettingsLogging = null;

            UguiShellSettingsLoggingHandle handle = new UguiShellSettingsLoggingHandle();
            GameObject block = this.CreateUguiGo("SettingsLoggingContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;

            GameObject title = this.CreateUguiHeaderLabel(block.transform, "Title", this.L("Logging"), 18f);
            PlaceUguiTopLeft(title, pad, 12f, w - pad * 2f, 26f);

            GameObject intro = this.CreateUguiMutedLabel(block.transform, "Intro",
                "Extended Logging — session only, not saved. Toggles the verbose debug logs each feature writes to the mod log.",
                12f);
            this.TrySetUguiLabelWrapped(intro);
            PlaceUguiTopLeft(intro, pad, 42f, w - pad * 2f, 36f);

            handle.Bindings = BuildUguiLoggingToggleBindings();
            if (handle.Bindings.Length != LoggingTabRowCount)
            {
                // The IMGUI drawer's row-count constant is the shared contract; a mismatch means
                // one surface gained/lost a flag the other doesn't show.
                ModLogger.Msg("[UguiShell] Logging bindings (" + handle.Bindings.Length
                    + ") != IMGUI LoggingTabRowCount (" + LoggingTabRowCount + ") — surfaces out of sync");
            }

            const float rowStep = 28f;
            const float scrollTop = 84f;
            Transform rowsContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Rows",
                handle.Bindings.Length * rowStep + 16f, out rowsContent);
            PlaceUguiTopLeft(scroll, 8f, scrollTop, w - 16f, h - scrollTop - 8f);
            // Flat look over the block's ContentBg (LIVE rail idiom) — alpha-0 images still raycast,
            // so wheel/drag scrolling keeps working.
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (rowsContent != null && rowsContent.parent != null)
                {
                    Image viewportBg = rowsContent.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch { }

            // Scroll view width minus viewport insets (4 left + 18 right) = row area width.
            float rowW = w - 16f - 22f;
            for (int i = 0; i < handle.Bindings.Length; i++)
            {
                UguiLoggingToggleBinding binding = handle.Bindings[i];
                // DrawSwitchToggle localizes its label, so the UGUI twin does too.
                Toggle tog = this.CreateUguiCheckbox(rowsContent, "Log" + i,
                    this.L(binding.Label), binding.Get(), binding.Set);
                PlaceUguiTopLeft(tog.gameObject, 8f, 8f + i * rowStep, rowW - 16f, 24f);
                handle.Toggles.Add(tog);
            }

            handle.Root = block;
            this.uguiShellSettingsLogging = handle;
            return block;
        }

        // Called every frame from ProcessUguiShellOnUpdate. 39 bool compares while the Logging
        // sub-tab is the visible one — no throttle needed; skips in a few comparisons otherwise.
        private void ProcessUguiShellSettingsLoggingOnUpdate()
        {
            UguiShellSettingsLoggingHandle handle = this.uguiShellSettingsLogging;
            if (handle == null || handle.Root == null || handle.Bindings == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsLoggingSubIndex))
            {
                return;
            }

            try
            {
                for (int i = 0; i < handle.Toggles.Count && i < handle.Bindings.Length; i++)
                {
                    Toggle tog = handle.Toggles[i];
                    if (tog == null)
                    {
                        continue;
                    }
                    bool live = handle.Bindings[i].Get();
                    if (tog.isOn != live)
                    {
                        tog.SetIsOnWithoutNotify(live); // NEVER isOn = — that fires onValueChanged
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Logging content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Settings → Main (GENERAL / BEHAVIOR / PERFORMANCE / QUICK ACTION)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellSettingsMainHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;              // scroll content width (block w minus viewport insets)
            public GameObject SettingsHeader;

            // GENERAL
            public GameObject GeneralPanel;
            public GameObject LangLabel;
            public Dropdown LangDropdown;
            public bool LangListenerWired;
            public int LangLastValue;               // poll-fallback change detection
            public string[] LangCodes;              // codes snapshot matching the dropdown options
            public Toggle NotifToggle;
            public GameObject NotifPosLabel;        // only visible while notifications are enabled
            public Dropdown NotifPosDropdown;
            public bool NotifPosListenerWired;
            public int NotifPosLastValue;

            // BEHAVIOR
            public GameObject BehaviorPanel;
            public Toggle AutoStartToggle;
            public Toggle AutoCloseToggle;
            public Toggle HideIdToggle;
            public Toggle CustomIdToggle;
            public GameObject CustomIdValueLabel;   // "Value" caption
            public InputField CustomIdField;
            public string CustomIdShownValue;
            public Toggle ShowOverlayToggle;
            public Toggle BlockInputToggle;

            // PERFORMANCE
            public GameObject PerformancePanel;
            public Toggle FpsToggle;
            public GameObject FpsTargetLabel;       // only visible while FPS Bypass is on
            public Slider FpsSlider;
            public GameObject LodTitleLabel;
            public readonly List<GameObject> LodModeButtons = new List<GameObject>();
            public readonly List<Image> LodModeButtonBgs = new List<Image>();
            public readonly List<GameObject> LodModeButtonLabels = new List<GameObject>();
            public int LodHighlightedMode = -1;
            public GameObject LodStatusLabel;
            public GameObject LodBiasLabel;         // Custom mode only
            public Slider LodBiasSlider;
            public GameObject LodMaxLabel;
            public Slider LodMaxSlider;

            // QUICK ACTION
            public GameObject ActionPanel;
            public GameObject JoinTownButton;

            public int LayoutSignature = -1;        // packed conditional-visibility state
            public float NextSlowSyncAt;            // 0.5s tick for label/value re-syncs
            public int ErrorCount;                  // per-frame sync disabled at 3
        }

        private UguiShellSettingsMainHandle uguiShellSettingsMain;

        private static int IndexOfLanguageCode(string[] codes, string code)
        {
            if (codes == null)
            {
                return -1;
            }
            for (int i = 0; i < codes.Length; i++)
            {
                if (string.Equals(codes[i], code, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        // IMGUI panel chrome: DrawExentriSectionPanel(rect, accent, panelFill, panelLine) with
        // panelFill = uiContent @ clamp(panelAlpha*0.82) and panelLine = accent @ 0.24. Mirrored
        // as a sliced fill + ring overlay (the LIVE rail's construction for the same drawer).
        private GameObject CreateUguiSettingsMainPanel(Transform parent, string name, string headerText)
        {
            GameObject panel = this.CreateUguiGo(name, parent);
            Color accent = this.UguiKitAccent();
            Color fill = new Color(this.uiContentR, this.uiContentG, this.uiContentB,
                Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
            this.AddUguiImage(panel, fill, true, 1f);
            this.AddUguiRingOverlay(panel, new Color(accent.r, accent.g, accent.b, 0.24f), 1f);

            // Sub-header: IMGUI subHeaderStyle = bold 11 in the header color (panel-local, fixed —
            // never needs relayout).
            GameObject header = this.CreateUguiHeaderLabel(panel.transform, "Header", headerText, 11f);
            PlaceUguiTopLeft(header, 14f, 12f, 400f, 18f);
            return panel;
        }

        // UGUI mirror of DrawSettingsMainTab (HeartopiaComplete.Config.cs:1100) + the LOD section
        // (LodSettingsFeature.cs:196). Every control — including conditionally-visible ones — is
        // built ONCE here; RelayoutUguiShellSettingsMain owns positions/visibility/heights and
        // runs again whenever the conditional state changes. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellSettingsMainContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSettingsMain = null;

            UguiShellSettingsMainHandle handle = new UguiShellSettingsMainHandle();
            GameObject block = this.CreateUguiGo("SettingsMainContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
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

            // IMGUI headerStyle is bold 15 — header-label role carries it here.
            handle.SettingsHeader = this.CreateUguiHeaderLabel(scrollContent, "SettingsHeader", this.L("SETTINGS"), 15f);

            // ---------------- GENERAL ----------------
            GameObject general = this.CreateUguiSettingsMainPanel(scrollContent, "GeneralPanel", this.L("GENERAL"));
            handle.GeneralPanel = general;

            handle.LangLabel = this.CreateUguiBodyLabel(general.transform, "LangLabel", this.L("Localization"), 13f);

            string[] languageCodes = LocalizationManager.GetAvailableLanguageCodes();
            string[] languageNames = new string[languageCodes.Length];
            for (int i = 0; i < languageCodes.Length; i++)
            {
                languageNames[i] = LocalizationManager.GetLanguageDisplayName(languageCodes[i]);
            }
            int langInitial = IndexOfLanguageCode(languageCodes, this.selectedLanguage);
            if (langInitial < 0)
            {
                langInitial = 0;
            }
            handle.LangCodes = languageCodes;
            handle.LangLastValue = langInitial;
            bool langWired;
            handle.LangDropdown = this.CreateUguiDropdown(general.transform, "LangDropdown",
                languageNames, langInitial,
                new System.Action<int>(this.OnUguiSettingsMainLanguagePicked), out langWired);
            handle.LangListenerWired = langWired;

            handle.NotifToggle = this.CreateUguiCheckbox(general.transform, "NotifToggle",
                this.L("Enable Notifications"), this.notificationsEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainNotificationsChanged));

            handle.NotifPosLabel = this.CreateUguiBodyLabel(general.transform, "NotifPosLabel", this.L("Position"), 13f);
            string[] positionNames = new string[NotificationPositionOptions.Length];
            for (int i = 0; i < NotificationPositionOptions.Length; i++)
            {
                positionNames[i] = this.L(NotificationPositionOptions[i]);
            }
            int posInitial = Mathf.Clamp(this.notificationPosition, 0, NotificationPositionOptions.Length - 1);
            handle.NotifPosLastValue = posInitial;
            bool posWired;
            handle.NotifPosDropdown = this.CreateUguiDropdown(general.transform, "NotifPosDropdown",
                positionNames, posInitial,
                new System.Action<int>(this.OnUguiSettingsMainNotifPositionPicked), out posWired);
            handle.NotifPosListenerWired = posWired;

            // ---------------- BEHAVIOR ----------------
            GameObject behavior = this.CreateUguiSettingsMainPanel(scrollContent, "BehaviorPanel", this.L("BEHAVIOR"));
            handle.BehaviorPanel = behavior;

            handle.AutoStartToggle = this.CreateUguiCheckbox(behavior.transform, "AutoStartToggle",
                this.L("Auto Start on Lobby"), this.autoClickStartEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainAutoStartChanged));
            handle.AutoCloseToggle = this.CreateUguiCheckbox(behavior.transform, "AutoCloseToggle",
                this.L("Auto Close Announcements"), this.autoCloseAnnouncementEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainAutoCloseChanged));
            handle.HideIdToggle = this.CreateUguiCheckbox(behavior.transform, "HideIdToggle",
                this.L("Hide ID"), this.hideIdEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainHideIdChanged));
            handle.CustomIdToggle = this.CreateUguiCheckbox(behavior.transform, "CustomIdToggle",
                this.L("Custom ID"), this.customDisplayIdEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainCustomIdChanged));

            // Custom ID VALUE — now a real editable field (2026-07-22). It was a read-only readout
            // plus an "(edit via the in-game menu for now)" note because InputField was unproven on
            // this build at the time; that has since been settled (Teleport's XYZ entry and Auto
            // Sell's match key both ship one), so the placeholder is gone. Mirrors the source's
            // GUI.TextField (Config.cs:1352-1368): 24-char cap and NormalizeCustomId on every edit.
            handle.CustomIdShownValue = this.customDisplayId ?? string.Empty;
            handle.CustomIdValueLabel = this.CreateUguiBodyLabel(behavior.transform, "CustomIdValueLabel",
                this.L("Value"), 12f);
            handle.CustomIdField = this.CreateUguiInputField(behavior.transform, "CustomIdField",
                handle.CustomIdShownValue, 24,
                new System.Action<string>(this.OnUguiSettingsMainCustomIdValueEdited));

            handle.ShowOverlayToggle = this.CreateUguiCheckbox(behavior.transform, "ShowOverlayToggle",
                this.L("Show Status Overlay"), this.showStatusOverlay,
                new System.Action<bool>(this.OnUguiSettingsMainShowOverlayChanged));
            handle.BlockInputToggle = this.CreateUguiCheckbox(behavior.transform, "BlockInputToggle",
                this.L("Block Input"), this.blockGameUiWhenMenuOpen,
                new System.Action<bool>(this.OnUguiSettingsMainBlockInputChanged));

            // ---------------- PERFORMANCE ----------------
            GameObject performance = this.CreateUguiSettingsMainPanel(scrollContent, "PerformancePanel", this.L("PERFORMANCE"));
            handle.PerformancePanel = performance;

            handle.FpsToggle = this.CreateUguiCheckbox(performance.transform, "FpsToggle",
                this.L("FPS Bypass"), this.fpsBypassEnabled,
                new System.Action<bool>(this.OnUguiSettingsMainFpsBypassChanged));

            handle.FpsTargetLabel = this.CreateUguiBodyLabel(performance.transform, "FpsTargetLabel",
                this.LF("Target Max FPS: {0}", this.fpsBypassTarget), 13f);
            handle.FpsSlider = this.CreateUguiSlider(performance.transform, "FpsSlider",
                30f, 360f, this.fpsBypassTarget, true,
                new System.Action<float>(this.OnUguiSettingsMainFpsTargetChanged));

            handle.LodTitleLabel = this.CreateUguiBodyLabel(performance.transform, "LodTitle", this.L("LOD Override"), 13f);
            for (int i = 0; i < LodOverrideModeLabels.Length; i++)
            {
                int modeCopy = i; // capture a copy for the click closure
                GameObject btn = this.CreateUguiSecondaryButton(performance.transform, "LodMode" + i,
                    this.L(LodOverrideModeLabels[i]),
                    new System.Action(() => this.SetLodOverrideMode(modeCopy)));
                this.TrySetUguiButtonLabelSize(btn, 11.5f);
                handle.LodModeButtons.Add(btn);
                handle.LodModeButtonBgs.Add(btn.GetComponent<Image>());
                Transform lbl = btn.transform.Find("Label");
                handle.LodModeButtonLabels.Add(lbl != null ? lbl.gameObject : null);
            }

            handle.LodStatusLabel = this.CreateUguiMutedLabel(performance.transform, "LodStatus",
                this.BuildUguiSettingsMainLodStatusText(), 12f);

            handle.LodBiasLabel = this.CreateUguiBodyLabel(performance.transform, "LodBiasLabel",
                this.LF("LOD bias: {0:0.##}", this.lodCustomBias), 13f);
            handle.LodBiasSlider = this.CreateUguiSlider(performance.transform, "LodBiasSlider",
                0.25f, 4f, this.lodCustomBias, false,
                new System.Action<float>(this.OnUguiSettingsMainLodBiasChanged));
            handle.LodMaxLabel = this.CreateUguiBodyLabel(performance.transform, "LodMaxLabel",
                this.LF("Max LOD level: {0}", this.lodCustomMaxLevel), 13f);
            handle.LodMaxSlider = this.CreateUguiSlider(performance.transform, "LodMaxSlider",
                0f, 4f, this.lodCustomMaxLevel, true,
                new System.Action<float>(this.OnUguiSettingsMainLodMaxLevelChanged));

            // ---------------- QUICK ACTION ----------------
            GameObject action = this.CreateUguiSettingsMainPanel(scrollContent, "ActionPanel", this.L("QUICK ACTION"));
            handle.ActionPanel = action;
            handle.JoinTownButton = this.CreateUguiPrimaryButton(action.transform, "JoinTownButton",
                this.L("Join My Town"),
                new System.Action(() => this.StartLobbyAutoJoinMyTown("Manual button")));

            handle.LayoutSignature = this.ComputeUguiSettingsMainLayoutSignature();
            this.RelayoutUguiShellSettingsMain(handle);
            this.ApplyUguiSettingsMainLodHighlight(handle);

            handle.Root = block;
            this.uguiShellSettingsMain = handle;
            return block;
        }

        private int ComputeUguiSettingsMainLayoutSignature()
        {
            return (this.notificationsEnabled ? 1 : 0)
                 | (this.customDisplayIdEnabled ? 2 : 0)
                 | (this.fpsBypassEnabled ? 4 : 0)
                 | ((this.lodOverrideMode == LodOverrideModeCustom) ? 8 : 0);
        }

        private string BuildUguiSettingsMainLodStatusText()
        {
            // DrawLodSettingsInPerformancePanel:216-218 verbatim.
            return this.lodOverrideMode == LodOverrideModeDefault
                ? this.LF("Game LOD: bias {0:0.##}, max level {1}", QualitySettings.lodBias, QualitySettings.maximumLODLevel)
                : this.LF("Override active: bias {0:0.##}, max level {1}", QualitySettings.lodBias, QualitySettings.maximumLODLevel);
        }

        // Positions every Main control from the CURRENT conditional state — the UGUI analog of the
        // IMGUI drawer's y-cursor accumulation. Reposition/SetActive only; nothing is rebuilt.
        private void RelayoutUguiShellSettingsMain(UguiShellSettingsMainHandle handle)
        {
            bool notifOn = this.notificationsEnabled;
            bool customId = this.customDisplayIdEnabled;
            bool fpsOn = this.fpsBypassEnabled;
            bool lodCustom = this.lodOverrideMode == LodOverrideModeCustom;

            const float pad = 12f;
            float panelW = handle.ContentWidth - pad * 2f;
            float innerW = panelW - 32f;
            const float dropW = 220f;
            float y = 10f;

            if (handle.SettingsHeader != null)
            {
                PlaceUguiTopLeft(handle.SettingsHeader, pad + 4f, y, panelW - 8f, 22f);
            }
            y += 30f;

            // GENERAL — header occupies 12..30 (panel-local), rows start at 40.
            float gy = 40f;
            if (handle.LangLabel != null)
            {
                PlaceUguiTopLeft(handle.LangLabel, 16f, gy + 4f, 170f, 22f);
            }
            if (handle.LangDropdown != null)
            {
                PlaceUguiTopLeft(handle.LangDropdown.gameObject, panelW - 16f - dropW, gy, dropW, 30f);
            }
            gy += 38f;
            if (handle.NotifToggle != null)
            {
                PlaceUguiTopLeft(handle.NotifToggle.gameObject, 16f, gy, innerW, 24f);
            }
            gy += 32f;
            SetUguiGoActive(handle.NotifPosLabel, notifOn);
            SetUguiGoActive(handle.NotifPosDropdown != null ? handle.NotifPosDropdown.gameObject : null, notifOn);
            if (notifOn)
            {
                if (handle.NotifPosLabel != null)
                {
                    PlaceUguiTopLeft(handle.NotifPosLabel, 16f, gy + 4f, 170f, 22f);
                }
                if (handle.NotifPosDropdown != null)
                {
                    PlaceUguiTopLeft(handle.NotifPosDropdown.gameObject, panelW - 16f - dropW, gy, dropW, 30f);
                }
                gy += 38f;
            }
            float generalH = gy + 4f;
            if (handle.GeneralPanel != null)
            {
                PlaceUguiTopLeft(handle.GeneralPanel, pad, y, panelW, generalH);
            }
            y += generalH + 14f;

            // BEHAVIOR
            float by = 40f;
            if (handle.AutoStartToggle != null) { PlaceUguiTopLeft(handle.AutoStartToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            if (handle.AutoCloseToggle != null) { PlaceUguiTopLeft(handle.AutoCloseToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            if (handle.HideIdToggle != null) { PlaceUguiTopLeft(handle.HideIdToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            if (handle.CustomIdToggle != null) { PlaceUguiTopLeft(handle.CustomIdToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            SetUguiGoActive(handle.CustomIdValueLabel, customId);
            SetUguiGoActive(handle.CustomIdField != null ? handle.CustomIdField.gameObject : null, customId);
            if (customId)
            {
                if (handle.CustomIdValueLabel != null)
                {
                    PlaceUguiTopLeft(handle.CustomIdValueLabel, 28f, by + 3f, 60f, 22f);
                }
                if (handle.CustomIdField != null)
                {
                    // 92..panelW-16 — the field's right edge lines up with every toggle row above
                    // it (those span 16..panelW-16, i.e. x=16 width=innerW).
                    PlaceUguiTopLeft(handle.CustomIdField.gameObject, 92f, by, innerW - 76f, 26f);
                }
                by += 34f;
            }
            if (handle.ShowOverlayToggle != null) { PlaceUguiTopLeft(handle.ShowOverlayToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            if (handle.BlockInputToggle != null) { PlaceUguiTopLeft(handle.BlockInputToggle.gameObject, 16f, by, innerW, 24f); }
            by += 30f;
            float behaviorH = by + 4f;
            if (handle.BehaviorPanel != null)
            {
                PlaceUguiTopLeft(handle.BehaviorPanel, pad, y, panelW, behaviorH);
            }
            y += behaviorH + 14f;

            // PERFORMANCE
            float py = 40f;
            if (handle.FpsToggle != null) { PlaceUguiTopLeft(handle.FpsToggle.gameObject, 16f, py, innerW, 24f); }
            py += 30f;
            SetUguiGoActive(handle.FpsTargetLabel, fpsOn);
            SetUguiGoActive(handle.FpsSlider != null ? handle.FpsSlider.gameObject : null, fpsOn);
            if (fpsOn)
            {
                if (handle.FpsTargetLabel != null)
                {
                    PlaceUguiTopLeft(handle.FpsTargetLabel, 16f, py, 180f, 20f);
                }
                if (handle.FpsSlider != null)
                {
                    PlaceUguiTopLeft(handle.FpsSlider.gameObject, 200f, py, panelW - 200f - 24f, 20f);
                }
                py += 30f;
            }
            if (handle.LodTitleLabel != null) { PlaceUguiTopLeft(handle.LodTitleLabel, 16f, py, innerW, 20f); }
            py += 28f;
            float lodButtonW = (panelW - 32f - (LodOverrideModeLabels.Length - 1) * 4f) / LodOverrideModeLabels.Length;
            for (int i = 0; i < handle.LodModeButtons.Count; i++)
            {
                if (handle.LodModeButtons[i] != null)
                {
                    PlaceUguiTopLeft(handle.LodModeButtons[i], 16f + i * (lodButtonW + 4f), py, lodButtonW, 26f);
                }
            }
            py += 36f;
            if (handle.LodStatusLabel != null) { PlaceUguiTopLeft(handle.LodStatusLabel, 16f, py, innerW, 20f); }
            py += 28f;
            SetUguiGoActive(handle.LodBiasLabel, lodCustom);
            SetUguiGoActive(handle.LodBiasSlider != null ? handle.LodBiasSlider.gameObject : null, lodCustom);
            SetUguiGoActive(handle.LodMaxLabel, lodCustom);
            SetUguiGoActive(handle.LodMaxSlider != null ? handle.LodMaxSlider.gameObject : null, lodCustom);
            if (lodCustom)
            {
                if (handle.LodBiasLabel != null)
                {
                    PlaceUguiTopLeft(handle.LodBiasLabel, 16f, py, 180f, 20f);
                }
                if (handle.LodBiasSlider != null)
                {
                    PlaceUguiTopLeft(handle.LodBiasSlider.gameObject, 200f, py, panelW - 200f - 24f, 20f);
                }
                py += 30f;
                if (handle.LodMaxLabel != null)
                {
                    PlaceUguiTopLeft(handle.LodMaxLabel, 16f, py, 180f, 20f);
                }
                if (handle.LodMaxSlider != null)
                {
                    PlaceUguiTopLeft(handle.LodMaxSlider.gameObject, 200f, py, panelW - 200f - 24f, 20f);
                }
                py += 30f;
            }
            float performanceH = py + 4f;
            if (handle.PerformancePanel != null)
            {
                PlaceUguiTopLeft(handle.PerformancePanel, pad, y, panelW, performanceH);
            }
            y += performanceH + 14f;

            // QUICK ACTION
            if (handle.JoinTownButton != null)
            {
                PlaceUguiTopLeft(handle.JoinTownButton, 16f, 40f, panelW - 32f, 32f);
            }
            const float actionH = 84f;
            if (handle.ActionPanel != null)
            {
                PlaceUguiTopLeft(handle.ActionPanel, pad, y, panelW, actionH);
            }
            y += actionH + 14f;

            this.SetUguiScrollContentHeight(handle.ScrollContent, y + 6f);
        }

        // Active LOD segment = accent fill + on-accent text (the kit tab-bar active convention —
        // stands in for IMGUI's themeTopTabActiveStyle). No-ops until the mode actually changes.
        private void ApplyUguiSettingsMainLodHighlight(UguiShellSettingsMainHandle handle)
        {
            int mode = Mathf.Clamp(this.lodOverrideMode, 0, LodOverrideModeLabels.Length - 1);
            if (handle.LodHighlightedMode == mode)
            {
                return;
            }
            handle.LodHighlightedMode = mode;

            Color accent = this.UguiKitAccent();
            Color onAccent = this.GetUiTextOnAccent(accent);
            Color inactiveFill = this.UguiKitControlFill();
            Color text = this.UguiKitTextColor();
            for (int i = 0; i < handle.LodModeButtonBgs.Count; i++)
            {
                bool active = i == mode;
                Image bg = handle.LodModeButtonBgs[i];
                if (bg != null)
                {
                    bg.color = active ? accent : inactiveFill;
                }
                if (i < handle.LodModeButtonLabels.Count)
                {
                    this.SetUguiLabelColor(handle.LodModeButtonLabels[i], active ? onAccent : text);
                }
            }
        }

        // Called every frame from ProcessUguiShellOnUpdate; skips in a few comparisons unless the
        // Main sub-tab is the visible one. Order matters: dropdown POLL fallback first (a user pick
        // must win over the same frame's external re-sync), then the cheap every-frame toggle/
        // layout/highlight syncs, then the 0.5s slow tick for text + slider/dropdown re-syncs.
        private void ProcessUguiShellSettingsMainOnUpdate()
        {
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsMainSubIndex))
            {
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure
                // (uguiPocDropdownPollFallback precedent).
                if (!handle.LangListenerWired && handle.LangDropdown != null)
                {
                    int v = handle.LangDropdown.value;
                    if (v != handle.LangLastValue)
                    {
                        this.OnUguiSettingsMainLanguagePicked(v); // updates LangLastValue itself
                    }
                }
                if (!handle.NotifPosListenerWired && handle.NotifPosDropdown != null)
                {
                    int v = handle.NotifPosDropdown.value;
                    if (v != handle.NotifPosLastValue)
                    {
                        this.OnUguiSettingsMainNotifPositionPicked(v);
                    }
                }

                // Cross-surface toggle re-sync (fields are also editable from the IMGUI tab).
                this.SyncUguiToggleFromField(handle.NotifToggle, this.notificationsEnabled);
                this.SyncUguiToggleFromField(handle.AutoStartToggle, this.autoClickStartEnabled);
                this.SyncUguiToggleFromField(handle.AutoCloseToggle, this.autoCloseAnnouncementEnabled);
                this.SyncUguiToggleFromField(handle.HideIdToggle, this.hideIdEnabled);
                this.SyncUguiToggleFromField(handle.CustomIdToggle, this.customDisplayIdEnabled);
                this.SyncUguiToggleFromField(handle.ShowOverlayToggle, this.showStatusOverlay);
                this.SyncUguiToggleFromField(handle.BlockInputToggle, this.blockGameUiWhenMenuOpen);
                this.SyncUguiToggleFromField(handle.FpsToggle, this.fpsBypassEnabled);

                // Conditional sections (notification position row / custom-ID readout / FPS slider /
                // LOD custom sliders) — relayout only when the packed visibility state changes.
                int signature = this.ComputeUguiSettingsMainLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellSettingsMain(handle);
                }

                this.ApplyUguiSettingsMainLodHighlight(handle);

                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SlowSyncUguiShellSettingsMain(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Settings Main content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        private void SyncUguiToggleFromField(Toggle toggle, bool live)
        {
            if (toggle == null)
            {
                return;
            }
            if (toggle.isOn != live)
            {
                toggle.SetIsOnWithoutNotify(live); // NEVER isOn = — that fires onValueChanged
            }
            // BUG FIX (2026-07-22): checkboxes appeared checked regardless of their true state
            // after a hidden tab/panel reactivated (menu close+reopen, or switching tabs). Root
            // cause: CreateUguiCheckbox's checkmark relies entirely on Unity's Toggle.graphic +
            // CrossFadeAlpha (ToggleTransition.Fade) — an EPHEMERAL canvas-renderer alpha, not a
            // persistent GameObject state. Graphic.OnEnable() rebuilds from the graphic's own
            // (opaque) base color, which can win the race against Toggle's own OnEnable→PlayEffect
            // call and leave the checkmark visually opaque — independent of the true isOn value,
            // so the isOn-mismatch check above never catches it (isOn itself was never wrong).
            // Forcing the checkmark's OWN GameObject active state every gated frame, unconditionally
            // (not just on a value change), makes the visual a plain persistent SetActive rather
            // than a fade tween — immune to the OnEnable race regardless of its exact cause. Safe
            // no-op for switch-style toggles (CreateUguiSwitch sets targetGraphic, never graphic).
            if (toggle.graphic != null)
            {
                SetUguiGoActive(toggle.graphic.gameObject, live);
            }
        }

        // 0.5s tick: value labels (LOD status reads live QualitySettings), slider positions and
        // dropdown selections re-synced from the fields. WithoutNotify everywhere — an external
        // re-sync must never replay a control's own side effects. Poll-fallback LastValue caches
        // are updated alongside, or the next poll frame would misread the re-sync as a user pick.
        private void SlowSyncUguiShellSettingsMain(UguiShellSettingsMainHandle handle)
        {
            this.SetUguiLabelText(handle.LodStatusLabel, this.BuildUguiSettingsMainLodStatusText());

            this.SetUguiLabelText(handle.FpsTargetLabel, this.LF("Target Max FPS: {0}", this.fpsBypassTarget));
            if (handle.FpsSlider != null && Mathf.RoundToInt(handle.FpsSlider.value) != this.fpsBypassTarget)
            {
                handle.FpsSlider.SetValueWithoutNotify(this.fpsBypassTarget);
            }

            this.SetUguiLabelText(handle.LodBiasLabel, this.LF("LOD bias: {0:0.##}", this.lodCustomBias));
            if (handle.LodBiasSlider != null && Mathf.Abs(handle.LodBiasSlider.value - this.lodCustomBias) > 0.001f)
            {
                handle.LodBiasSlider.SetValueWithoutNotify(this.lodCustomBias);
            }
            this.SetUguiLabelText(handle.LodMaxLabel, this.LF("Max LOD level: {0}", this.lodCustomMaxLevel));
            if (handle.LodMaxSlider != null && Mathf.RoundToInt(handle.LodMaxSlider.value) != this.lodCustomMaxLevel)
            {
                handle.LodMaxSlider.SetValueWithoutNotify(this.lodCustomMaxLevel);
            }

            // Custom ID field: adopt external changes (the IMGUI twin, a config load, Reset) but
            // NEVER while the field has focus. NormalizeCustomId collapses whitespace, so pushing
            // the normalized value back mid-edit would eat a space the moment it was typed.
            string idValue = this.customDisplayId ?? string.Empty;
            if (!string.Equals(idValue, handle.CustomIdShownValue, StringComparison.Ordinal))
            {
                bool focused = false;
                try { focused = handle.CustomIdField != null && handle.CustomIdField.isFocused; }
                catch { }
                if (!focused)
                {
                    handle.CustomIdShownValue = idValue;
                    if (handle.CustomIdField != null)
                    {
                        try { handle.CustomIdField.SetTextWithoutNotify(idValue); } // never text = — fires onValueChanged
                        catch { }
                    }
                }
            }

            if (handle.LangDropdown != null && handle.LangCodes != null)
            {
                int want = IndexOfLanguageCode(handle.LangCodes, this.selectedLanguage);
                if (want >= 0 && handle.LangDropdown.value != want)
                {
                    handle.LangDropdown.SetValueWithoutNotify(want);
                    handle.LangLastValue = want;
                }
            }
            if (handle.NotifPosDropdown != null)
            {
                int want = Mathf.Clamp(this.notificationPosition, 0, NotificationPositionOptions.Length - 1);
                if (handle.NotifPosDropdown.value != want)
                {
                    handle.NotifPosDropdown.SetValueWithoutNotify(want);
                    handle.NotifPosLastValue = want;
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Main-tab change handlers — each mirrors its IMGUI block EXACTLY (same save/reset/notify
        // sequence, same one-direction-only notifications where IMGUI has them). Every handler
        // guards on "value actually changed" so a redundant event can never replay side effects.
        // ----------------------------------------------------------------------------------------

        // Shared by the wired listener AND the poll fallback. IMGUI: option click →
        // SetLocalizationLanguage(code, true) (Config.cs:1228).
        private void OnUguiSettingsMainLanguagePicked(int index)
        {
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle == null || handle.LangCodes == null || index < 0 || index >= handle.LangCodes.Length)
            {
                return;
            }
            handle.LangLastValue = index;
            string code = handle.LangCodes[index];
            if (string.Equals(code, this.selectedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return; // re-pick of the current language — IMGUI would no-op visibly too
            }
            this.SetLocalizationLanguage(code, true);
        }

        // IMGUI Config.cs:1170-1183 — notification only when turning ON.
        private void OnUguiSettingsMainNotificationsChanged(bool value)
        {
            if (value == this.notificationsEnabled)
            {
                return;
            }
            this.notificationsEnabled = value;
            this.SaveKeybinds(false);
            if (this.notificationsEnabled)
            {
                this.AddMenuNotification(this.L("Notifications enabled"), new Color(0.55f, 0.88f, 1f));
            }
        }

        // IMGUI Config.cs:1236-1262 — save + notify only when the position actually changed.
        private void OnUguiSettingsMainNotifPositionPicked(int index)
        {
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle != null)
            {
                handle.NotifPosLastValue = index;
            }
            if (index < 0 || index >= NotificationPositionOptions.Length || index == this.notificationPosition)
            {
                return;
            }
            this.notificationPosition = index;
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                this.LF("Notification position: {0}", this.L(NotificationPositionOptions[this.notificationPosition])),
                new Color(0.55f, 0.88f, 1f));
        }

        // IMGUI Config.cs:1272-1285 — notifies BOTH directions, different colors.
        private void OnUguiSettingsMainAutoStartChanged(bool value)
        {
            if (value == this.autoClickStartEnabled)
            {
                return;
            }
            this.autoClickStartEnabled = value;
            this.SaveKeybinds(false);
            if (this.autoClickStartEnabled)
            {
                this.AddMenuNotification(this.L("Auto Start enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Auto Start disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // IMGUI Config.cs:1288-1301.
        private void OnUguiSettingsMainAutoCloseChanged(bool value)
        {
            if (value == this.autoCloseAnnouncementEnabled)
            {
                return;
            }
            this.autoCloseAnnouncementEnabled = value;
            this.SaveKeybinds(false);
            if (this.autoCloseAnnouncementEnabled)
            {
                this.AddMenuNotification(this.L("Auto Close Announcement enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Auto Close Announcement disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // IMGUI Config.cs:1304-1318 — resets the ID-display refresh timer; BOTH notifications are
        // the light-blue color (that asymmetry is the IMGUI behavior, kept on purpose).
        private void OnUguiSettingsMainHideIdChanged(bool value)
        {
            if (value == this.hideIdEnabled)
            {
                return;
            }
            this.hideIdEnabled = value;
            this.nextIdDisplayUpdateAt = 0f;
            this.SaveKeybinds(false);
            if (this.hideIdEnabled)
            {
                this.AddMenuNotification(this.L("ID display hidden"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("ID display shown"), new Color(0.55f, 0.88f, 1f));
            }
        }

        // IMGUI Config.cs:1321-1335.
        private void OnUguiSettingsMainCustomIdChanged(bool value)
        {
            if (value == this.customDisplayIdEnabled)
            {
                return;
            }
            this.customDisplayIdEnabled = value;
            this.nextIdDisplayUpdateAt = 0f;
            this.SaveKeybinds(false);
            if (this.customDisplayIdEnabled)
            {
                this.AddMenuNotification(this.L("Custom ID enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Custom ID disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // IMGUI Config.cs:1352-1368 — the source's GUI.TextField edit block, verbatim: normalize,
        // bail if unchanged, then write + clear the ID-display timer + save + notify. Keeping
        // CustomIdShownValue in step here stops the per-frame sync from immediately re-pushing the
        // normalized text back into the field while it is still focused.
        private void OnUguiSettingsMainCustomIdValueEdited(string raw)
        {
            string next = this.NormalizeCustomId(raw ?? string.Empty);
            string previous = this.customDisplayId ?? string.Empty;
            if (string.Equals(next, previous, StringComparison.Ordinal))
            {
                return;
            }
            this.customDisplayId = next;
            this.nextIdDisplayUpdateAt = 0f;
            this.SaveKeybinds(false);
            UguiShellSettingsMainHandle liveHandle = this.uguiShellSettingsMain;
            if (liveHandle != null)
            {
                liveHandle.CustomIdShownValue = next;
            }
            if (string.IsNullOrEmpty(next))
            {
                this.AddMenuNotification(this.L("Custom ID cleared"), new Color(0.88f, 0.6f, 0.6f));
            }
            else
            {
                this.AddMenuNotification(this.L("Custom ID updated"), new Color(0.55f, 0.88f, 1f));
            }
        }

        // IMGUI Config.cs:1364-1380 — includes the Phase 2d UGUI-overlay hook (same-frame twin
        // reaction), called exactly once here, nowhere else in this file.
        private void OnUguiSettingsMainShowOverlayChanged(bool value)
        {
            if (value == this.showStatusOverlay)
            {
                return;
            }
            this.showStatusOverlay = value;
            this.SaveKeybinds(false);
            this.ProcessUguiStatusOverlayOnUpdate();
            if (this.showStatusOverlay)
            {
                this.AddMenuNotification(this.L("Status overlay enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Status overlay disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // IMGUI Config.cs:1383-1396.
        private void OnUguiSettingsMainBlockInputChanged(bool value)
        {
            if (value == this.blockGameUiWhenMenuOpen)
            {
                return;
            }
            this.blockGameUiWhenMenuOpen = value;
            this.SaveKeybinds(false);
            if (this.blockGameUiWhenMenuOpen)
            {
                this.AddMenuNotification(this.L("Block Input Enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Block Input Disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // IMGUI Config.cs:1406-1418 — the full reset sequence, not just a flag flip.
        private void OnUguiSettingsMainFpsBypassChanged(bool value)
        {
            if (value == this.fpsBypassEnabled)
            {
                return;
            }
            this.fpsBypassEnabled = value;
            this.nextFpsBypassTuneAt = 0f;
            this.fpsBypassCompOffset = 0f;
            this.fpsBypassObservedFps = 0f;
            this.ApplyFpsBypass(this.fpsBypassEnabled);
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                this.fpsBypassEnabled ? this.L("FPS Bypass Enabled") : this.L("FPS Bypass Disabled"),
                this.fpsBypassEnabled ? new Color(0.55f, 0.88f, 1f) : new Color(0.88f, 0.6f, 0.6f));
        }

        // IMGUI Config.cs:1423-1430. The slider is wholeNumbers 30..360, replacing IMGUI's
        // 30..361 + RoundToInt stepping trick — same effective range.
        private void OnUguiSettingsMainFpsTargetChanged(float value)
        {
            int target = Mathf.Clamp(Mathf.RoundToInt(value), 30, 360);
            if (target == this.fpsBypassTarget)
            {
                return;
            }
            this.fpsBypassTarget = target;
            this.ApplyFpsBypass(true);
            this.SaveKeybinds(false);
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle != null)
            {
                this.SetUguiLabelText(handle.FpsTargetLabel, this.LF("Target Max FPS: {0}", this.fpsBypassTarget));
            }
        }

        // LodSettingsFeature.cs:225-233 — clamp + timer reset + re-apply + save.
        private void OnUguiSettingsMainLodBiasChanged(float value)
        {
            if (Mathf.Abs(value - this.lodCustomBias) <= 0.001f)
            {
                return;
            }
            this.lodCustomBias = Mathf.Clamp(value, 0.25f, 4f);
            this.nextLodOverrideApplyAt = 0f;
            this.ApplyLodOverride(true);
            this.SaveKeybinds(false);
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle != null)
            {
                this.SetUguiLabelText(handle.LodBiasLabel, this.LF("LOD bias: {0:0.##}", this.lodCustomBias));
            }
        }

        // LodSettingsFeature.cs:236-244.
        private void OnUguiSettingsMainLodMaxLevelChanged(float value)
        {
            int level = Mathf.Clamp(Mathf.RoundToInt(value), 0, 4);
            if (level == this.lodCustomMaxLevel)
            {
                return;
            }
            this.lodCustomMaxLevel = level;
            this.nextLodOverrideApplyAt = 0f;
            this.ApplyLodOverride(true);
            this.SaveKeybinds(false);
            UguiShellSettingsMainHandle handle = this.uguiShellSettingsMain;
            if (handle != null)
            {
                this.SetUguiLabelText(handle.LodMaxLabel, this.LF("Max LOD level: {0}", this.lodCustomMaxLevel));
            }
        }
    }
}
