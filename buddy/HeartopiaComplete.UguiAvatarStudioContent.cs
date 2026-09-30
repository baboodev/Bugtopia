using System;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // AVATAR STUDIO WINDOW — the floating controls for the game's "Generate Avatar" panel.
    //
    // Shows itself while that panel is open and hides with it (AvatarStudioFeature.cs owns the
    // detection); the × closes it until the panel is opened again. Nothing is added to the mod menu.
    //
    // Built on the kit's window factory like the Action Panel, so drag, theme, click-blocking and
    // the shared UI scale come for free. It starts on the right-hand half of the screen: the capture
    // circle is on the left, and although the window hides itself for the capture, it should not
    // sit over the model while the player is framing the shot.
    //
    // The two lists are filled AFTER the window exists — the pose catalogue needs the game's tables
    // and the face list needs the model, which loads after the panel opens — so both dropdowns are
    // created with a placeholder row and refilled in place; the popup height is then re-derived by
    // hand, because the kit sizes the template from the option count it was created with.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float UguiAvatarStudioWidth = 300f;
        private const float UguiAvatarStudioPadX = 10f;
        private const float UguiAvatarStudioTitleH = 22f;
        private const float UguiAvatarStudioRowH = 26f;
        private const float UguiAvatarStudioSliderH = 20f;
        private const float UguiAvatarStudioLabelH = 16f;
        private const float UguiAvatarStudioDropdownItemH = 28f;

        // Same rung as the Action Panel: over the game, under the shell and under Dropdown popups
        // (30000, see the kit header).
        private const int UguiAvatarStudioSortingOrder = 29372;

        private sealed class UguiAvatarStudioHandle
        {
            public UguiWindowHandle Window;
            public float LastSyncedUiScale = -1f;
            public int ErrorCount;
            public bool Syncing;   // true while code (not the user) moves a control

            public Dropdown PoseDropdown;
            public bool PoseListenerWired;
            public int PoseLastValue;
            public int PoseOptionCount;

            public Dropdown FaceDropdown;
            public bool FaceListenerWired;
            public int FaceLastValue;
            public int FaceOptionCount;

            public Dropdown BackgroundDropdown;
            public bool BackgroundListenerWired;
            public int BackgroundLastValue;
            public int BackgroundListVersion = -1;

            public GameObject PlayButton;
            public string PlayShown;

            public Slider FrameSlider;
            public Slider ZoomSlider;
            public Slider YawSlider;
            public Slider PanXSlider;
            public Slider PanYSlider;

            public GameObject FrameLabel;
            public GameObject ZoomLabel;
            public GameObject YawLabel;
            public GameObject PanXLabel;
            public GameObject PanYLabel;
            public GameObject StatusLabel;
            public string FrameShown;
            public string ZoomShown;
            public string YawShown;
            public string PanXShown;
            public string PanYShown;
            public string StatusShown;
        }

        private UguiAvatarStudioHandle uguiAvatarStudio;
        private bool uguiAvatarStudioBuildFailed;

        private static float UguiAvatarStudioHeight
        {
            get { return 456f; }
        }

        private void BuildUguiAvatarStudio()
        {
            this.uguiAvatarStudio = null;
            UguiAvatarStudioHandle handle = null;
            try
            {
                handle = new UguiAvatarStudioHandle();
                float w = UguiAvatarStudioWidth;
                float inner = w - (UguiAvatarStudioPadX * 2f);
                float x = UguiAvatarStudioPadX;

                // Title drawn by hand in a compact strip — the kit title needs ~44px (Action Panel note).
                handle.Window = this.CreateUguiWindow("BugtopiaUguiAvatarStudio", null, null,
                    new Vector2(w, UguiAvatarStudioHeight), UguiAvatarStudioSortingOrder, UguiAvatarStudioTitleH);
                Transform panelT = handle.Window.PanelRt;

                GameObject title = this.CreateUguiLabel(panelT, "Title", this.L("Avatar Studio"),
                    12f, this.UguiKitHeaderColor(), false);
                this.TrySetUguiLabelBold(title);
                PlaceUguiTopLeft(title, x, 3f, inner - 30f, UguiAvatarStudioLabelH);

                GameObject close = this.CreateUguiSecondaryButton(panelT, "Close", "×",
                    new Action(this.DismissAvatarStudio));
                PlaceUguiTopLeft(close, w - UguiAvatarStudioPadX - 22f, 2f, 22f, 18f);

                float y = 28f;
                GameObject poseLabel = this.CreateUguiMutedLabel(panelT, "PoseLabel", this.L("Pose"), 12f);
                PlaceUguiTopLeft(poseLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.PoseDropdown = this.CreateUguiDropdown(panelT, "PoseDropdown",
                    new string[] { this.L("Choose a pose…") }, 0,
                    v => this.OnUguiAvatarStudioPosePicked(v), out handle.PoseListenerWired);
                PlaceUguiTopLeft(handle.PoseDropdown.gameObject, x, y, inner, UguiAvatarStudioRowH);
                y += UguiAvatarStudioRowH + 6f;

                float half = (inner - 6f) * 0.5f;
                handle.PlayButton = this.CreateUguiSecondaryButton(panelT, "PlayPause", this.L("Pause"),
                    new Action(this.ToggleAvatarStudioPause));
                PlaceUguiTopLeft(handle.PlayButton, x, y, half, UguiAvatarStudioRowH);
                GameObject reset = this.CreateUguiSecondaryButton(panelT, "ResetView", this.L("Reset view"),
                    new Action(this.ResetAvatarStudioView));
                PlaceUguiTopLeft(reset, x + half + 6f, y, half, UguiAvatarStudioRowH);
                y += UguiAvatarStudioRowH + 6f;

                handle.FrameLabel = this.CreateUguiMutedLabel(panelT, "FrameLabel", string.Empty, 12f);
                PlaceUguiTopLeft(handle.FrameLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.FrameSlider = this.CreateUguiSlider(panelT, "FrameSlider", 1f, 2f, 1f, true,
                    v => this.OnUguiAvatarStudioFrameMoved(v));
                PlaceUguiTopLeft(handle.FrameSlider.gameObject, x, y, inner, UguiAvatarStudioSliderH);
                y += UguiAvatarStudioSliderH + 6f;

                GameObject faceLabel = this.CreateUguiMutedLabel(panelT, "FaceLabel", this.L("Face"), 12f);
                PlaceUguiTopLeft(faceLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.FaceDropdown = this.CreateUguiDropdown(panelT, "FaceDropdown",
                    new string[] { this.L("Choose a face…") }, 0,
                    v => this.OnUguiAvatarStudioFacePicked(v), out handle.FaceListenerWired);
                PlaceUguiTopLeft(handle.FaceDropdown.gameObject, x, y, inner, UguiAvatarStudioRowH);
                y += UguiAvatarStudioRowH + 6f;

                GameObject bgLabel = this.CreateUguiMutedLabel(panelT, "BackgroundLabel", this.L("Background"), 12f);
                PlaceUguiTopLeft(bgLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                const float folderW = 70f;
                handle.BackgroundDropdown = this.CreateUguiDropdown(panelT, "BackgroundDropdown",
                    new string[] { this.L("Game (panel button)") }, 0,
                    v => this.OnUguiAvatarStudioBackgroundPicked(v), out handle.BackgroundListenerWired);
                PlaceUguiTopLeft(handle.BackgroundDropdown.gameObject, x, y, inner - folderW - 6f, UguiAvatarStudioRowH);
                GameObject folder = this.CreateUguiSecondaryButton(panelT, "BackgroundFolder", this.L("Folder"),
                    new Action(this.OpenAvatarStudioBackgroundFolder));
                PlaceUguiTopLeft(folder, x + inner - folderW, y, folderW, UguiAvatarStudioRowH);
                y += UguiAvatarStudioRowH + 6f;

                handle.ZoomLabel = this.CreateUguiMutedLabel(panelT, "ZoomLabel", string.Empty, 12f);
                PlaceUguiTopLeft(handle.ZoomLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.ZoomSlider = this.CreateUguiSlider(panelT, "ZoomSlider",
                    AvatarStudioZoomMin, AvatarStudioZoomMax, 1f, false, v => this.OnUguiAvatarStudioZoomMoved(v));
                PlaceUguiTopLeft(handle.ZoomSlider.gameObject, x, y, inner, UguiAvatarStudioSliderH);
                y += UguiAvatarStudioSliderH + 6f;

                handle.YawLabel = this.CreateUguiMutedLabel(panelT, "YawLabel", string.Empty, 12f);
                PlaceUguiTopLeft(handle.YawLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.YawSlider = this.CreateUguiSlider(panelT, "YawSlider",
                    -AvatarStudioYawMax, AvatarStudioYawMax, 0f, true, v => this.OnUguiAvatarStudioYawMoved(v));
                PlaceUguiTopLeft(handle.YawSlider.gameObject, x, y, inner, UguiAvatarStudioSliderH);
                y += UguiAvatarStudioSliderH + 6f;

                handle.PanXLabel = this.CreateUguiMutedLabel(panelT, "PanXLabel", string.Empty, 12f);
                PlaceUguiTopLeft(handle.PanXLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.PanXSlider = this.CreateUguiSlider(panelT, "PanXSlider",
                    -AvatarStudioPanMax, AvatarStudioPanMax, 0f, false, v => this.OnUguiAvatarStudioPanMoved(v, true));
                PlaceUguiTopLeft(handle.PanXSlider.gameObject, x, y, inner, UguiAvatarStudioSliderH);
                y += UguiAvatarStudioSliderH + 6f;

                handle.PanYLabel = this.CreateUguiMutedLabel(panelT, "PanYLabel", string.Empty, 12f);
                PlaceUguiTopLeft(handle.PanYLabel, x, y, inner, UguiAvatarStudioLabelH);
                y += 18f;
                handle.PanYSlider = this.CreateUguiSlider(panelT, "PanYSlider",
                    -AvatarStudioPanMax, AvatarStudioPanMax, 0f, false, v => this.OnUguiAvatarStudioPanMoved(v, false));
                PlaceUguiTopLeft(handle.PanYSlider.gameObject, x, y, inner, UguiAvatarStudioSliderH);
                y += UguiAvatarStudioSliderH + 6f;

                handle.StatusLabel = this.CreateUguiMutedLabel(panelT, "Status", string.Empty, 11f);
                PlaceUguiTopLeft(handle.StatusLabel, x, y, inner, UguiAvatarStudioLabelH);

                handle.LastSyncedUiScale = this.GetUiScale();
                this.SetUguiWindowScale(handle.Window, handle.LastSyncedUiScale);

                // Right-hand half, a little above centre — over the panel's style list, clear of the
                // capture circle on the left and of Confirm at the bottom right.
                float s = Mathf.Max(handle.Window.Scale, 0.1f);
                float halfW = Screen.width / s * 0.5f;
                handle.Window.PanelRt.anchoredPosition = new Vector2(halfW - (w * 0.5f) - 40f, 40f);
                this.ClampUguiWindowPosition(handle.Window);

                this.uguiAvatarStudio = handle;

                this.RegisterUguiThemeRebuilder("UguiAvatarStudio",
                    new Action(this.RebuildUguiAvatarStudioForTheme));

                // Floating, not modal: it must not swallow the game's input while it just sits there.
                this.RegisterInputOwnershipSurface("UguiAvatarStudio", false,
                    () => this.uguiAvatarStudio != null && this.IsUguiWindowVisible(this.uguiAvatarStudio.Window),
                    () => this.uguiAvatarStudio != null && this.IsUguiWindowPointerOver(this.uguiAvatarStudio.Window));

                ModLogger.Msg("[UguiShell] Avatar Studio built — sortingOrder " + UguiAvatarStudioSortingOrder
                    + ", listeners pose=" + handle.PoseListenerWired + " face=" + handle.FaceListenerWired);
            }
            catch (Exception ex)
            {
                this.uguiAvatarStudioBuildFailed = true;
                try
                {
                    if (handle != null && handle.Window != null && handle.Window.Root != null)
                    {
                        Object.Destroy(handle.Window.Root);
                    }
                }
                catch { }
                this.uguiAvatarStudio = null;
                FeatureLog.Fail(AvatarStudioTag, "window build failed: " + ex.Message);
            }
        }

        private void RebuildUguiAvatarStudioForTheme()
        {
            try
            {
                if (this.uguiAvatarStudio != null && this.uguiAvatarStudio.Window != null
                    && this.uguiAvatarStudio.Window.Root != null)
                {
                    Object.Destroy(this.uguiAvatarStudio.Window.Root);
                }
            }
            catch { }

            this.uguiAvatarStudio = null;
            this.uguiAvatarStudioBuildFailed = false;
        }

        // ── control callbacks ───────────────────────────────────────────────────────────────────

        // Row 0 is the placeholder, so list index = value - 1.
        private void OnUguiAvatarStudioPosePicked(int value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            h.PoseLastValue = value;
            if (value > 0)
            {
                this.ApplyAvatarStudioPose(value - 1);
            }
        }

        private void OnUguiAvatarStudioFacePicked(int value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            h.FaceLastValue = value;
            if (value > 0)
            {
                this.ApplyAvatarStudioFace(value - 1);
            }
        }

        // No placeholder row here: index 0 IS "Game", so list index = value.
        private void OnUguiAvatarStudioBackgroundPicked(int value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            h.BackgroundLastValue = value;
            this.ApplyAvatarStudioBackground(value);
        }

        private void OnUguiAvatarStudioFrameMoved(float value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            this.SeekAvatarStudioFrame(Mathf.RoundToInt(value));
        }

        private void OnUguiAvatarStudioZoomMoved(float value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            this.SetAvatarStudioZoom(value);
        }

        private void OnUguiAvatarStudioYawMoved(float value)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            this.SetAvatarStudioYaw(value);
        }

        private void OnUguiAvatarStudioPanMoved(float value, bool horizontal)
        {
            UguiAvatarStudioHandle h = this.uguiAvatarStudio;
            if (h == null || h.Syncing)
            {
                return;
            }
            Vector2 pan = this.AvatarStudioPan;
            if (horizontal)
            {
                pan.x = value;
            }
            else
            {
                pan.y = value;
            }
            this.SetAvatarStudioPan(pan);
        }

        // ── per-frame driver ────────────────────────────────────────────────────────────────────

        private void ProcessUguiAvatarStudioOnUpdate()
        {
            try
            {
                this.TickAvatarStudioDetection();
                bool show = this.AvatarStudioWanted;

                UguiAvatarStudioHandle handle = this.uguiAvatarStudio;
                if (handle == null)
                {
                    // Built the first time it is wanted: a window nobody opens costs nothing.
                    if (!show || this.uguiAvatarStudioBuildFailed || !this.IsWorldReady)
                    {
                        return;
                    }
                    this.BuildUguiAvatarStudio();
                    handle = this.uguiAvatarStudio;
                    if (handle == null)
                    {
                        return;
                    }
                }

                if (handle.ErrorCount >= 3)
                {
                    if (this.IsUguiWindowVisible(handle.Window))
                    {
                        this.SetUguiWindowVisible(handle.Window, false);
                    }
                    return;
                }

                if (this.IsUguiWindowVisible(handle.Window) != show)
                {
                    this.SetUguiWindowVisible(handle.Window, show);
                    if (show)
                    {
                        this.ResetUguiAvatarStudioControls(handle);
                    }
                }
                if (!show)
                {
                    return;
                }

                this.ProcessUguiWindowFrame(handle.Window);
                this.ProcessAvatarStudioPanDrag(this.IsUguiWindowPointerOver(handle.Window));

                float targetScale = this.GetUiScale();
                if (!Mathf.Approximately(targetScale, handle.LastSyncedUiScale))
                {
                    handle.LastSyncedUiScale = targetScale;
                    this.SetUguiWindowScale(handle.Window, targetScale);
                }

                this.SyncUguiAvatarStudio(handle);
            }
            catch (Exception ex)
            {
                UguiAvatarStudioHandle h = this.uguiAvatarStudio;
                if (h != null)
                {
                    h.ErrorCount++;
                }
                FeatureLog.Fail(AvatarStudioTag, "window update failed: " + ex.Message);
            }
        }

        // A fresh panel open starts from the game's own framing, so the controls go back to neutral.
        private void ResetUguiAvatarStudioControls(UguiAvatarStudioHandle h)
        {
            h.Syncing = true;
            try
            {
                if (h.PoseDropdown != null)
                {
                    h.PoseDropdown.SetValueWithoutNotify(0);
                }
                if (h.FaceDropdown != null)
                {
                    h.FaceDropdown.SetValueWithoutNotify(0);
                }
                h.PoseLastValue = 0;
                h.FaceLastValue = 0;
            }
            finally
            {
                h.Syncing = false;
            }
        }

        private void SyncUguiAvatarStudio(UguiAvatarStudioHandle h)
        {
            // Dropdown poll fallback — only when UnityEvent<int> wiring failed (Birds tab precedent).
            if (!h.PoseListenerWired && h.PoseDropdown != null && h.PoseDropdown.value != h.PoseLastValue)
            {
                this.OnUguiAvatarStudioPosePicked(h.PoseDropdown.value);
            }
            if (!h.FaceListenerWired && h.FaceDropdown != null && h.FaceDropdown.value != h.FaceLastValue)
            {
                this.OnUguiAvatarStudioFacePicked(h.FaceDropdown.value);
            }
            if (!h.BackgroundListenerWired && h.BackgroundDropdown != null
                && h.BackgroundDropdown.value != h.BackgroundLastValue)
            {
                this.OnUguiAvatarStudioBackgroundPicked(h.BackgroundDropdown.value);
            }

            // Backgrounds: folder rescan + keep the pick on the active plane, then mirror the list.
            this.TickAvatarStudioBackground();
            if (h.BackgroundDropdown != null)
            {
                if (h.BackgroundListVersion != this.AvatarStudioBackgroundListVersion)
                {
                    h.BackgroundListVersion = this.AvatarStudioBackgroundListVersion;
                    string[] labels = new string[this.AvatarStudioBackgrounds.Count];
                    for (int i = 0; i < labels.Length; i++)
                    {
                        labels[i] = this.AvatarStudioBackgrounds[i].Label;
                    }
                    this.FillUguiAvatarStudioDropdown(h, h.BackgroundDropdown, null, labels);
                }
                int want = this.AvatarStudioBackgroundSelected;
                if (h.BackgroundDropdown.value != want)
                {
                    h.Syncing = true;
                    try
                    {
                        h.BackgroundDropdown.SetValueWithoutNotify(want);
                        h.BackgroundDropdown.RefreshShownValue();
                    }
                    finally
                    {
                        h.Syncing = false;
                    }
                }
                h.BackgroundLastValue = want;
            }

            // Lists that were not ready when the window was built.
            if (h.PoseOptionCount == 0 && this.AvatarStudioPoses.Count > 0)
            {
                string[] labels = new string[this.AvatarStudioPoses.Count];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = this.AvatarStudioPoses[i].Label;
                }
                this.FillUguiAvatarStudioDropdown(h, h.PoseDropdown, this.L("Choose a pose…"), labels);
                h.PoseOptionCount = labels.Length;
                h.PoseLastValue = 0;
            }
            if (h.FaceOptionCount == 0 && this.EnsureAvatarStudioFaceClips())
            {
                string[] labels = this.AvatarStudioFaceLabels.ToArray();
                this.FillUguiAvatarStudioDropdown(h, h.FaceDropdown, this.L("Choose a face…"), labels);
                h.FaceOptionCount = labels.Length;
                h.FaceLastValue = 0;
            }

            // While the model plays, follow it; while paused the numbers only change on a seek.
            this.SyncAvatarStudioLiveFrame();

            h.Syncing = true;
            try
            {
                int n = Math.Max(1, this.AvatarStudioFrameCount);
                if (h.FrameSlider != null)
                {
                    // wholeNumbers needs max > min to be draggable at all; a 1-frame state keeps 1..2.
                    float max = Math.Max(2, n);
                    if (!Mathf.Approximately(h.FrameSlider.maxValue, max))
                    {
                        h.FrameSlider.maxValue = max;
                    }
                    float want = Mathf.Clamp(this.AvatarStudioFrame, 1, n);
                    if (!Mathf.Approximately(h.FrameSlider.value, want))
                    {
                        h.FrameSlider.SetValueWithoutNotify(want);
                    }
                }
                if (h.ZoomSlider != null && Mathf.Abs(h.ZoomSlider.value - this.AvatarStudioZoom) > 0.001f)
                {
                    h.ZoomSlider.SetValueWithoutNotify(this.AvatarStudioZoom);
                }
                if (h.YawSlider != null && Mathf.Abs(h.YawSlider.value - this.AvatarStudioYaw) > 0.01f)
                {
                    h.YawSlider.SetValueWithoutNotify(this.AvatarStudioYaw);
                }
                // The right-button drag moves the pan from outside the sliders.
                Vector2 pan = this.AvatarStudioPan;
                if (h.PanXSlider != null && Mathf.Abs(h.PanXSlider.value - pan.x) > 0.0005f)
                {
                    h.PanXSlider.SetValueWithoutNotify(pan.x);
                }
                if (h.PanYSlider != null && Mathf.Abs(h.PanYSlider.value - pan.y) > 0.0005f)
                {
                    h.PanYSlider.SetValueWithoutNotify(pan.y);
                }
            }
            finally
            {
                h.Syncing = false;
            }

            string frameText = this.LF("Frame {0} / {1}", this.AvatarStudioFrame, Math.Max(1, this.AvatarStudioFrameCount))
                + (this.AvatarStudioPaused ? string.Empty : " — " + this.L("playing"));
            this.SetUguiAvatarStudioLabel(h.FrameLabel, ref h.FrameShown, frameText);
            this.SetUguiAvatarStudioLabel(h.ZoomLabel, ref h.ZoomShown,
                this.LF("Zoom {0}x", this.AvatarStudioZoom.ToString("0.00")));
            this.SetUguiAvatarStudioLabel(h.YawLabel, ref h.YawShown,
                this.LF("Rotation {0}°", Mathf.RoundToInt(this.AvatarStudioYaw)));
            this.SetUguiAvatarStudioLabel(h.PanXLabel, ref h.PanXShown,
                this.LF("Move X {0} (right-drag the model)", this.AvatarStudioPan.x.ToString("0.00")));
            this.SetUguiAvatarStudioLabel(h.PanYLabel, ref h.PanYShown,
                this.LF("Move Y {0}", this.AvatarStudioPan.y.ToString("0.00")));
            this.SetUguiAvatarStudioLabel(h.StatusLabel, ref h.StatusShown, this.AvatarStudioStatus);

            string play = this.AvatarStudioPaused ? this.L("Play") : this.L("Pause");
            if (!string.Equals(play, h.PlayShown, StringComparison.Ordinal))
            {
                h.PlayShown = play;
                this.SetUguiButtonLabel(h.PlayButton, play);
            }
        }

        private void SetUguiAvatarStudioLabel(GameObject label, ref string shown, string text)
        {
            if (label != null && !string.Equals(text, shown, StringComparison.Ordinal))
            {
                shown = text;
                this.SetUguiLabelText(label, text);
            }
        }

        private void FillUguiAvatarStudioDropdown(UguiAvatarStudioHandle h, Dropdown dd, string placeholder, string[] labels)
        {
            if (dd == null)
            {
                return;
            }

            h.Syncing = true;
            try
            {
                var options = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
                int rows = labels.Length;
                if (placeholder != null)
                {
                    options.Add(new Dropdown.OptionData(placeholder));
                    rows++;
                }
                for (int i = 0; i < labels.Length; i++)
                {
                    options.Add(new Dropdown.OptionData(labels[i]));
                }
                dd.ClearOptions();
                dd.AddOptions(options);
                dd.SetValueWithoutNotify(0);
                dd.RefreshShownValue();

                // The kit sized the popup template for the single placeholder row; the template
                // height is the popup's CAP (Dropdown.Show only shrinks it), so re-derive it.
                if (dd.template != null)
                {
                    float height = Mathf.Min(rows * UguiAvatarStudioDropdownItemH + 4f,
                                             UguiDropdownMaxPopupHeight);
                    dd.template.sizeDelta = new Vector2(dd.template.sizeDelta.x, height);
                }
            }
            finally
            {
                h.Syncing = false;
            }
        }
    }
}
