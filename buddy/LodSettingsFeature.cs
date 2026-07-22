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

    }
}
