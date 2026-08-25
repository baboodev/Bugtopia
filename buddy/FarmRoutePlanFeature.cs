using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // The node-tour planner.
    //
    // WHY. The farm used to take the NEAREST node on every scan. For teleporting that did not
    // matter — a hop costs the same from anywhere. For walking, a greedy pick falls apart: the
    // nearest node leads into a dead end, the way back is recomputed, and the route jitters. On top
    // of that the candidate list was rebuilt from scratch every 2 seconds, so the target could
    // change in the middle of a walk.
    //
    // WHAT WE DO. Build the sequence once, then only:
    //   * walk to the HEAD of the list,
    //   * drop the head once it is collected,
    //   * INSERT new points without rebuilding anything.
    //
    // ALGORITHM. This is an open travelling salesman (a route with no return to the start). The
    // classic pair for problems this size:
    //   1. Nearest neighbour — a quick initial sequence (about 25% worse than optimal on average);
    //   2. 2-opt — reverse a segment when that shortens the route (brings it to roughly 5%).
    // Both are O(n²) per pass; at n ≤ FarmTourMaxStops that is tens of microseconds.
    //
    // New points are added by CHEAPEST INSERTION: find the position i that minimises
    //     d(t[i-1], p) + d(p, t[i]) − d(t[i-1], t[i])
    // — that is, where the point can be squeezed in so the route grows least. The existing order is
    // left alone, which means the current walk's target cannot move out from under it.
    //
    // ⚠️ THE TOUR HEAD IS NOT TOUCHED while a walk is running. 2-opt works on the tail only
    // (indices ≥ 1); otherwise the optimiser reorders the current target and the walker turns
    // around — exactly the illness behind "the route kept rebuilding back and forth".
    //
    // ⚠️ Distances are horizontal. Walking does not change height, and counting Y made points that
    // are neighbours on the ground read as "far" purely because one of them sits on a rock.
    public partial class HeartopiaComplete
    {
        internal readonly struct FarmTourStop
        {
            internal readonly Vector3 Position;
            internal readonly string Label;

            internal FarmTourStop(Vector3 position, string label)
            {
                this.Position = position;
                this.Label = label;
            }
        }

        // Enough points for any gathering area, and 2-opt over them is instant.
        private const int FarmTourMaxStops = 48;

        // Two points closer than this are the same resource. The same threshold the scan uses
        // (recentlyVisitedNodes matches within 2 m), so duplicates cannot diverge between systems.
        private const float FarmTourSameStopDistance = 2f;

        // How many 2-opt passes to run. Almost all of the gain comes out in the first two.
        private const int FarmTourTwoOptPasses = 4;

        // Points further than this from the player do not join the tour: a tour should cover an
        // area, not the map.
        //
        // It was 120 m, and it stood there for the move between areas: after a teleport the old plan
        // described a place we are no longer in and dragged the player back. That job has since been
        // closed properly in three places at once — the tour resets on any `area:*` teleport (the one
        // point every farm teleport passes through), on arriving in an area on foot, and on toggling
        // the farm. The distance cut-off stayed as a second, blunt line of the same defence.
        //
        // It cost real things meanwhile: with a single resource kind enabled and the nearest marker
        // 135 m out, the farm sat in "Scanning for nodes" forever and said nothing about it
        // (measured 2026-08-20: two Bubble markers at 146 and 135 m, zero candidates). It was also
        // asymmetric — the teleport mode has no limit at all, so the walking one was stricter for no
        // reason.
        //
        // 300 m is roughly a minute of swimming: still an area, not the map. The walk deadline takes
        // it: clamp(straight line × 3 + 15, 20, 300) gives the full 300 s on a haul that long. The
        // number of points is capped separately (FarmTourMaxStops), so 2-opt's cost is unaffected.
        //
        // ⚠️ For a DRIFTING target a haul like that is meaningless in principle: a bubble moves at
        // about 1.5 m/s and in a minute is ninety metres away or has burst. There is no separate,
        // shorter limit for consumable targets here yet — if the chase starts going nowhere, the
        // bound has to come from the nature of the target, not from distance.
        private const float FarmTourMaxStopRange = 300f;

        // The tour head among consumable targets, held between calls. Without it the pick is
        // recomputed every tick and the route is replayed on every step the player takes.
        private Vector3 farmTourTransientLock;
        private bool farmTourHasTransientLock;

        // How much nearer a new consumable target must be before the current one is dropped. Bubbles
        // drift at ~1.5 m/s and sit tens of metres apart, so the margin has to cover both the drift
        // and the difference that accrues from the player simply moving.
        private const float FarmTourTransientSwitchMargin = 25f;

        // The range at which "the scan does not see it" already means "it is not there" rather than
        // "it did not reach". Entities stream by proximity, so for a node this close streaming is
        // not in question.
        private const float FarmTourAbsenceProofRange = 30f;

        private readonly List<FarmTourStop> farmTourStops = new List<FarmTourStop>();
        private readonly List<FarmTourStop> farmTourCandidates = new List<FarmTourStop>();

        // The sink for FindClosestAvailableNode: while it is non-null the scan drops every eligible
        // candidate in HERE. That keeps the label/cooldown filter in exactly one place.
        private List<FarmTourStop> farmCandidateSink;

        private bool farmTourBuilt;

        // Size of the tour at the last full ordering pass — the yardstick the re-plan trigger uses.
        private int farmTourPlannedCount;

        // Below this a re-plan is not worth turning the player around for.
        private const int FarmTourReplanMinStops = 8;

        // Refreshed once per plan / top-up: is the farm currently swimming?
        private bool farmTourVerticalCost;

        // ⚠️ Horizontal ON LAND, fully 3-D UNDERWATER.
        //
        // On land walking does not change height, and counting Y pushed apart points that are
        // neighbours on the ground purely because one of them sits on a rock — a sound reason to
        // measure flat.
        //
        // Underwater it is the other way round: descending IS travel, and the spread of depths there
        // exceeds the spread across the horizontal (−36…−74 m in one run). The flat metric declared
        // points neighbours with twenty metres of vertical between them, and the visiting order
        // looked random. The log shows it literally: `walking 9,7m` followed immediately by
        // `diving 19,6m`, `wedged at 20,6m`.
        private float FarmTourDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            float planar = dx * dx + dz * dz;
            if (!this.farmTourVerticalCost)
            {
                return Mathf.Sqrt(planar);
            }

            float dy = a.y - b.y;
            return Mathf.Sqrt(planar + dy * dy);
        }

        private void RefreshFarmTourCostModel()
        {
            // ⚠️ TWO SOURCES, EITHER WILL DO. Resolving the swim locomotion is a Mono call that can
            // come back empty for a frame — and when it did, the whole underwater rule below
            // silently reverted to "take the head of the plan", which is how the farm ended up
            // swimming to a stop 38 m away while nearer ones sat unvisited. The walker's own
            // swimming flag is the cheap second opinion and is right whenever it is set.
            this.farmTourVerticalCost = this.farmWalkIsSwimming || this.TryGetFarmWalkSwimLocomotion(out _);
        }

        // Where the route is measured from. The camera is not the player: underwater it hangs behind
        // and above, and with the 3-D metric that offset distorts the choice of the first point most
        // of all — exactly where the cost of a wrong pick is highest. The camera stays as the
        // fallback for when the player's position does not resolve.
        internal Vector3 ResolveFarmTourOrigin()
        {
            if (this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                return selfPos;
            }

            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        // ⚠️ A BUBBLE CANNOT BE PLANNED FOR — IT CAN ONLY BE TAKEN NOW.
        //
        // A tour is a route: a point goes where it fits most cheaply between its neighbours, and its
        // turn comes round eventually. For a mushroom that is right, it is not going anywhere. A
        // bubble, though, drifts and bursts on its own, and it was being inserted exactly the same
        // way — into the middle of forty-eight points. By the time the tour reached that place the
        // bubble was long gone, and from outside it looked as though the walking mode did not notice
        // bubbles at all. The teleport mode did take them: there the target comes from
        // FindClosestAvailableNode, which is always the NEAREST.
        //
        // Hence the rule: a consumable target does not join the queue, it is taken as the head.
        private static bool IsTransientFarmTourStop(string label)
        {
            return string.Equals(label, "Bubble", StringComparison.Ordinal);
        }

        // Whether this stop is still among the last scan's candidates.
        private bool HasFreshFarmTourCandidateAt(Vector3 stop)
        {
            for (int i = 0; i < this.farmTourCandidates.Count; i++)
            {
                if (IsSameFarmTourStop(stop, this.farmTourCandidates[i].Position))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameFarmTourStop(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude < FarmTourSameStopDistance * FarmTourSameStopDistance;
        }

        // Collect candidates through the ordinary scan. Returns false when the radar is not ready.
        private bool TryCollectFarmTourCandidates(Vector3 origin)
        {
            // Every ordering decision below depends on this, so resolve it before any of them.
            this.RefreshFarmTourCostModel();
            this.farmTourCandidates.Clear();
            this.farmCandidateSink = this.farmTourCandidates;
            try
            {
                this.FindClosestAvailableNode(out _);
            }
            finally
            {
                this.farmCandidateSink = null;
            }

            // Drop the far ones and the duplicates within the sample itself: one resource can produce
            // several markers (the radar and the underwater scan each draw their own), and the tour
            // must see it once.
            float nearestDropped = float.MaxValue;
            string nearestDroppedLabel = null;
            for (int i = this.farmTourCandidates.Count - 1; i >= 0; i--)
            {
                Vector3 pos = this.farmTourCandidates[i].Position;
                float range = FarmTourDistance(origin, pos);
                bool drop = range > FarmTourMaxStopRange;
                if (drop && range < nearestDropped)
                {
                    nearestDropped = range;
                    nearestDroppedLabel = this.farmTourCandidates[i].Label;
                }

                if (!drop)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (IsSameFarmTourStop(pos, this.farmTourCandidates[j].Position))
                        {
                            drop = true;
                            break;
                        }
                    }
                }

                if (drop)
                {
                    this.farmTourCandidates.RemoveAt(i);
                }
            }

            // ⚠️ AN IDLE FARM MUST SAY WHY IT IS IDLE.
            //
            // With only one resource kind enabled and every one of them out of range, the farm sits
            // in "Scanning for nodes..." indefinitely and the log says NOTHING — there is no line
            // for a candidate that was found and then discarded. Measured 2026-08-20: two Bubble
            // markers at 146 m and 135 m, tour range 120 m, candidates 0, and no way to tell that
            // apart from "the radar sees nothing" without attaching a debugger.
            if (this.farmTourCandidates.Count == 0 && nearestDroppedLabel != null)
            {
                float now = Time.unscaledTime;
                if (now >= this.farmTourRangeComplaintAt)
                {
                    this.farmTourRangeComplaintAt = now + FarmTourRangeComplaintInterval;
                    ModLogger.Msg("[FarmTour] nothing in range: nearest is " + nearestDroppedLabel
                        + " at " + nearestDropped.ToString("F0") + "m, and the tour only takes stops within "
                        + FarmTourMaxStopRange.ToString("F0") + "m. Move closer or enable another resource.");
                }
            }

            return this.farmTourCandidates.Count > 0;
        }

        private const float FarmTourRangeComplaintInterval = 20f;
        private float farmTourRangeComplaintAt;

        // A full rebuild. Called when gathering starts and when the tour has run empty.
        private bool RebuildFarmTour(Vector3 origin)
        {
            if (!this.TryCollectFarmTourCandidates(origin))
            {
                return false;
            }

            this.farmTourStops.Clear();

            // 1. Nearest neighbour, starting from the player.
            List<FarmTourStop> pool = new List<FarmTourStop>(this.farmTourCandidates);
            Vector3 cursor = origin;
            while (pool.Count > 0 && this.farmTourStops.Count < FarmTourMaxStops)
            {
                int best = 0;
                float bestDist = FarmTourDistance(cursor, pool[0].Position);
                for (int i = 1; i < pool.Count; i++)
                {
                    float d = FarmTourDistance(cursor, pool[i].Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }

                cursor = pool[best].Position;
                this.farmTourStops.Add(pool[best]);
                pool.RemoveAt(best);
            }

            // 2. 2-opt over the whole tour — no walk is running yet, so the head may move.
            float before = this.MeasureFarmTour(origin);
            this.ImproveFarmTourWithTwoOpt(origin, 0);
            float after = this.MeasureFarmTour(origin);

            this.farmTourBuilt = true;
            this.farmTourPlannedCount = this.farmTourStops.Count;
            ModLogger.Msg("[FarmTour] planned " + this.farmTourStops.Count + " stops, "
                + after.ToString("F0") + "m total"
                + (this.farmTourVerticalCost ? " [3-D cost]" : " [planar cost]")
                + (before - after > 0.5f ? " (2-opt saved " + (before - after).ToString("F0") + "m)" : string.Empty)
                + (pool.Count > 0 ? " — " + pool.Count + " candidate(s) over the " + FarmTourMaxStops + " cap" : string.Empty)
                + ".");
            return this.farmTourStops.Count > 0;
        }

        // Length of the open route: player -> first -> ... -> last.
        private float MeasureFarmTour(Vector3 origin)
        {
            if (this.farmTourStops.Count == 0)
            {
                return 0f;
            }

            float total = FarmTourDistance(origin, this.farmTourStops[0].Position);
            for (int i = 1; i < this.farmTourStops.Count; i++)
            {
                total += FarmTourDistance(this.farmTourStops[i - 1].Position, this.farmTourStops[i].Position);
            }

            return total;
        }

        // 2-opt: reverse the segment [i..j] when that is shorter. lockedPrefix says how many points
        // from the start must not be touched (1 while a walk to the head is running).
        private void ImproveFarmTourWithTwoOpt(Vector3 origin, int lockedPrefix)
        {
            int n = this.farmTourStops.Count;
            if (n - lockedPrefix < 3)
            {
                return;
            }

            for (int pass = 0; pass < FarmTourTwoOptPasses; pass++)
            {
                bool improved = false;
                for (int i = Mathf.Max(lockedPrefix, 0); i < n - 1; i++)
                {
                    Vector3 a = i == 0 ? origin : this.farmTourStops[i - 1].Position;
                    Vector3 b = this.farmTourStops[i].Position;
                    for (int j = i + 1; j < n; j++)
                    {
                        Vector3 c = this.farmTourStops[j].Position;

                        // Open route: the last point has no successor, so reversing the tail is judged
                        // on the incoming edge alone.
                        float delta;
                        if (j == n - 1)
                        {
                            delta = FarmTourDistance(a, c) - FarmTourDistance(a, b);
                        }
                        else
                        {
                            Vector3 d = this.farmTourStops[j + 1].Position;
                            delta = FarmTourDistance(a, c) + FarmTourDistance(b, d)
                                - FarmTourDistance(a, b) - FarmTourDistance(c, d);
                        }

                        if (delta < -0.01f)
                        {
                            this.farmTourStops.Reverse(i, j - i + 1);
                            improved = true;

                            // ⚠️ b is stale now: after the reversal position i holds what used to be
                            // t[j]. Continuing the inner loop with the old b means computing deltas
                            // against a route that no longer exists, and 2-opt starts making the tour
                            // WORSE. Break out and take fresh a/b on the next i.
                            break;
                        }
                    }
                }

                if (!improved)
                {
                    return;
                }
            }
        }

        // Top-up: new candidates go in by cheapest insertion, and the order of the existing ones is
        // preserved. lockedPrefix protects the point a walk is already heading for.
        private void TopUpFarmTour(Vector3 origin, int lockedPrefix)
        {
            if (!this.farmTourBuilt || !this.TryCollectFarmTourCandidates(origin))
            {
                return;
            }

            // One deliberate re-plan when the tour has outgrown the plan it was built from.
            //
            // The radar only ever sees what has streamed in, so the first plan is built from
            // whatever is in range at that moment — the previous run started from TWO stops and
            // reached twenty-three purely by insertion. Insertion never reverses direction (which
            // is what keeps the route stable) but it also never fixes a seed that small.
            //
            // Rare and loud, not per-scan: doubling is a real change of the picture, and the log
            // says so, whereas re-optimising every two seconds is what made the player oscillate.
            if (this.farmTourStops.Count >= FarmTourReplanMinStops
                && this.farmTourStops.Count >= this.farmTourPlannedCount * 2)
            {
                int grewFrom = this.farmTourPlannedCount;
                float beforeLength = this.MeasureFarmTour(origin);
                this.ImproveFarmTourWithTwoOpt(origin, 0);
                this.farmTourPlannedCount = this.farmTourStops.Count;
                ModLogger.Msg("[FarmTour] re-planned: grew from " + grewFrom + " to "
                    + this.farmTourStops.Count + " stops, " + beforeLength.ToString("F0") + "m -> "
                    + this.MeasureFarmTour(origin).ToString("F0") + "m.");
            }

            int added = 0;
            for (int c = 0; c < this.farmTourCandidates.Count; c++)
            {
                if (this.farmTourStops.Count >= FarmTourMaxStops)
                {
                    break;
                }

                FarmTourStop candidate = this.farmTourCandidates[c];
                bool known = false;
                for (int i = 0; i < this.farmTourStops.Count; i++)
                {
                    if (IsSameFarmTourStop(candidate.Position, this.farmTourStops[i].Position))
                    {
                        known = true;
                        break;
                    }
                }

                if (known)
                {
                    continue;
                }

                int bestIndex = this.farmTourStops.Count;
                float bestCost = float.MaxValue;
                for (int i = Mathf.Max(lockedPrefix, 0); i <= this.farmTourStops.Count; i++)
                {
                    Vector3 prev = i == 0 ? origin : this.farmTourStops[i - 1].Position;
                    float cost;
                    if (i == this.farmTourStops.Count)
                    {
                        cost = FarmTourDistance(prev, candidate.Position);
                    }
                    else
                    {
                        Vector3 next = this.farmTourStops[i].Position;
                        cost = FarmTourDistance(prev, candidate.Position)
                            + FarmTourDistance(candidate.Position, next)
                            - FarmTourDistance(prev, next);
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestIndex = i;
                    }
                }

                this.farmTourStops.Insert(bestIndex, candidate);
                added++;
            }

            if (added > 0)
            {
                // ⚠️ NO 2-opt ON A TOP-UP — insertion only.
                //
                // It was run here on the reasoning "the head is locked, the tail is fair game". The
                // head did indeed stay put, but 2-opt solves the OPEN problem, and its optimum
                // depends sharply on the starting point. The starting point is the player, and the
                // player moves after every collect. So on each top-up the optimiser legitimately
                // found a different answer and reversed the whole remainder of the route.
                //
                // In the log it reads off z: 51 → 41 → 16 → 6 → 1, then back 16 → 24 → 40 → 66, then
                // back again 73 → 51 → 21. The player combed the area one way, turned around and
                // walked back — "swimming back and forth".
                //
                // Insertion on its own does not reorder anything, so the route stays coherent. The
                // full 2-opt runs once, when the plan is built. That is exactly what was asked for:
                // sort it up front, then only top it up.
                ModLogger.Msg("[FarmTour] +" + added + " new stop(s), " + this.farmTourStops.Count
                    + " pending, " + this.MeasureFarmTour(origin).ToString("F0") + "m total.");
            }
        }

        // The tour head. Builds the plan if there is not one yet.
        private bool TryGetNextFarmTourStop(Vector3 origin, out Vector3 position, out string label)
        {
            position = Vector3.zero;
            label = string.Empty;

            // The only place points leave the plan. Any system that marks a node in
            // recentlyVisitedNodes — a collect, a skip, a five-minute park, a rescue teleport —
            // strikes it from here automatically by doing so. Hanging removal calls off all of those
            // branches would guarantee forgetting one.
            this.PruneFarmTourStops(origin);

            if (!this.farmTourBuilt || this.farmTourStops.Count == 0)
            {
                if (!this.RebuildFarmTour(origin))
                {
                    return false;
                }
            }

            if (this.farmTourStops.Count == 0)
            {
                return false;
            }

            // UNDERWATER — always the nearest, never the next in the plan.
            //
            // Planning a tour makes sense where the legs are predictable. Underwater they are not:
            // the waypoint graph here has 86 nodes against 1745 on land, the hops between them run
            // 20-30 m in a straight line, and every third one runs into terrain. An order computed
            // from distances is worth nothing when half its transitions are impassable, and the cost
            // of a wrong pick is a wedged walk with four retreats.
            //
            // The nearest point is almost always reachable simply because it is close. The plan does
            // not go anywhere meanwhile: same list, topped up the same way, pruned the same way —
            // only the rule for picking the head changes.
            // Consumable targets jump the whole queue; the nearest of them wins.
            //
            // ⚠️ AND ONCE PICKED IT IS HELD. This block used to recompute the nearest on EVERY call,
            // by straight line from the player's current position. While the player swims toward
            // bubble B the distances to A and B both change, and the tour head flips back and forth.
            // Measured 02:55-02:57:
            //
            //     02:55:29 → A (-119,44 … 248,25)
            //     02:56:35 → B (-136,59 … 299,68)
            //     02:56:58 → A          ← back again
            //     02:57:11 → B          ← and back once more
            //
            // Every switch threw away the route just built and built another: in two minutes not one
            // bubble was collected, and the only one that vanished burst by itself. Formally nothing
            // failed anywhere — no ban, no teleport — which is why in the log it merely looked like
            // "it is walking".
            //
            // The cure is holding on: while the chosen target is still in the list, it stays the
            // head. Switching is allowed only when a new one is nearer by a MARGIN rather than by
            // any amount at all — otherwise a bubble's drift (~1.5 m/s) flips the pick on its own.
            int transientPick = -1;
            float transientBest = float.MaxValue;
            int lockedIndex = -1;
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                if (!IsTransientFarmTourStop(this.farmTourStops[i].Label))
                {
                    continue;
                }

                if (this.farmTourHasTransientLock
                    && IsSameFarmTourStop(this.farmTourTransientLock, this.farmTourStops[i].Position))
                {
                    lockedIndex = i;
                }

                float d = FarmTourDistance(origin, this.farmTourStops[i].Position);
                if (d < transientBest)
                {
                    transientBest = d;
                    transientPick = i;
                }
            }

            // The target we had already committed to is gone from the list — collected, burst, or
            // timed out. Only then is the pick made again.
            if (this.farmTourHasTransientLock && lockedIndex < 0)
            {
                this.farmTourHasTransientLock = false;
            }

            if (lockedIndex >= 0)
            {
                float lockedDistance = FarmTourDistance(origin, this.farmTourStops[lockedIndex].Position);
                if (transientPick >= 0 && transientPick != lockedIndex
                    && transientBest + FarmTourTransientSwitchMargin < lockedDistance)
                {
                    ModLogger.Msg("[FarmTour] switching target: " + transientBest.ToString("F0")
                        + "m beats the one we were walking to at " + lockedDistance.ToString("F0")
                        + "m by more than the " + FarmTourTransientSwitchMargin.ToString("F0")
                        + "m margin.");
                }
                else
                {
                    transientPick = lockedIndex;
                }
            }

            if (transientPick >= 0)
            {
                position = this.farmTourStops[transientPick].Position;
                label = this.farmTourStops[transientPick].Label;
                this.farmTourTransientLock = position;
                this.farmTourHasTransientLock = true;
                return true;
            }

            int pick = 0;
            if (this.farmTourVerticalCost)
            {
                float bestDist = FarmTourDistance(origin, this.farmTourStops[0].Position);
                for (int i = 1; i < this.farmTourStops.Count; i++)
                {
                    float d = FarmTourDistance(origin, this.farmTourStops[i].Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        pick = i;
                    }
                }
            }

            // Walking pays for distance; a teleport does not. So only walk mode measures routes —
            // and only over the few nearest, ordered by the straight line that is already known.
            if (this.farmWalkToNodeEnabled)
            {
                // ⚠️ THE NEAREST FOUR, NOT THE FIRST FOUR. This used to take the tour head plus
                // stops 0, 1, 2 IN PLAN ORDER and only then sort them by distance — so on land,
                // where the head is index 0, the "shortlist of the nearest" was simply the first
                // four stops of the planned circuit. After nearest-neighbour and 2-opt that order
                // is a route, not a distance ranking, and its fourth entry can be across the map.
                // The ranking then chose the best of four arbitrary stops and called it nearest.
                this.farmRouteShortlist.Clear();
                for (int i = 0; i < this.farmTourStops.Count; i++)
                {
                    this.farmRouteShortlist.Add(i);
                }

                this.farmRouteShortlist.Sort((x, y) =>
                    FarmTourDistance(origin, this.farmTourStops[x].Position)
                        .CompareTo(FarmTourDistance(origin, this.farmTourStops[y].Position)));

                if (this.farmRouteShortlist.Count > FarmRouteRankMaxMeasured)
                {
                    this.farmRouteShortlist.RemoveRange(FarmRouteRankMaxMeasured,
                        this.farmRouteShortlist.Count - FarmRouteRankMaxMeasured);
                }

                int byRoute = this.PickFarmTourStopByRoute(origin, this.farmRouteShortlist);
                if (byRoute >= 0)
                {
                    pick = byRoute;
                }
            }

            position = this.farmTourStops[pick].Position;
            label = this.farmTourStops[pick].Label;
            return true;
        }

        private void PruneFarmTourStops(Vector3 origin)
        {
            if (this.farmTourStops.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;

            for (int i = this.farmTourStops.Count - 1; i >= 0; i--)
            {
                Vector3 stop = this.farmTourStops[i].Position;

                // Out of range. The farm moves between areas by teleport (area:*), and without this
                // the tour would keep dragging the player back to oysters 100+ m away after a move.
                if (FarmTourDistance(origin, stop) > FarmTourMaxStopRange)
                {
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                // ⚠️ ALREADY COLLECTED. The plan is built from radar markers at planning time, while a
                // node goes cold instantly — and until this check the tour kept handing out a point
                // whose resource had long been taken. The farm walked to a harvested mushroom, and the
                // only thing that turned it around was the check made EN ROUTE
                // (TryAbandonDrainedFarmWalkTarget), i.e. after the player had already set off.
                //
                // The live scan is the same authority the collector and FindClosestAvailableNode use.
                // Found and cold => drop it from the plan and stamp it with the REAL remaining
                // cooldown, or TopUpFarmTour will put the point straight back on the next top-up.
                //
                // Not found => no conclusion: the node may simply be outside the streaming range.
                if (this.TryGetLiveNodeColdState(stop, 0f, out bool stopCold, out long stopColdEndMs))
                {
                    if (stopCold)
                    {
                        this.StampVisitedNode(stop, now + this.GetVisitedColdStampSeconds(stopColdEndMs));
                        this.farmTourStops.RemoveAt(i);
                        continue;
                    }
                }
                else if (this.farmTourCandidates.Count > 0
                    && !IsTransientFarmTourStop(this.farmTourStops[i].Label)
                    && FarmTourDistance(origin, stop) <= FarmTourAbsenceProofRange
                    && !this.HasFreshFarmTourCandidateAt(stop))
                {
                    // ⚠️ ABSENCE NEARBY IS AN ANSWER TOO — BUT THE JUDGE MUST BE THE SCAN CANDIDATES.
                    //
                    // Above it says "not found => no conclusion", and for a distant node that is
                    // right: it may simply be outside the streaming range. For a node two steps away
                    // streaming is not in doubt, and its absence means it is no longer there.
                    // Without this branch a harvested mushroom stayed in the plan until the walker
                    // arrived at an empty spot and sat out the timeout.
                    //
                    // ⚠️ BUT THE JUDGE HERE IS NOT THE COLD SCAN. The first version asked
                    // TryGetLiveNodeColdState, and that knows only the seven MapResGather families.
                    // Pollution (Contaminated) is not among them at all — nor is a bubble — so for it
                    // "not found" is NORMAL, and the rule would have dropped every contaminated point
                    // the player swam within thirty metres of.
                    //
                    // The last scan's candidates know every kind of marker alike, and the bubble
                    // branch below has judged by them all along. Same judge, same meaning, with no
                    // tie to the kind of resource.
                    //
                    // Consumable targets (bubbles) do not come through here: they drift, and a
                    // "visited" stamp on their OLD position is pointless. The branch below removes
                    // them — same judge, but without the stamp.
                    this.StampVisitedNode(stop, now + FarmVisitedRetryStampSeconds);
                    this.farmTourStops.RemoveAt(i);
                    ModLogger.Msg("[FarmTour] dropped a stop at " + FormatNavMeshVector(stop)
                        + ": the scan lists " + this.farmTourCandidates.Count
                        + " marker(s) but none there, and it is only "
                        + FarmTourDistance(origin, stop).ToString("F0") + "m away.");
                    continue;
                }

                // ⚠️ A BURST BUBBLE STAYS IN THE PLAN FOREVER unless it is removed here.
                //
                // Every other stop is removed by the live collectable scan, but a bubble never enters
                // that scan at all (it is not a collectable), so "went cold" never happens for it. A
                // stop from a vanished bubble would outlive the whole tour: the walker would arrive at
                // an empty spot, wait out the timeout and only then mark it visited — one wasted walk
                // per burst bubble.
                //
                // Judge by the last scan's candidates, and only when there are any: an empty list
                // means "the scan has not been collected yet", not "there are no markers".
                if (this.farmTourCandidates.Count > 0
                    && IsTransientFarmTourStop(this.farmTourStops[i].Label)
                    && !this.HasFreshFarmTourCandidateAt(stop))
                {
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                // ⚠️ AN UNCONFIRMED DYNAMIC BUSH IS NOT A TARGET. The mushroom here may have been
                // collected and a new entity growing in its place, one whose component is
                // indistinguishable from a ripe one; only the client's verdict knows. The point is not
                // stamped as visited — it is not taken, it is unknown, and we must return to it as
                // soon as the verdict arrives (the sweep is triggered by an unfamiliar netId showing
                // up, i.e. a second or two later).
                if (this.IsFarmTargetUnconfirmed(stop, out _))
                {
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                foreach (KeyValuePair<Vector3, float> visited in this.recentlyVisitedNodes)
                {
                    if (now < visited.Value && IsSameFarmTourStop(stop, visited.Key))
                    {
                        this.farmTourStops.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        // ⚠️ UNDERWATER, "NEAREST" HAS TO BE RE-ASKED WHILE SWIMMING, NOT ONLY AT THE START.
        //
        // The head of the tour is chosen when a walk begins and then left alone on purpose — a
        // target that moves under the walker is what made routes thrash. On land that is right: the
        // walker follows corners and passes nothing it could have taken instead.
        //
        // A swim is different. It is a straight line through open water, thirty or forty metres of
        // it, and the plan was made before any of it happened — so the player sails past resources
        // that were not the nearest when they set off and are now five metres away.
        //
        // The switch is deliberately hard to trigger: a candidate has to be BOTH a good few metres
        // nearer AND a fraction of what is left, and only while there is real distance still to
        // swim. Anything looser turns a swim into a tour of second thoughts.
        private const float FarmSwimRetargetInterval = 2f;
        private const float FarmSwimRetargetMinRemaining = 12f;
        private const float FarmSwimRetargetMinGain = 6f;
        private const float FarmSwimRetargetFraction = 0.6f;
        private float farmSwimRetargetNextAt;

        private bool TryRetargetNearerSwimNode(Vector3 selfPos, float now)
        {
            if (!this.farmWalkIsSwimming
                || !this.farmWalkLabel.StartsWith("node:", StringComparison.Ordinal)
                || this.farmWalkUnstickPhase != FarmWalkUnstickIdle
                || this.farmTourStops.Count == 0)
            {
                return false;
            }

            if (now < this.farmSwimRetargetNextAt)
            {
                return false;
            }

            this.farmSwimRetargetNextAt = now + FarmSwimRetargetInterval;

            float remaining = Distance3D(selfPos, this.farmWalkTrueTarget);
            if (remaining < FarmSwimRetargetMinRemaining)
            {
                return false;   // close enough that switching would just waste the approach
            }

            float bestDistance = float.MaxValue;
            Vector3 best = Vector3.zero;
            bool found = false;
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                Vector3 stop = this.farmTourStops[i].Position;
                if (IsSameFarmTourStop(stop, this.farmWalkTrueTarget))
                {
                    continue;
                }

                float d = Distance3D(selfPos, stop);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = stop;
                    found = true;
                }
            }

            if (!found
                || bestDistance > remaining * FarmSwimRetargetFraction
                || remaining - bestDistance < FarmSwimRetargetMinGain)
            {
                return false;
            }

            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": passing a nearer node — "
                + bestDistance.ToString("F1") + "m away against " + remaining.ToString("F1")
                + "m still to swim; taking that one instead.");

            // Not stamped as visited: the target we are leaving is perfectly good, it is simply
            // further. It goes back into the plan and gets collected on the way through.
            this.farmWalkSkipToScan = true;
            this.FinishFarmWalk("a nearer node is " + bestDistance.ToString("F1")
                + "m away — switching to it", false);
            _ = best;
            return true;
        }

        // ⚠️ NEAREST BY ROUTE, NOT BY COORDINATES.
        //
        // Every pick in this file ranked candidates with FarmTourDistance — a straight line between
        // two points. That is the right metric for a teleport, which costs the same from anywhere,
        // and the wrong one for walking: a node ten metres away on the far side of a rock loses to
        // one twenty metres away across open ground, and the straight line cannot tell them apart.
        // It also cannot tell that a candidate is unreachable at all, which is how the farm kept
        // selecting nodes whose route build then failed.
        //
        // So the shortlist is still made by straight line — it is free, and a candidate cannot be
        // near by route while being far in space — but the WINNER is chosen by measuring the route
        // to each of the few nearest. Bounded to FarmRouteRankCandidates: this runs on every scan,
        // and measuring forty nodes to pick one would cost more than the walk saves.
        // ⚠️ NOT A SHORTLIST ANY MORE — A BOUND.
        //
        // This was 4 "to keep the cost sane", a number picked without measuring the cost and with no
        // reason to believe the fifth candidate could not win. It can: on land the fifth-nearest is
        // routinely the one across an open field while the first four sit behind a fence.
        //
        // A route is never SHORTER than its straight line. So once the best measured route is no
        // longer than the straight-line distance to the next candidate, nothing further can beat it
        // — every remaining candidate is already further away in a straight line than the winner is
        // by road. Candidates are walked in straight-line order, so that test ends the search
        // correctly rather than arbitrarily, and usually after two or three measurements.
        //
        // The cap that remains is a backstop against a pathological field, not the policy.
        private const int FarmRouteRankMaxMeasured = 12;
        private readonly List<Vector3> farmRouteMeasureCorners = new List<Vector3>();
        private readonly List<int> farmRouteShortlist = new List<int>();

        // Length of the route the walker would actually drive, or false when there is none.
        //
        // ⚠️ MEASURE WHAT THE BUILDER BUILDS. An earlier version snapped with the cheap nearest-node
        // rule and summed the RAW A* output — which is not a route the walk would ever drive. The
        // builder snaps to the nearest REACHABLE node and then SHORTCUTS the path, and shortcutting
        // is exactly what removes the graph's zig-zag. Skipping both overestimated every candidate,
        // unevenly, and produced numbers that were not comparable to each other:
        //     34m away costs 266m to travel   /   35m away costing 261m
        // Seven times the straight line, for a swim the walker would have taken in one leg.
        //
        // The start snap is resolved ONCE per pick and passed in: every candidate starts from the
        // same place, so paying for it four times bought nothing but log noise.
        private bool TryMeasureFarmRouteLength(Vector3 from, int startIndex, Vector3 to, out float length)
        {
            length = 0f;

            // Underwater a clear line is the route — no graph involved, so its length is the line.
            if (this.farmWalkIsSwimming && this.IsFarmWalkDirectSwimClear(from, to, out _))
            {
                length = Distance3D(from, to);
                return true;
            }

            if (startIndex < 0
                || !this.TryFindNearestTrackGraphNode(to, FarmWalkGraphSnapRadius, out int endIndex,
                    this.farmWalkExcludedEndNodes)
                || !this.TryComputeTrackGraphPath(startIndex, endIndex, this.farmRouteMeasureCorners,
                    this.farmWalkExcludedNodes))
            {
                return false;
            }

            // The same two steps the builder takes before committing: the target becomes the final
            // corner, then the path is straightened.
            this.farmRouteMeasureCorners.Add(to);
            this.ShortcutFarmWalkRoute(from, this.farmRouteMeasureCorners);

            Vector3 previous = from;
            for (int i = 0; i < this.farmRouteMeasureCorners.Count; i++)
            {
                length += Distance3D(previous, this.farmRouteMeasureCorners[i]);
                previous = this.farmRouteMeasureCorners[i];
            }

            return true;
        }

        // The shortlist's best by measured route. Returns the index into farmTourStops, or -1 when
        // nothing could be measured — in which case the caller keeps its straight-line pick rather
        // than refusing to walk.
        private int PickFarmTourStopByRoute(Vector3 origin, List<int> shortlist)
        {
            int best = -1;
            float bestLength = float.MaxValue;
            float bestStraight = float.MaxValue;

            // One reachable snap for the whole pick — the builder's rule, paid for once.
            if (!this.TryFindReachableTrackGraphNode(origin, FarmWalkGraphSnapRadius,
                    out int startIndex, this.farmWalkExcludedNodes, "start"))
            {
                startIndex = -1;
            }

            // ⚠️ AN UNMEASURABLE CANDIDATE IS NOT AN UNREACHABLE ONE.
            //
            // This used to DROP any candidate whose route would not measure. Underwater that is most
            // of them: the direct-swim check is capped at 50 m, past which it falls to a graph of 86
            // nodes that frequently has no path — and a near node whose straight line happens to be
            // blocked lands in the same bucket. The farm then picked the far node that happened to
            // measure, and called it "nearest by route":
            //     taking a stop 48m away (48m to walk) over one 30m away
            //     taking a stop 31m away (48m to walk) over one 13m away
            // Thirteen metres discarded for forty-eight. That is not a routing decision, it is a
            // measurement gap wearing one.
            //
            // So an unmeasured candidate keeps its place, ranked by straight line with a penalty —
            // it may need to go round, and that costs something, but not its whole candidacy. If it
            // really is unreachable the walk fails and rule 0.6 parks it, which is exactly the
            // mechanism that exists for that.
            // ⚠️ A ROUTE SEVERAL TIMES THE STRAIGHT LINE IS NOT A ROUTE, IT IS A SPARSE GRAPH.
            //
            // Measured 2026-08-22 underwater: a stop 34 m away "costs 266 m to travel", another 35 m
            // away "261 m". The walker will never drive those — underwater it swims the straight
            // line and lets the escape ladder deal with whatever is in the way; the 260 m is what
            // A* makes of 86 nodes strung 20-30 m apart. Believing it hands the pick to whichever
            // far node happens to sit near a waypoint.
            //
            // So an implausible measurement is treated as no measurement at all.
            const float implausibleFactor = 3f;

            // ⚠️ AND A MARGIN, or the winner is decided by noise. The same run picked a stop 48 m
            // away over one 39 m away to save TWO metres of route — inside the error of a ranking
            // that snaps to the nearest node rather than the reachable one. Overriding "nearest"
            // has to be worth something.
            const float routeMarginFactor = 0.8f;
            const float routeMarginMinimum = 5f;

            const float unmeasuredPenalty = 1.5f;
            float runnerUpLength = float.MaxValue;
            bool bestMeasured = false;

            int measuredCount = 0;
            for (int i = 0; i < shortlist.Count; i++)
            {
                Vector3 stop = this.farmTourStops[shortlist[i]].Position;
                float straight = Distance3D(origin, stop);

                // The bound: nothing from here on can beat what we already have, because a route is
                // never shorter than its straight line and the list is in straight-line order.
                if (best >= 0 && bestLength <= straight)
                {
                    break;
                }

                measuredCount++;
                bool measured = this.TryMeasureFarmRouteLength(origin, startIndex, stop, out float routeLength);
                if (measured && routeLength > straight * implausibleFactor)
                {
                    measured = false;
                }

                if (!measured)
                {
                    routeLength = straight * unmeasuredPenalty;
                }

                if (i == 0)
                {
                    runnerUpLength = routeLength;
                }

                if (i == 0 || routeLength < bestLength)
                {
                    bestLength = routeLength;
                    bestStraight = straight;
                    bestMeasured = measured;
                    best = shortlist[i];
                }
            }

            // The nearest keeps the pick unless the winner is better by a real margin.
            if (best >= 0 && best != shortlist[0]
                && bestLength > runnerUpLength * routeMarginFactor - routeMarginMinimum)
            {
                best = shortlist[0];
            }

            if (best >= 0 && best != shortlist[0])
            {
                // Both sides of the comparison, or the line is unreadable: the version that printed
                // only the winner's route against the runner-up's STRAIGHT distance read as nonsense
                // ("31m away, 48m to walk, beat one 13m away") and hid the real cause for a day.
                ModLogger.Msg("[FarmTour] nearest by route, not by line: "
                    + bestStraight.ToString("F0") + "m away costs " + bestLength.ToString("F0")
                    + "m to travel" + (bestMeasured ? string.Empty : " (estimated)")
                    + ", against " + Distance3D(origin, this.farmTourStops[shortlist[0]].Position)
                        .ToString("F0") + "m away costing " + runnerUpLength.ToString("F0")
                    + "m (" + measuredCount + " of " + shortlist.Count + " measured).");
            }

            return best;
        }

        // Is there anything else worth going to? Rule 0.2: a teleport is allowed only when the walk
        // has spent its budget AND this is the only target — if the plan still holds another stop,
        // switching to it is both cheaper and quieter than a warp.
        private bool HasAnotherFarmTourStop(Vector3 exceptThis)
        {
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                if (!IsSameFarmTourStop(this.farmTourStops[i].Position, exceptThis))
                {
                    return true;
                }
            }

            return false;
        }

        internal void ResetFarmTour()
        {
            this.farmTourStops.Clear();
            this.farmTourCandidates.Clear();
            this.farmCandidateSink = null;
            this.farmTourBuilt = false;
            this.farmTourPlannedCount = 0;
            this.farmTourHasTransientLock = false;
        }
    }
}
