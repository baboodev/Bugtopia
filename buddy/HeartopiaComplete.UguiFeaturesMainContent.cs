using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 1 of 8 (migration plan:
    // cosmic-waddling-rainbow.md, item 11): the MAIN sub-tab only — the flat toggle/slider list
    // from DrawAutomationTab's automationSubTab == 0 branch (HeartopiaComplete.Gui.cs:438-582).
    // The other seven Features sub-tabs (Food & Repair, Snow Sculpting, Auto Buy, Auto Sell,
    // Mass Cook, Puzzle, Pet Care) are separate future rounds and are untouched — their cells
    // stay shell placeholders, gated by the IsUguiShellFeaturesSubTabActive function this round
    // establishes (the tab's shell wiring anchor, like Foraging was for Resource Gathering).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer (DrawAutomationTab) and every backend method it calls stay fully
    //    functional and untouched — this file only READS the same fields and CALLS the same
    //    methods. Two independent rendering paths over one backend. Zero interop additions:
    //    every field/method here is already this.-accessible partial-class state.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesMainSubIndex = 0 — declared next to their siblings in
    //    UguiPhase3Content.cs), never by localized label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Layout notes vs the IMGUI source:
    //  - The source is a FLAT list — no DrawExentriSectionPanel cards, just wrapped switch
    //    toggles straight on the tab background — so this block is flat rows in a transparent
    //    scroll view (Self→Main shape), no CreateUguiSettingsMainPanel chrome.
    //  - IMGUI uses DrawWrappedSwitchToggle + GetSwitchToggleHeight because its column is 260px
    //    (label area = 260-60 = 200px at font 14 — "Hide Jump Button (Space still works)" and
    //    "Keep HUD in fishing/vehicle modes" genuinely wrap there, so each toggle's HEIGHT
    //    varies per label). The shell rows are 446px (checkbox label area ~414px at the same
    //    font size): all 8 toggle labels fit one line, so fixed 24px rows suffice — the same
    //    conclusion Self→Main documented for its three DrawWrappedSwitchToggle uses. No dynamic
    //    height measurement needed. Both IMGUI switch variants localize their label internally
    //    (GUI.Label this.L(label)), so every checkbox label here goes through this.L too;
    //    DrawDangerActionButton also localizes, so DISABLE ALL does as well. The two slider
    //    value labels are raw GUI.Labels in the source (no L) — kept verbatim-unlocalized,
    //    including the "Collect radius: unlimited" literal-string special case at <= 0.01f.
    //  - The 2 conditional blocks (bubble-rate row while Fast Bubble Gen is on, collect-radius
    //    row while Auto Collect Bubbles is on) reposition via relayout-on-signature (Foraging's
    //    conditional-block idiom). IMGUI stacks label above slider purely for the narrow column;
    //    the shell uses the established side-by-side label+slider row (Self→Main/Foraging).
    //
    // Cross-surface sync: every control here is also editable from the still-live IMGUI twin, so
    // a per-frame processor — gated on IsUguiShellFeaturesSubTabActive(Main) — re-syncs toggles
    // via SetIsOnWithoutNotify (SyncUguiToggleFromField) and sliders via SetValueWithoutNotify,
    // NEVER the plain setters (those fire onValueChanged and replay side effects). Value labels
    // refresh through the cached-string compare (SyncUguiSelfLabelText). Everything is cheap
    // bool/float compares — no 0.5s slow tick needed (nothing here is fed by background hooks).
    //
    // Sliders are wholeNumbers = FALSE on purpose: the IMGUI twin's DrawAccentSlider calls use
    // default integerSteps = false and store the raw clamped float (only the LABEL formats F0),
    // so a whole-number UGUI slider would diverge from the shared backing value.
    //
    // DISABLE ALL (Gui.cs:554-579) is deliberately a CROSS-FEATURE kill switch: it resets state
    // spanning many other tabs (analog move bridge, anti-AFK, auto cook/eat/sell, aura farm,
    // cooking cleanup, game speed, custom FOV, noclip + its boost/vehicle override), not just
    // this sub-tab's own toggles. The handler reproduces the source sequence verbatim, in
    // order. Not extracted into a shared method: the IMGUI drawer must stay untouched, so
    // extraction could never dedupe the IMGUI copy — it would just move this handler's body.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellFeaturesMainHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // scroll content width (block w minus viewport insets)

            public Toggle BypassToggle;           // "Hide UI + Player (Client Side)" — flag only
            public Toggle HideJumpToggle;
            public Toggle PersistentHudToggle;
            public Toggle BunnyHopToggle;
            public Toggle BirdVacuumToggle;       // flag only (source has no save/notification)
            public Toggle FastBubbleGenToggle;

            public GameObject BubbleRateLabel;    // conditional row (fastBubbleGenEnabled)
            public string BubbleRateShown;
            public Slider BubbleRateSlider;

            public Toggle BubbleSpawnAtPlayerToggle;
            public Toggle AutoBubbleCollectToggle;

            public GameObject CollectRadiusLabel; // conditional row (autoBubbleCollectEnabled)
            public string CollectRadiusShown;
            public Slider CollectRadiusSlider;

            public GameObject DisableAllButton;

            public int LayoutSignature = -1;      // packed fastBubbleGen/autoBubbleCollect
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesMainHandle uguiShellFeaturesMain;

        // ----------------------------------------------------------------------------------------
        // Shared gate: is the shell showing a specific Features sub-tab right now?
        // (Foraging's IsUguiShellResourceGatheringSubTabActive shape, pointed at this tab —
        // future Features rounds gate their processors on this same function.)
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellFeaturesSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellFeaturesTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellFeaturesTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellFeaturesTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Display builders — IMGUI strings verbatim (both are raw unlocalized GUI.Labels)
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFeaturesMainBubbleRateText()
        {
            // Gui.cs:498.
            return string.Format("Bubbles per minute: {0:F0}", this.bubbleBubblesPerMinute);
        }

        private string BuildUguiFeaturesMainCollectRadiusText()
        {
            // Gui.cs:539-542 — the LITERAL STRING "unlimited" at <= 0.01f, else "{0:F0} m".
            string collectRadiusText = this.autoBubbleCollectRadius <= 0.01f
                ? "unlimited"
                : string.Format("{0:F0} m", this.autoBubbleCollectRadius);
            return "Collect radius: " + collectRadiusText;
        }

        private int ComputeUguiFeaturesMainLayoutSignature()
        {
            return (this.fastBubbleGenEnabled ? 1 : 0)
                 | (this.autoBubbleCollectEnabled ? 2 : 0);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAutomationTab's Main branch: 8 toggles + 2 conditional slider rows +
        // the DISABLE ALL danger button, flat in a transparent scroll view. All controls —
        // including conditionally-visible ones — are built ONCE here in IMGUI source order;
        // RelayoutUguiShellFeaturesMain owns the conditional positions/visibility (the y-cursor
        // accumulation analog). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesMainContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesMain = null;

            UguiShellFeaturesMainHandle handle = new UguiShellFeaturesMainHandle();
            GameObject block = this.CreateUguiGo("FeaturesMainContent", parent);
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
            handle.ContentWidth = w - 22f; // viewport insets: 4 left + 18 right

            // Controls in IMGUI source order (Gui.cs:442-554); positions/visibility belong to
            // the relayout, called once below.
            handle.BypassToggle = this.CreateUguiCheckbox(scrollContent, "BypassToggle",
                this.L("Hide UI + Player (Client Side)"), this.bypassEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainBypassToggled));
            handle.HideJumpToggle = this.CreateUguiCheckbox(scrollContent, "HideJumpToggle",
                this.L("Hide Jump Button (Space still works)"), this.hideJumpButtonEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainHideJumpToggled));
            handle.PersistentHudToggle = this.CreateUguiCheckbox(scrollContent, "PersistentHudToggle",
                this.L("Keep HUD in fishing/vehicle modes"), this.persistentHudEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainPersistentHudToggled));
            handle.BunnyHopToggle = this.CreateUguiCheckbox(scrollContent, "BunnyHopToggle",
                this.L("Bunny Hop (hold Space)"), this.bunnyHopEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainBunnyHopToggled));
            handle.BirdVacuumToggle = this.CreateUguiCheckbox(scrollContent, "BirdVacuumToggle",
                this.L("Bird Vacuum (Client Side)"), this.birdVacuumEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainBirdVacuumToggled));
            handle.FastBubbleGenToggle = this.CreateUguiCheckbox(scrollContent, "FastBubbleGenToggle",
                this.L("Fast Bubble Gen"), this.fastBubbleGenEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainFastBubbleGenToggled));

            handle.BubbleRateShown = this.BuildUguiFeaturesMainBubbleRateText();
            handle.BubbleRateLabel = this.CreateUguiBodyLabel(scrollContent, "BubbleRateLabel",
                handle.BubbleRateShown, 13f);
            handle.BubbleRateSlider = this.CreateUguiSlider(scrollContent, "BubbleRateSlider",
                0f, 100f, this.bubbleBubblesPerMinute, false,
                new System.Action<float>(this.OnUguiFeaturesMainBubbleRateChanged));

            handle.BubbleSpawnAtPlayerToggle = this.CreateUguiCheckbox(scrollContent, "BubbleSpawnAtPlayerToggle",
                this.L("Bubbles Spawn At Player"), this.bubbleSpawnAtPlayerEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainBubbleSpawnAtPlayerToggled));
            handle.AutoBubbleCollectToggle = this.CreateUguiCheckbox(scrollContent, "AutoBubbleCollectToggle",
                this.L("Auto Collect Bubbles"), this.autoBubbleCollectEnabled,
                new System.Action<bool>(this.OnUguiFeaturesMainAutoBubbleCollectToggled));

            handle.CollectRadiusShown = this.BuildUguiFeaturesMainCollectRadiusText();
            handle.CollectRadiusLabel = this.CreateUguiBodyLabel(scrollContent, "CollectRadiusLabel",
                handle.CollectRadiusShown, 13f);
            handle.CollectRadiusSlider = this.CreateUguiSlider(scrollContent, "CollectRadiusSlider",
                0f, 100f, this.autoBubbleCollectRadius, false,
                new System.Action<float>(this.OnUguiFeaturesMainCollectRadiusChanged));

            handle.DisableAllButton = this.CreateUguiDangerButton(scrollContent, "DisableAllButton",
                this.L("DISABLE ALL"),
                new System.Action(this.OnUguiFeaturesMainDisableAllClicked));

            handle.LayoutSignature = this.ComputeUguiFeaturesMainLayoutSignature();
            this.RelayoutUguiShellFeaturesMain(handle);

            handle.Root = block;
            this.uguiShellFeaturesMain = handle;
            return block;
        }

        // Positions everything from the CURRENT fastBubbleGen/autoBubbleCollect state — the UGUI
        // analog of the IMGUI drawer's num accumulation (Gui.cs:443-580).
        // Reposition/SetActive/resize only; nothing is rebuilt.
        private void RelayoutUguiShellFeaturesMain(UguiShellFeaturesMainHandle handle)
        {
            bool fastBubbleGen = this.fastBubbleGenEnabled;
            bool autoBubbleCollect = this.autoBubbleCollectEnabled;

            const float rowX = 8f;
            const float labelW = 200f;
            float rowW = handle.ContentWidth - 16f;
            float sliderX = rowX + labelW + 10f;
            float sliderW = handle.ContentWidth - sliderX - 8f;
            float yCur = 8f;

            if (handle.BypassToggle != null)
            {
                PlaceUguiTopLeft(handle.BypassToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.HideJumpToggle != null)
            {
                PlaceUguiTopLeft(handle.HideJumpToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.PersistentHudToggle != null)
            {
                PlaceUguiTopLeft(handle.PersistentHudToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.BunnyHopToggle != null)
            {
                PlaceUguiTopLeft(handle.BunnyHopToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.BirdVacuumToggle != null)
            {
                PlaceUguiTopLeft(handle.BirdVacuumToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.FastBubbleGenToggle != null)
            {
                PlaceUguiTopLeft(handle.FastBubbleGenToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.BubbleRateLabel, fastBubbleGen);
            SetUguiGoActive(handle.BubbleRateSlider != null ? handle.BubbleRateSlider.gameObject : null, fastBubbleGen);
            if (fastBubbleGen)
            {
                if (handle.BubbleRateLabel != null)
                {
                    PlaceUguiTopLeft(handle.BubbleRateLabel, rowX, yCur + 2f, labelW, 20f);
                }
                if (handle.BubbleRateSlider != null)
                {
                    PlaceUguiTopLeft(handle.BubbleRateSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                }
                yCur += 28f;
            }

            if (handle.BubbleSpawnAtPlayerToggle != null)
            {
                PlaceUguiTopLeft(handle.BubbleSpawnAtPlayerToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.AutoBubbleCollectToggle != null)
            {
                PlaceUguiTopLeft(handle.AutoBubbleCollectToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.CollectRadiusLabel, autoBubbleCollect);
            SetUguiGoActive(handle.CollectRadiusSlider != null ? handle.CollectRadiusSlider.gameObject : null, autoBubbleCollect);
            if (autoBubbleCollect)
            {
                if (handle.CollectRadiusLabel != null)
                {
                    PlaceUguiTopLeft(handle.CollectRadiusLabel, rowX, yCur + 2f, labelW, 20f);
                }
                if (handle.CollectRadiusSlider != null)
                {
                    PlaceUguiTopLeft(handle.CollectRadiusSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                }
                yCur += 28f;
            }

            // IMGUI: 260x35 danger button (Gui.cs:554), then num += 45.
            if (handle.DisableAllButton != null)
            {
                PlaceUguiTopLeft(handle.DisableAllButton, rowX, yCur + 4f, 260f, 35f);
            }
            yCur += 4f + 35f;

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 16f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesMainOnUpdate()
        {
            UguiShellFeaturesMainHandle handle = this.uguiShellFeaturesMain;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesMainSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI edits — incl. DISABLE ALL from the twin) —
                // WithoutNotify only.
                this.SyncUguiToggleFromField(handle.BypassToggle, this.bypassEnabled);
                this.SyncUguiToggleFromField(handle.HideJumpToggle, this.hideJumpButtonEnabled);
                this.SyncUguiToggleFromField(handle.PersistentHudToggle, this.persistentHudEnabled);
                this.SyncUguiToggleFromField(handle.BunnyHopToggle, this.bunnyHopEnabled);
                this.SyncUguiToggleFromField(handle.BirdVacuumToggle, this.birdVacuumEnabled);
                this.SyncUguiToggleFromField(handle.FastBubbleGenToggle, this.fastBubbleGenEnabled);
                this.SyncUguiToggleFromField(handle.BubbleSpawnAtPlayerToggle, this.bubbleSpawnAtPlayerEnabled);
                this.SyncUguiToggleFromField(handle.AutoBubbleCollectToggle, this.autoBubbleCollectEnabled);

                if (handle.BubbleRateSlider != null
                    && Mathf.Abs(handle.BubbleRateSlider.value - this.bubbleBubblesPerMinute) > 0.0005f)
                {
                    handle.BubbleRateSlider.SetValueWithoutNotify(this.bubbleBubblesPerMinute);
                }
                this.SyncUguiSelfLabelText(handle.BubbleRateLabel, ref handle.BubbleRateShown,
                    this.BuildUguiFeaturesMainBubbleRateText());
                if (handle.CollectRadiusSlider != null
                    && Mathf.Abs(handle.CollectRadiusSlider.value - this.autoBubbleCollectRadius) > 0.0005f)
                {
                    handle.CollectRadiusSlider.SetValueWithoutNotify(this.autoBubbleCollectRadius);
                }
                this.SyncUguiSelfLabelText(handle.CollectRadiusLabel, ref handle.CollectRadiusShown,
                    this.BuildUguiFeaturesMainCollectRadiusText());

                // Conditional-layout signature (bubble-rate row + collect-radius row).
                int signature = this.ComputeUguiFeaturesMainLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesMain(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Main content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // The equal-guard is the UGUI analog of IMGUI's prev-vs-new change check.
        // ----------------------------------------------------------------------------------------

        // Gui.cs:442 — flag only: no save, no notification.
        private void OnUguiFeaturesMainBypassToggled(bool value)
        {
            this.bypassEnabled = value;
        }

        // Gui.cs:444-451.
        private void OnUguiFeaturesMainHideJumpToggled(bool value)
        {
            if (value == this.hideJumpButtonEnabled)
            {
                return;
            }
            this.hideJumpButtonEnabled = value;
            this.cachedJumpButtonGo = null;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:453-466 — enabling prompts an immediate poll; the notification fires on BOTH
        // directions with the one shared color.
        private void OnUguiFeaturesMainPersistentHudToggled(bool value)
        {
            if (value == this.persistentHudEnabled)
            {
                return;
            }
            this.persistentHudEnabled = value;
            if (this.persistentHudEnabled)
            {
                // Prompt poll so enabling mid-fishing/driving restores the HUD right away.
                this.persistentHudNextPollAt = 0f;
            }
            this.AddMenuNotification(this.persistentHudEnabled ? "Persistent HUD on" : "Persistent HUD off",
                new Color(0.45f, 0.88f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:468-479 — state reset on turn-OFF only.
        private void OnUguiFeaturesMainBunnyHopToggled(bool value)
        {
            if (value == this.bunnyHopEnabled)
            {
                return;
            }
            this.bunnyHopEnabled = value;
            if (!this.bunnyHopEnabled)
            {
                this.ResetBunnyHopState();
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:481-482 — flag only: the source genuinely has no save and no notification.
        private void OnUguiFeaturesMainBirdVacuumToggled(bool value)
        {
            this.birdVacuumEnabled = value;
        }

        // Gui.cs:485-493 — accumulator reset + immediate retry on ANY change (both directions).
        private void OnUguiFeaturesMainFastBubbleGenToggled(bool value)
        {
            if (value == this.fastBubbleGenEnabled)
            {
                return;
            }
            this.fastBubbleGenEnabled = value;
            this.bubbleSpawnRateAccumulator = 0f;
            this.RequestBubbleFeatureImmediateRetry();
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:500-507 — same 0.01f change threshold as the IMGUI block; clamp-write + reset.
        private void OnUguiFeaturesMainBubbleRateChanged(float value)
        {
            if (Math.Abs(value - this.bubbleBubblesPerMinute) <= 0.01f)
            {
                return;
            }
            this.bubbleBubblesPerMinute = Mathf.Clamp(value, 0f, 100f);
            this.bubbleSpawnRateAccumulator = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:511-521 — immediate rewrite install on turn-ON only.
        private void OnUguiFeaturesMainBubbleSpawnAtPlayerToggled(bool value)
        {
            if (value == this.bubbleSpawnAtPlayerEnabled)
            {
                return;
            }
            this.bubbleSpawnAtPlayerEnabled = value;
            if (this.bubbleSpawnAtPlayerEnabled)
            {
                this.RequestBubbleSpawnRewriteImmediateInstall();
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:524-534 — immediate collect install on turn-ON only.
        private void OnUguiFeaturesMainAutoBubbleCollectToggled(bool value)
        {
            if (value == this.autoBubbleCollectEnabled)
            {
                return;
            }
            this.autoBubbleCollectEnabled = value;
            if (this.autoBubbleCollectEnabled)
            {
                this.RequestAutoBubbleCollectImmediateInstall();
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:544-550 — clamp-write + save only (no accumulator reset here).
        private void OnUguiFeaturesMainCollectRadiusChanged(float value)
        {
            if (Math.Abs(value - this.autoBubbleCollectRadius) <= 0.01f)
            {
                return;
            }
            this.autoBubbleCollectRadius = Mathf.Clamp(value, 0f, 100f);
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:554-579 — the EXACT cross-feature reset sequence, verbatim and in source order
        // (see file header: this spans many other tabs' features on purpose).
        private void OnUguiFeaturesMainDisableAllClicked()
        {
            this.bypassEnabled = false;
            this.hideJumpButtonEnabled = false;
            this.cachedJumpButtonGo = null;
            this.persistentHudEnabled = false;
            this.bunnyHopEnabled = false;
            this.ResetBunnyHopState();
            this.analogMoveBridgeEnabled = false;
            this.ReleaseMovementBridgeIfInjecting();
            this.birdVacuumEnabled = false;
            this.antiAfkEnabled = false;
            this.StopAutoCookInternal("Disabled");
            this.isAutoEating = false;
            this.autoSellEnabled = false;
            this.SetAuraFarmEnabled(false);
            this.cookingCleanupMode = false;
            this.SetGameSpeed(1f);
            this.customCameraFOVEnabled = false;
            this.cameraFOV = 60f;
            this.noclipEnabled = false;
            this.ClearNoclipVehicleOverride();
            this.noclipBoostMultiplier = 2f;
            this.RestoreCameraFOV();
        }
    }
}
