using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Resource Gathering round 3 of 4 (migration plan:
    // cosmic-waddling-rainbow.md, item 6): the INSECTS sub-tab only — InsectNetFarm.DrawSection
    // (InsectNetFarm.cs:232-343). Birds is the remaining round and is untouched (its cell stays
    // a shell placeholder).
    //
    // Ground rules (same as Foraging/Fishing and every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same state and CALLS the same public InsectNetFarm accessors.
    //    ZERO backend additions this round (unlike Fishing's interop wrappers): every value the
    //    drawer touches already has a public accessor whose clamp matches the slider's own range.
    //  - Wiring is by STATIC display-position index (UguiShellResourceGatheringTabIndex = 1 +
    //    UguiShellInsectsSubIndex = 2, declared next to their siblings in UguiPhase3Content.cs;
    //    Insects = autoFarmSubTab 2 per DrawAutoFarmTab's dispatcher, Farm.cs:128-131), never
    //    label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own.
    //
    // Presentation: the source is the simplest of the four rounds — one FLAT linear list straight
    // onto the tab background (no DrawExentriSectionPanel), ZERO conditional blocks and ZERO
    // AddMenuNotification calls (none added here either). So: one fixed stack inside a
    // transparent scroll view (Logging idiom — alpha-0 images still raycast, so wheel/drag
    // scrolling works over the block's own ContentBg), every position replaying the IMGUI
    // drawer's own `num +=` cursor chain ONCE at build time. No RelayoutOnSignatureChange
    // machinery — nothing ever moves or hides.
    //
    // The five sliders are genuinely uniform in SHAPE (label+range+get/set+save, no cross-field
    // side effects) but not in NUMBERS, so they are one data-driven binding array + loop (the
    // Logging/Game-UI array precedent) instead of five handwritten blocks. Multi-Catch Limit is
    // the one INT slider (source: Mathf.Clamp(Mathf.RoundToInt(...), 1, 10), InsectNetFarm.cs:333)
    // and keeps its int semantics via the entry's IsInteger flag; the three F1 sliders round to
    // tenths (the Game-UI round's 0.1 idiom), Scan Range to wholes (Fishing's Scan Range idiom).
    //
    // Source oddities carried over knowingly:
    //  - Each IMGUI slider block writes the raw field FIRST and then — on change — calls the
    //    matching Set* with that same already-current value; since every setter clamp equals the
    //    slider range, the double write is a no-op beyond the single setter call. This port calls
    //    the public setter ONCE with the rounded value on an actual change, then
    //    SaveKeybinds(false) (each block's change-guarded save; the private Log() calls are debug
    //    output, deliberately not reproduced — same as every prior round).
    //  - The Eat Teleport Pause SLIDER runs 0.5-15.2 while its setter clamps 0.5-15 — kept
    //    exactly (deliberately NOT "fixed" into consistency): dragging into the 15.0-15.2 dead
    //    zone stores 15 and the per-frame re-sync snaps the knob back, the same clamp-after-write
    //    tug the IMGUI twin produces there on its own next frame.
    //
    // Cross-surface sync cadence: every gated frame (shell visible + Resource Gathering tab +
    // Insects sub-tab, via the Foraging round's shared gate) re-sync all 3 toggles
    // (SetIsOnWithoutNotify) and all 5 sliders (SetValueWithoutNotify — NEVER the plain setters;
    // SetEnabled resets sessionCatchCount and tool state) and refresh every label text via
    // cached-string diffs — Status/Tool/Caught change from background farm activity, not just
    // user edits, and the slider value labels are cheap. No 0.5s throttle tier this round:
    // nothing here allocates or recomputes (pure static-field reads). Per-frame sync disabled
    // after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellInsectsHandle
        {
            public GameObject Root;
            public Transform ScrollContent;

            public Toggle EnabledToggle;
            public GameObject StatusLabel;
            public string StatusShown;
            public GameObject ToolLabel;
            public string ToolShown;
            public GameObject CaughtLabel;
            public string CaughtShown;
            public Toggle TeleportToggle;
            public Toggle PauseTriggersToggle;

            // Slider block — parallel lists in binding order (Game-UI round idiom).
            public UguiInsectsSliderBinding[] SliderBindings;
            public readonly List<GameObject> SliderLabels = new List<GameObject>();
            public readonly List<string> SliderShown = new List<string>();
            public readonly List<Slider> Sliders = new List<Slider>();

            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellInsectsHandle uguiShellInsects;

        // One slider binding — label format + the SOURCE slider's own bounds + rounding rule +
        // get/set over InsectNetFarm's public accessors, NOTHING else (the source blocks have no
        // cross-field side effects). Four float entries and one int entry share the shape:
        //  - IsInteger (Multi-Catch Limit only): Mathf.Clamp(Mathf.RoundToInt(v), Min, Max) —
        //    the source's own int rule (InsectNetFarm.cs:333) — and a wholeNumbers UGUI slider.
        //  - Decimals = 1: round to tenths (the F1-displayed seconds sliders).
        //  - Decimals = 0: round to wholes (Scan Range; also gets a wholeNumbers slider so the
        //    knob snaps natively, Fishing's Scan Range precedent).
        // Set receives the ALREADY-rounded value; every setter's own clamp matches Min..Max
        // except Eat's deliberate 15.2-vs-15 mismatch (see file header).
        private struct UguiInsectsSliderBinding
        {
            public string LabelFormat;            // LF format over the live getter value
            public float Min;
            public float Max;
            public int Decimals;                  // 1 = tenths, 0 = wholes (ignored when IsInteger)
            public bool IsInteger;
            public Func<float> Get;
            public Action<float> Set;
        }

        // Static (Logging-round precedent): every delegate targets static InsectNetFarm state,
        // nothing captures the host.
        private static UguiInsectsSliderBinding[] BuildUguiInsectsSliderBindings()
        {
            return new UguiInsectsSliderBinding[]
            {
                // InsectNetFarm.cs:282-292.
                new UguiInsectsSliderBinding
                {
                    LabelFormat = "Repair Teleport Pause: {0:F1}s", Min = 0.5f, Max = 60.2f,
                    Decimals = 1, IsInteger = false,
                    Get = () => InsectNetFarm.GetRepairTeleportPauseSeconds(),
                    Set = v => InsectNetFarm.SetRepairTeleportPauseSeconds(v)
                },
                // InsectNetFarm.cs:294-304 — slider max 15.2, setter clamp 15 (kept, see header).
                new UguiInsectsSliderBinding
                {
                    LabelFormat = "Eat Teleport Pause: {0:F1}s", Min = 0.5f, Max = 15.2f,
                    Decimals = 1, IsInteger = false,
                    Get = () => InsectNetFarm.GetEatTeleportPauseSeconds(),
                    Set = v => InsectNetFarm.SetEatTeleportPauseSeconds(v)
                },
                // InsectNetFarm.cs:306-316.
                new UguiInsectsSliderBinding
                {
                    LabelFormat = "Catch Cooldown: {0:F1}s", Min = 0.2f, Max = 10f,
                    Decimals = 1, IsInteger = false,
                    Get = () => InsectNetFarm.GetCatchCooldown(),
                    Set = v => InsectNetFarm.SetCatchCooldown(v)
                },
                // InsectNetFarm.cs:318-328.
                new UguiInsectsSliderBinding
                {
                    LabelFormat = "Scan Range: {0:F0}m", Min = 1f, Max = 100f,
                    Decimals = 0, IsInteger = false,
                    Get = () => InsectNetFarm.GetScanRange(),
                    Set = v => InsectNetFarm.SetScanRange(v)
                },
                // InsectNetFarm.cs:330-340 — the one INT slider; RoundToInt in the Set lambda is
                // belt-and-braces (the change handler already delivers an exact whole).
                new UguiInsectsSliderBinding
                {
                    LabelFormat = "Multi-Catch Limit: {0}", Min = 1f, Max = 10f,
                    Decimals = 0, IsInteger = true,
                    Get = () => InsectNetFarm.GetBatchSize(),
                    Set = v => InsectNetFarm.SetBatchSize(Mathf.RoundToInt(v))
                },
            };
        }

        // IMGUI displays the raw field through the format string (F1/F0 do the display rounding);
        // the int entry boxes an int so "{0}" renders exactly like the source's int argument.
        private string BuildUguiInsectsSliderLabelText(UguiInsectsSliderBinding binding)
        {
            float value = binding.Get();
            return binding.IsInteger
                ? this.LF(binding.LabelFormat, Mathf.RoundToInt(value))
                : this.LF(binding.LabelFormat, value);
        }

        // ----------------------------------------------------------------------------------------
        // Builder — positions replay InsectNetFarm.DrawSection's own `num +=` cursor chain, base
        // y=8 / x=16 (Fishing convention: x=16 after the 4px viewport inset sits at the IMGUI
        // drawer's visual x=20). Fixed chain, annotated with the running IMGUI cursor:
        //   8 header +28 → 36 equip +42 → 78 enabled +30 → 108 status +24 → 132 tool +24 →
        //   156 caught +24 → 180 teleport +30 → 210 pause +30 → 240 slider block, five of
        //   (label +22, slider +30) → 500 = the source's own returned cursor (DrawSection
        //   returns bare num — no bottom margin; the IMGUI tab height is a separate 980f
        //   estimate in CalculateAutoFarmTabHeight, not derived from this).
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellInsectsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellInsects = null;

            UguiShellInsectsHandle handle = new UguiShellInsectsHandle();
            GameObject block = this.CreateUguiGo("InsectsContent", parent);
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

            // InsectNetFarm.cs:238-239 — IMGUI `header` style = fontSize 14 bold.
            GameObject farmHeader = this.CreateUguiHeaderLabel(scrollContent, "FarmHeader",
                this.L("Auto Insect Farm"), 14f);
            PlaceUguiTopLeft(farmHeader, 16f, 8f, 320f, 22f);

            // InsectNetFarm.cs:241-246 — source also calls the private Log(); debug output, not
            // user-visible behavior (same for every handler below).
            GameObject equipBtn = this.CreateUguiPrimaryButton(scrollContent, "EquipNet",
                this.L("Auto Equip Net"), new System.Action(this.OnUguiInsectsEquipNetClicked));
            PlaceUguiTopLeft(equipBtn, 16f, 36f, 260f, 35f);

            // InsectNetFarm.cs:248-253.
            handle.EnabledToggle = this.CreateUguiCheckbox(scrollContent, "EnabledToggle",
                this.L("Auto Insect Farm"), InsectNetFarm.IsEnabled,
                new System.Action<bool>(this.OnUguiInsectsEnabledToggled));
            PlaceUguiTopLeft(handle.EnabledToggle.gameObject, 16f, 78f, 280f, 25f);

            // InsectNetFarm.cs:255-260 — safe to seed at build time (pure reads with their own
            // Idle/Unknown fallbacks). IMGUI `small` style = fontSize 12.
            handle.StatusShown = this.LF("Status: {0}", InsectNetFarm.GetLastStatus());
            handle.StatusLabel = this.CreateUguiBodyLabel(scrollContent, "StatusLabel", handle.StatusShown, 12f);
            PlaceUguiTopLeft(handle.StatusLabel, 16f, 108f, 360f, 20f);

            handle.ToolShown = this.LF("Tool: {0}", InsectNetFarm.GetLastToolStatus());
            handle.ToolLabel = this.CreateUguiBodyLabel(scrollContent, "ToolLabel", handle.ToolShown, 12f);
            PlaceUguiTopLeft(handle.ToolLabel, 16f, 132f, 360f, 20f);

            handle.CaughtShown = this.LF("Caught This Session: {0}", InsectNetFarm.GetSessionCatchCount());
            handle.CaughtLabel = this.CreateUguiBodyLabel(scrollContent, "CaughtLabel", handle.CaughtShown, 12f);
            PlaceUguiTopLeft(handle.CaughtLabel, 16f, 156f, 360f, 20f);

            // InsectNetFarm.cs:262-269.
            handle.TeleportToggle = this.CreateUguiCheckbox(scrollContent, "TeleportToggle",
                this.L("Teleport"), InsectNetFarm.GetTeleportEnabled(),
                new System.Action<bool>(this.OnUguiInsectsTeleportToggled));
            PlaceUguiTopLeft(handle.TeleportToggle.gameObject, 16f, 180f, 280f, 25f);

            // InsectNetFarm.cs:271-280.
            handle.PauseTriggersToggle = this.CreateUguiCheckbox(scrollContent, "PauseTriggersToggle",
                this.L("Pause Teleport On Eat / Repair"), InsectNetFarm.GetPauseTeleportOnTriggersEnabled(),
                new System.Action<bool>(this.OnUguiInsectsPauseTriggersToggled));
            PlaceUguiTopLeft(handle.PauseTriggersToggle.gameObject, 16f, 210f, 280f, 25f);

            // InsectNetFarm.cs:282-340 — the five slider blocks, one loop over the binding array.
            // The change closure captures the array element directly (not the handle, which is
            // still null until assigned below): the delegates target static InsectNetFarm state,
            // so a stale closure after a shell rebuild behaves identically anyway.
            UguiInsectsSliderBinding[] bindings = BuildUguiInsectsSliderBindings();
            handle.SliderBindings = bindings;
            float yCur = 240f;
            for (int i = 0; i < bindings.Length; i++)
            {
                int indexCopy = i; // capture a copy for the change closure
                string text = this.BuildUguiInsectsSliderLabelText(bindings[i]);
                GameObject label = this.CreateUguiBodyLabel(scrollContent, "SliderLabel" + i, text, 12f);
                PlaceUguiTopLeft(label, 16f, yCur, 320f, 20f);
                handle.SliderLabels.Add(label);
                handle.SliderShown.Add(text);
                yCur += 22f;

                bool wholeNumbers = bindings[i].IsInteger || bindings[i].Decimals == 0;
                Slider slider = this.CreateUguiSlider(scrollContent, "Slider" + i,
                    bindings[i].Min, bindings[i].Max, bindings[i].Get(), wholeNumbers,
                    new System.Action<float>(v => this.OnUguiInsectsSliderChanged(bindings[indexCopy], v)));
                PlaceUguiTopLeft(slider.gameObject, 16f, yCur, 260f, 20f);
                handle.Sliders.Add(slider);
                yCur += 30f;
            }

            // yCur = 500 here = DrawSection's own returned cursor (see builder header).
            this.SetUguiScrollContentHeight(scrollContent, yCur);

            handle.Root = block;
            this.uguiShellInsects = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellInsectsOnUpdate()
        {
            UguiShellInsectsHandle handle = this.uguiShellInsects;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellInsectsSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI/hotkey edits) — WithoutNotify only; the plain
                // setters would replay side effects (SetEnabled captures/restores the equipped
                // tool and resets sessionCatchCount on disable).
                this.SyncUguiToggleFromField(handle.EnabledToggle, InsectNetFarm.IsEnabled);
                this.SyncUguiToggleFromField(handle.TeleportToggle, InsectNetFarm.GetTeleportEnabled());
                this.SyncUguiToggleFromField(handle.PauseTriggersToggle, InsectNetFarm.GetPauseTeleportOnTriggersEnabled());

                // Status readouts — every gated frame like the IMGUI drawer (background farm
                // activity moves them, not just user edits); cached diffs limit SetText churn.
                // Caught-This-Session also resets to 0 via SetEnabled(false), which is exactly
                // why it re-syncs here rather than only on its own change.
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.LF("Status: {0}", InsectNetFarm.GetLastStatus()));
                this.SyncUguiSelfLabelText(handle.ToolLabel, ref handle.ToolShown,
                    this.LF("Tool: {0}", InsectNetFarm.GetLastToolStatus()));
                this.SyncUguiSelfLabelText(handle.CaughtLabel, ref handle.CaughtShown,
                    this.LF("Caught This Session: {0}", InsectNetFarm.GetSessionCatchCount()));

                // Slider re-syncs + value labels, one loop over the same binding array. The label
                // formats the RAW live value (an external IMGUI edit can store an unrounded
                // float; F1/F0 display it exactly as the IMGUI twin would).
                for (int i = 0; i < handle.Sliders.Count && i < handle.SliderBindings.Length; i++)
                {
                    float live = handle.SliderBindings[i].Get();
                    Slider slider = handle.Sliders[i];
                    if (slider != null && Mathf.Abs(slider.value - live) > 0.0005f)
                    {
                        slider.SetValueWithoutNotify(live);
                    }
                    string text = this.BuildUguiInsectsSliderLabelText(handle.SliderBindings[i]);
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
                ModLogger.Msg("[UguiShell] Insects content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order;
        // no notifications anywhere — the source drawer never toasts).
        // ----------------------------------------------------------------------------------------

        // InsectNetFarm.cs:241-246.
        private void OnUguiInsectsEquipNetClicked()
        {
            this.EquipHandTool(5);
        }

        // InsectNetFarm.cs:248-253 — SetEnabled MUST get the host: enabling captures the
        // previously-equipped tool, disabling restores it (and resets sessionCatchCount); a null
        // host would silently break the tool capture/restore. No save (source parity — SetEnabled
        // itself is the whole block).
        private void OnUguiInsectsEnabledToggled(bool value)
        {
            if (value == InsectNetFarm.IsEnabled)
            {
                return;
            }
            InsectNetFarm.SetEnabled(value, this);
        }

        // InsectNetFarm.cs:262-269 — the source writes the field via the drawer and saves on
        // change; SetTeleportEnabled is that field write's public equivalent (no clamp, no side
        // effects).
        private void OnUguiInsectsTeleportToggled(bool value)
        {
            if (value == InsectNetFarm.GetTeleportEnabled())
            {
                return;
            }
            InsectNetFarm.SetTeleportEnabled(value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // InsectNetFarm.cs:271-280 — the getter is the OR of the repair/eat flags and the setter
        // writes both, exactly like the IMGUI toggle's combined view.
        private void OnUguiInsectsPauseTriggersToggled(bool value)
        {
            if (value == InsectNetFarm.GetPauseTeleportOnTriggersEnabled())
            {
                return;
            }
            InsectNetFarm.SetPauseTeleportOnTriggersEnabled(value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // InsectNetFarm.cs:282-340 — shared by all five sliders. Round per the entry's rule
        // FIRST (UGUI fires continuously during a drag; rounding before the change-guard is what
        // keeps the setter+save from firing on every micro-move), then setter + save only on an
        // actual change (each source block's `if (changed) { Set...; Save; }` pattern).
        private void OnUguiInsectsSliderChanged(UguiInsectsSliderBinding binding, float value)
        {
            if (binding.Get == null || binding.Set == null)
            {
                return;
            }

            float rounded;
            if (binding.IsInteger)
            {
                // The source's own int rule (InsectNetFarm.cs:333).
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
            try { this.SaveKeybinds(false); } catch { }
        }
    }
}
