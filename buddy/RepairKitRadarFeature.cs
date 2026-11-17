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
        private const string ToolRestorerEventName = "ScriptsRefactory.DataAndProtocol.Events.ToolRestorerEvent";
        private const string ToolRestoreDestroyEventName = "ScriptsRefactory.DataAndProtocol.Events.ToolRestoreDestroyEvent";
        private const int ToolRestorerEventBytes = 8;
        private const int ToolRestoreDestroyEventBytes = 4;

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
        private int repairKitScanEpoch = -1;
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

        // Metadata-only. The Mono resolve for the remove detour waits on the world-ready gate.
        private void EnsureRepairKitRadarHooks()
        {
            if (this.repairKitHooksRegistered)
            {
                return;
            }

            this.repairKitHooksRegistered = true;
            if (!this.RegisterGameEventHook(ToolRestorerEventName, ToolRestorerEventBytes, _ => this.RequestRepairKitRescan()))
            {
                FeatureLog.Fail(RepairKitRadarTag, "ToolRestorerEvent hook registration refused — new kits will wait for the next sync");
            }

            if (!this.RegisterGameEventHook(ToolRestoreDestroyEventName, ToolRestoreDestroyEventBytes, _ => this.RequestRepairKitRescan()))
            {
                FeatureLog.Fail(RepairKitRadarTag, "ToolRestoreDestroyEvent hook registration refused — landed kits will wait for the next sync");
            }

            this.RegisterWorldReadyCallback(RepairKitRadarTag, this.TryInstallRepairKitRemoveDetour);
            this.repairKitNeedsScan = true;
        }

        private void RequestRepairKitRescan()
        {
            if (this.IsRepairKitRadarWorkWanted)
            {
                this.repairKitNeedsScan = true;
            }
        }

        // The CmdRemoveRestorer detour lands later. Returning true marks this world done
        // so the gate does not retry the stub.
        private bool TryInstallRepairKitRemoveDetour()
        {
            return true;
        }

        private void SyncRepairKitRadarMarkers(Vector3 scanOrigin, Material mat, Material mat2)
        {
            if (this.repairKitNeedsScan && this.TryScanRepairKitsIntoEntries())
            {
                this.repairKitNeedsScan = false;
            }
        }

        // Pinned GetComponents<ToolRestorerComponent>. No owner filter — every standing kit.
        // infrastructureOk with an empty result is a real snapshot of zero kits.
        // A component that fails the data/netId/position read is not proof it is gone: successful
        // rows are still upserted, but absent ids are pruned only on a clean enumeration. A dirty
        // scan returns false so the caller keeps repairKitNeedsScan and retries.
        private bool TryScanRepairKitsIntoEntries()
        {
            if (this.repairKitScanEpoch != this.WorldReadyEpoch)
            {
                this.repairKitScanEpoch = this.WorldReadyEpoch;
                this.repairKitSeenScratch.Clear();
                this.RemoveRepairKitsMissingFromSeen();
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || !AuraMonoPinningAvailable)
            {
                return false;
            }

            IntPtr cls = this.ResolveToolRestorerKitViewClass();
            if (cls == IntPtr.Zero)
            {
                if (this.IsWorldReady)
                {
                    FeatureLog.Fail(RepairKitRadarTag, "ToolRestorerComponent class unavailable");
                }

                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(cls, out List<IntPtr> components, out bool infrastructureOk, pins)
                    || components == null)
                {
                    if (infrastructureOk)
                    {
                        this.repairKitSeenScratch.Clear();
                        this.RemoveRepairKitsMissingFromSeen();
                        return true;
                    }

                    if (this.IsWorldReady)
                    {
                        FeatureLog.Fail(RepairKitRadarTag, "GetComponents<ToolRestorerComponent> could not run");
                    }

                    return false;
                }

                this.repairKitSeenScratch.Clear();
                int readFailures = 0;
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero
                        || !this.TryReadToolRestorerKitData(comp, out uint ownerNetId, out int staticId))
                    {
                        readFailures++;
                        continue;
                    }

                    if ((!this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(comp, "Entity", out entityObj) || entityObj == IntPtr.Zero))
                    {
                        readFailures++;
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0u)
                        {
                            if (!this.TryGetAuraMonoEntityNetId(entityObj, out netId) || netId == 0u)
                            {
                                readFailures++;
                                continue;
                            }
                        }

                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 position)
                            && !this.TryGetAuraMonoEntityPosition(entityObj, out position))
                        {
                            readFailures++;
                            continue;
                        }

                        this.repairKitSeenScratch.Add(netId);
                        if (!this.repairKitEntries.TryGetValue(netId, out RepairKitEntry entry))
                        {
                            entry = new RepairKitEntry { NetId = netId };
                            this.repairKitEntries[netId] = entry;
                        }

                        entry.OwnerNetId = ownerNetId;
                        entry.StaticId = staticId;
                        entry.Position = position;
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }

                if (readFailures == 0)
                {
                    this.RemoveRepairKitsMissingFromSeen();
                    return true;
                }

                FeatureLog.Fail(RepairKitRadarTag, readFailures + " of " + components.Count
                    + " repair kit component(s) unreadable — snapshot kept, rescan pending");
                return false;
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(RepairKitRadarTag, "repair kit scan failed: " + ex.Message);
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private void RemoveRepairKitsMissingFromSeen()
        {
            this.repairKitScratchIds.Clear();
            foreach (KeyValuePair<uint, RepairKitEntry> pair in this.repairKitEntries)
            {
                if (!this.repairKitSeenScratch.Contains(pair.Key))
                {
                    this.repairKitScratchIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < this.repairKitScratchIds.Count; i++)
            {
                uint netId = this.repairKitScratchIds[i];
                this.repairKitEntries.Remove(netId);
                this.RemoveRepairKitVisuals(netId);
            }
        }

        private void RemoveRepairKitVisuals(uint netId)
        {
            this.DestroyRepairKitVisual(this.trackedRepairKitMarkers, netId);
            this.DestroyRepairKitVisual(this.repairKitAuraRings, netId);
        }

        private void DestroyRepairKitVisual(Dictionary<uint, GameObject> visuals, uint netId)
        {
            if (!visuals.TryGetValue(netId, out GameObject visual))
            {
                return;
            }

            if (visual != null)
            {
                this.RemoveMarkerMetadata(visual);
                this.RemoveTrackedMarkerMapping(visual);
                UnityEngine.Object.Destroy(visual);
            }

            visuals.Remove(netId);
        }
    }
}
