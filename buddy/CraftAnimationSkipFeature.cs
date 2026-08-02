using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Skip Craft / Dye animation — cuts the character animation that plays after a craft or a dye
    // is confirmed by the server.
    //
    // Crafting and dyeing share ONE path in the game. CraftCompositeDetailPanel (craft),
    // DyeColorPanel (dye) and DrawSewingPanel (artwork overlay) all dispatch a MakeItemEvent, which
    // lands in MakeItemCommand
    // (ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Gameplay.Interaction/MakeItemCommand.cs):
    // it hides the scene UI, sends the craft/dye command, and waits on
    // RPCRespTask<MakeItemResultEvent>. On the server response it casts PlayerCraftsmanArg, and
    // PlayerActionGraph.GetBeforeActions expands that single cast into a THREE-clip sequence,
    // because PlayerCraftsmanArg implements IMoveStrideWhenStart:
    //
    //     Generic_MoveStride  ->  Generic_FaceDirectionAction (0.2 s)  ->  PlayerCraftsmanAction
    //     (walk to the bench)     (turn to face it)                       (the craft animation)
    //
    // PlayerCraftsmanAction sets animator trigger "Craftsman", or "Dye" when staticId == 0, in the
    // interactivecraftsman controller, with a 10 s timeOut. This feature cuts that third clip; the
    // walk is left alone (see the note at the bottom).
    //
    // WHAT MUST SURVIVE THE CUT: PlayerCraftsmanAction.OnBehaveFinish is where all the real work
    // lives — CharacterProtocolManager.CancelOccupyCommand() (releases the bench),
    // SceneUIVisibilityRequestedEvent{visible=true} (brings the HUD back) and the
    // RewardToastEvent / Flaunt. ActorBehaveClip only runs OnBehaveFinish once the clip has reached
    // its Action phase, so ending the cast during the stride would leave the player occupying the
    // bench with a hidden HUD. Hence the gate below.
    //
    // LEVER: ActorActionGraph.EndCasting() (ilspy-dumps/.../Gameplay/Playable/ActorActionGraph.cs).
    // This is the exact call the game itself makes when a cast finishes naturally —
    // AbilityCaster.TickGraph calls EndCasting() the moment the sequence track reports done — and
    // when one action interrupts another (ActorActionGraph.Cast). It runs _sequenceTrack.Finish()
    // -> ClipWrapper.SetState(Done) -> clip.Finish() -> OnBehaveFinish, i.e. the identical
    // termination the natural path takes. Deliberately NOT ActorActionGraph.Stop(), which
    // additionally tears down the motion graph: that shape stranded animator/buoy state and broke
    // fishing once (memory: fishing-skip-catch-animation v1).
    //
    // GATE: cut only once animationComponent.IsAnimState(Craftsman | Dye) is true — the very
    // condition PlayerCraftsmanAction.OnBehaveTick waits on. Those triggers are set in
    // OnBehaveStart and nothing else in the game sets them, so seeing the state proves the clip is
    // in its Action phase. A second belt: ActorActionGraph.actionContext must be the
    // PlayerCraftsmanArg (AbilityCaster._context is set once per cast), so the one destructive call
    // this feature makes is provably scoped to the craft/dye cast.
    //
    // TRIGGER: MakeItemResultEvent (the same event MakeItemCommand awaits) opens a short poll
    // window — events-first per AGENTS.md §7. Until that detour is live a slower always-on poll
    // keeps the feature working (the IsGameEventHookInstalled fallback pattern).
    //
    // Local player only: the animator/action graph read here is the self player, so other players'
    // craft animations are untouched. All game access is public API over AuraMono — no native
    // detour, no IL2CPP .text patch.
    //
    // NOT handled here: the walk to the bench. The trip cannot be cancelled anyway
    // (Generic_MoveStride.OnBehaveFinish does WorldPlaceTo(_destination) unconditionally, so the
    // player lands at the bench however the clip ends), and for plain crafting the Direct Craft
    // Send feature removes the whole sequence — walk included — by refusing the command outright.
    // Dye still walks; that is vanilla.
    public partial class HeartopiaComplete
    {
        private const string CraftAnimSkipResultEventName = "ScriptsRefactory.DataAndProtocol.Events.MakeItemResultEvent";

        // bool showOff@0 (padded), uint netId@4, int staticId@8, int count@12 — natural layout.
        // Snapshotted for the diagnostic log ONLY; the skip itself never reads the payload, so a
        // layout surprise after a game patch cannot change behaviour.
        private const int CraftAnimSkipResultEventPayloadBytes = 16;

        // Long enough to cover the walk plus the clip, shorter than the action's own 10 s timeOut.
        private const float CraftAnimSkipWindowSeconds = 6f;
        private const float CraftAnimSkipWindowPollInterval = 0.05f;
        private const float CraftAnimSkipFallbackPollInterval = 0.3f;
        // After a cut the animator needs a frame or two to leave the state; don't re-fire into it.
        private const float CraftAnimSkipReArmSeconds = 0.25f;

        // Short name of the action context that identifies the craft/dye cast.
        private const string CraftAnimSkipContextTypeName = "PlayerCraftsmanArg";
        private const string CraftAnimSkipIdleStatus = "No craft/dye cast in flight.";

        internal static bool MasterLogCraftAnimSkip = false;

        private bool skipCraftDyeAnimations;
        private bool craftAnimSkipHookRegistered;
        private bool craftAnimSkipHookInstallLogged;
        private float craftAnimSkipWindowEndsAt;
        private float craftAnimSkipNextPollAt;
        private float craftAnimSkipReArmAt;
        private int craftAnimSkipCraftsmanHash;
        private int craftAnimSkipDyeHash;
        private int craftAnimSkipCount;
        private string craftAnimSkipStatus = "Idle.";
        private string craftAnimSkipLastLoggedStatus;
        private FeatureBreakerState craftAnimSkipBreaker;

        private void ProcessCraftAnimationSkipOnUpdate()
        {
            if (!this.skipCraftDyeAnimations)
            {
                // Drop any window left over from before the toggle went off, so re-enabling later
                // cannot fire into a craft that already finished.
                this.craftAnimSkipWindowEndsAt = 0f;
                return;
            }

            float now = Time.unscaledTime;
            this.EnsureCraftAnimationSkipEventHook();

            bool hooked = this.IsGameEventHookInstalled(CraftAnimSkipResultEventName);
            if (MasterLogCraftAnimSkip && hooked && !this.craftAnimSkipHookInstallLogged)
            {
                this.craftAnimSkipHookInstallLogged = true;
                ModLogger.Msg("[CraftAnimSkip] hook installed: " + CraftAnimSkipResultEventName);
            }

            // Event-primary: the MakeItemResultEvent handler opens the fast window. The slow poll
            // covers the frames before the detour is live (and any dispatch it could not splice).
            if (hooked && now >= this.craftAnimSkipWindowEndsAt)
            {
                return;
            }

            if (now < this.craftAnimSkipNextPollAt || now < this.craftAnimSkipReArmAt)
            {
                return;
            }

            this.craftAnimSkipNextPollAt = now + (hooked ? CraftAnimSkipWindowPollInterval : CraftAnimSkipFallbackPollInterval);

            if (!this.craftAnimSkipBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.TryEndCraftDyeAnimationClip(out string status))
                {
                    this.craftAnimSkipCount++;
                    this.craftAnimSkipWindowEndsAt = 0f;
                    this.craftAnimSkipReArmAt = now + CraftAnimSkipReArmSeconds;
                    this.CraftAnimSkipSetStatus("Skipped " + this.craftAnimSkipCount + " craft/dye animation(s).");
                }
                else if (!string.Equals(status, CraftAnimSkipIdleStatus, StringComparison.Ordinal))
                {
                    // "Nothing in flight" is the normal poll result — only surface the rest.
                    this.CraftAnimSkipSetStatus(status);
                }

                this.craftAnimSkipBreaker.Success();
            }
            catch (Exception ex)
            {
                this.craftAnimSkipBreaker.Failure("CraftAnimSkip", ex, now);
                this.CraftAnimSkipSetStatus("Error: " + ex.Message);
            }
        }

        private void EnsureCraftAnimationSkipEventHook()
        {
            if (this.craftAnimSkipHookRegistered)
            {
                return;
            }

            // Registered on first enable rather than at startup: no point splicing a detour onto
            // DispatchEvent<MakeItemResultEvent> for users who never turn the feature on.
            this.craftAnimSkipHookRegistered = true;
            bool ok = this.RegisterGameEventHook(CraftAnimSkipResultEventName,
                CraftAnimSkipResultEventPayloadBytes,
                this.OnCraftAnimSkipMakeItemResultEvent);
            if (MasterLogCraftAnimSkip)
            {
                ModLogger.Msg("[CraftAnimSkip] register " + CraftAnimSkipResultEventName + " = " + ok);
            }
        }

        // Runs on the main-thread drain, so touching fields / logging here is fine.
        private void OnCraftAnimSkipMakeItemResultEvent(GameEventSnapshot e)
        {
            // The server confirmed the craft/dye — MakeItemCommand is about to cast
            // PlayerCraftsmanArg. Open the fast window and let the next tick poll for the cast.
            this.craftAnimSkipWindowEndsAt = Time.unscaledTime + CraftAnimSkipWindowSeconds;
            this.craftAnimSkipNextPollAt = 0f;

            if (MasterLogCraftAnimSkip)
            {
                ModLogger.Msg("[CraftAnimSkip] MakeItemResultEvent netId=" + e.ReadUInt32(4)
                    + " staticId=" + e.ReadInt32(8)
                    + " count=" + e.ReadInt32(12)
                    + " len=" + e.Length);
            }
        }

        // One poll step over the live craft/dye cast; returns true when the cast was ended.
        private unsafe bool TryEndCraftDyeAnimationClip(out string status)
        {
            status = "AuraMono unavailable.";

            if (this.craftAnimSkipCraftsmanHash == 0)
            {
                // Same hashes as the game's AnimStateHash.Craftsman / AnimStateHash.Dye.
                this.craftAnimSkipCraftsmanHash = Animator.StringToHash("Craftsman");
                this.craftAnimSkipDyeHash = Animator.StringToHash("Dye");
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            // Fail closed: without pinning, the objects we hold across the next allocating invoke
            // are moving-GC stale pointers (memory: auramono-pinning-fail-closed).
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
                if (playerClass == IntPtr.Zero)
                {
                    status = "Player class unavailable.";
                    return false;
                }

                IntPtr getActionGraph = this.FindAuraMonoMethodOnHierarchy(playerClass, "get_actionGraph", 0);
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
                    // AbilityCaster._context is set once per cast, so this identifies the whole
                    // three-clip craft sequence — stride included.
                    if (!this.IsCraftDyeCastInFlight(graphObj))
                    {
                        status = CraftAnimSkipIdleStatus;
                        return false;
                    }

                    if (!this.TryReadCraftDyeAnimStateActive(playerObj, playerClass, out bool inCraftState, out string animStatus))
                    {
                        status = animStatus;
                        return false;
                    }

                    if (!inCraftState)
                    {
                        // Still in Generic_MoveStride / Generic_FaceDirectionAction. Cutting here
                        // would skip PlayerCraftsmanAction.OnBehaveFinish entirely (its clip never
                        // reaches Phase.Action), so wait for the animator to enter the clip.
                        status = "Craft cast walking to the bench.";
                        return false;
                    }

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
                        // transient, so just report and let the next poll retry.
                        status = "EndCasting() threw.";
                        return false;
                    }

                    status = "Craft/dye animation ended.";
                    return true;
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

        // actionGraph.actionContext is the craft/dye arg — i.e. a craft/dye cast is running.
        private bool IsCraftDyeCastInFlight(IntPtr graphObj)
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
                && (displayName.EndsWith("." + CraftAnimSkipContextTypeName, StringComparison.Ordinal)
                    || string.Equals(displayName, CraftAnimSkipContextTypeName, StringComparison.Ordinal));
        }

        private unsafe bool TryReadCraftDyeAnimStateActive(IntPtr playerObj, IntPtr playerClass, out bool inCraftState, out string status)
        {
            inCraftState = false;
            status = string.Empty;

            IntPtr getAnimComponent = this.FindAuraMonoMethodOnHierarchy(playerClass, "get_animationComponent", 0);
            if (getAnimComponent == IntPtr.Zero)
            {
                status = "get_animationComponent unavailable.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr animObj = auraMonoRuntimeInvoke(getAnimComponent, playerObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || animObj == IntPtr.Zero)
            {
                status = "animationComponent unavailable.";
                return false;
            }

            uint animPin = AuraMonoPinNew(animObj);
            if (animPin == 0U)
            {
                status = "AnimationComponent pin failed.";
                return false;
            }

            try
            {
                IntPtr animClass = auraMonoObjectGetClass(animObj);
                IntPtr isAnimState = animClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(animClass, "IsAnimState", 1)
                    : IntPtr.Zero;
                if (isAnimState == IntPtr.Zero)
                {
                    status = "IsAnimState(1) unavailable.";
                    return false;
                }

                if (!this.TryReadCraftAnimStateFlag(isAnimState, animObj, this.craftAnimSkipCraftsmanHash, out bool inCraftsman))
                {
                    status = "IsAnimState(Craftsman) invoke failed.";
                    return false;
                }

                bool inDye = false;
                if (!inCraftsman
                    && !this.TryReadCraftAnimStateFlag(isAnimState, animObj, this.craftAnimSkipDyeHash, out inDye))
                {
                    status = "IsAnimState(Dye) invoke failed.";
                    return false;
                }

                inCraftState = inCraftsman || inDye;
                return true;
            }
            finally
            {
                AuraMonoPinFree(animPin);
            }
        }

        private unsafe bool TryReadCraftAnimStateFlag(IntPtr isAnimStateMethod, IntPtr animObj, int stateHash, out bool inState)
        {
            inState = false;

            int hash = stateHash;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&hash);
            IntPtr boxed = auraMonoRuntimeInvoke(isAnimStateMethod, animObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            IntPtr unboxed = auraMonoObjectUnbox(boxed);
            if (unboxed == IntPtr.Zero)
            {
                return false;
            }

            inState = *(byte*)unboxed != 0;
            return true;
        }

        private void CraftAnimSkipSetStatus(string status)
        {
            this.craftAnimSkipStatus = status;
            if (!MasterLogCraftAnimSkip || string.Equals(this.craftAnimSkipLastLoggedStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            this.craftAnimSkipLastLoggedStatus = status;
            ModLogger.Msg("[CraftAnimSkip] " + status);
        }
    }
}
