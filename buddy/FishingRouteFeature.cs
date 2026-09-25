using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // "Start Fishing Locations": rotates the player through a list of fishing spots (fixed +
    // user-saved), driving the existing AutoFishingFarm engine at each stop. On start it forces
    // scan range to 200m and turns on Auto Eat Energy Panel / Auto Repair on Durability; on stop
    // it restores whatever the user had before. Hops to the next spot when the radius has been
    // fish-free for NoFishHopSeconds, but never mid-cast/battle, and defers the hop teleport
    // while an auto repair is running (fishing itself keeps going during the pause).
    //
    // With Walk to Nodes on, a hop is a WALK (and a ride, by the walker's own vehicle rules)
    // instead of a teleport — user rule 2026-09-25. The engine keeps fishing on the way: when a
    // session starts (cast / bite / battle) the walk is aborted and the player stands still until
    // it ends, then the walk to the same spot resumes. Aura Farm is stopped for the route (one
    // writer on the walker). The teleport stays as the fallback: walker unavailable or refusing,
    // the walk failing twice, or the travel running past its deadline.
    public static class FishingRouteFeature
    {
        private const float NoFishHopSeconds = 10f;
        private const float SettleGraceSeconds = 3f;
        private const float WorldUnavailableStopSeconds = 30f;
        private const float ForcedDetectRange = 200f;
        // Travel: arrive this close to the spot (flat), or accept the walker's own end this close.
        private const float TravelArriveDistance = 2.5f;
        private const float TravelWalkerEndAccept = 6f;
        private const float TravelResumeGrace = 1.5f;
        // A repair hold on the way is bounded the same way the farm bounds its own (~30 s window).
        private const float TravelRepairHoldMax = 40f;
        private const float TravelDeadlineSeconds = 300f;
        private const int TravelMaxWalkFailures = 2;

        private struct FixedSpot
        {
            public string name;
            public Vector3 pos;
            public FixedSpot(string name, float x, float y, float z)
            {
                this.name = name;
                this.pos = new Vector3(x, y, z);
            }
        }

        // ⭐ MEASURED CYCLE, NOT A CATALOGUE. The list used to be grouped by water type (rivers,
        // lakes, seas), which made every group boundary a haul across the map: a full round
        // was 6 538 m of walker route with twelve legs over 150 m (up to 520 m). This order is
        // the shortest cycle over the walker's own route lengths between all 40 spots,
        // measured in game on 2026-09-25 (tools/FishRouteProbe, nearest-neighbour + 2-opt):
        // 3 412 m, longest leg 165 m. The route starts at the spot nearest the player and goes
        // round from there, so the first entry is arbitrary. Custom spots are appended at
        // runtime and sit outside this cycle. Shallow River 1 snaps to an isolated graph node
        // (no route measurable to or from it); it stays next to Shallow River 2.
        private static readonly FixedSpot[] FixedSpots = new FixedSpot[]
        {
            new FixedSpot("Rosy River 1", -132.044f, 20.728f, 123.636f),
            new FixedSpot("Old Sea 1", -182.468f, 10.563f, 201.788f),
            new FixedSpot("Old Sea 2", -157.400f, 10.841f, 250.438f),
            new FixedSpot("Old Sea 3", -138.933f, 10.926f, 303.255f),
            new FixedSpot("Old Sea 4", -94.604f, 10.557f, 267.479f),
            new FixedSpot("Old Sea 5", 2.903f, 10.833f, 250.543f),
            new FixedSpot("Onsen Mountain Lake 2", -68.906f, 28.009f, 199.926f),
            new FixedSpot("Onsen Mountain Lake 1", 7.902f, 20.045f, 166.517f),
            new FixedSpot("Suburban Lake 2", -15.798f, 23.880f, 86.701f),
            new FixedSpot("Shallow River 2", 77.957f, 20.506f, 97.549f),
            new FixedSpot("Shallow River 1", 109.264f, 19.823f, 107.453f),
            new FixedSpot("Forest Lake 1", 169.729f, 31.086f, 79.796f),
            new FixedSpot("East Sea 5", 250.926f, 13.364f, 94.227f),
            new FixedSpot("East Sea 4", 245.587f, 10.670f, 33.395f),
            new FixedSpot("East Sea 3", 249.190f, 10.640f, -4.454f),
            new FixedSpot("East Sea 1", 266.370f, 11.274f, -105.900f),
            new FixedSpot("East Sea 2", 222.824f, 10.609f, -61.501f),
            new FixedSpot("Forest Lake 3", 150.090f, 21.495f, -52.907f),
            new FixedSpot("Forest Lake 2", 156.001f, 21.425f, 8.155f),
            new FixedSpot("Suburban Lake 3", 81.600f, 22.236f, 38.265f),
            new FixedSpot("Suburban Lake 4", 84.599f, 21.996f, 11.957f),
            new FixedSpot("Suburban Lake 5", 63.881f, 22.350f, -34.296f),
            new FixedSpot("Giantwood River 1", 68.196f, 19.838f, -95.980f),
            new FixedSpot("Giantwood River 2", 89.264f, 18.126f, -114.870f),
            new FixedSpot("Zephyr Sea 2", 40.823f, 11.601f, -158.479f),
            new FixedSpot("Zephyr Sea 1", 46.434f, 11.500f, -180.964f),
            new FixedSpot("Zephyr Sea 3", 0.809f, 11.698f, -142.726f),
            new FixedSpot("Suburban Lake 6", -9.485f, 15.069f, -72.374f),
            new FixedSpot("Zephyr Sea 4", -40.134f, 11.298f, -145.853f),
            new FixedSpot("Zephyr Sea 5", -114.150f, 11.407f, -185.543f),
            new FixedSpot("Tranquil River 1", -112.565f, 15.698f, -138.189f),
            new FixedSpot("Tranquil River 2", -91.925f, 19.004f, -97.962f),
            new FixedSpot("Suburban Lake 7", -99.859f, 19.652f, -53.989f),
            new FixedSpot("Whale Sea 1", -226.625f, 11.911f, -56.772f),
            new FixedSpot("Whale Sea 3", -251.356f, 10.776f, 8.765f),
            new FixedSpot("Whale Sea 2", -223.321f, 10.472f, -13.959f),
            new FixedSpot("Meadow Lake", -188.604f, 19.717f, -21.203f),
            new FixedSpot("Suburban Lake 8", -91.586f, 25.915f, 60.682f),
            new FixedSpot("Suburban Lake 1", -77.757f, 20.781f, 91.570f),
            new FixedSpot("Rosy River 2", -110.786f, 20.096f, 121.876f),
        };

        private static readonly List<HeartopiaComplete.CustomTeleportEntry> customSpots = new List<HeartopiaComplete.CustomTeleportEntry>();

        private static bool active;
        private static int currentIndex;
        private static float spotArrivedAt = -999f;
        private static float graceUntil = -999f;
        private static float noFishSinceAt = -1f;
        // Event cursors: the no-fish window advances only when a NEW scan / bait throw is seen.
        private static float lastHandledScanAt = -999f;
        private static float lastHandledBaitAt = -999f;
        private static float worldNotReadySince = -1f;
        private static bool pausedForRepair;
        private static string lastStatus = "Idle";
        // Travel state (walk / ride between spots).
        private static bool travelling;
        private static bool travelWalking;          // the walker is driving right now
        private static bool travelPausedForFishing;
        private static bool travelPausedForRepair;
        private static float travelRepairHoldSince = -1f;
        private static int travelIndex = -1;
        private static Vector3 travelTarget;
        private static float travelStartedAt = -999f;
        private static float travelResumeAt = -999f;
        private static float travelNextStatusAt = -999f;
        private static int travelWalkFailures;
        // Read by FarmWalkRunActive: the out-of-bounds rescue stays suppressed while we drive.
        public static bool Walking => travelling && travelWalking;

        // Snapshot of the user's settings taken at Start; restored on Stop. Also read by the
        // config writer so a save during an active route persists the user's values, not the
        // route's forced ones (200m range / both toggles on).
        private static bool hasSnapshot;
        private static float snapshotDetectRange = 60f;
        private static bool snapshotAutoEatPanel;
        private static bool snapshotAutoRepair;
        private static bool snapshotAutoFishEnabled;

        public static bool Active => active;
        // Read-only status exposure for the UGUI Fishing content (HeartopiaComplete.
        // UguiFishingContent.cs) — mirrors what DrawSection reads for its own status lines.
        public static int CurrentIndex => currentIndex;
        public static bool PausedForRepair => pausedForRepair;
        public static string LastStatus => lastStatus;
        public static float SnapshotDetectRange => hasSnapshot ? snapshotDetectRange : AutoFishingFarm.GetDetectRange();
        public static bool SnapshotAutoEatPanel => snapshotAutoEatPanel;
        public static bool SnapshotAutoRepair => snapshotAutoRepair;

        // "Custom Spots Only": rotate over user-saved spots and skip the fixed list. With zero
        // custom spots the toggle is inert (full list) so the route can never start empty.
        private static bool customSpotsOnly;
        public static bool GetCustomSpotsOnly() => customSpotsOnly;
        public static void SetCustomSpotsOnly(bool value)
        {
            if (customSpotsOnly == value)
            {
                return;
            }

            customSpotsOnly = value;
            // The index space changed (fixed+custom vs custom-only) — restart the rotation; the
            // actual teleport happens on the next due hop, the current spot keeps fishing.
            if (active)
            {
                currentIndex = 0;
            }
            Log("Custom Spots Only " + (value ? "enabled" : "disabled") + $" (custom={customSpots.Count})");
        }

        private static bool UseCustomOnly => customSpotsOnly && customSpots.Count > 0;

        public static int TotalSpotCount => UseCustomOnly ? customSpots.Count : FixedSpots.Length + customSpots.Count;

        public static string GetSpotName(int index)
        {
            int customIndex;
            if (UseCustomOnly)
            {
                customIndex = index;
            }
            else
            {
                if (index < FixedSpots.Length)
                {
                    return FixedSpots[index].name;
                }

                customIndex = index - FixedSpots.Length;
            }

            if (customIndex >= 0 && customIndex < customSpots.Count)
            {
                return customSpots[customIndex]?.name ?? ("Custom " + (customIndex + 1));
            }

            return "?";
        }

        private static Vector3 GetSpotPos(int index)
        {
            int customIndex;
            if (UseCustomOnly)
            {
                customIndex = index;
            }
            else
            {
                if (index < FixedSpots.Length)
                {
                    return FixedSpots[index].pos;
                }

                customIndex = index - FixedSpots.Length;
            }

            return customIndex >= 0 && customIndex < customSpots.Count ? customSpots[customIndex].position : Vector3.zero;
        }

        // --- Config persistence (list lives in UnifiedConfigData.FishingRouteSpots) ---
        public static void ImportCustomSpots(List<HeartopiaComplete.CustomTeleportEntry> entries)
        {
            customSpots.Clear();
            if (entries == null)
            {
                return;
            }

            foreach (HeartopiaComplete.CustomTeleportEntry entry in entries)
            {
                if (entry != null)
                {
                    customSpots.Add(entry);
                }
            }
        }

        public static List<HeartopiaComplete.CustomTeleportEntry> ExportCustomSpots()
        {
            var result = new List<HeartopiaComplete.CustomTeleportEntry>(customSpots.Count);
            foreach (HeartopiaComplete.CustomTeleportEntry entry in customSpots)
            {
                if (entry == null) continue;
                result.Add(new HeartopiaComplete.CustomTeleportEntry
                {
                    name = (entry.name ?? "").Replace("\"", "").Replace("\\", ""),
                    position = entry.position
                });
            }
            return result;
        }

        public static void Start(HeartopiaComplete host)
        {
            if (active || host == null)
            {
                return;
            }

            snapshotDetectRange = AutoFishingFarm.GetDetectRange();
            snapshotAutoEatPanel = host.GetAutoEatEnergyPanelEnabled();
            snapshotAutoRepair = host.GetAutoRepairOnDurabilityEnabled();
            snapshotAutoFishEnabled = AutoFishingFarm.IsEnabled;
            hasSnapshot = true;

            AutoFishingFarm.SetDetectRange(ForcedDetectRange);
            host.SetAutoEatEnergyPanelEnabled(true);
            host.SetAutoRepairOnDurabilityEnabled(true);
            // Repair-aura events (ToolRestorerEvent / ToolRestoreDestroyEvent) drive the
            // "repair active" window that IsAutoRepairBusy() folds in — without them a hop can
            // teleport the player out of the repair circle mid-restore.
            host.EnsureRepairAuraEventHooks();
            if (!AutoFishingFarm.IsEnabled)
            {
                AutoFishingFarm.SetEnabled(true, host);
            }

            active = true;
            currentIndex = FindNearestSpotIndex(host);
            pausedForRepair = false;
            worldNotReadySince = -1f;
            GoToSpot(host, currentIndex);
            FeatureLog.Life("FishingRoute", $"route started: {TotalSpotCount} spot(s)");
            Log($"Route started: {TotalSpotCount} spots, snapshot range={snapshotDetectRange:F0} eat={snapshotAutoEatPanel} repair={snapshotAutoRepair} fish={snapshotAutoFishEnabled}");
        }

        public static void Stop(HeartopiaComplete host)
        {
            if (!active)
            {
                return;
            }

            active = false;
            pausedForRepair = false;
            noFishSinceAt = -1f;
            worldNotReadySince = -1f;
            lastStatus = "Idle";
            EndTravel(host, "route stopped");

            if (hasSnapshot)
            {
                AutoFishingFarm.SetDetectRange(snapshotDetectRange);
                if (host != null)
                {
                    if (!snapshotAutoEatPanel)
                    {
                        host.SetAutoEatEnergyPanelEnabled(false);
                    }
                    if (!snapshotAutoRepair)
                    {
                        host.SetAutoRepairOnDurabilityEnabled(false);
                    }
                }
                if (!snapshotAutoFishEnabled && AutoFishingFarm.IsEnabled)
                {
                    AutoFishingFarm.SetEnabled(false, host);
                }
            }

            hasSnapshot = false;
            try { host?.UI_SaveKeybinds(false); } catch { }
            FeatureLog.Life("FishingRoute", "route stopped; previous settings restored");
        }

        // Used by StopAllAutoFishing / disable-all: restore settings quietly.
        public static void ForceStop(HeartopiaComplete host)
        {
            Stop(host);
        }

        public static void Update(HeartopiaComplete host)
        {
            if (!active || host == null)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (!host.IsFishingAutomationWorldReady())
            {
                if (worldNotReadySince < 0f)
                {
                    worldNotReadySince = now;
                }
                else if (now - worldNotReadySince >= WorldUnavailableStopSeconds)
                {
                    Stop(host);
                    try { host.UI_AddMenuNotification(host.UI_Localize("Fishing Locations stopped (world unavailable)"), new Color(1f, 0.65f, 0.45f)); } catch { }
                }

                // Every cached position belongs to the map that is going away.
                if (travelling)
                {
                    EndTravel(host, "world is changing");
                }

                lastStatus = "Waiting for world";
                return;
            }

            worldNotReadySince = -1f;

            // The engine is the route's workhorse; if the user (hotkey/toggle) switched it off,
            // the route cannot continue — stop and restore instead of silently re-enabling.
            if (!AutoFishingFarm.IsEnabled)
            {
                Stop(host);
                try { host.UI_AddMenuNotification(host.UI_Localize("Fishing Locations stopped (Auto Fishing disabled)"), new Color(1f, 0.65f, 0.45f)); } catch { }
                return;
            }

            if (travelling)
            {
                TickTravel(host, now);
                return;
            }

            // Combined farming: the route may only move the player while the Fish slice actually owns
            // the tool. Hopping during an insect/bird slice (or a repair cycle) would drag the other
            // farm somewhere else — and a hop mid-repair cancels the aura. FREEZE rather than stop:
            // the no-fish clock keeps whatever value it had, and the route resumes on the next Fish
            // slice at the same spot. Always true when the coordinator is off.
            if (!CombinedFarmFeature.AllowsRouteHop)
            {
                lastStatus = "Paused (another farm is running)";
                return;
            }

            if (now < graceUntil)
            {
                lastStatus = "Arriving at spot";
                return;
            }

            // A bait/attractor throw restarts the no-fish window (server spawns fish in 1-3s);
            // without this the route would hop away right before the spawned fish appear.
            if (AutoFishingFarm.LastAutoBaitAt > lastHandledBaitAt)
            {
                lastHandledBaitAt = AutoFishingFarm.LastAutoBaitAt;
                noFishSinceAt = -1f;
            }

            // The no-fish window is driven only by scan EVENTS, mirroring the engine's own
            // TryAutoBaitTick (which is why Auto Bait's timer behaves): scans run only while the
            // engine is idle, so during a cast/battle the window simply freezes instead of
            // resetting. IsInFishingSession is deliberately NOT used to reset the timer — the
            // engine briefly blips it on every stale-idle exit cycle (~3s), which would restart
            // the countdown forever.
            if (AutoFishingFarm.LastScanAt >= spotArrivedAt && AutoFishingFarm.LastScanAt > lastHandledScanAt)
            {
                lastHandledScanAt = AutoFishingFarm.LastScanAt;
                if (AutoFishingFarm.LastInRangeCount > 0)
                {
                    noFishSinceAt = -1f;
                }
                else if (noFishSinceAt < 0f)
                {
                    noFishSinceAt = AutoFishingFarm.LastScanAt;
                }
            }

            // Never hop mid-cast/battle; the window keeps whatever value it had.
            if (AutoFishingFarm.IsInFishingSession)
            {
                pausedForRepair = false;
                lastStatus = "Fishing";
                return;
            }

            // Don't start the no-fish countdown until the engine actually scanned this spot
            // (tool equip / world sync can take a moment after a hop).
            if (lastHandledScanAt < spotArrivedAt)
            {
                lastStatus = "Waiting for first scan";
                return;
            }

            if (noFishSinceAt < 0f)
            {
                lastStatus = "Fishing";
                return;
            }

            if (now - noFishSinceAt < NoFishHopSeconds)
            {
                lastStatus = $"No fish {now - noFishSinceAt:F0}s / {NoFishHopSeconds:F0}s";
                return;
            }

            // Hop due — but not while the engine is held for energy. The no-fish window is fed by
            // scan events, and the energy gate stops the scans, so the countdown keeps running on
            // wall-clock with nothing able to reset it: without this the route would tour every
            // spot on the list while the player simply has no stamina to cast. Auto Eat (which
            // this feature switches on at start) lifts the hold and the rotation resumes.
            if (AutoFishingFarm.IsHeldForEnergy)
            {
                lastStatus = "Paused (out of energy)";
                return;
            }

            // Defer the teleport while an auto repair is running or queued — fishing (and the
            // repair) continue at the current spot; hop fires once the repair finishes.
            if (host.IsAutoRepairBusy())
            {
                pausedForRepair = true;
                lastStatus = "Paused (repair in progress)";
                return;
            }

            pausedForRepair = false;
            currentIndex = (currentIndex + 1) % TotalSpotCount;
            GoToSpot(host, currentIndex);
        }

        // The route opens at the spot nearest the player and goes round the list from there
        // (user rule 2026-09-25) — starting at index 0 sent a player standing at Forest Lake
        // across the map to Rosy River first. Flat distance; unknown position = index 0.
        private static int FindNearestSpotIndex(HeartopiaComplete host)
        {
            int total = TotalSpotCount;
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < total; i++)
            {
                float d = host.FishingRouteDistanceTo(GetSpotPos(i));
                if (d >= 0f && d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (bestDist < float.MaxValue)
            {
                ModLogger.Msg("[FishingRoute] starting at the nearest spot " + (best + 1) + "/" + total + " '" + GetSpotName(best)
                    + "' (" + bestDist.ToString("F0") + "m away).");
            }

            return best;
        }

        // A hop: walk when the walker is available, teleport otherwise. A spot the walker cannot
        // route to is SKIPPED for the next one on the list (user rule 2026-09-25) — the teleport is
        // kept only for a walker that is not ours to drive (a boss run) and for the case where
        // every spot on the list refused in a row.
        private static void GoToSpot(HeartopiaComplete host, int index)
        {
            currentIndex = index;
            if (!host.FishingRouteWalkAvailable)
            {
                TeleportToSpot(host, currentIndex);
                return;
            }

            if (!host.FishingRoutePrepareWalk(out string why))
            {
                ModLogger.Msg("[FishingRoute] walking unavailable (" + why + ") — teleporting to '" + GetSpotName(currentIndex) + "'.");
                TeleportToSpot(host, currentIndex);
                return;
            }

            for (int attempt = 0; attempt < TotalSpotCount; attempt++)
            {
                if (TryBeginTravel(host, currentIndex))
                {
                    return;
                }

                int next = (currentIndex + 1) % TotalSpotCount;
                ModLogger.Msg("[FishingRoute] no route to '" + GetSpotName(currentIndex) + "' — skipping to spot "
                    + (next + 1) + "/" + TotalSpotCount + " '" + GetSpotName(next) + "'.");
                currentIndex = next;
            }

            ModLogger.Msg("[FishingRoute] no route to any spot on the list — teleporting to '" + GetSpotName(currentIndex) + "'.");
            TeleportToSpot(host, currentIndex);
        }

        private static void TeleportToSpot(HeartopiaComplete host, int index)
        {
            Vector3 pos = GetSpotPos(index);
            host.TeleportToLocationWithOffset(pos, 0f);
            MarkArrived(index, "Teleporting");
            Log($"Hop to spot {index + 1}/{TotalSpotCount} '{GetSpotName(index)}' at {pos}");
        }

        private static void MarkArrived(int index, string status)
        {
            float now = Time.unscaledTime;
            spotArrivedAt = now;
            graceUntil = now + SettleGraceSeconds;
            noFishSinceAt = -1f;
            lastHandledScanAt = -999f;
            lastStatus = status;
        }

        // ---- Travel (walk / ride) ----------------------------------------------------------

        private static bool TryBeginTravel(HeartopiaComplete host, int index)
        {
            Vector3 pos = GetSpotPos(index);
            float away = host.FishingRouteDistanceTo(pos);
            if (away >= 0f && away <= TravelArriveDistance)
            {
                MarkArrived(index, "Arriving at spot");
                ModLogger.Msg("[FishingRoute] already at '" + GetSpotName(index) + "' (" + away.ToString("F1") + "m).");
                return true;
            }

            travelling = true;
            travelWalking = false;
            travelPausedForFishing = false;
            travelPausedForRepair = false;
            travelRepairHoldSince = -1f;
            travelIndex = index;
            travelTarget = pos;
            travelStartedAt = Time.unscaledTime;
            travelResumeAt = -999f;
            travelNextStatusAt = -999f;
            travelWalkFailures = 0;
            if (!StartTravelWalk(host, "hop"))
            {
                travelling = false;
                return false;
            }

            return true;
        }

        // A travel that failed under way: the spot is given up for the next one on the list.
        private static void SkipToNextSpot(HeartopiaComplete host, string why)
        {
            int failed = travelIndex;
            EndTravel(host, why);
            int next = (failed + 1) % TotalSpotCount;
            ModLogger.Msg("[FishingRoute] giving up on '" + GetSpotName(failed) + "' (" + why + ") — next spot "
                + (next + 1) + "/" + TotalSpotCount + " '" + GetSpotName(next) + "'.");
            GoToSpot(host, next);
        }

        private static bool StartTravelWalk(HeartopiaComplete host, string why)
        {
            if (!host.FishingRouteBeginWalk(travelTarget, GetSpotName(travelIndex)))
            {
                ModLogger.Msg("[FishingRoute] the walker refused a route to '" + GetSpotName(travelIndex) + "' (" + why + ").");
                return false;
            }

            travelWalking = true;
            float away = host.FishingRouteDistanceTo(travelTarget);
            lastStatus = "Walking to spot " + (travelIndex + 1) + "/" + TotalSpotCount;
            ModLogger.Msg("[FishingRoute] walking to spot " + (travelIndex + 1) + "/" + TotalSpotCount + " '" + GetSpotName(travelIndex)
                + "' " + (away >= 0f ? away.ToString("F0") + "m away" : "") + " (" + why + ").");
            return true;
        }

        private static void TickTravel(HeartopiaComplete host, float now)
        {
            // Fishing on the way: a session (cast / bite / battle) stops the walk; it resumes
            // once the session is over and has stayed over for a moment.
            if (AutoFishingFarm.IsInFishingSession)
            {
                if (travelWalking)
                {
                    host.FishingRouteAbortWalk();
                    travelWalking = false;
                    ModLogger.Msg("[FishingRoute] fish on the way — stopped walking to '" + GetSpotName(travelIndex) + "'.");
                }

                travelPausedForFishing = true;
                travelResumeAt = now + TravelResumeGrace;
                lastStatus = "Fishing on the way";
                return;
            }

            if (travelPausedForFishing)
            {
                if (now < travelResumeAt)
                {
                    return;
                }

                travelPausedForFishing = false;
                if (!StartTravelWalk(host, "fishing over"))
                {
                    SkipToNextSpot(host, "no route after fishing");
                }
                return;
            }

            // An auto repair on the way (user rule 2026-09-25): the kit use is silently ignored
            // unless the player stands still, and the restore aura only helps while the player is
            // inside it. Queued, in use, or aura running — stand still until it is over. Bounded,
            // so a wedged repair cannot pin the route.
            bool repairBusy;
            try { repairBusy = host.IsAutoRepairBusy(); } catch { repairBusy = false; }
            if (repairBusy && (travelRepairHoldSince < 0f || now - travelRepairHoldSince <= TravelRepairHoldMax))
            {
                if (travelRepairHoldSince < 0f)
                {
                    travelRepairHoldSince = now;
                }

                if (travelWalking)
                {
                    host.FishingRouteAbortWalk();
                    travelWalking = false;
                    ModLogger.Msg("[FishingRoute] auto repair on the way — stopped walking to '" + GetSpotName(travelIndex) + "'.");
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
                ModLogger.Msg("[FishingRoute] repair " + (repairBusy ? "hold timed out" : "done") + " — walking on to '" + GetSpotName(travelIndex) + "'.");
                if (!StartTravelWalk(host, "repair over"))
                {
                    SkipToNextSpot(host, "no route after repair");
                }
                return;
            }

            if (!repairBusy)
            {
                travelRepairHoldSince = -1f;
            }

            // Another farm slice owns the tool: stand still, resume when it hands back.
            if (!CombinedFarmFeature.AllowsRouteHop)
            {
                if (travelWalking)
                {
                    host.FishingRouteAbortWalk();
                    travelWalking = false;
                    ModLogger.Msg("[FishingRoute] another farm is running — walk to '" + GetSpotName(travelIndex) + "' paused.");
                }

                lastStatus = "Paused (another farm is running)";
                return;
            }

            if (!travelWalking)
            {
                if (!StartTravelWalk(host, "resume"))
                {
                    SkipToNextSpot(host, "no route on resume");
                }
                return;
            }

            float away = host.FishingRouteDistanceTo(travelTarget);
            if (away >= 0f && away <= TravelArriveDistance)
            {
                ArriveByWalk(host, away);
                return;
            }

            if (now - travelStartedAt > TravelDeadlineSeconds)
            {
                SkipToNextSpot(host, "travel deadline (" + TravelDeadlineSeconds.ToString("F0") + "s)");
                return;
            }

            if (now >= travelNextStatusAt)
            {
                travelNextStatusAt = now + 0.5f;
                lastStatus = "Walking to spot " + (travelIndex + 1) + "/" + TotalSpotCount
                    + (away >= 0f ? " (" + away.ToString("F0") + "m)" : "");
            }

            if (host.FishingRouteTickWalk())
            {
                // The walker ended the leg itself: its own arrival test, or it gave up.
                travelWalking = false;
                away = host.FishingRouteDistanceTo(travelTarget);
                if (away >= 0f && away <= TravelWalkerEndAccept)
                {
                    ArriveByWalk(host, away);
                    return;
                }

                travelWalkFailures++;
                ModLogger.Msg("[FishingRoute] the walker ended the leg " + (away >= 0f ? away.ToString("F0") + "m" : "?")
                    + " from '" + GetSpotName(travelIndex) + "' (" + travelWalkFailures + "/" + TravelMaxWalkFailures + ").");
                if (travelWalkFailures >= TravelMaxWalkFailures || !StartTravelWalk(host, "retry"))
                {
                    SkipToNextSpot(host, "walk failed");
                }
            }
        }

        private static void ArriveByWalk(HeartopiaComplete host, float away)
        {
            host.FishingRouteAbortWalk();   // stops the axis and gets out of the vehicle
            int index = travelIndex;
            float took = Time.unscaledTime - travelStartedAt;
            travelling = false;
            travelWalking = false;
            travelPausedForFishing = false;
            travelPausedForRepair = false;
            MarkArrived(index, "Arriving at spot");
            ModLogger.Msg("[FishingRoute] arrived at spot " + (index + 1) + "/" + TotalSpotCount + " '" + GetSpotName(index)
                + "' on foot (" + away.ToString("F1") + "m, " + took.ToString("F0") + "s).");
        }

        private static void EndTravel(HeartopiaComplete host, string why)
        {
            if (!travelling)
            {
                return;
            }

            if (travelWalking)
            {
                try { host?.FishingRouteAbortWalk(); } catch (Exception ex) { ModLogger.Msg("[FishingRoute] abort threw: " + ex.Message); }
            }

            travelling = false;
            travelWalking = false;
            travelPausedForFishing = false;
            travelPausedForRepair = false;
            Log("travel ended: " + why);
        }

        private static void SaveCurrentLocation(HeartopiaComplete host)
        {
            GameObject player = HeartopiaComplete.GetLocalPlayer();
            if (player == null)
            {
                try { host.UI_AddMenuNotification(host.UI_Localize("Player unavailable"), new Color(1f, 0.55f, 0.55f)); } catch { }
                return;
            }

            Vector3 pos = player.transform.position;
            string name = "Custom " + (customSpots.Count + 1);
            customSpots.Add(new HeartopiaComplete.CustomTeleportEntry { name = name, position = pos });
            try { host.UI_SaveKeybinds(false); } catch { }
            try { host.UI_AddMenuNotification(host.UI_LocalizeFormat("Fishing spot saved: {0}", name), new Color(0.45f, 1f, 0.55f)); } catch { }
            Log($"Custom spot saved '{name}' at {pos}");
        }


        // UGUI entry for the "Save Current Location" button — a pure pass-through (the private
        // method is otherwise reachable only from this class's own DrawSection).
        public static void SaveCurrentLocationFromUi(HeartopiaComplete host) => SaveCurrentLocation(host);

        // Removal + route-pointer fixup, extracted verbatim from DrawSection's old inline block
        // so both rendering surfaces (IMGUI above, UGUI in HeartopiaComplete.UguiFishingContent.cs)
        // share ONE implementation. Keeps the route pointer stable when the list shrinks under
        // it: capture the index mapping BEFORE removal — dropping the last custom spot flips
        // UseCustomOnly back to the full list, which changes the index space.
        public static void RemoveCustomSpotAt(int removeIndex, HeartopiaComplete host)
        {
            if (removeIndex < 0 || removeIndex >= customSpots.Count)
            {
                return;
            }

            bool wasCustomOnly = UseCustomOnly;
            int removedRouteIndex = wasCustomOnly ? removeIndex : FixedSpots.Length + removeIndex;
            customSpots.RemoveAt(removeIndex);
            if (active && wasCustomOnly != UseCustomOnly)
            {
                currentIndex = 0;
            }
            else if (active && currentIndex >= removedRouteIndex && currentIndex > 0)
            {
                currentIndex--;
            }
            if (active && currentIndex >= TotalSpotCount)
            {
                currentIndex = 0;
            }
            try { host.UI_SaveKeybinds(false); } catch { }
        }

        private static void Log(string message)
        {
            if (!HeartopiaComplete.MasterLogAutoFish)
            {
                return;
            }

            ModLogger.Msg("[FishingRoute] " + message);
        }
    }
}
