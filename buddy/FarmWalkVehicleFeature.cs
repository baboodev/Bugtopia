using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Vehicles for the long hauls between farm areas.
    //
    // WHY. Areas sit 200-400 m apart (356 and 390 m in the logs). On foot that is over a minute per
    // move; the server meanwhile allows 9 m/s in a vehicle against 4.3 m/s on foot
    // (MovementAntiCheating), so riding is not only faster but safer as far as anti-cheat goes.
    //
    // ⚠️ VEHICLES DO NOT SUMMON UNDERWATER. Confirmed twice by live tests: the server refuses
    // silently (VehicleErrorCode.AreaForbid sends neither an event nor a type). Sea hauls go on
    // foot, and that is not a fault — the check sits before the summon so no timeout is spent.
    //
    // ⚠️ SUMMONING NEEDS ROOM. VehicleUtility.CreateVehiclePosition runs an OverlapBox directly in
    // front of the player, and a vehicle abandoned nearby blocks the next summon. Hence the recall
    // before every summon.
    //
    // ⚠️ SUCCESS IS SILENT. The server answers only on refusal, so being seated is confirmed by
    // polling GetUsingVehicle() rather than by waiting for an event.
    public partial class HeartopiaComplete
    {
        // How far from the target we dismount. A slider: below the floor the vehicle delivers us
        // right on top of the node and gets in the way of the approach; above the ceiling the walk
        // that is left takes longer than the ride saved.
        internal const float FarmWalkVehicleDismountFloor = 1f;
        internal const float FarmWalkVehicleDismountCeiling = 20f;
        internal float farmWalkVehicleDismountDistance = 10f;

        // Set when the vehicle was left because it could not get past something, cleared as soon as
        // we are back in one (or the walk ends). See TryRemountFarmWalkVehicle.
        private bool farmWalkVehicleLeftForObstacle;
        private Vector3 farmWalkVehicleLeftAt;

        // How far past the obstacle counts as "past it". Short: the point is that the wedge is
        // behind us, not that we have walked the rest of the way.
        private const float FarmWalkVehicleRemountClearance = 8f;

        // Unsticking in a vehicle: back up, then step aside.
        // 2 m, not 5: the car rolls back just far enough to free its nose for the turn. Five metres
        // is already a manoeuvre that can run into something of its own (rule 3.2).
        private const float FarmWalkVehicleBackOffDistance = 2f;
        private const float FarmWalkVehicleSideStepDistance = 5f;

        // Two rounds of back-up-then-sidestep. If that did not help, dismount and fall through to
        // the on-foot ladder.
        private const int FarmWalkVehicleUnstickRoundLimit = 2;

        // Guard on an unstick leg: it ends on distance, and the timer is only for the case where
        // this direction turns out to be blocked too.
        private const float FarmWalkVehicleUnstickLegTimeout = 4f;

        private const int FarmWalkUnstickVehicleBackOff = 5;
        private const int FarmWalkUnstickVehicleSideStep = 6;

        // Guards the mount round-trip window — see ShouldFarmWalkSummonVehicle.
        private const float FarmWalkVehicleSummonCooldown = 5f;

        // ⚠️ THE MIRROR OF THE SUMMON WINDOW, AND IT WAS MISSING.
        //
        // Mounting is a server round-trip, so IsFarmWalkRidingVehicle answers "no" for a moment
        // after a successful summon — that is what the cooldown above guards. Getting OUT is the
        // same round-trip in the other direction: it answers "yes" for a moment after a dismount.
        //
        // Nothing guarded that side, so the escape ladder — which checks "am I driving?" first —
        // saw a vehicle that was no longer there and started ANOTHER reverse-and-sidestep round on
        // a player standing on their own feet. Measured 05:37:04, one second apart:
        //     dismounted from netId 2336382 (stuck in the vehicle).
        //     wedged (not closing) — round 1/2: reversing 2m, then 5m to the right.
        // which is precisely what the dismount exists to prevent: the comment on
        // BeginFarmWalkVehicleUnstick says it gets out so the ON-FOOT ladder (jumps, hop burst,
        // probe) can have it, and then the car's manoeuvre took the frame anyway.
        private const float FarmWalkVehicleDismountSettle = 2f;
        private float farmWalkVehicleLastDismountAt = -999f;

        // "Is the vehicle what the escape should act on?" — riding AND past the dismount round-trip.
        // Every escape site asks THIS, never IsFarmWalkRidingVehicle directly.
        internal bool IsFarmWalkVehicleSteering()
        {
            return this.IsFarmWalkRidingVehicle()
                && Time.unscaledTime - this.farmWalkVehicleLastDismountAt >= FarmWalkVehicleDismountSettle;
        }
        private float farmWalkVehicleLastSummonAt = -999f;

        // ⭐ THE PLAYER MOUNTS AND DISMOUNTS TOO, AND BOTH ROUND-TRIP WINDOWS ONLY KNEW ABOUT US.
        //
        // farmWalkVehicleLastSummonAt and farmWalkVehicleLastDismountAt were stamped where the mod
        // ISSUES a command, so a seat change the player made themselves was covered by neither:
        //   * they get out → no settle window → IsFarmWalkVehicleSteering answers "yes" for a moment
        //     on someone standing on their own feet, and the escape runs a reverse-and-sidestep on a
        //     pedestrian — the exact failure the settle window was added for (measured 05:37:04),
        //     reached through the other door;
        //   * they get in → no summon window → the live check still answers "no" for a moment and
        //     the mod can summon its own vehicle on top of the one they just took.
        //
        // So stamp on the OBSERVED transition instead of on our own command. Ownership is the
        // discriminator and it is exact: the mod sets farmWalkVehicleOurs at command time, so a
        // transition that disagrees with it is the player's doing — riding while we claim nothing
        // means they got in, not riding while we still claim the seat means they got out.
        private bool farmWalkVehicleSeatSeen;
        private bool farmWalkVehicleSeatSeenValid;

        internal void ObserveFarmWalkVehicleSeat()
        {
            bool riding = this.IsFarmWalkRidingVehicle();
            if (this.farmWalkVehicleSeatSeenValid && riding == this.farmWalkVehicleSeatSeen)
            {
                return;
            }

            bool first = !this.farmWalkVehicleSeatSeenValid;
            this.farmWalkVehicleSeatSeenValid = true;
            this.farmWalkVehicleSeatSeen = riding;
            if (first)
            {
                // Nothing changed — this is the first reading, and a first reading is a baseline,
                // not a transition. Stamping here would open a settle window nobody asked for.
                return;
            }

            if (riding)
            {
                this.farmWalkVehicleLastSummonAt = Time.unscaledTime;
                if (!this.farmWalkVehicleOurs)
                {
                    ModLogger.Msg("[FarmVehicle] the player took a seat we did not summon — the walk "
                        + "treats it as a vehicle from here, and will still get out to collect.");
                }
            }
            else
            {
                this.farmWalkVehicleLastDismountAt = Time.unscaledTime;
                if (this.farmWalkVehicleOurs)
                {
                    // Our own dismount clears the claim at command time, so reaching here with it
                    // still set means the player left the seat by hand.
                    this.farmWalkVehicleOurs = false;
                    ModLogger.Msg("[FarmVehicle] the player left the seat we summoned — dropping our "
                        + "claim on it.");
                }
            }
        }

        private int farmWalkVehicleUnstickRounds;

        // ⚠️ THE ROUND BUDGET IS PER OBSTACLE, NOT PER RIDE.
        //
        // The counter was zeroed on mount and on dismount and nowhere else, so it accumulated over
        // a whole haul: two unrelated obstacles, cleared successfully and tens of metres apart,
        // spent both rounds and the third wedge threw the driver out. Measured over one 270 m haul:
        //     05:36:28  wedged at 270,7m - round 1/2 ... side-stepped 5,0m of 5m (clear).
        //     05:36:39  wedged at 232,7m - round 2/2 ... side-stepped 5,0m of 5m (clear).
        //     05:37:04  2 reverse-and-sidestep rounds did not clear it - getting out.
        // Thirty-eight metres of driving between the two, so BOTH rounds had in fact cleared it and
        // the message announcing otherwise was simply false.
        //
        // Driving well past where the manoeuvre started is proof the obstacle is behind us, and the
        // budget belongs to the next one. The threshold has to clear the manoeuvre itself, which is
        // 2 m back plus 5 m sideways.
        private const float FarmWalkVehicleUnstickClearedDistance = 15f;

        // Called from the walker's progress sample - the one place that already knows the body is
        // moving - so this cannot drift out of step with what "progress" means elsewhere.
        internal void NoteFarmWalkVehicleProgress(Vector3 selfPos)
        {
            if (this.farmWalkVehicleUnstickRounds <= 0 || !this.IsFarmWalkRidingVehicle())
            {
                return;
            }

            if (Distance3D(selfPos, this.farmWalkVehicleUnstickFrom)
                < FarmWalkVehicleUnstickClearedDistance)
            {
                return;
            }

            ModLogger.Msg("[FarmVehicle] drove "
                + Distance3D(selfPos, this.farmWalkVehicleUnstickFrom).ToString("F0")
                + "m clear of the last wedge - the round budget goes back to the next obstacle.");
            this.farmWalkVehicleUnstickRounds = 0;
        }
        private Vector3 farmWalkVehicleUnstickFrom;
        private int farmWalkVehicleSideSign = 1;

        // Set when the vehicle was summoned by THIS haul: that is the only kind we dismount ourselves.
        private bool farmWalkVehicleOurs;

        private IntPtr farmWalkVehicleSystemModule;
        private IntPtr farmWalkVehicleGetUsingMethod;
        private IntPtr farmWalkVehicleGetDefaultMethod;
        private IntPtr farmWalkVehicleGetOffMethod;
        private bool farmWalkVehicleResolveTried;

        private const string FarmWalkVehicleSystemTypeName = "XDTGameSystem.GameplaySystem.Vehicle.VehicleSystem";
        private const string FarmWalkVehicleProtocolTypeName = "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager";

        private bool EnsureFarmWalkVehicleApi()
        {
            if (this.farmWalkVehicleResolveTried)
            {
                return this.farmWalkVehicleGetDefaultMethod != IntPtr.Zero;
            }

            this.farmWalkVehicleResolveTried = true;
            try
            {
                if (this.TryResolveAuraMonoModule(FarmWalkVehicleSystemTypeName, out IntPtr module))
                {
                    this.farmWalkVehicleSystemModule = module;
                    IntPtr cls = this.FindAuraMonoClassByFullName(FarmWalkVehicleSystemTypeName);
                    if (cls != IntPtr.Zero)
                    {
                        this.farmWalkVehicleGetUsingMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetUsingVehicle", 0);
                        this.farmWalkVehicleGetDefaultMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetDefaultVehicle", 0);
                    }
                }

                IntPtr proto = this.FindAuraMonoClassByFullName(FarmWalkVehicleProtocolTypeName);
                if (proto != IntPtr.Zero)
                {
                    this.farmWalkVehicleGetOffMethod = this.FindAuraMonoMethodOnHierarchy(proto, "GetOffVehicle", 2);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmVehicle] resolve threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            // GetUsingVehicle is resolved but deliberately UNUSED as a mount check — see
            // IsFarmWalkRidingVehicle for why. It stays here only so the log shows whether the
            // type resolved at all.
            ModLogger.Msg("[FarmVehicle] api: default=" + (this.farmWalkVehicleGetDefaultMethod != IntPtr.Zero)
                + " getOff=" + (this.farmWalkVehicleGetOffMethod != IntPtr.Zero)
                + " (config accessor " + (this.farmWalkVehicleGetUsingMethod != IntPtr.Zero ? "present" : "missing")
                + ", not used as the mount check).");
            return this.farmWalkVehicleGetDefaultMethod != IntPtr.Zero;
        }

        // Both accessors return an int staticId, 0 when there is none — see VehicleSystem.cs:140.
        private unsafe bool TryReadFarmWalkVehicleStaticId(IntPtr method, out int staticId)
        {
            staticId = 0;
            if (method == IntPtr.Zero || this.farmWalkVehicleSystemModule == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, this.farmWalkVehicleSystemModule, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            staticId = *(int*)raw;
            return true;
        }

        // ⚠️ NOT GetUsingVehicle(). That reads CurrentUsingVehicleStaticId out of the vehicle CONFIG
        // filter — "which car is selected", a setting, which answers 81009 while the player is
        // standing on their own two feet. Using it as the mount check inverted the whole feature:
        //   * ShouldFarmWalkSummonVehicle ends in !IsFarmWalkRidingVehicle(), so it was ALWAYS
        //     false and the vehicle was NEVER summoned;
        //   * the wedge ladder took the vehicle branch on every stall and had the player on foot
        //     reversing and side-stepping like a car;
        //   * and the dismount then found no netId, because there was nothing to dismount from.
        //
        // The real question is "is there a vehicle entity I am riding", which is what the game's own
        // forbidden-area trigger asks — VehicleManager.Instance.GetSelfEntityVehicle().
        internal bool IsFarmWalkRidingVehicle()
        {
            return this.TryGetSelfEntityVehicleComponentMono() != IntPtr.Zero;
        }

        // Worth summoning? Land only, long enough haul, a default vehicle actually set, and not
        // already riding one.
        //
        // ⚠️ THE HAUL IS THE ROUTE, NOT THE STRAIGHT LINE.
        //
        // This used to test Distance3D(from, to), which is the one number that does not describe
        // the journey about to be made. The planner already knows better and says so in its own
        // log: "nearest by route, not by line: 39m away costs 47m to travel, against 38m away
        // costing 73m" — a target 38 m off measured 73 m of actual walking around a bay. On the
        // straight line that is comfortably under any sane threshold and the farm walked it; the
        // reverse case is just as wrong, summoning a vehicle for 60 m of open beach.
        //
        // The route is committed before this is called (see the note at the call site in
        // FarmWalkFeature.cs — the vehicle is transport for a journey, and whether the journey
        // exists is decided first), so the measurement is already in hand. When there is no route
        // ComputeFarmWalkRouteRemaining falls back to the straight line by itself.
        //
        // ⚠️ The threshold now means something different. A route is almost always longer than
        // the line it spans, so the same slider value summons a vehicle more readily than it did.
        // That is the point — it is the walking that got measured properly — but the number was
        // chosen against the old metric and is worth revisiting against this one.
        internal bool ShouldFarmWalkSummonVehicle(Vector3 from, Vector3 to)
        {
            if (!this.farmWalkUseVehicleEnabled || !this.farmWalkToNodeEnabled)
            {
                return false;
            }

            float haul = this.ComputeFarmWalkRouteRemaining(from);
            if (haul < this.farmWalkVehicleMinDistance)
            {
                return false;
            }

            // Say it only when the two metrics disagree about the verdict — that is the whole
            // behaviour change, and it is rare enough to be worth a line when it happens.
            float straight = Distance3D(from, to);
            if (straight < this.farmWalkVehicleMinDistance)
            {
                ModLogger.Msg("[FarmVehicle] " + haul.ToString("F0") + "m to walk though only "
                    + straight.ToString("F0") + "m away — riding it, the line would have walked it.");
            }

            // Underwater the server refuses the summon outright — check before spending the call.
            if (this.TryGetFarmWalkSwimLocomotion(out _))
            {
                return false;
            }

            if (this.IsFarmWalkRidingVehicle())
            {
                return false;
            }

            // Mounting is a server round-trip, so IsFarmWalkRidingVehicle still answers "no" for a
            // moment after a successful summon. Without this the next walk started inside that
            // window and summoned again — the log showed "summoned 81104 and took the seat" three
            // times back to back for one destination.
            return Time.unscaledTime - this.farmWalkVehicleLastSummonAt >= FarmWalkVehicleSummonCooldown;
        }

        // Summon the favourite vehicle and take the driving seat. Returns false on any failure —
        // every one of them is "walk instead", never "abort the haul".
        internal bool TryFarmWalkSummonAndMount()
        {
            if (!this.EnsureFarmWalkVehicleApi()
                || !this.TryReadFarmWalkVehicleStaticId(this.farmWalkVehicleGetDefaultMethod, out int staticId)
                || staticId == 0)
            {
                ModLogger.Msg("[FarmVehicle] no default vehicle set — walking this haul.");
                return false;
            }

            // Recall first: a vehicle still parked from the last haul trips the summon's own
            // OverlapBox and the next call fails for a reason that reads nothing like the cause.
            this.TryFarmWalkRecallVehicle(staticId);

            if (!this.TryVehicleBypassForceSummon(staticId, true, out string error))
            {
                ModLogger.Msg("[FarmVehicle] summon of " + staticId + " failed: " + error + " — walking.");
                return false;
            }

            this.farmWalkVehicleLastSummonAt = Time.unscaledTime;
            this.farmWalkVehicleOurs = true;
            this.farmWalkVehicleUnstickRounds = 0;
            ModLogger.Msg("[FarmVehicle] summoned " + staticId + " and took the seat for this haul.");
            return true;
        }

        private void TryFarmWalkRecallVehicle(int staticId)
        {
            try
            {
                if (this.TryFindSelfOwnedLiveVehicleByStaticId(staticId, out uint netId) && netId != 0)
                {
                    this.RecallVehicleById(staticId, null);
                    ModLogger.Msg("[FarmVehicle] recalled the vehicle still parked from the last haul (netId "
                        + netId + ").");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmVehicle] recall threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Dismount. GetOffVehicle wants the RIDDEN vehicle's net id.
        internal unsafe void TryFarmWalkDismount(string why)
        {
            this.farmWalkVehicleUnstickRounds = 0;

            if (!this.EnsureFarmWalkVehicleApi() || this.farmWalkVehicleGetOffMethod == IntPtr.Zero)
            {
                return;
            }

            // The RIDDEN vehicle's netId, straight off the component — the same lever the game's own
            // forbidden-area trigger uses to eject a driver (ForbiddenVehicleTriggerCase:43).
            //
            // The world scan was the wrong source and the log said so plainly: "cannot dismount:
            // vehicle 81009 not found in the world scan", every single time, so GetOffVehicle never
            // ran and the player circled the node in the driving seat forever. The scan filters on
            // ownership resolved through LevelEntityComponentData, which is a different question
            // from "what am I sitting in" — kept below only as a fallback.
            uint netId = 0;
            IntPtr selfVehicle = this.TryGetSelfEntityVehicleComponentMono();
            if (selfVehicle != IntPtr.Zero
                && this.TryGetMonoObjectMember(selfVehicle, "entity", out IntPtr entityObj)
                && entityObj != IntPtr.Zero)
            {
                this.TryGetMonoUInt32Member(entityObj, "netId", out netId);
            }

            if (netId == 0)
            {
                // No ridden vehicle: either we already got out, or we never were in one.
                this.farmWalkVehicleOurs = false;
                return;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                uint id = netId;
                int reason = 0; // VehicleGetOffReason.Default
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&reason);
                auraMonoRuntimeInvoke(this.farmWalkVehicleGetOffMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                this.farmWalkVehicleOurs = false;
                this.farmWalkVehicleLastDismountAt = Time.unscaledTime;
                ModLogger.Msg("[FarmVehicle] dismounted from netId " + netId + " (" + why + ").");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmVehicle] dismount threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Blocked while driving. A car cannot jump and cannot be threaded through a gap the way a
        // swimmer can, so its escape is the one a driver would use: reverse, then pull out
        // sideways. Two rounds, alternating sides, and then the vehicle is the problem — get out
        // and let the on-foot ladder (jumps, hop burst, probe) have it.
        internal void BeginFarmWalkVehicleUnstick(Vector3 selfPos, float now, string why)
        {
            this.farmWalkVehicleUnstickRounds++;
            if (this.farmWalkVehicleUnstickRounds > FarmWalkVehicleUnstickRoundLimit)
            {
                ModLogger.Msg("[FarmVehicle] " + FarmWalkVehicleUnstickRoundLimit
                    + " reverse-and-sidestep rounds did not clear it — getting out and continuing on foot.");
                this.TryFarmWalkDismount("stuck in the vehicle");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;

                // Remember WHY we got out. Rule 3.3: the vehicle was abandoned for one obstacle,
                // not because the haul was over — so once the obstacle is behind us and there is
                // still a vehicle's worth of distance left, we get back in.
                this.farmWalkVehicleLeftForObstacle = true;
                this.TryGetNavMeshSelfPosition(out this.farmWalkVehicleLeftAt, out _);
                return;
            }

            // Alternate sides between rounds: whatever blocked the first pull-out is on one side,
            // and trying the same side twice buys nothing.
            this.farmWalkVehicleSideSign = this.farmWalkVehicleUnstickRounds % 2 == 1 ? 1 : -1;
            this.farmWalkUnstickPhase = FarmWalkUnstickVehicleBackOff;
            this.farmWalkVehicleUnstickFrom = selfPos;
            this.farmWalkUnstickPhaseUntil = now + FarmWalkVehicleUnstickLegTimeout;
            ModLogger.Msg("[FarmVehicle] wedged (" + why + ") — round "
                + this.farmWalkVehicleUnstickRounds + "/" + FarmWalkVehicleUnstickRoundLimit
                + ": reversing " + FarmWalkVehicleBackOffDistance.ToString("0.#") + "m, then "
                + FarmWalkVehicleSideStepDistance.ToString("0.#") + "m to the "
                + (this.farmWalkVehicleSideSign > 0 ? "right" : "left") + ".");
        }

        // Each leg ends on DISTANCE covered, with the timer only for "blocked this way too" —
        // the same rule the swim back-off and the probe legs use.
        internal void UpdateFarmWalkVehicleUnstick(Vector3 selfPos, float now)
        {
            float covered = Distance3D(selfPos, this.farmWalkVehicleUnstickFrom);
            bool backOffLeg = this.farmWalkUnstickPhase == FarmWalkUnstickVehicleBackOff;
            float want = backOffLeg ? FarmWalkVehicleBackOffDistance : FarmWalkVehicleSideStepDistance;

            if (covered < want && now < this.farmWalkUnstickPhaseUntil)
            {
                return;
            }

            ModLogger.Msg("[FarmVehicle] " + (backOffLeg ? "reversed " : "side-stepped ")
                + covered.ToString("F1") + "m of " + want.ToString("0.#") + "m ("
                + (covered >= want ? "clear" : "blocked this way too") + ").");

            if (backOffLeg)
            {
                this.farmWalkUnstickPhase = FarmWalkUnstickVehicleSideStep;
                this.farmWalkVehicleUnstickFrom = selfPos;
                this.farmWalkUnstickPhaseUntil = now + FarmWalkVehicleUnstickLegTimeout;
                return;
            }

            this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
        }

        // Start a zone haul. Summons and mounts first when the haul is long enough, so the whole
        // route is driven rather than the vehicle being called halfway.
        internal bool TryBeginFarmWalkToArea(Vector3 areaPos, string areaName)
        {
            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                return false;
            }

            // The summon itself lives in TryBeginFarmWalk now, so every kind of long haul gets it
            // on the same terms — a zone move is just the longest one.
            if (!this.TryBeginFarmWalk(areaPos, "area:" + areaName, false, null))
            {
                // No route — hand back so the caller teleports, and do not leave the player sitting
                // in a vehicle that the haul is no longer going to use.
                if (this.farmWalkVehicleOurs)
                {
                    this.TryFarmWalkDismount("no route to the zone point");
                }

                return false;
            }

            this.farmWalkPendingArea = true;
            ModLogger.Msg("[FarmWalk] zone haul to " + areaName + ": "
                + Distance3D(selfPos, areaPos).ToString("F0") + "m"
                + (this.IsFarmWalkRidingVehicle() ? " by vehicle" : " on foot") + ".");
            return true;
        }

        // Rule 3.3: back in the vehicle once the obstacle is behind us.
        //
        // The threshold is the SAME one that decided to mount in the first place
        // (farmWalkVehicleMinDistance): if the remaining haul would have been worth a vehicle at the
        // start, it is worth one now. Anything shorter and the summon round-trip costs more than it
        // saves — which is exactly the judgement that constant already encodes.
        internal void TryRemountFarmWalkVehicle(Vector3 selfPos)
        {
            if (!this.farmWalkVehicleLeftForObstacle)
            {
                return;
            }

            // Still fighting the same obstacle, or already riding: nothing to do.
            if (this.farmWalkUnstickPhase != FarmWalkUnstickIdle || this.IsFarmWalkRidingVehicle())
            {
                return;
            }

            // Far enough from where we got out that the wedge is genuinely behind us. Without this
            // the summon fires while the player is still against the obstacle and the vehicle wedges
            // on it again immediately.
            if (Distance3D(selfPos, this.farmWalkVehicleLeftAt) < FarmWalkVehicleRemountClearance)
            {
                return;
            }

            if (!this.ShouldFarmWalkSummonVehicle(selfPos, this.farmWalkTarget))
            {
                // Either the rest is too short to be worth a vehicle, or we are swimming, or the
                // summon is on cooldown. The first of those is permanent for this walk; the others
                // resolve on their own, so the flag stays set and this runs again.
                //
                // Measured the same way the decision above measures, or the log explains a refusal
                // with a number that did not cause it.
                float left = this.ComputeFarmWalkRouteRemaining(selfPos);
                if (left < this.farmWalkVehicleMinDistance)
                {
                    this.farmWalkVehicleLeftForObstacle = false;
                    ModLogger.Msg("[FarmVehicle] obstacle cleared, but only " + left.ToString("F0")
                        + "m of route left — walking the rest.");
                }

                return;
            }

            this.farmWalkVehicleLeftForObstacle = false;
            ModLogger.Msg("[FarmVehicle] obstacle cleared with "
                + Distance3D(selfPos, this.farmWalkTarget).ToString("F0")
                + "m still to go — getting back in.");
            this.TryFarmWalkSummonAndMount();
        }

        // Ticked from the walk loop: get out BEFORE the destination, not on top of it. Applies to
        // every haul — a node needs the last stretch on foot anyway (the collect wants the player
        // within 0.25 m, which no vehicle is going to deliver), and a zone point wants the arrival
        // to look like an arrival.
        //
        // ⚠️ WHOEVER PUT THE PLAYER IN THE SEAT, THE COLLECT NEEDS THEM OUT OF IT.
        //
        // This used to ask farmWalkVehicleOurs — a memory of the mod's own summon, never reconciled
        // with the game — so a vehicle the player mounted themselves was invisible here: the haul
        // drove up to the node and tried to collect from the driving seat, which wants the player
        // within a stand-off no vehicle delivers. The escape ladder meanwhile DID see it, because it
        // asks the live check — half the walker treating the same seat as a vehicle and half not.
        //
        // IsFarmWalkVehicleSteering, not IsFarmWalkRidingVehicle: it is the live check plus the
        // dismount round-trip window, so a get-off already in flight is not re-issued every tick.
        // Ownership still governs GIVING THE VEHICLE BACK (AbortFarmWalk, the no-route release) —
        // the mod returns what it summoned; it does not confiscate the player's ride when it stops.
        internal void ProcessFarmWalkVehicleDismount(Vector3 selfPos)
        {
            if (!this.IsFarmWalkVehicleSteering())
            {
                return;
            }

            if (Distance3D(selfPos, this.farmWalkTarget) > this.farmWalkVehicleDismountDistance)
            {
                return;
            }

            this.TryFarmWalkDismount("within " + this.farmWalkVehicleDismountDistance.ToString("0.#")
                + "m of " + (this.farmWalkPendingArea ? "the zone point" : "the node"));
        }

        // Steering for the two vehicle legs: straight back, then perpendicular to the aim.
        internal bool TryGetFarmWalkVehicleUnstickDirection(Vector3 toAim, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (this.farmWalkUnstickPhase == FarmWalkUnstickVehicleBackOff)
            {
                direction = -toAim;
                return true;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickVehicleSideStep)
            {
                // Perpendicular in the ground plane; the sign alternates per round.
                direction = new Vector3(toAim.z, 0f, -toAim.x) * this.farmWalkVehicleSideSign;
                return true;
            }

            return false;
        }
    }
}
