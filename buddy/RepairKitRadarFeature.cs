using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // Repair kits = ToolRestorer view entities (Entity 1750/1751/1752).
    // Discovery: one GetComponents scan on enable, then ToolRestorerEvent /
    // ToolRestoreDestroyEvent rescan + CmdRemoveRestorer NativeDetour for remove.
    // Spec: docs/superpowers/specs/2026-11-17-repair-kit-radar-design.md
    public partial class HeartopiaComplete
    {
        private const string RepairKitRadarTag = "RepairKitRadar";
        private const string RepairKitCanonicalLabel = "Repair Kit";
        private const string RepairKitTrackedMarkerPrefix = "RepairKitMarker_";
        private const float RepairKitAuraRadiusMeters = 5f;
        private const int RepairKitAuraRingSegments = 48;

        private sealed class RepairKitEntry
        {
            public uint NetId;
            public uint OwnerNetId;
            public int StaticId;
            public Vector3 Position;
        }

        private readonly Dictionary<uint, RepairKitEntry> repairKitEntries =
            new Dictionary<uint, RepairKitEntry>();
        private readonly Dictionary<uint, GameObject> trackedRepairKitMarkers =
            new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> repairKitAuraRings =
            new Dictionary<uint, GameObject>();
        private readonly HashSet<uint> repairKitSeenScratch = new HashSet<uint>();
        private readonly List<uint> repairKitScratchIds = new List<uint>();

        private bool repairKitHooksRegistered;
        private bool repairKitNeedsScan;
        private bool repairKitRemoveDetourTried;
        private NativeDetour repairKitRemoveDetour;
        private IntPtr repairKitRemoveTrampoline;

        private bool IsRepairKitRadarWorkWanted =>
            this.showRepairKitRadar || this.showRepairKitAuraRadius;

        private void ClearRepairKitTrackedMarkers()
        {
            foreach (KeyValuePair<uint, GameObject> pair in this.trackedRepairKitMarkers)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            this.trackedRepairKitMarkers.Clear();
        }

        private void ClearRepairKitAuraRings()
        {
            foreach (KeyValuePair<uint, GameObject> pair in this.repairKitAuraRings)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            this.repairKitAuraRings.Clear();
        }

        private void ClearRepairKitRadarState()
        {
            this.repairKitEntries.Clear();
            this.ClearRepairKitTrackedMarkers();
            this.ClearRepairKitAuraRings();
            this.repairKitNeedsScan = false;
        }

        // Filled in later tasks.
        private void EnsureRepairKitRadarHooks() { }

        private void RequestRepairKitRescan() { this.repairKitNeedsScan = true; }

        private void SyncRepairKitRadarMarkers(Vector3 scanOrigin, Material mat, Material mat2) { }
    }
}