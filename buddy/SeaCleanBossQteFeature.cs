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
        // SeaCleanExecutionState.
        private const int SeaCleanBossQteStateReady = 1;
        private const int SeaCleanBossQteStateAwaitingRelease = 3;

        private bool seaCleanBossQteEnabled;

        private IntPtr seaCleanBossLocalPlayerClass = IntPtr.Zero;
        private IntPtr seaCleanBossGetCurrentStateMethod = IntPtr.Zero;
        private IntPtr seaCleanBossStateClass = IntPtr.Zero;
        private IntPtr seaCleanBossGetCurrentTargetMethod = IntPtr.Zero;
        private IntPtr seaCleanBossOnMainInteractionMethod = IntPtr.Zero;
        private IntPtr seaCleanBossGetQteStateMethod = IntPtr.Zero;

        private bool seaCleanBossResolveTried;
        private float seaCleanBossNextResolveAt;
        private int seaCleanBossLastDrivenState = -1;
        private int seaCleanBossRoundsThisQte;
        private int seaCleanBossPassCount;
        private IntPtr seaCleanBossGetMonsterTypeMethod = IntPtr.Zero;
        private string seaCleanBossLastDiagnosis = string.Empty;
        private float seaCleanBossNextDiagAt;
        private float seaCleanBossNextNoTargetLogAt;
        private float seaCleanBossNextRejectLogAt;
        private float seaCleanBossNextIdleLogAt;
        private string seaCleanBossLastIdleLogged = string.Empty;

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
                // ⚠️ Fail-closed on pinning. Without gchandle exports every AuraMonoPinNew below is
                // a silent no-op, and SGen is free to move an object across ANY allocation — and a
                // boxing invoke IS an allocation. Reading a moved object hands back whatever now
                // occupies that address, which is exactly what "qteState=1434087536" was. Not
                // gating on this is a documented hard rule I broke.
                if (!AuraMonoPinningAvailable)
                {
                    this.SeaCleanBossResetRun("AuraMono pinning unavailable — assist disabled.");
                    return;
                }

                // Shares the auto-clean feature's hook (idempotent) purely for visibility: the
                // poll below can only ever see a QTE that is ALREADY running, so without the event
                // there is no way to tell "no QTE fired" from "we missed it".
                this.EnsureSeaCleanQteEventHooks();

                if (!this.EnsureSeaCleanBossQteResolved())
                {
                    return;
                }

                List<uint> pins = new List<uint>();
                try
                {
                    IntPtr state = this.TryGetSeaCleanBossPlayerState(pins, out bool lookupFailed);
                    if (lookupFailed)
                    {
                        // Could not look at all — hold the previous status and the run latch rather
                        // than reporting a state change that was never observed.
                        return;
                    }

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

                    // Identity check BEFORE any invoke. `CurrentTarget.monsterComponent` can hand
                    // back something that is not a live pollutant component, and calling a getter
                    // on the wrong class is how this project has crashed before — the junk values
                    // in the log were the harmless version of that.
                    if (!this.SeaCleanBossIsMonsterComponent(monster, out string actualClass))
                    {
                        this.SeaCleanBossLogThrottled("target 0x" + monster.ToInt64().ToString("X")
                            + " is not a SeaCleanMonsterComponent (class=" + actualClass + ") — ignored.");
                        this.SeaCleanBossResetRun("Target is not a pollutant component.");
                        return;
                    }

                    // Re-checked EVERY frame, not just at QTE start: a server HP sync can kill the
                    // boss mid-QTE (someone else finished it), which flips IsInQTE off but leaves
                    // the component's own _qteState stranded. Driving on _qteState alone would keep
                    // pressing — and EndHold would still "complete" and claim a pass on a corpse.
                    // Everything is read FIRST, then judged. Each getter reports ok/value
                    // separately: the earlier version collapsed "the invoke failed" into "false",
                    // so an unresolved method looked exactly like a wrong target and the status
                    // line blamed the player for the mod's own resolve gap.
                    bool okPublic = this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsPublicMethod, out bool isPublic);
                    bool okCleaned = this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsCleanedMethod, out bool cleaned);
                    bool okInQte = this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsInQteMethod, out bool inQte);
                    bool okShield = this.SeaCleanQteInvokeBoolGetter(monster, this.seaCleanQteGetIsQteShieldMethod, out bool shield);
                    bool okType = this.SeaCleanBossInvokeIntGetter(monster, this.seaCleanBossGetMonsterTypeMethod, out int monsterType);
                    bool okQte = this.SeaCleanBossInvokeIntGetter(monster, this.seaCleanBossGetQteStateMethod, out int qteState);

                    uint targetNetId = 0U;
                    if (this.TryGetMonoObjectMember(monster, "entity", out IntPtr entityObj) && entityObj != IntPtr.Zero)
                    {
                        uint entityPin = AuraMonoPinNew(entityObj);
                        if (entityPin != 0U)
                        {
                            pins.Add(entityPin);
                        }
                        this.TryGetMonoUInt32Member(entityObj, "netId", out targetNetId);
                    }

                    this.SeaCleanBossDiagnose(targetNetId, okType, monsterType, okPublic, isPublic,
                        okCleaned, cleaned, okInQte, inQte, okShield, shield, okQte, qteState);

                    if (!okPublic)
                    {
                        this.SeaCleanBossResetRun("Cannot read the target's type — see the log.");
                        return;
                    }

                    if (!isPublic)
                    {
                        this.SeaCleanBossResetRun("Aiming at a private pollutant ("
                            + (okType ? SeaCleanBossDescribeMonsterType(monsterType) : "type ?")
                            + ") — this assist only drives the public boss.");
                        return;
                    }

                    if (okCleaned && cleaned)
                    {
                        this.SeaCleanBossResetRun("Pollutant already cleaned.");
                        return;
                    }

                    if (!okInQte || !inQte)
                    {
                        this.SeaCleanBossResetRun("Cleaning — waiting for the next QTE.");
                        return;
                    }

                    if (okShield && shield)
                    {
                        this.SeaCleanBossResetRun("Shield QTE — left to the player.");
                        return;
                    }

                    if (!okQte)
                    {
                        this.SeaCleanBossQteStatus = "QTE state unreadable — see the log.";
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

        // Also logs the TRANSITION. Statuses are set every frame, so logging the text only when it
        // actually changes turns the log into a readable trace of why the assist is idle.
        private void SeaCleanBossResetRun(string status)
        {
            this.seaCleanBossLastDrivenState = -1;
            this.seaCleanBossRoundsThisQte = 0;
            if (!string.Equals(status, this.SeaCleanBossQteStatus, StringComparison.Ordinal))
            {
                // Rate-limited: "log every transition" is only quiet while transitions are rare.
                // Two statuses alternating per frame printed six pairs a second and drowned the
                // events worth reading. One line per 2 s, and never the same text twice in a row.
                float now = Time.unscaledTime;
                if (now >= this.seaCleanBossNextIdleLogAt
                    && !string.Equals(status, this.seaCleanBossLastIdleLogged, StringComparison.Ordinal))
                {
                    this.seaCleanBossNextIdleLogAt = now + 2f;
                    this.seaCleanBossLastIdleLogged = status;
                    this.SeaCleanBossLog("idle: " + status);
                }
            }

            this.SeaCleanBossQteStatus = status;
        }

        // The FSM's CURRENT state, which is a non-generic accessor — `GetState<T>()` is generic and
        // inflated-generic invokes are a known crash class here. Returns Zero unless the player is
        // actually in PlayerState.SeaClean, which doubles as the "the game is driving" gate.
        // `lookupFailed` separates "the component enumeration itself did not work this frame" from
        // "the player is not cleaning". They are NOT the same, and collapsing them is what made the
        // status flap between two lines many times a second: a transient enumeration miss read as
        // "left the sea-clean state". Same family of bug as treating a failed getter invoke as a
        // `false` value — a failure is not an answer.
        private IntPtr TryGetSeaCleanBossPlayerState(List<uint> pins, out bool lookupFailed)
        {
            lookupFailed = false;

            if (!this.TryAuraMonoGetComponentObjects(this.seaCleanBossLocalPlayerClass, out List<IntPtr> players, pins)
                || players == null || players.Count == 0)
            {
                lookupFailed = true;
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

                if (this.SeaCleanBossIsInstanceOf(state, this.seaCleanBossStateClass, out string _))
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
                // Empty is normal (nothing aimed at); a read FAILURE is not, and the two are
                // indistinguishable from the caller, so say which one happened here. Throttled:
                // this runs every frame the player stands in the state without aiming.
                float now = Time.unscaledTime;
                if (now >= this.seaCleanBossNextNoTargetLogAt)
                {
                    this.seaCleanBossNextNoTargetLogAt = now + 10f;
                    this.SeaCleanBossLog("CurrentTarget.monsterComponent is null — nothing aimed at, or the field moved.");
                }

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

            if (this.seaCleanQteMonsterClass != IntPtr.Zero && this.seaCleanBossGetMonsterTypeMethod == IntPtr.Zero)
            {
                // Diagnostics only — it names WHICH kind of pollutant is being aimed at,
                // which is the difference between "wrong target" and "broken resolve".
                this.seaCleanBossGetMonsterTypeMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_MonsterType", 0);
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
                // NOTE: get_State is deliberately NOT resolved. It is an override per state
                // class, so a non-virtual invoke of PlayerStateSeaClean's copy answers "SeaClean"
                // for every object — see SeaCleanBossIsInstanceOf.
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
                && this.seaCleanBossGetCurrentTargetMethod != IntPtr.Zero
                && this.seaCleanBossOnMainInteractionMethod != IntPtr.Zero
                && this.seaCleanBossGetQteStateMethod != IntPtr.Zero;

            this.SeaCleanBossLog("resolve: localPlayer=0x" + this.seaCleanBossLocalPlayerClass.ToInt64().ToString("X")
                + " getCurrentState=0x" + this.seaCleanBossGetCurrentStateMethod.ToInt64().ToString("X")
                + " currentTarget=0x" + this.seaCleanBossGetCurrentTargetMethod.ToInt64().ToString("X")
                + " onMainInteraction=0x" + this.seaCleanBossOnMainInteractionMethod.ToInt64().ToString("X")
                + " qteState=0x" + this.seaCleanBossGetQteStateMethod.ToInt64().ToString("X"));

            return ready;
        }

        // EPollutantBehaviorType — the enum behind MonsterType.
        private static string SeaCleanBossDescribeMonsterType(int value)
        {
            switch (value)
            {
                case 0: return "Normal";
                case 1: return "Split";
                case 2: return "Shield";
                case 3: return "MultiQte";
                case 4: return "Composite";
                case 5: return "PublicPollutant";
                default: return "type " + value;
            }
        }

        // SeaCleanExecutionState.
        private static string SeaCleanBossDescribeQteState(int value)
        {
            switch (value)
            {
                case 0: return "Idle";
                case 1: return "Ready";
                case 2: return "Holding";
                case 3: return "AwaitingRelease";
                case 4: return "Completed";
                case 5: return "Failed";
                default: return "state " + value;
            }
        }

        private static string SeaCleanBossFlag(string name, bool ok, bool value)
        {
            // "?" is NOT the same as "false" — an unreadable getter is a mod problem, a false one
            // is a game fact, and the whole point of this line is telling them apart.
            return name + "=" + (ok ? (value ? "1" : "0") : "?");
        }

        // Logged when the picture CHANGES, and otherwise at most once every 5 s — a per-frame dump
        // would bury the very transition it exists to show.
        private void SeaCleanBossDiagnose(uint netId, bool okType, int monsterType,
            bool okPublic, bool isPublic, bool okCleaned, bool cleaned,
            bool okInQte, bool inQte, bool okShield, bool shield, bool okQte, int qteState)
        {
            string line = "target netId=" + netId
                + " monsterType=" + (okType ? SeaCleanBossDescribeMonsterType(monsterType) : "?")
                + " " + SeaCleanBossFlag("public", okPublic, isPublic)
                + " " + SeaCleanBossFlag("cleaned", okCleaned, cleaned)
                + " " + SeaCleanBossFlag("inQte", okInQte, inQte)
                + " " + SeaCleanBossFlag("shieldQte", okShield, shield)
                + " qteState=" + (okQte ? SeaCleanBossDescribeQteState(qteState) : "?");

            float now = Time.unscaledTime;
            if (string.Equals(line, this.seaCleanBossLastDiagnosis, StringComparison.Ordinal)
                && now < this.seaCleanBossNextDiagAt)
            {
                return;
            }

            this.seaCleanBossLastDiagnosis = line;
            this.seaCleanBossNextDiagAt = now + 5f;
            this.SeaCleanBossLog(line);
        }

        private bool SeaCleanBossIsMonsterComponent(IntPtr obj, out string actualClass)
        {
            return this.SeaCleanBossIsInstanceOf(obj, this.seaCleanQteMonsterClass, out actualClass);
        }

        // True only when the object really is (or derives from) `expected`.
        //
        // ⚠️ This is the ONLY sound way to identify a game object here, and the reason is worth
        // keeping: `PlayerStateBase.State` is OVERRIDDEN by every state class, each returning its
        // own constant. Resolving `get_State` off PlayerStateSeaClean and invoking it through
        // mono_runtime_invoke calls THAT implementation — which returns SeaClean unconditionally,
        // whatever object it is handed. The check "is the player in the sea-clean state?" built on
        // it therefore passed always, and PlayerStateSeaClean's own property getters were then run
        // against unrelated states, reading fields at the wrong offsets (the log's
        // `class=PlayerTeaseCatMotionArg`). A non-virtual invoke can never answer "what is this" —
        // only the class can.
        private bool SeaCleanBossIsInstanceOf(IntPtr obj, IntPtr expected, out string actualClass)
        {
            actualClass = "?";
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || expected == IntPtr.Zero)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(obj);
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoClassGetName != null)
            {
                actualClass = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(auraMonoClassGetName(klass)) ?? "?";
            }

            // Walk the hierarchy: the live object may be a subclass.
            IntPtr cursor = klass;
            for (int depth = 0; depth < 8 && cursor != IntPtr.Zero; depth++)
            {
                if (cursor == expected)
                {
                    return true;
                }

                cursor = auraMonoClassGetParent != null ? auraMonoClassGetParent(cursor) : IntPtr.Zero;
            }

            return false;
        }

        private void SeaCleanBossLogThrottled(string message)
        {
            float now = Time.unscaledTime;
            if (now < this.seaCleanBossNextRejectLogAt)
            {
                return;
            }

            this.seaCleanBossNextRejectLogAt = now + 10f;
            this.SeaCleanBossLog(message);
        }

        private void SeaCleanBossLog(string message)
        {
            ModLogger.Msg("[SeaCleanBossQte] " + message);
        }
    }
}
