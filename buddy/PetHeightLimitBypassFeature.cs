using System;

namespace HeartopiaMod
{
    // Pet height-limit bypass — kills the client-side check behind the toast
    // "Too far to interact." (zhHans 距离太远，无法发起互动, ru "Слишком далеко, не дотянуться.").
    //
    // WHERE THE LIMIT COMES FROM (all client-side; the refusal never reaches the server):
    //
    //   * The toast text is Localization id 11041, and that id IS the error code:
    //     InteractErrorCode.BeyondHeightLimit = 11041. Despite the English wording it is NOT a
    //     distance check — it is a VERTICAL one:
    //
    //       AnimalComponent.CheckHigh(playerPos)
    //         => |playerPos.y - pet.y| > GetPetConfig().interactHeightLimit
    //
    //     GetPetConfig() is LevelScriptableConfig.Instance.catConfig (MeowComponent) or .dogConfig
    //     (DogComponent, WildAnimalComponent). Header of the field: 人交互时最大的高度差 — "max height
    //     difference for a person to interact".
    //
    //   * Two producers, both pet interactions (cat and dog):
    //       1. PetTouchUtil.IsExecutable(player, petHandle)  — petting (PetPetCommand + the click
    //          variant PetTouchClickCommand). Hunger is tested BEFORE the height.
    //       2. FeedPetCommand.IsExecutable()                 — FeedMeowCommand / HereWeGoCommand.
    //          Height is tested BEFORE CheckInteract (the busy test).
    //     CheckHigh has no other caller, and interactHeightLimit no other reader.
    //
    // LEVER — a DATA write, not a detour. Raising interactHeightLimit on both PetConfig objects
    // makes CheckHigh false and every other test in both producers runs untouched. A code-rewrite
    // detour (the InteractObstacle shape) would be WRONG for producer 2: because the height test
    // comes first, rewriting 11041 -> 0 would also skip the busy test behind it.
    //
    // Chain (embedded Mono — AuraMono only), same as JumpTuningFeature:
    //   LevelScriptableConfig -> static <Instance>k__BackingField (declared on the inflated generic
    //   base ConfigurableSingleton<LevelScriptableConfig>, so only TryGetAuraMonoStaticObjectField)
    //     -> catConfig / dogConfig (PetConfig reference fields) -> float interactHeightLimit
    //
    // The config is a session-lifetime singleton, so the write runs from the world-ready gate
    // (once per world load, which also covers an unlikely re-create), and a toggle flip re-arms
    // that callback so it applies/restores now. No OnUpdate timer. Originals are captured before
    // the first write and restored when the toggle goes off.
    //
    // Scope: client gate only. The pet RPC (PetProtocolManager.Pet / PrepareFeed) still goes to the
    // server, which may run its own check; if it refuses, the game shows its usual RPC-failure tip.
    public partial class HeartopiaComplete
    {
        // Far beyond any reachable height difference in a map; finite so the compare stays sane.
        private const float PetHeightLimitBypassValue = 10000f;

        private bool petHeightLimitBypassEnabled;
        private bool petHeightLimitPrevEnabled;
        private bool petHeightLimitCallbackRegistered;
        private string petHeightLimitStatus = "Idle.";
        private string petHeightLimitLastLoggedStatus;

        private IntPtr petHeightLimitLevelConfigClass;
        private IntPtr petHeightLimitPetConfigClass;
        private IntPtr petHeightLimitField;

        // Originals per config (cat, dog). Captured once per process — the asset defaults do not
        // change, so restoring them onto a re-created object stays correct.
        private bool petHeightLimitOriginalsCaptured;
        private float petHeightLimitOriginalCat;
        private float petHeightLimitOriginalDog;

        private void ProcessPetHeightLimitBypassOnUpdate()
        {
            bool enabled = this.petHeightLimitBypassEnabled;
            if (enabled == this.petHeightLimitPrevEnabled)
            {
                return;
            }

            this.petHeightLimitPrevEnabled = enabled;

            // Nothing was ever written: turning it off has nothing to undo.
            if (!enabled && !this.petHeightLimitOriginalsCaptured)
            {
                return;
            }

            if (!this.petHeightLimitCallbackRegistered)
            {
                this.petHeightLimitCallbackRegistered = true;
                this.RegisterWorldReadyCallback("PetHeightLimitBypass", this.TryApplyPetHeightLimitOnWorldReady);
            }
            else
            {
                this.ResetWorldReadyCallback("PetHeightLimitBypass");
            }
        }

        // World-ready callback: true = done for this world, false = retry (bounded by the gate).
        private bool TryApplyPetHeightLimitOnWorldReady()
        {
            try
            {
                if (this.petHeightLimitBypassEnabled)
                {
                    return this.ApplyPetHeightLimit(PetHeightLimitBypassValue, PetHeightLimitBypassValue, captureOriginals: true);
                }

                if (!this.petHeightLimitOriginalsCaptured)
                {
                    return true;
                }

                if (this.ApplyPetHeightLimit(this.petHeightLimitOriginalCat, this.petHeightLimitOriginalDog, captureOriginals: false))
                {
                    this.PetHeightLimitSetStatus(this.LF("Originals restored (cat={0:F2}m dog={1:F2}m).",
                        this.petHeightLimitOriginalCat, this.petHeightLimitOriginalDog));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                this.PetHeightLimitSetStatus("Error: " + ex.Message);
                return true;
            }
        }

        private unsafe bool ApplyPetHeightLimit(float catValue, float dogValue, bool captureOriginals)
        {
            if (auraMonoFieldSetValue == null || auraMonoObjectGetClass == null)
            {
                this.PetHeightLimitSetStatus("Error: AuraMono field-set export unavailable.");
                return true; // permanent — do not burn the retry budget
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.PetHeightLimitSetStatus("AuraMono not ready.");
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                this.PetHeightLimitSetStatus("Error: AuraMono pinning unavailable.");
                return true;
            }

            if (this.petHeightLimitLevelConfigClass == IntPtr.Zero)
            {
                this.petHeightLimitLevelConfigClass = this.FindAuraMonoClassInImages(
                    "ScriptsRefactory.LevelAndEntity.Utils",
                    "LevelScriptableConfig",
                    new[] { "XDTLevelAndEntity" });
                if (this.petHeightLimitLevelConfigClass == IntPtr.Zero)
                {
                    this.petHeightLimitLevelConfigClass = this.FindAuraMonoClassInAllLoadedImages(
                        "LevelScriptableConfig",
                        "ScriptsRefactory.LevelAndEntity.Utils");
                }
            }

            if (this.petHeightLimitLevelConfigClass == IntPtr.Zero)
            {
                this.PetHeightLimitSetStatus("LevelScriptableConfig class unresolved.");
                return false;
            }

            if (!this.TryGetAuraMonoStaticObjectField(this.petHeightLimitLevelConfigClass, "<Instance>k__BackingField", out IntPtr configObj)
                || configObj == IntPtr.Zero)
            {
                if (!this.TryGetAuraMonoStaticObjectField(this.petHeightLimitLevelConfigClass, "Instance", out configObj)
                    || configObj == IntPtr.Zero)
                {
                    this.PetHeightLimitSetStatus("LevelScriptableConfig.Instance null (config not loaded yet).");
                    return false;
                }
            }

            uint configPin = AuraMonoPinNew(configObj);
            try
            {
                if (!this.TryGetMonoObjectMember(configObj, "catConfig", out IntPtr catObj) || catObj == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(configObj, "dogConfig", out IntPtr dogObj) || dogObj == IntPtr.Zero)
                {
                    this.PetHeightLimitSetStatus("LevelScriptableConfig.catConfig/dogConfig null.");
                    return false;
                }

                uint catPin = AuraMonoPinNew(catObj);
                uint dogPin = AuraMonoPinNew(dogObj);
                try
                {
                    IntPtr klass = auraMonoObjectGetClass(catObj);
                    if (klass == IntPtr.Zero)
                    {
                        this.PetHeightLimitSetStatus("PetConfig class unresolved.");
                        return false;
                    }

                    if (klass != this.petHeightLimitPetConfigClass)
                    {
                        this.petHeightLimitPetConfigClass = klass;
                        this.petHeightLimitField = this.FindAuraMonoFieldOnHierarchy(klass, "interactHeightLimit");
                    }

                    if (this.petHeightLimitField == IntPtr.Zero)
                    {
                        this.PetHeightLimitSetStatus("Error: PetConfig.interactHeightLimit not found (game update?).");
                        return true;
                    }

                    // Never write a value we could not learn how to undo.
                    if (captureOriginals && !this.petHeightLimitOriginalsCaptured)
                    {
                        if (!this.TryGetMonoSingleMember(catObj, "interactHeightLimit", out float origCat)
                            || !this.TryGetMonoSingleMember(dogObj, "interactHeightLimit", out float origDog))
                        {
                            this.PetHeightLimitSetStatus("Original value read failed.");
                            return false;
                        }

                        this.petHeightLimitOriginalCat = origCat;
                        this.petHeightLimitOriginalDog = origDog;
                        this.petHeightLimitOriginalsCaptured = true;
                        ModLogger.Msg(this.LF("[PetHeightLimit] originals captured: cat={0:F2}m dog={1:F2}m",
                            origCat, origDog));
                    }

                    // Value-type float field: mono_field_set_value takes a pointer TO the value.
                    float catWrite = catValue;
                    float dogWrite = dogValue;
                    auraMonoFieldSetValue(catObj, this.petHeightLimitField, (IntPtr)(&catWrite));
                    auraMonoFieldSetValue(dogObj, this.petHeightLimitField, (IntPtr)(&dogWrite));

                    if (captureOriginals)
                    {
                        this.PetHeightLimitSetStatus(this.LF("Active (vanilla limit cat={0:F2}m dog={1:F2}m).",
                            this.petHeightLimitOriginalCat, this.petHeightLimitOriginalDog));
                    }

                    return true;
                }
                finally
                {
                    AuraMonoPinFree(dogPin);
                    AuraMonoPinFree(catPin);
                }
            }
            finally
            {
                AuraMonoPinFree(configPin);
            }
        }

        private void PetHeightLimitSetStatus(string status)
        {
            this.petHeightLimitStatus = status;
            if (!string.Equals(status, this.petHeightLimitLastLoggedStatus, StringComparison.Ordinal))
            {
                this.petHeightLimitLastLoggedStatus = status;
                ModLogger.Msg("[PetHeightLimit] " + status);
            }
        }
    }
}
