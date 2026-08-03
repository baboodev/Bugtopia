using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Custom Jump — overrides the player's jump arc by mutating the game's live MotionConfig
    // (EcsClient.XDT.Scene.Shared.Data.ServerData.MotionConfig), the object every
    // LevelScriptableConfig jump/gravity static reads through.
    //
    // Mechanic (ilspy-dumps/XDTLevelAndEntity/.../Locomotion/StandLocomotion.cs) — 100% client side,
    // no signal and no packet, unlike the SetMoveSpeed route the trampoline uses:
    //  * :758 the launch is a DIRECT Velocity.y write of LevelScriptableConfig.JumpingInitSpeed the
    //    frame the JumpUp anim state is entered (times _tmpVerticalSpeedRate, which stays 1 here).
    //  * :406-443 gravity: zero for the first JumpingHoldingStartTime (0.1 s), then — while Space is
    //    still held and Velocity.y > 0 — a DYNAMIC value solved so the apex lands exactly on
    //    JumpingHighest above the launch point, otherwise plain Gravity. Fall clamps at
    //    FallingSpeedLimit.
    //
    // ⚠️ That solver is why JumpingInitSpeed alone is not a jump-height knob: with Space held the
    // game eats any extra launch speed and still tops out at JumpingHighest. So the UI edits the two
    // apexes separately in METRES and this file converts:
    //      hold apex  = JumpingHighest                    (verbatim)
    //      tap apex   = JumpingInitSpeed^2 / (2*|Gravity|) -> JumpingInitSpeed = sqrt(2*|G|*h)
    // (the same v=sqrt(2*g*h) inversion JumpBedComponent._GetInitSpeedByHeight does for trampolines).
    //
    // Config chain (embedded Mono — AuraMono only, per project policy for EcsClient/XDT* types):
    //   LevelScriptableConfig (image XDTLevelAndEntity)
    //     -> static <Instance>k__BackingField           ⚠️ declared on the INFLATED GENERIC base
    //                                                   ConfigurableSingleton<LevelScriptableConfig>,
    //                                                   so it MUST go through
    //                                                   TryGetAuraMonoStaticObjectField (which pairs
    //                                                   the field with its DECLARING class's vtable).
    //                                                   Invoking get_Instance instead would be an
    //                                                   inflated-generic static invoke — the known
    //                                                   NRE/crash trap.
    //       -> playerMotion                             (public FIELD, MotionConfig reference)
    //         -> float fields JumpingInitSpeed / JumpingHighest / Gravity / FallingSpeedLimit
    //            (mono_field_set_value with &float — a value-type field takes a pointer to the value)
    //
    // Same safety envelope as SwimSprintTweakFeature: the chain is re-resolved fresh on every apply
    // (no raw mono object ptr crosses a frame; each hop is pinned while in use), re-applied on a
    // throttle so a config reload cannot silently revert it, gated on IsGameDataQueryable (the static
    // read is the pre-login AV shape), fail-closed with a status string, and originals are captured
    // from the live object before the first write and restored on the toggle's falling edge.
    //
    // Scope note for the UI copy: these constants are GLOBAL and read by remote players' locomotions
    // on this client too (plus SkateLocomotion / HoldingHandLocomotion), so other people's jumps look
    // altered locally. Cosmetic only — nothing leaves the machine.
    public partial class HeartopiaComplete
    {
        // Game defaults (MotionConfig field initialisers). Gravity/fall limit are stored POSITIVE in
        // the mod and negated on write — the game keeps them signed.
        private const float JumpTuningHoldHeightDefault = 1.3f;   // JumpingHighest
        private const float JumpTuningTapHeightDefault = 0.72f;   // 4.8^2 / (2*16)
        private const float JumpTuningGravityDefault = 16f;       // |Gravity|
        private const float JumpTuningFallLimitDefault = 13f;     // |FallingSpeedLimit|

        // Upper bounds are a fall-through guard, not taste: XDCharacterController sweeps once per
        // frame, so at ~30 m/s a 60 fps step is already half a metre and the capsule starts tunnelling
        // through floors and stair colliders. 8 m of apex is ~16 m/s launch — still swept reliably.
        private const float JumpTuningHeightMin = 0.2f;
        private const float JumpTuningHeightMax = 8f;
        private const float JumpTuningGravityMin = 2f;
        private const float JumpTuningGravityMax = 60f;
        private const float JumpTuningFallLimitMin = 3f;
        private const float JumpTuningFallLimitMax = 80f;

        private const float JumpTuningApplyInterval = 0.5f;

        private bool jumpTuningEnabled;
        private float jumpTuningHoldHeight = JumpTuningHoldHeightDefault;
        private float jumpTuningTapHeight = JumpTuningTapHeightDefault;
        private float jumpTuningGravity = JumpTuningGravityDefault;
        private float jumpTuningFallSpeedLimit = JumpTuningFallLimitDefault;
        private bool jumpTuningPrevEnabled;
        private float jumpTuningNextApplyAt = -999f;
        private string jumpTuningStatus = "Idle.";
        private string jumpTuningLastLoggedStatus;
        private FeatureBreakerState jumpTuningBreaker;

        // Class/field pointers are mono metadata (image lifetime — safe to cache raw, unlike object
        // pointers). The field set is keyed on the resolved MotionConfig class ptr so a class change
        // re-resolves them.
        private IntPtr jumpTuningLevelConfigClass;
        private IntPtr jumpTuningMotionClass;
        private IntPtr jumpTuningInitSpeedField;
        private IntPtr jumpTuningHighestField;
        private IntPtr jumpTuningGravityField;
        private IntPtr jumpTuningFallLimitField;

        // Originals captured from the live config before the first write (restored on disable).
        // Captured once per process — a reloaded config carries the same asset defaults, so
        // restoring these onto a fresh object stays correct.
        private bool jumpTuningOriginalsCaptured;
        private float jumpTuningOriginalInitSpeed = 4.8f;
        private float jumpTuningOriginalHighest = JumpTuningHoldHeightDefault;
        private float jumpTuningOriginalGravity = -JumpTuningGravityDefault;
        private float jumpTuningOriginalFallLimit = -JumpTuningFallLimitDefault;

        // Falling-edge restore keeps retrying on the throttle until it lands or the deadline passes
        // (a config reload replaces the object with clean values anyway).
        private bool jumpTuningRestorePending;
        private float jumpTuningRestoreGiveUpAt;

        // Apex (metres) a tap reaches for a given launch speed / gravity — the inverse of the
        // sqrt below, used for the "current game value" readout when originals are captured.
        private static float JumpTuningApexFromSpeed(float initSpeed, float gravityMagnitude)
        {
            if (gravityMagnitude <= 0.0001f)
            {
                return 0f;
            }
            return initSpeed * initSpeed / (2f * gravityMagnitude);
        }

        private static float JumpTuningSpeedFromApex(float apex, float gravityMagnitude)
        {
            return Mathf.Sqrt(Mathf.Max(0f, apex) * 2f * Mathf.Max(0.0001f, gravityMagnitude));
        }

        private void ProcessJumpTuningOnUpdate()
        {
            float now = Time.unscaledTime;

            bool falling = !this.jumpTuningEnabled && this.jumpTuningPrevEnabled;
            this.jumpTuningPrevEnabled = this.jumpTuningEnabled;
            if (falling && this.jumpTuningOriginalsCaptured)
            {
                this.jumpTuningRestorePending = true;
                this.jumpTuningRestoreGiveUpAt = now + 10f;
                this.jumpTuningNextApplyAt = 0f; // restore promptly
            }

            if (!this.jumpTuningEnabled && !this.jumpTuningRestorePending)
            {
                return;
            }

            if (now < this.jumpTuningNextApplyAt)
            {
                return;
            }

            this.jumpTuningNextApplyAt = now + JumpTuningApplyInterval;

            // The static-field read below is the pre-login AV shape (a class the login screen never
            // initialised has no static block). Never attempt it outside a live world.
            if (!this.IsGameDataQueryable)
            {
                this.JumpTuningSetStatus("Waiting for a loaded world.");
                return;
            }

            if (!this.jumpTuningBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.jumpTuningEnabled)
                {
                    this.jumpTuningRestorePending = false;
                    float gravity = Mathf.Clamp(this.jumpTuningGravity, JumpTuningGravityMin, JumpTuningGravityMax);
                    float holdHeight = Mathf.Clamp(this.jumpTuningHoldHeight, JumpTuningHeightMin, JumpTuningHeightMax);
                    float tapHeight = Mathf.Clamp(this.jumpTuningTapHeight, JumpTuningHeightMin, JumpTuningHeightMax);
                    float fallLimit = Mathf.Clamp(this.jumpTuningFallSpeedLimit, JumpTuningFallLimitMin, JumpTuningFallLimitMax);

                    // Tap apex is expressed against the gravity we are about to write, not the
                    // game's — otherwise editing gravity would silently move the tap height too.
                    this.ApplyJumpTuningValues(
                        JumpTuningSpeedFromApex(tapHeight, gravity),
                        holdHeight,
                        -gravity,
                        -fallLimit,
                        captureOriginals: true);
                }
                else if (now >= this.jumpTuningRestoreGiveUpAt)
                {
                    this.jumpTuningRestorePending = false;
                    this.JumpTuningSetStatus("Restore window expired (config unreachable; a config reload resets it anyway).");
                }
                else if (this.ApplyJumpTuningValues(
                    this.jumpTuningOriginalInitSpeed,
                    this.jumpTuningOriginalHighest,
                    this.jumpTuningOriginalGravity,
                    this.jumpTuningOriginalFallLimit,
                    captureOriginals: false))
                {
                    this.jumpTuningRestorePending = false;
                    this.JumpTuningSetStatus(
                        "Originals restored (hold=" + this.jumpTuningOriginalHighest.ToString("F2")
                        + "m speed=" + this.jumpTuningOriginalInitSpeed.ToString("F2")
                        + " gravity=" + this.jumpTuningOriginalGravity.ToString("F1") + ").");
                }

                this.jumpTuningBreaker.Success();
            }
            catch (Exception ex)
            {
                this.jumpTuningBreaker.Failure("JumpTuning", ex, now);
                this.jumpTuningStatus = "Error: " + ex.Message;
            }
        }

        // Resolves LevelScriptableConfig.Instance.playerMotion fresh and writes the four jump floats.
        // Returns false (status set, no partial capture-then-skip) on any unresolved hop.
        private unsafe bool ApplyJumpTuningValues(float initSpeed, float highest, float gravity, float fallLimit, bool captureOriginals)
        {
            if (auraMonoFieldSetValue == null || auraMonoObjectGetClass == null)
            {
                this.JumpTuningSetStatus("AuraMono field-set export unavailable.");
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.JumpTuningSetStatus("AuraMono not ready.");
                return false;
            }

            if (this.jumpTuningLevelConfigClass == IntPtr.Zero)
            {
                this.jumpTuningLevelConfigClass = this.FindAuraMonoClassInImages(
                    "ScriptsRefactory.LevelAndEntity.Utils",
                    "LevelScriptableConfig",
                    new[] { "XDTLevelAndEntity" });
                if (this.jumpTuningLevelConfigClass == IntPtr.Zero)
                {
                    this.jumpTuningLevelConfigClass = this.FindAuraMonoClassInAllLoadedImages(
                        "LevelScriptableConfig",
                        "ScriptsRefactory.LevelAndEntity.Utils");
                }
            }

            if (this.jumpTuningLevelConfigClass == IntPtr.Zero)
            {
                this.JumpTuningSetStatus("LevelScriptableConfig class unresolved.");
                return false;
            }

            // ConfigurableSingleton<T>.Instance is an auto-property, so the storage is the compiler
            // backing field; "Instance" is probed too in case a future build declares it plainly.
            // TryGetAuraMonoStaticObjectField is the only sanctioned path here: the field belongs to
            // the inflated generic BASE, and reading it off this class's vtable would be a near-null
            // deref (the child declares no statics of its own).
            if (!this.TryGetAuraMonoStaticObjectField(this.jumpTuningLevelConfigClass, "<Instance>k__BackingField", out IntPtr configObj)
                || configObj == IntPtr.Zero)
            {
                if (!this.TryGetAuraMonoStaticObjectField(this.jumpTuningLevelConfigClass, "Instance", out configObj)
                    || configObj == IntPtr.Zero)
                {
                    this.JumpTuningSetStatus("LevelScriptableConfig.Instance null (config not loaded yet).");
                    return false;
                }
            }

            uint configPin = AuraMonoPinNew(configObj);
            try
            {
                if (!this.TryGetMonoObjectMember(configObj, "playerMotion", out IntPtr motionObj) || motionObj == IntPtr.Zero)
                {
                    this.JumpTuningSetStatus("LevelScriptableConfig.playerMotion null.");
                    return false;
                }

                uint motionPin = AuraMonoPinNew(motionObj);
                try
                {
                    IntPtr klass = auraMonoObjectGetClass(motionObj);
                    if (klass == IntPtr.Zero)
                    {
                        this.JumpTuningSetStatus("MotionConfig class unresolved.");
                        return false;
                    }

                    if (klass != this.jumpTuningMotionClass)
                    {
                        this.jumpTuningMotionClass = klass;
                        this.jumpTuningInitSpeedField = this.FindAuraMonoFieldOnHierarchy(klass, "JumpingInitSpeed");
                        this.jumpTuningHighestField = this.FindAuraMonoFieldOnHierarchy(klass, "JumpingHighest");
                        this.jumpTuningGravityField = this.FindAuraMonoFieldOnHierarchy(klass, "Gravity");
                        this.jumpTuningFallLimitField = this.FindAuraMonoFieldOnHierarchy(klass, "FallingSpeedLimit");
                    }

                    if (this.jumpTuningInitSpeedField == IntPtr.Zero || this.jumpTuningHighestField == IntPtr.Zero
                        || this.jumpTuningGravityField == IntPtr.Zero || this.jumpTuningFallLimitField == IntPtr.Zero)
                    {
                        this.JumpTuningSetStatus("MotionConfig jump fields unresolved.");
                        return false;
                    }

                    // Capture originals BEFORE the first write; failing the read fails the whole
                    // apply (never write values we could not learn how to undo).
                    if (captureOriginals && !this.jumpTuningOriginalsCaptured)
                    {
                        if (!this.TryGetMonoSingleMember(motionObj, "JumpingInitSpeed", out float origInitSpeed)
                            || !this.TryGetMonoSingleMember(motionObj, "JumpingHighest", out float origHighest)
                            || !this.TryGetMonoSingleMember(motionObj, "Gravity", out float origGravity)
                            || !this.TryGetMonoSingleMember(motionObj, "FallingSpeedLimit", out float origFallLimit))
                        {
                            this.JumpTuningSetStatus("Original value read failed.");
                            return false;
                        }

                        this.jumpTuningOriginalInitSpeed = origInitSpeed;
                        this.jumpTuningOriginalHighest = origHighest;
                        this.jumpTuningOriginalGravity = origGravity;
                        this.jumpTuningOriginalFallLimit = origFallLimit;
                        this.jumpTuningOriginalsCaptured = true;
                        ModLogger.Msg(this.LF(
                            "[JumpTuning] originals captured: initSpeed={0:F2} (tap apex {1:F2}m) highest={2:F2}m gravity={3:F1} fallLimit={4:F1}",
                            origInitSpeed,
                            JumpTuningApexFromSpeed(origInitSpeed, Mathf.Abs(origGravity)),
                            origHighest,
                            origGravity,
                            origFallLimit));
                    }

                    // Value-type float fields: mono_field_set_value takes a pointer TO the value.
                    float initSpeedValue = initSpeed;
                    float highestValue = highest;
                    float gravityValue = gravity;
                    float fallLimitValue = fallLimit;
                    auraMonoFieldSetValue(motionObj, this.jumpTuningInitSpeedField, (IntPtr)(&initSpeedValue));
                    auraMonoFieldSetValue(motionObj, this.jumpTuningHighestField, (IntPtr)(&highestValue));
                    auraMonoFieldSetValue(motionObj, this.jumpTuningGravityField, (IntPtr)(&gravityValue));
                    auraMonoFieldSetValue(motionObj, this.jumpTuningFallLimitField, (IntPtr)(&fallLimitValue));

                    this.JumpTuningSetStatus(this.LF(
                        "Applied: hold={0:F2}m tap={1:F2}m gravity={2:F1} fall={3:F1}.",
                        highest,
                        JumpTuningApexFromSpeed(initSpeed, Mathf.Abs(gravity)),
                        gravity,
                        fallLimit));
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(motionPin);
                }
            }
            finally
            {
                AuraMonoPinFree(configPin);
            }
        }

        // "Reset to Defaults" — prefers the values the LIVE game had (captured off the config object
        // before our first write) over the compiled-in constants, so on a build that retunes the
        // stock arc the button still restores THAT arc rather than yesterday's numbers. Falls back
        // to the constants until a capture has happened (feature never enabled this session).
        //
        // Note this only rewrites the mod's four inputs; it deliberately does NOT touch the toggle.
        // Leaving Custom Jump on with default values re-applies the stock arc explicitly, which is
        // the same end state as turning it off — minus the restore round-trip.
        private void ResetJumpTuningToGameDefaults()
        {
            float gravity = JumpTuningGravityDefault;
            float holdHeight = JumpTuningHoldHeightDefault;
            float tapHeight = JumpTuningTapHeightDefault;
            float fallLimit = JumpTuningFallLimitDefault;

            if (this.jumpTuningOriginalsCaptured)
            {
                gravity = Mathf.Abs(this.jumpTuningOriginalGravity);
                holdHeight = this.jumpTuningOriginalHighest;
                // The captured original is a launch SPEED; the UI edits an apex.
                tapHeight = JumpTuningApexFromSpeed(this.jumpTuningOriginalInitSpeed, gravity);
                fallLimit = Mathf.Abs(this.jumpTuningOriginalFallLimit);
            }

            this.jumpTuningHoldHeight = Mathf.Clamp(holdHeight, JumpTuningHeightMin, JumpTuningHeightMax);
            this.jumpTuningTapHeight = Mathf.Clamp(tapHeight, JumpTuningHeightMin, JumpTuningHeightMax);
            this.jumpTuningGravity = Mathf.Clamp(gravity, JumpTuningGravityMin, JumpTuningGravityMax);
            this.jumpTuningFallSpeedLimit = Mathf.Clamp(fallLimit, JumpTuningFallLimitMin, JumpTuningFallLimitMax);
            this.jumpTuningNextApplyAt = 0f; // push it out on the next tick, not after the throttle
        }

        private void JumpTuningSetStatus(string status)
        {
            this.jumpTuningStatus = status;
            if (!string.Equals(status, this.jumpTuningLastLoggedStatus, StringComparison.Ordinal))
            {
                this.jumpTuningLastLoggedStatus = status;
                ModLogger.Msg("[JumpTuning] " + status);
            }
        }
    }
}
