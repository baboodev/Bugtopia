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
    public static class FishingRouteFeature
    {
        private const float NoFishHopSeconds = 10f;
        private const float SettleGraceSeconds = 3f;
        private const float WorldUnavailableStopSeconds = 30f;
        private const float ForcedDetectRange = 200f;

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

        private static readonly FixedSpot[] FixedSpots = new FixedSpot[]
        {
            // RIVER ====================
            new FixedSpot("Rosy River 1", -132.044f, 20.728f, 123.636f),
            new FixedSpot("Rosy River 2", -110.786f, 20.096f, 121.876f),
            new FixedSpot("Shallow River 1", 109.264f, 19.823f, 107.453f),
            new FixedSpot("Shallow River 2", 77.957f, 20.506f, 97.549f),
            new FixedSpot("Tranquil River 1", -112.565f, 15.698f, -138.189f),
            new FixedSpot("Tranquil River 2", -91.925f, 19.004f, -97.962f),
            new FixedSpot("Giantwood River 1", 68.196f, 19.838f, -95.980f),
            new FixedSpot("Giantwood River 2", 89.264f, 18.126f, -114.870f),
            // LAKE ====================
            new FixedSpot("Forest Lake 1", 169.729f, 31.086f, 79.796f),
            new FixedSpot("Forest Lake 2", 156.001f, 21.425f, 8.155f),
            new FixedSpot("Forest Lake 3", 150.090f, 21.495f, -52.907f),
            new FixedSpot("Meadow Lake", -188.604f, 19.717f, -21.203f),
            new FixedSpot("Onsen Mountain Lake 1", 7.902f, 20.045f, 166.517f),
            new FixedSpot("Onsen Mountain Lake 2", -68.906f, 28.009f, 199.926f),
            new FixedSpot("Suburban Lake 1", -77.757f, 20.781f, 91.570f),
            new FixedSpot("Suburban Lake 2", -15.798f, 23.880f, 86.701f),
            new FixedSpot("Suburban Lake 3", 81.600f, 22.236f, 38.265f),
            new FixedSpot("Suburban Lake 4", 84.599f, 21.996f, 11.957f),
            new FixedSpot("Suburban Lake 5", 63.881f, 22.350f, -34.296f),
            new FixedSpot("Suburban Lake 6", -9.485f, 15.069f, -72.374f),
            new FixedSpot("Suburban Lake 7", -99.859f, 19.652f, -53.989f),
            new FixedSpot("Suburban Lake 8", -91.586f, 25.915f, 60.682f),
            // SEA ====================
            new FixedSpot("East Sea 1", 266.370f, 11.274f, -105.900f),
            new FixedSpot("East Sea 2", 222.824f, 10.609f, -61.501f),
            new FixedSpot("East Sea 3", 249.190f, 10.640f, -4.454f),
            new FixedSpot("East Sea 4", 245.587f, 10.670f, 33.395f),
            new FixedSpot("East Sea 5", 250.926f, 13.364f, 94.227f),
            new FixedSpot("Old Sea 1", -182.468f, 10.563f, 201.788f),
            new FixedSpot("Old Sea 2", -157.400f, 10.841f, 250.438f),
            new FixedSpot("Old Sea 3", -138.933f, 10.926f, 303.255f),
            new FixedSpot("Old Sea 4", -94.604f, 10.557f, 267.479f),
            new FixedSpot("Old Sea 5", 2.903f, 10.833f, 250.543f),
            new FixedSpot("Whale Sea 1", -226.625f, 11.911f, -56.772f),
            new FixedSpot("Whale Sea 2", -223.321f, 10.472f, -13.959f),
            new FixedSpot("Whale Sea 3", -251.356f, 10.776f, 8.765f),
            new FixedSpot("Zephyr Sea 1", 46.434f, 11.500f, -180.964f),
            new FixedSpot("Zephyr Sea 2", 40.823f, 11.601f, -158.479f),
            new FixedSpot("Zephyr Sea 3", 0.809f, 11.698f, -142.726f),
            new FixedSpot("Zephyr Sea 4", -40.134f, 11.298f, -145.853f),
            new FixedSpot("Zephyr Sea 5", -114.150f, 11.407f, -185.543f),
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
            currentIndex = 0;
            pausedForRepair = false;
            worldNotReadySince = -1f;
            TeleportToSpot(host, currentIndex);
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
            Log("Route stopped; previous settings restored.");
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

            // Hop due. Defer the teleport while an auto repair is running or queued — fishing
            // (and the repair) continue at the current spot; hop fires once the repair finishes.
            if (host.IsAutoRepairBusy())
            {
                pausedForRepair = true;
                lastStatus = "Paused (repair in progress)";
                return;
            }

            pausedForRepair = false;
            currentIndex = (currentIndex + 1) % TotalSpotCount;
            TeleportToSpot(host, currentIndex);
        }

        private static void TeleportToSpot(HeartopiaComplete host, int index)
        {
            Vector3 pos = GetSpotPos(index);
            host.TeleportToLocationWithOffset(pos, 0f);
            float now = Time.unscaledTime;
            spotArrivedAt = now;
            graceUntil = now + SettleGraceSeconds;
            noFishSinceAt = -1f;
            lastHandledScanAt = -999f;
            lastStatus = "Teleporting";
            Log($"Hop to spot {index + 1}/{TotalSpotCount} '{GetSpotName(index)}' at {pos}");
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
