using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Reusable walk / ride travel state machine between route spots, originally extracted from
    // the fishing route. The consumer supplies the route (indices, positions, names) and the
    // host walker bridges through ICallbacks; the helper owns only the travel state.
    //
    // A hop is a walk when the walker is available, a teleport otherwise. A spot the walker
    // cannot route to is skipped for the next one on the list; the teleport is kept only for a
    // walker that is not ours to drive and for the case where every spot refused in a row.
    public static class RouteTravelHelper
    {
        // Arrive this close to the spot (flat), or accept the walker's own end this close.
        private const float TravelArriveDistance = 2.5f;
        private const float TravelWalkerEndAccept = 6f;
        private const float TravelResumeGrace = 1.5f;
        // A repair hold on the way is bounded the same way the farm bounds its own (~30 s window).
        private const float TravelRepairHoldMax = 40f;
        private const float TravelDeadlineSeconds = 300f;
        private const int TravelMaxWalkFailures = 2;

        private static bool travelling;
        private static bool travelWalking;          // the walker is driving right now
        private static bool travelOneShot;          // GoOnce: on failure, stop — do not cycle
        private static bool travelPausedForSession;
        private static bool travelPausedForRepair;
        private static float travelRepairHoldSince = -1f;
        private static int travelIndex = -1;
        private static Vector3 travelTarget;
        private static float travelStartedAt = -999f;
        private static float travelResumeAt = -999f;
        private static float travelNextStatusAt = -999f;
        private static int travelWalkFailures;
        private static string lastStatus = "Idle";

        // True while the walker is driving; farm-walk consumers use it to suppress the
        // out-of-bounds rescue.
        public static bool Walking => travelling && travelWalking;
        public static bool Travelling => travelling;
        public static string LastStatus => lastStatus;

        public interface ICallbacks
        {
            // The consumer's own session (e.g. a fishing cast / bite / battle) is running: stand still.
            bool IsSessionBusy();
            // False while another farm slice owns the tool: stand still.
            bool AllowsHop();
            // The player is at the spot (on foot or teleported). status is a short label.
            void OnArrived(int routeIndex, string status);
            // Notification only: the helper has given up on failedIndex and advances to the next
            // index itself (calling OnArrived / the walker for it).
            void OnSkipToNext(int failedIndex, string why);
            string SpotName(int routeIndex);
            Vector3 SpotPos(int routeIndex);
            int TotalSpotCount();
            bool WalkAvailable(HeartopiaComplete host);
            bool PrepareWalk(HeartopiaComplete host, out string why);
            bool BeginWalk(HeartopiaComplete host, Vector3 target, string name);
            void AbortWalk(HeartopiaComplete host);
            // True when the walker ended its leg (arrival or give-up).
            bool TickWalk(HeartopiaComplete host);
            // Flat distance to the point, or a negative value when unknown.
            float DistanceTo(HeartopiaComplete host, Vector3 to);
            void TeleportTo(HeartopiaComplete host, Vector3 pos);
            bool IsRepairBusy(HeartopiaComplete host);
        }

        // Stops any travel in progress; aborts the walker when it is driving.
        public static void End(HeartopiaComplete host, ICallbacks cb, string why)
        {
            ClearTravelState(host, cb, why);
        }

        // Aborts an active walk leg and resets all travel state. No-op when not travelling.
        // Used by End and before every teleport / already-at arrival so Travelling / Walking
        // never stay set after OnArrived.
        private static void ClearTravelState(HeartopiaComplete host, ICallbacks cb, string why)
        {
            if (!travelling)
            {
                return;
            }

            if (travelWalking)
            {
                try { cb.AbortWalk(host); } catch (Exception ex) { ModLogger.Msg("[RouteTravel] abort threw: " + ex.Message); }
            }

            travelling = false;
            travelWalking = false;
            travelOneShot = false;
            travelPausedForSession = false;
            travelPausedForRepair = false;
            travelRepairHoldSince = -1f;
            ModLogger.Msg("[RouteTravel] travel ended: " + why);
        }

        // One-off walk/teleport to a single point. On walk failure the helper stops instead of
        // cycling to the next route index (there is no "next").
        public static void GoOnce(HeartopiaComplete host, ICallbacks cb, Vector3 pos)
        {
            ClearTravelState(host, cb, "go once");
            travelOneShot = true;
            GoTo(host, cb, 0, pos);
        }

        // A hop to the spot at index: walk when possible, teleport otherwise. pos is the position
        // of index; later indices tried after a refusal are read through cb.SpotPos.
        public static void GoTo(HeartopiaComplete host, ICallbacks cb, int index, Vector3 pos)
        {
            // A new hop supersedes any leg still in flight. Preserve GoOnce across ClearTravelState.
            bool keepOneShot = travelOneShot;
            ClearTravelState(host, cb, "new hop");
            travelOneShot = keepOneShot;
            if (!cb.WalkAvailable(host))
            {
                TeleportToSpot(host, cb, index, pos);
                return;
            }

            if (!cb.PrepareWalk(host, out string why))
            {
                ModLogger.Msg("[RouteTravel] walking unavailable (" + why + ") — teleporting to '" + cb.SpotName(index) + "'.");
                TeleportToSpot(host, cb, index, pos);
                return;
            }

            int total = cb.TotalSpotCount();
            int current = index;
            Vector3 currentPos = pos;
            for (int attempt = 0; attempt < total; attempt++)
            {
                if (TryBeginTravel(host, cb, current, currentPos))
                {
                    return;
                }

                int next = (current + 1) % total;
                ModLogger.Msg("[RouteTravel] no route to '" + cb.SpotName(current) + "' — skipping to spot "
                    + (next + 1) + "/" + total + " '" + cb.SpotName(next) + "'.");
                cb.OnSkipToNext(current, "no route");
                current = next;
                currentPos = cb.SpotPos(current);
            }

            ModLogger.Msg("[RouteTravel] no route to any spot on the list — teleporting to '" + cb.SpotName(current) + "'.");
            TeleportToSpot(host, cb, current, currentPos);
        }

        // now must be Time.unscaledTime (the clock travelStartedAt uses).
        public static void Tick(HeartopiaComplete host, ICallbacks cb, float now)
        {
            if (!travelling)
            {
                return;
            }

            // The consumer's session on the way: it stops the walk; it resumes once the session
            // is over and has stayed over for a moment.
            if (cb.IsSessionBusy())
            {
                if (travelWalking)
                {
                    cb.AbortWalk(host);
                    travelWalking = false;
                    ModLogger.Msg("[RouteTravel] session on the way — stopped walking to '" + cb.SpotName(travelIndex) + "'.");
                }

                travelPausedForSession = true;
                travelResumeAt = now + TravelResumeGrace;
                lastStatus = "Session on the way";
                return;
            }

            if (travelPausedForSession)
            {
                if (now < travelResumeAt)
                {
                    return;
                }

                travelPausedForSession = false;
                if (!StartTravelWalk(host, cb, "session over"))
                {
                    SkipToNextSpot(host, cb, "no route after session");
                }
                return;
            }

            // An auto repair on the way: the kit use is silently ignored unless the player
            // stands still, and the restore aura only helps while the player is inside it.
            // Queued, in use, or aura running — stand still until it is over. Bounded, so a
            // wedged repair cannot pin the route.
            bool repairBusy;
            try { repairBusy = cb.IsRepairBusy(host); } catch { repairBusy = false; }
            if (repairBusy && (travelRepairHoldSince < 0f || now - travelRepairHoldSince <= TravelRepairHoldMax))
            {
                if (travelRepairHoldSince < 0f)
                {
                    travelRepairHoldSince = now;
                }

                if (travelWalking)
                {
                    cb.AbortWalk(host);
                    travelWalking = false;
                    ModLogger.Msg("[RouteTravel] auto repair on the way — stopped walking to '" + cb.SpotName(travelIndex) + "'.");
                }

                travelPausedForRepair = true;
                travelResumeAt = now + TravelResumeGrace;
                lastStatus = "Paused for repair on the way";
                return;
            }

            if (travelPausedForRepair)
            {
                if (now < travelResumeAt)
                {
                    return;
                }

                travelPausedForRepair = false;
                travelRepairHoldSince = -1f;
                ModLogger.Msg("[RouteTravel] repair " + (repairBusy ? "hold timed out" : "done") + " — walking on to '" + cb.SpotName(travelIndex) + "'.");
                if (!StartTravelWalk(host, cb, "repair over"))
                {
                    SkipToNextSpot(host, cb, "no route after repair");
                }
                return;
            }

            if (!repairBusy)
            {
                travelRepairHoldSince = -1f;
            }

            // Another farm slice owns the tool: stand still, resume when it hands back.
            if (!cb.AllowsHop())
            {
                if (travelWalking)
                {
                    cb.AbortWalk(host);
                    travelWalking = false;
                    ModLogger.Msg("[RouteTravel] another farm is running — walk to '" + cb.SpotName(travelIndex) + "' paused.");
                }

                lastStatus = "Paused (another farm is running)";
                return;
            }

            if (!travelWalking)
            {
                if (!StartTravelWalk(host, cb, "resume"))
                {
                    SkipToNextSpot(host, cb, "no route on resume");
                }
                return;
            }

            float away = cb.DistanceTo(host, travelTarget);
            if (away >= 0f && away <= TravelArriveDistance)
            {
                ArriveByWalk(host, cb, away);
                return;
            }

            if (now - travelStartedAt > TravelDeadlineSeconds)
            {
                SkipToNextSpot(host, cb, "travel deadline (" + TravelDeadlineSeconds.ToString("F0") + "s)");
                return;
            }

            if (now >= travelNextStatusAt)
            {
                travelNextStatusAt = now + 0.5f;
                lastStatus = "Walking to spot " + (travelIndex + 1) + "/" + cb.TotalSpotCount()
                    + (away >= 0f ? " (" + away.ToString("F0") + "m)" : "");
            }

            if (cb.TickWalk(host))
            {
                // The walker ended the leg itself: its own arrival test, or it gave up.
                travelWalking = false;
                away = cb.DistanceTo(host, travelTarget);
                if (away >= 0f && away <= TravelWalkerEndAccept)
                {
                    ArriveByWalk(host, cb, away);
                    return;
                }

                travelWalkFailures++;
                ModLogger.Msg("[RouteTravel] the walker ended the leg " + (away >= 0f ? away.ToString("F0") + "m" : "?")
                    + " from '" + cb.SpotName(travelIndex) + "' (" + travelWalkFailures + "/" + TravelMaxWalkFailures + ").");
                if (travelWalkFailures >= TravelMaxWalkFailures || !StartTravelWalk(host, cb, "retry"))
                {
                    SkipToNextSpot(host, cb, "walk failed");
                }
            }
        }

        private static void TeleportToSpot(HeartopiaComplete host, ICallbacks cb, int index, Vector3 pos)
        {
            ClearTravelState(host, cb, "teleport");
            cb.TeleportTo(host, pos);
            lastStatus = "Teleporting";
            cb.OnArrived(index, "Teleporting");
            ModLogger.Msg("[RouteTravel] hop to spot " + (index + 1) + "/" + cb.TotalSpotCount() + " '" + cb.SpotName(index) + "' at " + pos);
        }

        private static bool TryBeginTravel(HeartopiaComplete host, ICallbacks cb, int index, Vector3 pos)
        {
            float away = cb.DistanceTo(host, pos);
            if (away >= 0f && away <= TravelArriveDistance)
            {
                ClearTravelState(host, cb, "already at spot");
                lastStatus = "Arriving at spot";
                cb.OnArrived(index, "Arriving at spot");
                ModLogger.Msg("[RouteTravel] already at '" + cb.SpotName(index) + "' (" + away.ToString("F1") + "m).");
                return true;
            }

            travelling = true;
            travelWalking = false;
            travelPausedForSession = false;
            travelPausedForRepair = false;
            travelRepairHoldSince = -1f;
            travelIndex = index;
            travelTarget = pos;
            travelStartedAt = Time.unscaledTime;
            travelResumeAt = -999f;
            travelNextStatusAt = -999f;
            travelWalkFailures = 0;
            if (!StartTravelWalk(host, cb, "hop"))
            {
                travelling = false;
                return false;
            }

            return true;
        }

        // A travel that failed under way: the spot is given up for the next one on the list.
        private static void SkipToNextSpot(HeartopiaComplete host, ICallbacks cb, string why)
        {
            int failed = travelIndex;
            bool oneShot = travelOneShot;
            End(host, cb, why);
            cb.OnSkipToNext(failed, why);
            if (oneShot)
            {
                ModLogger.Msg("[RouteTravel] go-once gave up on '" + cb.SpotName(failed) + "' (" + why + ").");
                return;
            }

            int total = cb.TotalSpotCount();
            int next = (failed + 1) % total;
            ModLogger.Msg("[RouteTravel] giving up on '" + cb.SpotName(failed) + "' (" + why + ") — next spot "
                + (next + 1) + "/" + total + " '" + cb.SpotName(next) + "'.");
            GoTo(host, cb, next, cb.SpotPos(next));
        }

        private static bool StartTravelWalk(HeartopiaComplete host, ICallbacks cb, string why)
        {
            if (!cb.BeginWalk(host, travelTarget, cb.SpotName(travelIndex)))
            {
                ModLogger.Msg("[RouteTravel] the walker refused a route to '" + cb.SpotName(travelIndex) + "' (" + why + ").");
                return false;
            }

            travelWalking = true;
            float away = cb.DistanceTo(host, travelTarget);
            int total = cb.TotalSpotCount();
            lastStatus = "Walking to spot " + (travelIndex + 1) + "/" + total;
            ModLogger.Msg("[RouteTravel] walking to spot " + (travelIndex + 1) + "/" + total + " '" + cb.SpotName(travelIndex)
                + "' " + (away >= 0f ? away.ToString("F0") + "m away" : "") + " (" + why + ").");
            return true;
        }

        private static void ArriveByWalk(HeartopiaComplete host, ICallbacks cb, float away)
        {
            // Stops the axis; Get Off At = never keeps the seat (FishingRouteAbortWalk → keepVehicle).
            cb.AbortWalk(host);
            int index = travelIndex;
            float took = Time.unscaledTime - travelStartedAt;
            travelling = false;
            travelWalking = false;
            travelOneShot = false;
            travelPausedForSession = false;
            travelPausedForRepair = false;
            lastStatus = "Arriving at spot";
            cb.OnArrived(index, "Arriving at spot");
            ModLogger.Msg("[RouteTravel] arrived at spot " + (index + 1) + "/" + cb.TotalSpotCount() + " '" + cb.SpotName(index)
                + "' on foot (" + away.ToString("F1") + "m, " + took.ToString("F0") + "s).");
        }
    }
}
