using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // REPAIR THROW ANIMATION TRIM
    //
    // WHY: the direct repair-kit send (PutRecoverToolCommand straight through
    // ToolRestorerProtocolManager.NotifyThrowToolRestorer) is instant but has to invent the landing
    // spot itself, and it cannot ground-snap — the game runs its own XDT.Physics, so every
    // UnityEngine.Physics cast from the mod returns nothing (memory:
    // unity-physics-raycast-from-mod). The GAME path gets the placement right for free:
    // BackpackToolRestorer.IsExecutable runs PlayerSphereChecker.TryFindThrowPoint, which
    // line-of-sight tests the aim, raycasts down onto the real ground and resolves parentNetId — so
    // on a ship the device rides the ship instead of being left in world space. Its only cost is
    // ~2s of animation. This feature keeps the game's placement and removes almost all of that.
    //
    // WHAT CANNOT BE CUT: the send is driven BY the animation. PlayerThrowSomethingAction wires an
    // anim listener; the clip's "throw" LogicSignal calls ThrowSomething(), and only then does the
    // Shoot tick reach NoticeThrowSomething -> the network command. Ending the clip before that
    // signal consumes nothing and throws nothing. So the wind-up cannot simply be cancelled — it
    // has to be SHORT-CIRCUITED instead, which is what step 1 below does.
    //
    // THE THREE CUTS (PlayerThrowSomethingAction, XDTLevelAndEntity.Gameplay.Action):
    //   1. wind-up  — invoke ThrowSomething() ourselves the moment _state == Start, instead of
    //                 waiting for the animator to emit "throw". It creates the thrown entity and
    //                 moves the clip to Shoot. When the real signal arrives later it re-enters
    //                 ThrowSomething() and no-ops on its own `_entity == null` guard.
    //   2. flight   — the 0.65s hop is cosmetic: NoticeThrowSomething sends `_arg.targetPos`, never
    //                 the motion's end point. Re-invoking StartShoot(pos, 0.01f) makes JumpMotion
    //                 report normalizedTime >= 1 on the very next tick, so the command goes out
    //                 immediately. Deliberately a method call, not a `_shootMotion = null` field
    //                 write — same effect via the game's own API, no raw ref-field store.
    //   3. tail     — once _state == SendCommand the command is already on the wire, so
    //                 ActorActionGraph.EndCasting() drops the remaining ~1.2s + idle wait. Same
    //                 lever, same reasoning and the same "this is the termination the game itself
    //                 uses" argument as CraftAnimationSkipFeature — NOT Stop(), which also tears
    //                 down the motion graph.
    //
    // STILL PAID: the CanPut server round-trip and the PlayerState.Free gate, both of which run in
    // BackpackToolRestorer.IsExecutable before any animation exists. That gate is exactly why the
    // direct send remains the right choice mid-fishing.
    //
    // SCOPE BELT: every cut is gated on actionGraph.actionContext being the tool-restorer arg, so
    // this can only ever touch a repair-kit throw — the same guard shape the craft skip uses.
    // Local player only. All game access is public API + private-field READS over AuraMono; no
    // native detour, no IL2CPP .text patch.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // PlayerThrowSomethingAction.State
        private const int RepairThrowStateStart = 0;
        private const int RepairThrowStateShoot = 1;
        private const int RepairThrowStateSendCommand = 2;

        private const string RepairThrowTrimContextTypeName = "PlayerToolRestorerParaBase";
        private const string RepairThrowTrimIdleStatus = "No repair-kit throw in flight.";
        private const float RepairThrowTrimWindowSeconds = 8f;
        private const float RepairThrowTrimWindowPollInterval = 0.03f;
        private const float RepairThrowTrimFallbackPollInterval = 0.3f;
        private const float RepairThrowTrimReArmSeconds = 0.25f;
        // Any tiny non-zero cost time works; 0.01s is below one frame at 60fps, so the first Shoot
        // tick already reports the hop finished. Exactly 0 is avoided so JumpMotion cannot divide
        // by zero into a NaN normalizedTime.
        private const float RepairThrowTrimInstantFlightSeconds = 0.01f;

        internal static bool MasterLogRepairThrowTrim = false;

        private bool trimRepairThrowAnimation;
        private float repairThrowTrimWindowEndsAt;
        private float repairThrowTrimNextPollAt;
        private float repairThrowTrimReArmAt;
        private int repairThrowTrimCount;
        private string repairThrowTrimStatus = "Idle.";
        private string repairThrowTrimLastLoggedStatus;
        private FeatureBreakerState repairThrowTrimBreaker;

        // Opened by the animated repair path so the common case costs no polling at all; the slow
        // fallback poll below still catches a kit thrown by hand from the bag.
        internal void NotifyRepairThrowAnimationStarted()
        {
            if (!this.trimRepairThrowAnimation)
            {
                return;
            }
            this.repairThrowTrimWindowEndsAt = Time.unscaledTime + RepairThrowTrimWindowSeconds;
            this.repairThrowTrimNextPollAt = 0f;
        }

        private void ProcessRepairThrowAnimationTrimOnUpdate()
        {
            if (!this.trimRepairThrowAnimation)
            {
                // Drop a window left over from before the toggle went off, so re-enabling later
                // cannot fire into a throw that already finished.
                this.repairThrowTrimWindowEndsAt = 0f;
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.repairThrowTrimNextPollAt || now < this.repairThrowTrimReArmAt)
            {
                return;
            }

            bool inWindow = now < this.repairThrowTrimWindowEndsAt;
            this.repairThrowTrimNextPollAt = now
                + (inWindow ? RepairThrowTrimWindowPollInterval : RepairThrowTrimFallbackPollInterval);

            if (!this.repairThrowTrimBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.TryTrimRepairThrowAnimation(out string status))
                {
                    this.repairThrowTrimCount++;
                    this.repairThrowTrimWindowEndsAt = 0f;
                    this.repairThrowTrimReArmAt = now + RepairThrowTrimReArmSeconds;
                    this.RepairThrowTrimSetStatus("Trimmed " + this.repairThrowTrimCount + " throw animation(s).");
                }
                else if (!string.Equals(status, RepairThrowTrimIdleStatus, StringComparison.Ordinal))
                {
                    // "Nothing in flight" is the normal poll result — only surface the rest.
                    this.RepairThrowTrimSetStatus(status);
                }

                this.repairThrowTrimBreaker.Success();
            }
            catch (Exception ex)
            {
                this.repairThrowTrimBreaker.Failure("RepairThrowTrim", ex, now);
                this.RepairThrowTrimSetStatus("Error: " + ex.Message);
            }
        }

        // One poll step over the live throw. Returns true only on the final cut (the tail), so the
        // window stays open across the two or three ticks the sequence needs.
        private bool TryTrimRepairThrowAnimation(out string status)
        {
            status = "AuraMono unavailable.";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            // Fail closed: without pinning the objects held across the next allocating invoke are
            // moving-GC stale pointers (memory: auramono-pinning-fail-closed).
            if (!AuraMonoPinningAvailable)
            {
                status = "AuraMono pinning unavailable.";
                return false;
            }

            if (!this.TryResolveCraftAuraLocalPlayerObject(out IntPtr playerObj, out _) || playerObj == IntPtr.Zero)
            {
                status = "Local player unavailable.";
                return false;
            }

            uint playerPin = AuraMonoPinNew(playerObj);
            if (playerPin == 0U)
            {
                status = "Player pin failed.";
                return false;
            }

            try
            {
                IntPtr playerClass = auraMonoObjectGetClass(playerObj);
                IntPtr getActionGraph = playerClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(playerClass, "get_actionGraph", 0)
                    : IntPtr.Zero;
                if (getActionGraph == IntPtr.Zero)
                {
                    status = "get_actionGraph unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr graphObj = auraMonoRuntimeInvoke(getActionGraph, playerObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || graphObj == IntPtr.Zero)
                {
                    status = "actionGraph unavailable.";
                    return false;
                }

                uint graphPin = AuraMonoPinNew(graphObj);
                if (graphPin == 0U)
                {
                    status = "ActionGraph pin failed.";
                    return false;
                }

                try
                {
                    if (!this.IsRepairThrowCastInFlight(graphObj))
                    {
                        status = RepairThrowTrimIdleStatus;
                        return false;
                    }

                    return this.TryStepRepairThrowClip(graphObj, out status);
                }
                finally
                {
                    AuraMonoPinFree(graphPin);
                }
            }
            finally
            {
                AuraMonoPinFree(playerPin);
            }
        }

        // actionGraph.actionContext is the tool-restorer arg — i.e. a repair-kit throw is casting.
        // AbilityCaster._context is set once per cast, so this covers the whole clip.
        private bool IsRepairThrowCastInFlight(IntPtr graphObj)
        {
            IntPtr graphClass = auraMonoObjectGetClass(graphObj);
            IntPtr getContext = graphClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(graphClass, "get_actionContext", 0)
                : IntPtr.Zero;
            if (getContext == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr contextObj = auraMonoRuntimeInvoke(getContext, graphObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || contextObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr contextClass = auraMonoObjectGetClass(contextObj);
            if (contextClass == IntPtr.Zero)
            {
                return false;
            }

            // Suffix match so a namespace move between builds does not break the gate.
            string displayName = this.GetAuraMonoClassDisplayName(contextClass);
            return !string.IsNullOrEmpty(displayName)
                && (displayName.EndsWith("." + RepairThrowTrimContextTypeName, StringComparison.Ordinal)
                    || string.Equals(displayName, RepairThrowTrimContextTypeName, StringComparison.Ordinal));
        }

        // Drives the clip forward one step per poll. ActorActionGraph.abilityCaster is private, but
        // AbilityCaster.actionClip is public and is the LIVE clip — ActionContext.GetExecuteAction()
        // looks like the obvious route and is NOT: it is a factory (Activator.CreateInstance).
        private unsafe bool TryStepRepairThrowClip(IntPtr graphObj, out string status)
        {
            status = "abilityCaster unavailable.";
            if (!this.TryReadAuraMonoObjectField(graphObj, out IntPtr casterObj, "abilityCaster")
                || casterObj == IntPtr.Zero)
            {
                return false;
            }

            uint casterPin = AuraMonoPinNew(casterObj);
            if (casterPin == 0U)
            {
                status = "AbilityCaster pin failed.";
                return false;
            }

            try
            {
                IntPtr casterClass = auraMonoObjectGetClass(casterObj);
                IntPtr getActionClip = casterClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(casterClass, "get_actionClip", 0)
                    : IntPtr.Zero;
                if (getActionClip == IntPtr.Zero)
                {
                    status = "get_actionClip unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr clipObj = auraMonoRuntimeInvoke(getActionClip, casterObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || clipObj == IntPtr.Zero)
                {
                    status = "actionClip unavailable.";
                    return false;
                }

                uint clipPin = AuraMonoPinNew(clipObj);
                if (clipPin == 0U)
                {
                    status = "ActionClip pin failed.";
                    return false;
                }

                try
                {
                    IntPtr clipClass = auraMonoObjectGetClass(clipObj);
                    if (clipClass == IntPtr.Zero)
                    {
                        status = "ActionClip class unavailable.";
                        return false;
                    }

                    // Resolve the field before reading it: TryReadAuraMonoUIntField answers 0 both
                    // for "the value is 0" and for "the read failed", and 0 is State.Start — the one
                    // value this method acts on. Checking the field exists keeps a renamed field
                    // after a game update from masquerading as "a throw is starting" forever.
                    if (this.FindAuraMonoFieldOnHierarchy(clipClass, "_state") == IntPtr.Zero)
                    {
                        status = "_state unavailable.";
                        return false;
                    }

                    // _state is only Start once OnBehaveStart resolved the left_throw socket, so
                    // seeing Start proves the clip is live and ThrowSomething() has what it needs.
                    // (Its own `_entity == null && _arg != null` guard makes a mistimed call a no-op.)
                    uint state = this.TryReadAuraMonoUIntField(clipObj, "_state");

                    if (state == RepairThrowStateStart)
                    {
                        IntPtr throwSomething = this.FindAuraMonoMethodOnHierarchy(clipClass, "ThrowSomething", 0);
                        if (throwSomething == IntPtr.Zero)
                        {
                            status = "ThrowSomething() unavailable.";
                            return false;
                        }

                        exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(throwSomething, clipObj, IntPtr.Zero, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            status = "ThrowSomething() threw.";
                            return false;
                        }

                        // It sets _state = Shoot and starts the 0.65s hop; collapse that hop now so
                        // the command goes out on the very next tick instead of ~40 frames later.
                        state = this.TryReadAuraMonoUIntField(clipObj, "_state");
                        if (MasterLogRepairThrowTrim)
                        {
                            ModLogger.Msg("[RepairThrowTrim] ThrowSomething() forced, state=" + state);
                        }
                    }

                    if (state == RepairThrowStateShoot)
                    {
                        this.TryCollapseRepairThrowFlight(clipObj, clipClass);
                        status = "Throw short-circuited; waiting for the send.";
                        return false;
                    }

                    if (state == RepairThrowStateSendCommand)
                    {
                        // NoticeThrowSomething already ran — PutRecoverToolCommand is on the wire,
                        // so the rest of the clip is pure decoration.
                        IntPtr graphClass = auraMonoObjectGetClass(graphObj);
                        // paramCount 0 selects the inherited ActorActionGraph.EndCasting() — NOT the
                        // 1-arg PlayerActionGraph.EndCasting(ActionContext) overload on the subclass.
                        IntPtr endCasting = graphClass != IntPtr.Zero
                            ? this.FindAuraMonoMethodOnHierarchy(graphClass, "EndCasting", 0)
                            : IntPtr.Zero;
                        if (endCasting == IntPtr.Zero)
                        {
                            status = "EndCasting() unavailable.";
                            return false;
                        }

                        exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(endCasting, graphObj, IntPtr.Zero, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            // AbilityCaster.EndCasting throws while the caster is mid Beging/Ending —
                            // transient, so report and let the next poll retry.
                            status = "EndCasting() threw.";
                            return false;
                        }

                        status = "Throw animation trimmed.";
                        return true;
                    }

                    status = RepairThrowTrimIdleStatus;
                    return false;
                }
                finally
                {
                    AuraMonoPinFree(clipPin);
                }
            }
            finally
            {
                AuraMonoPinFree(casterPin);
            }
        }

        // StartShoot(endPosition, costTime) resets the existing JumpMotion, so re-calling it with a
        // sub-frame cost time ends the hop immediately. endPosition is cosmetic — the command sends
        // _arg.targetPos, which the game already resolved through TryFindThrowPoint.
        private unsafe void TryCollapseRepairThrowFlight(IntPtr clipObj, IntPtr clipClass)
        {
            IntPtr startShoot = this.FindAuraMonoMethodOnHierarchy(clipClass, "StartShoot", 2);
            if (startShoot == IntPtr.Zero)
            {
                return;
            }

            Vector3 endPosition = Vector3.zero;
            this.TryGetLocalPlayerPosition(out endPosition);
            float costTime = RepairThrowTrimInstantFlightSeconds;

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&endPosition);
            args[1] = (IntPtr)(&costTime);
            auraMonoRuntimeInvoke(startShoot, clipObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero && MasterLogRepairThrowTrim)
            {
                ModLogger.Msg("[RepairThrowTrim] StartShoot() threw; the 0.65s hop stays.");
            }
        }

        // TIER 1 — see the twin helper in CraftAnimationSkipFeature.cs. Same defect, same fix:
        // this was the feature's only output and it was gated behind an unreachable flag.
        private void RepairThrowTrimSetStatus(string status)
        {
            this.repairThrowTrimStatus = status;
            if (!string.Equals(status, this.repairThrowTrimLastLoggedStatus, StringComparison.Ordinal))
            {
                this.repairThrowTrimLastLoggedStatus = status;
                FeatureLog.Life("RepairThrowTrim", status);
            }
        }
    }
}
