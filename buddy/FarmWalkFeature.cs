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
        // Always full stick. The old 0.2–0.95 slider existed only to stay under the sprint
        // threshold and its stamina drain — and this game has no stamina, so the ceiling bought
        // nothing but a slower farm. Full axis still only reaches MotionInfo.MovingSpeedLimit =
        // 4.0 m/s, the game's own run cap, under MovementAntiCheating's 4.3 m/s threshold.
        private const float FarmWalkSpeedMax = 1f;

        // A corner is "reached" inside this XZ radius. Loose enough that the locomotion's own
        // acceleration curve does not overshoot into an orbit around the point.
        private const float FarmWalkCornerReachDistance = 1.2f;

        // Arrival is measured in 3-D, not in XZ.
        //
        // What decides whether a gather happens is the Aura Farm's own reach, and the aura measures
        // the real separation — so an XZ-only test silently fails on any resource that is not at
        // the player's feet: the first
        // coastal run stopped 5.7 m XZ from an oyster sitting 3.8 m up a rock — 6.8 m apart in 3-D.
        // The aura's own scan radius (AuraDirectScanRadius, 8 m) is far looser and hides this; the
        // server just drops the collect, and per project memory those rejections are SILENT, so the
        // farm looks like it is working while gathering nothing.
        // HORIZONTAL target. The 3-D verdict is FarmWalkArrivalDistance below, and the two must NOT
        // be the same number: 3-D distance is always >= horizontal, so equal thresholds leave
        // sqrt(t^2 - dy^2) of horizontal room, i.e. ZERO once dy reaches the threshold. Setting both
        // to 0.5 made a node twenty centimetres above the player unreachable — the 04:49 run failed
        // five in a row with "under the node but 0,5m away vertically (dy=0,2m)" while the one node
        // that happened to be level arrived at 0,49m.
        //
        // 0.25 horizontal + 0.8 3-D leaves room for ~0.76 m of height, which is what the walker can
        // actually close.
        //
        // 0.25 m — walk essentially ONTO the node, the way the teleport path used to land.
        //
        // Reasoning from the in-game runs: at 1.5 m the walker reported "arrived 1,29m" and then
        // "arrived 1,29m" again on the SAME oyster — it stopped where it believed the collect would
        // work and the collect still did not happen, so the farm re-scanned and re-targeted the same
        // node forever. Anything the walker
        // cannot quite close now trips the not-closing timeout and gets a very short final hop,
        // which is a strictly better failure than looping on an uncollectable node.
        //
        private const float FarmWalkCollectDistance = 0.25f;

        // Below this range the approach eases off. At full axis the locomotion overshoots a 0.25 m
        // target and orbits it; the game floors the joystick at 0.1, so 0.2 is the slowest usable
        // creep. Anything nearer than this scales linearly between the two.
        private const float FarmWalkSlowApproachDistance = 2f;
        private const float FarmWalkSlowApproachSpeed = 0.2f;

        // 2026-08-16, on request: the ARRIVAL VERDICT is 0.5 m in 3-D, replacing the old 1.8 m
        // backstop that let half a metre in the plane pass with over a metre of unclosed height.
        //
        // ⚠️ Keep this STRICTLY LARGER than FarmWalkCollectDistance. They are not two views of one
        // number: 3-D >= horizontal always, so with both at the same value the height budget is
        // sqrt(t^2 - t^2) = 0, and any node even slightly off level becomes unreachable. That is
        // exactly what a run with both at 0.5 did — five consecutive failures at dy = 0.1-0.2 m.
        //
        // ⚠️ The horizontal test stays as the ENTRY to the arrival block, and it has to: the branch
        // underneath it — "horizontally there, vertically short" — is what drives the final dive
        // onto a sea grape and what reports an unreachable ledge on land. Testing 3-D alone would
        // skip straight past it.
        // 2026-08-16: 0.5 -> 0.8 on request. Still well inside the aura's reach, and it widens the
        // height budget the walker can accept without the wider fallback below:
        // sqrt(0.8^2 - 0.25^2) = 0.76 m, up from 0.43.
        // 2026-08-17: 0.8 -> 1.2 on request, because the walker was still hopping at nodes it could
        // already collect from. The height budget is the reason: sqrt(1.2^2 - 0.25^2) = 1.17 m, and
        // the ore nodes that triggered the hopping sit ~1.3 m off in Y (see
        // TryRefineFarmWalkTargetHeight, which closes the rest). Still inside the aura's reach.
        private const float FarmWalkArrivalDistance = 1.2f;

        // ⚠️ THIS IS THE AURA FARM'S TRIGGER RADIUS. 1.5 m, measured in game 2026-08-20.
        //
        // It was 1.8 m for a while, taken from a comment that claimed a 2 m server anti-cheat rule.
        // No such rule governs this: what collects a resource is the aura, and an arrival at 1,76 m
        // collected nothing at all.
        //
        // 1.4 leaves a little margin under the radius. Used to decide whether an approach that could
        // not close the HEIGHT should be abandoned — walking cannot fix a 1.2 m drop, but the aura
        // still fires from there, and skipping it loses the resource for nothing.
        private const float FarmWalkAuraReach = 1.5f;
        private const float FarmWalkAuraReachFallback = 1.4f;

        // Where the walker STOPS on a good approach: inside the reach with room to spare, so that
        // an anchor height that is off by a few decimetres, or a last-moment drift, cannot push the
        // finished walk outside it. Driving all the way to FarmWalkCollectDistance (0.25 m) is what
        // made the last metre the expensive part of every walk; stopping at the edge of the reach
        // (1.76 m) collected nothing at all. This is the middle that does both jobs.
        private const float FarmWalkCollectStandoff = 1.1f;

        // ⚠️ NARROW IN STEPS, ON THIS NODE, UNTIL IT COLLECTS.
        //
        // The first version learned one tighter number per kind and applied it to the NEXT node —
        // so the node that taught us the lesson was abandoned, and if the new number was still too
        // far, the next one was abandoned too. Stepping in on the node in hand costs a few seconds
        // and both collects it and finds the real distance instead of a safe guess.
        //
        // The floor is FarmWalkCollectDistance: below that we are standing on the node, which is
        // what the stand-off exists to avoid, and if that does not collect then distance was never
        // the problem.
        private const float FarmWalkStandoffStep = 0.3f;
        private const int FarmWalkMaxStandoffSteps = 4;

        // Steps spent narrowing on the node in hand. Reset when a collect succeeds, so the budget is
        // per stubborn node rather than per session.
        private int farmWalkStandoffRetries;
        private readonly System.Collections.Generic.Dictionary<string, float> farmWalkKindStandoff =
            new System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal);

        private float ResolveFarmWalkCollectStandoff()
        {
            return !string.IsNullOrEmpty(this.farmWalkDwellLabel)
                && this.farmWalkKindStandoff.TryGetValue(this.farmWalkDwellLabel, out float learned)
                    ? learned
                    : FarmWalkCollectStandoff;
        }

        // One step closer for this kind. Returns false when there is no room left to step.
        internal bool TryNarrowFarmWalkStandoff(string label, out float from, out float to)
        {
            from = FarmWalkCollectStandoff;
            to = FarmWalkCollectStandoff;
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            if (this.farmWalkKindStandoff.TryGetValue(label, out float current))
            {
                from = current;
            }

            to = from - FarmWalkStandoffStep;
            if (to < FarmWalkCollectDistance)
            {
                return false;
            }

            this.farmWalkKindStandoff[label] = to;
            return true;
        }

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

        // Deepest drop that may be postponed to the end of the leg. Past this the descent runs
        // alongside the traverse instead — see the defer test for what holding a 13 m drop did.
        private const float FarmWalkMaxDeferredDescent = 5f;

        // How many back-off rounds get a vertical leg: one up, one down, then no more.
        private const int FarmWalkBackOffVerticalRounds = 2;

        // Rebuilds that hand back the SAME route while the walker has not advanced a corner. Past
        // this the node is abandoned for a different one — the graph has nothing else to offer here.
        private const int FarmWalkMaxFutileRepaths = 3;

        private int farmWalkFutileRepaths;

        // Waypoints proven unwalkable, mapped to the time their ban lifts. Survives between walks —
        // the blockage is a property of the world, and re-learning it costs a whole walk each time.
        //
        // ⚠️ BANS MUST EXPIRE AND MUST BE CAPPED. As a permanent per-run set this ate the graph: a
        // long session banned 696 of the 1745 land waypoints — 40%, concentrated exactly where the
        // farm works — and then nothing could route at all, because the snap skips excluded nodes
        // and found zero candidates within 60 m. The symptom was "Quest Walk jumps into the vehicle
        // and goes nowhere", with "no graph node within 60m of an endpoint" underneath it.
        //
        // A ban is a heuristic about a moment, not a fact about the map: another player standing in
        // a doorway, a prop, a mesh seam. Five minutes matches the node parking window.
        private const float FarmWalkBlockedNodeTtl = 300f;

        // Hard ceiling on how much of the graph may be off the table at once. Well under the ~1745
        // land nodes, and small enough that a dense area still has somewhere to snap.
        private const int FarmWalkMaxBlockedNodes = 48;

        private readonly Dictionary<int, float> farmWalkBlockedGraphNodes = new Dictionary<int, float>();
        private readonly List<int> farmWalkBlockedScratch = new List<int>();

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
        // "Ascending" by history only — the phase now drives whichever vertical direction the
        // alternator picked for this round, so it descends on even rounds.
        private const int FarmWalkUnstickAscending = 2;
        private const int FarmWalkUnstickProbing = 3;
        private const int FarmWalkUnstickHopBurst = 4;

        // The retreat-and-chain burst that used to live here is gone. It reversed five metres and
        // then pushed at the node while pulsing the jump — and pushing INTO a blocker suppresses the
        // jump outright (measured: 43 pulses, 0.16 m of path, grounded 100% the whole time). What
        // replaced it is the apex escape further down, whose numbers are recorded beside it.

        // Final-approach probe: when the walker is close but wedged, sweep the eight compass
        // directions one at a time, each followed by a vertical attempt, looking for the gap in
        // whatever geometry is in the way. Cheaper than giving up on the node, and it is the only
        // search available — the mod has no collision data to reason about the obstacle with.
        // Aborted the moment the 3-D distance improves.
        // Horizontal leg of the probe: swim this far off the blocker before trying the vertical
        // again. Distance, not duration — see UpdateFarmWalkProbe. Matched to
        // FarmWalkBackOffDistance so both underwater unsticks retreat by the same amount.
        private const float FarmWalkProbeHorizontalDistance = 5f;

        // Guard for the leg above, sized for 5 m at swim speed with slack for drag and turning.
        private const float FarmWalkProbeHorizontalTimeout = 4f;

        private const float FarmWalkProbeVerticalSeconds = 0.6f;
        private const int FarmWalkProbeDirections = 4;
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

        // How far off the AIM POINT a standoff approach may finish. Not the aura's reach —
        // that governs plain nodes only. See the arrival test for why this is a judgement call.
        private const float FarmWalkStandoffArrivalDistance = 4f;

        // Within this range of the target, "no progress" means the last metres are simply not
        // walkable (a ledge, a rock, a jetty). Escalate to the final-approach teleport on the FIRST
        // stuck sample instead of burning the full three strikes on something that cannot be fixed
        // by walking harder.
        private const float FarmWalkFinalApproachDistance = 8f;

        // SAFETY cadence, not a driver — the route is pinned and re-paths on cause (see the
        // re-path block). At 1.5 s this rebuilt the route ~40 times per walk and was itself the
        // reason the walker circled; 12 s is a backstop for a route that has gone stale in a way
        // neither the corridor test nor the not-closing timer notices.
        // The off-corridor distance below mirrors the game's own live deviateDis (16 sqr).
        private const float FarmWalkRepathInterval = 12f;
        private const float FarmWalkCorridorTolerance = 4f;

        // How much shorter a rebuilt route must be to count as progress rather than a reshuffle.
        // Half a metre is under the noise of re-snapping both ends and well under the spacing of the
        // waypoint graph, so it passes real improvements and rejects the 3<->5 corner flip.
        private const float FarmWalkRepathMustGain = 0.5f;

        // ⚠️ A FLOOR UNDER EVERY REBUILD, whatever the cause. "Not closing" stays true until the
        // route actually improves, and a waypoint ban used to clear the re-path timer outright, so
        // the two together ran at frame rate: twelve rebuilds and SIX waypoint bans inside one and a
        // half seconds, taking the ban list from 37/48 to 42/48 — and those bans last five minutes,
        // so the damage outlives the walk that caused it.
        //
        // One second is far below the cadence anything legitimate needs and far above frame time.
        private const float FarmWalkRepathMinGap = 1f;

        // Stuck detection: sample this often, and treat less than this much ground covered while a
        // move axis is being applied as a strike. Three strikes escalate to the teleport fallback.
        private const float FarmWalkStuckSampleInterval = 0.6f;
        // 0.15 m, not 0.35 m. This sampler only has to catch a HARD freeze (a true wedge reads
        // 0.00–0.05 m); "moving but getting nowhere" is already covered, and covered better, by the
        // remaining-route check below. At 0.35 m it killed a walk logging 0.34 m — a player creeping
        // past an obstacle at ~0.57 m/s, one centimetre from being left alone to finish.
        private const float FarmWalkStuckMinProgress = 0.15f;

        // How long a walk gets to turn and accelerate before a missed sample counts against it.
        // Two sample windows: one to stop and turn, one to be moving.
        private const float FarmWalkStuckGraceSeconds = 1.5f;

        // Walking off a ledge to land on a node below. Falling is free in this game, so the only
        // budget needed is against a node that is below because it sits under solid rock, where
        // walking on simply runs into more ground.
        private const int FarmWalkMaxDropAttempts = 2;
        private const float FarmWalkDropSeconds = 1.6f;
        private int farmWalkDropAttempts;
        private float farmWalkDropUntil;
        private Vector3 farmWalkDropDir;
        private Vector3 farmWalkDropFrom;
        private bool farmWalkDropAirborne;
        private float farmWalkStartedAt;
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

        // How many times the walker RE-PATHED ON ITS OWN. It only rises on a corrective re-path —
        // the kind it uses to route around an obstacle. QuestWalk needs it: that one watches the
        // corner count, and the count changes identically whether the game sent a new route or the
        // walker went around a wall. Without this counter the second was taken for the first, and
        // the detour was overwritten by the very route that had hit the wall.
        internal int farmWalkOwnRouteSeq;
        private float farmWalkLastRepathAt;
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

        // Latched once this walk has had to unstick: the deferral is a comfort optimisation and is
        // never allowed to be the reason a walk fails twice.
        private bool farmWalkDescentDeferGivenUp;

        // ⚠️ THE DEFER THRESHOLD NEEDS HYSTERESIS LIKE EVERY OTHER ONE HERE.
        //
        // deferrableDrop was a bare comparison against FarmWalkMaxDeferredDescent, and a walk that
        // settles NEAR that line does not sit still: dy jitters by a decimetre and flips the rule
        // every second. Each flip releases the dive input and presses it again, so the descent
        // never lasts long enough to move the body, and the log fills with the same line:
        //     17:32:20 diving 5,1m   17:32:21 diving 5,0m   17:32:22 diving 5,0m
        //     ... 25 of them in 40 s, dy still 5,0m, three unstick rounds burned ...
        // Depth ENGAGE/RELEASE already carry a band (0.35 / 0.12) for exactly this reason; the
        // ceiling did not, and pinned the player at one depth with the obstacle in front of it.
        private const float FarmWalkDeferDescentHysteresis = 0.75f;
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
        private int farmWalkEndNodeIndex = -1;
        private readonly HashSet<int> farmWalkExcludedNodes = new HashSet<int>();

        // Holds the last good route across an edge-audit retry; see the trap noted at the retry.
        private readonly System.Collections.Generic.List<Vector3> farmWalkEdgeAuditBackup =
            new System.Collections.Generic.List<Vector3>();

        // Final waypoints to avoid when snapping the TARGET end of a route. A retry seeds this with
        // the waypoint whose approach already failed, forcing A* to come at the node from a
        // different side — without it a retry rebuilds the identical approach and fails with
        // identical numbers, after walking all the way back.
        private readonly HashSet<int> farmWalkExcludedEndNodes = new HashSet<int>();
        private int farmWalkRetryAvoidEndNode = -1;

        // A retry is only worth walking if the node is still nearby. Further than this the route
        // is a long trek to an approach that has already failed once.
        private const float FarmWalkRetryMaxDistance = 35f;

        // Set when a failed walk should hand back to the node scan instead of teleporting, so the
        // farm moves on to the next nearest node rather than warping to an unreachable one.
        // Deliberate vertical standoff baked into farmWalkTarget (contamination only, 0 otherwise).
        // farmWalkTrueTarget keeps the real node position: the cooldown stamp and the teleport
        // fallback must both address the NODE, never the offset aim point.
        private float farmWalkAimOffsetY;

        // A node's aim point is the resource's ANCHOR, which is its pivot — for a rock that is the
        // base, while the player ends up standing on top of the collider: an Ore aim routinely sits
        // ~1.3 m below where the character can actually stand. Once per walk, close enough for the
        // entity to be streamed in, the aim height is replaced with the resource's REAL y.
        // (Unchanged by the move off the hand-snapped tables: the harvested points were recorded
        // from these same anchors, so the offset is a property of the anchor, not of stale data.)
        private bool farmWalkHeightRefined;
        private const float FarmWalkHeightRefineRange = 8f;   // horizontal distance to try it at
        private const float FarmWalkHeightRefineMatch = 3f;   // how near the aim point the entity must be

        // Last scan (liveCollectableScanCompletedAt) that still SAW this walk's node. -1 = never
        // seen yet, which is the state a node keeps while it is outside the streamed bubble.
        private float farmWalkTargetSeenAt = -1f;
        // Inside this range, absence from the scan is conclusive on its own — no prior sighting needed.
        private const float FarmWalkDrainedCloseDistance = 12f;
        // Crossing this forces one fresh scan, so the approach is judged on current world state well
        // before the last stretch rather than a scan interval into it.
        private const float FarmWalkApproachVerifyDistance = 25f;
        private bool farmWalkApproachScanForced;
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

        // Retry-after-next-collect. A skipped node is not abandoned: once the following node has
        // actually been collected, come back and try it again with a FRESH route. By then the
        // player is standing somewhere else entirely, so the approach is recomputed from a new
        // angle — which is often the whole reason the first attempt failed.
        //   0 = nothing pending, 1 = skipped, waiting for the next collect, 2 = ready to retry.
        private int farmWalkRetryState;
        private Vector3 farmWalkRetryNode;
        private string farmWalkRetryLabel;

        // Called on a genuine arrival, to arm a pending retry for the next scan.
        private void NoteFarmWalkCollectForRetry()
        {
            if (this.farmWalkRetryState == 1)
            {
                this.farmWalkRetryState = 2;
            }
        }

        // Taken by the node scan, ahead of picking a fresh nearest node.
        private bool TryTakeFarmWalkRetryNode(out Vector3 node, out string label)
        {
            node = this.farmWalkRetryNode;
            label = this.farmWalkRetryLabel;
            if (this.farmWalkRetryState != 2)
            {
                return false;
            }

            this.farmWalkRetryState = 0;

            // Only worth it if we are still near it — otherwise the retry is a long trek back to
            // an approach already known to fail.
            if (this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _)
                && HorizontalDistance(selfPos, node) > FarmWalkRetryMaxDistance)
            {
                ModLogger.Msg("[FarmWalk] skipped node is " + HorizontalDistance(selfPos, node).ToString("F0")
                    + "m away now — not walking back for it.");
                this.farmWalkRetryAvoidEndNode = -1;
                return false;
            }

            // Clear the skip stamp, or FindClosestAvailableNode would keep passing it over.
            this.ForgetVisitedNode(node);
            return true;
        }

        // A node already reclaimed once and failed again is a repeat offender: reclaiming it a
        // second time produces the "two teleports back to back" the log shows — the reclaim hop,
        // then an area relocation moments later because the area is empty anyway. Park it instead,
        // so the farm relocates ONCE and stops cycling on it.
        private Vector3 farmWalkLastReclaimedNode;
        private float farmWalkLastReclaimedAt = -999f;
        private bool farmWalkHasLastReclaimed;
        private const float FarmWalkRepeatOffenderWindow = 180f;
        private const float FarmWalkRepeatOffenderParkSeconds = 300f;

        // Failure ladder for a node the walk could not reach:
        //   1st failure, node within FarmWalkRescueTeleportRange and the rescue is off cooldown
        //       -> one teleport onto it;
        //   1st failure otherwise -> skip to a different node (the existing skip/retry path);
        //   2nd failure           -> park it, whatever the distance.
        //
        // The cooldown is what keeps this honest. A rescue every minute cannot become the way the
        // farm gets around — at the observed cadence of roughly one node every 5-8 s it covers well
        // under a tenth of the hops — so the mode still travels on foot and the server still sees
        // continuous movement between resources.
        private const float FarmWalkRescueTeleportRange = 10f;
        private const float FarmWalkRescueTeleportCooldown = 60f;
        private const int FarmWalkMaxNodeFailures = 2;

        private float farmWalkLastRescueTeleportAt;
        private readonly System.Collections.Generic.Dictionary<Vector3, int> farmWalkNodeFailures
            = new System.Collections.Generic.Dictionary<Vector3, int>();

        // What the current hop burst / probe is trying to reach: the node on a final approach, the
        // current corner on a mid-route wedge.
        private Vector3 farmWalkHopBurstAim;
        private Vector3 farmWalkProbeAim;

        // Underwater back-off rounds. The vertical leg alternates up / down between them, so a
        // blocker that cannot be cleared over is tried under on the next attempt.
        private int farmWalkBackOffRound;
        private int farmWalkBackOffVerticalDir = 1;

        // Where the current probe leg started, so it can end on distance swum rather than on time.
        private Vector3 farmWalkProbeLegFrom;

        // Zone travel, two independent user switches.
        //
        // Walking between farm areas and riding there are separate decisions, and deliberately so:
        // the areas are 200-400 m apart, so walking one is a real time cost the user may accept on
        // its own, while the vehicle carries its own constraints — it cannot be summoned underwater
        // at all (twice-confirmed silent AreaForbid), and its summon needs clear space in front of
        // the player. Either switch is useful without the other.
        internal bool farmWalkToAreaEnabled;
        internal bool farmWalkUseVehicleEnabled;

        // Straight-line distance past which the vehicle is worth summoning. Slider bounds, not
        // behaviour limits: below the floor the summon costs more time than it saves, and the
        // ceiling is past the widest gap between farm areas on this map (~390 m observed).
        internal const float FarmWalkVehicleMinDistanceFloor = 10f;
        internal const float FarmWalkVehicleMinDistanceCeiling = 1000f;
        internal float farmWalkVehicleMinDistance = 50f;

        // Mirrors StealthForagingActive: the toggle only means anything while a run is going.
        // Read by OutOfBoundsGuardFeature — see IsOutOfBoundsGuardRequested for why.
        //
        // ⚠️ Quest Walk is a SECOND term, deliberately added rather than routed around. The OOB
        // rescue suppression hangs off this property, and the last time a feature quietly dropped
        // a term from it every underwater relocation got rolled back 48 m upward. A quest walk
        // drives the same walker through the same water, so it needs the same suppression.
        internal bool FarmWalkRunActive => (this.farmWalkToNodeEnabled && this.autoFarmActive)
                                           || this.questWalkFollowing;

        // How many alternative end nodes to try when the direct line to a node is blocked.
        private const int FarmWalkDetourAttempts = 4;

        private readonly System.Collections.Generic.HashSet<int> farmWalkDetourExcluded
            = new System.Collections.Generic.HashSet<int>();

        // Probe buffer for detour candidates, kept apart from farmWalkScratchCorners so a rejected
        // candidate can never become the committed route.
        private readonly System.Collections.Generic.List<Vector3> farmWalkDetourCorners
            = new System.Collections.Generic.List<Vector3>();

        // One detour search per walk. Without this the search re-ran on every re-path.
        private bool farmWalkDetourSearchDone;

        // True while the scan should keep looking rather than relocating or reclaiming.
        private bool ShouldHoldFarmScanForSkippedNode()
        {
            return this.farmWalkHasSkippedNode && Time.unscaledTime < this.farmWalkReclaimNotBefore;
        }
        // Consecutive skips with no successful arrival. Bounded so a pocket of unreachable nodes
        // cannot spin the scan forever — past the cap, one teleport breaks the deadlock.
        private int farmWalkConsecutiveSkips;

        // ⚠️ NO ROUTE IS A REASON TO PICK ANOTHER NODE, NOT TO WARP.
        //
        // Every failed route build used to end in FarmTeleportTo. That is the loudest thing the mod
        // does — the whole point of walk mode is that the server sees ordinary movement — and it was
        // being spent on the most ordinary of causes: one node the graph cannot reach, in a field
        // full of nodes it can. Measured 2026-08-22: "A* found no route between waypoints 83 and 67
        // — cannot route", followed immediately by a 39 m warp, while four Glasswort within twenty
        // metres had just been collected on foot.
        //
        // So the node is parked briefly and the farm goes back to the scan, which offers the next
        // one. The teleport survives only as the backstop for "nothing here can be routed to at
        // all", after enough consecutive failures to make that the likely truth.
        private const float FarmWalkRouteFailStampSeconds = 45f;
        private int farmWalkRouteFailures;

        // Always true: "no route" is never a reason to warp (rule 0.6).
        //
        // An earlier version gave up after four failures in a row and teleported "because this is
        // not one bad node". That reasoning is wrong twice over: a run of unroutable nodes is a
        // statement about the graph in this area, and warping does not fix a graph — it just moves
        // the player somewhere the same problem will recur, loudly. Park each one and keep picking;
        // the stamps expire, and a walk that gets us elsewhere makes the graph reachable again.
        private bool TryDeferUnroutableFarmNode(Vector3 node, string label)
        {
            this.farmWalkRouteFailures++;

            // Parked, not banned: the graph may reach it from somewhere else entirely, and the walk
            // that gets us near will make that true. Short enough that a lap of the area retries it.
            this.StampVisitedNode(node, Time.unscaledTime + FarmWalkRouteFailStampSeconds,
                approachFailure: true);
            ModLogger.Msg("[FarmWalk] no route to " + (label ?? "the node") + " at "
                + FormatNavMeshVector(node) + " — parking it for "
                + FarmWalkRouteFailStampSeconds.ToString("F0") + "s and taking the next node instead ("
                + this.farmWalkRouteFailures + " in a row).");
            return true;
        }

        internal void NoteFarmWalkRouteSucceeded()
        {
            this.farmWalkRouteFailures = 0;
        }
        private const int FarmWalkMaxConsecutiveSkips = 3;

        // Dive/surface state: -1 diving, 0 released, +1 surfacing, plus the resolved Mono method.
        private int farmWalkVerticalHeld;
        private int farmWalkPrevVerticalHeld;
        private float farmWalkVerticalAssertedAt;

        // Height and time at which the current dive/surface hold was pressed — the yardstick the
        // next log line uses to say whether the hold actually moved anything.
        private float farmWalkDepthAssertFrom = float.NaN;
        private float farmWalkDepthAssertStartedAt;
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
        // On-foot hop-burst state.
        // ⚠️ A BUDGET, NOT A FLAG. One escape per walk was harmless while the escape did nothing;
        // now that it works it is the binding constraint. A walk climbed its ledge with the first —
        // "+0,95m closer, +3,35m up, airborne 70%, cleared it" — and then gave up two metres from
        // the target, on level ground, because there was no second one left:
        //     final approach not walkable (2,0m, dy=-0,1m) ... jumps are spent, taking the teleport
        // Three is enough for a ledge, a lip and a last-metre kerb, and still bounded — each escape
        // has its own 22 s cap, so the worst case stays inside the walk's own patience.
        private int farmWalkHopBurstsUsed;
        private bool farmWalkEscapePressBarren;
        private bool farmWalkEscapeWon;
        private float farmWalkEscapeSimpleUntil, farmWalkEscapeSimpleJumpAt;
        private Vector3 farmWalkEscapeSteerDir;
        private float farmWalkHopBurstUntil;
        // Where we wedged: the retreat measures away from it, the run-up measures back toward it.
        private Vector3 farmWalkHopAnchor;
        private int farmWalkEscapeStage;
        private float farmWalkEscapePressSide;
        private Vector3 farmWalkEscapePressDir;
        private Vector3 farmWalkEscapePressFrom, farmWalkEscapePressSample;
        private float farmWalkEscapePressSampleAt;
        private float farmWalkEscapeStageSince;
        private int farmWalkEscapeHeading;
        private int farmWalkEscapeRepeats;
        private Vector3 farmWalkEscapeAttemptFrom;
        private float farmWalkEscapeAttemptUntil;
        private int farmWalkEscapeHops, farmWalkEscapeHopsRefused;
        private int farmWalkEscapeFrames, farmWalkEscapeAirFrames;
        private int farmWalkApexPhase;
        private float farmWalkApexPhaseSince;
        private IntPtr farmSwimLocomotionClass;
        private IntPtr farmSwimSetVerticalMethod;
        private int farmWalkStuckStrikes;
        private float farmWalkDeadline;
        private bool farmWalkActive;

        // Arrival setup captured from the caller, so finishing a walk reproduces exactly what the
        // teleport path would have done at the moment it landed.
        private bool farmWalkPendingPriority;

        // This walk is heading for a cleansing coral, not a resource — the arrival hands over to
        // the cleanse wait instead of starting a collect.
        internal bool farmWalkPendingCleanse;

        // This walk is a zone haul: the arrival goes to LoadingArea, not to a collect.
        internal bool farmWalkPendingArea;
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
            //
            // The one exception is a waypoint that has already proved unwalkable THIS RUN. That is
            // a fact about the world, not about the last walk, and re-learning it costs a whole
            // walk each time — the 04:xx run failed four consecutive targets at corner 2 because
            // every route from that spot ran through the same blocked waypoint.
            this.farmWalkExcludedNodes.Clear();
            this.PruneFarmWalkBlockedNodes();
            foreach (KeyValuePair<int, float> blocked in this.farmWalkBlockedGraphNodes)
            {
                this.farmWalkExcludedNodes.Add(blocked.Key);
            }

            // Each walk gets its own budget of futile rebuilds. Carrying the tally over meant the
            // third target was abandoned after ONE rebuild and the fourth after one more, which is
            // what turned "try another node" into "teleport" so quickly.
            this.farmWalkFutileRepaths = 0;

            // A retry avoids the final waypoint whose approach already failed; every other walk
            // starts with no end-side restriction.
            this.farmWalkExcludedEndNodes.Clear();
            if (string.Equals(label, "node:retry", StringComparison.Ordinal) && this.farmWalkRetryAvoidEndNode >= 0)
            {
                this.farmWalkExcludedEndNodes.Add(this.farmWalkRetryAvoidEndNode);
                this.farmWalkRetryAvoidEndNode = -1;
            }

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

            // ⚠️ Every route-build failure from here on is logged UNCONDITIONALLY. They used to be
            // silent — this one entirely, the two in TryBuildFarmWalkRoute behind AutoFarmLog — so
            // "the walk simply never started" had no explanation anywhere. Quest Walk summoned a
            // vehicle, printed nothing else, and went nowhere, and the log could not say which of
            // the four causes it was.
            if (!this.EnsureTrackPathGraph())
            {
                ModLogger.Msg("[FarmWalk] " + label + ": no waypoint graph (not built or unresolvable) — cannot route.");
                return false;
            }

            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                ModLogger.Msg("[FarmWalk] " + label + ": self position unresolved — cannot route.");
                return false;
            }

            // Set BEFORE the route build, not after: the build's own failure lines print it, and
            // assigning it later made them name the PREVIOUS walk's target.
            this.farmWalkLabel = label ?? string.Empty;

            // Fresh walk, so the detour search is allowed once more.
            this.farmWalkDetourSearchDone = false;
            this.farmWalkDescentDeferGivenUp = false;
            this.ResetFarmWalkEdgeAudit();
            this.farmWalkBubbleNextResyncAt = 0f;
            this.farmWalkBubbleTouchingSince = -1f;
            this.farmWalkBubbleId = 0;
            this.farmWalkBubbleNextLogAt = 0f;
            this.farmWalkBubbleDrift = 0f;
            this.farmWalkDepthAssertFrom = float.NaN;

            // Vehicle for any long haul, not just a zone move. The node hops on this map run
            // 77-104 m routinely, which is the same distance the vehicle was added for — the only
            // thing that made a zone move special was that it happened to be the case asked for
            // first. ShouldFarmWalkSummonVehicle owns every precondition (option on, far enough,
            // on land, not already riding), so calling it here covers node walks, retries,
            // priority nodes and zone hauls from one place.
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
            this.farmWalkActive = true;
            this.farmWalkStartedAt = Time.unscaledTime;
            this.farmWalkDropAttempts = 0;
            this.farmWalkDropUntil = 0f;
            this.farmWalkDropAirborne = false;
            this.farmWalkStuckStrikes = 0;
            this.farmWalkLastSample = selfPos;
            this.farmWalkNextStuckSampleAt = Time.unscaledTime + FarmWalkStuckSampleInterval;
            this.farmWalkNextRepathAt = Time.unscaledTime + FarmWalkRepathInterval;
            this.farmWalkLastRepathAt = Time.unscaledTime;
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
            this.farmWalkBackOffRound = 0;   // next walk starts its alternation at "rise" again
            this.farmWalkBackOffVerticalDir = 1;
            this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
            this.farmWalkProbeUsed = false;   // one probe sweep per walk
            this.farmWalkHeightRefined = false; // one height refine per walk
            this.farmWalkTargetSeenAt = -1f;  // "seen present" is per-walk evidence, never carried over
            this.farmWalkApproachScanForced = false; // one forced verification scan per walk
            this.farmWalkHopBurstsUsed = 0;
            this.farmWalkEscapePressBarren = false;
            this.farmWalkEverAdvanced = this.farmWalkCornerIndex > 0;
            this.farmWalkPrevVerticalHeld = this.farmWalkVerticalHeld;

            // Summon AFTER the route is committed, never before.
            //
            // It used to run ahead of TryBuildFarmWalkRoute, so a route that could not be built
            // still left the player sitting in a freshly-summoned car with no walk to drive. Quest
            // Walk showed it plainly: "summoned 81104 and took the seat" with no "walking Nm via N
            // corners" line after it, three times in a row as the retry re-summoned each attempt —
            // the hotkey put the player in a vehicle that then went nowhere.
            //
            // The vehicle is transport for a journey; whether the journey exists is decided first.
            if (!alreadyInRange && this.ShouldFarmWalkSummonVehicle(selfPos, target))
            {
                this.TryFarmWalkSummonAndMount(); // failure means "walk it", never "abort"
            }

            // Generous deadline: straight-line metres at the configured speed, tripled for detours,
            // plus a fixed allowance. Walking is meant to be slow; this only catches a wedge.
            float straight = HorizontalDistance(selfPos, target);
            this.farmWalkDeadline = Time.unscaledTime + Mathf.Clamp(straight * 3f / FarmWalkSpeedMax + 15f, 20f, 300f);

            // Always logged, not behind the AutoFarmLog flag: one line per node hop (the same
            // cadence as [ForagingTp]) is what makes a wedged walk diagnosable at all — the first
            // build's freeze was invisible precisely because this line was flag-gated off.
            // Diagnostic: ask the game to route to the same node itself (a no-op while the
            // Compare Game Track switch is off).
            this.RequestGameTrackForWalk(target);

            ModLogger.Msg("[FarmWalk] " + label + ": walking " + straight.ToString("F1") + "m via "
                + this.farmWalkCorners.Count + " corners, starting at corner " + this.farmWalkCornerIndex
                + ", target=" + FormatNavMeshVector(target)
                + (alreadyInRange ? " (already in range)" : string.Empty) + ".");
            return true;
        }

        // The direct line is blocked, so find a route that actually goes AROUND rather than through.
        //
        // A* already connects the graph itself; what is missing is a pair of end nodes whose
        // off-graph legs are clear. Walk outwards through the nearest candidates for the end node,
        // rebuild, and take the first route whose final leg (last waypoint -> resource) is clear.
        // Bounded: each attempt is an A* plus a linecast pair, and this runs on a re-path timer.
        private bool TryBuildDetouredFarmWalkRoute(Vector3 from, Vector3 to, int startIndex, int endIndex)
        {
            this.farmWalkDetourExcluded.Clear();
            if (this.farmWalkExcludedEndNodes != null)
            {
                foreach (int excluded in this.farmWalkExcludedEndNodes)
                {
                    this.farmWalkDetourExcluded.Add(excluded);
                }
            }

            this.farmWalkDetourExcluded.Add(endIndex);

            for (int attempt = 0; attempt < FarmWalkDetourAttempts; attempt++)
            {
                if (!this.TryFindReachableTrackGraphNode(to, FarmWalkGraphSnapRadius, out int altEnd,
                        this.farmWalkDetourExcluded, "detour-end"))
                {
                    return false;
                }

                this.farmWalkDetourExcluded.Add(altEnd);

                // A SEPARATE buffer, never farmWalkScratchCorners. TryComputeTrackGraphPath clears
                // its output up front, so probing into the scratch list destroyed the route the
                // caller was about to commit: after four failed attempts the walk was handed the
                // LAST rejected candidate instead of its own route. That is what turned an 18.8 m
                // 2-corner walk into "timed out 13,1m short [corner 1/8]".
                if (!this.TryComputeTrackGraphPath(startIndex, altEnd, this.farmWalkDetourCorners))
                {
                    continue;
                }

                this.farmWalkDetourCorners.Add(to);
                if (this.farmWalkDetourCorners.Count < 2)
                {
                    continue;
                }

                // Passable, not All. The final leg runs 15-20 m from a waypoint to a resource, both
                // at ground level: against All (which includes Ground) it is blocked by definition,
                // which is why all four attempts failed every single time.
                Vector3 lastWaypoint = this.farmWalkDetourCorners[this.farmWalkDetourCorners.Count - 2];
                if (!this.IsFarmWalkLineClear(lastWaypoint, to, this.farmWalkMaskPassable))
                {
                    continue;
                }

                // ⚠️ AUDIT THE DETOUR — IT IS THE ONE ROUTE THAT NEEDS IT MOST.
                //
                // Everything above tested exactly one leg: the last waypoint to the target. The leg
                // the swimmer ACTUALLY takes first, from where they stand to corner 0, was never
                // looked at — in a route whose entire reason for existing is that the straight line
                // is blocked. And committing here returns straight out of the route builder, so the
                // normal edge audit further down never sees a detour either.
                //
                // Measured 02:42:52, a Glasswort 2.6 m away: beeline correctly refused, detour built,
                // first corner 10 m below through a wall, swimmer drove into it and repeated every
                // four seconds. A 10 m dive is perfectly normal — the wall in front of it was not.
                //
                // includeFirstLeg: true, because leg 0 is precisely the question here. The audit
                // still refuses to BAN a waypoint on leg 0 (rule 1.6); testing and banning are
                // different things.
                int detourBlocked = this.FindFirstImpassableFarmWalkLeg(from, this.farmWalkDetourCorners,
                    out string detourWhy, includeFirstLeg: true);
                if (detourBlocked >= 0)
                {
                    ModLogger.Msg("[FarmWalk] detour attempt " + (attempt + 1) + " rejected: leg "
                        + detourBlocked + " — " + detourWhy + ".");
                    continue;
                }

                // Do NOT shortcut a route that only exists because the straight line was blocked —
                // collapsing it puts the wall right back in the middle.
                this.farmWalkCorners.Clear();
                this.farmWalkCorners.AddRange(this.farmWalkDetourCorners);
                this.farmWalkEndNodeIndex = altEnd;
                this.farmWalkStartNodeIndex = startIndex;

                // Deliberately corner 0, not the usual "skip what is already behind me" pass: the
                // first corner is the whole point of a detour. Skipping it aims straight at the
                // target again, which is the beeline this route exists to avoid.
                this.farmWalkCornerIndex = 0;
                this.farmWalkLegStart = from;
                ModLogger.Msg("[FarmWalk] detour found via " + this.farmWalkCorners.Count
                    + " corners (attempt " + (attempt + 1) + "), shortcutting skipped.");
                return true;
            }

            return false;
        }

        // Snap both ends onto the graph, A*, then append the true target as the final corner (the
        // game's own GetPath2 does exactly this, so the last leg leaves the graph and ends on the
        // resource). A start node that is already behind us is dropped so the first step is forward.
        // Drop bans whose time is up. Called before every merge, so an expired one never reaches
        // the snap's exclusion set.
        private void PruneFarmWalkBlockedNodes()
        {
            if (this.farmWalkBlockedGraphNodes.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            this.farmWalkBlockedScratch.Clear();
            foreach (KeyValuePair<int, float> entry in this.farmWalkBlockedGraphNodes)
            {
                if (now >= entry.Value)
                {
                    this.farmWalkBlockedScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < this.farmWalkBlockedScratch.Count; i++)
            {
                this.farmWalkBlockedGraphNodes.Remove(this.farmWalkBlockedScratch[i]);
            }

            if (this.farmWalkBlockedScratch.Count > 0)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkBlockedScratch.Count
                    + " waypoint ban(s) expired, " + this.farmWalkBlockedGraphNodes.Count + " still banned.");
            }
        }

        // Builds into a SCRATCH list and only commits on success. A mid-walk re-path that fails
        // must leave the route we are already following untouched — TryComputeTrackGraphPath clears
        // its output list up front, so writing straight into farmWalkCorners would empty the route
        // and the very next corner read would be out of range.
        private bool TryBuildFarmWalkRoute(Vector3 from, Vector3 to)
        {
            // ⚠️ BEFORE THE SNAP, NOT AFTER. Underwater a straight swim needs no graph node at all,
            // and the snap is exactly what fails there: 86 nodes to cover a whole sea floor, so
            // "no reachable graph node within 60m" refuses routes that a fifteen-metre swim would
            // have finished. Asking first also skips an A* whose answer we were going to discard.
            if (this.IsFarmWalkDirectSwimClear(from, to, out string swimWhy))
            {
                this.farmWalkCorners.Clear();
                this.farmWalkCorners.Add(to);
                this.farmWalkCornerIndex = 0;

                // ⚠️ THE CORRIDOR NEEDS A LEG START. Without it farmWalkLegStart keeps whatever the
                // PREVIOUS route left there — a point that may be tens of metres away — and the
                // corridor test then measures the swimmer against a line they were never on. It
                // fired every single second:
                //     swimming straight there — clear over 14,5m
                //     re-pathed (off corridor): 1 -> 1 corners, now at 0 (IDENTICAL)
                // Identical every time, so nothing changed; but each of those counts as a futile
                // re-path, and three futile re-paths ban a waypoint. That is where the underwater
                // bans came from — seventeen of them, from a corridor test aimed at a stale point.
                this.farmWalkLegStart = from;
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": swimming straight there — "
                    + swimWhy + ", no graph needed.");
                return true;
            }

            // ⚠️ SAY WHY IT WAS REFUSED. Without this the graph gets the job back in silence, and
            // when the graph then fails the log reads as an unexplained teleport:
            //     A* found no route between waypoints 83 and 67 — cannot route.
            //     [FarmTeleport] node:Glasswort -> (-8.7, -41.0, -102.2)
            // with nothing anywhere saying that a 38 m straight swim had been considered first.
            if (this.farmWalkIsSwimming && swimWhy.Length > 0)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": not swimming straight there — "
                    + swimWhy + "; trying the graph.");
            }

            if (!this.TryFindReachableTrackGraphNode(from, FarmWalkGraphSnapRadius, out int startIndex,
                    this.farmWalkExcludedNodes, "start")
                || !this.TryFindReachableTrackGraphNode(to, FarmWalkGraphSnapRadius, out int endIndex,
                    this.farmWalkExcludedEndNodes, "end"))
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": no reachable graph node within "
                    + FarmWalkGraphSnapRadius.ToString("F0") + "m of " + FormatNavMeshVector(from)
                    + " or " + FormatNavMeshVector(to) + " — cannot route.");
                return false;
            }

            if (!this.TryComputeTrackGraphPath(startIndex, endIndex, this.farmWalkScratchCorners,
                    this.farmWalkExcludedNodes))
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": A* found no route between waypoints "
                    + startIndex + " and " + endIndex + " — cannot route.");
                return false;
            }

            this.farmWalkScratchCorners.Add(to);

            // Degenerate route = a beeline, and a beeline is exactly "a wall in the middle".
            //
            // Most farm hops are 5-20 m, shorter than the spacing of the waypoint graph, so both
            // ends snap to the SAME node and A* returns a single point. Append the target and the
            // route is [node, target] — two corners, i.e. walk straight at it, with nothing between
            // the player and the resource ever checked against geometry.
            //
            // The game refuses to do this. GetPath only emits its 2-point straight path when
            //     !PhysicsExtension.Linecast(start, end - horizontalOffset, All)
            // says the direct line is clear; otherwise it falls through to the spline over the
            // graph however long that turns out to be. Copy that test verbatim — same layer mask,
            // same 0.5 m pull-back at the target end, single ray at foot height, no lift.
            // Only on the INITIAL build, never on a re-path. Re-running it every 1.5 s produced the
            // wedge the 21:53 run recorded: each detour reset the corner index to 0, the player
            // turned back toward the first corner, the corridor test failed, and it re-pathed again
            // — 45 s of walking away from the node while the log printed the same six lines.
            if (!this.farmWalkActive && !this.farmWalkDetourSearchDone
                && this.farmWalkScratchCorners.Count < 3 && this.EnsureFarmWalkLinecast())
            {
                this.farmWalkDetourSearchDone = true;

                // Lift both ends off the floor. The game's own test is
                //     PhysicsExtension.Linecast(start + startOffset, end + endOffset - pullBack, All)
                // and those two config offsets are the whole reason it works: `All` includes the
                // Ground layer, so a ray between two points AT ground level hits terrain on any
                // slope and reports every straight line as blocked. That is exactly what happened —
                // "direct line blocked" fired on open beach, 8.9 m, then 11.8, then 13.8, while the
                // player walked backwards away from the oyster.
                Vector3 probeFrom = from;
                Vector3 pullBack = (to - from).normalized * 0.5f;
                Vector3 probeEnd = to - new Vector3(pullBack.x, 0f, pullBack.z);
                probeFrom.y += FarmWalkShortcutProbeLift;
                probeEnd.y += FarmWalkShortcutProbeLift;
                if (!this.IsFarmWalkRayClear(probeFrom, probeEnd, this.farmWalkMaskAll))
                {
                    ModLogger.Msg("[FarmWalk] direct line to the node is blocked ("
                        + HorizontalDistance(from, to).ToString("F1")
                        + "m) — refusing the beeline, routing through the graph instead.");
                    if (this.TryBuildDetouredFarmWalkRoute(from, to, startIndex, endIndex))
                    {
                        return true;
                    }

                    // No graph detour available either. Keep the straight route — the walker's
                    // stuck ladder will deal with it — but the log now says why it will struggle.
                    ModLogger.Msg("[FarmWalk] no graph detour available; keeping the blocked straight route.");
                }
            }

            // ⚠️ ASK THE WORLD BEFORE COMMITTING. A* over the waypoint graph optimises DISTANCE and
            // knows nothing about walls — the graph describes the game's Track line, not walkability.
            // Measured 2026-08-21 with the GeoProbe route audit: a two-leg route to an oyster whose
            // second leg needed a 2.65 m step up, against a jump that peaks at 1.42 m. Both the
            // point-by-point surface profile and the game's own sphere sweep called it blocked; the
            // builder had never asked either of them.
            //
            // One sweep per leg, then ban the waypoint the blocked leg ends at and let A* find
            // another way round. Bounded to FarmWalkEdgeAuditRetries: this is a sanity check on a
            // route, not a search. Dry land only, and silent when the oracle does not answer — see
            // FarmWalkGeometryFeature for why both of those matter.
            for (int audit = 0; audit < FarmWalkEdgeAuditRetries; audit++)
            {
                int blockedLeg = this.FindFirstImpassableFarmWalkLeg(from, this.farmWalkScratchCorners,
                    out string blockedDetail);
                if (blockedLeg < 0)
                {
                    if (blockedDetail.Length > 0)
                    {
                        ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + blockedDetail + ".");
                    }

                    break;
                }

                if (!this.TryBanFarmWalkWaypointAt(this.farmWalkScratchCorners[blockedLeg], blockedDetail))
                {
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + blockedDetail
                        + " is not passable and there is no waypoint left to ban — keeping the route, "
                        + "the escape ladder will have to deal with it.");
                    break;
                }

                // ⚠️ KEEP A COPY FIRST. TryComputeTrackGraphPath CLEARS its output list before it
                // searches, so a failed retry does not leave the previous route in place — it leaves
                // NOTHING, and the empty list is then committed and read out of range on the next
                // corner. The same trap is called out at the top of this method for a different
                // caller; a retry loop walks straight into it.
                this.farmWalkEdgeAuditBackup.Clear();
                this.farmWalkEdgeAuditBackup.AddRange(this.farmWalkScratchCorners);

                // Re-snap as well as re-route: the ban may have taken the START node off the table.
                if (!this.TryFindReachableTrackGraphNode(from, FarmWalkGraphSnapRadius,
                        out int reStart, this.farmWalkExcludedNodes, "start")
                    || !this.TryFindReachableTrackGraphNode(to, FarmWalkGraphSnapRadius,
                        out int reEnd, this.farmWalkExcludedEndNodes, "end")
                    || !this.TryComputeTrackGraphPath(reStart, reEnd, this.farmWalkScratchCorners,
                        this.farmWalkExcludedNodes))
                {
                    this.farmWalkScratchCorners.Clear();
                    this.farmWalkScratchCorners.AddRange(this.farmWalkEdgeAuditBackup);
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel
                        + ": no route left after the ban — keeping the blocked one, the escape ladder "
                        + "will have to deal with it.");
                    break;
                }

                this.farmWalkScratchCorners.Add(to);
            }

            // Straighten the graph path before committing it.
            this.ShortcutFarmWalkRoute(from, this.farmWalkScratchCorners);

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
            this.farmWalkEndNodeIndex = endIndex;
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

            // FIRST, before anything else this tick decides.
            //
            // ⚠️ IT USED TO SIT FURTHER DOWN, just before the progress sampler, and that is the same
            // as not running: a dozen early returns above it handle arrival, steering, stall
            // recovery and re-pathing, so on a walk that is fighting geometry the check is reached
            // on almost no ticks at all. Measured 2026-08-19 — a retry walk spent 16 s covering
            // 30 m to a node that was COLD at selection and still COLD on arrival, through jumps,
            // an unreachable start waypoint and three re-paths, and the guard never once ran.
            //
            // "Is this target still worth walking to" does not depend on any of that machinery, so
            // it is answered before it.
            // Underwater only: a long swim can sail past something nearer than where it is going.
            if (this.TryRetargetNearerSwimNode(selfPos, Time.unscaledTime))
            {
                return true;
            }

            if (this.TryAbandonDrainedFarmWalkTarget(selfPos))
            {
                return true;
            }

            float now = Time.unscaledTime;
            float remaining = HorizontalDistance(selfPos, this.farmWalkTarget);

            float distance3D = Distance3D(selfPos, this.farmWalkTarget);

            // ⚠️ THE COLLECT RADIUS IS THE DESTINATION, NOT THE NODE.
            //
            // The walker used to close to FarmWalkCollectDistance (0.25 m) before calling it an
            // arrival, which is a target the locomotion cannot hold: it overshoots, gets turned
            // around, and the last metre becomes the expensive part of every walk — the same last
            // metre that produced the depth limit cycle and the four-leg probes underwater.
            //
            // The aura fires out to FarmWalkAuraReach, so anything inside it collects exactly as
            // well as standing on top of the node. Standoff aims (contamination) and bubbles keep
            // their own rules below — one is deliberately offset, the other is caught rather than
            // approached.
            if (this.farmWalkAimOffsetY == 0f
                && !this.FarmWalkTargetIsBubble
                && distance3D <= this.ResolveFarmWalkCollectStandoff())
            {
                this.FinishFarmWalk("arrived " + remaining.ToString("F2") + "m horizontally ("
                    + distance3D.ToString("F2") + "m 3-D) — inside the "
                    + this.ResolveFarmWalkCollectStandoff().ToString("0.0#") + "m stand-off for this kind,"
                    + " not walking further in",
                    teleport: false);
                return true;
            }

            // ⚠️ A DROP IN FLIGHT OWNS THE TICK. Without this the arrival tests run again on the
            // very next frame, find the height still unclosed — of course they do, the body has not
            // fallen yet — and spend the next attempt. Both attempts burned inside one second:
            //     02:21:06  the node is 3,8m below ... walking off the edge to drop onto it (1/2).
            //     02:21:06  the node is 3,8m below ... walking off the edge to drop onto it (2/2).
            // A budget of two is a budget of two SECONDS, not two frames.
            if (now < this.farmWalkDropUntil)
            {
                // Closed the height on the way down? Then the drop is done and the ordinary arrival
                // test should have this frame, not the remainder of the window.
                if (Mathf.Abs(this.farmWalkTarget.y - selfPos.y) <= this.ResolveFarmWalkCollectStandoff())
                {
                    this.farmWalkDropUntil = 0f;
                    this.farmWalkDropAirborne = false;
                }
                else
                {
                    return false;
                }
            }

            // ⚠️ A BUBBLE IS NOT "ARRIVED AT", IT IS POPPED.
            //
            // Measured through the bridge, one bubble sampled twice a few seconds apart:
            //     (80.82, -30.62, -77.87) -> (77.47, -30.62, -83.73)   6.75 m of XZ, dy exactly 0
            // It drifts at roughly swimming speed and holds its depth. Against that, the 0.25 m
            // horizontal arrival gate is unreachable — the walker would circle it until the walk
            // timed out — and the coordinate the farm planned against is metres stale by arrival
            // (the 17:35 run aimed at (70.85, -30.62, -80.71) for the bubble now six metres away).
            //
            // So the proof of arrival is the bubble VANISHING: swim into it and let the touch pop
            // it. The radius below only keeps the walker on top of it; the grace is the backstop
            // for a bubble that will not pop, and hands over to the dwell, which has its own
            // completion test and its own cap.
            if (this.FarmWalkTargetIsBubble)
            {
                // IDENTITY, NOT PROXIMITY. "No bubble at the place we stopped" is true of every
                // bubble a second after we stop, because it drifts - and the dwell used exactly
                // that test, so it reported four collects in one minute that never happened. The
                // only question that means anything is whether THIS bubble still exists.
                if (this.farmWalkBubbleId != 0 && !this.IsBubbleStillLive(this.farmWalkBubbleId))
                {
                    this.FinishFarmWalk("bubble popped after "
                        + this.farmWalkBubbleDrift.ToString("F1") + "m of chasing", teleport: false);
                    return true;
                }

                // Still there, so keep swimming into it. The dwell cannot chase, so handing over
                // while the bubble is alive just parks the player and lets it drift away. The touch
                // timer only decides when to call this one uncatchable and move on.
                if (distance3D <= FarmWalkBubbleTouchDistance)
                {
                    if (this.farmWalkBubbleTouchingSince < 0f)
                    {
                        this.farmWalkBubbleTouchingSince = now;
                    }
                    else if (now - this.farmWalkBubbleTouchingSince >= FarmWalkBubbleTouchGrace)
                    {
                        this.FinishFarmWalk("inside " + distance3D.ToString("F2") + "m of the bubble for "
                            + FarmWalkBubbleTouchGrace.ToString("F1")
                            + "s and it is STILL THERE - this one will not pop from here",
                            teleport: false);
                        return true;
                    }
                }
                else
                {
                    this.farmWalkBubbleTouchingSince = -1f;
                }

                // AND THEN FALL THROUGH. This block DECIDES ARRIVAL; it is not the tick. An early
                // return here skipped everything below it - the chase itself, the depth drive, the
                // steering, the corner advance, the re-path - so a bubble walk sat completely inert
                // and the farm moved on eleven seconds later without one line in between. The
                // generic arrival test is already gated off for bubbles further down; that gate is
                // the whole mechanism, and nothing else here may be skipped.
            }

            // ⚠️ AND THE ORDINARY ARRIVAL MUST NOT PRE-EMPT IT. The block above only ENDS a bubble
            // walk on its own terms (popped, or held on it long enough); otherwise it falls through
            // — and 0.25 m of a still-drifting bubble satisfied the generic test in the very frame
            // the walker got there, so the grace never once elapsed:
            //     node:Bubble: arrived 0,21m horizontally (0,22m 3-D) from the aim point
            //     AutoFarm: Bubble dwell capped after 6,0s (marker still present)
            // A bubble is finished by popping, not by proximity.
            if (remaining <= FarmWalkCollectDistance && !this.FarmWalkTargetIsBubble)
            {
                // Two different questions, so two different tolerances.
                //
                // Plain node: FarmWalkArrivalDistance in 3-D — "arrived" means genuinely within
                // half a metre of the resource in every axis, not half a metre in the plane with
                // up to 1.8 m of unclosed height.
                //
                // Standoff (contamination): the aura's reach does not apply — the repair
                // kit is thrown, the aim point is deliberately 3 m off the node, and being closer
                // is not better. But "no check at all" was wrong too: the bypass short-circuited on
                // farmWalkAimOffsetY alone, and distance3D is measured against the AIM POINT (not
                // the node, which the old comment here got wrong), so arrivals were accepted on
                // horizontal distance only. The remote run logged "arrived 0,21m horizontally
                // (7,38m 3-D) [standoff +3m]" and again at 10,79m — directly over the node but ten
                // metres off the point we meant to throw from.
                //
                // ⚠️ FarmWalkStandoffArrivalDistance is a JUDGEMENT CALL, not a measured value: the
                // repair throw's real range is unknown. It is set wide enough to keep the ~3.7 m
                // arrivals that the same run collected successfully, and tight enough to reject the
                // 7-11 m ones. If contamination starts failing, this is the number to revisit.
                float arrivalTolerance = this.farmWalkAimOffsetY != 0f
                    ? FarmWalkStandoffArrivalDistance
                    : FarmWalkArrivalDistance;
                if (distance3D <= arrivalTolerance)
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
                    // ...but "not as close as we wanted" is not the same as "cannot collect".
                    //
                    // The aura measures 3-D, so a node a metre below the player with the
                    // horizontal closed is still within reach. Failing it throws away a resource the
                    // aura would have taken — and the 04:59 run did exactly that on Penny Bun and
                    // Ore at dy = -1.1 to -1.5 m, every one of them inside the radius.
                    //
                    // FarmWalkArrivalDistance stays the target the walker aims for; this is the
                    // wider "the collect will still work from here" fallback that decides whether
                    // to give up. Logged distinctly so the compromise is never invisible.
                    // ⚠️ THE LEARNED STAND-OFF APPLIES HERE TOO (rule 0.4a). This fallback kept the
                    // global 1.4 m while the walk had already proved that this kind does not collect
                    // from 1.1 m — so it accepted an arrival at 1,37 m 3-D and called it done:
                    //     arrived 0,24m horizontally (1,37m 3-D, dy=-1,3m) — still inside the aura reach
                    // A kind with a tight stand-off gets the tight number here as well; anything
                    // wider is an arrival that collects nothing.
                    float reachHere = Mathf.Min(FarmWalkAuraReachFallback,
                        this.ResolveFarmWalkCollectStandoff() + FarmWalkAuraReachFallback
                            - FarmWalkCollectStandoff);
                    if (distance3D <= reachHere)
                    {
                        this.FinishFarmWalk("arrived " + remaining.ToString("F2") + "m horizontally ("
                            + distance3D.ToString("F2") + "m 3-D, dy="
                            + (this.farmWalkTarget.y - selfPos.y).ToString("F1")
                            + "m) — height not closed, but still inside the "
                            + reachHere.ToString("0.0#") + "m reach for this kind",
                            teleport: false);
                        return true;
                    }

                    // ⚠️ BELOW IS NOT A DEAD END — WALK OFF THE EDGE.
                    //
                    // This branch used to give up on anything it could not close vertically, in
                    // either direction. Upwards that is right: 3.6 m of climb is beyond every action
                    // the walker owns. Downwards it is nonsense — a player gets down by stepping off
                    // and falling, which costs nothing, and the walker simply had no action for it:
                    //     above the node but 3,7m away vertically (dy=-3,6m) — skipping (1/3)
                    //     above the node but 1,6m away vertically (dy=-1,5m) — skipping (2/3)
                    // The second of those is a knee-high step down, refused outright.
                    //
                    // The horizontal is already closed here, so steering at the target gives a delta
                    // of nothing — there is no direction left in it. The direction that leaves a
                    // ledge is the one we ARRIVED along: keep going, and the ground runs out.
                    // ⚠️ THE DROP OUTRANKS THE ESCAPE HERE — IT DOES NOT WAIT FOR IT.
                    //
                    // Two mistakes were made in a row on this. First the drop ran interleaved with a
                    // hop burst, and worked only by coincidence: its direction never reached the
                    // axis (steering is gated off during an escape), the fall happened because the
                    // escape's own jump pushed at the aim, and the escape scored the fall as its own
                    // result. Then that was "fixed" by refusing to drop while an escape runs — which
                    // killed the feature outright, because the stuck detector fires first and the
                    // escape is always already running by the time this branch is reached:
                    //     02:39:41  wedged at 2,7m on foot — escape 1/3
                    //     02:39:41  above the node but 3,8m away vertically — skipping (1/3)
                    //
                    // One owner per frame is the rule (0.3); which owner is the question. A hop
                    // burst is for horizontal obstacles and has nothing to offer against a node
                    // three metres BELOW, so the escape is ended and the drop takes the axis.
                    if (this.farmWalkTarget.y < selfPos.y
                        && this.farmWalkDropAttempts < FarmWalkMaxDropAttempts)
                    {
                        if (this.farmWalkUnstickPhase != FarmWalkUnstickIdle)
                        {
                            this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                            this.TryClearGameMoveAxis();
                        }

                        this.farmWalkDropAttempts++;
                        this.farmWalkDropUntil = now + FarmWalkDropSeconds;
                        this.farmWalkDropFrom = selfPos;

                        Vector3 approach = this.farmWalkTarget - this.farmWalkLegStart;
                        approach.y = 0f;
                        this.farmWalkDropDir = approach.sqrMagnitude > 0.0001f
                            ? approach.normalized
                            : Vector3.forward;

                        ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": the node is "
                            + (selfPos.y - this.farmWalkTarget.y).ToString("F1")
                            + "m below and the horizontal is closed — walking off the edge to drop onto it ("
                            + this.farmWalkDropAttempts + "/" + FarmWalkMaxDropAttempts + ").");
                        return false;
                    }

                    // ⚠️ SAY WHICH WAY. "under the node" is written into the message, but dy is
                    // signed and the case that produced it read dy=-3,6 — the node was three and a
                    // half metres BELOW the player, who was standing on a ledge above it. A message
                    // that names the wrong side of the problem sends the reader looking for a climb
                    // when the walker needed a way DOWN.
                    float dyToNode = this.farmWalkTarget.y - selfPos.y;
                    this.FinishFarmWalk((dyToNode >= 0f ? "under the node but " : "above the node but ")
                        + distance3D.ToString("F1") + "m away vertically (dy="
                        + dyToNode.ToString("F1") + "m)", teleport: true);
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
            this.ProcessFarmWalkTrackCompare();

            // Decide the axis order for this frame (see FarmWalkDescendHoldDistance). Suspended
            // entirely during an unstick, which owns both axes while it runs.
            // Same aim the depth control uses, so the defer / climb-hold decisions agree with it.
            // Before anything reads the aim: a bubble target has moved since the last frame.
            this.UpdateFarmWalkBubbleChase(selfPos, now);

            float dyNow = this.ResolveFarmWalkDepthAim().y - selfPos.y;
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
            // ⚠️ Only SMALL descents get deferred.
            //
            // Holding the whole descent until the last 4 m keeps the traverse at the depth we
            // happen to be at, and if the node sits far below, that depth is inside the terrain
            // between us and it. The run logged exactly that: "corner=16,5m target=16,5m dy=-13,5m
            // held=0 gameVerticalInput=0,00" — thirteen metres to drop, zero dive input, the walker
            // pushing horizontally into a wall until its four unstick rounds ran out.
            //
            // A deep node wants a DIAGONAL: descend while travelling, so the approach arrives at
            // roughly the right depth. Only shallow drops are worth flattening out to keep the
            // traverse off the floor, which is what the defer was for in the first place.
            //
            // Entering the deferral takes a drop clearly INSIDE the ceiling; leaving it takes one
            // clearly past it. Straddling the line is what pinned the walker (see the constant).
            if (unsticking)
            {
                this.farmWalkDescentDeferGivenUp = true;
            }

            float deferCeiling = this.farmWalkDeferDescent
                ? FarmWalkMaxDeferredDescent + FarmWalkDeferDescentHysteresis
                : FarmWalkMaxDeferredDescent - FarmWalkDeferDescentHysteresis;
            bool deferrableDrop = dyNow > -deferCeiling;
            this.farmWalkDeferDescent = deferrableDrop
                && this.farmWalkIsSwimming
                && !unsticking
                && !this.farmWalkDescentDeferGivenUp
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
                // The gap the hold is actually working on — the corner's, not the node's. Adding
                // the node's would keep 30 m of not-yet-relevant depth in the metric for the whole
                // route, so closing an intermediate corner would barely register as progress.
                routeRemaining += Mathf.Abs(dyNow);
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
            // ⚠️ A DELIBERATE HOLD IS NOT A STALLED WALK.
            //
            // Auto Repair throws a kit and the walker SINKS ONTO IT on purpose, then stands in the
            // aura for as long as the repair takes. Nothing was telling the progress timer about it,
            // so the timer aged through the whole hold and fired at the end of it:
            //     00:49:37  repair kit thrown — sinking onto it to enter the repair aura.
            //     00:49:55  surfacing 2,9m (last hold moved +2,90m in 18,3s)
            //     00:49:55  stopped closing, 10,5m of route left — taking the rescue teleport.
            // Eighteen seconds of doing exactly what it was told, punished with the loudest thing
            // the mod can do. The same re-basing an unstick gets applies here for the same reason.
            bool repairHold = this.farmRepairAuraHoldSince >= 0f;
            bool dropping = now < this.farmWalkDropUntil;
            bool clearingObstacle = this.farmWalkUnstickPhase != FarmWalkUnstickIdle || repairHold || dropping;
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
                else if (this.farmWalkHopBurstsUsed < FarmWalkMaxHopBursts && !this.farmWalkIsSwimming)
                {
                    // Same reason as the other two sites: a lone impulse under a held axis does not
                    // lift the body. Underwater keeps its own ladder, which was measured separately.
                    this.BeginFarmWalkHopBurst(selfPos, now,
                        Distance3D(selfPos, this.farmWalkTarget), this.farmWalkTarget);
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

                // Real progress along the route — the futile-rebuild tally starts over.
                this.farmWalkFutileRepaths = 0;
            }

            if (this.farmWalkCornerIndex >= this.farmWalkCorners.Count)
            {
                this.FinishFarmWalk("ran out of corners " + remaining.ToString("F1") + "m short", teleport: true);
                return true;
            }

            Vector3 corner = this.farmWalkCorners[this.farmWalkCornerIndex];

            // THE ROUTE IS PINNED. Re-path only on a real cause, never merely because time passed.
            //
            // This used to rebuild from scratch every 1.5 s unconditionally — around forty times
            // over a minute-long walk. Each rebuild re-snaps both ends, so it can land on different
            // graph nodes and hand back a different chain of corners at different heights. On land
            // that mostly went unnoticed; underwater it is visible as a vertical flip-flop, because
            // the depth control follows the current corner:
            //     diving 17,6m / surfacing 1,1m / diving 17,6m / surfacing 2,1m ...
            // — the aim alternating between two corners of two different routes, the player bobbing
            // in place. "It circles, the route keeps rebuilding" is exactly this.
            //
            // Three real causes remain, and the safety cadence is now long enough to be a backstop
            // rather than a driver:
            //   * off the corridor  — pushed aside, shoved off a ledge, route genuinely stale;
            //   * not closing       — the route may be the problem, so a fresh one is worth trying;
            //   * long safety timer — never re-pathing at all would be its own trap.
            bool offCorridor = DistanceToWalkLeg(selfPos, this.farmWalkLegStart, corner) > FarmWalkCorridorTolerance;
            bool notClosing = now - this.farmWalkBestAt >= FarmWalkNoClosingTimeout * 0.5f;
            bool safetyDue = now >= this.farmWalkNextRepathAt;

            // ⚠️ NEVER REBUILD THE ROUTE UNDER A RUNNING ESCAPE. The escape captured its aim when it
            // began and measures every hop against that point; swapping the corner list mid-flight
            // moves the goalposts and resets the corner index, so a sequence that was gaining metres
            // suddenly reads as going nowhere. One run rebuilt twice inside a single escape and then
            // gave up on a node the escape had just brought fifteen metres closer.
            //
            // The escape is bounded — FarmWalkEscapeBudget, and a per-heading cap inside it — so
            // waiting it out costs at most a few seconds, and the safety cadence fires the moment it
            // hands back.
            if (this.farmWalkUnstickPhase != FarmWalkUnstickIdle)
            {
                offCorridor = false;
                notClosing = false;
                safetyDue = false;
            }

            // Never twice within the floor, however good the reason.
            if (now - this.farmWalkLastRepathAt < FarmWalkRepathMinGap)
            {
                offCorridor = false;
                notClosing = false;
                safetyDue = false;
            }

            if (offCorridor || notClosing || safetyDue)
            {
                this.farmWalkNextRepathAt = now + FarmWalkRepathInterval;
                this.farmWalkLastRepathAt = now;

                // From here down the route is our own, whoever owned it until now.
                this.farmWalkOwnRouteSeq++;
                int cornersBefore = this.farmWalkCorners.Count;
                Vector3 firstBefore = this.farmWalkCorners.Count > 0 ? this.farmWalkCorners[0] : Vector3.zero;
                if (this.TryBuildFarmWalkRoute(selfPos, this.farmWalkTarget) && this.farmWalkCorners.Count > 0)
                {
                    corner = this.farmWalkCorners[this.farmWalkCornerIndex];

                    // Did the rebuild actually produce anything new?
                    bool routeChanged = this.farmWalkCorners.Count != cornersBefore
                        || (this.farmWalkCorners[0] - firstBefore).sqrMagnitude > 0.01f;

                    // Say WHY and WHAT CHANGED. A silent rebuild is what made this take a week to
                    // spot: the walk-begin line prints one route and the walker then follows a
                    // succession of unlogged others.
                    float rebuiltRemaining = this.ComputeFarmWalkRouteRemaining(selfPos);
                    bool routeImproved = routeChanged
                        && rebuiltRemaining < this.farmWalkBestDistance - FarmWalkRepathMustGain;

                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": re-pathed ("
                        + (offCorridor ? "off corridor" : notClosing ? "not closing" : "safety cadence")
                        + "): " + cornersBefore + " -> " + this.farmWalkCorners.Count
                        + " corners, now at " + this.farmWalkCornerIndex
                        // ⚠️ SAY WHAT IS BEING COMPARED. "no shorter" reads as old route vs new
                        // route, and it is not: rebuiltRemaining is measured against
                        // farmWalkBestDistance, the best remaining this walk has ever achieved. Once
                        // the walker has closed in, EVERY rebuild is "no shorter" by construction —
                        // which made "8 -> 2 corners (RESHUFFLED, no shorter)" look like a
                        // contradiction rather than a baseline decision.
                        + (!routeChanged ? " (IDENTICAL)"
                            : routeImproved ? " (" + (this.farmWalkBestDistance - rebuiltRemaining).ToString("F1") + "m better than this walk's best)"
                            : " (RESHUFFLED, no better than this walk's best of "
                                + this.farmWalkBestDistance.ToString("F1") + "m)") + ".");

                    // ⚠️ DIFFERENT IS NOT THE SAME AS BETTER.
                    //
                    // Re-baselining on any change made the walk immortal a second way, through the
                    // branch the earlier guard does not cover. The graph has two attractors here and
                    // each rebuild flips between them, so the route is never IDENTICAL and the futile
                    // counter never grows:
                    //     re-pathed (safety cadence): 3 -> 5 corners
                    //     shortcut removed 2 corner(s), 3 left
                    //     re-pathed (off corridor):   5 -> 3 corners
                    // — every nine seconds, for as long as anyone watched. And since "not closing" is
                    // measured from farmWalkBestAt, each flip restarted the clock that is supposed to
                    // end the walk.
                    //
                    // So the test is whether the new route is genuinely SHORTER. A rebuild that only
                    // reshuffles corners carries no information, exactly like an identical one, and
                    // is counted as futile — which lets the waypoint-ban path below do its job.
                    if (routeImproved)
                    {
                        // A shorter route has a different length, so the previous best is not
                        // comparable — re-baseline rather than reading the change as lost progress.
                        this.farmWalkBestDistance = rebuiltRemaining;
                        this.farmWalkBestAt = now;
                        this.farmWalkFutileRepaths = 0;
                    }
                    else
                    {
                        // ⚠️ LIVELOCK GUARD. Re-baselining here is what made the walk immortal:
                        // "not closing" fires at HALF the window and triggers a re-path, the
                        // re-path reset the timer, so the full window — the one that ends the walk
                        // — could never elapse. The log showed the same line for two minutes:
                        // "re-pathed (not closing): 9 -> 9 corners, now at 0", the graph handing
                        // back the identical route every time while the player sat against a wall.
                        //
                        // An identical route is not new information. Leave the timer running, and
                        // count the futile attempts so a node that cannot be approached at all is
                        // abandoned for a different one instead of being retried forever.
                        this.farmWalkFutileRepaths++;
                        if (this.farmWalkFutileRepaths >= FarmWalkMaxFutileRepaths)
                        {
                            // Before giving up on the NODE, give up on the WAYPOINT. The route is
                            // wedged at a specific corner, and A* will keep handing back that same
                            // corner until it is taken off the table — which is why four different
                            // targets in a row all died at "corner 2": different destinations,
                            // identical blocked waypoint in the middle. Banning it for the rest of
                            // the run is both cheaper and more correct than abandoning resources
                            // that are perfectly reachable by another way round.
                            // ⚠️ NOT UNDERWATER. A swimmer has vertical freedom: the shapes that
                            // stop a walker are not obstacles there, and a waypoint banned for one
                            // is banned for every land route through that spot afterwards. This
                            // site predates the swim/land split and never asked which it was.
                            if (!this.farmWalkIsSwimming
                                && this.farmWalkBlockedGraphNodes.Count < FarmWalkMaxBlockedNodes
                                && this.TryFindNearestTrackGraphNode(corner, FarmWalkGraphSnapRadius,
                                    out int blockedIndex, this.farmWalkExcludedNodes)
                                && !this.farmWalkBlockedGraphNodes.ContainsKey(blockedIndex))
                            {
                                this.farmWalkBlockedGraphNodes[blockedIndex] =
                                    Time.unscaledTime + FarmWalkBlockedNodeTtl;
                                this.farmWalkExcludedNodes.Add(blockedIndex);
                                this.farmWalkFutileRepaths = 0;

                                // Soon, but not this frame. Zeroing it is what let a ban trigger the
                                // rebuild that produced the next ban, six deep in a second and a half.
                                this.farmWalkNextRepathAt = now + FarmWalkRepathMinGap;
                                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": corner "
                                    + this.farmWalkCornerIndex + " is unwalkable — banning waypoint "
                                    + blockedIndex + " for " + (FarmWalkBlockedNodeTtl / 60f).ToString("0.#")
                                    + " min (" + this.farmWalkBlockedGraphNodes.Count + "/"
                                    + FarmWalkMaxBlockedNodes + " banned) and routing around it.");
                                return false;
                            }

                            this.FinishFarmWalk("the graph keeps returning the same unwalkable route ("
                                + this.farmWalkCorners.Count + " corners, still at corner "
                                + this.farmWalkCornerIndex + " after " + this.farmWalkFutileRepaths
                                + " rebuilds, " + this.farmWalkBlockedGraphNodes.Count
                                + " waypoint(s) already banned)", teleport: true);
                            return true;
                        }
                    }
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
            // ⚠️ AN ESCAPE OWNS THE AXIS OUTRIGHT. Steering here during one is not a second opinion,
            // it is the thing that breaks it: the apex hop releases the axis to launch, and this line
            // put it straight back, pressed at the corner — which is to say, into the obstacle. A
            // jump taken with the axis held into a blocker does not leave the ground at all, measured
            // both in the probe and here:
            //     apex hop 45 deg -> 0,00m closer, 0,00m up, 3 hop(s), airborne 0%
            // Three impulses accepted by the game per attempt, on three headings, and the body never
            // left the floor once.
            //
            // It also means the metres those attempts appeared to win in earlier runs were this
            // steering walking to the corner, not the hops — the escape had never actually run.
            //
            // ⚠️ THE HOP BURST, AND ONLY IT. Every OTHER unstick takes its direction from INSIDE
            // SteerFarmWalkToward — backing off reverses `delta` there, the underwater probe swaps
            // in its leg heading there, the vehicle unstick asks
            // TryGetFarmWalkVehicleUnstickDirection there. Gating them all out of this call did not
            // hand them the axis, it left them with no way to push at all, and they went on
            // measuring an obstacle they were no longer swimming away from:
            //     probe leg 1/4 (180 deg) swam 0,0m of 5m (blocked this way too)
            //     probe leg 2/4  (90 deg) swam 0,0m of 5m (blocked this way too)
            //     probe leg 3/4 (270 deg) swam 0,0m of 5m (blocked this way too)
            //     probe leg 4/4   (0 deg) swam 0,0m of 5m (blocked this way too)
            // Four directions, four times zero — and the moment the probe gave up, the walk
            // re-pathed and swam to the node in four seconds. The water was never blocked.
            if (this.farmWalkUnstickPhase == FarmWalkUnstickHopBurst)
            {
                // This one drives the axis itself, frame by frame, and must not be second-guessed.
            }
            else if (this.farmWalkHoldHorizontalForClimb || sinkingOnTarget)
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
            // Before the sampler judges the approach, so the arrival/stall tests see the height the
            // resource is really at rather than the aim point's.
            this.TryRefineFarmWalkTargetHeight(selfPos);

            // ⚠️ THE SAMPLER MUST NOT JUDGE AN ESCAPE THAT IS ALREADY RUNNING.
            //
            // An apex hop stands still ON PURPOSE for about 0.4 s of every cycle — no axis while it
            // launches and waits for the top of the arc — and the sampler's window is 0.6 s against
            // 0.15 m. The window fits inside that pause, reads 0.00 m and scores a strike, so the
            // escape gets abandoned in the middle of working:
            //     apex hop 0 deg -> +7,15m closer — repeating
            //     apex hop 0 deg -> +5,79m closer — repeating
            //     apex hop 0 deg -> +2,57m closer — repeating
            //     stuck (0,00m in 0,6s) ... skipping to the next nearest node
            // — fifteen metres of progress thrown away by the detector that was supposed to notice
            // the walk was stuck.
            //
            // The escape has its own success test, its own per-heading budget and its own overall
            // cap; a second opinion here can only ever cut it short. Re-baseline the sampler as we
            // go so it starts clean the moment the escape hands back.
            if (this.farmWalkUnstickPhase != FarmWalkUnstickIdle || repairHold || dropping)
            {
                this.farmWalkLastSample = selfPos;
                this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;
            }
            else if (this.SampleFarmWalkProgress(selfPos, now))
            {
                return true;
            }

            this.ProcessFarmWalkVehicleDismount(selfPos);
            this.TryRemountFarmWalkVehicle(selfPos);
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

            // Driving out of a wedge: reverse, then perpendicular. Handled before the on-foot
            // cases because a vehicle never takes any of them.
            if (this.TryGetFarmWalkVehicleUnstickDirection(delta, out Vector3 vehicleDir)
                && vehicleDir.sqrMagnitude > 0.0001f)
            {
                delta = vehicleDir.normalized;
            }
            // Backing off an obstacle: drive the opposite way. Reversed BEFORE smoothing, so the
            // turn sweeps rather than snapping, exactly like any other direction change.
            else if (this.farmWalkUnstickPhase == FarmWalkUnstickBackingOff)
            {
                // No hop-burst case here any more: that escape never reaches this method (see the
                // gate at the call site) and it has had no retreat leg since it was rewritten
                // around the press.
                delta = -delta;
            }
            // Walking off the edge: hold the direction we came in on, so the ground runs out and
            // the body falls onto the node. Placed with the other unstick directions because it is
            // one — the axis has a single owner per frame (rule 0.3).
            //
            // ⚠️ PUSH ONLY UNTIL THE GROUND GOES AWAY. The step is "walk to the edge", not "run for
            // 1.6 seconds": once the body is falling, the axis has nothing left to contribute and
            // holding it carries the player clear over the node, who then lands past it and turns
            // back. Stepping off is a push followed by a release.
            else if (Time.unscaledTime < this.farmWalkDropUntil
                && this.farmWalkDropDir.sqrMagnitude > 0.0001f)
            {
                bool droppingGrounded = true;
                try
                {
                    if (this.TryReadBunnyHopSurfaceState(out bool g, out bool sliding))
                    {
                        droppingGrounded = g || sliding;
                    }
                }
                catch
                {
                    droppingGrounded = true;
                }

                if (!droppingGrounded)
                {
                    // Airborne: the edge is behind us. Let gravity finish it.
                    this.farmWalkDropAirborne = true;
                    this.TryClearGameMoveAxis();
                    return;
                }

                if (this.farmWalkDropAirborne)
                {
                    // Landed again — the drop is over, whatever it achieved. Hand back to the
                    // ordinary walk so the arrival test can judge the new position this frame.
                    this.farmWalkDropAirborne = false;
                    this.farmWalkDropUntil = 0f;
                }
                else
                {
                    delta = this.farmWalkDropDir;
                }
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
            const float speed = FarmWalkSpeedMax;
            float distance = Distance3D(selfPos, this.farmWalkTarget);
            if (distance >= FarmWalkSlowApproachDistance)
            {
                return speed;
            }

            // NEVER EASE OFF AT A BUBBLE. The ramp exists so the locomotion does not overshoot a
            // node that is standing still. A bubble is not standing still - it travels at about the
            // speed we swim - so easing off inside two metres simply matches its speed and the gap
            // stops closing. Measured twice, at the same number both times:
            //     on the bubble (0,56m 3-D) for 2,0s and it has not popped
            //     on the bubble (0,57m 3-D) for 2,0s and it has not popped
            // That is not a wall, it is an equilibrium between our reduced speed and its steady one.
            if (this.FarmWalkTargetIsBubble)
            {
                return speed;
            }

            float slow = Mathf.Min(FarmWalkSlowApproachSpeed, speed);
            float t = Mathf.Clamp01((distance - FarmWalkCollectDistance)
                / Mathf.Max(0.01f, FarmWalkSlowApproachDistance - FarmWalkCollectDistance));
            return Mathf.Lerp(slow, speed, t);
        }

        // Stuck detection is only a safety net for what the graph cannot know about: another player
        // in the doorway, a mesh/collision mismatch, a prop spawned since the path was authored.
        // Returns true when it ENDED the walk (arrival taken here), so the tick stops touching a
        // finished walk in the same frame.
        private bool SampleFarmWalkProgress(Vector3 selfPos, float now)
        {
            if (now < this.farmWalkNextStuckSampleAt)
            {
                return false;
            }

            // ⚠️ THE FIRST SECONDS OF A WALK ARE NOT EVIDENCE.
            //
            // A strike is 0.15 m of travel missed in a 0.6 s window, and a single strike is enough
            // to launch an escape. Right after a walk begins the body is still stopping from the
            // last one, turning toward the first corner and accelerating — so the very first window
            // reads as no progress on perfectly open ground:
            //     02:10:07  walking 11,9m via 8 corners
            //     02:10:08  wedged at 12,9m on foot — escape 1/3
            //     02:10:13  pressed 45 deg and ran 3,52m without wedging — nothing is blocking us
            // An escape, three jumps and six seconds spent proving there was never an obstacle.
            if (now - this.farmWalkStartedAt < FarmWalkStuckGraceSeconds)
            {
                this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;
                this.farmWalkLastSample = selfPos;
                return false;
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

                // Real movement is also what tells the vehicle its last obstacle is behind it.
                this.NoteFarmWalkVehicleProgress(selfPos);
                return false;
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
                // Already collectable, and the only thing left is height we do not need. Jumping at
                // it cannot help: the node's Y is the resource ANCHOR, which for a rock is its base,
                // so the aim point sits under the surface the player is standing on — the hop lands
                // exactly where it started and reads as the character trying to climb into the node.
                // Take the arrival instead.
                if (this.farmWalkAimOffsetY == 0f
                    && HorizontalDistance(selfPos, this.farmWalkTarget) <= FarmWalkCollectDistance
                    && Distance3D(selfPos, this.farmWalkTarget) <= FarmWalkAuraReachFallback)
                {
                    this.FinishFarmWalk("arrived "
                        + HorizontalDistance(selfPos, this.farmWalkTarget).ToString("F2") + "m horizontally ("
                        + Distance3D(selfPos, this.farmWalkTarget).ToString("F2") + "m 3-D, dy="
                        + (this.farmWalkTarget.y - selfPos.y).ToString("F1")
                        + "m) — inside the aura reach, not jumping at the height",
                        teleport: false);
                    return true;
                }

                // ⚠️ NOT TryFarmWalkJump. That fires a lone impulse while the walker is still
                // steering at the node — the axis pressed into whatever is in the way — and a jump
                // taken like that never leaves the ground: measured at "3 hop(s), airborne 0%,
                // 0.00m up" on three headings, and in the probe as 43 pulses buying 0.16 m of
                // travel. It burned the four-jump budget without the body once being airborne, and
                // the walk then teleported "because jumps are spent".
                //
                // The apex escape is the same move done properly: it owns the axis, releases before
                // the launch, steers at the top of the arc, and repeats what pays. A final approach
                // that is 6.5 m out and 4.0 m up is exactly the case the probe cleared with it.
                //
                // ON FOOT ONLY. Every stage of it — the running jump, the press, the apex hops —
                // is built on leaving the ground and landing again, and underwater there is no
                // ground: the jump is refused, the phase waits for a touchdown that never comes,
                // and the escape holds the axis at zero for its full twenty-two second budget. It
                // also pre-empted the water's own ladder, which lives just below, so a dive that
                // stalled got a silent no-op instead of the 8-direction sweep.
                if (!this.farmWalkIsSwimming
                    && this.farmWalkHopBurstsUsed < FarmWalkMaxHopBursts
                    && this.farmWalkStuckStrikes < 2)
                {
                    this.BeginFarmWalkHopBurst(selfPos, now,
                        Distance3D(selfPos, this.farmWalkTarget), this.farmWalkTarget);
                    return false;
                }

                // Diagnostics on the vertical state. A stall here is either "we never asked to
                // dive", "we asked and the game refused", or "we are descending into something
                // solid" — and those need completely different fixes, so record which.
                float stallDistance = Distance3D(selfPos, this.farmWalkTarget);

                // ON FOOT: never wander looking for a way in — a walker circling a bush reads as
                // broken, and on land the blocker is usually a ledge or a lip that height clears,
                // not a wall to be walked around. Back off a little, then HOLD bunny-hop: chained
                // jumps carry further and higher than the single hop already tried.
                if (!this.farmWalkIsSwimming)
                {
                    if (this.farmWalkHopBurstsUsed < FarmWalkMaxHopBursts)
                    {
                        this.BeginFarmWalkHopBurst(selfPos, now, stallDistance, this.farmWalkTarget);
                        return false;
                    }
                }
                else if (!this.farmWalkProbeUsed)
                {
                    // Underwater the 8-direction sweep stays: there the blocker really is geometry
                    // to swim around, and the player is already moving in three dimensions.
                    this.BeginFarmWalkProbe(selfPos, now, stallDistance, this.farmWalkTarget);
                    return false;
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
                return true;
            }

            if (this.farmWalkStuckStrikes < FarmWalkStuckStrikeLimit)
            {
                // Hop whatever is in the way (fence, kerb, root) and force a fresh route before
                // giving up on walking. Both are cheap and either can rescue the walk.
                //
                // The apex escape rather than a lone impulse, for the reason recorded at the final
                // approach above: an impulse sent while the walker holds the axis at the corner does
                // not lift the body at all.
                // On foot only, for the reason given at the final approach: underwater this is a
                // twenty-two second stall that also swallows the strike the water ladder needs.
                if (!this.farmWalkIsSwimming && this.farmWalkHopBurstsUsed < FarmWalkMaxHopBursts)
                {
                    this.BeginFarmWalkHopBurst(selfPos, now,
                        Distance3D(selfPos, this.farmWalkTarget), this.farmWalkCornerIndex < this.farmWalkCorners.Count
                            ? this.farmWalkCorners[this.farmWalkCornerIndex]
                            : this.farmWalkTarget);
                }

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
                return false;
            }

            if (this.farmWalkStuckStrikes >= FarmWalkStuckStrikeLimit)
            {
                // Retreat + bunny-hop belongs to ANY on-foot wedge, not just the last few metres.
                //
                // It used to live only inside the final-approach branch (target within 8 m), so a
                // block at an intermediate corner never got it: the 23:09 run wedged 1.7 m from
                // corner 1 of 10 with the node still 53 m away, spent its four jumps standing
                // still, and skipped — no retreat line in the log at all, because that code was
                // unreachable from here.
                //
                // Aimed at the CORNER, not the node. At 53 m out, progress measured against the
                // node would not register a hop that cleared the fence in front of us.
                if (this.farmWalkCornerIndex < this.farmWalkCorners.Count)
                {
                    Vector3 wedgeCorner = this.farmWalkCorners[this.farmWalkCornerIndex];
                    if (!this.farmWalkIsSwimming && this.farmWalkHopBurstsUsed < FarmWalkMaxHopBursts)
                    {
                        this.BeginFarmWalkHopBurst(selfPos, now, Distance3D(selfPos, wedgeCorner), wedgeCorner);
                        return false;
                    }

                    // UNDERWATER: the 8-direction sweep is the water-side equivalent, and it was
                    // reachable ONLY from the final-approach branch — within 8 m of the node. A reef
                    // between corner 3 and corner 4, thirty metres out, got the back-off/ascend
                    // unstick and then nothing at all. Giving land a mid-route recovery and leaving
                    // water without one was an asymmetry, not a design.
                    if (this.farmWalkIsSwimming && !this.farmWalkProbeUsed)
                    {
                        this.BeginFarmWalkProbe(selfPos, now, Distance3D(selfPos, wedgeCorner), wedgeCorner);
                        return false;
                    }
                }

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

                // The vertical state, same as the final-approach line already reports. Without it a
                // stall three metres out horizontally and sixteen down reads as an ordinary wedge,
                // when the question is actually "is the game taking the dive input at all". Only
                // "final approach not walkable" carried this, and a deep-water stall never gets
                // there — it escalates through the stuck path instead.
                detail += " dyAim=" + (this.ResolveFarmWalkDepthAim().y - selfPos.y).ToString("F1")
                    + "m dyNode=" + (this.farmWalkTarget.y - selfPos.y).ToString("F1")
                    + "m held=" + this.farmWalkVerticalHeld;
                if (this.TryGetFarmWalkSwimLocomotion(out IntPtr stuckSwim)
                    && this.TryGetMonoSingleMember(stuckSwim, "_swimVerticalInput", out float stuckVerticalInput))
                {
                    detail += " gameVerticalInput=" + stuckVerticalInput.ToString("F2");
                }

                this.FinishFarmWalk(detail, teleport: true);
                return true;
            }

            return false;
        }

        // Replace the aim height with the resource's REAL one, once per walk, when close enough for
        // the entity to be streamed in.
        //
        // The node tables are hand-measured constants and their Y is the BASE of the resource, not
        // the ground the player can stand on: the Ore entry at (-22.6, 20.1, 131.4) leaves the walk
        // reporting dy=-1.3 m after the horizontal axis has closed, and the final approach then
        // spends its jump budget trying to climb into a point buried under the rock. The live
        // CollectableObjectComponent knows where the thing actually is, so ask it.
        //
        // Deliberately matched HORIZONTALLY: height is the axis under suspicion, so including it in
        // the match would reject exactly the nodes worth correcting.
        // ==========================================================================================
        // BUBBLE TARGETS ARE A CHASE, NOT A WALK TO A COORDINATE.
        //
        // Every other farm target stands still, so the position picked at planning time is the
        // position on arrival. A bubble drifts the whole way there, and the walker was steering at
        // where it USED to be — arriving at an empty patch of water and reporting the bubble gone.
        //
        // So the aim is re-read from the radar's live tracking every tick, and inside the closing
        // range the radar is forced to re-sync rather than waiting out its idle cadence. Each
        // re-aim logs the drift, which is the number that says whether the chase is keeping up.
        private const float FarmWalkBubbleMatchRadius = 8f;        // how far the same bubble may have drifted
        private const float FarmWalkBubbleRetargetStep = 0.35f;    // re-aim once it has moved this far
        private const float FarmWalkBubbleResyncRange = 20f;       // force fresh reads inside this
        private const float FarmWalkBubbleResyncInterval = 0.4f;
        private const float FarmWalkBubbleTouchDistance = 1.5f;    // judgement call: the game's pop radius is unmeasured
        private const float FarmWalkBubbleTouchGrace = 2f;
        private float farmWalkBubbleTouchingSince = -1f;
        private int farmWalkBubbleId;
        private float farmWalkBubbleNextResyncAt;
        private float farmWalkBubbleNextLogAt;
        private float farmWalkBubbleDrift;

        internal bool FarmWalkTargetIsBubble =>
            string.Equals(this.farmWalkDwellLabel, "Bubble", StringComparison.Ordinal);

        private void UpdateFarmWalkBubbleChase(Vector3 selfPos, float now)
        {
            if (!this.FarmWalkTargetIsBubble)
            {
                return;
            }

            float distance = Distance3D(selfPos, this.farmWalkTarget);
            if (distance <= FarmWalkBubbleResyncRange && now >= this.farmWalkBubbleNextResyncAt)
            {
                this.farmWalkBubbleNextResyncAt = now + FarmWalkBubbleResyncInterval;
                this.ForceBubbleRadarResync();
            }

            if (!this.TryGetLiveBubblePosition(this.farmWalkTarget, FarmWalkBubbleMatchRadius, out Vector3 live, out int liveId))
            {
                // Nothing tracked near the aim. Not a verdict on its own — the bubble may simply be
                // outside the marker range — so the ordinary walk rules decide what happens next.
                return;
            }

            this.farmWalkBubbleId = liveId;
            float moved = Distance3D(live, this.farmWalkTarget);
            this.farmWalkBubbleDrift += moved;
            if (moved < FarmWalkBubbleRetargetStep)
            {
                return;
            }

            this.farmWalkTarget = live;
            this.farmWalkTrueTarget = live;
            if (this.farmWalkCorners.Count > 0)
            {
                // The route's last corner IS the target (see TryBuildFarmWalkRoute) — move it with
                // the aim or the walker steers at the stale one all the way down the route.
                this.farmWalkCorners[this.farmWalkCorners.Count - 1] = live;
            }

            // Throttled: at 0.4 s resyncs a chatty bubble would fill the ring by itself.
            if (now >= this.farmWalkBubbleNextLogAt)
            {
                this.farmWalkBubbleNextLogAt = now + 2f;
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": bubble moved "
                    + moved.ToString("F2") + "m (drift " + this.farmWalkBubbleDrift.ToString("F1")
                    + "m this walk) — re-aiming at " + live.ToString("F1") + ", "
                    + distance.ToString("F1") + "m out.");
            }
        }

        private void TryRefineFarmWalkTargetHeight(Vector3 selfPos)
        {
            if (this.farmWalkHeightRefined
                || this.farmWalkAimOffsetY != 0f    // contamination standoff owns its own height
                || HorizontalDistance(selfPos, this.farmWalkTarget) > FarmWalkHeightRefineRange)
            {
                return;
            }

            // Shared snapshot, not a private enumeration. This used to run its own full
            // CollectableObjectComponent walk with its own pin list — the same component family the
            // scan already enumerates every ~2 s for the radar, the map spots and the cold sync. One
            // scan now answers all four, and the walker no longer opens a pinning window of its own
            // (every pin site is a chance to leak one under an exception; the fewer, the better).
            // RefreshCollectableScan is self-throttled, so calling it here costs nothing when a
            // fresh snapshot already exists — which, during a farm run, it always does.
            this.RefreshCollectableScan();
            if (this.liveCollectableColds.Count == 0)
            {
                return;     // nothing streamed yet — worth another try next tick
            }

            this.farmWalkHeightRefined = true;

            // liveCollectableColds, not mapResEntities: the unfiltered list carries EVERY positioned
            // collectable, including the ones whose produce id never resolved. For a height fix the
            // species is irrelevant — only "is a collectable standing here, and how high".
            float bestSqr = FarmWalkHeightRefineMatch * FarmWalkHeightRefineMatch;
            float bestY = 0f;
            bool found = false;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                Vector3 pos = this.liveCollectableColds[i].Position;
                float d = HorizontalDistanceSqr(pos, this.farmWalkTarget);
                if (d < bestSqr)
                {
                    bestSqr = d;
                    bestY = pos.y;
                    found = true;
                }
            }

            if (!found || Mathf.Abs(bestY - this.farmWalkTarget.y) < 0.25f)
            {
                return;     // nothing near, or the aim was already right
            }

            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": node height "
                + this.farmWalkTarget.y.ToString("F1") + "m -> " + bestY.ToString("F1")
                + "m from the live entity.");

            this.farmWalkTarget.y = bestY;
            this.farmWalkTrueTarget.y = bestY;
        }

        // Stop closing on a resource that is provably no longer collectable.
        //
        // WHY. Reaching the last metre is the expensive part of a walk: it is where the jumps, the
        // sidestep probes and the once-a-minute rescue teleport get spent. Spending all of that to
        // stand on an empty patch of ground — because the aura drained the node while we were still
        // walking, or another player took it — buys nothing, and the farm then burns a whole collect
        // dwell there before moving on.
        //
        // TWO WAYS A RESOURCE STOPS BEING COLLECTABLE, and the live scan shows both:
        //   • COOLDOWN family (trees, stone, ore, berries) — the entity stays and flips inCold.
        //   • DESPAWN family (mushrooms and the other dynamic bushes) — the entity is REMOVED.
        //     They never go cold, so waiting for a cooldown flag on them waits forever.
        //
        // ⚠️ ABSENCE ALONE IS NOT PROOF. A node outside the streamed bubble is absent too, and so is
        // one the scan has simply not reached yet. Absence only counts once this walk has SEEN the
        // node present in an earlier scan: seen, then missing from a LATER scan, is unambiguous.
        private bool TryAbandonDrainedFarmWalkTarget(Vector3 selfPos)
        {
            // ⚠️ CONTAMINATION HAS ITS OWN AUTHORITY, AND IT WAS NEVER ASKED.
            //
            // A pollutant is not a collectable, so the scan below can never see it — which is why
            // the standoff is exempted a few lines down. That exemption then meant NOTHING could
            // call a contamination walk off, and a spot cleaned by hand stayed a destination:
            //     18:35:00-18:35:29  thirty seconds of thrashing 1,4 m short, four probe legs,
            //                        three unstick rounds, then the rescue teleport
            //     18:35:30  Contamination dwell done (kills=0, reason=area clear)
            // The farm found out on arrival what the radar's own live scan already knew — that scan
            // drops a pollutant the moment it reads isCleaned, and it runs several times a second.
            if (string.Equals(this.farmWalkDwellLabel, "Contaminated", StringComparison.Ordinal)
                && !this.IsContaminationStillActionable(this.farmWalkTrueTarget, out bool contaminationKnown)
                && contaminationKnown)
            {
                this.StampVisitedNode(this.farmWalkTrueTarget, Time.unscaledTime + FarmVisitedRetryStampSeconds,
                    approachFailure: true);
                this.farmWalkSkipToScan = true;
                this.FinishFarmWalk("already clean — the live pollutant scan has nothing here ("
                    + Distance3D(selfPos, this.farmWalkTrueTarget).ToString("F1")
                    + "m short) — moving to the next node", false);
                return true;
            }

            // ⚠️ THE WALK'S OWN LABEL, NOT THE DWELL FLAG. autoFarmTargetIsBubble is set by
            // BeginFarmNodeDwell — that is, ON ARRIVAL — so throughout the approach it still holds
            // whatever the PREVIOUS node set, and the bubble exemption below was never in force
            // while it was needed. Every bubble walk died at the absence gate instead:
            //     node:Bubble: target is not there at all (12,0m short) — moving to the next node
            // twice in three seconds, which is the "stops 10 m out and turns away" the player sees.
            // farmWalkDwellLabel is set at Begin and is correct for the whole walk.
            // ⚠️ ONLY A RESOURCE MAY BE JUDGED BY THE RESOURCE SCAN. Stated as a POSITIVE test,
            // because the list of exceptions was wrong three times running: contamination, then the
            // cleansing coral, then bubbles — each found the same way, by a walk livelocking against
            // a rule that could never be satisfied.
            //
            // A quest point was the fourth, and it is the reason this is now inverted rather than
            // extended again. A quest track point is a PLACE; nothing about it will ever appear in
            // the collectable scan, so the absence rule was true on arrival every single time:
            //     14:58:31  quest:8000060: walking 10,7m via 2 corners
            //     14:58:31  quest:8000060: target is not there at all (11,1m short)
            //     14:58:33  quest:8000060: walking 10,7m via 2 corners
            //     14:58:33  quest:8000060: target is not there at all (11,1m short)
            //     ... every two seconds, the distance frozen at 11,1 m, the body never moving ...
            // and an earlier stretch of the same run reached 1,4 m before the rule threw it away.
            //
            // Farm nodes are labelled "node:*" (including "node:priority-*" and "node:retry");
            // "quest:", "cleanse:" and "area:" are destinations, not things that can be collected.
            if (!this.farmWalkLabel.StartsWith("node:", StringComparison.Ordinal)
                || this.farmWalkAimOffsetY != 0f    // contamination standoff: aimed off the node
                || this.FarmWalkTargetIsBubble      // bubbles are not in the collectable scan
                || this.autoFarmTargetIsBubble)
            {
                return false;
            }

            float distance = Distance3D(selfPos, this.farmWalkTrueTarget);

            // ⚠️ VERIFY BEFORE ARRIVAL, NOT ON ARRIVAL. The shared scan runs every 2 s, which at
            // walking speed is ~9 m of travel, so a target that streamed in during the approach was
            // judged a whole scan late — the 2026-08-19 log has a node picked at 6,8 m and only
            // found cold at 0,3 m, one scan interval later. Crossing into range therefore forces
            // ONE fresh scan (once per walk: the point is to have current state before the last
            // stretch, not to re-enumerate every frame).
            if (!this.farmWalkApproachScanForced && distance <= FarmWalkApproachVerifyDistance)
            {
                this.farmWalkApproachScanForced = true;
                this.mapResNextScanAt = 0f;
            }

            // Self-throttled, so this costs nothing when a current snapshot already exists.
            this.RefreshCollectableScan();
            if (this.liveCollectableColds.Count == 0)
            {
                return false;
            }

            string why;
            long coldEndMs = 0L;
            if (this.TryGetLiveNodeColdState(this.farmWalkTrueTarget, 0f, out bool onCooldown, out coldEndMs))
            {
                this.farmWalkTargetSeenAt = this.liveCollectableScanCompletedAt;
                if (!onCooldown)
                {
                    return false;   // the object itself says collectable — the only thing that does
                }

                // ⚠️ NO DISTANCE GATE HERE. The object is loaded and the game says it is spent;
                // that is conclusive at 3 m and at 300 m alike, and there is nothing to gain by
                // walking any further before believing it. Only the ABSENCE cases below need a
                // distance rule, because "not in the scan" at range means "not streamed in".
                // Name the SOURCE. The component and the client's broadcast verdict disagree often
                // enough that "on cooldown" alone is not a diagnosis: one of them is a field that
                // may simply have no data, the other is the number the game computed for that netId.
                why = "went on cooldown while we walked" + this.DescribeFarmWalkColdSource(this.farmWalkTrueTarget);
            }
            else if (this.farmWalkTargetSeenAt >= 0f
                && this.liveCollectableScanCompletedAt > this.farmWalkTargetSeenAt)
            {
                why = "was collected while we walked";
            }
            else if (distance <= FarmWalkDrainedCloseDistance)
            {
                // Never seen by this walk, but we are close enough that "not in the scan" can only
                // mean "not there". The scan enumerates every loaded CollectableObjectComponent
                // rather than a radius, so at this range a resource that exists is in it.
                //
                // This is the case a prior sighting cannot cover: a node that was already gone when
                // the walk started. It happens with tour stops planned minutes earlier, and with the
                // harvested static list, whose mushroom points are SPAWN POINTS — most of them stand
                // empty at any moment, because dynamic bushes are not all up at once.
                why = "is not there at all";
            }
            else
            {
                return false;   // too far to conclude anything from absence
            }

            // Park it for as long as it is actually unavailable: the server's own cooldown end when
            // we have one, the standard cold fallback otherwise. A despawned dynamic bush respawns
            // on its own schedule, so the fallback is the honest guess there.
            this.StampVisitedNode(this.farmWalkTrueTarget, Time.unscaledTime + this.GetVisitedColdStampSeconds(coldEndMs));

            // Straight back to the scan, NOT into the collect dwell. Without this the walk ended as
            // a normal arrival and the farm still stood there for the full Collect Wait Max on a
            // resource it had just proved was gone ("target was collected while we walked" at
            // 01:04:21 followed by a 5,0s timeout at 01:04:26 — 2026-08-19 log).
            this.farmWalkSkipToScan = true;

            // teleport:false — there is nothing there to warp to. The visited stamp above is what
            // sends FindClosestAvailableNode somewhere else.
            // FinishFarmWalk prefixes farmWalkLabel itself; naming it again printed it twice.
            this.FinishFarmWalk("target " + why + " (" + distance.ToString("F1")
                + "m short) — moving to the next node instead of closing in", false);
            return true;
        }

        // Which source called this node spent, and how fresh it is. Empty when only the component
        // said so — that is the default and needs no annotation.
        private string DescribeFarmWalkColdSource(Vector3 node)
        {
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                Vector3 d = this.liveCollectableColds[i].Position - node;
                if ((d.x * d.x) + (d.z * d.z) >= 2.25f)
                {
                    continue;
                }

                uint netId = this.liveCollectableColds[i].NetId;
                if (netId == 0u || !this.collectColdByNetId.TryGetValue(netId, out CollectColdRecord r))
                {
                    return string.Empty;
                }

                long remaining = (r.EndUnixMs - NowUnixMs()) / 1000L;
                if (remaining <= 0L)
                {
                    return string.Empty;
                }

                return " [client verdict: netId " + netId + " not ready for " + remaining
                    + "s, heard " + (Time.unscaledTime - r.SeenAt).ToString("F0") + "s ago]";
            }

            return string.Empty;
        }

        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (dx * dx) + (dz * dz);
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

            // IN A VEHICLE FIRST. A car cannot jump and cannot thread a gap the way a swimmer can,
            // so its escape is the driver's one — reverse, then pull out sideways. Two rounds, and
            // then the vehicle IS the obstacle: BeginFarmWalkVehicleUnstick gets out on the third
            // call and the on-foot ladder below takes over from the next block.
            if (this.IsFarmWalkVehicleSteering())
            {
                if (this.TryGetNavMeshSelfPosition(out Vector3 vehiclePos, out _))
                {
                    this.BeginFarmWalkVehicleUnstick(vehiclePos, Time.unscaledTime, why);
                    return;
                }
            }

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

                // Count failures per node. Two failed approaches is the point at which trying again
                // stops being worth anything: the 20:44 run proved a retry reproduces the SAME
                // numbers (6.8 m, dy=3.8 m) because the obstacle is geometry, not routing.
                this.farmWalkNodeFailures.TryGetValue(this.farmWalkTrueTarget, out int nodeFailures);
                nodeFailures++;
                this.farmWalkNodeFailures[this.farmWalkTrueTarget] = nodeFailures;

                // Close enough that a teleport is a small, occasional correction rather than the
                // mode's normal way of getting around — and the walk has already spent its jumps.
                bool rescueInRange = this.TryGetNavMeshSelfPosition(out Vector3 finishPos, out _)
                    && Distance3D(finishPos, this.farmWalkTrueTarget) <= FarmWalkRescueTeleportRange;
                bool rescueOffCooldown = this.farmWalkLastRescueTeleportAt <= 0f
                    || skipNow - this.farmWalkLastRescueTeleportAt >= FarmWalkRescueTeleportCooldown;

                if (this.farmWalkHasLastReclaimed
                    && skipNow - this.farmWalkLastReclaimedAt < FarmWalkRepeatOffenderWindow
                    && HorizontalDistance(this.farmWalkLastReclaimedNode, this.farmWalkTrueTarget) < 2f)
                {
                    this.farmWalkHasLastReclaimed = false;
                    this.farmWalkHasSkippedNode = false;
                    this.StampVisitedNode(this.farmWalkTrueTarget, skipNow + FarmWalkRepeatOffenderParkSeconds,
                        approachFailure: true);
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — unreachable again after a reclaim, parking it for "
                        + (FarmWalkRepeatOffenderParkSeconds / 60f).ToString("0.#") + " min.");
                }
                else if (nodeFailures >= FarmWalkMaxNodeFailures)
                {
                    // Second failure on the same node — stop spending time on it either way.
                    this.farmWalkConsecutiveSkips++;
                    this.farmWalkHasSkippedNode = false;
                    this.farmWalkRetryState = 0;
                    this.farmWalkSkipToScan = true;
                    this.farmWalkNodeFailures.Remove(this.farmWalkTrueTarget);
                    this.StampVisitedNode(this.farmWalkTrueTarget, skipNow + FarmWalkRepeatOffenderParkSeconds,
                        approachFailure: true);
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — failed " + FarmWalkMaxNodeFailures + " times, parking it for "
                        + (FarmWalkRepeatOffenderParkSeconds / 60f).ToString("0.#") + " min.");
                }
                else if (rescueInRange && rescueOffCooldown
                    && !this.HasAnotherFarmTourStop(this.farmWalkTrueTarget))
                {
                    // Rescue teleport. The obstacle survived the jumps, but the node is right there,
                    // so one hop collects it instead of abandoning it. Rate-limited hard so this
                    // stays an exception: at one per minute it can never become the way the farm
                    // travels, which is the whole reason walking exists.
                    //
                    // ⚠️ AND ONLY WHEN THERE IS NOTHING ELSE TO GO TO (rule 0.2). Being near a node
                    // is not a reason to warp onto it while a dozen others stand unvisited: the
                    // right answer there is to walk to one of them. The warp is for the case where
                    // giving up on this node means giving up entirely.
                    this.farmWalkLastRescueTeleportAt = skipNow;
                    this.farmWalkNodeFailures.Remove(this.farmWalkTrueTarget);
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — within " + FarmWalkRescueTeleportRange.ToString("F0")
                        + "m and jumps are spent, taking the once-a-minute rescue teleport.");
                    this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(this.farmWalkTrueTarget, this.farmWalkLabel),
                        "node:walk-rescue", this.farmWalkTrueTarget);
                }
                else if (this.farmWalkConsecutiveSkips < FarmWalkMaxConsecutiveSkips)
                {
                    this.farmWalkConsecutiveSkips++;
                    this.StampVisitedNode(this.farmWalkTrueTarget, Time.unscaledTime + FarmVisitedRetryStampSeconds,
                        approachFailure: true);
                    this.farmWalkSkipToScan = true;
                    this.farmWalkHasSkippedNode = true;
                    this.farmWalkSkippedNode = this.farmWalkTrueTarget;
                    this.farmWalkSkippedNodeLabel = this.farmWalkDwellLabel;
                    this.farmWalkReclaimNotBefore = Time.unscaledTime + FarmWalkReclaimGraceSeconds;

                    // Queue it to be retried once the next node has been collected, remembering
                    // WHICH approach failed so the retry comes at it from somewhere else.
                    this.farmWalkRetryState = 1;
                    this.farmWalkRetryNode = this.farmWalkTrueTarget;
                    this.farmWalkRetryLabel = this.farmWalkDwellLabel;
                    this.farmWalkRetryAvoidEndNode = this.farmWalkEndNodeIndex;
                    // Say which arm of the rescue test failed — "why did it not just teleport, it
                    // was right there" is otherwise unanswerable from the log.
                    string rescueDeclined = rescueInRange
                        ? " (rescue teleport on cooldown, "
                            + (FarmWalkRescueTeleportCooldown - (skipNow - this.farmWalkLastRescueTeleportAt)).ToString("F0")
                            + "s left)"
                        : " (beyond the " + FarmWalkRescueTeleportRange.ToString("F0") + "m rescue range)";
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + reason + progress
                        + " — skipping to the next nearest node (" + this.farmWalkConsecutiveSkips
                        + "/" + FarmWalkMaxConsecutiveSkips + ")" + rescueDeclined + ".");
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
                // Arrived: clear the failure tally so a node that was awkward once, then reached,
                // does not carry a strike into its next visit.
                this.farmWalkNodeFailures.Remove(this.farmWalkTrueTarget);

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
            this.AbortFarmWalk(keepVehicle: false);
        }

        // keepVehicle: the caller is replacing this walk with another one RIGHT NOW, to the same
        // journey's destination. The seat belongs to the haul, not to a particular aim point.
        //
        // ⚠️ Without this a quest point that shifts twenty metres cost a full dismount and re-summon
        // with a hundred and seventy metres still to drive. Measured 00:34:08-00:34:14:
        //     walking 199,5m via 24 corners, target=(29,02, 22,33, 96,38)
        //     summoned 81009 and took the seat for this haul.
        //     walking 169,4m via 19 corners, target=(8,80, 22,93, 97,09)
        //     dismounted from netId 4150159 (walk aborted).
        // Six seconds in the car, and the re-summon that follows is another server round-trip plus
        // the mount window before anything moves again.
        private void AbortFarmWalk(bool keepVehicle)
        {
            if (!this.farmWalkActive)
            {
                // ⚠️ THE SEAT OUTLIVES THE WALK, AND NOTHING WAS LEFT TO GET OUT OF IT.
                //
                // The early return stood BEFORE the dismount, and keepVehicle:true leaves the
                // player in the car with farmWalkActive=false. If the next leg then failed to
                // build (end off the graph, the two-ban limit, own position unresolvable), there
                // was nothing left to dismount with: ShouldFarmWalkSummonVehicle sees that we are
                // already riding, ProcessFarmWalkVehicleDismount does not run without a walk, and
                // StopQuestWalk hit exactly this line. The user switched the mode off and stayed
                // sitting in a mod-summoned vehicle.
                //
                // A request to release everything must release everything, even with no walk left.
                if (this.farmWalkVehicleOurs && !keepVehicle)
                {
                    this.TryFarmWalkDismount("walk aborted (nothing was walking)");
                }

                return;
            }

            this.farmWalkActive = false;
            this.farmWalkCorners.Clear();
            this.farmWalkCornerIndex = 0;
            this.ReleaseFarmWalkDepth();
            this.TryClearGameMoveAxis();
            this.farmWalkPendingCleanse = false;
            this.farmWalkPendingArea = false;
            this.farmWalkVehicleLeftForObstacle = false;   // never leaks into the next walk
            if (this.farmWalkVehicleOurs && !keepVehicle)
            {
                this.TryFarmWalkDismount("walk aborted");
            }
        }

        // Arrival setup, shared by "walked there" and "gave up and teleported" — both end standing
        // at the node, so both hand over to Collecting the same way the teleport path always did.
        private void EnterFarmCollectingAfterWalk()
        {
            // Consume the flag HERE, before any early return. Left set on a skipped cleanse walk it
            // would misroute the next ordinary arrival into the cleanse wait.
            bool cleanseWalk = this.farmWalkPendingCleanse;
            this.farmWalkPendingCleanse = false;
            bool areaWalk = this.farmWalkPendingArea;
            this.farmWalkPendingArea = false;

            // A zone haul ends at the area, not at a resource — and the vehicle goes back whether
            // the haul succeeded or was skipped, so it never follows us into the next one.
            if (this.farmWalkVehicleOurs)
            {
                this.TryFarmWalkDismount("zone haul finished");
            }

            if (areaWalk)
            {
                this.farmWalkSkipToScan = false;
                this.farmState = HeartopiaComplete.AutoFarmState.LoadingArea;
                this.autoFarmTimer = 0f;
                this.autoFarmStatus = "Arrived — loading the area...";
                this.ResetFarmTour(); // the plan described the zone we just left
                return;
            }

            // A skipped node was never reached, so there is nothing to collect — go straight back
            // to the scan, which will pick the next nearest node with this one stamped out.
            if (this.farmWalkSkipToScan)
            {
                this.farmWalkSkipToScan = false;

                // A cleanse walk that never arrived must not be allowed to re-fire on the next
                // frame — see NoteCorruptionCleanseWalkAborted for what that costs.
                if (cleanseWalk)
                {
                    this.NoteCorruptionCleanseWalkAborted();
                }

                this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
                this.autoFarmTimer = 0f;
                this.autoFarmStatus = "Node unreachable, finding another...";
                return;
            }

            // Reached one: the run is healthy again, and a queued retry is now due.
            this.farmWalkConsecutiveSkips = 0;
            this.NoteFarmWalkCollectForRetry();

            // Opens the "let the aura finish" window that MovingToLocation waits out before leaving
            // the area — a walk arrival is the walk-mode equivalent of a node teleport.
            this.lastFarmNodeActivityAt = Time.unscaledTime;

            // Swam to a cleansing coral rather than a resource: hand straight to the cleanse wait,
            // which takes it from "arrived, confirm the flow started" exactly as the teleport did.
            if (cleanseWalk)
            {
                this.NoteCorruptionCleanseArrival();
                return;
            }

            // Decoration only: queues the gathering animation when someone is watching. It never
            // gates the collect below, which Aura Farm owns.
            this.NoteForagingAnimArrival(this.farmWalkTarget);

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
        // ⭐ ONE PLACE THAT MAKES A RUN A FRESH RUN.
        //
        // Most walker state is already re-armed per WALK, inside TryBeginFarmWalk — the escape
        // ladder, the probe, the sweep cache, the edge-audit bans. What this clears is the layer
        // ABOVE that: things which deliberately outlive a single walk so that repeated failures can
        // be recognised, and which therefore also outlive Stop Foraging unless somebody clears them.
        //
        // Audited 2026-08-23 by walking every farmWalk*/farmTour* field and subtracting what the
        // toggle, TryBeginFarmWalk and AbortFarmWalk already touch. Everything below survived both.
        // The symptom of a miss is a run that opens mid-argument: a node parked because the PREVIOUS
        // session gave up on it, a deadlock teleport fired on the third node because the skip
        // counter still held two from before, a stand-off already narrowed for a kind whose geometry
        // was somewhere else entirely.
        internal void ResetFarmWalkRunState()
        {
            // The skip/reclaim ladder. Each of these exists to remember a failure ACROSS walks, so
            // each is exactly what must not cross a run boundary.
            this.farmWalkHasSkippedNode = false;
            this.farmWalkSkippedNode = Vector3.zero;
            this.farmWalkSkippedNodeLabel = null;
            this.farmWalkHasLastReclaimed = false;
            this.farmWalkLastReclaimedNode = Vector3.zero;
            this.farmWalkLastReclaimedAt = -999f;
            this.farmWalkReclaimNotBefore = 0f;
            this.farmWalkConsecutiveSkips = 0;
            this.farmWalkSkipToScan = false;

            // Route-failure counter: at FarmWalkMaxRouteFailures it stops parking nodes and takes
            // the teleport instead, so a stale count starts the run closer to warping.
            this.farmWalkRouteFailures = 0;

            // A retry queued by the previous run would otherwise be the first thing the new one does.
            this.farmWalkRetryState = 0;
            this.farmWalkRetryNode = Vector3.zero;
            this.farmWalkRetryLabel = null;
            this.farmWalkRetryAvoidEndNode = -1;

            // ⚠️ THE LEARNED STAND-OFFS GO TOO. This is real knowledge — 0.4a/0.4b buy each entry
            // with a collect that timed out — and dropping it costs one re-learn per kind. It is
            // cleared anyway, on purpose: the number was learned from ONE node's geometry, a fresh
            // run is usually somewhere else, and a stand-off narrowed for the wrong place fails
            // silently by never collecting. Re-learning is one node; a wrong value is every node.
            this.farmWalkKindStandoff.Clear();
            this.farmWalkStandoffRetries = 0;

            // Vehicle: a summon cooldown and a half-finished dismount from the last run.
            this.farmWalkVehicleLastSummonAt = 0f;
            this.farmWalkVehicleLastDismountAt = -999f;
            this.farmWalkVehicleLeftForObstacle = false;
            this.farmWalkVehicleUnstickRounds = 0;
            this.farmWalkVehicleSideSign = 1;

            // Log throttle, so the first oversized-range complaint of a run is not swallowed.
            this.farmTourRangeComplaintAt = 0f;
        }

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
            this.ForgetVisitedNode(node);

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

        // What the DEPTH control aims at this frame: the current corner while the route still has
        // corners to run, the node itself only on the final leg.
        //
        // Depth used to aim at the node unconditionally, and underwater that is how the walker
        // swam into the floor. The waypoint graph carries real heights — a route is a chain of
        // corners at different depths, and the sea-side graph is sparse enough that consecutive
        // ones differ by tens of metres. Standing on corner 1 of 13, driving full dive toward a
        // node 32 m below is not "descending toward the goal", it is descending into whatever the
        // corner is sitting on. The run logged exactly that: "corner=2,8m target=29,7m dy=-32,4m
        // held=-1 gameVerticalInput=-1,00" — full dive input, 0.04 m of movement.
        //
        // Following the corners' own heights is what makes the vertical axis agree with the
        // horizontal one: both now head for the same point.
        private Vector3 ResolveFarmWalkDepthAim()
        {
            if (this.farmWalkCornerIndex >= 0
                && this.farmWalkCornerIndex < this.farmWalkCorners.Count - 1)
            {
                return this.farmWalkCorners[this.farmWalkCornerIndex];
            }

            return this.farmWalkTarget;
        }

        // Is the vertical gap to the target actually shrinking? Sampled on its own clock so a dive
        // in progress is never mistaken for a stall. Only meaningful while a depth hold is engaged.
        private bool IsFarmWalkDepthClosing(Vector3 selfPos, float now)
        {
            if (this.farmWalkVerticalHeld == 0)
            {
                return false;
            }

            float dy = Mathf.Abs(this.ResolveFarmWalkDepthAim().y - selfPos.y);
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
                    // ALWAYS take a vertical leg, ALTERNATE its direction between rounds, and start
                    // with the direction that does NOT undo the approach.
                    //
                    // The old code climbed only when the node was above, which left "blocked while
                    // diving" with no vertical attempt at all. Alternating fixed that but, seeded
                    // at "rise" unconditionally, it made a deep descent strictly worse: the run
                    // logged diving 15,8 -> 16,6 -> (round 1 rising) -> 20,0, i.e. every odd round
                    // gave back ~3 m of the ~1 m the dive had just won, and dy oscillated between
                    // 15,8 and 20,0 for four rounds without ever closing.
                    //
                    // Seeding from where the node actually is keeps both directions in play — the
                    // second round still tries the other way — while the FIRST attempt pushes the
                    // way we were already trying to go.
                    int firstDir = this.ResolveFarmWalkDepthAim().y - selfPos.y > 0f ? 1 : -1;
                    this.farmWalkBackOffVerticalDir = this.farmWalkBackOffRound % 2 == 0 ? firstDir : -firstDir;
                    this.farmWalkBackOffRound++;

                    // Only the first two rounds get a vertical leg: one each way, which is the whole
                    // point of alternating. Rounds three and four just repeat it, and repeating is
                    // what turned a stall into a climb — the run logged diving 12,0 -> 15,8 -> 21,4
                    // across four rounds, each "rising" leg giving back more than the "descending"
                    // one had won, so the walker ended nine metres higher than it started while
                    // trying to get down. Past two rounds the back-off alone is the useful part.
                    this.farmWalkUnstickPhase = this.farmWalkBackOffRound <= FarmWalkBackOffVerticalRounds
                        ? FarmWalkUnstickAscending
                        : FarmWalkUnstickIdle;
                    this.farmWalkUnstickPhaseUntil = now + FarmWalkObstacleAscendDuration;
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": backed off "
                        + Distance3D(selfPos, this.farmWalkUnstickFrom).ToString("F1") + "m ("
                        + (farEnough ? "clear" : "TIMED OUT — backing off is blocked too") + "), round "
                        + this.farmWalkBackOffRound + " — "
                        + (this.farmWalkUnstickPhase != FarmWalkUnstickAscending
                            ? "no vertical leg (both directions already tried)."
                            : this.farmWalkBackOffVerticalDir > 0 ? "rising." : "descending."));
                }

                return;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickVehicleBackOff
                || this.farmWalkUnstickPhase == FarmWalkUnstickVehicleSideStep)
            {
                this.UpdateFarmWalkVehicleUnstick(selfPos, now);
                return;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickProbing)
            {
                this.UpdateFarmWalkProbe(selfPos, now);
                return;
            }

            if (this.farmWalkUnstickPhase == FarmWalkUnstickHopBurst)
            {
                this.UpdateFarmWalkHopBurst(selfPos, now);
                return;
            }

            if (now >= this.farmWalkUnstickPhaseUntil)
            {
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
            }
        }

        // Probe order, as offsets from the bearing we were pushing toward the aim when we wedged.
        // BACKWARDS FIRST: whatever we are jammed against is in front, so reversing is the move
        // most likely to free us — and it is the one the back-off unstick already proves works.
        // Then the two sides, and only lastly forward again.
        //
        // FOUR directions, not eight. The eight-way sweep spent 8 x (0.8 s horizontal + 0.6 s
        // vertical) = 11 s before giving up, and the diagonals sit between legs that were already
        // tried — most of that time bought nothing. Four covers the same space in 5.6 s.
        private static readonly float[] FarmWalkProbeOffsets = { 180f, 90f, 270f, 0f };

        private Vector3 GetFarmWalkProbeDirection(int index)
        {
            float angle = (this.farmWalkProbeBaseYaw + FarmWalkProbeOffsets[index % FarmWalkProbeOffsets.Length]) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        // Alternate horizontal nudge -> vertical attempt, stepping through the directions. Ends as
        // soon as the target gets meaningfully closer, or when every direction has been tried.
        private void UpdateFarmWalkProbe(Vector3 selfPos, float now)
        {
            float distance = Distance3D(selfPos, this.farmWalkProbeAim);
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

            if (this.farmWalkProbeStage == FarmWalkProbeStageHorizontal)
            {
                // The horizontal leg ends on DISTANCE, not on a stopwatch — same rule as the
                // back-off unstick, and for the same reason. A fixed 0.8 s of swimming covers
                // whatever the water lets it: shove off a reef and it is metres, jammed into a
                // crevice it is centimetres, and the probe then declared that direction "tried"
                // without having gone anywhere. 5 m is enough to be past most single blockers, and
                // matches FarmWalkBackOffDistance so both unsticks retreat by the same amount.
                //
                // The timer stays as the guard for exactly the case the distance test cannot end:
                // blocked in this direction too.
                if (this.farmWalkProbeLegFrom == Vector3.zero)
                {
                    this.farmWalkProbeLegFrom = selfPos;
                }

                bool farEnough = Distance3D(selfPos, this.farmWalkProbeLegFrom) >= FarmWalkProbeHorizontalDistance;
                if (!farEnough && now < this.farmWalkUnstickPhaseUntil)
                {
                    return;
                }

                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": probe leg "
                    + (this.farmWalkProbeIndex + 1) + "/" + FarmWalkProbeDirections + " ("
                    + FarmWalkProbeOffsets[this.farmWalkProbeIndex % FarmWalkProbeOffsets.Length].ToString("0")
                    + " deg) swam " + Distance3D(selfPos, this.farmWalkProbeLegFrom).ToString("F1") + "m of "
                    + FarmWalkProbeHorizontalDistance.ToString("0.#") + "m ("
                    + (farEnough ? "clear" : "blocked this way too") + ") — trying the vertical.");

                // Horizontal leg done; now try to move vertically from the new spot.
                this.farmWalkProbeStage = FarmWalkProbeStageVertical;
                this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeVerticalSeconds;
                return;
            }

            if (now < this.farmWalkUnstickPhaseUntil)
            {
                return;
            }

            // Vertical leg done — next direction.
            this.farmWalkProbeStage = FarmWalkProbeStageHorizontal;
            this.farmWalkProbeIndex++;
            this.farmWalkProbeLegFrom = Vector3.zero; // re-seeded on the next leg's first tick
            this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeHorizontalTimeout;

            if (this.farmWalkProbeIndex >= FarmWalkProbeDirections)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": probe exhausted all "
                    + FarmWalkProbeDirections + " directions at " + distance.ToString("F1") + "m.");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                this.ReleaseFarmWalkDepth();
            }
        }

        // ==========================================================================================
        // On-foot escape: press to a wedge, then hop with the steer taken AT THE APEX, repeating
        // whatever pays. Every number here was measured in-game rather than chosen.
        //
        //   HOLDING THE AXIS INTO A BLOCKER SUPPRESSES THE JUMP. Pressed against an obstacle, 43
        //   pulses over 15 s moved the body 0.16 m of path and never left the ground (grounded 100%
        //   throughout). Releasing the axis, jumping, and only steering once airborne moved it
        //   4.15 m over 26 pulses with grounded down to 32%. The old burst pushed and pulsed in the
        //   same frame, so against anything solid it was driving the suppressed mode.
        //
        //   THE STEER MUST WAIT FOR THE APEX. Same spot, same heading, three timings: axis applied
        //   with the jump gained +0.11 m of height, at lift-off +0.36 m, at the apex +4.00 m and
        //   +3.67 m on a repeat. Tenfold, and it depends on nothing else.
        //
        //   45 DEGREES, NOT THE PERPENDICULAR. Headings that carried the body out were 0 and +/-45
        //   (+1.24 m, +2.45 m closer); +/-90 measured about +0.31 m.
        //
        //   A CLIMB IS A SEQUENCE. One apex hop takes one step of it: run by hand, the same action
        //   five times running took a walker 21.0 m -> 13.4 m. The first two of those gained only
        //   +0.52 m and +0.60 m — under any sensible success bar — so the unit of judgement has to
        //   be the sequence, and a repetition that buys HEIGHT counts even when it loses ground.
        //
        //   PRESS FIRST, AND ONLY WHERE THERE IS A WEDGE. The same apex sweep from the raw block
        //   point failed on all nine headings; from a wedge found by pressing at 45 degrees its
        //   first heading gained +3.90 m and the walk arrived 3.5 s later. A press that never moves
        //   the body is not a wedge, it is a wall against the shoulder — that side is abandoned.
        // ==========================================================================================
        private const float FarmWalkEscapePressMaxDistance = 3.5f;

        // A press that gets this far without wedging has proved there is no obstacle. Just under the
        // press's own budget, so only a run that went the distance counts.
        private const float FarmWalkEscapePressOpenGround = 3f;
        private const float FarmWalkEscapePressMinSeconds = 1f;
        private const float FarmWalkEscapePressTimeout = 6f;
        private const float FarmWalkEscapePressSampleEvery = 0.3f;
        private const float FarmWalkEscapePressCrept = 0.1f;

        // Apex of the measured arc is ~0.42 s (peak +1.42 m, MotionConfig JumpingHighest 1.30 plus
        // the launch); 0.4 s puts the steer just under it.
        private const float FarmWalkEscapeApexDelay = 0.4f;
        private const float FarmWalkEscapeLandGrace = 0.35f;

        private const float FarmWalkEscapeAttemptSeconds = 1.8f;
        // Both were a shade too strict, and a heading was dropped for missing them by nothing:
        //     apex hop 45 -> +0,30m closer, +0,52m up — next heading
        // while it was still climbing half a metre per hop. Half a metre of height is real progress
        // on a climb; a quarter metre of ground is more than the noise on a landing.
        private const float FarmWalkEscapeRepeatCloser = 0.25f;
        private const float FarmWalkEscapeRepeatRise = 0.5f;
        private const int FarmWalkEscapeMaxRepeats = 6;
        private const float FarmWalkEscapeWin = 1f;
        private const float FarmWalkEscapeBudget = 22f;
        private const int FarmWalkMaxHopBursts = 3;

        private static readonly float[] FarmWalkEscapeHeadings = { 45f, -45f, 0f };

        // The plain thing first: run at it and jump, the way a player clears a kerb without thinking
        // about it. Against a solid blocker this is the SUPPRESSED mode — the axis is held into the
        // obstacle through the arc and the body does not leave the ground (measured: 43 impulses,
        // 0.16 m of travel, grounded 100%) — but most obstacles are not solid blockers, they are a
        // kerb, a root or a step, and one and a half seconds settles it. Only when this fails is the
        // press-and-apex apparatus worth its ten-odd seconds.
        private const int FarmWalkEscapeStageSimpleJump = 3;
        private const float FarmWalkEscapeSimpleSeconds = 1.5f;
        private const float FarmWalkEscapeSimpleJumpInterval = 0.35f;

        private const int FarmWalkEscapeStagePress = 0;
        private const int FarmWalkEscapeStageHop = 1;


        // `aim` is what the escape is trying to reach — the node on a final approach, the current
        // corner on a mid-route wedge. Progress is judged against it, so a hop that clears the
        // obstacle in front of us counts even when the node is still fifty metres away.
        private void BeginFarmWalkHopBurst(Vector3 selfPos, float now, float distance, Vector3 aim)
        {
            this.farmWalkHopBurstsUsed++;

            // ⚠️ A CAR CANNOT JUMP, AND THIS ESCAPE IS NOTHING BUT JUMPING.
            //
            // The vehicle has its own ladder — reverse, then pull out sideways, twice, then get out
            // and continue on foot — and it used to be reached from TryFarmWalkJump. Then all three
            // land call sites were rerouted here, to the apex escape, and nothing was left pointing
            // at the driver's one: TryFarmWalkJump now only runs once the hop-burst budget is spent,
            // so a wedged vehicle spent three escapes and the better part of a minute pogoing before
            // it ever tried reversing.
            //
            // Delegating here rather than at each call site keeps the rule in one place — a new
            // escape site cannot forget it, the way the underwater guard was forgotten twice.
            if (this.IsFarmWalkVehicleSteering())
            {
                if (this.TryGetNavMeshSelfPosition(out Vector3 vehiclePos, out _))
                {
                    this.BeginFarmWalkVehicleUnstick(vehiclePos, now, "wedged at " + distance.ToString("F1") + "m");
                    return;
                }

                // Position unreadable: do not fall through into a jump ladder the vehicle cannot
                // perform. Get out and let the next tick escape on foot.
                ModLogger.Msg("[FarmVehicle] wedged but the position did not resolve — getting out.");
                this.TryFarmWalkDismount("wedged, position unreadable");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                return;
            }

            // Backstop for the guard every caller is supposed to carry. Underwater the whole escape
            // is inert — no ground to launch from, no landing to end a phase — so it would sit out
            // its budget with the axis released while the water ladder waited its turn. Counted,
            // not silent: the burst is spent so this cannot re-arm every tick, and the line names
            // the leak if a new call site ever forgets the guard.
            if (this.farmWalkIsSwimming)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel
                    + ": apex escape asked for underwater at " + distance.ToString("F1")
                    + "m — refused, that ladder is for dry land.");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                return;
            }

            this.farmWalkEscapeWon = false;
            this.farmWalkUnstickPhase = FarmWalkUnstickHopBurst;
            this.farmWalkHopBurstAim = aim;
            this.farmWalkHopAnchor = selfPos;
            this.farmWalkHopBurstUntil = now + FarmWalkEscapeBudget;

            // ⚠️ PRESS TO A WEDGE FIRST, THEN HOP FROM IT. This is the order the standalone probe
            // measured on clean data, at the very obstacle this walk keeps failing at:
            //     apex sweep from the raw block point   9 headings, every one negative, best -0.19 m
            //     press 45 deg -> wedge at 1.1-1.5 m
            //       then apex-steer 45 deg              +3.90 m of height, ARRIVED 3.5 s later
            // Neither half does it alone: the press moves nothing by itself (grounded 100%, no jump)
            // and the sweep from the open goes backwards.
            //
            // An earlier version of this file hopped first, on the strength of numbers taken from
            // the mod's own logs — and those were worthless: the walker's ordinary steering ran in
            // the same frames and owned the axis, so every "press ran 3.79 m" and every "+6 m hop"
            // was that steering, not the escape. Do not reorder these stages again from mod logs
            // taken before the axis-ownership fix.
            // Always start with the ordinary jump; the press (or, if it has already proved barren in
            // this walk, the apex hops) is what happens when that does not clear it.
            this.farmWalkEscapeStage = FarmWalkEscapeStageSimpleJump;
            this.farmWalkEscapeSimpleUntil = now + FarmWalkEscapeSimpleSeconds;
            this.farmWalkEscapeSimpleJumpAt = 0f;
            this.farmWalkEscapePressSide = this.farmWalkEscapePressBarren ? 0f : 45f;
            this.farmWalkEscapeAttemptFrom = selfPos;
            this.farmWalkEscapeSteerDir = Vector3.zero;
            this.farmWalkEscapeAttemptUntil = now + FarmWalkEscapeAttemptSeconds;
            this.farmWalkEscapeHops = 0;
            this.farmWalkEscapeHopsRefused = 0;
            this.farmWalkEscapeFrames = 0;
            this.farmWalkEscapeAirFrames = 0;
            this.farmWalkEscapePressFrom = selfPos;
            this.farmWalkEscapePressSample = selfPos;
            this.farmWalkEscapePressSampleAt = now;
            this.farmWalkEscapeStageSince = now;
            this.farmWalkEscapeHeading = 0;
            this.farmWalkEscapeRepeats = 0;
            this.farmWalkApexPhase = 0;
            this.farmWalkApexPhaseSince = now;

            Vector3 toAimAtStart = aim - selfPos;
            toAimAtStart.y = 0f;
            toAimAtStart = toAimAtStart.sqrMagnitude > 0.0001f ? toAimAtStart.normalized : Vector3.forward;
            this.farmWalkEscapePressDir = Quaternion.Euler(0f, 45f, 0f) * toAimAtStart;

            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": wedged at " + distance.ToString("F1")
                + "m on foot — escape " + this.farmWalkHopBurstsUsed + "/" + FarmWalkMaxHopBursts
                + ": trying a plain running jump first.");
        }

        // Run at the obstacle and jump, holding the axis the whole way — the ordinary move. Ends on
        // the shared success test like any other stage, or hands over to the press when its second
        // and a half is up.
        private void UpdateFarmWalkEscapeSimpleJump(Vector3 selfPos, float now, Vector3 aimDir)
        {
            this.ApplyFarmWalkMoveAxis(aimDir, 1f);

            bool grounded = true, sliding = false;
            try
            {
                if (this.TryReadBunnyHopSurfaceState(out bool g, out bool sl))
                {
                    grounded = g;
                    sliding = sl;
                }
            }
            catch
            {
                grounded = true;
            }

            // ⚠️ STOP WHEN IT HAS WORKED, NOT WHEN THE TIMER RUNS OUT.
            //
            // The stage pulsed for a fixed 1.5 s — four or five jumps — whatever the first one
            // achieved. So the ordinary case, a kerb cleared by the very first jump, spent the next
            // second bouncing on open ground past the obstacle it had already beaten. The same
            // shape of mistake as running a drop on a stopwatch: the action has an outcome, so ask
            // for the outcome.
            //
            // Judged on the ground, and against the escape's own bar: a hop peaks 1.42 m, so an
            // airborne sample would call the top of every arc a success.
            if (grounded || sliding)
            {
                float clearedBy = HorizontalDistance(this.farmWalkHopAnchor, this.farmWalkHopBurstAim)
                    - HorizontalDistance(selfPos, this.farmWalkHopBurstAim);
                if (clearedBy >= FarmWalkEscapeWin && this.farmWalkEscapeHops > 0)
                {
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": over it on the "
                        + this.farmWalkEscapeHops + (this.farmWalkEscapeHops == 1 ? "st" : "th")
                        + " plain jump — " + clearedBy.ToString("+0.00;-0.00; 0.00") + "m closer, "
                        + (selfPos.y - this.farmWalkHopAnchor.y).ToString("+0.00;-0.00; 0.00")
                        + "m up — ending the escape rather than jumping on.");
                    this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                    this.TryClearGameMoveAxis();
                    this.farmWalkStuckStrikes = 0;
                    this.farmWalkLastSample = selfPos;
                    this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;
                    this.farmWalkBestAt = now;
                    return;
                }
            }

            if ((grounded || sliding) && now - this.farmWalkEscapeSimpleJumpAt >= FarmWalkEscapeSimpleJumpInterval)
            {
                this.farmWalkEscapeSimpleJumpAt = now;
                this.farmWalkEscapeHops++;
                try
                {
                    if (!this.TryBunnyHopJumpViaMono())
                    {
                        this.farmWalkEscapeHopsRefused++;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[FarmWalk] plain jump threw: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            if (now < this.farmWalkEscapeSimpleUntil)
            {
                return;
            }

            float gained = HorizontalDistance(this.farmWalkHopAnchor, this.farmWalkHopBurstAim)
                - HorizontalDistance(selfPos, this.farmWalkHopBurstAim);
            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": plain running jump -> "
                + gained.ToString("+0.00;-0.00; 0.00") + "m closer, "
                + (selfPos.y - this.farmWalkHopAnchor.y).ToString("+0.00;-0.00; 0.00") + "m up, "
                + this.farmWalkEscapeHops + " jump(s)"
                + (this.farmWalkEscapeHopsRefused > 0 ? " (" + this.farmWalkEscapeHopsRefused + " REFUSED)" : string.Empty)
                + " — " + (this.farmWalkEscapePressBarren
                    ? "apex-hopping from here."
                    : "pressing 45 deg for a corner."));

            this.farmWalkEscapeStage = this.farmWalkEscapePressBarren
                ? FarmWalkEscapeStageHop
                : FarmWalkEscapeStagePress;
            this.farmWalkEscapeStageSince = now;
            this.farmWalkEscapeAttemptFrom = selfPos;
            this.farmWalkEscapeSteerDir = Vector3.zero;
            this.farmWalkEscapeAttemptUntil = now + FarmWalkEscapeAttemptSeconds;
            this.farmWalkEscapeHops = 0;
            this.farmWalkEscapeHopsRefused = 0;
            this.farmWalkEscapeFrames = 0;
            this.farmWalkEscapeAirFrames = 0;
            this.farmWalkApexPhase = 0;
            this.farmWalkApexPhaseSince = now;
            this.farmWalkEscapePressFrom = selfPos;
            this.farmWalkEscapePressSample = selfPos;
            this.farmWalkEscapePressSampleAt = now;
            this.farmWalkEscapePressSide = 45f;
            this.farmWalkEscapePressDir = Quaternion.Euler(0f, 45f, 0f) * aimDir;
        }

        // The press leg. Ends when the body stops creeping (that is the wedge), when it has run far
        // enough that there is plainly nothing to wedge against, or when it never moved at all.
        private bool UpdateFarmWalkEscapePress(Vector3 selfPos, float now)
        {
            // ⚠️ FIXED DIRECTION. Recomputing it from the live bearing to the aim every frame turns
            // "press at 45 degrees" into an arc AROUND the aim as that bearing swings — and an arc
            // has nothing to run into. That is why every press on this node reported "no wedge, it
            // just runs on" after three or four metres: it was never pressing, it was circling.
            this.ApplyFarmWalkMoveAxis(this.farmWalkEscapePressDir, 1f);

            if (now - this.farmWalkEscapePressSampleAt < FarmWalkEscapePressSampleEvery)
            {
                return false;
            }

            float crept = HorizontalDistance(selfPos, this.farmWalkEscapePressSample);
            float ran = HorizontalDistance(selfPos, this.farmWalkEscapePressFrom);
            this.farmWalkEscapePressSample = selfPos;
            this.farmWalkEscapePressSampleAt = now;

            bool elapsed = now - this.farmWalkEscapeStageSince >= FarmWalkEscapePressMinSeconds;
            bool wedged = elapsed && crept < FarmWalkEscapePressCrept;
            bool overrun = ran >= FarmWalkEscapePressMaxDistance
                || now - this.farmWalkEscapeStageSince >= FarmWalkEscapePressTimeout;
            if (!wedged && !overrun)
            {
                return false;
            }

            // A press that never moved is a wall, not a corner: that side has nothing to offer and
            // repeating it would be a second of pressing followed by hops on the spot.
            bool useless = ran < 0.3f || overrun;

            // ⚠️ "RAN FREE" AND "HIT A WALL" ARE OPPOSITE FINDINGS, and this used to treat them as
            // one. A press that covers its whole four metres at 45 degrees without wedging is proof
            // that the body is in the OPEN — there is nothing here to climb, and hopping is theatre:
            //     plain running jump -> +4,54m closer, +1,92m up, 2 jump(s)
            //     pressed  45 deg for 1,2s, 4,28m — no wedge, it just runs on.
            //     pressed -45 deg for 1,2s, 4,12m — no wedge, it just runs on.
            //     escape has cleared it at 45 deg — carrying on while it still pays.
            // Two free runs, then a hop series, on flat ground where the walker was never blocked.
            // Whatever stalled the walk was not geometry in front of us, so the escape has nothing
            // to solve; hand back and let the ordinary walk continue.
            bool ranFree = ran >= FarmWalkEscapePressOpenGround;
            if (ranFree)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": pressed "
                    + this.farmWalkEscapePressSide.ToString("F0") + " deg and ran " + ran.ToString("F2")
                    + "m without wedging — nothing is blocking us, ending the escape.");
                this.farmWalkEscapePressBarren = true;
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                this.TryClearGameMoveAxis();
                this.farmWalkStuckStrikes = 0;
                this.farmWalkLastSample = selfPos;
                this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;
                this.farmWalkBestAt = now;      // the walk was never stuck; do not let it time out on this
                return true;
            }
            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": pressed "
                + this.farmWalkEscapePressSide.ToString("F0") + " deg for "
                + (now - this.farmWalkEscapeStageSince).ToString("F1") + "s, " + ran.ToString("F2")
                + "m — " + (useless
                    ? (ran < 0.3f ? "NEVER STARTED, that side is a wall" : "no wedge, it just runs on")
                    : "wedged, hopping from here") + ".");

            if (!useless)
            {
                // Drop the press axis the instant the wedge is found: leaving it set for even one
                // frame is enough to swallow the first hop, since that is the frame the hop launches
                // on.
                this.TryClearGameMoveAxis();

                // Re-anchor: the hops that follow are judged on what THEY achieve from the wedge,
                // not on paying off the walk that got here.
                // The side is spent: without this, hops that fail from the wedge fall back into
                // pressing the SAME 45 degrees again from the new spot, over and over until the
                // budget runs out.
                this.farmWalkEscapePressSide = this.farmWalkEscapePressSide > 0f ? -45f : 0f;
                this.farmWalkHopAnchor = selfPos;
                this.farmWalkEscapeStage = FarmWalkEscapeStageHop;
                this.farmWalkEscapeStageSince = now;
                this.farmWalkEscapeHeading = 0;
                this.farmWalkEscapeRepeats = 0;
                this.farmWalkEscapeAttemptFrom = selfPos;
                this.farmWalkEscapeSteerDir = Vector3.zero;
                this.farmWalkEscapeAttemptUntil = now + FarmWalkEscapeAttemptSeconds;
                this.farmWalkApexPhase = 0;
                this.farmWalkApexPhaseSince = now;
                return false;
            }

            // Try the mirror side once, then give up on pressing and hop from where we stand.
            if (this.farmWalkEscapePressSide > 0f)
            {
                Vector3 toAim = this.farmWalkHopBurstAim - selfPos;
                toAim.y = 0f;
                toAim = toAim.sqrMagnitude > 0.0001f ? toAim.normalized : Vector3.forward;

                this.farmWalkEscapePressSide = -45f;
                this.farmWalkEscapePressFrom = selfPos;
                this.farmWalkEscapePressSample = selfPos;
                this.farmWalkEscapePressSampleAt = now;
                this.farmWalkEscapePressDir = Quaternion.Euler(0f, -45f, 0f) * toAim;
                this.farmWalkEscapeStageSince = now;
                return false;
            }

            // Both sides pressed and neither gave a corner. Hop from here anyway — weak is not
            // nothing — but ⚠️ DO NOT re-anchor.
            //
            // Re-taking the anchor here looked fair ("the hops should not pay for the presses'
            // wandering") and produced fake victories instead: the presses put the body 4 m + 4 m off
            // the block point, the hops recovered a metre of that against the NEW anchor, and the
            // escape announced "cleared it" three times running while the walk advanced 21,1 -> 20,5
            // -> 20,3 m. Eight tenths of a metre for three escapes and twenty seconds.
            //
            // What the walk needs to know is whether it got past the obstacle, and that is measured
            // from where it was blocked. Paying off a debt is not progress.
            this.farmWalkEscapePressBarren = true;
            this.farmWalkEscapePressSide = 0f;
            this.farmWalkEscapeStage = FarmWalkEscapeStageHop;
            this.farmWalkEscapeStageSince = now;
            this.farmWalkEscapeHeading = 0;
            this.farmWalkEscapeRepeats = 0;
            this.farmWalkEscapeAttemptFrom = selfPos;
            this.farmWalkEscapeSteerDir = Vector3.zero;
            this.farmWalkEscapeAttemptUntil = now + FarmWalkEscapeAttemptSeconds;
            this.farmWalkEscapeHops = 0;
            this.farmWalkEscapeHopsRefused = 0;
            this.farmWalkEscapeFrames = 0;
            this.farmWalkEscapeAirFrames = 0;
            this.farmWalkApexPhase = 0;
            this.farmWalkApexPhaseSince = now;
            return false;
        }


        // One apex hop: stand still, launch, wait for the top, and only then steer. Returns the
        // direction to drive this frame, or zero while the body must stay unsteered.
        // The heading a hop attempt steers on, FROZEN IN WORLD SPACE for the whole attempt.
        //
        // ⚠️ NEVER rotate a LIVE bearing by the heading. Chasing a point while holding a constant
        // angle off the bearing to it is not a diagonal, it is a CIRCLE: the direction turns with
        // you, and the body orbits the aim at a fixed radius. The escape's aim is often the current
        // corner, two or three metres away, where one lap takes about six seconds — so the walker
        // spent whole escapes hopping round and round the corner, the log reading gain, stall, lose
        // (+1.43 m, +0.06 m, -0.38 m) with airborne at 85%. Closer in it is worse: the bearing rate
        // blows up near the point itself, and the heading whips through every direction at once.
        //
        // Straight ahead is exempt and stays live — pursuit at zero offset IS a straight line, and
        // a live bearing keeps homing after a hop shoves the body sideways.
        private void AimFarmWalkEscapeAttempt(Vector3 selfPos, float heading)
        {
            Vector3 toAim = this.farmWalkHopBurstAim - selfPos;
            toAim.y = 0f;
            toAim = toAim.sqrMagnitude > 0.0001f ? toAim.normalized : Vector3.forward;
            this.farmWalkEscapeSteerDir = Quaternion.Euler(0f, heading, 0f) * toAim;
        }

        private Vector3 FarmWalkApexHopSteer(float now, Vector3 dir, bool grounded, bool sliding)
        {
            bool onGround = grounded || sliding;

            if (this.farmWalkApexPhase == 0)
            {
                if (onGround)
                {
                    // ⚠️ RELEASE BEFORE THE IMPULSE, NOT AFTER. The caller decides what to do with
                    // the axis only once this method returns, so an impulse sent here goes out while
                    // the axis still holds LAST frame's value — the final frame of the press, or the
                    // previous hop's steer. Pressed into the very corner we just wedged against, that
                    // suppresses the jump outright:
                    //     apex hop  45 deg -> airborne 0%    (same heading as the press)
                    //     apex hop -45 deg -> airborne 41%, 69%
                    //     apex hop   0 deg -> airborne 25%
                    // The heading that matched the press direction never left the ground once.
                    this.TryClearGameMoveAxis();

                    this.farmWalkEscapeHops++;
                    try
                    {
                        if (!this.TryBunnyHopJumpViaMono())
                        {
                            this.farmWalkEscapeHopsRefused++;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.farmWalkEscapeHopsRefused++;
                        ModLogger.Msg("[FarmWalk] apex hop threw: " + ex.GetType().Name + ": " + ex.Message);
                    }

                    this.farmWalkApexPhase = 1;
                    this.farmWalkApexPhaseSince = now;
                }

                return Vector3.zero;
            }

            if (this.farmWalkApexPhase == 1)
            {
                // ⚠️ WAIT THE FULL DELAY. This used to advance early once the body was airborne,
                // which put the steer in at about 0.2 s — halfway UP the arc, not at the top of it.
                // The apex of the measured jump is ~0.42 s (peak +1.42 m), and steering before it is
                // a different move with different results: the probe measured the same heading at
                // +4.00 m of height when the axis went in at the apex and +0.36 m when it went in at
                // lift-off. The delay is the whole technique; there is no shortcut worth taking.
                if (now - this.farmWalkApexPhaseSince >= FarmWalkEscapeApexDelay)
                {
                    this.farmWalkApexPhase = 2;
                    this.farmWalkApexPhaseSince = now;
                }

                return Vector3.zero;
            }

            // Touchdown ends the steer. Holding it through the landing means the NEXT impulse goes
            // out with the axis already pressed into the obstacle for a third of a second — the same
            // suppression, just moved from the first hop of a series to every later one. Release on
            // contact, and only then take the settle time before launching again.
            if (onGround)
            {
                if (now - this.farmWalkApexPhaseSince >= FarmWalkEscapeLandGrace)
                {
                    this.farmWalkApexPhase = 0;
                    this.farmWalkApexPhaseSince = now;
                }

                return Vector3.zero;
            }

            return dir;
        }

        // Drives the escape: press to a wedge, then apex hops on 45 / -45 / 0, repeating a heading
        // while it keeps buying ground or height.
        private void UpdateFarmWalkHopBurst(Vector3 selfPos, float now)
        {
            if (now >= this.farmWalkHopBurstUntil)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": escape budget spent.");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                return;
            }

            Vector3 aimDir = this.farmWalkHopBurstAim - selfPos;
            aimDir.y = 0f;
            aimDir = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector3.forward;

            if (this.farmWalkEscapeStage == FarmWalkEscapeStageSimpleJump)
            {
                this.UpdateFarmWalkEscapeSimpleJump(selfPos, now, aimDir);
                return;
            }

            if (this.farmWalkEscapeStage == FarmWalkEscapeStagePress)
            {
                this.UpdateFarmWalkEscapePress(selfPos, now);
                return;
            }


            bool grounded = true, sliding = false;
            try
            {
                if (this.TryReadBunnyHopSurfaceState(out bool g, out bool sl))
                {
                    grounded = g;
                    sliding = sl;
                }
            }
            catch
            {
                grounded = true;
            }

            this.farmWalkEscapeFrames++;
            if (!(grounded || sliding))
            {
                this.farmWalkEscapeAirFrames++;
            }

            float heading = FarmWalkEscapeHeadings[this.farmWalkEscapeHeading % FarmWalkEscapeHeadings.Length];
            if (heading != 0f && this.farmWalkEscapeSteerDir.sqrMagnitude < 0.0001f)
            {
                this.AimFarmWalkEscapeAttempt(selfPos, heading);
            }

            Vector3 steer = this.FarmWalkApexHopSteer(now,
                heading != 0f ? this.farmWalkEscapeSteerDir : aimDir, grounded, sliding);
            if (steer != Vector3.zero)
            {
                this.ApplyFarmWalkMoveAxis(steer, 1f);
            }
            else
            {
                this.TryClearGameMoveAxis();
            }

            // Cleared it? Judged landed and horizontally, against the aim, from where the escape
            // began — a hop peaks 1.42 m, so a 3-D test would call the top of every arc a success.
            // ⚠️ DO NOT STOP AT THE FIRST METRE OF A CLIMB IN PROGRESS.
            //
            // Success used to end the escape the instant it was met — mid-attempt, mid-series. The
            // walker then walked, met the same slope a second later, and started a fresh escape: one
            // climb arrived in the log as three, one or two hops each, 21,1 -> 19,9 -> 17,6 m over
            // twenty-five seconds, and the walk gave up anyway. The bar says "this escape has earned
            // its keep", not "there is nothing left to gain".
            //
            // So the win is REMEMBERED and the series carries on; the escape ends when an attempt
            // stops paying, and reports everything it took.
            if ((grounded || sliding)
                && HorizontalDistance(this.farmWalkHopAnchor, this.farmWalkHopBurstAim)
                    - HorizontalDistance(selfPos, this.farmWalkHopBurstAim) >= FarmWalkEscapeWin)
            {
                if (!this.farmWalkEscapeWon)
                {
                    this.farmWalkEscapeWon = true;
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": escape has cleared it at "
                        + heading.ToString("F0") + " deg — "
                        + (HorizontalDistance(this.farmWalkHopAnchor, this.farmWalkHopBurstAim)
                            - HorizontalDistance(selfPos, this.farmWalkHopBurstAim)).ToString("+0.00;-0.00; 0.00")
                        + "m closer, " + (selfPos.y - this.farmWalkHopAnchor.y).ToString("+0.00;-0.00; 0.00")
                        + "m up so far — carrying on while it still pays.");
                }
            }


            // Attempts end on the ground, never mid-arc: the gain arrives on the landing, and a
            // window that closes in flight writes off the hop that was working.
            if (now < this.farmWalkEscapeAttemptUntil || !(grounded || sliding))
            {
                return;
            }

            float closer = HorizontalDistance(this.farmWalkEscapeAttemptFrom, this.farmWalkHopBurstAim)
                - HorizontalDistance(selfPos, this.farmWalkHopBurstAim);
            float rise = selfPos.y - this.farmWalkEscapeAttemptFrom.y;
            bool paid = closer >= FarmWalkEscapeRepeatCloser || rise >= FarmWalkEscapeRepeatRise;
            float airborneShare = this.farmWalkEscapeFrames > 0
                ? 100f * this.farmWalkEscapeAirFrames / this.farmWalkEscapeFrames
                : 0f;

            float fromAnchor = HorizontalDistance(this.farmWalkHopAnchor, this.farmWalkHopBurstAim)
                - HorizontalDistance(selfPos, this.farmWalkHopBurstAim);
            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": apex hop " + heading.ToString("F0")
                + " deg -> " + closer.ToString("+0.00;-0.00; 0.00") + "m closer this go, "
                + fromAnchor.ToString("+0.00;-0.00; 0.00") + "m since the escape began, "
                + rise.ToString("+0.00;-0.00; 0.00") + "m up, " + this.farmWalkEscapeHops + " hop(s)"
                + (this.farmWalkEscapeHopsRefused > 0 ? " (" + this.farmWalkEscapeHopsRefused + " REFUSED)" : string.Empty)
                + ", airborne " + airborneShare.ToString("F0") + "%"
                + (paid ? " — repeating." : " — next heading."));

            if (paid && this.farmWalkEscapeRepeats < FarmWalkEscapeMaxRepeats)
            {
                this.farmWalkEscapeRepeats++;
            }
            else if (this.farmWalkEscapeWon)
            {
                // Earned its metre and has now stopped paying: hand back with the whole tally.
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": escape done at "
                    + heading.ToString("F0") + " deg — " + fromAnchor.ToString("+0.00;-0.00; 0.00")
                    + "m closer, " + (selfPos.y - this.farmWalkHopAnchor.y).ToString("+0.00;-0.00; 0.00")
                    + "m up since it began, airborne " + airborneShare.ToString("F0") + "%.");
                this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                this.farmWalkStuckStrikes = 0;
                this.farmWalkLastSample = selfPos;
                this.farmWalkNextStuckSampleAt = now + FarmWalkStuckSampleInterval;
                return;
            }
            else
            {
                this.farmWalkEscapeRepeats = 0;
                this.farmWalkEscapeHeading++;
                if (this.farmWalkEscapeHeading >= FarmWalkEscapeHeadings.Length)
                {
                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel
                        + ": every apex heading tried; " + fromAnchor.ToString("+0.00;-0.00; 0.00")
                        + "m closer since the escape began, needed "
                        + FarmWalkEscapeWin.ToString("F1") + "m"
                        + (this.farmWalkEscapePressSide != 0f
                            ? " — pressing " + this.farmWalkEscapePressSide.ToString("F0") + " deg for a corner."
                            : "."));

                    // Hops from this wedge are spent. If the other side has not been pressed yet,
                    // go and find its corner; otherwise the escape is done.
                    if (this.farmWalkEscapePressSide != 0f)
                    {
                        this.farmWalkEscapeStage = FarmWalkEscapeStagePress;
                        this.farmWalkEscapePressFrom = selfPos;
                        this.farmWalkEscapePressSample = selfPos;
                        this.farmWalkEscapePressSampleAt = now;
                        this.farmWalkEscapePressDir =
                            Quaternion.Euler(0f, this.farmWalkEscapePressSide, 0f) * aimDir;
                        this.farmWalkEscapeStageSince = now;
                        return;
                    }

                    this.farmWalkUnstickPhase = FarmWalkUnstickIdle;
                    return;
                }
            }

            this.farmWalkEscapeAttemptFrom = selfPos;
            this.farmWalkEscapeSteerDir = Vector3.zero;
            this.farmWalkEscapeAttemptUntil = now + FarmWalkEscapeAttemptSeconds;
            this.farmWalkEscapeHops = 0;
            this.farmWalkEscapeHopsRefused = 0;
            this.farmWalkEscapeFrames = 0;
            this.farmWalkEscapeAirFrames = 0;
            this.farmWalkApexPhase = 0;
            this.farmWalkApexPhaseSince = now;
        }

        // `aim` is what the sweep is trying to reach — the node on a final approach, the current
        // corner on a mid-route wedge. It sets BOTH the bearing the offsets rotate around and the
        // distance progress is judged by; using the node for a wedge thirty metres out would point
        // the first reversal at the wrong thing and then fail to notice a successful detour.
        private void BeginFarmWalkProbe(Vector3 selfPos, float now, float distance, Vector3 aim)
        {
            this.farmWalkProbeUsed = true;
            this.farmWalkUnstickPhase = FarmWalkUnstickProbing;
            this.farmWalkProbeIndex = 0;
            this.farmWalkProbeStage = FarmWalkProbeStageHorizontal;
            this.farmWalkProbeBestDistance = distance;
            this.farmWalkProbeAim = aim;
            this.farmWalkProbeLegFrom = Vector3.zero; // seeded on the first tick of the leg
            this.farmWalkUnstickPhaseUntil = now + FarmWalkProbeHorizontalTimeout;

            // Bearing we were pushing toward the aim; the probe reverses it first.
            Vector3 toTarget = aim - selfPos;
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
            float dy = this.ResolveFarmWalkDepthAim().y - selfPos.y;

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
                // Whichever way this round decided to clear — up on odd rounds, down on even.
                want = this.farmWalkBackOffVerticalDir >= 0 ? 1 : -1;
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
                    // ⚠️ SAY WHAT THE PREVIOUS HOLD BOUGHT. Twenty-five bare "diving 5,0m" lines in
                    // forty seconds read as a descent in progress; they were a descent that never
                    // started, and nothing in the log said so. The gain since the last assertion is
                    // the one number that separates the two.
                    string gained = string.Empty;
                    if (!float.IsNaN(this.farmWalkDepthAssertFrom))
                    {
                        gained = " (last hold moved " + (selfPos.y - this.farmWalkDepthAssertFrom).ToString("+0.00;-0.00; 0.00")
                            + "m in " + (now - this.farmWalkDepthAssertStartedAt).ToString("F1") + "s)";
                    }

                    ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + (want > 0 ? "surfacing" : "diving")
                        + " " + Mathf.Abs(dy).ToString("F1") + "m" + gained + ".");
                }

                this.farmWalkDepthAssertFrom = selfPos.y;
                this.farmWalkDepthAssertStartedAt = now;
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

        // ── LINE-OF-SIGHT SHORTCUTTING ─────────────────────────────────────
        // A* returns the GRAPH path, not the path a player would take. On the 1745-node land graph
        // that meant 11 corners to cover 12 m — a corner every metre, threading a dense waypoint
        // cluster instead of crossing the gap, which is the "weird route around" report.
        //
        // The game does not follow its raw path either: TrackingPathModule.GetPath drops corners
        // while _HasNoCollider says the line is clear, and connects straight under
        // tryLineConnectDis (~10 m). Same idea here, using the same primitive —
        // PhysicsExtension.Linecast, which is the GAME's physics; the mod's own UnityEngine.Physics
        // is dead on this build. Masks are read from TrackingPathModule's own statics rather than
        // rebuilt from layer names, so they cannot drift from what the game tests against.
        //
        // Runs at route-build time only (every ~1.5 s re-path), never per frame.
        // 10 m, matching the game's own tryLineConnectDis (100 sqr). A 30 m span let a single
        // foot-level ray justify erasing an entire detour.
        private const float FarmWalkShortcutMaxSpan = 10f;
        private const int FarmWalkShortcutMaxRemovals = 3;
        // Obstacles are rarely blocking at ankle height. Testing only along the feet-to-feet line
        // let rays slip under railings, over dips and through gaps, reporting "clear" across
        // geometry the player cannot actually walk through — so the line is tested at chest height
        // as well, and BOTH must be clear.
        private const float FarmWalkShortcutProbeLift = 1.2f;

        private static readonly string[] FarmWalkPhysicsImageNames =
        {
            "EngineWrapper", "EngineWrapper.dll",
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };
        private IntPtr farmWalkLinecastMethod;
        private int farmWalkMaskAll;
        private int farmWalkMaskPassable;
        private bool farmWalkLinecastResolved;
        private float farmWalkLinecastRetryAt;

        private bool EnsureFarmWalkLinecast()
        {
            if (this.farmWalkLinecastMethod != IntPtr.Zero && this.farmWalkMaskAll != 0)
            {
                return true;
            }

            // Retry periodically rather than latching a failure: a first attempt can land before
            // the image carrying PhysicsExtension is loaded, and a permanent latch would then
            // disable shortcutting for the whole session.
            if (Time.unscaledTime < this.farmWalkLinecastRetryAt)
            {
                return false;
            }

            this.farmWalkLinecastRetryAt = Time.unscaledTime + 30f;
            bool firstAttempt = !this.farmWalkLinecastResolved;
            this.farmWalkLinecastResolved = true;
            try
            {
                // PhysicsExtension lives in the ENGINEWRAPPER image, not XDTLevelAndEntity —
                // namespace is not assembly (the recurring trap in this codebase). Searching the
                // TrackingPathModule image list resolved the masks but not the method.
                IntPtr physicsClass = this.FindAuraMonoClassInImages(
                    "MonoGame.ScriptFramework", "PhysicsExtension", FarmWalkPhysicsImageNames);
                if (physicsClass == IntPtr.Zero)
                {
                    // Last resort if the image list ever goes stale.
                    physicsClass = this.FindAuraMonoClassByFullName("MonoGame.ScriptFramework.PhysicsExtension");
                }

                if (physicsClass != IntPtr.Zero)
                {
                    this.farmWalkLinecastMethod = this.FindAuraMonoMethodOnHierarchy(physicsClass, "Linecast", 4);
                }

                IntPtr moduleClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.GameplaySystem.TrackingPoint", "TrackingPathModule", TrackPathModuleImageNames);
                if (moduleClass != IntPtr.Zero)
                {
                    this.TryReadAuraMonoStaticIntField(moduleClass, new[] { "All" }, out this.farmWalkMaskAll);
                    this.TryReadAuraMonoStaticIntField(moduleClass, new[] { "Passable" }, out this.farmWalkMaskPassable);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmWalk] linecast resolve threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            bool ready = this.farmWalkLinecastMethod != IntPtr.Zero && this.farmWalkMaskAll != 0;

            // Log the first attempt and every success; stay quiet on repeat failures so a build
            // without PhysicsExtension does not spam a line every 30 s.
            if (ready || firstAttempt)
            {
                ModLogger.Msg("[FarmWalk] route shortcutting "
                    + (ready ? "ready" : "UNAVAILABLE (following raw graph corners)")
                    + " (Linecast=" + (this.farmWalkLinecastMethod != IntPtr.Zero)
                    + ", maskAll=" + this.farmWalkMaskAll + ", maskPassable=" + this.farmWalkMaskPassable + ").");
            }

            return ready;
        }

        // True when nothing solid sits between the two points (mirrors _HasNoCollider).
        private bool IsFarmWalkLineClear(Vector3 from, Vector3 to, int layerMask)
        {
            Vector3 liftFrom = from;
            Vector3 liftTo = to;
            liftFrom.y += FarmWalkShortcutProbeLift;
            liftTo.y += FarmWalkShortcutProbeLift;
            return this.IsFarmWalkRayClear(from, to, layerMask)
                && this.IsFarmWalkRayClear(liftFrom, liftTo, layerMask);
        }

        private unsafe bool IsFarmWalkRayClear(Vector3 from, Vector3 to, int layerMask)
        {
            if (this.farmWalkLinecastMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            Vector3 a = from;
            Vector3 b = to;
            int mask = layerMask;
            int queryTrigger = 1; // QueryTriggerInteraction.Ignore
            IntPtr* args = stackalloc IntPtr[4];
            args[0] = (IntPtr)(&a);
            args[1] = (IntPtr)(&b);
            args[2] = (IntPtr)(&mask);
            args[3] = (IntPtr)(&queryTrigger);
            IntPtr boxed = auraMonoRuntimeInvoke(this.farmWalkLinecastMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out bool hit))
            {
                return false; // unreadable: assume blocked, i.e. keep the corner
            }

            return !hit;
        }

        // Drop every corner we can see past. Walks from the FAR end so one test can remove a whole
        // run of corners, and only considers spans short enough to be a plausible straight line.
        private void ShortcutFarmWalkRoute(Vector3 from, System.Collections.Generic.List<Vector3> corners)
        {
            if (corners.Count < 2 || !this.EnsureFarmWalkLinecast())
            {
                return;
            }

            // Mirrors TrackingPathModule: drop ONLY the first corner, and only when the line to the
            // second is clear — one at a time, a bounded number of times.
            //
            // The previous version took the FARTHEST visible corner and deleted everything before
            // it, which collapsed routes to a beeline (18.9 m in 2 corners) and walked the player
            // straight into buildings. A single clear line at foot level is not evidence that a
            // whole detour is redundant; it usually just means the ray slipped through a gap.
            int removed = 0;
            for (int pass = 0; pass < FarmWalkShortcutMaxRemovals && corners.Count >= 2; pass++)
            {
                Vector3 first = corners[0];
                Vector3 second = corners[1];

                // Only worth cutting if going direct is actually shorter, and only over a short
                // span — the game's own straight-line optimisation stops at ~10 m for this reason.
                if (HorizontalDistance(from, second) > FarmWalkShortcutMaxSpan
                    || HorizontalDistance(from, second) >= HorizontalDistance(from, first) + HorizontalDistance(first, second))
                {
                    break;
                }

                if (!this.IsFarmWalkLineClear(from, second, this.farmWalkMaskAll))
                {
                    break;
                }

                corners.RemoveAt(0);
                removed++;
            }

            if (removed > 0)
            {
                this.AutoFarmLog("[FarmWalk] shortcut removed " + removed + " corner(s), "
                    + corners.Count + " left.");
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Steering and corner logic stay horizontal (the walker cannot climb, so height is noise
        // there), but anything compared against the aura's reach must be 3-D.
        private static float Distance3D(Vector3 a, Vector3 b)
        {
            return (a - b).magnitude;
        }
    }
}
