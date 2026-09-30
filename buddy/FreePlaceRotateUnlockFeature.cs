using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Free Place / Rotate Unlock — lets the game's OWN free-rotation (1°) and free-placement
    // modes be chosen for the items the game blacklists from them: every wall, floor, low wall
    // and light by entity type, plus 360 individual pieces (77 staircases, fan terraces,
    // platforms, pools, cubes, windows, pillars, glass floors).
    //
    // ── WHERE THE LIMIT ACTUALLY LIVES ───────────────────────────────────────────────────────────
    // Rotation is snapped once, at CONFIRM, in BuildSingle.GenConfirmOption:
    //
    //   dst.rotation = ReducePrecision(localRot,
    //                    EffectiveAnglePrecision(element.anglePrecision, settings.rotatePrecision), side);
    //   EffectiveAnglePrecision(p, mode) => mode != Free ? p : 1;
    //
    // and in god mode GodCraftMode.OnUpdate copies HomelandSystem.rotatePrecision into those
    // settings every frame. The core never consults the blacklist. The ONLY enforcement is UI:
    // BuildStatusPanel / SimulationBuildStatusPanel.ApplyFreePlaceRotateBlackList, reached from
    // EnableTarget on a focus change, which forces Free -> Fixed90 (and Free placement -> Fine)
    // and blocks the precision button while a blacklisted object is focused.
    //
    // That this is enforced nowhere else was proven by a live object: Ice Rink Flooring (35383,
    // listed by staticId, anglePrecision 90) placed and kept at exactly 30° local yaw, reached
    // through the group path — EnableTarget skips the blacklist entirely when the craft box is a
    // group, so a Free setting survives into the confirm.
    //
    // ── HOW IT IS OPENED ─────────────────────────────────────────────────────────────────────────
    // By emptying the two lists the panels ask, not by detouring the question:
    //   BuildModule._freePlaceRotateStaticIdBlackList   HashSet<int>
    //   BuildModule._freePlaceRotateEntityTypeBlackList HashSet<int>
    // IsFreePlaceRotateBlackListed has two ONE-argument overloads, (int) and (IBuildObject), and
    // name+arity cannot tell them apart — the same trap as DyeColorPanel.UpdatePartDyeColor.
    //
    // The lists are rebuilt by InitFreePlaceRotateBlackList only when TableData's table reference
    // changes. So: call that init ourselves first (the game then records the current table as its
    // source, through its own write — we never write a Mono reference field), THEN Clear() both
    // sets. A cheap count check re-clears if anything ever refills them. Switching off calls the
    // same init once, which restores the lists from the table — nothing to remember or undo.
    //
    // The panel re-evaluates on the next focus change: with the lists empty its else-branch
    // restores the player's saved precision and unblocks the button. So after toggling, refocus.
    //
    // ── KNOWN LIMITS ─────────────────────────────────────────────────────────────────────────────
    // * One list gates BOTH free rotation and free placement; emptying it offers both. Nothing is
    //   forced — they are the game's own buttons, left to the player.
    // * Verified live after save: Ice Rink Flooring keeps its angle, walls keep theirs, plain
    //   Flooring snaps back to 90°. The wire carries whole degrees (ToBuildingRotValue), so the
    //   floor snap is storage: a plain floor is a LocationCollection box plus `rotation /= 90`,
    //   drawn with Quaternion.identity. Walls decode the same collection yet keep their angle, so
    //   do not generalise from floors — off-grid walls land in some other representation.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string FreePlaceRotateTag = "FreePlaceRotate";
        private const float FreePlaceRotatePollInterval = 1f;

        private const string FreePlaceRotateIdsField = "_freePlaceRotateStaticIdBlackList";
        private const string FreePlaceRotateTypesField = "_freePlaceRotateEntityTypeBlackList";
        private const string FreePlaceRotateInitMethod = "InitFreePlaceRotateBlackList";

        private bool freePlaceRotateUnlockEnabled;
        private string freePlaceRotateUnlockStatus = "Idle.";

        private float freePlaceRotateNextPollAt;
        private FeatureBreakerState freePlaceRotateBreaker;
        private int freePlaceRotateEpoch = -1;
        private bool freePlaceRotateInitDone;        // we ran the game's init in this world
        private bool freePlaceRotateRestorePending;  // switched off: put the lists back once

        public bool FreePlaceRotateUnlockEnabled
        {
            get { return this.freePlaceRotateUnlockEnabled; }
        }

        public string FreePlaceRotateUnlockStatus
        {
            get { return this.freePlaceRotateUnlockStatus; }
        }

        private void ProcessFreePlaceRotateUnlockOnUpdate()
        {
            if (!this.IsWorldReady || (!this.freePlaceRotateUnlockEnabled && !this.freePlaceRotateRestorePending))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.freePlaceRotateNextPollAt || !this.freePlaceRotateBreaker.ShouldRun(now))
            {
                return;
            }
            this.freePlaceRotateNextPollAt = now + FreePlaceRotatePollInterval;

            if (this.freePlaceRotateEpoch != this.WorldReadyEpoch)
            {
                // A new level can bring a new BuildModule whose lists have never been built.
                this.freePlaceRotateEpoch = this.WorldReadyEpoch;
                this.freePlaceRotateInitDone = false;
            }

            try
            {
                if (this.freePlaceRotateUnlockEnabled)
                {
                    this.TryEmptyFreePlaceRotateBlackList();
                }
                else
                {
                    this.TryRestoreFreePlaceRotateBlackList();
                }
                this.freePlaceRotateBreaker.Success();
            }
            catch (Exception ex)
            {
                this.freePlaceRotateBreaker.Failure(FreePlaceRotateTag, ex, now);
            }
        }

        // Called from the UI toggle: flag only, the tick does the Mono work.
        internal void SetFreePlaceRotateUnlock(bool value)
        {
            if (value == this.freePlaceRotateUnlockEnabled)
            {
                return;
            }
            this.freePlaceRotateUnlockEnabled = value;
            this.freePlaceRotateRestorePending = !value;
            this.freePlaceRotateInitDone = false;
            this.freePlaceRotateNextPollAt = 0f;
            this.freePlaceRotateUnlockStatus = value ? "Waiting for the build module." : "Restoring the lists.";
            FeatureLog.Toggle(FreePlaceRotateTag, value);
        }

        private void TryEmptyFreePlaceRotateBlackList()
        {
            if (!this.TryGetFreePlaceRotateModule(out IntPtr module))
            {
                return;
            }

            uint modulePin = AuraMonoPinNew(module);
            try
            {
                if (!this.freePlaceRotateInitDone)
                {
                    // Let the game build the lists and record the table as their source, so they
                    // are not silently rebuilt behind our back on the next lookup.
                    if (!this.TryInvokeFreePlaceRotateVoid(module, FreePlaceRotateInitMethod))
                    {
                        this.FreePlaceRotateFail("BuildModule." + FreePlaceRotateInitMethod
                            + "() unavailable (game update?)");
                        return;
                    }
                    this.freePlaceRotateInitDone = true;
                }

                int cleared = this.ClearFreePlaceRotateSet(module, FreePlaceRotateIdsField, out bool idsOk)
                            + this.ClearFreePlaceRotateSet(module, FreePlaceRotateTypesField, out bool typesOk);
                if (!idsOk || !typesOk)
                {
                    this.FreePlaceRotateFail("blacklist fields not found on BuildModule (game update?)");
                    return;
                }

                if (cleared > 0)
                {
                    this.freePlaceRotateUnlockStatus = "Active — " + cleared
                        + " blacklist entries cleared; refocus the object to use Free rotate.";
                    FeatureLog.Life(FreePlaceRotateTag, "cleared " + cleared
                        + " free place/rotate blacklist entries (staticIds + entity types)");
                }
                else if (this.freePlaceRotateUnlockStatus.StartsWith("Waiting", StringComparison.Ordinal))
                {
                    this.freePlaceRotateUnlockStatus = "Active — blacklist is empty.";
                }
            }
            finally
            {
                if (modulePin != 0U) { AuraMonoPinFree(modulePin); }
            }
        }

        private void TryRestoreFreePlaceRotateBlackList()
        {
            if (!this.TryGetFreePlaceRotateModule(out IntPtr module))
            {
                // Nothing to restore into; a later module builds its own lists from the table.
                this.freePlaceRotateRestorePending = false;
                this.freePlaceRotateUnlockStatus = "Off.";
                return;
            }

            uint modulePin = AuraMonoPinNew(module);
            try
            {
                if (this.TryInvokeFreePlaceRotateVoid(module, FreePlaceRotateInitMethod))
                {
                    this.freePlaceRotateUnlockStatus = "Off — blacklist restored; refocus the object.";
                    FeatureLog.Life(FreePlaceRotateTag, "free place/rotate blacklist restored from the table");
                }
                else
                {
                    this.FreePlaceRotateFail("could not restore the blacklist — it returns on the next level load");
                }
                this.freePlaceRotateRestorePending = false;
            }
            finally
            {
                if (modulePin != 0U) { AuraMonoPinFree(modulePin); }
            }
        }

        private bool TryGetFreePlaceRotateModule(out IntPtr module)
        {
            module = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || !AuraMonoPinningAvailable)
            {
                return false;
            }
            // Quiet on a miss: outside a build-capable level there simply is no module yet.
            return this.TryGetPadBuildAuraModule(out module) && module != IntPtr.Zero;
        }

        // Returns how many entries the set held; `ok` is false when the field itself is missing.
        private int ClearFreePlaceRotateSet(IntPtr module, string field, out bool ok)
        {
            ok = false;
            if (!this.TryGetMonoObjectMember(module, field, out IntPtr set) || set == IntPtr.Zero)
            {
                return 0;
            }
            ok = true;

            uint pin = AuraMonoPinNew(set);
            try
            {
                // Resolved on the set's OWN class — a concrete HashSet<int>, nothing inflated.
                IntPtr klass = auraMonoObjectGetClass(set);
                IntPtr getCount = klass == IntPtr.Zero ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(klass, "get_Count", 0);
                int count = this.ReadFurnitureDyeDictCount(set, getCount);   // boxed-int get_Count read
                if (count <= 0)
                {
                    return 0;
                }
                return this.TryInvokeFreePlaceRotateVoid(set, "Clear") ? count : 0;
            }
            finally
            {
                if (pin != 0U) { AuraMonoPinFree(pin); }
            }
        }

        // TryInvokeAuraMonoZeroArg reports a void method as a failure (it wants a non-null result),
        // so void calls go through here: success is "resolved, and no exception".
        private unsafe bool TryInvokeFreePlaceRotateVoid(IntPtr obj, string methodName)
        {
            IntPtr klass = auraMonoObjectGetClass(obj);
            IntPtr method = klass == IntPtr.Zero ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(klass, methodName, 0);
            if (method == IntPtr.Zero)
            {
                return false;
            }
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, obj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }

        private void FreePlaceRotateFail(string message)
        {
            this.freePlaceRotateUnlockStatus = "Off — " + message;
            FeatureLog.Fail(FreePlaceRotateTag, message);
        }
    }
}
