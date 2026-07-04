using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // --- Homeland farm event diagnostics (observe-only, no game actions) ---
        //
        // Goal: move the homeland auto-farm from 30s radius re-scans to event-driven updates. The
        // distance-independent chokepoint for crop state is the ECS->data bridge
        // DataCenter.UpdateComponentData<T>(NetId netId, in T data) (XDTDataAndProtocol.
        // ComponentsData, the 2-arg overload — NOT the 3-arg (EGameLevel, NetId, in T) sibling):
        // HomelandSyncSystem pushes every server crop/plant/crop-box update through it regardless
        // of view streaming. This feature detours the inflated instantiations for CropItemData /
        // PlantItemData / CropBoxItemData and LOGS each hit (netId + scalar state via a directed
        // main-thread DataCenter read) so we can confirm which updates arrive and with what
        // parameters BEFORE wiring any actions.
        //
        // Safety properties (mirrors the NetCook OnUpdateCookerStatus detour):
        // - OFF by default; detours install lazily on the first toggle-on with an attempted-guard,
        //   so a failed install is inert (no retry loop, nothing at startup).
        // - Detour bodies are allocation-free, never throw, never call Mono: they push (tag, netId)
        //   value types into a fixed ring and forward via the trampoline. The `in T` data pointer
        //   is forwarded verbatim and NEVER dereferenced (GC/pointer hazard).
        // - Drain runs on the main thread from UpdateHomelandFarmBackground; no worker threads.
        // - ABI matches the proven EventCenter.DispatchEvent<T> hook: a fully-inflated value-type
        //   generic static method takes its declared args directly. NetId is a readonly struct with
        //   a single uint field -> passed in an integer register like a uint; `in T` -> pointer.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HomelandFarmUpdateComponentDataHookDelegate(uint netId, IntPtr dataPtr);

        private delegate IntPtr HomelandFarmEventDiagCompileMethodDelegate(IntPtr method);

        private const int HomelandFarmEventDiagTagCrop = 1;
        private const int HomelandFarmEventDiagTagPlant = 2;
        private const int HomelandFarmEventDiagTagCropBox = 3;

        // Toggle (in-session, like netCookStatusDiagEnabled; OFF by default).
        private bool homelandFarmEventDiagEnabled = false;
        private bool homelandFarmEventDiagDetourAttempted;

        // Per-component detours + keep-alives (delegate must outlive the native splice).
        private MonoMod.RuntimeDetour.NativeDetour homelandFarmEventDiagCropDetour;
        private MonoMod.RuntimeDetour.NativeDetour homelandFarmEventDiagPlantDetour;
        private MonoMod.RuntimeDetour.NativeDetour homelandFarmEventDiagCropBoxDetour;
        private HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagCropBodyKeepAlive;
        private HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagPlantBodyKeepAlive;
        private HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagCropBoxBodyKeepAlive;
        private bool homelandFarmEventDiagCropDetourInstalled;
        private bool homelandFarmEventDiagPlantDetourInstalled;
        private bool homelandFarmEventDiagCropBoxDetourInstalled;

        private const int HomelandFarmEventDiagRingSize = 256; // power of two
        private const int HomelandFarmEventDiagRingMask = HomelandFarmEventDiagRingSize - 1;
        private static readonly uint[] homelandFarmEventDiagRingNetId = new uint[HomelandFarmEventDiagRingSize];
        private static readonly int[] homelandFarmEventDiagRingTag = new int[HomelandFarmEventDiagRingSize];
        private static int homelandFarmEventDiagRingWrite; // monotonic; written only by the detour bodies (game thread)
        private int homelandFarmEventDiagRingRead; // drained on the main thread
        private static HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagTrampolineCrop;
        private static HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagTrampolinePlant;
        private static HomelandFarmUpdateComponentDataHookDelegate homelandFarmEventDiagTrampolineCropBox;
        private static long homelandFarmEventDiagHitsCrop;
        private static long homelandFarmEventDiagHitsPlant;
        private static long homelandFarmEventDiagHitsCropBox;

        // Scalar field values captured directly FROM the `in T data` pointer inside the detour body
        // (offset-based), so state is readable regardless of distance / view load state. The view-based
        // read misses whenever the entity is not in the local Entities manager — which is MOST hits,
        // because the 2-arg UpdateComponentData<T> loops ALL game levels (world-wide plant/crop writes).
        // 6 long slots per ring entry; the meaning of each slot is per-tag (see FormatHomelandFarmEventDiagState).
        private const int HomelandFarmEventDiagValuesPerEntry = 6;
        private const long HomelandFarmEventDiagUnresolved = long.MinValue; // field/class missing -> logged "?"
        private static readonly long[] homelandFarmEventDiagRingValues =
            new long[HomelandFarmEventDiagRingSize * HomelandFarmEventDiagValuesPerEntry];

        // Field offsets into the UNBOXED struct (mono_field_get_offset minus the 2*IntPtr MonoObject
        // header — the `in T` arg points at raw struct data, no header). Resolved once before install;
        // -1 = unresolved. Static because the detour bodies that read them are static. Slot order must
        // match the reads in the detour bodies AND the formatter.
        private bool homelandFarmEventDiagOffsetsResolved;
        private static readonly int[] homelandFarmEventDiagCropOffsets = { -1, -1, -1, -1, -1, -1 };   // stage,hasWeed,isPick,sowTime,ripeGrowTime,growTime
        private static readonly int[] homelandFarmEventDiagPlantOffsets = { -1, -1, -1, -1, -1, -1 };  // stage,isPick,hasCrossedSeed,masterWater,manureId,plantNetId
        private static readonly int[] homelandFarmEventDiagCropBoxOffsets = { -1 };                    // isWet

        // Allocation-free field reads for the detour body: return the scalar at (dataPtr+offset), or
        // the Unresolved sentinel when the offset never resolved / the pointer is null. Reads land on
        // valid field offsets within the struct (0 <= off < structSize), so they cannot over-read.
        private static long HomelandFarmEventDiagReadByte(IntPtr p, int off)
            => (off < 0 || p == IntPtr.Zero) ? HomelandFarmEventDiagUnresolved : Marshal.ReadByte(p, off);

        private static long HomelandFarmEventDiagReadInt(IntPtr p, int off)
            => (off < 0 || p == IntPtr.Zero) ? HomelandFarmEventDiagUnresolved : Marshal.ReadInt32(p, off);

        private static long HomelandFarmEventDiagReadLong(IntPtr p, int off)
            => (off < 0 || p == IntPtr.Zero) ? HomelandFarmEventDiagUnresolved : Marshal.ReadInt64(p, off);

        // --- Event-fed crop/box state cache (the event-driven auto-farm's data source) ---
        // The detour drain writes the latest server state for every crop / crop-box here. The auto-farm
        // reads THIS instead of doing a radius rescan, so it never hitches and works at distance (the
        // server pushes CropItemData/CropBoxItemData updates to the client even when the field is far —
        // confirmed in-game). Entries never time-expire: a crop that hasn't changed keeps its last known
        // state validly until the next event overwrites it. Cleared on capture / stop (ClearHomelandFarmAutoCaches).
        private sealed class HomelandFarmEventCropState
        {
            public int Stage;
            public bool HasWeed;
            public bool IsPick;
            public long SowTime;
            public long RipeGrowTime;
            public long GrowTime;
            public float UpdatedAt;
            // True when the entry was fed by a PlantItemData event (stage/isPick only — the struct has
            // no weed or sow-timing fields). A CropItemData event is richer and always overwrites; a
            // Plant event never overwrites a Crop-fed entry.
            public bool FromPlantEvent;
        }

        private readonly Dictionary<uint, HomelandFarmEventCropState> homelandFarmEventCropStateCache =
            new Dictionary<uint, HomelandFarmEventCropState>();
        private readonly Dictionary<uint, bool> homelandFarmEventBoxWetCache = new Dictionary<uint, bool>();

        private void ClearHomelandFarmEventStateCache()
        {
            this.homelandFarmEventCropStateCache.Clear();
            this.homelandFarmEventBoxWetCache.Clear();
        }

        private void UpdateHomelandFarmCropStateCacheFromEvent(int tag, uint netId, long v0, long v1, long v2, long v3, long v4, long v5)
        {
            if (netId == 0U)
            {
                return;
            }

            if (tag == HomelandFarmEventDiagTagCrop)
            {
                // v0..v5 = stage, hasWeed, isPick, sowTime, ripeGrowTime, growTime (see the crop detour body).
                if (v0 == HomelandFarmEventDiagUnresolved)
                {
                    return; // offsets never resolved — don't poison the cache with sentinels.
                }

                if (!this.homelandFarmEventCropStateCache.TryGetValue(netId, out HomelandFarmEventCropState entry))
                {
                    entry = new HomelandFarmEventCropState();
                    this.homelandFarmEventCropStateCache[netId] = entry;
                }

                entry.Stage = (int)v0;
                entry.HasWeed = v1 == 1L;
                entry.IsPick = v2 == 1L;
                entry.SowTime = v3;
                entry.RipeGrowTime = v4;
                entry.GrowTime = v5;
                entry.UpdatedAt = Time.realtimeSinceStartup;
                entry.FromPlantEvent = false;

                // Remote-sow adoption: crops created by an away CropSeeding never hit this detour at
                // creation (that path is AddEntity), so the tracked set can't learn their netIds from a
                // scan (field unloaded). Adopt unknown crops from their FIRST update event when their
                // sowTime falls in the window after our remote send — weed/harvest only need the netId.
                // Bounded by the captured planter count; a wrong adoption (foreign crop sown in the same
                // seconds) is harmless: harvest is server-own-gated, and returning home re-syncs truth.
                if (this.homelandFarmAutoRunning
                    && this.homelandFarmAutoRemoteSowSentUnix > 0L
                    && this.homelandFarmAutoPendingSowBoxNetIds.Count > 0
                    && entry.SowTime >= this.homelandFarmAutoRemoteSowSentUnix - 10L
                    && entry.SowTime <= this.homelandFarmAutoRemoteSowSentUnix + 300L
                    && !this.homelandFarmAutoCropNetIds.Contains(netId)
                    && !this.homelandFarmAutoHarvestedNetIds.Contains(netId)
                    && this.homelandFarmAutoCropNetIds.Count < this.homelandFarmCapturedSowPointByBoxNetId.Count)
                {
                    this.homelandFarmAutoCropNetIds.Add(netId);
                    this.HomelandFarmLog("Auto: adopted remote-sown crop netId=" + netId
                        + " (sowTime=" + entry.SowTime + ", tracked=" + this.homelandFarmAutoCropNetIds.Count + ").");
                }

                // Event-driven weeding: the instant a weed appears on one of OUR tracked crops during
                // auto-farm, send the weed command (throttled). This is what lets the auto loop sleep
                // until maturity instead of polling every second to catch weeds. Weeding works at
                // distance (netId-keyed command), so this keeps clearing weeds even while roaming.
                if (entry.HasWeed && this.homelandFarmAutoRunning && this.homelandFarmAutoCropNetIds.Contains(netId))
                {
                    this.TryHomelandFarmAutoWeedThrottled(netId);
                }
            }
            else if (tag == HomelandFarmEventDiagTagPlant)
            {
                // v0..v5 = stage, isPick, hasCrossedSeed, masterWater, manureId, plantNetId.
                // Crop-box crops on this build can surface as PlantItemData, and the capture snapshot
                // may include stage-4 plants — without a cache entry their state is invisible remotely,
                // so they sit in the tracked set forever and BLOCK the remote sow (tracked never reaches
                // 0). Cache stage/isPick from Plant events (the struct has no weed/timing fields —
                // remaining stays unknown); never overwrite a richer CropItemData-fed entry.
                if (v0 == HomelandFarmEventDiagUnresolved)
                {
                    return;
                }

                if (this.homelandFarmEventCropStateCache.TryGetValue(netId, out HomelandFarmEventCropState plantEntry))
                {
                    if (!plantEntry.FromPlantEvent)
                    {
                        return; // CropItemData-fed entry is authoritative.
                    }
                }
                else
                {
                    plantEntry = new HomelandFarmEventCropState { FromPlantEvent = true };
                    this.homelandFarmEventCropStateCache[netId] = plantEntry;
                }

                plantEntry.Stage = (int)v0;
                plantEntry.IsPick = v1 == 1L;
                plantEntry.HasWeed = false;
                plantEntry.SowTime = 0L;
                plantEntry.RipeGrowTime = 0L; // no timing on PlantItemData -> remaining stays MaxValue
                plantEntry.GrowTime = 0L;
                plantEntry.UpdatedAt = Time.realtimeSinceStartup;
            }
            else if (tag == HomelandFarmEventDiagTagCropBox)
            {
                if (v0 != HomelandFarmEventDiagUnresolved)
                {
                    this.homelandFarmEventBoxWetCache[netId] = v0 == 1L;
                }
            }
        }

        // Cache-first crop state: true if the detour has ever seen this crop. remainingSeconds is derived
        // from the cached sow/ripe/grow times + the game clock (long.MaxValue if the clock is unavailable).
        private bool TryGetHomelandFarmEventCropState(uint cropNetId, out int stage, out bool hasWeed, out bool isPick, out long remainingSeconds)
        {
            stage = 0;
            hasWeed = false;
            isPick = false;
            remainingSeconds = long.MaxValue;
            if (cropNetId == 0U || !this.homelandFarmEventCropStateCache.TryGetValue(cropNetId, out HomelandFarmEventCropState entry))
            {
                return false;
            }

            stage = entry.Stage;
            hasWeed = entry.HasWeed;
            isPick = entry.IsPick;
            if (entry.RipeGrowTime > 0L && this.TryHomelandFarmGetGameUnixTime(out long nowUnix) && nowUnix > 0L)
            {
                remainingSeconds = (entry.SowTime + entry.RipeGrowTime - entry.GrowTime) - nowUnix;
            }

            return true;
        }

        private bool TryGetHomelandFarmEventBoxWet(uint boxNetId, out bool isWet)
        {
            return this.homelandFarmEventBoxWetCache.TryGetValue(boxNetId, out isWet);
        }

        // Dedupe: last logged scalar state per (tag, netId) — a hit only logs when the state string
        // changed since the last logged line for that key (server re-sends identical data often).
        private readonly Dictionary<ulong, string> homelandFarmEventDiagLastLoggedState =
            new Dictionary<ulong, string>();
        private const int HomelandFarmEventDiagDedupeCacheCap = 1024;
        private const int HomelandFarmEventDiagMaxLogsPerDrain = 12;

        // 60s hits-per-tag summary (mirrors the "[AuraMono] N invoke exception(s) in the last 60s"
        // summary style) so quiet periods and dedupe-suppressed traffic stay visible.
        private float homelandFarmEventDiagNextSummaryAt;
        private long homelandFarmEventDiagSummaryBaseCrop;
        private long homelandFarmEventDiagSummaryBasePlant;
        private long homelandFarmEventDiagSummaryBaseCropBox;

        private void HomelandFarmEventDiagLog(string msg)
        {
            if (string.IsNullOrEmpty(msg))
            {
                return;
            }

            ModLogger.Msg("[HomelandFarm][EventDiag] " + msg);
        }

        // Allocation-free, no throw, no Mono calls. Records (tag, netId) into the ring and forwards.
        // dataPtr is the `in T` argument — forwarded verbatim, never dereferenced.
        private static void HomelandFarmEventDiagCropDetourBody(uint netId, IntPtr dataPtr)
        {
            int idx = homelandFarmEventDiagRingWrite & HomelandFarmEventDiagRingMask;
            homelandFarmEventDiagRingNetId[idx] = netId;
            homelandFarmEventDiagRingTag[idx] = HomelandFarmEventDiagTagCrop;
            // CropItemData: stage(byte) hasWeed(bool) isPick(bool) sowTime(long) ripeGrowTime(long) growTime(long).
            int vb = idx * HomelandFarmEventDiagValuesPerEntry;
            int[] off = homelandFarmEventDiagCropOffsets;
            homelandFarmEventDiagRingValues[vb] = HomelandFarmEventDiagReadByte(dataPtr, off[0]);
            homelandFarmEventDiagRingValues[vb + 1] = HomelandFarmEventDiagReadByte(dataPtr, off[1]);
            homelandFarmEventDiagRingValues[vb + 2] = HomelandFarmEventDiagReadByte(dataPtr, off[2]);
            homelandFarmEventDiagRingValues[vb + 3] = HomelandFarmEventDiagReadLong(dataPtr, off[3]);
            homelandFarmEventDiagRingValues[vb + 4] = HomelandFarmEventDiagReadLong(dataPtr, off[4]);
            homelandFarmEventDiagRingValues[vb + 5] = HomelandFarmEventDiagReadLong(dataPtr, off[5]);
            homelandFarmEventDiagRingWrite++;
            homelandFarmEventDiagHitsCrop++;

            HomelandFarmUpdateComponentDataHookDelegate tramp = homelandFarmEventDiagTrampolineCrop;
            if (tramp != null)
            {
                tramp(netId, dataPtr);
            }
        }

        private static void HomelandFarmEventDiagPlantDetourBody(uint netId, IntPtr dataPtr)
        {
            int idx = homelandFarmEventDiagRingWrite & HomelandFarmEventDiagRingMask;
            homelandFarmEventDiagRingNetId[idx] = netId;
            homelandFarmEventDiagRingTag[idx] = HomelandFarmEventDiagTagPlant;
            // PlantItemData: stage(byte) isPick(bool) hasCrossedSeed(bool) masterWater(bool) manureId(int) plantNetId(uint).
            int vb = idx * HomelandFarmEventDiagValuesPerEntry;
            int[] off = homelandFarmEventDiagPlantOffsets;
            homelandFarmEventDiagRingValues[vb] = HomelandFarmEventDiagReadByte(dataPtr, off[0]);
            homelandFarmEventDiagRingValues[vb + 1] = HomelandFarmEventDiagReadByte(dataPtr, off[1]);
            homelandFarmEventDiagRingValues[vb + 2] = HomelandFarmEventDiagReadByte(dataPtr, off[2]);
            homelandFarmEventDiagRingValues[vb + 3] = HomelandFarmEventDiagReadByte(dataPtr, off[3]);
            homelandFarmEventDiagRingValues[vb + 4] = HomelandFarmEventDiagReadInt(dataPtr, off[4]);
            homelandFarmEventDiagRingValues[vb + 5] = HomelandFarmEventDiagReadInt(dataPtr, off[5]);
            homelandFarmEventDiagRingWrite++;
            homelandFarmEventDiagHitsPlant++;

            HomelandFarmUpdateComponentDataHookDelegate tramp = homelandFarmEventDiagTrampolinePlant;
            if (tramp != null)
            {
                tramp(netId, dataPtr);
            }
        }

        private static void HomelandFarmEventDiagCropBoxDetourBody(uint netId, IntPtr dataPtr)
        {
            int idx = homelandFarmEventDiagRingWrite & HomelandFarmEventDiagRingMask;
            homelandFarmEventDiagRingNetId[idx] = netId;
            homelandFarmEventDiagRingTag[idx] = HomelandFarmEventDiagTagCropBox;
            // CropBoxItemData: isWet(bool).
            int vb = idx * HomelandFarmEventDiagValuesPerEntry;
            homelandFarmEventDiagRingValues[vb] = HomelandFarmEventDiagReadByte(dataPtr, homelandFarmEventDiagCropBoxOffsets[0]);
            homelandFarmEventDiagRingWrite++;
            homelandFarmEventDiagHitsCropBox++;

            HomelandFarmUpdateComponentDataHookDelegate tramp = homelandFarmEventDiagTrampolineCropBox;
            if (tramp != null)
            {
                tramp(netId, dataPtr);
            }
        }

        // Called from the UI when the toggle flips. Resets the dedupe/summary state so a fresh
        // session logs a clean baseline; the detours themselves stay installed once placed
        // (undoing a native detour at runtime is the riskier operation).
        private void OnHomelandFarmEventDiagToggled()
        {
            if (this.homelandFarmEventDiagEnabled)
            {
                this.homelandFarmEventDiagLastLoggedState.Clear();
                this.homelandFarmEventDiagNextSummaryAt = Time.unscaledTime + 60f;
                this.homelandFarmEventDiagSummaryBaseCrop = homelandFarmEventDiagHitsCrop;
                this.homelandFarmEventDiagSummaryBasePlant = homelandFarmEventDiagHitsPlant;
                this.homelandFarmEventDiagSummaryBaseCropBox = homelandFarmEventDiagHitsCropBox;
                this.HomelandFarmEventDiagLog("Event diagnostics ON. Detouring DataCenter.UpdateComponentData<T> "
                    + "(CropItemData/PlantItemData/CropBoxItemData). Walk around the farm; each server "
                    + "update logs netId + scalar state (deduped). No actions are wired.");
            }
            else
            {
                this.HomelandFarmEventDiagLog("Event diagnostics OFF (detours stay installed; logging stops).");
            }
        }

        // Pump: installs the detours on first enable (attempted-guard) and drains the ring. Called
        // every frame from UpdateHomelandFarmBackground; a single bool check when the toggle is off.
        private void PumpHomelandFarmEventDiag()
        {
            // Run when the diagnostics toggle is on OR auto-farm is running: the detour + drain feed the
            // crop-state cache that the event-driven auto-farm reads instead of radius-rescanning. When
            // only auto-farm needs it, the drain updates the cache but does NOT emit per-line logs.
            bool diag = this.homelandFarmEventDiagEnabled;
            bool auto = this.homelandFarmAutoRunning;
            if (!diag && !auto)
            {
                return;
            }

            this.EnsureHomelandFarmEventDiagDetours();
            this.DrainHomelandFarmEventDiagRing(diag);
            if (diag)
            {
                this.MaybeLogHomelandFarmEventDiagSummary();
            }
        }

        private void EnsureHomelandFarmEventDiagDetours()
        {
            if (this.homelandFarmEventDiagDetourAttempted)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // API not ready — retry next frame (don't mark attempted).
                }

                IntPtr dataCenterClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ComponentsData.DataCenter");
                if (dataCenterClass == IntPtr.Zero)
                {
                    dataCenterClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ComponentsData", "DataCenter");
                }
                if (dataCenterClass == IntPtr.Zero)
                {
                    return; // XDTDataAndProtocol image not loaded yet — retry next frame.
                }

                // 2-arg overload UpdateComponentData<T>(NetId, in T); the 3-arg (EGameLevel, NetId,
                // in T) sibling is skipped by the param-count filter.
                IntPtr openMethod = this.FindAuraMonoMethodOnHierarchy(dataCenterClass, "UpdateComponentData", 2);
                if (openMethod == IntPtr.Zero)
                {
                    this.homelandFarmEventDiagDetourAttempted = true;
                    this.HomelandFarmEventDiagLog("UpdateComponentData (2-arg) method not found — detours disabled.");
                    return;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                HomelandFarmEventDiagCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<HomelandFarmEventDiagCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.homelandFarmEventDiagDetourAttempted = true;
                    this.HomelandFarmEventDiagLog("mono_compile_method unavailable — detours disabled.");
                    return;
                }

                // Single shot from here on: even a partial failure must not retry-loop.
                this.homelandFarmEventDiagDetourAttempted = true;

                // Resolve the struct field offsets BEFORE the detours go live — the bodies read them on
                // the very first fire. Unresolved fields stay -1 and log as "?" (never crash).
                this.ResolveHomelandFarmEventDiagOffsets();

                this.homelandFarmEventDiagCropDetourInstalled = this.TryInstallHomelandFarmEventDiagDetour(
                    openMethod, compile, "CropItemData",
                    HomelandFarmEventDiagCropDetourBody,
                    ref this.homelandFarmEventDiagCropDetour,
                    ref this.homelandFarmEventDiagCropBodyKeepAlive,
                    ref homelandFarmEventDiagTrampolineCrop);
                this.homelandFarmEventDiagPlantDetourInstalled = this.TryInstallHomelandFarmEventDiagDetour(
                    openMethod, compile, "PlantItemData",
                    HomelandFarmEventDiagPlantDetourBody,
                    ref this.homelandFarmEventDiagPlantDetour,
                    ref this.homelandFarmEventDiagPlantBodyKeepAlive,
                    ref homelandFarmEventDiagTrampolinePlant);
                this.homelandFarmEventDiagCropBoxDetourInstalled = this.TryInstallHomelandFarmEventDiagDetour(
                    openMethod, compile, "CropBoxItemData",
                    HomelandFarmEventDiagCropBoxDetourBody,
                    ref this.homelandFarmEventDiagCropBoxDetour,
                    ref this.homelandFarmEventDiagCropBoxBodyKeepAlive,
                    ref homelandFarmEventDiagTrampolineCropBox);

                this.HomelandFarmEventDiagLog("Detour install: Crop=" + this.homelandFarmEventDiagCropDetourInstalled
                    + " Plant=" + this.homelandFarmEventDiagPlantDetourInstalled
                    + " CropBox=" + this.homelandFarmEventDiagCropBoxDetourInstalled + ".");
            }
            catch (Exception ex)
            {
                this.homelandFarmEventDiagDetourAttempted = true;
                this.HomelandFarmEventDiagLog("Detour install failed: " + ex.Message);
            }
        }

        // Inflate UpdateComponentData<T> for one component data struct, compile it and splice the
        // given allocation-free body over the native entry. Mirrors the NetCook OnUpdateCookerStatus
        // install (trampoline-or-revert) + the EventHook inflate recipe.
        private bool TryInstallHomelandFarmEventDiagDetour(
            IntPtr openMethod,
            HomelandFarmEventDiagCompileMethodDelegate compile,
            string componentShortName,
            HomelandFarmUpdateComponentDataHookDelegate body,
            ref MonoMod.RuntimeDetour.NativeDetour detourField,
            ref HomelandFarmUpdateComponentDataHookDelegate keepAliveField,
            ref HomelandFarmUpdateComponentDataHookDelegate trampolineField)
        {
            try
            {
                IntPtr componentClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ComponentsData." + componentShortName);
                if (componentClass == IntPtr.Zero)
                {
                    componentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ComponentsData", componentShortName);
                }
                if (componentClass == IntPtr.Zero)
                {
                    this.HomelandFarmEventDiagLog(componentShortName + ": mono class unresolved — skipped.");
                    return false;
                }

                if (!this.TryHomelandFarmEventDiagInflateUpdateComponentData(openMethod, componentClass, out IntPtr inflated))
                {
                    this.HomelandFarmEventDiagLog("inflate UpdateComponentData<" + componentShortName + "> failed.");
                    return false;
                }

                IntPtr nativePtr = compile(inflated);
                if (nativePtr == IntPtr.Zero)
                {
                    this.HomelandFarmEventDiagLog("mono_compile_method null for UpdateComponentData<" + componentShortName + ">.");
                    return false;
                }

                keepAliveField = body;
                detourField = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, body);

                HomelandFarmUpdateComponentDataHookDelegate tramp = detourField.GenerateTrampoline<HomelandFarmUpdateComponentDataHookDelegate>();
                if (tramp == null)
                {
                    // Without a trampoline the game would stop writing this component's data — revert.
                    try { detourField.Undo(); } catch { }
                    detourField = null;
                    keepAliveField = null;
                    this.HomelandFarmEventDiagLog("trampoline null for UpdateComponentData<" + componentShortName + ">, reverted.");
                    return false;
                }

                trampolineField = tramp;
                this.HomelandFarmEventDiagLog("hooked UpdateComponentData<" + componentShortName + "> @0x"
                    + nativePtr.ToInt64().ToString("X") + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.HomelandFarmEventDiagLog("install for " + componentShortName + " failed: " + ex.Message);
                return false;
            }
        }

        // Inflate the open generic DataCenter.UpdateComponentData<T> for a concrete component data
        // struct class. Same recipe as TryInflateDispatchForEvent: mono_class_get_type ->
        // mono_metadata_get_generic_inst (a REAL MonoGenericInst*, never a raw MonoType*[]) ->
        // mono_class_inflate_generic_method, then validate the param count before splicing.
        private unsafe bool TryHomelandFarmEventDiagInflateUpdateComponentData(IntPtr openMethod, IntPtr componentClass, out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (openMethod == IntPtr.Zero
                || componentClass == IntPtr.Zero
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null
                || auraMonoClassInflateGenericMethod == null)
            {
                return false;
            }

            IntPtr typeArg = auraMonoClassGetType(componentClass);
            if (typeArg == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = typeArg;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return false;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            inflatedMethod = auraMonoClassInflateGenericMethod(openMethod, ref context);
            if (inflatedMethod == IntPtr.Zero)
            {
                return false;
            }

            // Guard the native signature we splice our hook over: a wrong method_inst would
            // otherwise hand the body garbage arguments and AV the process.
            return AuraMonoMethodParamCountIs(inflatedMethod, 2);
        }

        // Main-thread drain of the detour ring: log each (tag, netId) with the scalar field values the
        // detour body captured from the `in T data` pointer. Dedupes on unchanged state and caps the
        // lines per drain so a burst (login floods every crop) cannot spam the log.
        private void DrainHomelandFarmEventDiagRing(bool logLines)
        {
            int write = homelandFarmEventDiagRingWrite; // monotonic snapshot
            if (this.homelandFarmEventDiagRingRead == write)
            {
                return;
            }

            int backlog = write - this.homelandFarmEventDiagRingRead;
            if (backlog > HomelandFarmEventDiagRingSize)
            {
                if (logLines)
                {
                    this.HomelandFarmEventDiagLog("ring overflow: skipped " + (backlog - HomelandFarmEventDiagRingSize) + " entries.");
                }

                this.homelandFarmEventDiagRingRead = write - HomelandFarmEventDiagRingSize;
            }

            if (this.homelandFarmEventDiagLastLoggedState.Count > HomelandFarmEventDiagDedupeCacheCap)
            {
                this.homelandFarmEventDiagLastLoggedState.Clear();
            }

            int logged = 0;
            int suppressed = 0;
            while (this.homelandFarmEventDiagRingRead != write)
            {
                int idx = this.homelandFarmEventDiagRingRead & HomelandFarmEventDiagRingMask;
                uint netId = homelandFarmEventDiagRingNetId[idx];
                int tag = homelandFarmEventDiagRingTag[idx];
                int vb = idx * HomelandFarmEventDiagValuesPerEntry;
                long v0 = homelandFarmEventDiagRingValues[vb];
                long v1 = homelandFarmEventDiagRingValues[vb + 1];
                long v2 = homelandFarmEventDiagRingValues[vb + 2];
                long v3 = homelandFarmEventDiagRingValues[vb + 3];
                long v4 = homelandFarmEventDiagRingValues[vb + 4];
                long v5 = homelandFarmEventDiagRingValues[vb + 5];
                this.homelandFarmEventDiagRingRead++;

                // Always feed the crop-state cache (this is what makes auto-farm event-driven); logging
                // is the diagnostics-only extra.
                this.UpdateHomelandFarmCropStateCacheFromEvent(tag, netId, v0, v1, v2, v3, v4, v5);

                if (!logLines || logged >= HomelandFarmEventDiagMaxLogsPerDrain)
                {
                    if (logLines)
                    {
                        suppressed++;
                    }

                    continue;
                }

                string state = this.FormatHomelandFarmEventDiagState(tag, v0, v1, v2, v3, v4, v5, out string tagName);
                ulong dedupeKey = ((ulong)(uint)tag << 32) | netId;
                if (this.homelandFarmEventDiagLastLoggedState.TryGetValue(dedupeKey, out string lastState)
                    && string.Equals(lastState, state, StringComparison.Ordinal))
                {
                    suppressed++;
                    continue;
                }

                this.homelandFarmEventDiagLastLoggedState[dedupeKey] = state;
                logged++;
                this.HomelandFarmEventDiagLog(tagName + " netId=" + netId + " " + state);
            }

            if (suppressed > 0 && logged >= HomelandFarmEventDiagMaxLogsPerDrain)
            {
                this.HomelandFarmEventDiagLog("(+" + suppressed + " hits not logged this drain: per-drain cap.)");
            }
        }

        // Resolve the struct field offsets once (idempotent). Fills the static offset tables the detour
        // bodies read; logs the resolved offsets so they can be eyeballed / cross-checked in-game.
        private void ResolveHomelandFarmEventDiagOffsets()
        {
            if (this.homelandFarmEventDiagOffsetsResolved)
            {
                return;
            }

            this.homelandFarmEventDiagOffsetsResolved = true;
            this.TryResolveHomelandFarmEventDiagFieldOffsets(
                "CropItemData",
                homelandFarmEventDiagCropOffsets,
                new[] { "stage", "hasWeed", "isPick", "sowTime", "ripeGrowTime", "growTime" });
            this.TryResolveHomelandFarmEventDiagFieldOffsets(
                "PlantItemData",
                homelandFarmEventDiagPlantOffsets,
                new[] { "stage", "isPick", "hasCrossedSeed", "masterWater", "manureId", "plantNetId" });
            this.TryResolveHomelandFarmEventDiagFieldOffsets(
                "CropBoxItemData",
                homelandFarmEventDiagCropBoxOffsets,
                new[] { "isWet" });
        }

        private void TryResolveHomelandFarmEventDiagFieldOffsets(string shortName, int[] offsets, string[] fieldNames)
        {
            IntPtr klass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ComponentsData." + shortName);
            if (klass == IntPtr.Zero)
            {
                klass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ComponentsData", shortName);
            }
            if (klass == IntPtr.Zero)
            {
                this.HomelandFarmEventDiagLog(shortName + ": class unresolved for field offsets (values will log '?').");
                return;
            }

            // TryGetTrackFieldRawOffset = mono_field_get_offset(field) - 2*IntPtr (strips the MonoObject
            // header; the `in T` arg points at the unboxed struct). See HeartopiaComplete.MapSpots.cs.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < fieldNames.Length && i < offsets.Length; i++)
            {
                offsets[i] = this.TryGetTrackFieldRawOffset(klass, fieldNames[i], out int off) ? off : -1;
                sb.Append(fieldNames[i]).Append('=').Append(offsets[i]).Append(' ');
            }

            this.HomelandFarmEventDiagLog(shortName + " field offsets: " + sb.ToString().TrimEnd());
        }

        // Format the scalars the detour body captured from the `in T data` pointer into a stable
        // state string (also the dedupe key value). No view lookup — works at any distance / level.
        private string FormatHomelandFarmEventDiagState(int tag, long v0, long v1, long v2, long v3, long v4, long v5, out string tagName)
        {
            switch (tag)
            {
                case HomelandFarmEventDiagTagCrop:
                    tagName = "CropItemData";
                    return "stage=" + HomelandFarmEventDiagFmt(v0)
                        + " hasWeed=" + HomelandFarmEventDiagFmt(v1)
                        + " isPick=" + HomelandFarmEventDiagFmt(v2)
                        + " sowTime=" + HomelandFarmEventDiagFmt(v3)
                        + " ripeGrowTime=" + HomelandFarmEventDiagFmt(v4)
                        + " growTime=" + HomelandFarmEventDiagFmt(v5);

                case HomelandFarmEventDiagTagPlant:
                    tagName = "PlantItemData";
                    return "stage=" + HomelandFarmEventDiagFmt(v0)
                        + " isPick=" + HomelandFarmEventDiagFmt(v1)
                        + " hasCrossedSeed=" + HomelandFarmEventDiagFmt(v2)
                        + " masterWater=" + HomelandFarmEventDiagFmt(v3)
                        + " manureId=" + HomelandFarmEventDiagFmt(v4)
                        + " plantNetId=" + HomelandFarmEventDiagFmtUInt(v5);

                case HomelandFarmEventDiagTagCropBox:
                    tagName = "CropBoxItemData";
                    return "isWet=" + HomelandFarmEventDiagFmt(v0);

                default:
                    tagName = "tag" + tag;
                    return "unknown tag";
            }
        }

        private static string HomelandFarmEventDiagFmt(long v)
            => v == HomelandFarmEventDiagUnresolved ? "?" : v.ToString();

        private static string HomelandFarmEventDiagFmtUInt(long v)
            => v == HomelandFarmEventDiagUnresolved ? "?" : ((uint)v).ToString();

        // 60s hits-per-component summary so traffic volume stays visible even when the dedupe
        // suppresses every line (identical re-sends) or the per-drain cap kicks in.
        private void MaybeLogHomelandFarmEventDiagSummary()
        {
            float now = Time.unscaledTime;
            if (this.homelandFarmEventDiagNextSummaryAt <= 0f)
            {
                this.homelandFarmEventDiagNextSummaryAt = now + 60f;
                return;
            }

            if (now < this.homelandFarmEventDiagNextSummaryAt)
            {
                return;
            }

            this.homelandFarmEventDiagNextSummaryAt = now + 60f;
            long cropDelta = homelandFarmEventDiagHitsCrop - this.homelandFarmEventDiagSummaryBaseCrop;
            long plantDelta = homelandFarmEventDiagHitsPlant - this.homelandFarmEventDiagSummaryBasePlant;
            long cropBoxDelta = homelandFarmEventDiagHitsCropBox - this.homelandFarmEventDiagSummaryBaseCropBox;
            this.homelandFarmEventDiagSummaryBaseCrop = homelandFarmEventDiagHitsCrop;
            this.homelandFarmEventDiagSummaryBasePlant = homelandFarmEventDiagHitsPlant;
            this.homelandFarmEventDiagSummaryBaseCropBox = homelandFarmEventDiagHitsCropBox;
            this.HomelandFarmEventDiagLog("60s summary: Crop=" + cropDelta
                + " Plant=" + plantDelta
                + " CropBox=" + cropBoxDelta
                + " (totals " + homelandFarmEventDiagHitsCrop
                + "/" + homelandFarmEventDiagHitsPlant
                + "/" + homelandFarmEventDiagHitsCropBox
                + ", installed Crop=" + this.homelandFarmEventDiagCropDetourInstalled
                + " Plant=" + this.homelandFarmEventDiagPlantDetourInstalled
                + " CropBox=" + this.homelandFarmEventDiagCropBoxDetourInstalled + ").");
        }
    }
}
