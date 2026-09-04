using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Foraging animations — play the gathering animation that matches the node, but ONLY when
    // another player is close enough to see it.
    //
    // Aura Farm collects by sending the server command directly, so a witness sees a character walk
    // up to a bush and the bush simply empties. This adds the missing motion. It is decoration on
    // purpose: the collection path is untouched, the animation never gates it, and nothing here
    // reaches the server.
    //
    // ── WHY THESE EXACT ACTIONS ─────────────────────────────────────────────────────────────────
    //   Tree  -> PlayerAxeAttackTree   (ActionId 200)
    //   Stone -> PlayerAxeAttackStone  (205)
    //   Bush  -> PlayerGatherContinuous (203)
    //
    // The two axe actions are the REAL chop/mine actions, so a remote client replays the correct
    // ActionId — a "miss" swing would look subtly wrong. They are still side-effect free here
    // because their send sits behind a target check:
    //
    //     LevelObject t = GetInteractTarget();          // GetLevelObject(levelObjectNetId)
    //     if (t != null && Entities.GetEntityRef(...).TryGet(out var e))
    //         OnHitAction(e, isCombo);                  // SendAttackTreeCommand lives in here
    //
    // levelObjectNetId is left 0, so the lookup returns null and nothing is sent — verified live:
    // both cast with ActionErrorCode 0 and a visible swing, with no resource touched.
    //
    // Gather is gated differently: its combo count is a CONTEXT FIELD (maxComboTime), so filling it
    // is what makes the motion play at all — at 0 the clip sets its trigger and cancels in the same
    // tick. The axe actions read their combo count off the target instead, which is why they get one
    // cast per swing rather than one cast for three.
    //
    // ── WHAT IT NEEDS ───────────────────────────────────────────────────────────────────────────
    // A tool in hand: the swing controller is (stand, <tool override>, lumbering|mining) and with
    // empty hands that combination has no asset, so the cast is accepted and nothing renders. The axe
    // is taken once when foraging starts (on the ground only) through FarmToolBroker, and given back
    // when it stops — one server round trip per run instead of one per node.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogForagingAnim = false;

        private const float ForagingAnimRadius = 20f;        // play only if a player is this close
        private const float ForagingAnimRadiusExit = 25f;    // hysteresis: stop only past this
        private const int ForagingAnimSwings = 3;            // strictly three, per the spec
        private const float ForagingAnimPlayerScanInterval = 1.0f;
        // ── PACING: one cast is one WHOLE swing, and there is no way to speed that up ────────────
        //
        // ActionClipWeaponComb (the axe action's base) only leaves its behave when the animator has
        // gone Start -> Back0X -> End -> Idle, so a swing owns the actor for ~1.5 s. Casting again
        // before that does NOT queue a second swing: the framework routes the repeat into OnReplay,
        // which advances the combo only when _canCombo is set — and that comes from
        //
        //     _canCombo = combTimes < _maxComboTime;      // OnHit
        //     _maxComboTime = maxComboTime;               // OnBehaveStart
        //
        // where PlayerAxeAttackAction.maxComboTime reads the TARGET's CollectableObjectComponent
        // .maxComboCount and returns 0 when there is no target. We deliberately pass
        // levelObjectNetId = 0 to keep the swing side-effect free, so _maxComboTime is 0, _canCombo
        // is never set, and every early re-cast is accepted (ActionErrorCode 0) and does nothing.
        // That is why both earlier attempts showed 1-2 swings with an empty log: the swings were not
        // failing, they were being swallowed.
        //
        // So the swings have to be paced by the CLIP, and three of them need ~4.5 s — more than the
        // ~2 s the aura dwell spends at a node. ShouldHoldFarmNodeForForagingAnim buys that time.
        private const float ForagingAnimSwingGap = 0.15f;    // after Idle returns, before the next
        private const float ForagingAnimSwingTimeout = 2.5f; // never wait forever on the animator

        // A cast refused because the previous clip still owns the actor is NOT the end of the
        // sequence — it is one beat too early. Retry instead of dropping the remaining swings.
        private const int ForagingAnimSwingRetries = 2;

        // Upper bound on how long the farm may linger at a node for the animation. Three clip-paced
        // swings land in ~4.5 s; past this the node is released with the swings still owed, so a
        // wedged animator can never stall the farm.
        private const float ForagingAnimHoldBudget = 6f;
        private const float ForagingAnimNodeMatchRadius = 4f;

        // ControllerShortName ordinals; the override comes from whatever is actually in hand.
        // ⚠️ ORDINALS, and they MOVE. ControllerShortName is a plain enum with a single explicit
        // value, so every name's number is its position — the 2026-08-20 update inserted entries
        // ahead of these and shifted them by four (lumbering was 159, mining 160). The cast still
        // returned ActionErrorCode 0 and simply rendered nothing, because the action asks the
        // animator for a controller family that no longer means what it did.
        // Re-check against ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.ResHandle.AnimationRes
        // /ControllerShortName.cs after every game update.
        private const int ForagingAnimShortLumbering = 163;
        private const int ForagingAnimShortMining = 164;

        private const int ForagingAnimResTree = 1;
        private const int ForagingAnimResStone = 2;
        private const int ForagingAnimResBush = 3;
        private const int ForagingAnimResMeteorite = 9;
        private const int ForagingAnimResDynamicBush = 10;

        private bool foragingAnimEnabled;
        private bool foragingAnimToolHeld;      // we took the axe for this run
        private bool foragingAnimWitnessed;     // hysteresis state
        private int foragingAnimPending;        // swings still owed
        private int foragingAnimKind;           // ResType being played
        private Vector3 foragingAnimNode;
        private float foragingAnimNextSwingAt;
        private float foragingAnimSwingDeadline;
        private int foragingAnimRetriesLeft;

        // Accounting for the one-line-per-node summary below. Kept unconditional: the failure this
        // feature actually has — swings that are ACCEPTED but never seen — is invisible without a
        // record of how many casts landed and over what span, and one line per node is affordable.
        private float foragingAnimQueueStartedAt;
        private float foragingAnimHoldSpent;   // dwell time spent waiting on swings, per node
        private int foragingAnimQueueWanted;
        private int foragingAnimCastsAccepted;
        private string foragingAnimStatus = "Idle.";

        public bool ForagingAnimEnabled
        {
            get { return this.foragingAnimEnabled; }
            set { this.foragingAnimEnabled = value; }
        }

        public string ForagingAnimStatus
        {
            get { return this.foragingAnimStatus; }
        }

        // ── lifecycle ───────────────────────────────────────────────────────────────────────────

        // Start Foraging: take the axe once, here, while we are still standing on the ground. Doing
        // it per node would cost a server round trip each time and read as constant tool-swapping.
        private void NoteForagingAnimRunStarted()
        {
            this.foragingAnimPending = 0;
            this.foragingAnimWitnessed = false;

            if (!this.foragingAnimEnabled)
            {
                return;
            }

            if (this.TryGetFarmWalkSwimLocomotion(out IntPtr _))
            {
                this.foragingAnimStatus = "Swimming — axe not taken.";
                return;
            }

            FarmToolBroker.Acquire("ForagingAnim");
            if (this.TryForagingAnimEquipAxe())
            {
                this.foragingAnimToolHeld = true;
                this.foragingAnimStatus = "Axe taken for animations.";
            }
            else
            {
                FarmToolBroker.Release(this, restoreTool: false);
                this.foragingAnimStatus = "Could not take the axe — swings will not render.";
            }

            if (MasterLogForagingAnim)
            {
                ModLogger.Msg("[ForagingAnim] run started, toolHeld=" + this.foragingAnimToolHeld);
            }
        }

        private void NoteForagingAnimRunStopped()
        {
            this.foragingAnimPending = 0;
            this.foragingAnimWitnessed = false;

            if (this.foragingAnimToolHeld)
            {
                this.foragingAnimToolHeld = false;
                FarmToolBroker.Release(this, restoreTool: true);
                this.foragingAnimStatus = "Idle (axe returned).";
            }
            else
            {
                this.foragingAnimStatus = "Idle.";
            }
        }

        // Arrival at a node, called from EnterFarmCollectingAfterWalk BEFORE the state flips to
        // Collecting. Queues the swings; it never blocks or delays the collect, which Aura Farm owns.
        private void NoteForagingAnimArrival(Vector3 node)
        {
            if (!this.foragingAnimEnabled || !this.farmWalkToNodeEnabled)
            {
                return;
            }

            this.ReportForagingAnimQueue("node changed");

            // Zeroed for EVERY node, not just the ones that queue swings: it extends this node's
            // collect-wait budget, and carrying a previous node's hold into an unwitnessed one would
            // silently lengthen its dwell.
            this.foragingAnimHoldSpent = 0f;

            if (!this.IsForagingAnimWitnessed(node))
            {
                this.foragingAnimPending = 0;
                return;
            }

            int resType = this.ResolveForagingAnimNodeKind(node);
            if (resType == 0)
            {
                this.foragingAnimStatus = "Witnessed, but the node kind did not resolve.";
                return;
            }

            this.foragingAnimKind = resType;
            this.foragingAnimNode = node;
            this.foragingAnimPending = IsForagingAnimGather(resType) ? 1 : ForagingAnimSwings;
            this.foragingAnimNextSwingAt = 0f; // first one goes on the next tick
            this.foragingAnimSwingDeadline = 0f;
            this.foragingAnimRetriesLeft = ForagingAnimSwingRetries;
            this.foragingAnimQueueStartedAt = Time.unscaledTime;
            this.foragingAnimQueueWanted = this.foragingAnimPending;
            this.foragingAnimCastsAccepted = 0;
            this.foragingAnimStatus = "Witnessed — playing " + ForagingAnimKindName(resType) + ".";

            if (MasterLogForagingAnim)
            {
                ModLogger.Msg("[ForagingAnim] arrival at " + node + " kind=" + ForagingAnimKindName(resType)
                              + " swings=" + this.foragingAnimPending);
            }
        }

        private void ProcessForagingAnimOnUpdate()
        {
            if (this.foragingAnimPending <= 0)
            {
                return;
            }

            if (!this.autoFarmActive)
            {
                this.ReportForagingAnimQueue("foraging stopped");
                this.foragingAnimPending = 0;
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.foragingAnimNextSwingAt)
            {
                return;
            }

            // Wait for the previous swing to hand the actor back. IsAnimState(Idle) is the same test
            // the action itself finishes on (ActionClipWeaponComb.OnBehaveTick), so this tracks the
            // real clip instead of a guessed constant; the deadline bounds a stuck animator.
            if (this.foragingAnimSwingDeadline > 0f)
            {
                if (now < this.foragingAnimSwingDeadline && !this.IsForagingAnimIdle())
                {
                    return;
                }

                this.foragingAnimSwingDeadline = 0f;
                this.foragingAnimNextSwingAt = now + ForagingAnimSwingGap;
                return;
            }

            if (this.TryPlayForagingAnim(this.foragingAnimKind, this.foragingAnimNode, out string status))
            {
                this.foragingAnimPending--;
                this.foragingAnimCastsAccepted++;
                this.foragingAnimRetriesLeft = ForagingAnimSwingRetries;
                this.foragingAnimSwingDeadline = now + ForagingAnimSwingTimeout;
                if (this.foragingAnimPending <= 0)
                {
                    this.ReportForagingAnimQueue("complete");
                }

                return;
            }

            // Refused. Most likely the previous swing still owns the actor, so give the SAME swing
            // another beat rather than dropping what is left of the sequence — a chop that stops
            // after one hit is more conspicuous than one that runs a beat long.
            if (this.foragingAnimRetriesLeft > 0)
            {
                this.foragingAnimRetriesLeft--;
                this.foragingAnimNextSwingAt = now + ForagingAnimSwingGap;
                this.foragingAnimStatus = "Animation retry: " + status;
                ModLogger.Msg("[ForagingAnim] swing refused (" + status + "), retrying — "
                              + this.foragingAnimPending + " left.");
                return;
            }

            this.foragingAnimPending = 0;
            this.foragingAnimStatus = "Animation failed: " + status;
            ModLogger.Msg("[ForagingAnim] " + this.foragingAnimStatus);
        }

        // Keep the farm standing at the node while swings are still owed. The collect is untouched —
        // Aura Farm sends its own commands throughout — this delays only the HOP to the next node,
        // which is the one thing that was cutting the sequence short.
        internal bool ShouldHoldFarmNodeForForagingAnim()
        {
            return this.foragingAnimPending > 0
                && this.foragingAnimQueueWanted > 0
                && Time.unscaledTime - this.foragingAnimQueueStartedAt < ForagingAnimHoldBudget;
        }

        // Is the body animation back to Idle? AnimStateHash.Idle is Animator.StringToHash("Idle") —
        // the same value used here, which is what the action's own finish test compares against.
        private bool IsForagingAnimIdle()
        {
            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr animClass = this.FindAuraMonoClassInAllLoadedImages(
                "AnimationComponent", "XDTLevelAndEntity.EntityView");
            IntPtr getAnim = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "get_animationComponent", 0);
            IntPtr isState = animClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(animClass, "IsAnimState", 1);
            if (getAnim == IntPtr.Zero || isState == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return true; // cannot tell — fall back to the deadline rather than stalling
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(playerClass,
                        out System.Collections.Generic.List<IntPtr> players, pins)
                    || players == null || players.Count == 0)
                {
                    return true;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr anim = auraMonoRuntimeInvoke(getAnim, players[0], IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || anim == IntPtr.Zero)
                {
                    return true;
                }

                return this.InvokeForagingAnimIsState(isState, anim, Animator.StringToHash("Idle"));
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool InvokeForagingAnimIsState(IntPtr method, IntPtr anim, int stateHash)
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&stateHash);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, anim, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return true;
            }

            return Marshal.ReadByte(boxed, 2 * IntPtr.Size) != 0;
        }

        // One line per node: how many casts the game ACCEPTED, out of how many were wanted, over how
        // long, and why the queue ended. "3/3 accepted" next to a witness who saw two swings means the
        // casts are landing and the ANIMATOR is dropping them — a different problem from a short
        // dwell, and the two are indistinguishable without this.
        private void ReportForagingAnimQueue(string why)
        {
            if (this.foragingAnimQueueWanted <= 0)
            {
                return;
            }

            ModLogger.Msg("[ForagingAnim] " + ForagingAnimKindName(this.foragingAnimKind) + ": "
                          + this.foragingAnimCastsAccepted + "/" + this.foragingAnimQueueWanted
                          + " casts accepted over "
                          + (Time.unscaledTime - this.foragingAnimQueueStartedAt).ToString("F2") + "s ("
                          + why + (this.foragingAnimPending > 0
                              ? ", " + this.foragingAnimPending + " unplayed"
                              : string.Empty) + ").");

            this.foragingAnimQueueWanted = 0;
        }

        // ── witnesses ───────────────────────────────────────────────────────────────────────────

        // Any REMOTE player within the radius of the NODE — measured from the collection point, not
        // from the character, because that is what a witness is standing near. Hysteresis so a player
        // hovering at the edge does not toggle the behaviour every node.
        private bool IsForagingAnimWitnessed(Vector3 node)
        {
            float radius = this.foragingAnimWitnessed ? ForagingAnimRadiusExit : ForagingAnimRadius;
            float radiusSqr = radius * radius;

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "RemotePlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            if (playerClass == IntPtr.Zero)
            {
                return false;
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            bool found = false;
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(playerClass,
                        out System.Collections.Generic.List<IntPtr> players, pins)
                    || players == null)
                {
                    return this.foragingAnimWitnessed = false;
                }

                for (int i = 0; i < players.Count && !found; i++)
                {
                    if (this.TryGetAuraMonoEntityPositionFromComponent(players[i], out Vector3 pos)
                        && (pos - node).sqrMagnitude <= radiusSqr)
                    {
                        found = true;
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            this.foragingAnimWitnessed = found;
            return found;
        }

        // ── node kind ───────────────────────────────────────────────────────────────────────────

        // ResType straight from the game: the nearest CollectableObjectComponent to the node. The
        // radar's marker label cannot be trusted for this — it is a map-marker name, not the resource
        // type the actions switch on.
        private int ResolveForagingAnimNodeKind(Vector3 node)
        {
            IntPtr collectable = this.FindAuraMonoClassInAllLoadedImages(
                "CollectableObjectComponent", "XDTLevelAndEntity.Gameplay.Component.Gather");
            if (collectable == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr getResType = this.FindAuraMonoMethodOnHierarchy(collectable, "get_resType", 0);
            if (getResType == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(collectable,
                        out System.Collections.Generic.List<IntPtr> nodes, pins)
                    || nodes == null)
                {
                    return 0;
                }

                float bestSqr = ForagingAnimNodeMatchRadius * ForagingAnimNodeMatchRadius;
                IntPtr best = IntPtr.Zero;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!this.TryGetAuraMonoEntityPositionFromComponent(nodes[i], out Vector3 pos))
                    {
                        continue;
                    }

                    float d = (pos - node).sqrMagnitude;
                    if (d < bestSqr)
                    {
                        bestSqr = d;
                        best = nodes[i];
                    }
                }

                if (best == IntPtr.Zero)
                {
                    return 0;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(getResType, best, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return 0;
                }

                return this.ReadAuraMonoBoxedInt32(boxed);
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private static bool IsForagingAnimGather(int resType)
        {
            return resType == ForagingAnimResBush || resType == ForagingAnimResDynamicBush;
        }

        private static string ForagingAnimKindName(int resType)
        {
            switch (resType)
            {
                case ForagingAnimResTree: return "chop";
                case ForagingAnimResStone: return "mine";
                case ForagingAnimResMeteorite: return "mine";
                case ForagingAnimResBush:
                case ForagingAnimResDynamicBush: return "gather";
                default: return "type " + resType;
            }
        }

        // ── the cast ────────────────────────────────────────────────────────────────────────────

        private unsafe bool TryPlayForagingAnim(int resType, Vector3 node, out string status)
        {
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoObjectNew == null || auraMonoFieldSetValue == null
                || auraMonoClassGetFieldFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr castMethod = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "Cast", 1);
            if (castMethod == IntPtr.Zero)
            {
                status = "Cast(1) unresolved";
                return false;
            }

            bool gather = IsForagingAnimGather(resType);
            string contextName = gather
                ? "PlayerGatherContinuous"
                : (resType == ForagingAnimResTree ? "PlayerAxeAttackTree" : "PlayerAxeAttackStone");
            string contextNamespace = gather
                ? "XDTLevelAndEntity.Gameplay.Action"
                : "ScriptsRefactory.LevelAndEntity.Gameplay.Action";

            IntPtr argClass = this.FindAuraMonoClassInAllLoadedImages(contextName, contextNamespace);
            if (argClass == IntPtr.Zero)
            {
                status = contextName + " unresolved";
                return false;
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(playerClass,
                        out System.Collections.Generic.List<IntPtr> players, pins)
                    || players == null || players.Count == 0)
                {
                    status = "no local player";
                    return false;
                }

                IntPtr argObj = auraMonoObjectNew(this.auraMonoRootDomain, argClass);
                if (argObj == IntPtr.Zero)
                {
                    status = "context alloc failed";
                    return false;
                }

                if (!this.TryGetAuraMonoEntityPositionFromComponent(players[0], out Vector3 selfPos))
                {
                    selfPos = node;
                }

                // Face the node: both contexts implement IFaceDirectionWhenPlaying and read this
                // straight back, so without it the character swings at whatever it happened to face.
                Vector2 face = new Vector2(node.x - selfPos.x, node.z - selfPos.z);
                if (face.sqrMagnitude > 0.0001f)
                {
                    face.Normalize();
                }

                if (gather)
                {
                    this.SetForagingAnimInt(argObj, argClass, "maxComboTime", ForagingAnimSwings);
                    this.SetForagingAnimVector2(argObj, argClass, "faceDir", face);
                }
                else
                {
                    int shortName = resType == ForagingAnimResTree
                        ? ForagingAnimShortLumbering
                        : ForagingAnimShortMining;
                    this.SetForagingAnimInt(argObj, argClass, "controllerFullName",
                        ForagingAnimControllerId(this.ReadForagingAnimOverride(playerClass, players[0]), shortName));
                    this.SetForagingAnimVector2(argObj, argClass, "faceDirection", face);
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(castMethod, players[0], (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Cast threw 0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                int code = result == IntPtr.Zero ? 0 : this.ReadAuraMonoBoxedInt32(result);
                status = "ActionErrorCode " + code;
                return code == 0;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // (charType << 24) | (poseType << 14) | (override << 9) | shortName — player=1, stand=1.
        private static int ForagingAnimControllerId(int overrideType, int shortName)
        {
            return (1 << 24) | (1 << 14) | (overrideType << 9) | shortName;
        }

        private int ReadForagingAnimOverride(IntPtr playerClass, IntPtr player)
        {
            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(playerClass, "GetControllerOverrideType", 0);
            if (getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 5; // empty hands
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, player, IntPtr.Zero, ref exc);
            return (exc != IntPtr.Zero || boxed == IntPtr.Zero) ? 5 : this.ReadAuraMonoBoxedInt32(boxed);
        }

        private unsafe void SetForagingAnimInt(IntPtr obj, IntPtr klass, string fieldName, int value)
        {
            IntPtr field = auraMonoClassGetFieldFromName(klass, fieldName);
            if (field != IntPtr.Zero)
            {
                auraMonoFieldSetValue(obj, field, (IntPtr)(&value));
            }
        }

        private unsafe void SetForagingAnimVector2(IntPtr obj, IntPtr klass, string fieldName, Vector2 value)
        {
            IntPtr field = auraMonoClassGetFieldFromName(klass, fieldName);
            if (field != IntPtr.Zero)
            {
                auraMonoFieldSetValue(obj, field, (IntPtr)(&value));
            }
        }

        // Boxed value type: the payload starts one object header (2 pointers) past the handle.
        private int ReadAuraMonoBoxedInt32(IntPtr boxed)
        {
            return boxed == IntPtr.Zero ? 0 : Marshal.ReadInt32(boxed, 2 * IntPtr.Size);
        }

        private bool TryGetAuraMonoEntityPositionFromComponent(IntPtr component, out Vector3 position)
        {
            position = Vector3.zero;
            if (component == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr componentClass = auraMonoObjectGetClass == null ? IntPtr.Zero : auraMonoObjectGetClass(component);
            IntPtr getEntity = componentClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(componentClass, "get_entity", 0);
            if (getEntity == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr entity = auraMonoRuntimeInvoke(getEntity, component, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || entity == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetAuraMonoEntityPosition(entity, out position);
        }

        private bool TryForagingAnimEquipAxe()
        {
            IntPtr toolSystem = this.FindAuraMonoClassInAllLoadedImages(
                "ToolSystem", "XDTGameSystem.GameplaySystem.Tool");
            IntPtr setHandhold = toolSystem == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(toolSystem, "SetHandhold", 1);
            IntPtr instance = toolSystem == IntPtr.Zero ? IntPtr.Zero : this.TryGetAuraMonoDataModuleInstance(toolSystem);
            if (setHandhold == IntPtr.Zero || instance == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            return this.InvokeForagingAnimSetHandhold(setHandhold, instance, 1); // ToolType.Axe
        }

        private unsafe bool InvokeForagingAnimSetHandhold(IntPtr method, IntPtr instance, int toolId)
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&toolId);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }
    }
}
