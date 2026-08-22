using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // ASKING THE WORLD WHETHER A ROUTE LEG IS PASSABLE.
    //
    // The route comes out of an A* over the Track waypoint graph, and nothing in that pipeline ever
    // asks the collision world whether an EDGE can be crossed — the only geometry test in the whole
    // builder is a Linecast, used solely to decide whether to shortcut a corner. So a leg that runs
    // into a cliff is not an anomaly, it is the expected output for a graph that was never meant to
    // describe walkability.
    //
    // Measured with the GeoProbe route audit, 2026-08-21, node:Oyster:
    //     leg 0: 3.5m  climbs 0.75m at 1.6m along (within a jump)      sweep=passable
    //     leg 1: 9.5m  WALL: the ground steps up 2.65m at 4.0m along   sweep=BLOCKED
    // 2.65 m of ground step against a measured jump peak of 1.42 m — nothing the escape ladder owns
    // clears that. The audit's point-by-point surface profile and the game's own sphere sweep agreed
    // on every trustworthy sample, and the sweep costs ONE call per leg against about forty for a
    // profile. That is what this uses.
    //
    // ⚠️ LIFT BOTH ENDS. CanPlayerMoveUseSphere lifts only its ORIGIN, by radius+0.05, and takes the
    // direction from the raw arguments, so a "horizontal" sweep between two ground-level corners
    // runs with the sphere scraping the floor and every pebble reads as a wall. Both ends are lifted
    // here by the capsule's standing centre.
    //
    // ⚠️ DRY LAND ONLY. Underwater the player has vertical freedom: a 2.65 m ledge is not an
    // obstacle, it is something to rise over, and the swim ladder already handles it. A sweep verdict
    // there would ban perfectly good waypoints for a shape the swimmer never has to cross.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // How many legs of one route to test. After shortcutting a route is usually under ten legs;
        // the cap only bounds the pathological case, and what it drops is logged rather than assumed
        // to be fine.
        private const int FarmWalkEdgeAuditMaxLegs = 24;

        // How many times one build may ban a waypoint and try the graph again. Two is enough to get
        // past a single bad corner without turning route-building into a search.
        private const int FarmWalkEdgeAuditRetries = 2;

        // Legs shorter than this are not worth a call: the sweep's own origin lift makes very short
        // segments noisy, and a corner half a metre away is not what strands a walk.
        private const float FarmWalkEdgeAuditMinLeg = 1.5f;

        private IntPtr farmWalkSweepClass;
        private IntPtr farmWalkSweepMethod;
        private bool farmWalkSweepResolveTried;
        private IntPtr farmWalkSweepScratch;

        // Verdicts for this run, keyed by the two endpoints rounded to a decimetre. A re-path every
        // second or two rebuilds mostly the same legs, and the answer cannot change while the world
        // stands still.
        private readonly Dictionary<long, bool> farmWalkSweepCache = new Dictionary<long, bool>();

        private const int FarmWalkEdgeAuditBansPerWalk = 2;
        private int farmWalkEdgeAuditBans;

        // ⚠️ UNDERWATER, A CLEAR LINE *IS* A ROUTE — and the sphere sweep is finally the right test.
        //
        // Everything learned about that sweep on land inverts here. Its flaw there is that it flies
        // over terrain when the two ends differ in height, so it misses walls a walker would hit.
        // A swimmer flies over terrain: that is the whole of swimming. Sweeping the actual segment
        // the body would travel is exactly the question, with no ground-following in it.
        //
        // And the graph is the wrong instrument underwater. It carries 86 nodes against 1745 on
        // land, its legs run 20-30 m apart, and routing a 15 m swim through it means going out to a
        // waypoint and back — when the snap finds a node at all. A straight swim needs no node.
        //
        // Bounded by distance: one sweep vouching for a 200 m crossing is a bet, not a measurement,
        // and a long line through a reef can pass at capsule radius while being a maze to swim.
        private const float FarmWalkDirectSwimMaxDistance = 50f;

        private bool IsFarmWalkDirectSwimClear(Vector3 from, Vector3 to, out string why)
        {
            why = string.Empty;
            if (!this.farmWalkIsSwimming)
            {
                return false;
            }

            float distance = Distance3D(from, to);
            if (distance > FarmWalkDirectSwimMaxDistance)
            {
                why = "too far for one sweep to vouch for (" + distance.ToString("F0") + "m)";
                return false;
            }

            if (!this.EnsureFarmWalkSweepResolved() || !this.TryGetFarmWalkSweepController(out IntPtr ctrl))
            {
                why = "the sweep oracle did not answer";
                return false;   // no opinion: fall through to the graph, as before
            }

            // No lift. On land both ends are raised to the capsule's standing centre so the sphere
            // does not scrape the floor; a swimmer's position IS the body, and the line between two
            // points in open water is the line it would travel.
            if (!this.TryFarmWalkSweepLeg(ctrl, from, to, out bool clear))
            {
                why = "the sweep oracle did not answer";
                return false;
            }

            if (!clear)
            {
                why = "the sweep is blocked over " + distance.ToString("F1") + "m";
                return false;
            }

            why = "clear over " + distance.ToString("F1") + "m";
            return true;
        }

        internal void ResetFarmWalkEdgeAudit()
        {
            this.farmWalkSweepCache.Clear();
            this.farmWalkEdgeAuditBans = 0;
        }

        private bool EnsureFarmWalkSweepResolved()
        {
            if (this.farmWalkSweepResolveTried)
            {
                return this.farmWalkSweepMethod != IntPtr.Zero;
            }

            this.farmWalkSweepResolveTried = true;
            try
            {
                if (!this.EnsureAuraMonoApiReady())
                {
                    return false;
                }

                if (!this.AttachAuraMonoThread())
                {
                    this.farmWalkSweepResolveTried = false;   // worth another try once the thread attaches
                    return false;
                }

                this.farmWalkSweepClass = this.FindAuraMonoClassInImages(
                    "ScriptsRefactory.LevelAndEntity.LevelLayerManager", "LevelLayerManager",
                    new[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });

                if (this.farmWalkSweepClass == IntPtr.Zero)
                {
                    ModLogger.Msg("[FarmWalk] LevelLayerManager did not resolve — route legs cannot be "
                        + "passability-tested this session.");
                    return false;
                }

                this.farmWalkSweepMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.farmWalkSweepClass, "CanPlayerMoveUseSphere", 4);

                if (this.farmWalkSweepMethod == IntPtr.Zero)
                {
                    ModLogger.Msg("[FarmWalk] CanPlayerMoveUseSphere did not resolve — route legs "
                        + "cannot be passability-tested this session.");
                    return false;
                }

                this.farmWalkFitsMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.farmWalkSweepClass, "CheckPlayerCollisionSafe", 4);

                this.farmWalkSweepScratch = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
                ModLogger.Msg("[FarmWalk] route leg passability test ready (sweep="
                    + (this.farmWalkSweepMethod != IntPtr.Zero) + ", ground profile="
                    + (this.farmWalkFitsMethod != IntPtr.Zero) + ").");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[FarmWalk] resolving the leg passability test threw: "
                    + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // ⚠️ The controller is a MonoObject* and the sgen GC moves it the moment anything allocates,
        // so it is resolved per call and never held.
        private bool TryGetFarmWalkSweepController(out IntPtr ctrl)
        {
            ctrl = IntPtr.Zero;
            try
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr player) || player == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetBunnyHopMonoMoveComponent(player, out IntPtr move) || move == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryInvokeAuraMonoZeroArg(move, out ctrl, "get_controller")
                    && ctrl != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryFarmWalkSweepLeg(IntPtr ctrl, Vector3 from, Vector3 to,
            out bool passable, float liftOverride = -1f)
        {
            passable = true;
            if (this.farmWalkSweepMethod == IntPtr.Zero || ctrl == IntPtr.Zero
                || this.farmWalkSweepScratch == IntPtr.Zero)
            {
                return false;
            }


            // The standing centre of the capsule, so the sphere sweeps at chest height rather than
            // through the floor. Both ends: the method lifts only its own origin.
            float lift = liftOverride >= 0f
                ? liftOverride
                : (FarmWalkSweepCapsuleHeight * 0.5f) + FarmWalkSweepCapsuleRadius;
            Vector3 a = new Vector3(from.x, from.y + lift, from.z);
            Vector3 b = new Vector3(to.x, to.y + lift, to.z);

            byte* p = (byte*)this.farmWalkSweepScratch;
            *(Vector3*)p = a;
            *(Vector3*)(p + 12) = b;
            *(float*)(p + 24) = FarmWalkSweepCapsuleRadius;

            // Static method: the controller travels as the first ARGUMENT, not as `this`.
            IntPtr* args = stackalloc IntPtr[4];
            args[0] = ctrl;
            args[1] = (IntPtr)p;
            args[2] = (IntPtr)(p + 12);
            args[3] = (IntPtr)(p + 24);

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(this.farmWalkSweepMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero
                || !this.TryUnboxMonoBoolean(result, out passable))
            {
                return false;
            }

            return true;
        }

        // The point test the profile needs. Resolved next to the sweep and, like it, silent rather
        // than wrong when the oracle is unavailable — every caller here treats a false return as
        // "no answer", never as "blocked".
        private IntPtr farmWalkFitsMethod;

        private unsafe bool TryFarmWalkCapsuleFits(IntPtr ctrl, Vector3 at, out bool fits)
        {
            fits = false;
            if (this.farmWalkFitsMethod == IntPtr.Zero || ctrl == IntPtr.Zero
                || this.farmWalkSweepScratch == IntPtr.Zero)
            {
                return false;
            }

            byte* p = (byte*)this.farmWalkSweepScratch;
            *(Vector3*)(p + 32) = at;
            *(float*)(p + 44) = FarmWalkSweepCapsuleRadius;

            IntPtr* args = stackalloc IntPtr[4];
            args[0] = ctrl;
            args[1] = (IntPtr)(p + 32);
            args[2] = (IntPtr)(p + 44);
            args[3] = IntPtr.Zero;

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(this.farmWalkFitsMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && result != IntPtr.Zero
                && this.TryUnboxMonoBoolean(result, out fits);
        }

        // ⚠️ THE ORACLE LIFTS ONLY ITS ORIGIN, by radius+0.05, so A->B and B->A are NOT the same
        // segment — each is tilted the other way, and a step at one end clips one and misses the
        // other. Comparing the two raw directions therefore reports an asymmetry that belongs to
        // the CALL, not to the world.
        //
        // Pre-lowering the origin by exactly that lift cancels it: after the oracle raises it back,
        // both directions travel the identical line. Measured with and without on the same ground:
        // 228 asymmetric edges vs 229, i.e. the tilt does NOT explain the one-way results — but it
        // has to be removed before that statement can be made at all.
        private const float FarmWalkSweepOriginLift = 0.20f;   // radius 0.15 + the method's own 0.05

        private bool TryFarmWalkSweepLegLevelled(IntPtr ctrl, Vector3 from, Vector3 to,
            out bool passable)
        {
            return this.TryFarmWalkSweepLeg(ctrl,
                new Vector3(from.x, from.y - FarmWalkSweepOriginLift, from.z), to, out passable);
        }

        private const float FarmWalkSweepCapsuleRadius = 0.15f;
        private const float FarmWalkSweepCapsuleHeight = 0.96f;

        // ⚠️ DIRECTIONAL ON PURPOSE — a and b are packed in order, never sorted. Since a leg can
        // be passable one way and not the other, folding both directions onto one key would store
        // whichever answer arrived first and hand it back for the opposite question.
        private static long FarmWalkSweepKey(Vector3 a, Vector3 b)
        {
            long ax = (long)Mathf.Round(a.x * 10f), az = (long)Mathf.Round(a.z * 10f);
            long bx = (long)Mathf.Round(b.x * 10f), bz = (long)Mathf.Round(b.z * 10f);
            return (((ax & 0xFFFF) << 48) | ((az & 0xFFFF) << 32) | ((bx & 0xFFFF) << 16) | (bz & 0xFFFF));
        }

        // ⚠️ THE SWEEP ALONE IS NOT A WALKABILITY TEST, and trusting it cost a whole route.
        //
        // CanPlayerMoveUseSphere runs a straight line between the two points it is given. When the
        // corners sit at different heights — a waypoint on a rise, the next one in a hollow — that
        // line FLIES OVER the ground between them, and nothing on the ground can block it. Measured
        // 2026-08-21 on a route this very audit had just approved:
        //     leg 1: 18.9m (-209.0, 15.9, -15.0) -> (-226.3, 11.1, -7.5)
        //            WALL: the ground steps up 1.99m at 8.2m along | rises -4.9m   sweep=passable
        // Nearly five metres of height between the ends, so the sphere sailed over a two-metre step
        // and reported the leg fine. The player, who walks on the ground, cannot.
        //
        // So the sweep is kept — it catches things a coarse profile misses — but the deciding test
        // is the GROUND: sample the surface along the leg and look at the step between samples.
        // Coarser than the probe's 0.4 m (this runs inside route building, not on a button) and
        // cached per leg, so a re-path every second costs nothing after the first build.
        // Above this the leg is a slope, and a slope is asymmetric for reasons that have
        // nothing to do with a barrier. Same number the rules use for a walkable step per sample.
        private const float FarmWalkOneWayMaxRise = 0.5f;

        // stepOffset 0.15 + the sphere's own radius, i.e. the lowest height at which an obstacle is
        // still something the controller will NOT climb over by itself.
        private const float FarmWalkSweepKneeLift = 0.30f;

        private const float FarmWalkProfileStep = 1.5f;
        private const float FarmWalkProfileSpanUp = 3f;
        // Deep enough to find the ground under a ledge or a bank. At 4 m a leg over any real drop
        // came back "no surface" and was waved through.
        private const float FarmWalkProfileSpanDown = 12f;
        private const float FarmWalkProfileJumpClears = 1.42f;   // measured jump peak

        private bool TryFarmWalkLegPassable(IntPtr ctrl, Vector3 from, Vector3 to,
            out bool passable, out string why)
        {
            passable = true;
            why = string.Empty;

            // ⚠️ NOT `out passable`. TryGetValue writes its out parameter even when it MISSES —
            // it writes default(bool), i.e. false — so a cache miss silently turned the `passable`
            // this method promises into false, and nothing downstream set it back. Every leg that
            // passed every test was then reported impassable, with an empty reason because no
            // failure branch had run:
            //     leg 1 — impassable (15,5m to ...) — banning waypoint 979
            //     the FINAL leg to the node is not passable — impassable (1,9m to ...)
            // A one-word fallback ("impassable") is what made it look like a cosmetic gap in the
            // logging rather than the verdict being wrong for every leg in the route.
            long key = FarmWalkSweepKey(from, to);
            if (this.farmWalkSweepCache.TryGetValue(key, out bool cached))
            {
                passable = cached;
                why = cached ? string.Empty : "already judged impassable this run";
                return true;
            }

            passable = true;

            if (!this.TryFarmWalkSweepLeg(ctrl, from, to, out bool sweepOk))
            {
                return false;
            }

            if (!sweepOk)
            {
                passable = false;
                why = "the sphere sweep is blocked";
                this.farmWalkSweepCache[key] = false;
                return true;
            }

            // ⚠️ PASSABLE ONE WAY IS NOT PASSABLE. Confirmed in game 2026-08-22, standing against a
            // barrier: walking away from it is free while walking into it from open ground is
            // blocked. The cause is in the query itself — the sphere is built AT THE ORIGIN and a
            // collider it already touches there is discarded by the cast — so a leg that BEGINS
            // next to a wall reports clear and the walk then runs into that wall on its first step.
            //
            // Every sweep in this file was single-direction until now, which is exactly why such a
            // wall read as ordinary open ground.
            //
            // [J] Only a near-level leg is REJECTED on this. Asymmetry appears on any slope for a
            // harmless reason (from the lower end the sphere already rests on the ground), and on
            // the probe's grid that produced 229 asymmetric edges against 2 once level ground was
            // required. 0.5 m is the slope the rules already call walkable per sample; above it the
            // disagreement is logged and the leg is left alone rather than banned on a guess.
            // ⚠️ BOTH SIDES OF THE COMPARISON MUST BE LEVELLED. The forward sweep above is the
            // walker's own test and stays exactly as it is — it is what the walk will actually do —
            // but comparing THAT against a levelled reverse reintroduces the tilt this file just
            // went to the trouble of cancelling, and the difference would be the call, not a wall.
            // So the one-way question is asked of two levelled sweeps, separately from the verdict.
            bool oneWay = this.TryFarmWalkSweepLegLevelled(ctrl, from, to, out bool fwdLevel)
                && this.TryFarmWalkSweepLegLevelled(ctrl, to, from, out bool backOk)
                && fwdLevel != backOk;
            if (oneWay)
            {
                float rise = Mathf.Abs(to.y - from.y);
                if (rise <= FarmWalkOneWayMaxRise)
                {
                    passable = false;
                    why = fwdLevel
                        ? "ONE-WAY: passable towards the target but not back, on level ground"
                        : "ONE-WAY: blocked towards the target though the way back is open";
                    this.farmWalkSweepCache[key] = false;
                    return true;
                }

                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": leg to " + FormatNavMeshVector(to)
                    + " is one-way (" + (fwdLevel ? "forward clear, back blocked"
                        : "forward blocked, back clear") + ") but rises " + rise.ToString("F2")
                    + "m — height can explain that, so it is NOT being banned.");
            }

            // ⚠️ ONE HEIGHT IS NOT THE PLAYER. The default sweep runs at the capsule's centre,
            // 0.63 m, where a sphere of radius 0.15 occupies 0.48-0.78 m. A rail, a kerb or a low
            // fence at knee height passes UNDER it and reports clear, while the player — who is a
            // capsule from the floor to 0.96 m, not a ball in the middle — walks straight into it.
            //
            // So the leg is also swept at the height the walker's own step logic gives up at:
            // stepOffset + radius, just above what the controller climbs by itself. Anything that
            // blocks THAT is something the walk has to go around.
            if (this.TryFarmWalkSweepLeg(ctrl, from, to, out bool lowOk, FarmWalkSweepKneeLift)
                && !lowOk)
            {
                passable = false;
                why = "blocked at knee height (" + FarmWalkSweepKneeLift.ToString("F2")
                    + "m) though clear at chest height";
                this.farmWalkSweepCache[key] = false;
                return true;
            }

            float length = Distance3D(from, to);
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / FarmWalkProfileStep), 1, 24);
            float previousY = 0f;
            bool havePrevious = false;
            int gapSamples = 0;

            for (int i = 0; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, (float)i / steps);
                if (!this.TryFindFarmWalkSurface(ctrl, at, out float surfaceY, out byte column))
                {
                    if (column == FarmWalkColumnSolid)
                    {
                        // A wall face standing in the leg. This is the one the collider table
                        // cannot see: measured twice with the player pressed against a barrier,
                        // zero blocking colliders within 4-5 m both times.
                        passable = false;
                        why = "a solid column blocks the leg at "
                            + (length * i / steps).ToString("F1") + "m along (no height fits there)";
                        this.farmWalkSweepCache[key] = false;
                        return true;
                    }

                    // ⚠️ NO GROUND IS NOT A VERDICT — IT CANNOT BE, FROM HERE.
                    //
                    // An empty column means open air OR water, and nothing available distinguishes
                    // them: there is no water level to query for an arbitrary point, and the swim
                    // locomotion answers only for the player. A walker falls through the first and
                    // swims the second, so banning a waypoint on this alone would take a perfectly
                    // good river crossing off the graph.
                    //
                    // (A brief attempt to treat it as impassable is recorded here on purpose: it
                    // came from a real defect — skipping the step test over such samples let a leg
                    // through open air pass as walkable — but the cure was worse than the disease.)
                    //
                    // What IS kept is continuity: the step test does not restart on the far side, so
                    // a leg that leaves the ground and returns to it much higher is still caught.
                    gapSamples++;
                    continue;
                }

                if (havePrevious && surfaceY - previousY > FarmWalkProfileJumpClears)
                {
                    passable = false;
                    why = "the ground steps up " + (surfaceY - previousY).ToString("F2") + "m at "
                        + (length * i / steps).ToString("F1") + "m along";
                    this.farmWalkSweepCache[key] = false;
                    return true;
                }

                previousY = surfaceY;
                havePrevious = true;
            }

            if (gapSamples > 0)
            {
                // Not a refusal, but the walk deserves to know: this is where a route crosses water
                // or thin air, and it is what a walk that "just falls" will have been doing.
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": leg to "
                    + FormatNavMeshVector(to) + " has " + gapSamples
                    + " sample(s) with no ground under them (open air or water) — passing it anyway, "
                    + "the two cannot be told apart from here.");
            }

            this.farmWalkSweepCache[key] = true;
            return true;
        }

        // The highest height at this XZ where the capsule fits with something solid beneath it —
        // the same search the GeoProbe grid uses, at a coarser resolution.
        // ⚠️ "NO SURFACE" WAS THREE DIFFERENT ANSWERS WEARING ONE HAT.
        //
        // This returned false when the column was SOLID at every height, when it was EMPTY at every
        // height, and when the oracle simply did not answer — and the caller folded all three into
        // "no ground here, carry on". They could not be more different:
        //
        //   Solid    the capsule fits NOWHERE in the column. Next to somewhere you can stand, that
        //            is the FACE OF A WALL, and it is the shape the barriers on the shore and on
        //            the rope bridge take — neither of which has a collider to find.
        //   Void     nothing solid anywhere below. Open air OR water, and nothing here can tell
        //            them apart, so it stays what it always was: reported, never ruled on (5.2).
        //   NoAnswer the oracle failed. Silence, not a verdict.
        private const byte FarmWalkColumnSurface = 0;
        private const byte FarmWalkColumnSolid = 1;
        private const byte FarmWalkColumnVoid = 2;
        private const byte FarmWalkColumnNoAnswer = 3;

        private bool TryFindFarmWalkSurface(IntPtr ctrl, Vector3 at, out float surfaceY)
        {
            return this.TryFindFarmWalkSurface(ctrl, at, out surfaceY, out _);
        }

        private bool TryFindFarmWalkSurface(IntPtr ctrl, Vector3 at, out float surfaceY,
            out byte column)
        {
            surfaceY = 0f;
            column = FarmWalkColumnNoAnswer;
            float top = at.y + FarmWalkProfileSpanUp;
            float bottom = at.y - FarmWalkProfileSpanDown;

            // ⚠️ SOLID AT THE TOP IS NOT A SOLID COLUMN. Rejecting as soon as the top sample is
            // blocked would ban every leg that passes UNDER something — a bridge deck, an arch, an
            // overhanging rock — where the player walks through perfectly well. A column only
            // counts as solid when the capsule fits at NO height in the whole span, so the search
            // keeps descending until it finds free air and only then looks for the floor below it.
            if (!this.TryFarmWalkCapsuleFits(ctrl, new Vector3(at.x, top, at.z), out bool freeAtTop))
            {
                return false;
            }

            float freeY = top;
            bool seenFree = freeAtTop;
            for (float y = top - 0.5f; y >= bottom; y -= 0.5f)
            {
                if (!this.TryFarmWalkCapsuleFits(ctrl, new Vector3(at.x, y, at.z), out bool free))
                {
                    return false;
                }

                if (free)
                {
                    freeY = y;
                    seenFree = true;
                    continue;
                }

                if (!seenFree)
                {
                    continue;   // still inside whatever occupies the top; keep going down
                }

                float lo = y, hi = freeY;
                for (int k = 0; k < 3; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (!this.TryFarmWalkCapsuleFits(ctrl, new Vector3(at.x, mid, at.z), out bool midFree))
                    {
                        return false;
                    }

                    if (midFree)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }

                surfaceY = hi;
                column = FarmWalkColumnSurface;
                return true;
            }

            // Nothing free anywhere in the span: there is no height at which a player fits here.
            // That is the face of a wall. Free the whole way down instead is a void — air or water,
            // and this cannot tell which, so it stays a report rather than a verdict.
            column = seenFree ? FarmWalkColumnVoid : FarmWalkColumnSolid;
            return false;
        }

        // The index of the first corner a leg cannot reach, or -1 when every tested leg is passable.
        //
        // The FINAL leg is tested but never reported as bannable: it ends at the resource itself, and
        // there is no waypoint there to take off the table — a blocked final approach is the escape
        // ladder's problem, not the graph's.
        // includeFirstLeg — ⚠️ THE TWO CALLERS ASK DIFFERENT QUESTIONS.
        //
        // For BANNING, leg 0 must be ignored: it starts at the player, so its verdict is about where
        // they are standing, not about the waypoint (see the note in the loop).
        //
        // For CHOOSING between the game's route and ours, leg 0 counts. Rejecting a route whose very
        // first leg cannot be walked is the whole point of the comparison, and the alternative route
        // starts from the same place but may leave it a different way. Suppressing leg 0 there is why
        // the walker kept following the game's route into a wall: the only blocked leg was the first
        // one, so nothing ever objected.
        private int FindFirstImpassableFarmWalkLeg(Vector3 from, List<Vector3> corners, out string detail,
            bool includeFirstLeg = false)
        {
            detail = string.Empty;
            string reason = string.Empty;
            if (corners == null || corners.Count < 2 || this.farmWalkIsSwimming)
            {
                return -1;
            }

            if (!this.EnsureFarmWalkSweepResolved() || !this.TryGetFarmWalkSweepController(out IntPtr ctrl))
            {
                return -1;
            }

            int tested = 0;
            Vector3 previous = from;
            for (int i = 0; i < corners.Count && tested < FarmWalkEdgeAuditMaxLegs; i++)
            {
                Vector3 corner = corners[i];
                float length = Distance3D(previous, corner);
                if (length < FarmWalkEdgeAuditMinLeg)
                {
                    previous = corner;
                    continue;
                }

                tested++;
                if (!this.TryFarmWalkLegPassable(ctrl, previous, corner, out bool passable, out string why))
                {
                    return -1;      // the oracle stopped answering; do not ban on silence
                }

                if (!passable)
                {
                    reason = why.Length > 0 ? why : "impassable";
                    bool finalLeg = i >= corners.Count - 1 && !includeFirstLeg;

                    // ⚠️ LEG 0 STARTS AT THE PLAYER, SO IT SAYS NOTHING ABOUT THE WAYPOINT.
                    //
                    // If the player is standing under a ledge, behind a fence, or anywhere the graph
                    // cannot be reached from, EVERY first leg is blocked — whichever waypoint it
                    // happens to aim at. Banning on that verdict punishes the graph for where the
                    // player is standing, and it compounds: ban one, A* offers the next, ban that
                    // too. Measured 2026-08-21 — eight waypoints (980, 978, 984, 983, 981, 979, 703,
                    // 988) banned in six seconds across a twenty-metre patch, every single line
                    // reading "leg 0", every single ban wrong.
                    //
                    // Getting out of where the player stands is the escape ladder's job. Only legs
                    // BETWEEN waypoints are evidence about the graph.
                    if (i == 0 && !includeFirstLeg)
                    {
                        detail = "the first leg out of here is not passable — " + reason + " ("
                            + length.ToString("F1") + "m to " + FormatNavMeshVector(corner)
                            + ") — that is about where the player is standing, not about the "
                            + "waypoint, so nothing is banned";
                        return -1;
                    }

                    detail = finalLeg
                        // ⚠️ SAY WHAT IT MEANS. This used to be a bare noun phrase, written to be
                        // embedded in "... is not passable — banning ...", and when the final-leg
                        // case printed it alone the log read "node:Oyster: leg 2 (11,4m to (...))."
                        // — a fragment with no verb, next to a walk that then wedged and burned its
                        // escapes. A line that does not say what it found is not a log line.
                        ? "the FINAL leg to the node is not passable — " + reason + " ("
                            + length.ToString("F1") + "m to " + FormatNavMeshVector(corner)
                            + ") — nothing to ban at a resource, so the walk goes anyway and the "
                            + "escape ladder gets it"
                        : "leg " + i + " — " + reason + " (" + length.ToString("F1") + "m to "
                            + FormatNavMeshVector(corner) + ")";

                    // The last corner is the resource; there is no waypoint there to take off the
                    // table, so it is reported rather than banned.
                    return finalLeg ? -1 : i;
                }

                previous = corner;
            }

            if (corners.Count > FarmWalkEdgeAuditMaxLegs)
            {
                detail = "only the first " + FarmWalkEdgeAuditMaxLegs + " leg(s) of "
                    + corners.Count + " were passability-tested — the rest are unchecked";
            }

            return -1;
        }

        // Ban the waypoint a blocked leg ends at, so the next A* has to find another way round.
        // Returns false when there is nothing to ban — no node resolved, already banned, or the ban
        // list is full — and the caller then keeps the route it has.
        private bool TryBanFarmWalkWaypointAt(Vector3 corner, string why)
        {
            // ⚠️ A RATE LIMIT, NOT ONLY A TOTAL. The global cap is 48 waypoints and the audit sailed
            // most of the way there: twenty-four bans in two minutes, each one A* offering the next
            // nearest node and this banning it too, marching outward from 6.8 m to 33 m. The cause
            // was judging leg 0 (fixed above), but the shape of the failure is general — any
            // systematic false positive turns a per-route check into a graph shredder.
            //
            // Two per walk is enough to route around a genuinely bad corner. Past that the evidence
            // is against the CHECK, not the graph, and the honest move is to stop and say so.
            if (this.farmWalkEdgeAuditBans >= FarmWalkEdgeAuditBansPerWalk)
            {
                ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + why
                    + " is not passable, but " + this.farmWalkEdgeAuditBans
                    + " waypoint(s) have already been banned on this walk — not banning another. "
                    + "That many in one walk means the test is wrong more likely than the graph.");
                return false;
            }

            if (this.farmWalkBlockedGraphNodes.Count >= FarmWalkMaxBlockedNodes)
            {
                return false;
            }

            // ⚠️ NO EXCLUSION FILTER HERE. Passing the excluded set made this find the nearest
            // node that was NOT already banned — so a second failure on the same corner banned a
            // different, innocent waypoint instead of recognising that this corner's node was
            // already dealt with. Ask for the node that is actually there, then refuse if it is
            // already banned.
            if (!this.TryFindNearestTrackGraphNode(corner, FarmWalkGraphSnapRadius, out int index))
            {
                return false;
            }

            if (this.farmWalkBlockedGraphNodes.ContainsKey(index))
            {
                return false;
            }

            this.farmWalkBlockedGraphNodes[index] = Time.unscaledTime + FarmWalkBlockedNodeTtl;
            this.farmWalkExcludedNodes.Add(index);
            this.farmWalkEdgeAuditBans++;
            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": " + why
                + " is not passable — banning waypoint " + index + " for "
                + (FarmWalkBlockedNodeTtl / 60f).ToString("0.#") + " min ("
                + this.farmWalkBlockedGraphNodes.Count + "/" + FarmWalkMaxBlockedNodes
                + " banned) and routing around it.");
            return true;
        }
    }
}
