using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Resource Gathering → "Combined" sub-tab: settings + live state for
    // CombinedFarmFeature (the coordinator that lets Auto Fish / Auto Insect / Auto Bird run
    // together). Plan: docs/plans/2026-07-27-combined-farm-coordinator.md, phase 4.
    //
    // Unlike every other content file here this one has NO IMGUI ancestor to mirror — the
    // coordinator postdates the IMGUI menu — so there is no drawer to keep in sync and no cursor
    // chain to replay; the layout below is its own.
    //
    // The page has nothing to switch on: the coordinator SELF-ARMS at two or more enabled farms.
    // What it offers is the order to arbitrate in, the two hysteresis windows, the stowed-tool
    // repair switch, and a live readout of who currently holds the tool — the last one being the
    // answer to "why is my other farm doing nothing", which is otherwise only visible in the log.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellCombinedFarmHandle
        {
            public GameObject Root;
            public Toggle CoordinateToggle;
            public Toggle RepairStowedToggle;
            public Dropdown PriorityDropdown;
            public Slider EmptySlider;
            public Slider PreemptSlider;
            public GameObject EmptyLabel;
            public GameObject PreemptLabel;
            public GameObject StateLabel;
            public GameObject TargetsLabel;
            public GameObject ToolsLabel;
            public string EmptyShown = string.Empty;
            public string PreemptShown = string.Empty;
            public string StateShown = string.Empty;
            public string TargetsShown = string.Empty;
            public string ToolsShown = string.Empty;
            public int ErrorCount; // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellCombinedFarmHandle uguiShellCombinedFarm;

        private GameObject BuildUguiShellCombinedFarmContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellCombinedFarm = null;

            UguiShellCombinedFarmHandle handle = new UguiShellCombinedFarmHandle();
            GameObject block = this.CreateUguiGo("CombinedFarmContent", parent);
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

            float yCur = 8f;

            GameObject header = this.CreateUguiHeaderLabel(scrollContent, "Header", this.L("Combined Farming"), 14f);
            PlaceUguiTopLeft(header, 16f, yCur, 360f, 22f);
            yCur += 28f;

            GameObject intro = this.CreateUguiMutedLabel(scrollContent, "Intro",
                this.L("Runs automatically whenever two or more of Fishing / Insects / Birds are enabled: one farm holds the tool at a time, in priority order, until its targets run out."),
                12f);
            this.TrySetUguiLabelWrapped(intro);
            PlaceUguiTopLeft(intro, 16f, yCur, w - 48f, 46f);
            yCur += 52f;

            // ── Live state ───────────────────────────────────────────────────────────────────────
            handle.StateShown = this.LF("Now: {0}", CombinedFarmFeature.GetLiveSummary());
            handle.StateLabel = this.CreateUguiBodyLabel(scrollContent, "StateLabel", handle.StateShown, 12f);
            PlaceUguiTopLeft(handle.StateLabel, 16f, yCur, w - 48f, 20f);
            yCur += 24f;

            handle.TargetsShown = CombinedFarmFeature.GetLiveTargets();
            handle.TargetsLabel = this.CreateUguiBodyLabel(scrollContent, "TargetsLabel", handle.TargetsShown, 12f);
            PlaceUguiTopLeft(handle.TargetsLabel, 16f, yCur, w - 48f, 20f);
            yCur += 24f;

            handle.ToolsShown = CombinedFarmFeature.GetLiveToolDurabilities();
            handle.ToolsLabel = this.CreateUguiBodyLabel(scrollContent, "ToolsLabel", handle.ToolsShown, 12f);
            PlaceUguiTopLeft(handle.ToolsLabel, 16f, yCur, w - 48f, 20f);
            yCur += 30f;

            // ── Settings ─────────────────────────────────────────────────────────────────────────
            handle.CoordinateToggle = this.CreateUguiCheckbox(scrollContent, "CoordinateToggle",
                this.L("Coordinate farms"), CombinedFarmFeature.GetCoordinationEnabled(),
                new Action<bool>(this.OnUguiCombinedFarmCoordinateToggled));
            PlaceUguiTopLeft(handle.CoordinateToggle.gameObject, 16f, yCur, 320f, 25f);
            yCur += 28f;

            GameObject coordinateHint = this.CreateUguiMutedLabel(scrollContent, "CoordinateHint",
                this.L("Off: every enabled farm equips its own tool again and they fight over it — nothing catches. Escape hatch only."),
                11f);
            this.TrySetUguiLabelWrapped(coordinateHint);
            PlaceUguiTopLeft(coordinateHint, 32f, yCur, w - 64f, 32f);
            yCur += 36f;

            GameObject priorityLabel = this.CreateUguiBodyLabel(scrollContent, "PriorityLabel",
                this.L("Priority order"), 12f);
            PlaceUguiTopLeft(priorityLabel, 16f, yCur, 320f, 20f);
            yCur += 22f;

            handle.PriorityDropdown = this.CreateUguiDropdown(scrollContent, "PriorityDropdown",
                CombinedFarmFeature.GetPriorityPresetLabels(), CombinedFarmFeature.GetPriorityPresetIndex(),
                new Action<int>(this.OnUguiCombinedFarmPriorityChanged), out bool _);
            PlaceUguiTopLeft(handle.PriorityDropdown.gameObject, 16f, yCur, 260f, 28f);
            yCur += 36f;

            handle.EmptyShown = this.LF("Hand over after empty for: {0}s", CombinedFarmFeature.GetEmptySliceSeconds().ToString("F1"));
            handle.EmptyLabel = this.CreateUguiBodyLabel(scrollContent, "EmptyLabel", handle.EmptyShown, 12f);
            PlaceUguiTopLeft(handle.EmptyLabel, 16f, yCur, 360f, 20f);
            yCur += 22f;

            handle.EmptySlider = this.CreateUguiSlider(scrollContent, "EmptySlider",
                CombinedFarmFeature.GetEmptySliceSecondsMin(), CombinedFarmFeature.GetEmptySliceSecondsMax(),
                CombinedFarmFeature.GetEmptySliceSeconds(), false,
                new Action<float>(this.OnUguiCombinedFarmEmptySliceChanged));
            PlaceUguiTopLeft(handle.EmptySlider.gameObject, 16f, yCur, 260f, 20f);
            yCur += 30f;

            handle.PreemptShown = this.LF("Take over after targets for: {0}s", CombinedFarmFeature.GetPreemptConfirmSeconds().ToString("F1"));
            handle.PreemptLabel = this.CreateUguiBodyLabel(scrollContent, "PreemptLabel", handle.PreemptShown, 12f);
            PlaceUguiTopLeft(handle.PreemptLabel, 16f, yCur, 360f, 20f);
            yCur += 22f;

            handle.PreemptSlider = this.CreateUguiSlider(scrollContent, "PreemptSlider",
                CombinedFarmFeature.GetPreemptConfirmSecondsMin(), CombinedFarmFeature.GetPreemptConfirmSecondsMax(),
                CombinedFarmFeature.GetPreemptConfirmSeconds(), false,
                new Action<float>(this.OnUguiCombinedFarmPreemptChanged));
            PlaceUguiTopLeft(handle.PreemptSlider.gameObject, 16f, yCur, 260f, 20f);
            yCur += 30f;

            handle.RepairStowedToggle = this.CreateUguiCheckbox(scrollContent, "RepairStowedToggle",
                this.L("Repair stowed tools"), CombinedFarmFeature.GetRepairStowedToolsEnabled(),
                new Action<bool>(this.OnUguiCombinedFarmRepairStowedToggled));
            PlaceUguiTopLeft(handle.RepairStowedToggle.gameObject, 16f, yCur, 320f, 25f);
            yCur += 28f;

            GameObject repairHint = this.CreateUguiMutedLabel(scrollContent, "RepairHint",
                this.L("Pauses all farms and repairs each worn tool in turn, standing still. Needs Auto Repair on Durability; the repair aura only ever fixes the tool in hand."),
                11f);
            this.TrySetUguiLabelWrapped(repairHint);
            PlaceUguiTopLeft(repairHint, 32f, yCur, w - 64f, 32f);
            yCur += 40f;

            this.SetUguiScrollContentHeight(scrollContent, yCur);

            handle.Root = block;
            this.uguiShellCombinedFarm = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — the setters are plain field writes with their own clamps; the debounced
        // save mirrors what the other farm pages do (SaveAllSettings on change, no toast).
        // ----------------------------------------------------------------------------------------

        private void OnUguiCombinedFarmCoordinateToggled(bool value)
        {
            CombinedFarmFeature.SetCoordinationEnabled(value);
            this.SaveAllSettings();
        }

        private void OnUguiCombinedFarmRepairStowedToggled(bool value)
        {
            CombinedFarmFeature.SetRepairStowedToolsEnabled(value);
            this.SaveAllSettings();
        }

        private void OnUguiCombinedFarmPriorityChanged(int index)
        {
            CombinedFarmFeature.SetPriorityPresetIndex(index);
            this.SaveAllSettings();
        }

        private void OnUguiCombinedFarmEmptySliceChanged(float value)
        {
            CombinedFarmFeature.SetEmptySliceSeconds(value);
        }

        private void OnUguiCombinedFarmPreemptChanged(float value)
        {
            CombinedFarmFeature.SetPreemptConfirmSeconds(value);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellCombinedFarmOnUpdate()
        {
            UguiShellCombinedFarmHandle handle = this.uguiShellCombinedFarm;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellCombinedSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.CoordinateToggle, CombinedFarmFeature.GetCoordinationEnabled());
                this.SyncUguiToggleFromField(handle.RepairStowedToggle, CombinedFarmFeature.GetRepairStowedToolsEnabled());

                // The three live lines move on their own (the coordinator switches slices and the
                // census refreshes in the background), so they re-sync every gated frame; the cached
                // diff keeps SetText churn down.
                this.SyncUguiSelfLabelText(handle.StateLabel, ref handle.StateShown,
                    this.LF("Now: {0}", CombinedFarmFeature.GetLiveSummary()));
                this.SyncUguiSelfLabelText(handle.TargetsLabel, ref handle.TargetsShown,
                    CombinedFarmFeature.GetLiveTargets());
                this.SyncUguiSelfLabelText(handle.ToolsLabel, ref handle.ToolsShown,
                    CombinedFarmFeature.GetLiveToolDurabilities());

                if (handle.EmptySlider != null)
                {
                    float live = CombinedFarmFeature.GetEmptySliceSeconds();
                    if (Math.Abs(handle.EmptySlider.value - live) > 0.001f)
                    {
                        handle.EmptySlider.SetValueWithoutNotify(live);
                    }
                    this.SyncUguiSelfLabelText(handle.EmptyLabel, ref handle.EmptyShown,
                        this.LF("Hand over after empty for: {0}s", live.ToString("F1")));
                }

                if (handle.PreemptSlider != null)
                {
                    float live = CombinedFarmFeature.GetPreemptConfirmSeconds();
                    if (Math.Abs(handle.PreemptSlider.value - live) > 0.001f)
                    {
                        handle.PreemptSlider.SetValueWithoutNotify(live);
                    }
                    this.SyncUguiSelfLabelText(handle.PreemptLabel, ref handle.PreemptShown,
                        this.LF("Take over after targets for: {0}s", live.ToString("F1")));
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Combined Farm content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }
    }
}
