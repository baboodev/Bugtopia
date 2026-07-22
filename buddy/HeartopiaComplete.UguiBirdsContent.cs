using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Resource Gathering round 4 of 4 (migration plan:
    // cosmic-waddling-rainbow.md, item 6): the BIRDS sub-tab — BirdNetFarm.DrawSection
    // (BirdNetFarm.cs:464-548). This completes the Resource Gathering tab: all four sub display
    // indices (0-3) now have real content and the shell's placeholder fallthrough no longer
    // serves this tab.
    //
    // Ground rules (same as Foraging/Fishing/Insects and every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional — this file
    //    only READS the same state and CALLS public BirdNetFarm accessors. UNLIKE Insects (zero
    //    additions), this round required real backend interop: perfectPhotoEnabled / captureMode
    //    / catchCooldown / scanRange / multiCatchLimit had NO public accessor of any kind, so
    //    BirdNetFarm gained five Get/Set*FromUi groups (BirdNetFarm.cs:161-231) plus ONE
    //    sanctioned call-site simplification: DrawSection's Capture Mode block now routes its
    //    change cascade through the shared SetCaptureModeFromUi (the private pending-confirm/
    //    burst state it clears is unreachable from outside the class — the
    //    FishingRouteFeature.RemoveCustomSpotAt precedent).
    //  - SAVE MODEL IS DIFFERENT FROM EVERY SIBLING: BirdNetFarm saves on a 2s DEBOUNCE
    //    (pendingSaveAt, flushed by BirdNetFarm.Update via host.SaveAllSettings — runs every
    //    frame regardless of menu state, BirdNetFarm.cs:562-566). The new setters arm that same
    //    debounce internally, so this file calls NO Save method anywhere — no SaveKeybinds, no
    //    SaveAllSettings.
    //  - Wiring is by STATIC display-position index (UguiShellResourceGatheringTabIndex = 1 +
    //    UguiShellBirdsSubIndex = 3, declared next to their siblings in UguiPhase3Content.cs;
    //    Birds = autoFarmSubTab 3 per DrawAutoFarmTab's dispatcher, Farm.cs:132-135), never
    //    label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own.
    //
    // Presentation: flat linear list straight onto the tab background (no
    // DrawExentriSectionPanel), zero conditional blocks, zero AddMenuNotification calls — the
    // Insects shape. One fixed stack inside a transparent scroll view, every position replaying
    // the IMGUI drawer's `num +=` cursor chain ONCE at build time. Birds-specific deviations
    // from the Insects template, each verified against source:
    //  - The equip button's own margin is 45 (BirdNetFarm.cs:477), NOT the 42 Fishing/Insects
    //    use.
    //  - Capture Mode is the FIRST dropdown in any Resource Gathering round — the kit's real
    //    CreateUguiDropdown wired exactly like Settings→Main's language dropdown (the
    //    out-bool listenerWired + per-frame poll fallback precedent,
    //    UguiPhase3SettingsContent.cs). Option strings are pre-localized at build time
    //    (IMGUI's UI_DrawSingleSelectDropdown L()s caption and rows, UiKit.cs:608/638 — the
    //    NotifPos precedent); the backend keeps raw indices.
    //  - IMGUI's `num += captureModeDropdownOpen ? 122 : 80` is an inline-reflow artifact of
    //    drawing the expanded option list in the flow; the stock UnityEngine.UI.Dropdown shows
    //    its list as its own popup overlay (template hierarchy — it never pushes siblings), so
    //    this layout uses the closed-state 80 unconditionally. No conditional-height machinery.
    //  - Status/Tool labels DOUBLE-localize: the drawer wraps the inner value in UI_Localize
    //    before UI_LocalizeFormat (BirdNetFarm.cs:507/509 — the Fishing Route-status shape), so
    //    this file wraps the getters in L() too. The Birds count line passes two PLAIN ints
    //    (BirdNetFarm.cs:511), not localized individually.
    //  - Slider rounding rules match BirdNetFarm, NOT InsectNetFarm's same-named sliders:
    //    Catch Cooldown rounds to TENTHS in the SETTER itself (unlike Insects'), Scan Range to
    //    wholes, Multi-Catch Limit is the int slider with its max sourced from
    //    GetMaxMultiCatchLimit() (the private const 10 — never hardcoded here).
    //
    // Cross-surface sync cadence (Insects shape): every gated frame (shell visible + Resource
    // Gathering tab + Birds sub-tab, via the Foraging round's shared gate) re-sync both toggles
    // (SetIsOnWithoutNotify — NEVER SetEnabled, which captures/restores the equipped tool and
    // resets session counters) and all 3 sliders (SetValueWithoutNotify, cheap diff guard),
    // refresh Status/Tool/Birds label texts via cached-string diffs (they move from background
    // farm activity), and re-sync the dropdown's selected index if the backend drifted
    // externally (SetValueWithoutNotify + LastValue update — the Settings→Main approach). The
    // poll fallback runs first so a user pick is applied before the external re-sync could
    // clobber the visual. No 0.5s throttle tier: nothing here allocates (pure static-field
    // reads). Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellBirdsHandle
        {
            public GameObject Root;
            public Transform ScrollContent;

            public Toggle EnabledToggle;
            public Toggle PerfectPhotoToggle;

            public Dropdown CaptureModeDropdown;
            public bool CaptureModeListenerWired;
            public int CaptureModeLastValue;      // poll-fallback change detection

            public GameObject StatusLabel;
            public string StatusShown;
            public GameObject ToolLabel;
            public string ToolShown;
            public GameObject BirdsLabel;
            public string BirdsShown;

            // Slider block — parallel lists in binding order (Insects/Game-UI idiom).
            public UguiBirdsSliderBinding[] SliderBindings;
            public readonly List<GameObject> SliderLabels = new List<GameObject>();
            public readonly List<string> SliderShown = new List<string>();
            public readonly List<Slider> Sliders = new List<Slider>();

            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellBirdsHandle uguiShellBirds;

        // One slider binding — label format + the SOURCE slider's own bounds + rounding rule +
        // get/set over BirdNetFarm's public accessors, NOTHING else (no cross-field side
        // effects; the setters arm the 2s debounced save themselves, so the change handler
        // never saves). Set receives the ALREADY-rounded value; every setter re-rounds/clamps
        // identically, so the double application is a no-op.
        private struct UguiBirdsSliderBinding
        {
            public string LabelFormat;            // LF format over the live getter value
            public float Min;
            public float Max;
            public int Decimals;                  // 1 = tenths, 0 = wholes (ignored when IsInteger)
            public bool IsInteger;
            public Func<float> Get;
            public Action<float> Set;
        }

        // Static (Logging-round precedent): every delegate targets static BirdNetFarm state,
        // nothing captures the host.
        private static UguiBirdsSliderBinding[] BuildUguiBirdsSliderBindings()
        {
            return new UguiBirdsSliderBinding[]
            {
                // BirdNetFarm.cs:514-523 — rounds to TENTHS (in the setter too, unlike Insects).
                new UguiBirdsSliderBinding
                {
                    LabelFormat = "Catch Cooldown: {0:F1}s", Min = 0.2f, Max = 10f,
                    Decimals = 1, IsInteger = false,
                    Get = () => BirdNetFarm.GetCatchCooldown(),
                    Set = v => BirdNetFarm.SetCatchCooldownFromUi(v)
                },
                // BirdNetFarm.cs:525-534 — rounds to WHOLE metres.
                new UguiBirdsSliderBinding
                {
                    LabelFormat = "Scan Range: {0:F0}m", Min = 1f, Max = 100f,
                    Decimals = 0, IsInteger = false,
                    Get = () => BirdNetFarm.GetScanRange(),
                    Set = v => BirdNetFarm.SetScanRangeFromUi(v)
                },
                // BirdNetFarm.cs:536-545 — the one INT slider; max = the private const via its
                // getter (GetMaxMultiCatchLimit), NEVER a hardcoded 10.
                new UguiBirdsSliderBinding
                {
                    LabelFormat = "Multi-Catch Limit: {0}", Min = 1f,
                    Max = BirdNetFarm.GetMaxMultiCatchLimit(),
                    Decimals = 0, IsInteger = true,
                    Get = () => BirdNetFarm.GetMultiCatchLimit(),
                    Set = v => BirdNetFarm.SetMultiCatchLimitFromUi(Mathf.RoundToInt(v))
                },
            };
        }

        // IMGUI displays the raw field through the format string (F1/F0 do the display
        // rounding); the int entry boxes an int so "{0}" renders exactly like the source's int
        // argument.
        private string BuildUguiBirdsSliderLabelText(UguiBirdsSliderBinding binding)
        {
            float value = binding.Get();
            return binding.IsInteger
                ? this.LF(binding.LabelFormat, Mathf.RoundToInt(value))
                : this.LF(binding.LabelFormat, value);
        }

        // ----------------------------------------------------------------------------------------
        // Builder — positions replay BirdNetFarm.DrawSection's own `num +=` cursor chain, base
        // y=8 / x=16 (Fishing convention: x=16 after the 4px viewport inset sits at the IMGUI
        // drawer's visual x=20). Fixed chain, annotated with the running IMGUI cursor:
        //   8 header +28 → 36 equip +45 → 81 enabled +30 → 111 perfect photo +30 → 141 capture
        //   mode (label at the cursor, control at cursor+22 — UI_DrawSingleSelectDropdown draws
        //   its label at rect.y-22, UiKit.cs:587) +80 closed-state (see file header) → 221
        //   status +24 → 245 tool +24 → 269 birds +24 → 293 then three of (label +22,
        //   slider +30) → 449 = the source's own returned cursor with the dropdown closed
        //   (DrawSection returns bare num — the IMGUI tab height is a separate 980f estimate in
        //   CalculateAutoFarmTabHeight, not derived from this).
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellBirdsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellBirds = null;

            UguiShellBirdsHandle handle = new UguiShellBirdsHandle();
            GameObject block = this.CreateUguiGo("BirdsContent", parent);
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
            handle.ScrollContent = scrollContent;

            // BirdNetFarm.cs:466-470 — IMGUI `header` style = fontSize 14 bold.
            GameObject farmHeader = this.CreateUguiHeaderLabel(scrollContent, "FarmHeader",
                this.L("Auto Bird Farm"), 14f);
            PlaceUguiTopLeft(farmHeader, 16f, 8f, 320f, 22f);

            // BirdNetFarm.cs:472-477.
            GameObject equipBtn = this.CreateUguiPrimaryButton(scrollContent, "EquipScanner",
                this.L("Equip Bird Scanner"), new System.Action(this.OnUguiBirdsEquipScannerClicked));
            PlaceUguiTopLeft(equipBtn, 16f, 36f, 260f, 35f);

            // BirdNetFarm.cs:479-484.
            handle.EnabledToggle = this.CreateUguiCheckbox(scrollContent, "EnabledToggle",
                this.L("Auto Bird Farm"), BirdNetFarm.IsEnabled,
                new System.Action<bool>(this.OnUguiBirdsEnabledToggled));
            PlaceUguiTopLeft(handle.EnabledToggle.gameObject, 16f, 81f, 280f, 25f);

            // BirdNetFarm.cs:486-493.
            handle.PerfectPhotoToggle = this.CreateUguiCheckbox(scrollContent, "PerfectPhotoToggle",
                this.L("Perfect Photo"), BirdNetFarm.GetPerfectPhotoEnabled(),
                new System.Action<bool>(this.OnUguiBirdsPerfectPhotoToggled));
            PlaceUguiTopLeft(handle.PerfectPhotoToggle.gameObject, 16f, 111f, 280f, 25f);

            // BirdNetFarm.cs:495-505 — label mirrors UI_DrawSingleSelectDropdown's own bold-13
            // label row (drawn at rect.y-22, width = the control's 260); options pre-localized
            // like the IMGUI widget renders them (see file header).
            GameObject captureLabel = this.CreateUguiBodyLabel(scrollContent, "CaptureModeLabel",
                this.L("Capture Mode"), 13f);
            PlaceUguiTopLeft(captureLabel, 16f, 141f, 260f, 20f);

            string[] captureModeRaw = BirdNetFarm.GetCaptureModeOptions();
            string[] captureModeNames = new string[captureModeRaw.Length];
            for (int i = 0; i < captureModeRaw.Length; i++)
            {
                captureModeNames[i] = this.L(captureModeRaw[i]);
            }
            int captureInitial = Mathf.Clamp(BirdNetFarm.GetCaptureMode(), 0, captureModeRaw.Length - 1);
            handle.CaptureModeLastValue = captureInitial;
            bool captureWired;
            handle.CaptureModeDropdown = this.CreateUguiDropdown(scrollContent, "CaptureModeDropdown",
                captureModeNames, captureInitial,
                new System.Action<int>(this.OnUguiBirdsCaptureModePicked), out captureWired);
            handle.CaptureModeListenerWired = captureWired;
            PlaceUguiTopLeft(handle.CaptureModeDropdown.gameObject, 16f, 163f, 260f, 28f);

            // BirdNetFarm.cs:507-512 — safe to seed at build time (pure reads with their own
            // Idle/Unknown fallbacks). IMGUI `small` style = fontSize 12. Status/Tool wrap the
            // inner value in L() (double-localization, see file header); Birds passes plain ints.
            handle.StatusShown = this.LF("Status: {0}", this.L(BirdNetFarm.GetLastStatus()));
            handle.StatusLabel = this.CreateUguiBodyLabel(scrollContent, "StatusLabel", handle.StatusShown, 12f);
            PlaceUguiTopLeft(handle.StatusLabel, 16f, 221f, 360f, 20f);

            handle.ToolShown = this.LF("Tool: {0}", this.L(BirdNetFarm.GetLastToolStatus()));
            handle.ToolLabel = this.CreateUguiBodyLabel(scrollContent, "ToolLabel", handle.ToolShown, 12f);
            PlaceUguiTopLeft(handle.ToolLabel, 16f, 245f, 360f, 20f);

            handle.BirdsShown = this.LF("Birds: {0} caught | {1} scared",
                BirdNetFarm.GetSessionCatchCount(), BirdNetFarm.GetSessionScaredCount());
            handle.BirdsLabel = this.CreateUguiBodyLabel(scrollContent, "BirdsLabel", handle.BirdsShown, 12f);
            PlaceUguiTopLeft(handle.BirdsLabel, 16f, 269f, 360f, 20f);

            // BirdNetFarm.cs:514-545 — the three slider blocks, one loop over the binding array.
            // The change closure captures the array element directly (not the handle, which is
            // still null until assigned below): the delegates target static BirdNetFarm state,
            // so a stale closure after a shell rebuild behaves identically anyway.
            UguiBirdsSliderBinding[] bindings = BuildUguiBirdsSliderBindings();
            handle.SliderBindings = bindings;
            float yCur = 293f;
            for (int i = 0; i < bindings.Length; i++)
            {
                int indexCopy = i; // capture a copy for the change closure
                string text = this.BuildUguiBirdsSliderLabelText(bindings[i]);
                GameObject label = this.CreateUguiBodyLabel(scrollContent, "SliderLabel" + i, text, 12f);
                PlaceUguiTopLeft(label, 16f, yCur, 320f, 20f);
                handle.SliderLabels.Add(label);
                handle.SliderShown.Add(text);
                yCur += 22f;

                bool wholeNumbers = bindings[i].IsInteger || bindings[i].Decimals == 0;
                Slider slider = this.CreateUguiSlider(scrollContent, "Slider" + i,
                    bindings[i].Min, bindings[i].Max, bindings[i].Get(), wholeNumbers,
                    new System.Action<float>(v => this.OnUguiBirdsSliderChanged(bindings[indexCopy], v)));
                PlaceUguiTopLeft(slider.gameObject, 16f, yCur, 260f, 20f);
                handle.Sliders.Add(slider);
                yCur += 30f;
            }

            // yCur = 449 here = DrawSection's own returned cursor with the dropdown closed (see
            // builder header).
            this.SetUguiScrollContentHeight(scrollContent, yCur);

            handle.Root = block;
            this.uguiShellBirds = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellBirdsOnUpdate()
        {
            UguiShellBirdsHandle handle = this.uguiShellBirds;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellBirdsSubIndex))
            {
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure
                // (uguiPocDropdownPollFallback precedent, same as Settings→Main). Runs BEFORE
                // the external re-sync below so a user pick lands before it could be clobbered.
                if (!handle.CaptureModeListenerWired && handle.CaptureModeDropdown != null)
                {
                    int v = handle.CaptureModeDropdown.value;
                    if (v != handle.CaptureModeLastValue)
                    {
                        this.OnUguiBirdsCaptureModePicked(v); // updates CaptureModeLastValue itself
                    }
                }

                // Toggle re-syncs (external IMGUI/hotkey edits) — WithoutNotify only; the plain
                // path would replay side effects (SetEnabled captures/restores the equipped tool
                // and resets both session counters on disable).
                this.SyncUguiToggleFromField(handle.EnabledToggle, BirdNetFarm.IsEnabled);
                this.SyncUguiToggleFromField(handle.PerfectPhotoToggle, BirdNetFarm.GetPerfectPhotoEnabled());

                // Dropdown external re-sync (the IMGUI twin or ApplyBirdFarmConfig moved the
                // backend) — Settings→Main's approach: WithoutNotify + LastValue update.
                if (handle.CaptureModeDropdown != null)
                {
                    int want = Mathf.Clamp(BirdNetFarm.GetCaptureMode(), 0,
                        Mathf.Max(0, BirdNetFarm.GetCaptureModeOptions().Length - 1));
                    if (handle.CaptureModeDropdown.value != want)
                    {
                        handle.CaptureModeDropdown.SetValueWithoutNotify(want);
                        handle.CaptureModeLastValue = want;
                    }
                }

                // Status readouts — every gated frame like the IMGUI drawer (background farm
                // activity moves them, not just user edits); cached diffs limit SetText churn.
                // Both session counters also reset via SetEnabled(false), which is exactly why
                // the Birds line re-syncs here rather than only on its own change.
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.LF("Status: {0}", this.L(BirdNetFarm.GetLastStatus())));
                this.SyncUguiSelfLabelText(handle.ToolLabel, ref handle.ToolShown,
                    this.LF("Tool: {0}", this.L(BirdNetFarm.GetLastToolStatus())));
                this.SyncUguiSelfLabelText(handle.BirdsLabel, ref handle.BirdsShown,
                    this.LF("Birds: {0} caught | {1} scared",
                        BirdNetFarm.GetSessionCatchCount(), BirdNetFarm.GetSessionScaredCount()));

                // Slider re-syncs + value labels, one loop over the same binding array. The label
                // formats the RAW live value (ApplyBirdFarmConfig can store an unrounded float;
                // F1/F0 display it exactly as the IMGUI twin would).
                for (int i = 0; i < handle.Sliders.Count && i < handle.SliderBindings.Length; i++)
                {
                    float live = handle.SliderBindings[i].Get();
                    Slider slider = handle.Sliders[i];
                    if (slider != null && Mathf.Abs(slider.value - live) > 0.0005f)
                    {
                        slider.SetValueWithoutNotify(live);
                    }
                    string text = this.BuildUguiBirdsSliderLabelText(handle.SliderBindings[i]);
                    if (i < handle.SliderLabels.Count
                        && !string.Equals(text, handle.SliderShown[i], StringComparison.Ordinal))
                    {
                        handle.SliderShown[i] = text;
                        this.SetUguiLabelText(handle.SliderLabels[i], text);
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Birds content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order;
        // no notifications anywhere — the source drawer never toasts — and NO direct saves
        // anywhere: the Set*FromUi setters arm BirdNetFarm's 2s debounce themselves).
        // ----------------------------------------------------------------------------------------

        // BirdNetFarm.cs:472-477.
        private void OnUguiBirdsEquipScannerClicked()
        {
            this.EquipHandTool(4);
        }

        // BirdNetFarm.cs:479-484 — SetEnabled MUST get the host: enabling captures the
        // previously-equipped tool, disabling restores it (and resets both session counters); a
        // null host would silently break the tool capture/restore. No save (source parity —
        // SetEnabled itself is the whole block).
        private void OnUguiBirdsEnabledToggled(bool value)
        {
            if (value == BirdNetFarm.IsEnabled)
            {
                return;
            }
            BirdNetFarm.SetEnabled(value, this);
        }

        // BirdNetFarm.cs:486-493 — field write + debounce arm, both inside the setter.
        private void OnUguiBirdsPerfectPhotoToggled(bool value)
        {
            if (value == BirdNetFarm.GetPerfectPhotoEnabled())
            {
                return;
            }
            BirdNetFarm.SetPerfectPhotoEnabledFromUi(value);
        }

        // BirdNetFarm.cs:495-505 — shared by the wired listener AND the poll fallback (the
        // OnUguiSettingsMainLanguagePicked shape: LastValue first, then guard on the backend so
        // a redundant event can never replay the cascade). SetCaptureModeFromUi runs the FULL
        // change cascade: debounce arm + ClearBirdFarmRuntimeState + pending-confirm/burst
        // resets — and needs the host for the runtime-state clear.
        private void OnUguiBirdsCaptureModePicked(int index)
        {
            UguiShellBirdsHandle handle = this.uguiShellBirds;
            if (handle == null)
            {
                return;
            }
            handle.CaptureModeLastValue = index;
            if (index == BirdNetFarm.GetCaptureMode())
            {
                return;
            }
            BirdNetFarm.SetCaptureModeFromUi(index, this);
        }

        // BirdNetFarm.cs:514-545 — shared by all three sliders. Round per the entry's rule FIRST
        // (UGUI fires continuously during a drag; rounding before the change-guard keeps the
        // setter from firing on every micro-move), then setter only on an actual change — no
        // save call, the setter arms the debounce (each source block's
        // `if (changed) { pendingSaveAt = ...; }` pattern lives inside Set*FromUi).
        private void OnUguiBirdsSliderChanged(UguiBirdsSliderBinding binding, float value)
        {
            if (binding.Get == null || binding.Set == null)
            {
                return;
            }

            float rounded;
            if (binding.IsInteger)
            {
                // The source's own int rule (Mathf.RoundToInt, clamped by the slider range).
                rounded = Mathf.Clamp(Mathf.RoundToInt(value),
                    Mathf.RoundToInt(binding.Min), Mathf.RoundToInt(binding.Max));
            }
            else if (binding.Decimals == 1)
            {
                rounded = Mathf.Round(value * 10f) / 10f;
            }
            else
            {
                rounded = Mathf.Round(value);
            }

            if (Mathf.Abs(rounded - binding.Get()) <= 0.0001f)
            {
                return;
            }
            binding.Set(rounded);
        }
    }
}
