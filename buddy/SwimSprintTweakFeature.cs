using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Custom Swim Sprint — overrides the underwater dash's DURATION and COOLDOWN by mutating the
    // game's live SwimSprintConfig (EcsClient.XDT.Scene.Shared.Data.Scriptable.SwimSprintConfig).
    //
    // Mechanic (ilspy-dumps/XDTLevelAndEntity/.../Locomotion/SwimLocomotion.cs): the sprint is a
    // client-side FSM. TickSprint holds SprintPhase.Accelerating for AccelerationDuration seconds
    // (speed ramps to and then stays pinned at MaxSpeed), then Decelerating for DecelerationDuration,
    // then None; TryStartSprint arms _sprintCooldownTimer = Cooldown. So AccelerationDuration IS the
    // sprint-duration knob (a huge value = effectively infinite — max speed holds until a sharp turn
    // cancels the sprint, SwimLocomotion._TickMovementMotion) and Cooldown is the re-dash wait
    // (0 = instant; GetSprintCooldownProgress guards Cooldown<=0, no div-by-zero).
    //
    // Config chain (embedded Mono — AuraMono only, per project policy for EcsClient/XDT* types):
    //   Managers._serviceDic[IConfigManager].manager        (reuses TryResolveCorruptionConfigManager)
    //     -> ConfigManager.SwimConfig                       (public auto-property; the field probe in
    //                                                        TryGetMonoObjectMember misses, the
    //                                                        get_SwimConfig getter resolves it)
    //       -> SwimConfig.SprintConfig                      (public FIELD, SwimSprintConfig reference)
    //         -> float fields AccelerationDuration/Cooldown (mono_field_set_value with &float —
    //                                                        value-type field takes a pointer to the value)
    //
    // The chain is re-resolved fresh on every apply (no raw mono object ptr ever crosses a frame;
    // each hop is pinned while in use) and re-applied on a 0.5 s throttle so a config reload
    // (world/scene change rebuilds ConfigManager assets) cannot silently revert the override.
    // Fail-closed: any unresolved hop -> no write, status string, retry next tick, log once per
    // distinct status. Originals are captured from the live object before the first write and
    // restored on the toggle's falling edge.
    public partial class HeartopiaComplete
    {
        private const float SwimSprintDurationMin = 0.5f;
        private const float SwimSprintDurationMax = 30f;      // slider max position = Infinite
        private const float SwimSprintDurationDefault = 0.5f; // game-default AccelerationDuration
        private const float SwimSprintInfiniteDuration = 99999f;
        private const float SwimSprintCooldownMin = 0f;
        private const float SwimSprintCooldownMax = 5f;
        private const float SwimSprintApplyInterval = 0.5f;

        private bool swimSprintTweakEnabled;
        private float swimSprintDurationSeconds = SwimSprintDurationDefault;
        private float swimSprintCooldownSeconds; // 0 = instant re-dash
        private bool swimSprintPrevEnabled;
        private float swimSprintNextApplyAt = -999f;
        private string swimSprintTweakStatus = "Idle.";
        private string swimSprintLastLoggedStatus;
        private FeatureBreakerState swimSprintBreaker;

        // Field pointers are mono metadata (image lifetime — safe to cache raw); keyed on the
        // resolved SwimSprintConfig class ptr so a class change re-resolves them.
        private IntPtr swimSprintConfigClass;
        private IntPtr swimSprintAccelDurationField;
        private IntPtr swimSprintCooldownField;

        // Originals captured from the live config before the first write (restored on disable).
        // Captured once per process — a reloaded config object carries the same asset defaults,
        // so restoring these onto a fresh object stays correct.
        private bool swimSprintOriginalsCaptured;
        private float swimSprintOriginalAccelDuration = SwimSprintDurationDefault;
        private float swimSprintOriginalCooldown = 3f; // game-default Cooldown

        // Falling-edge restore keeps retrying on the throttle until it lands or the deadline
        // passes (a config reload replaces the object with clean values anyway).
        private bool swimSprintRestorePending;
        private float swimSprintRestoreGiveUpAt;

        private void ProcessSwimSprintTweakOnUpdate()
        {
            float now = Time.unscaledTime;

            bool falling = !this.swimSprintTweakEnabled && this.swimSprintPrevEnabled;
            this.swimSprintPrevEnabled = this.swimSprintTweakEnabled;
            if (falling && this.swimSprintOriginalsCaptured)
            {
                this.swimSprintRestorePending = true;
                this.swimSprintRestoreGiveUpAt = now + 10f;
                this.swimSprintNextApplyAt = 0f; // restore promptly
            }

            if (!this.swimSprintTweakEnabled && !this.swimSprintRestorePending)
            {
                return;
            }

            if (now < this.swimSprintNextApplyAt)
            {
                return;
            }

            this.swimSprintNextApplyAt = now + SwimSprintApplyInterval;

            if (!this.swimSprintBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.swimSprintTweakEnabled)
                {
                    this.swimSprintRestorePending = false;
                    float duration = this.swimSprintDurationSeconds >= SwimSprintDurationMax - 0.001f
                        ? SwimSprintInfiniteDuration
                        : Mathf.Clamp(this.swimSprintDurationSeconds, SwimSprintDurationMin, SwimSprintDurationMax);
                    float cooldown = Mathf.Clamp(this.swimSprintCooldownSeconds, SwimSprintCooldownMin, SwimSprintCooldownMax);
                    this.ApplySwimSprintValues(duration, cooldown, captureOriginals: true);
                }
                else if (now >= this.swimSprintRestoreGiveUpAt)
                {
                    this.swimSprintRestorePending = false;
                    this.SwimSprintSetStatus("Restore window expired (config unreachable; a config reload resets it anyway).");
                }
                else if (this.ApplySwimSprintValues(this.swimSprintOriginalAccelDuration, this.swimSprintOriginalCooldown, captureOriginals: false))
                {
                    this.swimSprintRestorePending = false;
                    this.SwimSprintSetStatus(
                        "Originals restored (duration=" + this.swimSprintOriginalAccelDuration.ToString("F2")
                        + "s cooldown=" + this.swimSprintOriginalCooldown.ToString("F2") + "s).");
                }

                this.swimSprintBreaker.Success();
            }
            catch (Exception ex)
            {
                this.swimSprintBreaker.Failure("SwimSprintTweak", ex, now);
                this.swimSprintTweakStatus = "Error: " + ex.Message;
            }
        }

        // Resolves ConfigManager.SwimConfig.SprintConfig fresh and writes AccelerationDuration /
        // Cooldown. Returns false (status set, no partial capture-then-skip) on any unresolved hop.
        private unsafe bool ApplySwimSprintValues(float accelDuration, float cooldown, bool captureOriginals)
        {
            if (auraMonoFieldSetValue == null || auraMonoObjectGetClass == null)
            {
                this.SwimSprintSetStatus("AuraMono field-set export unavailable.");
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
                    this.SwimSprintSetStatus("ConfigManager unresolved: " + status);
                    return false;
                }

                if (!this.TryGetMonoObjectMember(configManagerObj, "SwimConfig", out IntPtr swimConfigObj) || swimConfigObj == IntPtr.Zero)
                {
                    this.SwimSprintSetStatus("ConfigManager.SwimConfig null (config not loaded yet).");
                    return false;
                }

                uint swimPin = AuraMonoPinNew(swimConfigObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(swimConfigObj, "SprintConfig", out IntPtr sprintConfigObj) || sprintConfigObj == IntPtr.Zero)
                    {
                        this.SwimSprintSetStatus("SwimConfig.SprintConfig null.");
                        return false;
                    }

                    uint sprintPin = AuraMonoPinNew(sprintConfigObj);
                    try
                    {
                        IntPtr klass = auraMonoObjectGetClass(sprintConfigObj);
                        if (klass == IntPtr.Zero)
                        {
                            this.SwimSprintSetStatus("SwimSprintConfig class unresolved.");
                            return false;
                        }

                        if (klass != this.swimSprintConfigClass)
                        {
                            this.swimSprintConfigClass = klass;
                            this.swimSprintAccelDurationField = this.FindAuraMonoFieldOnHierarchy(klass, "AccelerationDuration");
                            this.swimSprintCooldownField = this.FindAuraMonoFieldOnHierarchy(klass, "Cooldown");
                        }

                        if (this.swimSprintAccelDurationField == IntPtr.Zero || this.swimSprintCooldownField == IntPtr.Zero)
                        {
                            this.SwimSprintSetStatus("AccelerationDuration/Cooldown fields unresolved.");
                            return false;
                        }

                        // Capture originals BEFORE the first write; failing the read fails the whole
                        // apply (never write values we could not learn how to undo).
                        if (captureOriginals && !this.swimSprintOriginalsCaptured)
                        {
                            if (!this.TryGetMonoSingleMember(sprintConfigObj, "AccelerationDuration", out float origDuration)
                                || !this.TryGetMonoSingleMember(sprintConfigObj, "Cooldown", out float origCooldown))
                            {
                                this.SwimSprintSetStatus("Original value read failed.");
                                return false;
                            }

                            this.swimSprintOriginalAccelDuration = origDuration;
                            this.swimSprintOriginalCooldown = origCooldown;
                            this.swimSprintOriginalsCaptured = true;
                            ModLogger.Msg(this.LF(
                                "[SwimSprint] originals captured: duration={0:F2}s cooldown={1:F2}s",
                                origDuration,
                                origCooldown));
                        }

                        // Value-type float fields: mono_field_set_value takes a pointer TO the value.
                        float durationValue = accelDuration;
                        float cooldownValue = cooldown;
                        auraMonoFieldSetValue(sprintConfigObj, this.swimSprintAccelDurationField, (IntPtr)(&durationValue));
                        auraMonoFieldSetValue(sprintConfigObj, this.swimSprintCooldownField, (IntPtr)(&cooldownValue));

                        this.SwimSprintSetStatus(this.LF(
                            "Applied: duration={0} cooldown={1:F1}s.",
                            accelDuration >= SwimSprintInfiniteDuration ? "Infinite" : accelDuration.ToString("F1") + "s",
                            cooldown));
                        return true;
                    }
                    finally
                    {
                        AuraMonoPinFree(sprintPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(swimPin);
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

        private void SwimSprintSetStatus(string status)
        {
            this.swimSprintTweakStatus = status;
            if (!string.Equals(status, this.swimSprintLastLoggedStatus, StringComparison.Ordinal))
            {
                this.swimSprintLastLoggedStatus = status;
                ModLogger.Msg("[SwimSprint] " + status);
            }
        }
    }
}
