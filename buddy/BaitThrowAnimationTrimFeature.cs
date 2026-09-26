using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // BAIT THROW ANIMATION TRIM
    //
    // The bait / fish-attractor twin of RepairThrowAnimationTrimFeature. WHY: the game's own item
    // function (ChumBait 103 / FishingLureBall 108) is the path that gets the landing spot right —
    // BackpackCmdThrowBait / BackpackFishingLureBall call FishHelper.CanThrowAutoBait, which puts
    // the target ON THE WATER SURFACE ahead, in a legal fishing area — but it costs ~2s of
    // "spread bait" animation. This keeps the game's placement and removes the animation.
    //
    // THE CUTS, measured live 2026-09-27 (tools/BaitThrowProbe, river bank, rod equipped):
    //
    //   Attractor — PlayerFishingLureBallEvent, the same Start/Shoot/SendCommand shape as the repair
    //   kit's PlayerThrowSomethingAction:
    //     1. _state == Start       -> ThrowBall() ourselves instead of waiting for the animator's
    //                                 "throw" signal (its own `_lureBallComponent == null` guard
    //                                 makes the later real signal a no-op).
    //     2. _state == Shoot       -> _lureBallComponent.StartShoot(target, 0.01f): the 0.95s arc is
    //                                 cosmetic, OnBehaveTick sends `target` (the CanThrowAutoBait
    //                                 point) once the ball's JumpMotion reports done.
    //     3. _state == SendCommand -> ActorActionGraph.EndCasting(): CmdUseFishTrapDevice is already
    //                                 on the wire; OnBehaveFinish destroys the visual ball.
    //     Whole clip ~0.09s instead of ~2s; device spawned on the water, bag 19 -> 18.
    //
    //   Bait — ActionClipBait has no states: it sends CmdScatterBait from OnBehaveFinish, and
    //   ActorBehaveClip.OnDestroy runs OnBehaveFinish whenever the clip reached _tPhase == Action.
    //   So one EndCasting() once _tPhase == Action IS the send. Measured: one frame, bag 330 -> 329.
    //   Before Action (still Switch, waiting on the animator controller) OnBehaveFinish would be
    //   SKIPPED and nothing would be sent — hence the _tPhase gate.
    //
    // SCOPE: only runs inside a short window opened by the mod's own throw
    // (NotifyBaitThrowAnimationStarted from TryThrowFishBaitItemFromBag), and every cut is gated on
    // actionGraph.actionContext being the bait / lure-ball arg — the craft-skip / repair-trim guard
    // shape. A bait thrown by hand from the bag keeps its animation. Local player only; public API
    // plus private-field READS over AuraMono; no native detour, no IL2CPP .text patch.
    //
    // STILL PAID: the item function's IsExecutable gates (PlayerState.Free, not on a moving
    // platform). When the game refuses, no clip ever appears and the window just expires — that is
    // what the Skip Bait Animation direct send is for.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // PlayerFishingLureBallEvent.State
        private const int BaitThrowLureStateStart = 0;
        private const int BaitThrowLureStateShoot = 1;
        private const int BaitThrowLureStateSendCommand = 2;
        // ActorBehaveClip.Phase.Action — OnBehaveStart has run.
        private const int BaitThrowBehavePhaseAction = 2;

        private const string BaitThrowLureContextTypeName = "PlayerFishingLureBallParaBase";
        private const string BaitThrowBaitContextTypeName = "PlayerBaitParaBase";
        private const float BaitThrowTrimWindowSeconds = 3f;
        // Same reasoning as RepairThrowTrimInstantFlightSeconds: below one frame, never exactly 0.
        private const float BaitThrowTrimInstantFlightSeconds = 0.01f;

        private bool trimBaitThrowAnimation = true;
        private float baitThrowTrimWindowEndsAt;
        private bool baitThrowTrimSawClip;
        private bool baitThrowTrimShootCollapsed;
        private int baitThrowTrimCount;
        private string baitThrowTrimLastLoggedStatus;
        private FeatureBreakerState baitThrowTrimBreaker;

        internal void NotifyBaitThrowAnimationStarted()
        {
            if (!this.trimBaitThrowAnimation)
            {
                return;
            }
            this.baitThrowTrimWindowEndsAt = Time.unscaledTime + BaitThrowTrimWindowSeconds;
            this.baitThrowTrimSawClip = false;
            this.baitThrowTrimShootCollapsed = false;
        }

        private void ProcessBaitThrowAnimationTrimOnUpdate()
        {
            if (this.baitThrowTrimWindowEndsAt <= 0f)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!this.trimBaitThrowAnimation || now >= this.baitThrowTrimWindowEndsAt)
            {
                if (this.trimBaitThrowAnimation && !this.baitThrowTrimSawClip)
                {
                    // The item function was sent but the game never started the throw — its
                    // IsExecutable refused (not Free / on a moving platform) and showed its own tip.
                    this.BaitThrowTrimSetStatus("No throw clip appeared; the game refused the throw.");
                }
                this.baitThrowTrimWindowEndsAt = 0f;
                return;
            }

            if (!this.baitThrowTrimBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                // Every frame while the window is open: the attractor needs three consecutive
                // ticks (Start -> Shoot -> SendCommand) and each one is a frame of animation saved.
                if (this.TryTrimBaitThrowAnimation(out string status))
                {
                    this.baitThrowTrimCount++;
                    this.baitThrowTrimWindowEndsAt = 0f;
                    this.BaitThrowTrimSetStatus("Trimmed " + this.baitThrowTrimCount + " bait throw animation(s).");
                }
                else if (status != null)
                {
                    this.BaitThrowTrimSetStatus(status);
                }

                this.baitThrowTrimBreaker.Success();
            }
            catch (Exception ex)
            {
                this.baitThrowTrimBreaker.Failure("BaitThrowTrim", ex, now);
                this.BaitThrowTrimSetStatus("Error: " + ex.Message);
                this.baitThrowTrimWindowEndsAt = 0f;
            }
        }

        // One step over the live throw. True only once the throw is finished (EndCasting done).
        // `status` null = nothing worth surfacing (no clip yet).
        private bool TryTrimBaitThrowAnimation(out string status)
        {
            status = "AuraMono unavailable.";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            // Fail closed (memory: auramono-pinning-fail-closed).
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
                    int kind = this.GetBaitThrowCastKind(graphObj);
                    if (kind == 0)
                    {
                        status = null;
                        return false;
                    }

                    this.baitThrowTrimSawClip = true;
                    return this.TryStepBaitThrowClip(graphObj, kind == 1, out status);
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

        // 1 = attractor (lure ball), 2 = bait, 0 = something else / nothing is casting.
        private int GetBaitThrowCastKind(IntPtr graphObj)
        {
            IntPtr graphClass = auraMonoObjectGetClass(graphObj);
            IntPtr getContext = graphClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(graphClass, "get_actionContext", 0)
                : IntPtr.Zero;
            if (getContext == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr contextObj = auraMonoRuntimeInvoke(getContext, graphObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || contextObj == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr contextClass = auraMonoObjectGetClass(contextObj);
            string displayName = contextClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(contextClass) : null;
            if (string.IsNullOrEmpty(displayName))
            {
                return 0;
            }

            // Suffix match so a namespace move between builds does not break the gate.
            if (IsBaitThrowContextName(displayName, BaitThrowLureContextTypeName))
            {
                return 1;
            }
            return IsBaitThrowContextName(displayName, BaitThrowBaitContextTypeName) ? 2 : 0;
        }

        private static bool IsBaitThrowContextName(string displayName, string typeName)
        {
            return displayName.EndsWith("." + typeName, StringComparison.Ordinal)
                || string.Equals(displayName, typeName, StringComparison.Ordinal);
        }

        // AbilityCaster.actionClip is the LIVE clip (see RepairThrowAnimationTrimFeature for why not
        // ActionContext.GetExecuteAction()).
        private unsafe bool TryStepBaitThrowClip(IntPtr graphObj, bool lure, out string status)
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
                    // Both clips only act once OnBehaveStart has run. TryReadAuraMonoUIntField answers
                    // 0 on a failed read, and 0 is never Action, so a renamed field just idles.
                    if (this.TryReadAuraMonoUIntField(clipObj, "_tPhase") != BaitThrowBehavePhaseAction)
                    {
                        status = "Throw clip starting.";
                        return false;
                    }

                    return lure
                        ? this.TryStepLureBallThrowClip(graphObj, clipObj, out status)
                        : this.TryEndBaitThrowCasting(graphObj, "Bait", out status);
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

        private unsafe bool TryStepLureBallThrowClip(IntPtr graphObj, IntPtr clipObj, out string status)
        {
            IntPtr clipClass = auraMonoObjectGetClass(clipObj);
            // Resolve before reading: a failed read answers 0, which is State.Start — the one value
            // this acts on (same trap as the repair trim).
            if (clipClass == IntPtr.Zero || this.FindAuraMonoFieldOnHierarchy(clipClass, "_state") == IntPtr.Zero)
            {
                status = "_state unavailable.";
                return false;
            }

            uint state = this.TryReadAuraMonoUIntField(clipObj, "_state");
            if (state == BaitThrowLureStateStart)
            {
                IntPtr throwBall = this.FindAuraMonoMethodOnHierarchy(clipClass, "ThrowBall", 0);
                if (throwBall == IntPtr.Zero)
                {
                    status = "ThrowBall() unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(throwBall, clipObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "ThrowBall() threw.";
                    return false;
                }
                state = this.TryReadAuraMonoUIntField(clipObj, "_state");
            }

            if (state == BaitThrowLureStateShoot)
            {
                if (!this.baitThrowTrimShootCollapsed)
                {
                    // Once per throw: StartShoot RESETS the JumpMotion, so re-issuing it every tick
                    // would keep restarting the (0.01s) hop instead of letting it finish.
                    this.baitThrowTrimShootCollapsed = true;
                    this.TryCollapseLureBallFlight(clipObj);
                }
                status = "Attractor throw short-circuited; waiting for the send.";
                return false;
            }

            if (state == BaitThrowLureStateSendCommand)
            {
                return this.TryEndBaitThrowCasting(graphObj, "Attractor", out status);
            }

            status = "Attractor throw clip in state " + state + ".";
            return false;
        }

        // FishingLureBallComponent.StartShoot(endPosition, costTime). endPosition is cosmetic — the
        // command sends the clip's own `target` — but it is the target anyway, so the ball does not
        // snap anywhere odd for the one frame it is still visible.
        private unsafe void TryCollapseLureBallFlight(IntPtr clipObj)
        {
            if (!this.TryReadAuraMonoObjectField(clipObj, out IntPtr ballObj, "_lureBallComponent") || ballObj == IntPtr.Zero)
            {
                return;
            }

            uint ballPin = AuraMonoPinNew(ballObj);
            if (ballPin == 0U)
            {
                return;
            }

            try
            {
                IntPtr ballClass = auraMonoObjectGetClass(ballObj);
                IntPtr startShoot = ballClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(ballClass, "StartShoot", 2)
                    : IntPtr.Zero;
                if (startShoot == IntPtr.Zero)
                {
                    return;
                }

                Vector3 endPosition = this.TryReadAuraMonoVector3Field(clipObj, "target");
                if (endPosition == Vector3.zero)
                {
                    this.TryGetLocalPlayerPosition(out endPosition);
                }
                float costTime = BaitThrowTrimInstantFlightSeconds;

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&endPosition);
                args[1] = (IntPtr)(&costTime);
                auraMonoRuntimeInvoke(startShoot, ballObj, (IntPtr)args, ref exc);
            }
            finally
            {
                AuraMonoPinFree(ballPin);
            }
        }

        // paramCount 0 = the inherited ActorActionGraph.EndCasting(), not the 1-arg subclass overload.
        private bool TryEndBaitThrowCasting(IntPtr graphObj, string what, out string status)
        {
            IntPtr graphClass = auraMonoObjectGetClass(graphObj);
            IntPtr endCasting = graphClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(graphClass, "EndCasting", 0)
                : IntPtr.Zero;
            if (endCasting == IntPtr.Zero)
            {
                status = "EndCasting() unavailable.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(endCasting, graphObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                // AbilityCaster.EndCasting throws while mid Beging/Ending — transient, retry next tick.
                status = "EndCasting() threw.";
                return false;
            }

            status = what + " throw animation trimmed.";
            return true;
        }

        private void BaitThrowTrimSetStatus(string status)
        {
            if (!string.Equals(status, this.baitThrowTrimLastLoggedStatus, StringComparison.Ordinal))
            {
                this.baitThrowTrimLastLoggedStatus = status;
                FeatureLog.Life("BaitThrowTrim", status);
            }
        }
    }
}
