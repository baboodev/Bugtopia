using UnityEngine;
using UnityEngine.SceneManagement;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int LodOverrideModeDefault = 0;
        private const int LodOverrideModeBetter = 1;
        private const int LodOverrideModePerformance = 2;
        private const int LodOverrideModeCustom = 3;

        private const float LodBetterBias = 4f;
        private const int LodBetterMaxLevel = 0;
        private const float LodPerformanceBias = 0.5f;
        private const int LodPerformanceMaxLevel = 2;

        private static readonly string[] LodOverrideModeLabels =
        {
            "Game default",
            "Better",
            "Performance",
            "Custom"
        };

        private int lodOverrideMode = LodOverrideModeDefault;
        private float lodCustomBias = 1f;
        private int lodCustomMaxLevel = LodBetterMaxLevel;

        private bool lodBaseCaptured = false;
        private float lodBaseBias = 1f;
        private int lodBaseMaxLevel = 0;
        private bool lodOverrideWasApplied = false;
        private float nextLodOverrideApplyAt = 0f;

        private bool IsLodOverrideSceneReady()
        {
            Scene scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && !string.IsNullOrWhiteSpace(scene.name);
        }

        private void CaptureLodBaseSettingsFromGame()
        {
            lodBaseBias = QualitySettings.lodBias;
            lodBaseMaxLevel = QualitySettings.maximumLODLevel;
            lodBaseCaptured = true;
        }

        private void GetTargetLodValues(out float bias, out int maxLevel)
        {
            switch (Mathf.Clamp(this.lodOverrideMode, LodOverrideModeDefault, LodOverrideModeCustom))
            {
                case LodOverrideModeBetter:
                    bias = LodBetterBias;
                    maxLevel = LodBetterMaxLevel;
                    return;
                case LodOverrideModePerformance:
                    bias = LodPerformanceBias;
                    maxLevel = LodPerformanceMaxLevel;
                    return;
                case LodOverrideModeCustom:
                    bias = Mathf.Clamp(this.lodCustomBias, 0.25f, 4f);
                    maxLevel = Mathf.Clamp(this.lodCustomMaxLevel, 0, 4);
                    return;
                default:
                    bias = this.lodBaseBias;
                    maxLevel = this.lodBaseMaxLevel;
                    return;
            }
        }

        private string GetLodOverrideModeLabel()
        {
            int index = Mathf.Clamp(this.lodOverrideMode, 0, LodOverrideModeLabels.Length - 1);
            return this.L(LodOverrideModeLabels[index]);
        }

        private float GetLodSettingsPanelHeight()
        {
            float height = 132f;
            if (this.lodOverrideMode == LodOverrideModeCustom)
            {
                height += 92f;
            }

            return height;
        }

        private void ApplyLodOverride(bool enabled)
        {
            if (!enabled || this.lodOverrideMode == LodOverrideModeDefault)
            {
                this.RevertLodOverride();
                return;
            }

            if (!this.IsLodOverrideSceneReady())
            {
                return;
            }

            if (!this.lodBaseCaptured)
            {
                this.CaptureLodBaseSettingsFromGame();
            }

            this.GetTargetLodValues(out float bias, out int maxLevel);
            QualitySettings.lodBias = bias;
            QualitySettings.maximumLODLevel = maxLevel;
            this.lodOverrideWasApplied = true;
        }

        private void RevertLodOverride()
        {
            if (!this.lodOverrideWasApplied && !this.lodBaseCaptured)
            {
                return;
            }

            if (this.lodBaseCaptured)
            {
                QualitySettings.lodBias = this.lodBaseBias;
                QualitySettings.maximumLODLevel = this.lodBaseMaxLevel;
            }

            this.lodOverrideWasApplied = false;
        }

        private void SetLodOverrideMode(int mode)
        {
            mode = Mathf.Clamp(mode, LodOverrideModeDefault, LodOverrideModeCustom);
            if (this.lodOverrideMode == mode)
            {
                return;
            }

            int previousMode = this.lodOverrideMode;
            this.lodOverrideMode = mode;
            if (mode == LodOverrideModeDefault)
            {
                this.RevertLodOverride();
            }
            else
            {
                if (previousMode == LodOverrideModeDefault || !this.lodBaseCaptured)
                {
                    this.CaptureLodBaseSettingsFromGame();
                }

                this.nextLodOverrideApplyAt = 0f;
                this.ApplyLodOverride(true);
            }

            this.SaveKeybinds(false);
            this.AddMenuNotification(this.LF("LOD: {0}", this.GetLodOverrideModeLabel()), new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB));
        }

        private void SyncLodOverrideAfterConfigLoad()
        {
            this.lodOverrideMode = Mathf.Clamp(this.lodOverrideMode, LodOverrideModeDefault, LodOverrideModeCustom);
            this.lodCustomBias = Mathf.Clamp(this.lodCustomBias <= 0f ? 1f : this.lodCustomBias, 0.25f, 4f);
            this.lodCustomMaxLevel = Mathf.Clamp(this.lodCustomMaxLevel, 0, 4);
            if (this.lodOverrideMode == LodOverrideModeDefault)
            {
                this.RevertLodOverride();
                return;
            }

            this.nextLodOverrideApplyAt = 0f;
        }

        private void ProcessLodOverrideOnUpdate()
        {
            if (this.lodOverrideMode == LodOverrideModeDefault)
            {
                if (this.lodOverrideWasApplied)
                {
                    this.RevertLodOverride();
                }

                return;
            }

            if (!this.IsLodOverrideSceneReady())
            {
                return;
            }

            if (Time.unscaledTime >= this.nextLodOverrideApplyAt)
            {
                this.ApplyLodOverride(true);
                this.nextLodOverrideApplyAt = Time.unscaledTime + 0.5f;
            }
        }

        private float DrawLodSettingsInPerformancePanel(Rect panel, float rowY, float rowHeight, GUIStyle rowLabelStyle)
        {
            GUI.Label(new Rect(panel.x + 16f, rowY, panel.width - 32f, 20f), this.L("LOD Override"), rowLabelStyle);
            rowY += 32f;

            float buttonWidth = (panel.width - 52f) / LodOverrideModeLabels.Length;
            for (int i = 0; i < LodOverrideModeLabels.Length; i++)
            {
                Rect buttonRect = new Rect(panel.x + 16f + i * (buttonWidth + 4f), rowY, buttonWidth, rowHeight);
                bool selected = this.lodOverrideMode == i;
                GUIStyle buttonStyle = selected
                    ? (this.themeTopTabActiveStyle ?? GUI.skin.button)
                    : (this.themeSidebarButtonStyle ?? GUI.skin.button);
                if (GUI.Button(buttonRect, this.L(LodOverrideModeLabels[i]), buttonStyle))
                {
                    this.SetLodOverrideMode(i);
                }
            }

            rowY += rowHeight + 16f;
            string status = this.lodOverrideMode == LodOverrideModeDefault
                ? this.LF("Game LOD: bias {0:0.##}, max level {1}", QualitySettings.lodBias, QualitySettings.maximumLODLevel)
                : this.LF("Override active: bias {0:0.##}, max level {1}", QualitySettings.lodBias, QualitySettings.maximumLODLevel);
            GUI.Label(new Rect(panel.x + 16f, rowY, panel.width - 32f, 20f), status, rowLabelStyle);
            rowY += 30f;

            if (this.lodOverrideMode == LodOverrideModeCustom)
            {
                rowY += 6f;
                GUI.Label(new Rect(panel.x + 16f, rowY, 150f, 20f), this.LF("LOD bias: {0:0.##}", this.lodCustomBias), rowLabelStyle);
                float newBias = this.DrawAccentSlider(new Rect(panel.x + 170f, rowY, panel.width - 200f, 20f), this.lodCustomBias, 0.25f, 4.01f);
                if (Mathf.Abs(newBias - this.lodCustomBias) > 0.001f)
                {
                    this.lodCustomBias = Mathf.Clamp(newBias, 0.25f, 4f);
                    this.nextLodOverrideApplyAt = 0f;
                    this.ApplyLodOverride(true);
                    this.SaveKeybinds(false);
                }

                rowY += 42f;
                GUI.Label(new Rect(panel.x + 16f, rowY, 180f, 20f), this.LF("Max LOD level: {0}", this.lodCustomMaxLevel), rowLabelStyle);
                int newMaxLevel = Mathf.RoundToInt(this.DrawAccentSlider(new Rect(panel.x + 200f, rowY, panel.width - 230f, 20f), (float)this.lodCustomMaxLevel, 0f, 4.01f, true));
                if (newMaxLevel != this.lodCustomMaxLevel)
                {
                    this.lodCustomMaxLevel = Mathf.Clamp(newMaxLevel, 0, 4);
                    this.nextLodOverrideApplyAt = 0f;
                    this.ApplyLodOverride(true);
                    this.SaveKeybinds(false);
                }

                rowY += 42f;
            }

            return rowY - panel.y + 14f;
        }
    }
}
