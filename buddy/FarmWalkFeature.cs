using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Aura Farm "walk to the node" mode — move along the ground instead of teleporting, following
    // the route the game's own Track star-line follows.
    //
    // ROUTE SOURCE: the managed snapshot of the game's waypoint graph plus a C# A* over it
    // (TrackPathGraphFeature.cs). NOT a Unity navmesh — the game never loads one, proven in-game.
    //
    // MOVEMENT: TrySetGameMoveAxis (MovementInputFeature.cs) feeds the analog joystick axis through
    // the game's own locomotion chain, so ground-following, collision, slopes and swimming all come
    // for free and the server sees ordinary movement. NEVER a position write — a teleport-driven
    // "walk" is exactly what MovementAntiCheating samples for.
    //
    // THREE DECIDED CONFLICTS (do not re-litigate — see the walk-to-node plan in project memory):
    //   1. Mutually exclusive with Stealth Foraging. That mode parks the player under the terrain
    //      on noclip; walking needs ordinary ground collision. Enabling one clears the other.
    //   2. Forces game speed to 1x for the whole run. ToggleAutoFarm normally sets timeScale 5,
    //      and the server measures REAL time — at 5x a legitimate 4 m/s walk is a 20 m/s cheat.
    //   3. No walk/teleport distance cap: every NODE hop walks, at any distance. Long-hop
    //      teleports are a deferred follow-up. AREA hops (`area:*`) still teleport — those are
    //      cross-map world-load checkpoints, not resource approaches.
    //
    // The teleport path survives only as a SAFETY NET: no route, or stuck-detection escalation.
    public partial class HeartopiaComplete
    {
        // Persisted toggle.
        private bool farmWalkToNodeEnabled;

        // Axis magnitude. The game takes the magnitude verbatim as its joystick value, >0.95 is the
        // sprint threshold (drains stamina, fires UITipEvent 10 under 20), and the game zeroes
        // anything under 0.1 — so 0.2..0.95 is the usable band.
        private float farmWalkSpeed = 0.75f;
        private const float FarmWalkSpeedMin = 0.2f;
        private const float FarmWalkSpeedMax = 0.95f;

        // A corner is "reached" inside this XZ radius. Loose enough that the locomotion's own
        // acceleration curve does not overshoot into an orbit around the point.
        private const float FarmWalkCornerReachDistance = 1.2f;

        // Arrival is measured in 3-D against the SERVER's rule, not in XZ.
        //
        // CollectAntiCheating.Distance is 2 m and the server measures the real separation, so an
        // XZ-only test silently fails on any resource that is not at the player's feet: the first
        // coastal run stopped 5.7 m XZ from an oyster sitting 3.8 m up a rock — 6.8 m apart in 3-D.
        // The aura's own scan radius (AuraDirectScanRadius, 8 m) is far looser and hides this; the
        // server just drops the collect, and per project memory those rejections are SILENT, so the
        // farm looks like it is working while gathering nothing.
        // 0.25 m — walk essentially ONTO the node, the way the teleport path used to land.
        //
        // Reasoning from the in-game runs: at 1.5 m the walker reported "arrived 1,29m" and then
        // "arrived 1,29m" again on the SAME oyster, i.e. it stopped in range of the server's 2 m
        // rule but the collect still did not happen, so the farm re-scanned and re-targeted the
        // same node forever. Being inside the anti-cheat radius is evidently necessary but not
        // sufficient — the gather itself wants to be on top of the resource. Anything the walker
        // cannot quite close now trips the not-closing timeout and gets a very short final hop,
        // which is a strictly better failure than looping on an uncollectable node.
        private const float FarmWalkCollectDistance = 0.25f;

        // Below this range the approach eases off. At full axis the locomotion overshoots a 0.25 m
        // target and orbits it; the game floors the joystick at 0.1, so 0.2 is the slowest usable
        // creep. Anything nearer than this scales linearly between the two.
        private const float FarmWalkSlowApproachDistance = 2f;
        private const float FarmWalkSlowApproachSpeed = 0.2f;

        // Walking cannot change the height difference to a resource, so 0.25 m is a HORIZONTAL
        // target; the 3-D separation just has to end up inside the server's collect rule.
        // Measuring arrival purely in 3-D made an unreachable goal out of anything sitting even
        // slightly above the player: one run logged "final approach not walkable (0,3m, dy=0,3m)"
        // — standing directly under a mushroom 0.3 m up, with 0 m of horizontal distance left to
        // walk, timing out because 3-D distance could never drop below 0.3.
        private const float FarmWalkServerCollectDistance = 1.8f;

        // Steering smoothing. The raw desired direction can swing hard when a corner is cleared or
        // a re-path lands, and the character turns to face wherever it is driven — so feeding the
        // raw value makes the avatar snap around. Cap the turn rate instead.
        private const float FarmWalkTurnRateDegPerSecond = 360f;

        // Jump-to-unstick. Fences, kerbs and roots stop the walker dead while the route insists on
        // going straight through them; a hop clears most of them. Throttled, and capped per walk so
        // a genuinely impassable obstacle still escalates to the teleport instead of pogoing.
        // 0.5 s, deliberately shorter than the stuck sampler's 0.6 s: at 1.2 s only ONE jump could
        // ever fire before three 0.6 s strikes escalated the walk, so the budget of 4 was fiction.
        private const float FarmWalkJumpInterval = 0.5f;
        private const int FarmWalkMaxJumpsPerWalk = 4;

        // Dive / surface. Underwater the horizontal steering parks the player directly over a sea
        // grape and then has nothing left to give — one run logged "arrived 0,24m horizontally
        // (1,60m 3-D)" and "under the node but 5,3m away vertically". Depth is a separate axis and
        // needs a separate input.
        //
        // SwimLocomotion.SetSwimVerticalInput(bool isAscending, bool isPressed) is the game's own
        // dive/surface control and is consumed in EVERY swim move mode: under
        // CameraControlWithButtons it overrides the camera-pitch path, and in the other modes the
        // else-branch reads it directly (ComputeTargetVelocity3D). So this works without touching
        // the camera and without caring what the player has the swim setting on.
        // Depth control needs HYSTERESIS: engage at 0.35 m, but keep holding until inside 0.12 m.
        // With a single threshold the control chatters the moment dy settles near it — one run
        // logged "surfacing 0,4m" nine times and "diving 0,4m" twenty-five times on one node, each
        // line a direction REVERSAL. Worse than noise: the setter refuses a reversal within
        // VerticalInputBufferTime (0.3 s) of the last press, so most of those were swallowed and
        // the player hung at one depth going nowhere.
        private const float FarmWalkDepthEngageTolerance = 0.35f;
        private const float FarmWalkDepthReleaseTolerance = 0.12f;
        // Re-assert a held direction on this cadence. NOT every frame: each press stamps
        // _verticalInputBufferStartTime, and the setter refuses a REVERSAL within
        // VerticalInputBufferTime (0.3 s) of the last press — pressing every frame would pin that
        // timer at "just now" and make dive->surface impossible.
        private const float FarmWalkDepthReassertInterval = 1f;

        // Vertical/horizontal ORDERING (user directive). Underwater the two axes are cheap to
        // separate, and doing them in the right order avoids most of the geometry that blocks a
        // diagonal swim:
        //   * target ABOVE  -> float up FIRST, then swim across. Rising in place clears the reef
        //     or shelf you are standing under before travelling over it.
        //   * target BELOW  -> swim across FIRST, then descend at the end. Descending early puts
        //     the swim down at floor level, scraping every rock between here and there.
        // The descent is released once within this horizontal distance of the aim point.
        // Sprint on long legs only.
        //
        // LAND: the axis magnitude IS the speed, and full stick reaches MotionInfo.MovingSpeedLimit
        // = 4.0 m/s — the game's own run cap, comfortably under MovementAntiCheating's 4.3 m/s
        // walk threshold. So "sprinting" on foot is just holding the stick fully; no extra risk.
        //
        // WATER: SwimLocomotion.TryStartSprint is a real dash — SwimSprintConfig.MaxSpeed = 10 m/s,
        // above even the 9 m/s vehicle threshold. It is burst-shaped (0.5 s accelerate, 1 s
        // decelerate, 3 s cooldown) so the sustained average stays far lower, and every guard is
        // the game's own (cooldown, stamina via IsSprintBlockedByStamina, CanStartSprint). Kept to
        // long legs so it is occasional rather than a constant strobe.
        // Start well out, and CANCEL well before arrival — a dash carries through a 1 s
        // deceleration at up to 10 m/s, so stopping it at the door is far too late to settle into
        // the 0.25 m approach. The gap between the two also gives hysteresis, so a leg hovering
        // near the threshold cannot strobe the dash on and off.
        private const float FarmWalkSprintMinDistance = 25f;
        // 1 m (user setting). The dash runs almost the whole leg; the brake is then a genuine stop
        // rather than an early coast — the reversed steer decelerates as well as cancelling, which
        // is what makes such a late cutoff workable at all.
        private const float FarmWalkSprintStopDistance = 1f;
        private const float FarmWalkSprintRetryInterval = 1.5f;
        private const float FarmWalkSprintCancelInterval = 0.6f;
        private const int FarmWalkMaxSprintCancelTries = 3;
        // How long to steer backwards to trigger the game's large-turn sprint cancel. Long enough
        // for the locomotion to register the new direction, short enough to lose almost no ground.
        private const float FarmWalkDashBrakeSeconds = 0.35f;

        private const float FarmWalkDescendHoldDistance = 4f;

        // Inside this horizontal radius the approach is treated as purely vertical and the move
        // axis is released. Deliberately tight: it exists only to stop the walker steering on a
        // direction that is mostly noise, not to halt travel early.
        private const float FarmWalkVerticalOnlyRadius = 1f;
        // ...and only when the drop is worth stopping for. This value sits between two failures:
        //   too LOW (no minimum)  -> the depth hysteresis keeps a dive held down to 0.12 m, so a
        //                            0.2 m drop cleared the horizontal axis and froze the walker
        //                            exactly 1.0 m short of the node.
        //   too HIGH (1.5 m)      -> a 0.35–1.5 m drop got no hold at all, so the walker kept
        //                            re-aiming at a target one metre away, where the bearing is
        //                            mostly noise, and circled instead of descending.
        // 0.5 m clears the release tolerance comfortably while still catching every real drop.
        private const float FarmWalkVerticalOnlyMinDrop = 0.5f;
        // A climb cannot hold the horizontal forever — if something caps the ascent, resume
        // swimming rather than hovering under it (the unstick sequence handles the obstacle).
        private const float FarmWalkClimbFirstTimeout = 6f;

        // Underwater obstacle clearing. A jump is meaningless in water — the last run burned all
        // four on a reef ("jump 1/4 … 4/4 (not closing)") and still teleported — so while swimming
        // the unstick action is a sustained ASCEND instead: rise over the rock, then carry on. Held
        // for a stretch rather than pulsed, because clearing a reef takes metres, not a hop.
        private const float FarmWalkObstacleAscendDuration = 2.5f;

        // Underwater unstick is TWO phases: back off 5 m, then ascend. Rising while still pressed
        // against a reef just scrapes along it; reversing first buys the clearance for the climb to
        // actually go somewhere. The distance is the goal and the timeout is only a guard, so a
        // back-off that is itself blocked cannot stall the sequence.
        private const float FarmWalkBackOffDistance = 5f;
        private const float FarmWalkBackOffTimeout = 3f;

        private const int FarmWalkUnstickIdle = 0;
        private const int FarmWalkUnstickBackingOff = 1;
        private const int FarmWalkUnstickAscending = 2;
        private const int FarmWalkUnstickProbing = 3;

        // Final-approach probe: when the walker is close but wedged, sweep the eight compass
        // directions one at a time, each followed by a vertical attempt, looking for the gap in
        // whatever geometry is in the way. Cheaper than giving up on the node, and it is the only
        // search available — the mod has no collision data to reason about the obstacle with.
        // Aborted the moment the 3-D distance improves.
        private const float FarmWalkProbeMoveSeconds = 0.8f;
        private const float FarmWalkProbeVerticalSeconds = 0.6f;
        private const int FarmWalkProbeDirections = 8;
        private const float FarmWalkProbeProgress = 0.4f;
        private const int FarmWalkProbeStageHorizontal = 0;
        private const int FarmWalkProbeStageVertical = 1;

        // Contamination is worked by the sea-clean SWEEP, not by walking onto it, and the two
        // anchor classes want opposite standoffs (TryGetContaminatedAnchorClass, SeaCleanQteFeature):
        //   * HOSTED / static — permanently attached to a coral on the sea floor. Hover 3 m ABOVE:
        //     the pollutant volume sits on the ground, so approaching from over it keeps the sweep
        //     clear of the terrain the coral is planted in.
        //   * POINT / dynamic — temporary, spawned floating in open water. Sit 3 m BELOW, so the
        //     sweep comes up into the volume instead of pushing it away from above.
        // Applied to the WALKER'S AIM ONLY. lastNodePosition keeps the true position, so marker
        // matching, cooldown stamping and the dwell are all unaffected.
        private const float FarmWalkContaminationStandoff = 3f;

        // Within this range of the target, "no progress" means the last metres are simply not
        // walkable (a ledge, a rock, a jetty). Escalate to the final-approach teleport on the FIRST
        // stuck sample instead of burning the full three strikes on something that cannot be fixed
        // by walking harder.
        private const float FarmWalkFinalApproachDistance = 8f;

        // Re-path cadence, and the off-corridor distance that forces one early. 4 m mirrors the
        // game's own live deviateDis (16 sqr).
        private const float FarmWalkRepathInterval = 1.5f;
        private const float FarmWalkCorridorTolerance = 4f;

        // Stuck detection: sample this often, and treat less than this much ground covered while a
        // move axis is being applied as a strike. Three strikes escalate to the teleport fallback.
        private const float FarmWalkStuckSampleInterval = 0.6f;
        // 0.15 m, not 0.35 m. This sampler only has to catch a HARD freeze (a true wedge reads
        // 0.00–0.05 m); "moving but getting nowhere" is already covered, and covered better, by the
        // remaining-route check below. At 0.35 m it killed a walk logging 0.34 m — a player creeping
        // past an obstacle at ~0.57 m/s, one centimetre from being left alone to finish.
        private const float FarmWalkStuckMinProgress = 0.15f;
        private const int FarmWalkStuckStrikeLimit = 3;

        // "Not closing" detection, which is what actually matters on the final approach.
        //
        // Net displacement alone is not enough: a player pressed against a rock face SLIDES along
        // it, covering well over the stuck threshold every sample while never getting a centimetre
        // nearer the resource. That is the "walks up, then stops too far away and never farms"
        // report — the walker had not frozen, so nothing escalated, and it pushed until the
        // multi-minute deadline. So track the best 3-D distance achieved and give up on walking
        // when it stops improving.
        private const float FarmWalkClosingImprovement = 0.5f;
        private const float FarmWalkNoClosingTimeout = 3f;

        // Snap radius when hooking the player / the node onto the graph. Beyond this the target is
        // somewhere the authored graph does not cover (open water, sea floor) and we teleport.
        private const float FarmWalkGraphSnapRadius = 60f;

        private readonly List<Vector3> farmWalkCorners = new List<Vector3>();
        private readonly List<Vector3> farmWalkScratchCorners = new List<Vector3>();
        private int farmWalkCornerIndex;
        private Vector3 farmWalkTarget;
        private string farmWalkLabel = string.Empty;
        private float farmWalkNextRepathAt;
        private float farmWalkNextStuckSampleAt;
        private Vector3 farmWalkLastSample;
        // Start of the leg currently being walked (previous corner, or where the route was built).
        private Vector3 farmWalkLegStart;
        // Shortest REMAINING ROUTE achieved so far, and when it last meaningfully improved.
        //
        // Deliberately route length, not straight-line distance to the target. A route that goes
        // around a hill, a bay or a building legitimately INCREASES the distance to the node for
        // several seconds, and measuring that killed healthy walks: one run reported "stopped
        // closing at 35,4m" while at corner 2 of 7, i.e. still on the outbound leg of a detour it
        // was walking correctly. Remaining route length falls monotonically on any route that is
        // being followed, so it only stalls when progress genuinely stops.
        private float farmWalkBestDistance;
        private float farmWalkBestAt;
        // Vertical-gap sampler, used to tell "descending fine" from "genuinely blocked".
        private float farmWalkDySampleAt;
        private float farmWalkDySample;
        // Axis ordering for this frame: hold the horizontal while climbing, hold the descent while
        // still far out horizontally.
        private bool farmWalkHoldHorizontalForClimb;
        private bool farmWalkDeferDescent;
        private float farmWalkClimbFirstUntil;
        // Sprint state: how much route is left this frame, and the swim-dash attempt throttle.
        private float farmWalkRouteRemainingCache;
        private float farmWalkNextSprintAttemptAt;
        private IntPtr farmSwimTryStartSprintMethod;
        // Cancel attempts spent on the CURRENT dash. Hard-capped: a cancel that does not take must
        // fail quietly, never retry forever.
        private int farmWalkSprintCancelTries;
        private float farmWalkNextSprintCancelAt;
        // While in the future, steering drives BACKWARDS to trigger the game's large-turn cancel.
        private float farmWalkDashBrakeUntil;
        // Smoothed steering direction (world XZ, normalized) and jump budget for this walk.
        private Vector3 farmWalkSteerDir;
        private bool farmWalkSteerDirValid;
        private float farmWalkLastJumpAt;
        private int farmWalkJumpsUsed;
        // Graph node the current route snapped onto, and the nodes this walk has proven it cannot
        // reach (see the exclusion note on TryFindNearestTrackGraphNode).
        private int farmWalkStartNodeIndex = -1;
        private readonly HashSet<int> farmWalkExcludedNodes = new HashSet<int>();

        // Set when a failed walk should hand back to the node scan instead of teleporting, so the
        // farm moves on to the next nearest node rather than warping to an unreachable one.
        // Deliberate vertical standoff baked into farmWalkTarget (contamination only, 0 otherwise).
        // farmWalkTrueTarget keeps the real node position: the cooldown stamp and the teleport
        // fallback must both address the NODE, never the offset aim point.
        private float farmWalkAimOffsetY;
        private Vector3 farmWalkTrueTarget;
        private bool farmWalkSkipToScan;
        // Has this walk ever cleared a corner? Gates the start-waypoint exclusion.
        private bool farmWalkEverAdvanced;
        // The node the last skip stamped out, kept so the scan can fall back to it rather than
        // relocating the whole area (see ConsumeFarmWalkSkippedNodeFallback).
        private bool farmWalkHasSkippedNode;
        private Vector3 farmWalkSkippedNode;
        private string farmWalkSkippedNodeLabel;
        // Grace period before a skipped node may be reclaimed by teleport. One empty scan is a weak
        // reason to warp: the radar marker list is rebuilt on its own cadence and is briefly empty,
        // and nodes respawn. Waiting a few seconds turns most "teleported for no reason" moments
        // back into an ordinary walk to the next node.
        private float farmWalkReclaimNotBefore;
        private const float FarmWalkReclaimGraceSeconds = 5f;

        // A node already reclaimed once and failed again is a repeat offender: reclaiming it a
        // second time produces the "two teleports back to back" the log shows — the reclaim hop,
        // then an area relocation moments later because the area is empty anyway. Park it instead,
        // so the farm relocates ONCE and stops cycling on it.
        private Vector3 farmWalkLastReclaimedNode;
        private float farmWalkLastReclaimedAt = -999f;
        private bool farmWalkHasLastReclaimed;
        private const float FarmWalkRepeatOffenderWindow = 180f;
        private const float FarmWalkRepeatOffenderParkSeconds = 300f;

        // True while the scan should keep looking rather than relocating or reclaiming.
        private bool ShouldHoldFarmScanForSkippedNode()
        {
            return this.farmWalkHasSkippedNode && Time.unscaledTime < this.farmWalkReclaimNotBefore;
        }
        // Consecutive skips with no successful arrival. Bounded so a pocket of unreachable nodes
        // cannot spin the scan forever — past the cap, one teleport breaks the deadlock.
        private int farmWalkConsecutiveSkips;
        private const int FarmWalkMaxConsecutiveSkips = 3;

        // Dive/surface state: -1 diving, 0 released, +1 surfacing, plus the resolved Mono method.
        private int farmWalkVerticalHeld;
        private int farmWalkPrevVerticalHeld;
        private float farmWalkVerticalAssertedAt;
        // Underwater unstick sequence: phase, where the back-off started, and the phase deadline.
        private int farmWalkUnstickPhase;
        private Vector3 farmWalkUnstickFrom;
        private float farmWalkUnstickPhaseUntil;
        // Final-approach probe state.
        private int farmWalkProbeIndex;
        private int farmWalkProbeStage;
        private float farmWalkProbeBestDistance;
        private bool farmWalkProbeUsed;
        // Bearing toward the node when the probe started; every probe direction is an offset of it.
        private float farmWalkProbeBaseYaw;
        // Is the player in water this frame? Set by DriveFarmWalkDepth. Everything vertical —
        // dive/surface, axis ordering, the ascend unstick — is gated on it; land has no such axis.
        private bool farmWalkIsSwimming;
        private IntPtr farmSwimLocomotionClass;
        private IntPtr farmSwimSetVerticalMethod;
        private int farmWalkStuckStrikes;
        private float farmWalkDeadline;
        private bool farmWalkActive;

        // Arrival setup captured from the caller, so finishing a walk reproduces exactly what the
        // teleport path would have done at the moment it landed.
        private bool farmWalkPendingPriority;
        private string farmWalkDwellLabel = string.Empty;

        // Game speed the farm would use when walking is off. Walking pins 1x instead.
        private const float FarmWalkGameSpeed = 1f;
        private const float FarmDefaultGameSpeed = 5f;

        internal float FarmRunGameSpeed => this.farmWalkToNodeEnabled ? FarmWalkGameSpeed : FarmDefaultGameSpeed;

        // Try to begin walking to `target`. False means "no usable route" and the caller teleports
        // exactly as it always has, so every failure path degrades to today's behaviour.
        // priority   — the caller is a priority-area node, which arms the aura collect wait instead
        //              of the generic node dwell. Recorded so arrival reproduces the caller's setup.
        // dwellLabel — the raw marker label BeginFarmNodeDwell expects (routes "Contaminated" into
        //              the sea-clean sweep), as opposed to the decorated log label.
        private bool TryBeginFarmWalk(Vector3 target, string label, bool priority, string dwellLabel)
        {
            if (!this.farmWalkToNodeEnabled)
            {
                return false;
            }

            this.farmWalkPendingPriority = priority;
            this.farmWalkDwellLabel = dwellLabel;
            // Cleared BEFORE the first route build: unreachability is a property of where the
            // player was standing on the last walk, not a permanent fact about the graph.
            this.farmWalkExcludedNodes.Clear();

            // Contamination gets a deliberate vertical standoff instead of landing on the node.
            this.farmWalkAimOffsetY = 0f;
            if (string.Equals(dwellLabel, "Contaminated", StringComparison.Ordinal))
            {
                this.TryGetContaminatedAnchorClass(target, out bool hostedAnchor);
                this.farmWalkAimOffsetY = hostedAnchor
                    ? FarmWalkContaminationStandoff
                    : -FarmWalkContaminationStandoff;
                target.y += this.farmWalkAimOffsetY;
            }

            if (!this.EnsureTrackPathGraph())
            {
                return false;
            }

            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                this.AutoFarmLog("[FarmWalk] self position unresolved — teleporting instead.");
                return false;
            }

            // Already in server collect range. Start a walk that completes on its first tick rather
            // than returning false — false sends the caller into FarmTeleportTo, which is how the
            // farm ended up teleporting onto a node it was already standing 1.3 m from.
            bool alreadyInRange = Distance3D(selfPos, target) <= FarmWalkCollectDistance;
            if (alreadyInRange)
            {
                this.farmWalkCorners.Clear();
                this.farmWalkCorners.Add(target);
                this.farmWalkCornerIndex = 0;
                this.farmWalkLegStart = selfPos;
            }
            else if (!this.TryBuildFarmWalkRoute(selfPos, target))
            {
                return false;
            }

            this.farmWalkTarget = target;                              // aim point (may be offset)
            this.farmWalkTrueTarget = target;
            this.farmWalkTrueTarget.y -= this.farmWalkAimOffsetY;      // the node itself
            this.farmWalkLabel = label ?? string.Empty;
            this.farmWalkActive = true;
            this.farmWalkStuckStrikes = 0;
            this.farmWalkLastSample = selfPos;
            this.farmWalkNextStuckSampleAt = Time.unscaledTime + FarmWalkStuckSampleInterval;
            this.farmWalkNextRepathAt = Time.unscaledTime + FarmWalkRepathInterval;
            this.farmWalkBestDistance = this.ComputeFarmWalkRouteRemaining(selfPos);
            this.farmWalkBestAt = Time.unscaledTime;
            this.farmWalkDySample = Mathf.Abs(target.y - selfPos.y);
            this.farmWalkDySampleAt = Time.unscaledTime;
            this.farmWalkClimbFirstUntil = Time.unscaledTime + FarmWalkClimbFirstTimeout;
            this.farmWalkRouteRemainingCache = this.ComputeFarmWalkRouteRemaining(selfPos);
            this.farmWalkSprintCancelTries = 0;
            this.farmWalkDashBrakeUntil = 0f;
            this.farmWalkSteerDirValid = false; // first frame steers straight, no smoothing lag
            this.farmWalkJumpsUsed = 0;
            this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
            this.farmWalkProbeUsed = false; // one probe sweep per walk
            this.farmWalkEverAdvanced = this.farmWalkCornerIndex > 0;
            this.farmWalkPrevVerticalHeld = this.farmWalkVerticalHeld;

            // Generous deadline: straight-line metres at the configured speed, tripled for detours,
            // plus a fixed allowance. Walking is meant to be slow; this only catches a wedge.
            float straight = HorizontalDistance(selfPos, target);
            this.farmWalkDeadline = Time.unscaledTime + Mathf.Clamp(straight * 3f / Mathf.Max(0.2f, this.farmWalkSpeed) + 15f, 20f, 300f);

            // Always logged, not behind the AutoFarmLog flag: one line per node hop (the same
            // cadence as [ForagingTp]) is what makes a wedged walk diagnosable at all — the first
            // build's freeze was invisible precisely because this line was flag-gated off.
            ModLogger.Msg("[FarmWalk] " + label + ": walking " + straight.ToString("F1") + "m via "
                + this.farmWalkCorners.Count + " corners, starting at corner " + this.farmWalkCornerIndex
                + ", target=" + FormatNavMeshVector(target)
                + (alreadyInRange ? " (already in range)" : string.Empty) + ".");
            return true;
        }

        // Snap both ends onto the graph, A*, then append the true target as the final corner (the
        // game's own GetPath2 does exactly this, so the last leg leaves the graph and ends on the
        // resource). A start node that is already behind us is dropped so the first step is forward.
        // Builds into a SCRATCH list and only commits on success. A mid-walk re-path that fails
        // must leave the route we are already following untouched — TryComputeTrackGraphPath clears
        // its output list up front, so writing straight into farmWalkCorners would empty the route
        // and the very next corner read would be out of range.
        private bool TryBuildFarmWalkRoute(Vector3 from, Vector3 to)
        {
            if (!this.TryFindNearestTrackGraphNode(from, FarmWalkGraphSnapRadius, out int startIndex, this.farmWalkExcludedNodes)
                || !this.TryFindNearestTrackGraphNode(to, FarmWalkGraphSnapRadius, out int endIndex))
            {
                this.AutoFarmLog("[FarmWalk] no graph node within " + FarmWalkGraphSnapRadius.ToString("F0")
                    + "m of an endpoint — teleporting instead.");
                return false;
            }

            if (!this.TryComputeTrackGraphPath(startIndex, endIndex, this.farmWalkScratchCorners))
            {
                this.AutoFarmLog("[FarmWalk] A* found no route between the snapped nodes — teleporting instead.");
                return false;
            }

            this.farmWalkScratchCorners.Add(to);

            this.farmWalkCorners.Clear();
            this.farmWalkCorners.AddRange(this.farmWalkScratchCorners);

            // Start at the first corner that is genuinely still AHEAD, not at 0.
            //
            // A* always starts a route at the graph node nearest the player, and after walking a
            // few metres past a waypoint that node is BEHIND them. Two failures came from getting
            // this wrong:
            //   * standing on the node  -> corner 0 is underfoot, steering delta ~0, frozen solid;
            //   * a few metres past it  -> every 1.5 s re-path aims back at it, the player turns
            //     around, reaches it, advances, turns around again. Visible as running back and
            //     forth, and stuck-detection reads 0.00 m because it measures NET DISPLACEMENT,
            //     which an oscillation leaves at zero however fast the player is actually moving.
            //
            // So a corner is skipped when it is either already reached, or already passed — and
            // "passed" is the progress test "am I closer to what comes next than this corner is".
            this.farmWalkCornerIndex = 0;
            while (this.farmWalkCornerIndex < this.farmWalkCorners.Count - 1)
            {
                Vector3 candidate = this.farmWalkCorners[this.farmWalkCornerIndex];
                Vector3 next = this.farmWalkCorners[this.farmWalkCornerIndex + 1];

                bool reached = HorizontalDistance(from, candidate) <= FarmWalkCornerReachDistance;
                bool passed = HorizontalDistance(from, next) < HorizontalDistance(candidate, next);
                if (!reached && !passed)
                {
                    break;
                }

                this.farmWalkCornerIndex++;
            }

            this.farmWalkLegStart = from;
            this.farmWalkStartNodeIndex = startIndex;
            return this.farmWalkCorners.Count > 0;
        }

        // Perpendicular distance from the player to the leg being walked, in XZ. The corridor test
        // MUST measure this and not the distance to the next corner: authored waypoints sit tens of
        // metres apart, so "far from the next corner" is true for most of every leg and would
        // re-path on every frame.
        private static float DistanceToWalkLeg(Vector3 point, Vector3 legStart, Vector3 legEnd)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(legStart.x, legStart.z);
            Vector2 b = new Vector2(legEnd.x, legEnd.z);

            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }

        // Per-frame tick for AutoFarmState.WalkingToNode. Returns true once the walk is finished
        // (arrived, or escalated to a teleport) — the caller then moves on to Collecting.
        private bool RunFarmWalkTick()
        {
            if (!this.farmWalkActive)
            {
                return true;
            }

            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                this.FinishFarmWalk("self position lost", teleport: true);
                return true;
            }

            float now = Time.unscaledTime;
            float remaining = HorizontalDistance(selfPos, this.farmWalkTarget);

            float distance3D = Distance3D(selfPos, this.farmWalkTarget);

            // Arrived once we are horizontally on top of the node AND close enough overall for the
            // server to accept the collect. Splitting the two is what makes a slightly-raised
            // resource reachable: the walker closes the part it can (horizontal) and the height
            // difference only has to fit inside the anti-cheat radius.
            if (remaining <= FarmWalkCollectDistance)
            {
                // With a deliberate standoff the aim point IS the goal, so the server's 2 m collect
                // rule does not apply — the sweep reaches from there, and measuring against the
                // true node would reject an arrival that is exactly where we wanted to be.
                if (this.farmWalkAimOffsetY != 0f || distance3D <= FarmWalkServerCollectDistance)
                {
                    this.FinishFarmWalk("arrived " + remaining.ToString("F2") + "m horizontally ("
                        + distance3D.ToString("F2") + "m 3-D) from the aim point"
                        + (this.farmWalkAimOffsetY != 0f
                            ? " [standoff " + this.farmWalkAimOffsetY.ToString("+0.#;-0.#") + "m]"
                            : string.Empty), teleport: false);
                    return true;
                }

                // Horizontally there but vertically short. In water that is not a dead end — it is
                // simply the other axis, so keep going and let the dive/surface input close it.
                // On land it is a ledge or an overhang, which no amount of walking fixes.
                if (this.farmWalkVerticalHeld == 0)
                {
                    this.FinishFarmWalk("under the node but " + distance3D.ToString("F1") + "m away vertically (dy="
                        + (this.farmWalkTarget.y - selfPos.y).ToString("F1") + "m)", teleport: true);
                    return true;
                }
            }

            // Are we still making progress ALONG THE ROUTE? A slide along an obstacle keeps the
            // displacement sampler happy while achieving nothing, so this is the check that ends a
            // walk which can never finish.
            // The improvement that counts has to scale down on the final approach: inside 2 m there
            // is less than 2 m of progress left to make, so demanding a fixed 0.5 m would time out
            // a creep that is actually succeeding.
            // Advance the unstick sequence before steering and depth read it this frame.
            this.UpdateFarmWalkUnstickPhase(selfPos, now);

            // Decide the axis order for this frame (see FarmWalkDescendHoldDistance). Suspended
            // entirely during an unstick, which owns both axes while it runs.
            float dyNow = this.farmWalkTarget.y - selfPos.y;
            bool unsticking = this.farmWalkUnstickPhase != FarmWalkUnstickIdle;
            // BOTH are swimming-only. On land there is no vertical axis to order: the player
            // cannot float up or sink down, height is whatever the ground gives. Ungated, the
            // climb-first hold cleared the move axis for any node more than 0.35 m above — a
            // slope, a mushroom on a rise — and the player just stood there jumping until the
            // walk timed out. Land walking must never hold the horizontal.
            this.farmWalkHoldHorizontalForClimb = this.farmWalkIsSwimming
                && !unsticking
                && dyNow > FarmWalkDepthEngageTolerance
                && now < this.farmWalkClimbFirstUntil;
            this.farmWalkDeferDescent = this.farmWalkIsSwimming
                && !unsticking
                && dyNow < -FarmWalkDepthEngageTolerance
                && remaining > FarmWalkDescendHoldDistance;

            // Depth is driven before the progress check so that vertical movement counts as
            // progress: hovering over a sea grape leaves the horizontal route remaining at ~0
            // forever, and without the dy term the not-closing timer would fire mid-dive.
            this.DriveFarmWalkDepth(selfPos);

            float routeRemaining = this.ComputeFarmWalkRouteRemaining(selfPos);

            // Sprint decides on the HORIZONTAL route only — it covers ground, not depth. Judging it
            // on the combined metric would call a deep dive directly over the node a "long leg".
            this.farmWalkRouteRemainingCache = routeRemaining;
            this.TryFarmWalkSwimSprint(routeRemaining, now);

            if (this.farmWalkVerticalHeld != 0)
            {
                routeRemaining += Mathf.Abs(this.farmWalkTarget.y - selfPos.y);
            }

            // Re-baseline the progress metric whenever the vertical state changes, and hold it
            // re-baselined for the whole of an obstacle ascent.
            //
            // This is what made the player circle. The metric gains a |dy| term the instant a dive
            // engages, so against a baseline captured WITHOUT that term it leaps by the whole depth
            // (9 m on the first sample) and can never beat it. "Not closing" then fired 1.5 s into
            // every dive, triggering an ascent that increased dy, which made the next dive start
            // even further behind: 9,2m -> 14,6m -> 13,4m -> 12,1m, ascending and re-diving the
            // whole way down. Comparing a metric against a baseline measured a different way is the
            // bug; re-baselining on every transition is the fix.
            // The whole unstick sequence counts: backing off and ascending both move away from the
            // node on purpose, so neither may be read as lost progress.
            bool clearingObstacle = this.farmWalkUnstickPhase != FarmWalkUnstickIdle;
            if (clearingObstacle || this.farmWalkVerticalHeld != this.farmWalkPrevVerticalHeld)
            {
                this.farmWalkPrevVerticalHeld = this.farmWalkVerticalHeld;
                this.farmWalkBestDistance = routeRemaining;
                this.farmWalkBestAt = now;
            }

            float closingImprovement = distance3D >= FarmWalkSlowApproachDistance
                ? FarmWalkClosingImprovement
                : 0.1f;
            if (routeRemaining < this.farmWalkBestDistance - closingImprovement)
            {
                this.farmWalkBestDistance = routeRemaining;
                this.farmWalkBestAt = now;
            }
            else if (now - this.farmWalkBestAt >= FarmWalkNoClosingTimeout * 0.5f
                && now - this.farmWalkBestAt < FarmWalkNoClosingTimeout)
            {
                // Half-way through the not-closing window, try hopping. This is the case the
                // displacement sampler never sees: sliding along a fence covers plenty of ground
                // (so no stuck strike, so no jump from there) while closing nothing.
                //
                // Except while the DEPTH is still closing. The unstick is for horizontal
                // obstacles, and firing it mid-descent interrupted dives that were working
                // perfectly: 21,6m -> 16,6m -> 13,8m -> 11,1m, an unstick between each, the player
                // backing off and re-ascending 5 m every few seconds all the way down.
                if (this.IsFarmWalkDepthClosing(selfPos, now))
                {
                    this.farmWalkBestAt = now; // vertical progress IS progress
                }
                else
                {
                    this.TryFarmWalkJump("not closing");
                }
            }
            else if (now - this.farmWalkBestAt >= FarmWalkNoClosingTimeout)
            {
                this.FinishFarmWalk("stopped closing, " + routeRemaining.ToString("F1") + "m of route left (best "
                    + this.farmWalkBestDistance.ToString("F1") + "m, "
                    + distance3D.ToString("F1") + "m direct, dy="
                    + (this.farmWalkTarget.y - selfPos.y).ToString("F1") + "m, "
                    + this.farmWalkJumpsUsed + " jumps)", teleport: true);
                return true;
            }

            if (now >= this.farmWalkDeadline)
            {
                this.FinishFarmWalk("timed out " + remaining.ToString("F1") + "m short", teleport: true);
                return true;
            }

            // Advance past every corner already reached OR passed (a fast frame can clear more than
            // one, and cutting a corner wide can pass one without ever entering its reach radius —
            // without the passed test the walker would turn back for it). Same pair of conditions
            // the route builder uses. Each corner cleared becomes the start of the next leg, so the
            // corridor test below measures against the segment actually being walked.
            while (this.farmWalkCornerIndex < this.farmWalkCorners.Count - 1)
            {
                Vector3 candidate = this.farmWalkCorners[this.farmWalkCornerIndex];
                Vector3 next = this.farmWalkCorners[this.farmWalkCornerIndex + 1];

                bool reached = HorizontalDistance(selfPos, candidate) <= FarmWalkCornerReachDistance;
                bool passed = HorizontalDistance(selfPos, next) < HorizontalDistance(candidate, next);
                if (!reached && !passed)
                {
                    break;
                }

                this.farmWalkLegStart = candidate;
                this.farmWalkCornerIndex++;
                this.farmWalkEverAdvanced = true;
            }

            if (this.farmWalkCornerIndex >= this.farmWalkCorners.Count)
            {
                this.FinishFarmWalk("ran out of corners " + remaining.ToString("F1") + "m short", teleport: true);
                return true;
            }

            Vector3 corner = this.farmWalkCorners[this.farmWalkCornerIndex];

            // Re-path on cadence, or immediately when we have drifted off the corridor (pushed by
            // another player, shoved off a ledge, or the route was stale).
            if (now >= this.farmWalkNextRepathAt
                || DistanceToWalkLeg(selfPos, this.farmWalkLegStart, corner) > FarmWalkCorridorTolerance)
            {
                this.farmWalkNextRepathAt = now + FarmWalkRepathInterval;
                if (this.TryBuildFarmWalkRoute(selfPos, this.farmWalkTarget) && this.farmWalkCorners.Count > 0)
                {
                    corner = this.farmWalkCorners[this.farmWalkCornerIndex];

                    // A different route has a different length, so the previous best is not
                    // comparable — re-baseline rather than reading the change as lost progress.
                    this.farmWalkBestDistance = this.ComputeFarmWalkRouteRemaining(selfPos);
                    this.farmWalkBestAt = now;
                }
            }

            // Hold the horizontal while the movement is purely vertical.
            //
            // Climbing: rise in place, then swim across.
            // Descending onto the target: once we are over it, the horizontal delta is a fraction
            // of a metre, so its DIRECTION is mostly noise — steering on it made the player circle
            // the spot all the way down. Sink straight instead.
            // 1 m, NOT the 4 m descend-hold distance. Reusing that constant here stopped the swim
            // a full 4 m short of the node and only resumed once the depth had closed — visibly
            // "stopping too soon", then shuffling the last stretch. The anti-circling problem it
            // solves only exists when the horizontal delta is small enough for its DIRECTION to be
            // noise, which is a metre, not four.
            // Requires a REAL vertical gap as well as being close horizontally. Without the dy test
            // this froze the approach at exactly 1.0 m: the depth hysteresis keeps a dive held down
            // to 0.12 m, so a 0.2 m drop was enough to clear the horizontal axis and the player sat
            // one metre short trying to sink, until the final-approach stall gave up on it.
            bool sinkingOnTarget = this.farmWalkVerticalHeld < 0
                && remaining <= FarmWalkVerticalOnlyRadius
                && Mathf.Abs(dyNow) > FarmWalkVerticalOnlyMinDrop;
            if (this.farmWalkHoldHorizontalForClimb || sinkingOnTarget)
            {
                this.TryClearGameMoveAxis();
                this.autoFarmStatus = sinkingOnTarget
                    ? "Descending " + Mathf.Abs(dyNow).ToString("F0") + "m onto the node..."
                    : "Rising " + dyNow.ToString("F0") + "m before swimming...";
            }
            else
            {
                this.SteerFarmWalkToward(selfPos, corner);
            }
            this.SampleFarmWalkProgress(selfPos, now);
            this.autoFarmStatus = "Walking to node (" + remaining.ToString("F0") + "m)...";
            return false;
        }

        // Convert a world direction into the RAW joystick axis the game expects.
        //
        // The chain applies the camera transform downstream for us
        // (CameraComponent.ToCameraSpaceJoystick rotates the raw axis BY the camera yaw), so to end
        // up moving along world direction d we must hand it d rotated by MINUS the yaw.
        private void SteerFarmWalkToward(Vector3 selfPos, Vector3 corner)
        {
            Vector3 delta = corner - selfPos;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            delta.Normalize();

            // Backing off an obstacle: drive the opposite way. Reversed BEFORE smoothing, so the
            // turn sweeps rather than snapping, exactly like any other direction change.
            if (this.farmWalkUnstickPhase == FarmWalkUnstickBackingOff)
            {
                delta = -delta;
            }
            else if (this.farmWalkUnstickPhase == FarmWalkUnstickProbing)
            {
                // Horizontal leg: push along the probe direction. Vertical leg: stand still and let
                // the depth input do the work, so the two are actually separable.
                if (this.farmWalkProbeStage != FarmWalkProbeStageHorizontal)
                {
                    this.TryClearGameMoveAxis();
                    return;
                }

                delta = GetFarmWalkProbeDirection(this.farmWalkProbeIndex);
            }

            // Dash brake: reverse and BYPASS the smoothing. The whole point is to present the
            // locomotion with a large angle between its current move direction and the fed one —
            // easing round at 360 deg/s would never show it more than a few degrees per frame and
            // the large-turn cancel would not trigger.
            if (Time.unscaledTime < this.farmWalkDashBrakeUntil)
            {
                this.farmWalkSteerDir = -delta;
                this.farmWalkSteerDirValid = true;
                this.ApplyFarmWalkMoveAxis(-delta, FarmWalkSpeedMax);
                return;
            }

            // Ease the steering direction instead of snapping to it. The character faces whatever
            // direction it is driven in, so a hard swing at a corner or after a re-path reads as
            // the avatar pivoting on the spot. Turning at a bounded rate looks like a person.
            if (!this.farmWalkSteerDirValid)
            {
                this.farmWalkSteerDir = delta;
                this.farmWalkSteerDirValid = true;
            }
            else
            {
                float maxTurn = FarmWalkTurnRateDegPerSecond * Mathf.Deg2Rad * Time.unscaledDeltaTime;
                this.farmWalkSteerDir = Vector3.RotateTowards(this.farmWalkSteerDir, delta, maxTurn, 0f);
                if (this.farmWalkSteerDir.sqrMagnitude < 0.0001f)
                {
                    this.farmWalkSteerDir = delta;
                }
                else
                {
                    this.farmWalkSteerDir.Normalize();
                }
            }

            this.ApplyFarmWalkMoveAxis(this.farmWalkSteerDir, this.ResolveFarmWalkAxisMagnitude(selfPos));
        }

        // World XZ direction -> raw joystick axis. The chain applies the camera transform for us
        // (ToCameraSpaceJoystick rotates BY the camera yaw), so a world direction has to be handed
        // over rotated by MINUS the yaw.
        private void ApplyFarmWalkMoveAxis(Vector3 worldDir, float magnitude)
        {
            Camera cam = Camera.main;
            float yaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * worldDir;

            Vector2 axis = new Vector2(local.x, local.z);
            if (axis.sqrMagnitude > 0.0001f)
            {
                this.TrySetGameMoveAxis(axis.normalized * magnitude);
            }
        }

        // Full configured speed for the journey, easing to a creep over the last couple of metres.
        // Without this the 0.25 m arrival is unreachable: the locomotion carries the player past the
        // node and the walker turns it around, circling forever inside the not-closing window.
        // Distance to the TARGET, not to the current corner — only the final approach should slow.
        private float ResolveFarmWalkAxisMagnitude(Vector3 selfPos)
        {
            float speed = Mathf.Clamp(this.farmWalkSpeed, FarmWalkSpeedMin, FarmWalkSpeedMax);
            float distance = Distance3D(selfPos, this.farmWalkTarget);
            if (distance >= FarmWalkSlowApproachDistance)
            {
                // Long leg: hold the stick fully. The slider tops out at 0.95 to stay off the
                // sprint/stamina threshold for ordinary travel, but a full 1.0 still only reaches
                // the game's own 4.0 m/s run cap — so this is "run", not a speed cheat.
                if (this.farmWalkRouteRemainingCache >= FarmWalkSprintMinDistance)
                {
                    return 1f;
                }

                return speed;
            }

            float slow = Mathf.Min(FarmWalkSlowApproachSpeed, speed);
            float t = Mathf.Clamp01((distance - FarmWalkCollectDistance)
                / Mathf.Max(0.01f, FarmWalkSlowApproachDistance - FarmWalkCollectDistance));
            return Mathf.Lerp(slow, speed, t);
        }

        // Stuck detection is only a safety net for what the graph cannot know about: another player
        // in the doorway, a mesh/collision mismatch, a prop spawned since the path was authored.
        private void SampleFarmWalkProgress(Vector3 selfPos, float now)
        {
            if (now < this.farmWalkNextStuckSampleAt)
            {
                return;
            }

            this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;

            // 3-D displacement, not horizontal. A dive or an obstacle ascent is pure vertical
            // motion: measured horizontally it reads as 0.00 m and the walker declares itself stuck
            // while it is in fact descending 12 m to a sea grape.
            float progress = Distance3D(selfPos, this.farmWalkLastSample);
            this.farmWalkLastSample = selfPos;

            if (progress >= FarmWalkStuckMinProgress)
            {
                this.farmWalkStuckStrikes = 0;
                return;
            }

            this.farmWalkStuckStrikes++;

            // Close to the resource and not closing: the remaining metres are vertical or otherwise
            // unwalkable. Re-pathing cannot help — the graph has no node that close — so hop the
            // last stretch rather than waiting out three strikes to do exactly this.
            //
            // But try a jump FIRST. A kerb, root or low fence a metre from a mushroom is precisely
            // what a hop clears, and escalating on strike 1 meant the final approach never jumped
            // at all: one run logged "final approach not walkable (1,0m, dy=0,1m)" with zero jump
            // lines, having given up 1 m short without attempting the one thing that might work.
            if (Distance3D(selfPos, this.farmWalkTarget) <= FarmWalkFinalApproachDistance)
            {
                if (this.farmWalkStuckStrikes < 2 && this.farmWalkJumpsUsed < FarmWalkMaxJumpsPerWalk)
                {
                    this.TryFarmWalkJump("final approach");
                    return;
                }

                // Diagnostics on the vertical state. A stall here is either "we never asked to
                // dive", "we asked and the game refused", or "we are descending into something
                // solid" — and those need completely different fixes, so record which.
                // Before giving up on a node we are almost on top of, sweep for a way in.
                float stallDistance = Distance3D(selfPos, this.farmWalkTarget);
                if (!this.farmWalkProbeUsed)
                {
                    this.BeginFarmWalkProbe(selfPos, now, stallDistance);
                    return;
                }

                bool swimResolved = this.TryGetFarmWalkSwimLocomotion(out IntPtr stallSwim);
                string vertical = " held=" + this.farmWalkVerticalHeld
                    + " defer=" + this.farmWalkDeferDescent
                    + " climbHold=" + this.farmWalkHoldHorizontalForClimb
                    + " swim=" + swimResolved;
                if (swimResolved && this.TryGetMonoSingleMember(stallSwim, "_swimVerticalInput", out float swimVerticalInput))
                {
                    vertical += " gameVerticalInput=" + swimVerticalInput.ToString("F2");
                }

                this.FinishFarmWalk("final approach not walkable ("
                    + Distance3D(selfPos, this.farmWalkTarget).ToString("F1") + "m, dy="
                    + (this.farmWalkTarget.y - selfPos.y).ToString("F1") + "m)" + vertical, teleport: true);
                return;
            }

            if (this.farmWalkStuckStrikes < FarmWalkStuckStrikeLimit)
            {
                // Try to hop whatever is in the way (fence, kerb, root) and force a fresh route
                // before giving up on walking. Both are cheap and either can rescue the walk.
                this.TryFarmWalkJump("stuck");

                // Still on the FIRST corner means we cannot even reach the waypoint the route
                // snapped onto — it is behind whatever is blocking us. Two of the last run's four
                // failures were exactly this (stuck 2.1 m and 6.3 m from corner 1). Drop that node
                // and let the rebuild snap somewhere reachable instead of retrying the same route.
                // Only when this walk has NEVER cleared a corner. "cornerIndex == 0" alone is not
                // that test: a re-path rebuilds the route and resets the index, so mid-route
                // re-paths kept excluding perfectly good waypoints — one run excluded two while at
                // corner 2 of 3 and pushed the next corner from 33 m out to 41 m.
                if (!this.farmWalkEverAdvanced && this.farmWalkStartNodeIndex >= 0
                    && this.farmWalkExcludedNodes.Add(this.farmWalkStartNodeIndex))
                {
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": start waypoint unreachable, excluding it and re-routing.");
                }

                this.farmWalkNextRepathAt = 0f;
                return;
            }

            if (this.farmWalkStuckStrikes >= FarmWalkStuckStrikeLimit)
            {
                // Net displacement, deliberately: an oscillation covers ground while going nowhere,
                // and that reads as 0.00 here — which is exactly how the back-and-forth bug showed
                // itself. The corner/target distances separate "blocked" from "circling".
                string detail = "stuck (" + progress.ToString("F2") + "m in "
                    + FarmWalkStuckSampleInterval.ToString("F1") + "s, " + this.farmWalkJumpsUsed + " jumps)";
                if (this.farmWalkCornerIndex < this.farmWalkCorners.Count)
                {
                    detail += " corner=" + HorizontalDistance(selfPos, this.farmWalkCorners[this.farmWalkCornerIndex]).ToString("F1")
                        + "m target=" + HorizontalDistance(selfPos, this.farmWalkTarget).ToString("F1") + "m";
                }

                this.FinishFarmWalk(detail, teleport: true);
            }
        }

        // Hop an obstacle. Reuses BunnyHopFeature's Mono jump (OnJumpButton / SetJumpInput pulse on
        // the live player state) rather than a second implementation — that path already handles
        // the grounded gate and the pulse shape. Budgeted so an impassable obstacle escalates to
        // the teleport instead of the player pogoing against it indefinitely.
        private void TryFarmWalkJump(string why)
        {
            if (this.farmWalkJumpsUsed >= FarmWalkMaxJumpsPerWalk
                || Time.unscaledTime - this.farmWalkLastJumpAt < FarmWalkJumpInterval)
            {
                return;
            }

            this.farmWalkLastJumpAt = Time.unscaledTime;
            this.farmWalkJumpsUsed++;

            // In water, rise over the obstacle instead of jumping at it. Same budget, because both
            // are "the walker is blocked and is trying something"; a reef that survives four
            // attempts should still escalate rather than being climbed forever.
            if (this.TryGetFarmWalkSwimLocomotion(out _))
            {
                this.farmWalkUnstickPhase = FarmWalkUnstickBackingOff;
                this.farmWalkUnstickPhaseUntil = Time.unscaledTime + FarmWalkBackOffTimeout;
                this.farmWalkUnstickFrom = Vector3.zero; // seeded on the first tick of the phase
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": unstick " + this.farmWalkJumpsUsed + "/"
                    + FarmWalkMaxJumpsPerWalk + " (" + why + ") — backing off "
                    + FarmWalkBackOffDistance.ToString("0.#") + "m, then ascending.");
                return;
            }

            bool jumped;
            try
            {
                jumped = this.TryBunnyHopJumpViaMono();
            }
            catch (Exception ex)
            {
                jumped = false;
                ModLogger.Msg("[FarmWalk] jump threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            // Not flag-gated: at most 4 per walk, and without it a log cannot distinguish "jumped
            // and it did not help" from "never jumped at all".
            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": jump " + this.farmWalkJumpsUsed + "/"
                + FarmWalkMaxJumpsPerWalk + " (" + why + ") -> " + (jumped ? "ok" : "unavailable"));
        }

        // Always releases the injected axis — leaving it applied would keep driving the player
        // through the whole collect dwell.
        private void FinishFarmWalk(string reason, bool teleport)
        {
            // Corner progress goes in the message: "gave up on corner 1/9" and "gave up on corner
            // 8/9" are completely different failures (bad route vs one blocked doorway).
            string progress = " [corner " + (this.farmWalkCornerIndex + 1) + "/" + this.farmWalkCorners.Count + "]";

            this.farmWalkActive = false;
            this.farmWalkCorners.Clear();
            this.farmWalkCornerIndex = 0;
            this.ReleaseFarmWalkDepth();
            this.TryClearGameMoveAxis();

            if (teleport)
            {
                // Prefer moving on to a DIFFERENT node over warping to this one. A node the walker
                // cannot reach is usually behind a reef or a wall, and the whole point of the mode
                // is to travel legitimately — so stamp it as recently-visited (which is what makes
                // FindClosestAvailableNode skip it) and hand back to the scan.
                // Failed again on a node we already teleported onto once — park it for a while and
                // offer no reclaim, so the farm makes ONE move (relocate) instead of hopping onto
                // it and relocating a moment later.
                float skipNow = Time.unscaledTime;
                if (this.farmWalkHasLastReclaimed
                    && skipNow - this.farmWalkLastReclaimedAt < FarmWalkRepeatOffenderWindow
                    && HorizontalDistance(this.farmWalkLastReclaimedNode, this.farmWalkTrueTarget) < 2f)
                {
                    this.farmWalkHasLastReclaimed = false;
                    this.farmWalkHasSkippedNode = false;
                    this.recentlyVisitedNodes[this.farmWalkTrueTarget] = skipNow + FarmWalkRepeatOffenderParkSeconds;
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — unreachable again after a reclaim, parking it for "
                        + (FarmWalkRepeatOffenderParkSeconds / 60f).ToString("0.#") + " min.");
                }
                else if (this.farmWalkConsecutiveSkips < FarmWalkMaxConsecutiveSkips)
                {
                    this.farmWalkConsecutiveSkips++;
                    this.recentlyVisitedNodes[this.farmWalkTrueTarget] = Time.unscaledTime + FarmVisitedRetryStampSeconds;
                    this.farmWalkSkipToScan = true;
                    this.farmWalkHasSkippedNode = true;
                    this.farmWalkSkippedNode = this.farmWalkTrueTarget;
                    this.farmWalkSkippedNodeLabel = this.farmWalkDwellLabel;
                    this.farmWalkReclaimNotBefore = Time.unscaledTime + FarmWalkReclaimGraceSeconds;
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — skipping to the next nearest node (" + this.farmWalkConsecutiveSkips
                        + "/" + FarmWalkMaxConsecutiveSkips + ").");
                }
                else
                {
                    // Everything nearby is unreachable — take the one teleport that breaks the
                    // deadlock rather than rescanning the same pocket indefinitely.
                    this.farmWalkConsecutiveSkips = 0;
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — " + FarmWalkMaxConsecutiveSkips + " nodes skipped in a row, teleporting to break the deadlock.");
                    this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(this.farmWalkTrueTarget, this.farmWalkLabel),
                        "node:walk-fallback", this.farmWalkTrueTarget);
                }
            }
            else
            {
                // Also always logged. A silent success path is what made "it stops too far" so hard
                // to pin down: the log showed a walk starting and then nothing at all, so there was
                // no way to tell an arrival from a walk still grinding away against a rock.
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress + ".");
            }
        }

        // Called when the farm stops for any reason, so a half-finished walk never leaves the
        // player driving into a wall.
        private void AbortFarmWalk()
        {
            if (!this.farmWalkActive)
            {
                return;
            }

            this.farmWalkActive = false;
            this.farmWalkCorners.Clear();
            this.farmWalkCornerIndex = 0;
            this.ReleaseFarmWalkDepth();
            this.TryClearGameMoveAxis();
        }

        // Arrival setup, shared by "walked there" and "gave up and teleported" — both end standing
        // at the node, so both hand over to Collecting the same way the teleport path always did.
        private void EnterFarmCollectingAfterWalk()
        {
            // A skipped node was never reached, so there is nothing to collect — go straight back
            // to the scan, which will pick the next nearest node with this one stamped out.
            if (this.farmWalkSkipToScan)
            {
                this.farmWalkSkipToScan = false;
                this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
                this.autoFarmTimer = 0f;
                this.autoFarmStatus = "Node unreachable, finding another...";
                return;
            }

            // Reached one: the run is healthy again.
            this.farmWalkConsecutiveSkips = 0;

            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
            this.autoFarmTimer = 0f;
            this.autoCollectClickedSinceArrival = false;
            this.cameraRotationAttempts = 0;

            if (this.farmWalkPendingPriority)
            {
                this.ResetContaminationDwellState(); // priority nodes are plants, never contamination
                this.ArmAuraCollectWait(true);
            }
            else
            {
                this.BeginFarmNodeDwell(this.farmWalkDwellLabel);
            }
        }

        // Distance still to walk: to the current corner, then corner to corner to the end. Falls
        // monotonically on a route that is being followed, including around a detour, which is
        // exactly what the straight-line-to-target measure got wrong.
        private float ComputeFarmWalkRouteRemaining(Vector3 selfPos)
        {
            if (this.farmWalkCorners.Count == 0 || this.farmWalkCornerIndex >= this.farmWalkCorners.Count)
            {
                return HorizontalDistance(selfPos, this.farmWalkTarget);
            }

            float total = HorizontalDistance(selfPos, this.farmWalkCorners[this.farmWalkCornerIndex]);
            for (int i = this.farmWalkCornerIndex; i < this.farmWalkCorners.Count - 1; i++)
            {
                total += HorizontalDistance(this.farmWalkCorners[i], this.farmWalkCorners[i + 1]);
            }

            return total;
        }

        // The live SwimLocomotion, or false when the player is not swimming. Resolved per call —
        // locomotion objects are swapped as the player enters and leaves water, so caching the
        // object across frames would be exactly the detached-pointer trap the project keeps hitting
        // (only the CLASS and METHOD pointers are stable enough to cache).
        private bool TryGetFarmWalkSwimLocomotion(out IntPtr swimObj)
        {
            swimObj = IntPtr.Zero;
            if (auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null
                || !this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero
                || !this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) || moveObj == IntPtr.Zero
                || !this.TryGetMonoObjectMember(moveObj, "Locomotion", out IntPtr locomotion) || locomotion == IntPtr.Zero)
            {
                return false;
            }

            IntPtr locomotionClass = auraMonoObjectGetClass(locomotion);
            if (locomotionClass == IntPtr.Zero)
            {
                return false;
            }

            // Only SwimLocomotion has the vertical input; on land the locomotion is a different
            // class and the method genuinely does not exist.
            string name = this.GetAuraMonoClassDisplayName(locomotionClass);
            if (string.IsNullOrEmpty(name) || name.IndexOf("SwimLocomotion", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (locomotionClass != this.farmSwimLocomotionClass)
            {
                this.farmSwimLocomotionClass = locomotionClass;
                this.farmSwimSetVerticalMethod = this.FindAuraMonoMethodOnHierarchy(locomotionClass, "SetSwimVerticalInput", 2);
            }

            swimObj = locomotion;
            return this.farmSwimSetVerticalMethod != IntPtr.Zero;
        }

        private unsafe bool TryInvokeFarmWalkSwimVertical(IntPtr swimObj, bool ascending, bool pressed)
        {
            if (swimObj == IntPtr.Zero || this.farmSwimSetVerticalMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            bool ascendingArg = ascending;
            bool pressedArg = pressed;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&ascendingArg);
            args[1] = (IntPtr)(&pressedArg);
            auraMonoRuntimeInvoke(this.farmSwimSetVerticalMethod, swimObj, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // Last resort for the node scan: when nothing else is available, take back the node the
        // walker just skipped rather than relocating.
        //
        // A skip stamps its node into recentlyVisitedNodes, so if it was the only candidate the
        // very next scan finds nothing and the farm falls through to MovingToLocation — which
        // teleports to a FARM-LOCATION waypoint, potentially across the map. One unreachable sea
        // grape sent the run 121 m to "Sea Area 1". Hopping the last few metres onto the awkward
        // node is enormously less disruptive than abandoning the area.
        private bool TryConsumeFarmWalkSkippedNode(out Vector3 node, out string label)
        {
            node = this.farmWalkSkippedNode;
            label = this.farmWalkSkippedNodeLabel;
            if (!this.farmWalkHasSkippedNode)
            {
                return false;
            }

            this.farmWalkHasSkippedNode = false;
            this.farmWalkConsecutiveSkips = 0;
            this.recentlyVisitedNodes.Remove(node);

            // Remember it, so a second failure on the same node parks it instead of reclaiming again.
            this.farmWalkLastReclaimedNode = node;
            this.farmWalkLastReclaimedAt = Time.unscaledTime;
            this.farmWalkHasLastReclaimed = true;
            return true;
        }

        // Swim dash on a long leg. Every precondition is the game's own — TryStartSprint checks the
        // cooldown, the stamina block (IsSprintBlockedByStamina) and CanStartSprintInternal, and
        // simply returns false when any of them says no. So this asks rather than forces.
        //
        // Throttled and gated on distance for two reasons: TryStartSprint logs its own failures
        // (calling it per frame would flood the game's log), and a dash strobing on every short hop
        // is both pointless and the kind of perfectly-regular pattern worth not generating.
        private void TryFarmWalkSwimSprint(float routeRemaining, float now)
        {
            // Approaching: a dash left running carries the player straight past the node (up to
            // 10 m/s through a 1 s deceleration). SwimLocomotion has no public stop, but pressing a
            // vertical input clears _isSprinting as a side effect — so press and immediately
            // release, which cancels the dash while leaving the depth input as it was.
            if (routeRemaining <= FarmWalkSprintStopDistance)
            {
                // NOT gated on the depth hold — that gate made this never fire at all underwater.
                // But it MUST be rate-limited and capped: the unthrottled version pressed every
                // frame (571 cancels in one run), and each press re-stamps
                // _verticalInputBufferStartTime, which blocks depth REVERSALS for 0.3 s — so the
                // spam froze the dive/surface control and the player drifted off the node.
                if (this.farmWalkSprintCancelTries >= FarmWalkMaxSprintCancelTries
                    || now < this.farmWalkNextSprintCancelAt
                    || !this.TryGetFarmWalkSwimLocomotion(out IntPtr slowSwim)
                    || !this.TryGetMonoBoolMember(slowSwim, "IsSprinting", out bool stillSprinting)
                    || !stillSprinting)
                {
                    return;
                }

                this.farmWalkNextSprintCancelAt = now + FarmWalkSprintCancelInterval;
                this.farmWalkSprintCancelTries++;

                // Brake by STEERING BACKWARDS, which is the game's own cancel path: a turn beyond
                // SwimMotionInfo.LargeTurnAngleThreshold between the current move direction and the
                // one being fed clears _isSprinting / _sprintPhase / _sprintTimer and pops the
                // sprint camera. A 180 degree reversal clears any threshold.
                this.farmWalkDashBrakeUntil = now + FarmWalkDashBrakeSeconds;
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": braking to cancel the swim dash ("
                    + routeRemaining.ToString("F1") + "m left, try " + this.farmWalkSprintCancelTries
                    + "/" + FarmWalkMaxSprintCancelTries + ").");
                return;
            }

            if (routeRemaining < FarmWalkSprintMinDistance
                || now < this.farmWalkNextSprintAttemptAt
                || this.farmWalkUnstickPhase != FarmWalkUnstickIdle
                || this.farmWalkHoldHorizontalForClimb)
            {
                return;
            }

            if (!this.TryGetFarmWalkSwimLocomotion(out IntPtr swim))
            {
                return; // on land the axis magnitude already does the job
            }

            this.farmWalkNextSprintAttemptAt = now + FarmWalkSprintRetryInterval;

            // Already dashing, or the game says not now — either way, do not ask again yet.
            if (this.TryGetMonoBoolMember(swim, "IsSprinting", out bool sprinting) && sprinting)
            {
                return;
            }

            if (this.TryGetMonoBoolMember(swim, "IsSprintAvailable", out bool available) && !available)
            {
                return;
            }

            if (this.farmSwimTryStartSprintMethod == IntPtr.Zero && this.farmSwimLocomotionClass != IntPtr.Zero)
            {
                this.farmSwimTryStartSprintMethod = this.FindAuraMonoMethodOnHierarchy(this.farmSwimLocomotionClass, "TryStartSprint", 0);
            }

            if (this.farmSwimTryStartSprintMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.farmSwimTryStartSprintMethod, swim, IntPtr.Zero, ref exc);

            // A fresh dash gets a fresh cancel budget.
            this.farmWalkSprintCancelTries = 0;
        }

        // Is the vertical gap to the target actually shrinking? Sampled on its own clock so a dive
        // in progress is never mistaken for a stall. Only meaningful while a depth hold is engaged.
        private bool IsFarmWalkDepthClosing(Vector3 selfPos, float now)
        {
            if (this.farmWalkVerticalHeld == 0)
            {
                return false;
            }

            float dy = Mathf.Abs(this.farmWalkTarget.y - selfPos.y);
            if (now - this.farmWalkDySampleAt < 0.5f)
            {
                // Between samples, keep the last verdict rather than flapping.
                return dy < this.farmWalkDySample;
            }

            bool closing = dy < this.farmWalkDySample - 0.3f;
            this.farmWalkDySampleAt = now;
            this.farmWalkDySample = dy;
            return closing;
        }

        // Advance the underwater unstick sequence: back off 5 m, then ascend, then resume.
        // Back-off ends on DISTANCE (the point of it) with the timeout only as a guard, so being
        // blocked backwards too cannot wedge the sequence.
        private void UpdateFarmWalkUnstickPhase(Vector3 selfPos, float now)
        {
            if (this.farmWalkUnstickPhase == FarmWalkUnstickIdle)
            {
                return;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickBackingOff)
            {
                if (this.farmWalkUnstickFrom == Vector3.zero)
                {
                    this.farmWalkUnstickFrom = selfPos;
                }

                bool farEnough = Distance3D(selfPos, this.farmWalkUnstickFrom) >= FarmWalkBackOffDistance;
                if (farEnough || now >= this.farmWalkUnstickPhaseUntil)
                {
                    // Only climb if the node is ABOVE. Ascending after a back-off while heading
                    // DOWN just undoes the descent we are trying to finish — the back-off alone is
                    // the useful part there, so resume immediately and let the dive continue.
                    bool targetIsAbove = this.farmWalkTarget.y - selfPos.y > 0f;
                    this.farmWalkUnstickPhase = targetIsAbove ? FarmWalkUnstickAscending : FarmWalkUnstickIdle;
                    this.farmWalkUnstickPhaseUntil = now + FarmWalkObstacleAscendDuration;
                    this.AutoFarmLog("[FarmWalk] backed off "
                        + Distance3D(selfPos, this.farmWalkUnstickFrom).ToString("F1") + "m ("
                        + (farEnough ? "clear" : "timed out") + "), "
                        + (targetIsAbove ? "ascending." : "resuming the descent."));
                }

                return;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickProbing)
            {
                this.UpdateFarmWalkProbe(selfPos, now);
                return;
            }

            if (now >= this.farmWalkUnstickPhaseUntil)
            {
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
            }
        }

        // Probe order, as offsets from the bearing we were pushing toward the node when we wedged.
        // BACKWARDS FIRST: whatever we are jammed against is in front, so reversing is the move
        // most likely to free us — and it is the one the back-off unstick already proves works.
        // Then progressively less-reversed angles, sideways, and only lastly forward again.
        private static readonly float[] FarmWalkProbeOffsets = { 180f, 135f, 225f, 90f, 270f, 45f, 315f, 0f };

        private Vector3 GetFarmWalkProbeDirection(int index)
        {
            float angle = (this.farmWalkProbeBaseYaw + FarmWalkProbeOffsets[index % FarmWalkProbeOffsets.Length]) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        // Alternate horizontal nudge -> vertical attempt, stepping through the directions. Ends as
        // soon as the target gets meaningfully closer, or when every direction has been tried.
        private void UpdateFarmWalkProbe(Vector3 selfPos, float now)
        {
            float distance = Distance3D(selfPos, this.farmWalkTarget);
            if (distance < this.farmWalkProbeBestDistance - FarmWalkProbeProgress)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": probe found a way through at "
                    + distance.ToString("F1") + "m (direction " + (this.farmWalkProbeIndex + 1)
                    + "/" + FarmWalkProbeDirections + ", "
                    + FarmWalkProbeOffsets[this.farmWalkProbeIndex % FarmWalkProbeOffsets.Length].ToString("0")
                    + " deg off target).");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                this.ReleaseFarmWalkDepth();
                return;
            }

            if (now < this.farmWalkUnstickPhaseUntil)
            {
                return;
            }

            if (this.farmWalkProbeStage == FarmWalkProbeStageHorizontal)
            {
                // Horizontal leg done; now try to move vertically from the new spot.
                this.farmWalkProbeStage = FarmWalkProbeStageVertical;
                this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeVerticalSeconds;
                return;
            }

            // Vertical leg done — next direction.
            this.farmWalkProbeStage = FarmWalkProbeStageHorizontal;
            this.farmWalkProbeIndex++;
            this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeMoveSeconds;

            if (this.farmWalkProbeIndex >= FarmWalkProbeDirections)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": probe exhausted all "
                    + FarmWalkProbeDirections + " directions at " + distance.ToString("F1") + "m.");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                this.ReleaseFarmWalkDepth();
            }
        }

        private void BeginFarmWalkProbe(Vector3 selfPos, float now, float distance)
        {
            this.farmWalkProbeUsed = true;
            this.farmWalkUnstickPhase = FarmWalkUnstickProbing;
            this.farmWalkProbeIndex = 0;
            this.farmWalkProbeStage = FarmWalkProbeStageHorizontal;
            this.farmWalkProbeBestDistance = distance;
            this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeMoveSeconds;

            // Bearing we were pushing toward the node; the probe reverses it first.
            Vector3 toTarget = this.farmWalkTarget - selfPos;
            toTarget.y = 0f;
            this.farmWalkProbeBaseYaw = toTarget.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg
                : 0f;

            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": wedged at " + distance.ToString("F1")
                + "m — probing " + FarmWalkProbeDirections + " directions (backwards first) before giving up.");
        }

        // Hold dive or surface while the node is above/below us. Edge-triggered, with a periodic
        // re-assert; see FarmWalkDepthReassertInterval for why this must not fire every frame.
        private void DriveFarmWalkDepth(Vector3 selfPos)
        {
            if (!this.TryGetFarmWalkSwimLocomotion(out IntPtr swim))
            {
                this.farmWalkIsSwimming = false;
                this.farmWalkVerticalHeld = 0; // not swimming: nothing is held, nothing to release
                return;
            }

            this.farmWalkIsSwimming = true;

            float now = Time.unscaledTime;
            float dy = this.farmWalkTarget.y - selfPos.y;

            // Only the ASCEND phase overrides the node's direction; while backing off, depth is
            // still aimed normally so the reverse move stays level.
            bool clearingObstacle = this.farmWalkUnstickPhase == FarmWalkUnstickAscending;

            // Once a direction is held, it stays held down to the release tolerance; a new
            // direction only engages past the (larger) engage tolerance.
            float engage = FarmWalkDepthEngageTolerance;
            float release = FarmWalkDepthReleaseTolerance;

            int want;
            if (this.farmWalkUnstickPhase == FarmWalkUnstickProbing)
            {
                // On the vertical leg, drive toward the node's height; on the horizontal leg, let
                // go so the sideways nudge is not fighting a depth hold.
                if (this.farmWalkProbeStage != FarmWalkProbeStageVertical)
                {
                    want = 0;
                }
                else
                {
                    want = dy > 0f ? 1 : -1;
                }
            }
            else if (clearingObstacle)
            {
                want = 1;
            }
            else if (this.farmWalkVerticalHeld > 0)
            {
                want = dy > release ? 1 : (dy < -engage ? -1 : 0);
            }
            else if (this.farmWalkVerticalHeld < 0)
            {
                want = dy < -release ? -1 : (dy > engage ? 1 : 0);
            }
            else
            {
                want = dy > engage ? 1 : (dy < -engage ? -1 : 0);
            }

            // Swim across before dropping: descending early puts the whole traverse at floor level,
            // scraping every rock on the way. Never blocks an ASCENT — rising is always allowed.
            if (want < 0 && this.farmWalkDeferDescent)
            {
                want = 0;
            }
            if (want == this.farmWalkVerticalHeld)
            {
                if (want != 0 && now - this.farmWalkVerticalAssertedAt >= FarmWalkDepthReassertInterval)
                {
                    this.farmWalkVerticalAssertedAt = now;
                    this.TryInvokeFarmWalkSwimVertical(swim, want > 0, true);
                }

                return;
            }

            // Release the old direction before pressing the new one — the setter only clears the
            // input when told to release the direction that is actually held.
            if (this.farmWalkVerticalHeld != 0)
            {
                this.TryInvokeFarmWalkSwimVertical(swim, this.farmWalkVerticalHeld > 0, false);
            }

            if (want != 0)
            {
                this.TryInvokeFarmWalkSwimVertical(swim, want > 0, true);
                this.farmWalkVerticalAssertedAt = now;

                // The obstacle ascent logs itself once when it starts; without this guard it would
                // also log a "surfacing"/"diving" pair every time it engages and releases.
                if (!clearingObstacle)
                {
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + (want > 0 ? "surfacing" : "diving")
                        + " " + Mathf.Abs(dy).ToString("F1") + "m.");
                }
            }

            this.farmWalkVerticalHeld = want;
        }

        // Let go of dive/surface. Must run on every exit path: a held input persists on the
        // locomotion, so ending a walk mid-dive would leave the player sinking indefinitely.
        private void ReleaseFarmWalkDepth()
        {
            if (this.farmWalkVerticalHeld == 0)
            {
                return;
            }

            if (this.TryGetFarmWalkSwimLocomotion(out IntPtr swim))
            {
                this.TryInvokeFarmWalkSwimVertical(swim, this.farmWalkVerticalHeld > 0, false);
            }

            this.farmWalkVerticalHeld = 0;
        }

        // ── REPAIR THROW VERIFY / DESCEND / RETRY ──────────────────────────
        // A repair kit is not a use-item, it is a ToolRestorer ENTITY the server places on ground.
        // Thrown from open water there may be nothing to place it on: CanPutRestorerResult never
        // approves, no ToolRestorerEvent fires, IsAutoRepairBusy() reads false, and the sea-clean
        // dwell gives up with the cleaner still broken. Sinking toward the floor first gives the
        // placement something to land on.
        //
        // "Actually started" is judged by the game's own signals, not by our send returning true:
        // ToolRestorerEvent (server approved the throw) and the 701-706 tool-restore buff (the aura
        // is running). IsAutoRepairBusy() covers both.
        private const float FarmRepairRetryDescendSeconds = 2f;
        private const float FarmRepairRetryVerifySeconds = 3.5f;
        private const int FarmRepairMaxRetries = 4;

        private int farmRepairRetries;
        private float farmRepairNextAttemptAt;
        private float farmRepairDescendUntil;

        internal void ResetContaminationRepairRetryState()
        {
            if (this.farmRepairRetries == 0 && this.farmRepairDescendUntil <= 0f)
            {
                return;
            }

            this.farmRepairRetries = 0;
            this.farmRepairNextAttemptAt = 0f;
            this.farmRepairDescendUntil = 0f;
            this.ReleaseFarmWalkDepth();
        }

        // Sink onto a thrown repair kit so its aura actually reaches the player.
        //
        // The ToolRestorer is an entity resting on the sea floor with a SPHERE of effect around it
        // (radius = TableBuffConfig.range). Underwater the throw happens wherever the player is
        // floating, which can be well above that sphere — so the server approves the kit, the kit
        // lands, and the repair never starts because nobody is standing in it. Descending is the
        // whole fix; the restore buff (checked by the caller via IsAutoRepairBusy) ends the hold.
        //
        // Returns true when it is actively sinking, so the caller can say so in the status line.
        private bool TryHoldDescentIntoRepairAura()
        {
            if (this.repairBuffActive)
            {
                // Already in the aura — stop sinking, let it run.
                this.ReleaseFarmWalkDepth();
                return false;
            }

            if (!this.TryGetFarmWalkSwimLocomotion(out IntPtr swim))
            {
                return false; // on land the kit lands at the player's feet; nothing to do
            }

            float now = Time.unscaledTime;
            if (this.farmWalkVerticalHeld >= 0)
            {
                this.TryInvokeFarmWalkSwimVertical(swim, false, true);
                this.farmWalkVerticalHeld = -1;
                this.farmWalkVerticalAssertedAt = now;
                ModLogger.Msg("[FarmWalk] repair kit thrown — sinking onto it to enter the repair aura.");
            }
            else if (now - this.farmWalkVerticalAssertedAt >= FarmWalkDepthReassertInterval)
            {
                this.farmWalkVerticalAssertedAt = now;
                this.TryInvokeFarmWalkSwimVertical(swim, false, true);
            }

            return true;
        }

        // How long the farm may sit still for a repair before carrying on regardless. The repair
        // window itself is ~30 s; this only bounds a wedged one.
        private const float FarmRepairAuraHoldSeconds = 40f;
        private float farmRepairAuraHoldSince = -1f;

        // Per-frame, EVERY farm state. A repair is triggered by whatever notices low durability —
        // usually nowhere near the contamination dwell — so scoping the descent to that one branch
        // meant the common case (kit thrown mid-walk, underwater) never sank at all and the aura
        // was left floating below the player.
        //
        // Returns true while the farm should hold still for the repair.
        private bool ProcessFarmRepairAuraHold()
        {
            bool busy;
            try
            {
                busy = this.IsAutoRepairBusy();
            }
            catch
            {
                busy = false;
            }

            if (!busy)
            {
                if (this.farmRepairAuraHoldSince >= 0f)
                {
                    this.farmRepairAuraHoldSince = -1f;
                    this.ReleaseFarmWalkDepth();
                }

                return false;
            }

            float now = Time.unscaledTime;
            if (this.farmRepairAuraHoldSince < 0f)
            {
                this.farmRepairAuraHoldSince = now;
            }

            if (now - this.farmRepairAuraHoldSince > FarmRepairAuraHoldSeconds)
            {
                this.ReleaseFarmWalkDepth();
                return false; // bounded: never let a wedged repair pin the farm
            }

            // Do not swim away from the kit while its aura is what we are waiting for.
            this.TryClearGameMoveAxis();

            this.autoFarmStatus = this.TryHoldDescentIntoRepairAura()
                ? "Sinking into the repair aura..."
                : "Waiting for the repair aura...";
            return true;
        }

        // Returns true while it is still working on getting a repair started (caller should hold
        // the dwell); false once the retries are exhausted.
        private bool TryRetryContaminationRepairThrow(float now)
        {
            // Order matters: VERIFY the previous throw first, and only sink if it produced nothing.
            // (The caller only reaches this method while the repair has NOT started — once
            // ToolRestorerEvent or the restore buff lands, IsAutoRepairBusy() holds the dwell
            // instead and this never runs.)
            if (now < this.farmRepairNextAttemptAt)
            {
                this.autoFarmStatus = "Checking the repair kit landed...";
                return true;
            }

            // Sinking phase: hold dive, then throw again from the lower position.
            if (now < this.farmRepairDescendUntil)
            {
                if (this.TryGetFarmWalkSwimLocomotion(out IntPtr sinkSwim) && this.farmWalkVerticalHeld >= 0)
                {
                    this.TryInvokeFarmWalkSwimVertical(sinkSwim, false, true);
                    this.farmWalkVerticalHeld = -1;
                    this.farmWalkVerticalAssertedAt = now;
                }

                this.autoFarmStatus = "Descending to place the repair kit...";
                return true;
            }

            if (this.farmRepairRetries >= FarmRepairMaxRetries)
            {
                ModLogger.Msg("[FarmWalk] repair kit never started after " + FarmRepairMaxRetries
                    + " throws — giving up on this node.");
                this.ResetContaminationRepairRetryState();
                return false;
            }

            this.farmRepairRetries++;

            // Stop sinking before the throw so the kit is placed from a settled position.
            this.ReleaseFarmWalkDepth();

            bool thrown = false;
            try
            {
                thrown = this.TryDirectUseRepairKit();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmWalk] repair throw " + this.farmRepairRetries + " threw: "
                    + ex.GetType().Name + ": " + ex.Message);
            }

            ModLogger.Msg("[FarmWalk] repair throw " + this.farmRepairRetries + "/" + FarmRepairMaxRetries
                + " sent=" + thrown + " — verifying for " + FarmRepairRetryVerifySeconds.ToString("0.#") + "s.");

            // Verify first; only sink again if this throw produced nothing.
            this.farmRepairNextAttemptAt = now + FarmRepairRetryVerifySeconds;
            this.farmRepairDescendUntil = now + FarmRepairRetryVerifySeconds + FarmRepairRetryDescendSeconds;
            this.autoFarmStatus = "Repair kit thrown, verifying...";
            return true;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Steering and corner logic stay horizontal (the walker cannot climb, so height is noise
        // there), but anything compared against the server's collect rule must be 3-D.
        private static float Distance3D(Vector3 a, Vector3 b)
        {
            return (a - b).magnitude;
        }
    }
}
