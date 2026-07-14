using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Game UI Timings — editable tip/toast display durations (Self -> Game UI tab).
    //
    // Overrides float fields of the game's live TipShowTimeConfig
    // (EcsClient.XDT.Scene.Shared.Data.Scriptable.TipShowTimeConfig, held by
    // ConfigManager.TipConfig.TipShowTimeConfig on the ConfigMgr prefab). The item-obtained
    // bubbles (LightToastPanel/ToastWidget) read LightToastTime on EVERY widget Display(), the
    // other tip panels read their field per message — a live write takes effect on the next
    // toast, no hook needed.
    //
    // Config chain (embedded Mono — AuraMono only, per project policy for EcsClient/XDT* types):
    //   Managers._serviceDic[IConfigManager].manager      (reuses TryResolveCorruptionConfigManager)
    //     -> ConfigManager.TipConfig                      (auto-property; the field probe in
    //                                                      TryGetMonoObjectMember misses, the
    //                                                      get_TipConfig getter resolves it)
    //       -> TipConfig.TipShowTimeConfig                (public FIELD, plain serializable class)
    //         -> float fields (LightToastTime, ...)       (mono_field_set_value with &float —
    //                                                      value-type field takes a pointer to the value)
    //
    // Same lifecycle as SwimSprintTweakFeature: the chain is re-resolved fresh on every apply (no
    // raw mono object ptr ever crosses a frame; each hop is pinned while in use) and re-applied on
    // a 0.5 s throttle so a config reload (world/scene change rebuilds ConfigManager assets)
    // cannot silently revert the override. Fail-closed: any unresolved hop -> no write, status
    // string, retry next tick, log once per distinct status. Originals are captured from the live
    // object before the first write and restored on the toggle's falling edge.
    public partial class HeartopiaComplete
    {
        private const float GameUiTimingMin = 0.1f;
        private const float GameUiTimingMax = 5f;
        private const float GameUiTimingApplyInterval = 0.5f;

        // Field names on TipShowTimeConfig (verify in ilspy-dumps after a game patch:
        // EcsClient/EcsClient.XDT.Scene.Shared.Data.Scriptable/TipShowTimeConfig.cs) and the
        // matching game-default values. The four arrays below are index-aligned.
        private static readonly string[] GameUiTimingFieldNames =
        {
            "LightToastTime",           // item-obtained bubbles (LightToastPanel/ToastWidget)
            "ToastShowTime",            // plain text toasts (ToastPanel)
            "IconToastTime",            // buff icon toasts (IconToastPanel)
            "HarvestTipPictorialTime",  // new pictorial/record banner (HarvestTipPanel)
            "HarvestTipAchievementTime",
            "HarvestTipCatBuffTime",
            "HarvestTipTaskTime",
        };

        private static readonly string[] GameUiTimingSliderLabels =
        {
            "Item Toast (obtained items)",
            "Text Toast",
            "Buff Icon Toast",
            "Pictorial Tip",
            "Achievement Tip",
            "Cat Buff Tip",
            "Task Tip",
        };

        private static readonly float[] GameUiTimingGameDefaults = { 2.5f, 2.5f, 2.5f, 4f, 2.6f, 1.4f, 2.6f };

        private bool gameUiTimingsEnabled;
        private readonly float[] gameUiTimingSeconds = (float[])GameUiTimingGameDefaults.Clone();
        private bool gameUiTimingsPrevEnabled;
        private float gameUiTimingsNextApplyAt = -999f;
        private string gameUiTimingsStatus = "Idle.";
        private string gameUiTimingsLastLoggedStatus;
        private FeatureBreakerState gameUiTimingsBreaker;

        // Field pointers are mono metadata (image lifetime — safe to cache raw); keyed on the
        // resolved TipShowTimeConfig class ptr so a class change re-resolves them.
        private IntPtr gameUiTipShowTimeClass;
        private readonly IntPtr[] gameUiTimingFields = new IntPtr[GameUiTimingFieldNames.Length];

        // Originals captured from the live config before the first write (restored on disable).
        // Captured once per process — a reloaded config object carries the same asset defaults,
        // so restoring these onto a fresh object stays correct.
        private bool gameUiTimingOriginalsCaptured;
        private readonly float[] gameUiTimingOriginals = (float[])GameUiTimingGameDefaults.Clone();

        // Falling-edge restore keeps retrying on the throttle until it lands or the deadline
        // passes (a config reload replaces the object with clean values anyway).
        private bool gameUiTimingsRestorePending;
        private float gameUiTimingsRestoreGiveUpAt;

        private void ProcessGameUiTimingsOnUpdate()
        {
            float now = Time.unscaledTime;

            bool falling = !this.gameUiTimingsEnabled && this.gameUiTimingsPrevEnabled;
            this.gameUiTimingsPrevEnabled = this.gameUiTimingsEnabled;
            if (falling && this.gameUiTimingOriginalsCaptured)
            {
                this.gameUiTimingsRestorePending = true;
                this.gameUiTimingsRestoreGiveUpAt = now + 10f;
                this.gameUiTimingsNextApplyAt = 0f; // restore promptly
            }

            if (!this.gameUiTimingsEnabled && !this.gameUiTimingsRestorePending)
            {
                return;
            }

            if (now < this.gameUiTimingsNextApplyAt)
            {
                return;
            }

            this.gameUiTimingsNextApplyAt = now + GameUiTimingApplyInterval;

            if (!this.gameUiTimingsBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.gameUiTimingsEnabled)
                {
                    this.gameUiTimingsRestorePending = false;
                    this.ApplyGameUiTimingValues(this.gameUiTimingSeconds, captureOriginals: true);
                }
                else if (now >= this.gameUiTimingsRestoreGiveUpAt)
                {
                    this.gameUiTimingsRestorePending = false;
                    this.GameUiTimingsSetStatus("Restore window expired (config unreachable; a config reload resets it anyway).");
                }
                else if (this.ApplyGameUiTimingValues(this.gameUiTimingOriginals, captureOriginals: false))
                {
                    this.gameUiTimingsRestorePending = false;
                    this.GameUiTimingsSetStatus("Originals restored.");
                }

                this.gameUiTimingsBreaker.Success();
            }
            catch (Exception ex)
            {
                this.gameUiTimingsBreaker.Failure("GameUiTimings", ex, now);
                this.gameUiTimingsStatus = "Error: " + ex.Message;
            }
        }

        // Resolves ConfigManager.TipConfig.TipShowTimeConfig fresh and writes every timing field.
        // Returns false (status set, no partial capture-then-skip) on any unresolved hop.
        private unsafe bool ApplyGameUiTimingValues(float[] values, bool captureOriginals)
        {
            if (auraMonoFieldSetValue == null || auraMonoObjectGetClass == null)
            {
                this.GameUiTimingsSetStatus("AuraMono field-set export unavailable.");
                return false;
            }

            uint managerPin = 0U;
            try
            {
                // Managers._serviceDic[IConfigManager].manager walk (CorruptionCleanseFeature helper;
                // on success the manager object is pinned — freed in the finally below).
                if (!this.TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out managerPin, out string status)
                    || configManagerObj == IntPtr.Zero)
                {
                    this.GameUiTimingsSetStatus("ConfigManager unresolved: " + status);
                    return false;
                }

                if (!this.TryGetMonoObjectMember(configManagerObj, "TipConfig", out IntPtr tipConfigObj) || tipConfigObj == IntPtr.Zero)
                {
                    this.GameUiTimingsSetStatus("ConfigManager.TipConfig null (config not loaded yet).");
                    return false;
                }

                uint tipConfigPin = AuraMonoPinNew(tipConfigObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(tipConfigObj, "TipShowTimeConfig", out IntPtr showTimeObj) || showTimeObj == IntPtr.Zero)
                    {
                        this.GameUiTimingsSetStatus("TipConfig.TipShowTimeConfig null.");
                        return false;
                    }

                    uint showTimePin = AuraMonoPinNew(showTimeObj);
                    try
                    {
                        IntPtr klass = auraMonoObjectGetClass(showTimeObj);
                        if (klass == IntPtr.Zero)
                        {
                            this.GameUiTimingsSetStatus("TipShowTimeConfig class unresolved.");
                            return false;
                        }

                        if (klass != this.gameUiTipShowTimeClass)
                        {
                            this.gameUiTipShowTimeClass = klass;
                            for (int i = 0; i < GameUiTimingFieldNames.Length; i++)
                            {
                                this.gameUiTimingFields[i] = this.FindAuraMonoFieldOnHierarchy(klass, GameUiTimingFieldNames[i]);
                            }
                        }

                        for (int i = 0; i < this.gameUiTimingFields.Length; i++)
                        {
                            if (this.gameUiTimingFields[i] == IntPtr.Zero)
                            {
                                this.GameUiTimingsSetStatus("Field " + GameUiTimingFieldNames[i] + " unresolved (game update?).");
                                return false;
                            }
                        }

                        // Capture originals BEFORE the first write; failing any read fails the whole
                        // apply (never write values we could not learn how to undo).
                        if (captureOriginals && !this.gameUiTimingOriginalsCaptured)
                        {
                            for (int i = 0; i < GameUiTimingFieldNames.Length; i++)
                            {
                                if (!this.TryGetMonoSingleMember(showTimeObj, GameUiTimingFieldNames[i], out float original))
                                {
                                    this.GameUiTimingsSetStatus("Original value read failed (" + GameUiTimingFieldNames[i] + ").");
                                    return false;
                                }

                                this.gameUiTimingOriginals[i] = original;
                            }

                            this.gameUiTimingOriginalsCaptured = true;
                            ModLogger.Msg("[GameUiTimings] originals captured: " + string.Join(
                                ", ",
                                Array.ConvertAll(this.gameUiTimingOriginals, v => v.ToString("F2"))));
                        }

                        // Value-type float fields: mono_field_set_value takes a pointer TO the value.
                        for (int i = 0; i < this.gameUiTimingFields.Length; i++)
                        {
                            float value = values[i];
                            auraMonoFieldSetValue(showTimeObj, this.gameUiTimingFields[i], (IntPtr)(&value));
                        }

                        this.GameUiTimingsSetStatus(this.LF(
                            "Applied (item toast {0:F1}s).",
                            values[0]));
                        return true;
                    }
                    finally
                    {
                        AuraMonoPinFree(showTimePin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(tipConfigPin);
                }
            }
            finally
            {
                if (managerPin != 0U)
                {
                    AuraMonoPinFree(managerPin);
                }
            }
        }

        private void GameUiTimingsSetStatus(string status)
        {
            this.gameUiTimingsStatus = status;
            if (!string.Equals(status, this.gameUiTimingsLastLoggedStatus, StringComparison.Ordinal))
            {
                this.gameUiTimingsLastLoggedStatus = status;
                ModLogger.Msg("[GameUiTimings] " + status);
            }
        }

        // --- Config bridge (called from PopulateKeybindConfig / ApplyKeybindConfig) --------------

        private void SaveGameUiTimingsToConfig(KeybindConfigData data)
        {
            data.gameUiTimingsEnabled = this.gameUiTimingsEnabled;
            data.gameUiTimingSeconds = (float[])this.gameUiTimingSeconds.Clone();
        }

        private void LoadGameUiTimingsFromConfig(KeybindConfigData data)
        {
            this.gameUiTimingsEnabled = data.gameUiTimingsEnabled;
            Array.Copy(GameUiTimingGameDefaults, this.gameUiTimingSeconds, this.gameUiTimingSeconds.Length);
            if (data.gameUiTimingSeconds == null)
            {
                return;
            }

            int count = Math.Min(data.gameUiTimingSeconds.Length, this.gameUiTimingSeconds.Length);
            for (int i = 0; i < count; i++)
            {
                float value = data.gameUiTimingSeconds[i];
                this.gameUiTimingSeconds[i] = value <= 0f
                    ? GameUiTimingGameDefaults[i]
                    : Mathf.Clamp(value, GameUiTimingMin, GameUiTimingMax);
            }
        }

        // --- GUI (Self -> Game UI sub-tab) --------------------------------------------------------

        private float DrawSelfGameUiTab(int startY)
        {
            int num = startY + 25;
            const float controlWidth = 260f;

            const string toggleLabel = "Custom UI Timings";
            bool prevEnabled = this.gameUiTimingsEnabled;
            float toggleHeight = this.GetSwitchToggleHeight(controlWidth, toggleLabel, 25f);
            this.gameUiTimingsEnabled = this.DrawWrappedSwitchToggle(
                new Rect(20f, (float)num, controlWidth, toggleHeight),
                this.gameUiTimingsEnabled,
                toggleLabel,
                25f);
            if (this.gameUiTimingsEnabled != prevEnabled)
            {
                this.AddMenuNotification(
                    this.gameUiTimingsEnabled ? "Custom UI timings on" : "Custom UI timings off (restoring defaults)",
                    new Color(0.45f, 0.85f, 1f));
                try { this.SaveKeybinds(false); } catch { }
            }
            num += Mathf.CeilToInt(toggleHeight + 8f);

            bool sliderChanged = false;
            for (int i = 0; i < GameUiTimingSliderLabels.Length; i++)
            {
                GUI.Label(
                    new Rect(20f, (float)num, controlWidth, 20f),
                    this.LF(GameUiTimingSliderLabels[i] + ": {0:F1}s", this.gameUiTimingSeconds[i]));
                num += 22;
                float previousValue = this.gameUiTimingSeconds[i];
                this.gameUiTimingSeconds[i] = Mathf.Round(
                    this.DrawAccentSlider(new Rect(20f, (float)num, controlWidth, 20f), this.gameUiTimingSeconds[i], GameUiTimingMin, GameUiTimingMax) * 10f) / 10f;
                if (Mathf.Abs(this.gameUiTimingSeconds[i] - previousValue) > 0.0001f)
                {
                    sliderChanged = true;
                }
                num += 24;
            }

            if (sliderChanged)
            {
                try { this.SaveKeybinds(false); } catch { }
            }

            if (GUI.Button(new Rect(20f, (float)num, controlWidth, 26f), this.L("Reset to game defaults")))
            {
                Array.Copy(GameUiTimingGameDefaults, this.gameUiTimingSeconds, this.gameUiTimingSeconds.Length);
                try { this.SaveKeybinds(false); } catch { }
                this.AddMenuNotification("UI timings reset to game defaults", new Color(0.45f, 0.85f, 1f));
            }
            num += 34;

            GUI.Label(
                new Rect(20f, (float)num, controlWidth, 54f),
                this.L("How long the game's toasts/tips stay on screen (item-obtained bubbles, text toasts, banners). Applies live; disable to restore game defaults.")
                + (this.gameUiTimingsEnabled ? " Status: " + this.gameUiTimingsStatus : string.Empty));
            num += 62;

            return (float)num + 24f;
        }

        private float CalculateSelfGameUiTabHeight()
        {
            // toggle (33) + sliders (22+24 each) + reset button (34) + hint label (62) + padding.
            return 25f + 33f + GameUiTimingSliderLabels.Length * 46f + 34f + 62f + 48f;
        }
    }
}
