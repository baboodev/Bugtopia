using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Self → Minimap (new page, no IMGUI twin: the IMGUI menu is retired).
    //
    // UI for MiniMapZoomFeature.cs. Same ground rules as the other Self pages (see the
    // HeartopiaComplete.UguiSelfContent.cs header): built once, wired by static display index
    // (UguiShellSelfMiniMapSubIndex), kit checkboxes, per-frame processor gated on "shell visible AND
    // Self tab AND this sub-tab", value sync via SetValueWithoutNotify only, status line on the 0.5 s
    // slow tick. UI callbacks only set fields and save — the feature tick owns every Mono call.
    //
    // The auto-zoom controls stay visible while their checkbox is off (the checkbox gates APPLY, not
    // visibility), matching the Game LOD page — no conditional relayout.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellSelfMiniMapHandle
        {
            public GameObject Root;
            public Toggle EnabledToggle;
            public GameObject RestLabel;
            public string RestShown;
            public Slider RestSlider;
            public Toggle AutoToggle;
            public GameObject TopLabel;
            public string TopShown;
            public Slider TopSlider;
            public Dropdown ReactionDropdown;
            public bool ReactionListenerWired;
            public int ReactionLastValue;          // poll-fallback change detection
            public GameObject StatusLabel;
            public string StatusShown;
            public float NextSlowSyncAt;
            public int ErrorCount;
        }

        private UguiShellSelfMiniMapHandle uguiShellSelfMiniMap;

        private string BuildUguiSelfMiniMapRestLabelText()
        {
            return this.miniMapAutoZoomEnabled
                ? this.LF("Zoom at rest: {0:F2}x", this.miniMapZoomRest)
                : this.LF("Zoom: {0:F2}x", this.miniMapZoomRest);
        }

        private string BuildUguiSelfMiniMapTopLabelText()
        {
            return this.LF("Zoom at top speed: {0:F2}x", this.miniMapZoomTop);
        }

        private string BuildUguiSelfMiniMapStatusText()
        {
            return this.L("Scales the HUD minimap (both the normal and the vehicle HUD). Above 1x is closer. Auto-zoom widens the view as you speed up and zooms back in after you stop; top speed is the current car's maximum.")
                + (this.miniMapZoomEnabled ? " Status: " + this.miniMapZoomStatus : string.Empty);
        }

        private GameObject BuildUguiShellSelfMiniMapContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfMiniMap = null;

            UguiShellSelfMiniMapHandle handle = new UguiShellSelfMiniMapHandle();
            GameObject block = this.CreateUguiGo("SelfMiniMapContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            const float labelW = 220f; // longest label: "Zoom at top speed: 0.60x"
            float rowW = w - pad * 2f;
            float sliderX = pad + labelW + 10f;
            float sliderW = w - sliderX - pad;
            Color muted = this.UguiKitMutedColor();
            float yCur = 12f;

            handle.EnabledToggle = this.CreateUguiCheckbox(block.transform, "EnabledToggle",
                this.L("Minimap zoom"), this.miniMapZoomEnabled,
                new System.Action<bool>(this.OnUguiSelfMiniMapEnabledToggled));
            PlaceUguiTopLeft(handle.EnabledToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 32f;

            handle.RestShown = this.BuildUguiSelfMiniMapRestLabelText();
            handle.RestLabel = this.CreateUguiBodyLabel(block.transform, "RestLabel", handle.RestShown, 13f);
            PlaceUguiTopLeft(handle.RestLabel, pad, yCur + 2f, labelW, 20f);
            handle.RestSlider = this.CreateUguiSlider(block.transform, "RestSlider",
                MiniMapZoomMin, MiniMapZoomMax, this.miniMapZoomRest, false,
                new System.Action<float>(this.OnUguiSelfMiniMapRestChanged));
            PlaceUguiTopLeft(handle.RestSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 40f;

            handle.AutoToggle = this.CreateUguiCheckbox(block.transform, "AutoToggle",
                this.L("Auto-zoom by speed"), this.miniMapAutoZoomEnabled,
                new System.Action<bool>(this.OnUguiSelfMiniMapAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 32f;

            handle.TopShown = this.BuildUguiSelfMiniMapTopLabelText();
            handle.TopLabel = this.CreateUguiBodyLabel(block.transform, "TopLabel", handle.TopShown, 13f);
            PlaceUguiTopLeft(handle.TopLabel, pad, yCur + 2f, labelW, 20f);
            handle.TopSlider = this.CreateUguiSlider(block.transform, "TopSlider",
                MiniMapZoomTopMin, MiniMapZoomTopMax, this.miniMapZoomTop, false,
                new System.Action<float>(this.OnUguiSelfMiniMapTopChanged));
            PlaceUguiTopLeft(handle.TopSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 32f;

            GameObject reactionLabel = this.CreateUguiBodyLabel(block.transform, "ReactionLabel",
                this.L("Reaction"), 13f);
            PlaceUguiTopLeft(reactionLabel, pad, yCur + 4f, labelW, 20f);
            string[] reactionNames = new string[MiniMapZoomReactionNames.Length];
            for (int i = 0; i < reactionNames.Length; i++)
            {
                reactionNames[i] = this.L(MiniMapZoomReactionNames[i]);
            }
            int reactionInitial = Mathf.Clamp(this.miniMapZoomReaction, 0, reactionNames.Length - 1);
            handle.ReactionLastValue = reactionInitial;
            bool reactionWired;
            handle.ReactionDropdown = this.CreateUguiDropdown(block.transform, "ReactionDropdown",
                reactionNames, reactionInitial,
                new System.Action<int>(this.OnUguiSelfMiniMapReactionPicked), out reactionWired);
            handle.ReactionListenerWired = reactionWired;
            PlaceUguiTopLeft(handle.ReactionDropdown.gameObject, sliderX, yCur, 180f, 28f);
            yCur += 44f;

            handle.StatusShown = this.BuildUguiSelfMiniMapStatusText();
            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status",
                handle.StatusShown, 11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, pad, yCur, rowW, 80f);

            handle.Root = block;
            this.uguiShellSelfMiniMap = handle;
            return block;
        }

        private void ProcessUguiShellSelfMiniMapOnUpdate()
        {
            UguiShellSelfMiniMapHandle handle = this.uguiShellSelfMiniMap;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfMiniMapSubIndex))
            {
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure (Birds
                // precedent). Runs BEFORE the re-sync below so a user pick lands first.
                if (!handle.ReactionListenerWired && handle.ReactionDropdown != null)
                {
                    int picked = handle.ReactionDropdown.value;
                    if (picked != handle.ReactionLastValue)
                    {
                        this.OnUguiSelfMiniMapReactionPicked(picked);
                    }
                }

                this.SyncUguiToggleFromField(handle.EnabledToggle, this.miniMapZoomEnabled);
                this.SyncUguiToggleFromField(handle.AutoToggle, this.miniMapAutoZoomEnabled);

                if (handle.RestSlider != null && Mathf.Abs(handle.RestSlider.value - this.miniMapZoomRest) > 0.0005f)
                {
                    handle.RestSlider.SetValueWithoutNotify(this.miniMapZoomRest);
                }
                if (handle.TopSlider != null && Mathf.Abs(handle.TopSlider.value - this.miniMapZoomTop) > 0.0005f)
                {
                    handle.TopSlider.SetValueWithoutNotify(this.miniMapZoomTop);
                }
                if (handle.ReactionDropdown != null)
                {
                    int want = Mathf.Clamp(this.miniMapZoomReaction, 0, MiniMapZoomReactionNames.Length - 1);
                    if (handle.ReactionDropdown.value != want)
                    {
                        handle.ReactionDropdown.SetValueWithoutNotify(want);
                        handle.ReactionLastValue = want;
                    }
                }

                this.SyncUguiSelfLabelText(handle.RestLabel, ref handle.RestShown, this.BuildUguiSelfMiniMapRestLabelText());
                this.SyncUguiSelfLabelText(handle.TopLabel, ref handle.TopShown, this.BuildUguiSelfMiniMapTopLabelText());

                // The status suffix (live speed and zoom) comes from the feature tick.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                        this.BuildUguiSelfMiniMapStatusText());
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Minimap content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Save only — ProcessMiniMapZoomOnUpdate applies and restores.
        private void OnUguiSelfMiniMapEnabledToggled(bool value)
        {
            if (value == this.miniMapZoomEnabled)
            {
                return;
            }
            this.miniMapZoomEnabled = value;
            this.AddMenuNotification(
                this.miniMapZoomEnabled ? this.L("Minimap zoom on") : this.L("Minimap zoom off"),
                new Color(0.45f, 0.85f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfMiniMapAutoToggled(bool value)
        {
            if (value == this.miniMapAutoZoomEnabled)
            {
                return;
            }
            this.miniMapAutoZoomEnabled = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // 0.05x steps.
        private void OnUguiSelfMiniMapRestChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MiniMapZoomMin, MiniMapZoomMax);
            if (Mathf.Abs(rounded - this.miniMapZoomRest) <= 0.0001f)
            {
                return;
            }
            this.miniMapZoomRest = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfMiniMapTopChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MiniMapZoomTopMin, MiniMapZoomTopMax);
            if (Mathf.Abs(rounded - this.miniMapZoomTop) <= 0.0001f)
            {
                return;
            }
            this.miniMapZoomTop = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfMiniMapReactionPicked(int index)
        {
            UguiShellSelfMiniMapHandle handle = this.uguiShellSelfMiniMap;
            if (handle != null)
            {
                handle.ReactionLastValue = index;
            }

            int clamped = Mathf.Clamp(index, 0, MiniMapZoomReactionNames.Length - 1);
            if (clamped == this.miniMapZoomReaction)
            {
                return;
            }
            this.miniMapZoomReaction = clamped;
            try { this.SaveKeybinds(false); } catch { }
        }
    }
}
