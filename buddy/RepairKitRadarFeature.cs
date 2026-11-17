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
        // In-range marker ids for one sync. Not repairKitSeenScratch: that set is the scan's
        // world snapshot, and a world-epoch change prunes entries missing from it.
        private readonly HashSet<uint> repairKitInRangeScratch = new HashSet<uint>();
        private readonly List<uint> repairKitScratchIds = new List<uint>();
        private readonly List<uint> repairKitRemoveDrainIds = new List<uint>();

        private bool repairKitHooksRegistered;
        private bool repairKitNeedsScan;
        private bool repairKitRemoveDetourTried;
        private int repairKitScanEpoch = -1;
        private NativeDetour repairKitRemoveDetour;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RepairKitRemoveRestorerHookDelegate(uint netId);

        private delegate IntPtr RepairKitCompileMethodDelegate(IntPtr method);

        private static RepairKitCompileMethodDelegate repairKitCompileMethod;
        private static RepairKitRemoveRestorerHookDelegate repairKitRemoveHookKeepAlive;
        private static RepairKitRemoveRestorerHookDelegate repairKitRemoveTrampoline;

        // Hook writes scalars only. Sync drains this on the main thread. Capacity is a power of two.
        private const int RepairKitRemoveQueueCapacity = 64;
        private static readonly uint[] repairKitRemoveQueue = new uint[RepairKitRemoveQueueCapacity];
        private static readonly object repairKitRemoveQueueLock = new object();
        private static int repairKitRemoveQueueWrite;
        private static int repairKitRemoveQueueRead;
        private static bool repairKitRemoveQueueOverflow;

        private bool IsRepairKitRadarWorkWanted =>
            this.showRepairKitRadar || this.showRepairKitAuraRadius;

        private void ClearRepairKitTrackedMarkers()
        {
            foreach (KeyValuePair<uint, GameObject> pair in this.trackedRepairKitMarkers)
            {
                if (pair.Value != null)
                {
                    this.RemoveMarkerMetadata(pair.Value);
                    this.RemoveTrackedMarkerMapping(pair.Value);
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

        // Hard failures set repairKitRemoveDetourTried and return true so the world-ready gate
        // stops. A later toggle does not retry: the flag stays for the session. ToolRestoreDestroyEvent
        // rescan remains the fallback. AuraMono or the class not being loaded yet returns false.
        private bool TryInstallRepairKitRemoveDetour()
        {
            if (this.repairKitRemoveDetourTried || this.repairKitRemoveDetour != null)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.ItemDisplay.ToolRestorerProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.ItemDisplay",
                        "ToolRestorerProtocolManager");
                }

                if (cls == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "CmdRemoveRestorer", 1);
                if (method == IntPtr.Zero)
                {
                    return this.StopRepairKitRemoveDetour(
                        "CmdRemoveRestorer(1) not found — rescan on ToolRestoreDestroyEvent remains the fallback");
                }

                IntPtr nativePtr = this.CompileRepairKitDetourTarget(method);
                if (nativePtr == IntPtr.Zero)
                {
                    return this.StopRepairKitRemoveDetour(
                        "mono_compile_method unavailable for CmdRemoveRestorer — rescan remains the fallback");
                }

                repairKitRemoveHookKeepAlive = RepairKitRemoveRestorerHook;
                this.repairKitRemoveDetour = new NativeDetour(nativePtr, repairKitRemoveHookKeepAlive);
                repairKitRemoveTrampoline = this.repairKitRemoveDetour.GenerateTrampoline<RepairKitRemoveRestorerHookDelegate>();
                if (repairKitRemoveTrampoline == null)
                {
                    this.UndoRepairKitRemoveDetour();
                    return this.StopRepairKitRemoveDetour(
                        "CmdRemoveRestorer trampoline unavailable — detour reverted; rescan remains the fallback");
                }

                this.repairKitRemoveDetourTried = true;
                FeatureLog.Life(RepairKitRadarTag, "CmdRemoveRestorer detour installed @0x"
                    + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                this.UndoRepairKitRemoveDetour();
                return this.StopRepairKitRemoveDetour(
                    "CmdRemoveRestorer detour install failed: " + ex.Message);
            }
        }

        private bool StopRepairKitRemoveDetour(string reason)
        {
            this.repairKitRemoveDetourTried = true;
            FeatureLog.Fail(RepairKitRadarTag, reason);
            return true;
        }

        private void UndoRepairKitRemoveDetour()
        {
            NativeDetour detour = this.repairKitRemoveDetour;
            this.repairKitRemoveDetour = null;
            if (detour != null)
            {
                try
                {
                    detour.Undo();
                }
                catch
                {
                }
            }

            repairKitRemoveTrampoline = null;
            repairKitRemoveHookKeepAlive = null;
        }

        private IntPtr CompileRepairKitDetourTarget(IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (repairKitCompileMethod == null)
            {
                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                if (monoModule != IntPtr.Zero)
                {
                    repairKitCompileMethod = this.GetAuraMonoExport<RepairKitCompileMethodDelegate>(
                        monoModule, "mono_compile_method");
                }
            }

            return repairKitCompileMethod != null ? repairKitCompileMethod(method) : IntPtr.Zero;
        }

        // Native boundary: enqueue the netId, then always call the original. No Unity, no dictionaries.
        private static void RepairKitRemoveRestorerHook(uint netId)
        {
            try
            {
                lock (repairKitRemoveQueueLock)
                {
                    int pending = repairKitRemoveQueueWrite - repairKitRemoveQueueRead;
                    if (pending < RepairKitRemoveQueueCapacity)
                    {
                        repairKitRemoveQueue[repairKitRemoveQueueWrite & (RepairKitRemoveQueueCapacity - 1)] = netId;
                        repairKitRemoveQueueWrite++;
                    }
                    else
                    {
                        repairKitRemoveQueueOverflow = true;
                    }
                }
            }
            catch
            {
            }

            RepairKitRemoveRestorerHookDelegate orig = repairKitRemoveTrampoline;
            if (orig != null)
            {
                orig(netId);
            }
        }

        private void DrainRepairKitRemoveQueue()
        {
            bool overflow;
            this.repairKitRemoveDrainIds.Clear();
            lock (repairKitRemoveQueueLock)
            {
                while (repairKitRemoveQueueRead != repairKitRemoveQueueWrite)
                {
                    uint netId = repairKitRemoveQueue[repairKitRemoveQueueRead & (RepairKitRemoveQueueCapacity - 1)];
                    repairKitRemoveQueueRead++;
                    this.repairKitRemoveDrainIds.Add(netId);
                }

                overflow = repairKitRemoveQueueOverflow;
                repairKitRemoveQueueOverflow = false;
            }

            for (int i = 0; i < this.repairKitRemoveDrainIds.Count; i++)
            {
                uint netId = this.repairKitRemoveDrainIds[i];
                this.RemoveRepairKitVisuals(netId);
                this.repairKitEntries.Remove(netId);
            }

            if (overflow)
            {
                this.repairKitNeedsScan = true;
            }
        }

        // Called from RunRadar while Repair Kits and/or Repair Kit Aura is on. Aura-only still
        // drains the snapshot, then drops markers and returns so a later aura pass can draw rings.
        private void SyncRepairKitRadarMarkers(Vector3 scanOrigin, Material xRay, Material bg)
        {
            this.DrainRepairKitRemoveQueue();
            this.EnsureRepairKitRadarHooks();
            if (this.repairKitNeedsScan && this.TryScanRepairKitsIntoEntries())
            {
                this.repairKitNeedsScan = false;
            }

            if (!this.showRepairKitRadar)
            {
                this.ClearRepairKitTrackedMarkers();
                return;
            }

            FeatureLog.Once(RepairKitRadarTag, "radar-first-sync", "repair kit markers syncing — the Repair Kits radar category is on");

            float maxRangeSqr = this.radarMaxDistance * this.radarMaxDistance;
            this.repairKitInRangeScratch.Clear();
            foreach (RepairKitEntry entry in this.repairKitEntries.Values)
            {
                if ((scanOrigin - entry.Position).sqrMagnitude > maxRangeSqr)
                {
                    continue;
                }

                this.repairKitInRangeScratch.Add(entry.NetId);
                if (this.trackedRepairKitMarkers.TryGetValue(entry.NetId, out GameObject existing) && existing != null)
                {
                    existing.transform.position = entry.Position;
                    continue;
                }

                GameObject marker = this.CreateMarker(entry.Position, "repairkit", xRay, bg, null);
                if (marker == null)
                {
                    continue;
                }

                marker.name = RepairKitTrackedMarkerPrefix + entry.NetId.ToString();
                this.trackedRepairKitMarkers[entry.NetId] = marker;
                RadarMarkerMetadata metadata = this.GetMarkerMetadata(marker);
                if (metadata != null)
                {
                    metadata.MapSpeciesItemId = entry.StaticId;
                    this.SetMarkerMetadata(marker, metadata);
                }
            }

            this.repairKitScratchIds.Clear();
            foreach (KeyValuePair<uint, GameObject> tracked in this.trackedRepairKitMarkers)
            {
                if (tracked.Value == null || !this.repairKitInRangeScratch.Contains(tracked.Key))
                {
                    this.repairKitScratchIds.Add(tracked.Key);
                }
            }

            for (int i = 0; i < this.repairKitScratchIds.Count; i++)
            {
                this.RemoveRepairKitTrackedMarker(this.repairKitScratchIds[i]);
            }
        }

        private static bool IsRepairKitTrackedMarkerName(string markerName)
        {
            return !string.IsNullOrEmpty(markerName)
                && markerName.StartsWith(RepairKitTrackedMarkerPrefix, StringComparison.Ordinal);
        }

        private void RemoveRepairKitTrackedMarker(uint netId)
        {
            if (this.trackedRepairKitMarkers.TryGetValue(netId, out GameObject marker) && marker != null)
            {
                this.RemoveMarkerMetadata(marker);
                this.RemoveTrackedMarkerMapping(marker);
                UnityEngine.Object.Destroy(marker);
            }

            this.trackedRepairKitMarkers.Remove(netId);
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
