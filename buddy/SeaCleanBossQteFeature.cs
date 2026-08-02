using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // OCEAN CLEANUP — public-pollutant ("boss") QTE assist.
    //
    // Automates the TIMING of a button the player would otherwise press, and nothing else. Every
    // call goes through `PlayerStateSeaClean.OnMainInteraction(bool)` — the exact method
    // `LocalPlayerComponent` invokes from the main-interaction key (LocalPlayerComponent.cs:980) —
    // while the player is genuinely in `PlayerState.SeaClean` with the boss as their current
    // target. The mod never puts the player into that state and never touches the QTE component.
    //
    // WHY THIS SHAPE, and not the obvious one. The tempting design is to drive the component
    // directly: BeginHold / TickQTE / EndHold, or even skip to
    // `ReqStartCleanPublicPollutant(netId, qtePassed:true)`. Both work mechanically — the whole QTE
    // is client-side and the server is told only `bool qtePassed`. Both were rejected:
    //
    //  * Completing a QTE with a pass while NOT cleaning is not reachable in the real game. The
    //    trigger (CleanupEventModule.PublicPollutantQteJudge) fires on a timer whether or not you
    //    are cleaning, but the only code that can COMPLETE one is the player FSM
    //    (PlayerStateSeaClean is the sole caller of TickQTE/BeginHold/EndHold). A pass arriving
    //    from an idle player is a sequence the game cannot produce — on a PUBLIC boss, whose
    //    result other players see.
    //  * `ReqStartCleanPublicPollutant(..., true)` on its own also leaves the local component stuck
    //    in ExecutionQTE forever: `StartQTE` early-returns unless `_qteState == Idle`, and the only
    //    writer of Idle is `ResetQTE`, reachable solely through the FSM paths. Worse, entities are
    //    pooled with their components and `OnSpawned` never resets `_qteState`, so a stranded QTE
    //    can outlive a despawn.
    //
    // Going through the FSM makes every client→server send the game's own, in the game's own order.
    //
    // ⚠️ Real time IS charged here. `_qteTimeRemaining` is set once in StartQTE and NOT reset
    // between rounds (EndHold only swaps `_holdDuration`), so up to 3 rounds share one `timeLimit`
    // — 3 × 1.5 s of holds against a 5 s budget leaves ~0.5 s of total slack. Because the player is
    // in the state, the game's own tick drains that clock in real time, so a late release really
    // does lose budget. Hence this runs EVERY FRAME rather than on a throttle.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // PlayerState.SeaClean — XDTLevelAndEntity...Player.PlayerState.
        private const int SeaCleanBossPlayerStateSeaClean = 59;

        // SeaCleanExecutionState.
        private const int SeaCleanBossQteStateReady = 1;
        private const int SeaCleanBossQteStateAwaitingRelease = 3;

        private bool seaCleanBossQteEnabled;

        private IntPtr seaCleanBossLocalPlayerClass = IntPtr.Zero;
        private IntPtr seaCleanBossGetCurrentStateMethod = IntPtr.Zero;
        private IntPtr seaCleanBossStateClass = IntPtr.Zero;
        private IntPtr seaCleanBossGetPlayerStateMethod = IntPtr.Zero;
        private IntPtr seaCleanBossGetCurrentTargetMethod = IntPtr.Zero;
        private IntPtr seaCleanBossOnMainInteractionMethod = IntPtr.Zero;
        private IntPtr seaCleanBossGetQteStateMethod = IntPtr.Zero;

        private bool seaCleanBossResolveTried;
        private float seaCleanBossNextResolveAt;
        private int seaCleanBossLastDrivenState = -1;
        private int seaCleanBossRoundsThisQte;
        private int seaCleanBossPassCount;

        internal string SeaCleanBossQteStatus { get; private set; } = string.Empty;

        // Called every frame from OnUpdate. Costs two invokes while idle in the sea-clean state and
        // nothing at all otherwise.
        private void TickSeaCleanBossQte()
        {
            if (!this.seaCleanBossQteEnabled)
            {
                return;
            }

            try
            {
                if (!this.EnsureSeaCleanBossQteResolved())
                {
                    return;
                }

                List<uint> pins = new List<uint>();
                try
                {
                    IntPtr state = this.TryGetSeaCleanBossPlayerState(pins);
                    if (state == IntPtr.Zero)
                    {
                        // Not cleaning: the game is not ticking any QTE, and driving one from here
                        // is exactly the non-vanilla sequence this feature exists to avoid.
                        this.SeaCleanBossResetRun("Equip the sea cleaner and aim at the pollutant.");
                        return;
                    }

                    IntPtr monster = this.TryGetSeaCleanBossTarget(state, pins);
                    if (monster == IntPtr.Zero)
                    {
                        this.SeaCleanBossResetRun("No target — aim at the public pollutant.");
                        return;
                    }

                    // Re-checked EVERY frame, not just at QTE start: a server HP sync can kill the
                    // boss mid-QTE (someone else finished it), which flips IsInQTE off but leaves
                    // the component's own _qteState stranded. Driving on _qteState alone would keep
                    // pressing — and EndHold would still "complete" and claim a pass on a corpse.
                    if (!this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsPublicMethod, out bool isPublic) || !isPublic)
                    {
                        this.SeaCleanBossResetRun("Target is not the public pollutant.");
                        return;
                    }

                    if (this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsCleanedMethod, out bool cleaned) && cleaned)
                    {
                        this.SeaCleanBossResetRun("Pollutant already cleaned.");
                        return;
                    }

                    if (!this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsInQteMethod, out bool inQte) || !inQte)
                    {
                        this.SeaCleanBossResetRun("Cleaning — waiting for the next QTE.");
                        return;
                    }

                    if (this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsQteShieldMethod, out bool shield) && shield)
                    {
                        this.SeaCleanBossResetRun("Shield QTE — left to the player.");
                        return;
                    }

                    if (!this.SeaCleanBossInvokeIntGetter(monster, this.seaCleanBossGetQteStateMethod, out int qteState))
                    {
                        this.SeaCleanBossQteStatus = "QTE state unreadable.";
                        return;
                    }

                    // The whole automation. Ready → press, AwaitingRelease → release; Holding is
                    // the game's to advance. Releasing during Holding would be an early release,
                    // which EndHold treats as a failed round.
                    if (qteState == SeaCleanBossQteStateReady && this.seaCleanBossLastDrivenState != qteState)
                    {
                        if (this.SeaCleanBossInvokeMainInteraction(state, true))
                        {
                            this.seaCleanBossLastDrivenState = qteState;
                            this.SeaCleanBossQteStatus = "Holding round " + (this.seaCleanBossRoundsThisQte + 1) + "…";
                        }
                    }
                    else if (qteState == SeaCleanBossQteStateAwaitingRelease && this.seaCleanBossLastDrivenState != qteState)
                    {
                        if (this.SeaCleanBossInvokeMainInteraction(state, false))
                        {
                            this.seaCleanBossLastDrivenState = qteState;
                            this.seaCleanBossRoundsThisQte++;
                            this.seaCleanBossPassCount++;
                            this.SeaCleanBossQteStatus = "Released round " + this.seaCleanBossRoundsThisQte + ".";
                            this.SeaCleanBossLog("released round " + this.seaCleanBossRoundsThisQte);
                        }
                    }
                    else if (qteState != SeaCleanBossQteStateReady && qteState != SeaCleanBossQteStateAwaitingRelease)
                    {
                        // Holding (or a terminal state) — clear the edge latch so the next Ready
                        // of a multi-round QTE is driven again.
                        this.seaCleanBossLastDrivenState = -1;
                    }
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }
            }
            catch (Exception ex)
            {
                // Fail OPEN: a broken assist must never take the player's own input with it.
                this.seaCleanBossQteEnabled = false;
                this.SeaCleanBossQteStatus = "Disabled after an error: " + ex.GetType().Name;
                this.SeaCleanBossLog("disabled after " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void SeaCleanBossResetRun(string status)
        {
            this.seaCleanBossLastDrivenState = -1;
            this.seaCleanBossRoundsThisQte = 0;
            this.SeaCleanBossQteStatus = status;
        }

        // The FSM's CURRENT state, which is a non-generic accessor — `GetState<T>()` is generic and
        // inflated-generic invokes are a known crash class here. Returns Zero unless the player is
        // actually in PlayerState.SeaClean, which doubles as the "the game is driving" gate.
        private IntPtr TryGetSeaCleanBossPlayerState(List<uint> pins)
        {
            if (!this.TryAuraMonoGetComponentObjects(this.seaCleanBossLocalPlayerClass, out List<IntPtr> players, pins)
                || players == null || players.Count == 0)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < players.Count; i++)
            {
                IntPtr player = players[i];
                if (player == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr state = this.SeaCleanBossInvokeObjectGetter(player, this.seaCleanBossGetCurrentStateMethod);
                if (state == IntPtr.Zero)
                {
                    continue;
                }

                uint pin = AuraMonoPinNew(state);
                if (pin != 0U)
                {
                    pins.Add(pin);
                }

                if (this.SeaCleanBossInvokeIntGetter(state, this.seaCleanBossGetPlayerStateMethod, out int playerState)
                    && playerState == SeaCleanBossPlayerStateSeaClean)
                {
                    return state;
                }
            }

            return IntPtr.Zero;
        }

        // CurrentTarget is a STRUCT (SeaCleanTarget); the invoke hands back a boxed copy whose
        // `monsterComponent` field is a plain reference. Fail-closed: if the target cannot be read
        // we press nothing, because OnMainInteraction acts on the FSM's target — pressing blind
        // could start cleaning something else entirely.
        private IntPtr TryGetSeaCleanBossTarget(IntPtr state, List<uint> pins)
        {
            IntPtr boxed = this.SeaCleanBossInvokeObjectGetter(state, this.seaCleanBossGetCurrentTargetMethod);
            if (boxed == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint boxedPin = AuraMonoPinNew(boxed);
            if (boxedPin != 0U)
            {
                pins.Add(boxedPin);
            }

            if (!this.TryGetMonoObjectMember(boxed, "monsterComponent", out IntPtr monster) || monster == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint pin = AuraMonoPinNew(monster);
            if (pin != 0U)
            {
                pins.Add(pin);
            }

            return monster;
        }

        private unsafe bool SeaCleanBossInvokeMainInteraction(IntPtr state, bool down)
        {
            if (state == IntPtr.Zero || this.seaCleanBossOnMainInteractionMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            byte arg = down ? (byte)1 : (byte)0;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(this.seaCleanBossOnMainInteractionMethod, state, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe IntPtr SeaCleanBossInvokeObjectGetter(IntPtr obj, IntPtr getter)
        {
            if (obj == IntPtr.Zero || getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(getter, obj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero ? result : IntPtr.Zero;
        }

        private unsafe bool SeaCleanBossInvokeIntGetter(IntPtr obj, IntPtr getter, out int value)
        {
            value = 0;
            IntPtr boxed = this.SeaCleanBossInvokeObjectGetter(obj, getter);
            return boxed != IntPtr.Zero && this.TryUnboxMonoInt32(boxed, out value);
        }

        private bool EnsureSeaCleanBossQteResolved()
        {
            if (this.seaCleanBossLocalPlayerClass != IntPtr.Zero
                && this.seaCleanBossGetCurrentStateMethod != IntPtr.Zero
                && this.seaCleanBossGetPlayerStateMethod != IntPtr.Zero
                && this.seaCleanBossGetCurrentTargetMethod != IntPtr.Zero
                && this.seaCleanBossOnMainInteractionMethod != IntPtr.Zero
                && this.seaCleanBossGetQteStateMethod != IntPtr.Zero)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (this.seaCleanBossResolveTried && now < this.seaCleanBossNextResolveAt)
            {
                return false;
            }

            this.seaCleanBossResolveTried = true;
            this.seaCleanBossNextResolveAt = now + 3f;

            // The pollutant class + its getters are already resolved by the auto-clean feature;
            // only QTEState is added here.
            if (!this.EnsureSeaCleanQteAuraResolved(out string _))
            {
                return false;
            }

            if (this.seaCleanQteMonsterClass != IntPtr.Zero && this.seaCleanBossGetQteStateMethod == IntPtr.Zero)
            {
                this.seaCleanBossGetQteStateMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_QTEState", 0);
            }

            if (this.seaCleanBossLocalPlayerClass == IntPtr.Zero)
            {
                this.seaCleanBossLocalPlayerClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent");
                if (this.seaCleanBossLocalPlayerClass == IntPtr.Zero)
                {
                    this.seaCleanBossLocalPlayerClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerComponent");
                }
            }

            if (this.seaCleanBossLocalPlayerClass != IntPtr.Zero && this.seaCleanBossGetCurrentStateMethod == IntPtr.Zero)
            {
                this.seaCleanBossGetCurrentStateMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanBossLocalPlayerClass, "GetCurrentState", 0);
            }

            if (this.seaCleanBossStateClass == IntPtr.Zero)
            {
                this.seaCleanBossStateClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Player.PlayerStateSeaClean");
                if (this.seaCleanBossStateClass == IntPtr.Zero)
                {
                    this.seaCleanBossStateClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTLevelAndEntity.Gameplay.Component.Player", "PlayerStateSeaClean");
                }
            }

            if (this.seaCleanBossStateClass != IntPtr.Zero)
            {
                // get_State lives on PlayerStateBase — hierarchy lookup, not the leaf class.
                if (this.seaCleanBossGetPlayerStateMethod == IntPtr.Zero)
                {
                    this.seaCleanBossGetPlayerStateMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanBossStateClass, "get_State", 0);
                }
                if (this.seaCleanBossGetCurrentTargetMethod == IntPtr.Zero)
                {
                    this.seaCleanBossGetCurrentTargetMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanBossStateClass, "get_CurrentTarget", 0);
                }
                if (this.seaCleanBossOnMainInteractionMethod == IntPtr.Zero)
                {
                    this.seaCleanBossOnMainInteractionMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanBossStateClass, "OnMainInteraction", 1);
                }
            }

            bool ready = this.seaCleanBossLocalPlayerClass != IntPtr.Zero
                && this.seaCleanBossGetCurrentStateMethod != IntPtr.Zero
                && this.seaCleanBossGetPlayerStateMethod != IntPtr.Zero
                && this.seaCleanBossGetCurrentTargetMethod != IntPtr.Zero
                && this.seaCleanBossOnMainInteractionMethod != IntPtr.Zero
                && this.seaCleanBossGetQteStateMethod != IntPtr.Zero;

            this.SeaCleanBossLog("resolve: localPlayer=0x" + this.seaCleanBossLocalPlayerClass.ToInt64().ToString("X")
                + " getCurrentState=0x" + this.seaCleanBossGetCurrentStateMethod.ToInt64().ToString("X")
                + " getState=0x" + this.seaCleanBossGetPlayerStateMethod.ToInt64().ToString("X")
                + " currentTarget=0x" + this.seaCleanBossGetCurrentTargetMethod.ToInt64().ToString("X")
                + " onMainInteraction=0x" + this.seaCleanBossOnMainInteractionMethod.ToInt64().ToString("X")
                + " qteState=0x" + this.seaCleanBossGetQteStateMethod.ToInt64().ToString("X"));

            return ready;
        }

        private void SeaCleanBossLog(string message)
        {
            ModLogger.Msg("[SeaCleanBossQte] " + message);
        }
    }
}
