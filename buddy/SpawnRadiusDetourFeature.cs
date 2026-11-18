using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // Rewrites client-chosen insect/bird spawn and move positions to 3 m in front of the player
    // via embedded-Mono NativeDetours on InsectProtocolManager / BirdProtocolManager.
    // A non-empty radar species filter limits the rewrite to those item ids. An empty filter rewrites all.
    public partial class HeartopiaComplete
    {
        private const string SpawnRadiusDetourTag = "SpawnRadiusDetour";
        // Same placement as VacuumBirds: 3 m along the player's facing, height locked to the body.
        private const float SpawnRadiusAheadMeters = 3f;

        // Written from OnUpdate. Hook bodies read only these statics: no Unity, no Mono, no Mathf.
        private static bool spawnRadiusInsectArmed;
        private static bool spawnRadiusBirdArmed;
        private static bool spawnRadiusAnchorValid;
        private static float spawnRadiusPlaceX;
        private static float spawnRadiusPlaceY;
        private static float spawnRadiusPlaceZ;

        // Filter ids and netId→species are plain arrays. The hook only indexes them.
        // HashSet/Dictionary from the reverse-pinvoke faulted the runtime on spawn.
        private const int SpawnRadiusFilterCap = 128;
        private static int[] spawnRadiusInsectFilterIds = new int[SpawnRadiusFilterCap];
        private static int[] spawnRadiusBirdFilterIds = new int[SpawnRadiusFilterCap];
        private static int spawnRadiusInsectFilterCount;
        private static int spawnRadiusBirdFilterCount;
        private static int spawnRadiusSpeciesEpoch = -1;
        private const int SpawnRadiusSpeciesCap = 512;
        private static readonly uint[] spawnRadiusSpeciesNetIds = new uint[SpawnRadiusSpeciesCap];
        private static readonly int[] spawnRadiusSpeciesIds = new int[SpawnRadiusSpeciesCap];
        private static int spawnRadiusSpeciesCount;
        private const int SpawnRadiusPendingCap = 24;
        private const int SpawnRadiusPendingDrainPerFrame = 4;
        private static readonly uint[] spawnRadiusPendingInsect = new uint[SpawnRadiusPendingCap];
        private static readonly uint[] spawnRadiusPendingBird = new uint[SpawnRadiusPendingCap];
        private static readonly uint[] spawnRadiusPendingScratch = new uint[SpawnRadiusPendingCap];
        private static int spawnRadiusPendingInsectCount;
        private static int spawnRadiusPendingBirdCount;
        // One InsectFly per netId, fired from the loaded-insect scan, never from the detour.
        // Independent of InsectNetFarm scan range: with scan at 200 m the old "farther than scan"
        // gate never fired for map markers at 40-60 m, so habitat-snapped insects stayed remote.
        private const float SpawnRadiusInsectPullNearMeters = 12f;
        private const float SpawnRadiusInsectPullInterval = 1f;
        private const int SpawnRadiusInsectPullsPerScan = 8;
        private static float spawnRadiusInsectPullAt;
        private static readonly HashSet<uint> spawnRadiusInsectPulled = new HashSet<uint>();

        private static readonly string InsectProtocolType =
            "XDTDataAndProtocol.ProtocolService.Insect.InsectProtocolManager";
        private static readonly string BirdProtocolType =
            "XDTDataAndProtocol.ProtocolService.GamePlay.Bird.BirdProtocolManager";

        private const int SpawnRadiusInsectSlotCount = 4;
        private const int SpawnRadiusBirdSlotCount = 3;
        private const int SpawnRadiusSlotCount = SpawnRadiusInsectSlotCount + SpawnRadiusBirdSlotCount;

        private static readonly string[] SpawnRadiusMethodNames =
        {
            "SpawnInsect",
            "InsectFly",
            "InsectMove",
            "InsectJump",
            "SpawnBird",
            "BirdMove",
            "ReportBirdPosToServer",
        };

        private static readonly int[] SpawnRadiusMethodParamCounts =
        {
            4, 5, 4, 4, 4, 5, 3,
        };

        private struct SpawnRadiusDetourSlot
        {
            public NativeDetour Detour;
            public bool InstallTried;
            public bool MissingLogged;
        }

        private static readonly SpawnRadiusDetourSlot[] spawnRadiusSlots =
            new SpawnRadiusDetourSlot[SpawnRadiusSlotCount];

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SpawnRadiusCompileMethodDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SpawnRadiusSpawnHookDelegate(uint netId, int staticId, ulong stayPointNetId, IntPtr positionPtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SpawnRadiusInsectFlyHookDelegate(
            uint netId, IntPtr tarPos, ulong stayPointNetId, float time, int moveState);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SpawnRadiusInsectMoveHookDelegate(uint netId, IntPtr tarPos, ulong stayPointNetId, float time);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // forceFly is a 4-byte Win64 bool. A 1-byte slot shifts the stack and the trampoline
        // re-enters Mono with a smashed frame (same crash class as the cell-size detour).
        private delegate void SpawnRadiusBirdMoveHookDelegate(
            uint netId, float needTime, IntPtr target, int moveState, int forceFly);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SpawnRadiusReportBirdPosHookDelegate(uint netId, IntPtr pos, ulong pointUId);

        private static SpawnRadiusSpawnHookDelegate spawnRadiusSpawnInsectKeepAlive;
        private static SpawnRadiusSpawnHookDelegate spawnRadiusSpawnInsectTrampoline;
        private static SpawnRadiusInsectFlyHookDelegate spawnRadiusInsectFlyKeepAlive;
        private static SpawnRadiusInsectFlyHookDelegate spawnRadiusInsectFlyTrampoline;
        private static IntPtr spawnRadiusInsectFlyMethod;
        private static SpawnRadiusInsectMoveHookDelegate spawnRadiusInsectMoveKeepAlive;
        private static SpawnRadiusInsectMoveHookDelegate spawnRadiusInsectMoveTrampoline;
        private static SpawnRadiusInsectMoveHookDelegate spawnRadiusInsectJumpKeepAlive;
        private static SpawnRadiusInsectMoveHookDelegate spawnRadiusInsectJumpTrampoline;
        private static SpawnRadiusSpawnHookDelegate spawnRadiusSpawnBirdKeepAlive;
        private static SpawnRadiusSpawnHookDelegate spawnRadiusSpawnBirdTrampoline;
        private static SpawnRadiusBirdMoveHookDelegate spawnRadiusBirdMoveKeepAlive;
        private static SpawnRadiusBirdMoveHookDelegate spawnRadiusBirdMoveTrampoline;
        private static SpawnRadiusReportBirdPosHookDelegate spawnRadiusReportBirdPosKeepAlive;
        private static SpawnRadiusReportBirdPosHookDelegate spawnRadiusReportBirdPosTrampoline;

        private bool spawnRadiusDetourWorldReadyRegistered;
        private bool spawnRadiusDetourInstallPassComplete;

        private void EnsureSpawnRadiusDetourWorldReadyRegistered()
        {
            if (this.spawnRadiusDetourWorldReadyRegistered)
            {
                return;
            }

            this.spawnRadiusDetourWorldReadyRegistered = true;
            this.RegisterWorldReadyCallback(SpawnRadiusDetourTag, this.TrySpawnRadiusDetourWorldReadyInstall);
        }

        private bool TrySpawnRadiusDetourWorldReadyInstall()
        {
            if (this.spawnRadiusDetourInstallPassComplete)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr insectClass = this.ResolveSpawnRadiusInsectClass();
                IntPtr birdClass = this.ResolveSpawnRadiusBirdClass();

                bool anyPending = false;
                for (int i = 0; i < SpawnRadiusSlotCount; i++)
                {
                    if (spawnRadiusSlots[i].Detour != null || spawnRadiusSlots[i].InstallTried)
                    {
                        continue;
                    }

                    bool isInsect = i < SpawnRadiusInsectSlotCount;
                    IntPtr cls = isInsect ? insectClass : birdClass;
                    if (cls == IntPtr.Zero)
                    {
                        anyPending = true;
                        continue;
                    }

                    if (!this.TryInstallSpawnRadiusDetourSlot(i, cls))
                    {
                        if (!spawnRadiusSlots[i].InstallTried)
                        {
                            anyPending = true;
                        }
                    }
                }

                if (!anyPending)
                {
                    this.spawnRadiusDetourInstallPassComplete = true;
                    ModLogger.Msg("[" + SpawnRadiusDetourTag + "] install pass complete.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[" + SpawnRadiusDetourTag + "] world-ready install failed: " + ex.Message);
            }

            return this.spawnRadiusDetourInstallPassComplete;
        }

        private IntPtr ResolveSpawnRadiusInsectClass()
        {
            IntPtr cls = this.FindAuraMonoClassByFullName(InsectProtocolType);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassAcrossLoadedAssemblies(
                "XDTDataAndProtocol.ProtocolService.Insect",
                "InsectProtocolManager");
        }

        private IntPtr ResolveSpawnRadiusBirdClass()
        {
            IntPtr cls = this.FindAuraMonoClassByFullName(BirdProtocolType);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassAcrossLoadedAssemblies(
                "XDTDataAndProtocol.ProtocolService.GamePlay.Bird",
                "BirdProtocolManager");
        }

        private bool TryInstallSpawnRadiusDetourSlot(int slot, IntPtr cls)
        {
            ref SpawnRadiusDetourSlot state = ref spawnRadiusSlots[slot];
            if (state.Detour != null || state.InstallTried)
            {
                return true;
            }

            string methodName = SpawnRadiusMethodNames[slot];
            int paramCount = SpawnRadiusMethodParamCounts[slot];
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, methodName, paramCount);
            if (method == IntPtr.Zero)
            {
                state.InstallTried = true;
                if (!state.MissingLogged)
                {
                    state.MissingLogged = true;
                    ModLogger.Msg("[" + SpawnRadiusDetourTag + "] " + methodName + "(" + paramCount + ") not found");
                }

                return true;
            }

            IntPtr nativePtr = this.CompileSpawnRadiusDetourTarget(method);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                switch (slot)
                {
                    case 0:
                        spawnRadiusSpawnInsectKeepAlive = SpawnRadiusSpawnInsectHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusSpawnInsectKeepAlive);
                        spawnRadiusSpawnInsectTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusSpawnHookDelegate>();
                        if (spawnRadiusSpawnInsectTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 1:
                        spawnRadiusInsectFlyMethod = method;
                        spawnRadiusInsectFlyKeepAlive = SpawnRadiusInsectFlyHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusInsectFlyKeepAlive);
                        spawnRadiusInsectFlyTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusInsectFlyHookDelegate>();
                        if (spawnRadiusInsectFlyTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 2:
                        spawnRadiusInsectMoveKeepAlive = SpawnRadiusInsectMoveHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusInsectMoveKeepAlive);
                        spawnRadiusInsectMoveTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusInsectMoveHookDelegate>();
                        if (spawnRadiusInsectMoveTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 3:
                        spawnRadiusInsectJumpKeepAlive = SpawnRadiusInsectJumpHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusInsectJumpKeepAlive);
                        spawnRadiusInsectJumpTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusInsectMoveHookDelegate>();
                        if (spawnRadiusInsectJumpTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 4:
                        spawnRadiusSpawnBirdKeepAlive = SpawnRadiusSpawnBirdHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusSpawnBirdKeepAlive);
                        spawnRadiusSpawnBirdTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusSpawnHookDelegate>();
                        if (spawnRadiusSpawnBirdTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 5:
                        spawnRadiusBirdMoveKeepAlive = SpawnRadiusBirdMoveHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusBirdMoveKeepAlive);
                        spawnRadiusBirdMoveTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusBirdMoveHookDelegate>();
                        if (spawnRadiusBirdMoveTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                    case 6:
                        spawnRadiusReportBirdPosKeepAlive = SpawnRadiusReportBirdPosHook;
                        state.Detour = new NativeDetour(nativePtr, spawnRadiusReportBirdPosKeepAlive);
                        spawnRadiusReportBirdPosTrampoline =
                            state.Detour.GenerateTrampoline<SpawnRadiusReportBirdPosHookDelegate>();
                        if (spawnRadiusReportBirdPosTrampoline == null)
                        {
                            this.RevertSpawnRadiusSlotInstall(ref state);
                            return true;
                        }
                        break;
                }

                state.InstallTried = true;
                ModLogger.Msg("[" + SpawnRadiusDetourTag + "] hooked " + methodName
                    + " @0x" + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                state.InstallTried = true;
                ModLogger.Msg("[" + SpawnRadiusDetourTag + "] install " + methodName + " failed: " + ex.Message);
                return true;
            }
        }

        private void RevertSpawnRadiusSlotInstall(ref SpawnRadiusDetourSlot slot)
        {
            try
            {
                if (slot.Detour != null)
                {
                    slot.Detour.Undo();
                }
            }
            catch
            {
            }

            slot.Detour = null;
            slot.InstallTried = true;
            if (!slot.MissingLogged)
            {
                slot.MissingLogged = true;
                ModLogger.Msg("[" + SpawnRadiusDetourTag + "] trampoline unavailable — detour reverted");
            }
        }

        private IntPtr CompileSpawnRadiusDetourTarget(IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            SpawnRadiusCompileMethodDelegate compile = monoModule != IntPtr.Zero
                ? this.GetAuraMonoExport<SpawnRadiusCompileMethodDelegate>(monoModule, "mono_compile_method")
                : null;
            if (compile == null)
            {
                return IntPtr.Zero;
            }

            return compile(method);
        }

        private void NoteSpawnRadiusPlayerAnchor()
        {
            this.NoteSpawnRadiusSpeciesFilter();
            bool insectOn = InsectNetFarm.GetSpawnNearPlayerEnabled() && InsectNetFarm.IsEnabled;
            bool birdOn = BirdNetFarm.GetSpawnNearPlayerEnabled() && BirdNetFarm.IsEnabled;
            spawnRadiusInsectArmed = insectOn;
            spawnRadiusBirdArmed = birdOn;
            if (!insectOn && !birdOn)
            {
                spawnRadiusAnchorValid = false;
                return;
            }

            Vector3 anchor;
            Vector3 forward;
            if (this.TryGetNoclipSelfAnchorPose(out anchor, out Quaternion selfRot, out bool hasSelfRot, out _))
            {
                forward = hasSelfRot ? selfRot * Vector3.forward : Vector3.forward;
                if (!hasSelfRot)
                {
                    GameObject skeleton = this.GetVerifiedLocalPlayerSkeleton();
                    if (skeleton != null)
                    {
                        forward = skeleton.transform.forward;
                    }
                }
            }
            else
            {
                GameObject skeleton = this.GetVerifiedLocalPlayerSkeleton();
                if (skeleton == null)
                {
                    spawnRadiusAnchorValid = false;
                    return;
                }

                anchor = skeleton.transform.position;
                forward = skeleton.transform.forward;
            }

            if (!IsFiniteVector(anchor) || anchor == Vector3.zero)
            {
                spawnRadiusAnchorValid = false;
                return;
            }

            Vector3 place = anchor + forward * SpawnRadiusAheadMeters;
            place.y = anchor.y;
            spawnRadiusPlaceX = place.x;
            spawnRadiusPlaceY = place.y;
            spawnRadiusPlaceZ = place.z;
            spawnRadiusAnchorValid = true;
            if (insectOn)
            {
                this.PullFarInsectsOnce(anchor);
            }
        }

        // Same census the insect farm uses (Entities.GetComponents<InsectComponent>, then the radar
        // species filter). Anything farther than SpawnRadiusInsectPullNearMeters gets one InsectFly
        // to the cached point with a zero habitat id, so GetTargetPos keeps the vector (spawn rewrite
        // alone is not enough — the view snaps onto the habitat level object).
        private void PullFarInsectsOnce(Vector3 anchor)
        {
            float now = Time.unscaledTime;
            if (now < spawnRadiusInsectPullAt || !this.IsWorldReady)
            {
                return;
            }

            spawnRadiusInsectPullAt = now + SpawnRadiusInsectPullInterval;
            if (spawnRadiusInsectFlyMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || !this.AttachAuraMonoThread())
            {
                return;
            }

            List<uint> ids;
            List<Vector3> positions;
            if (!this.TryGetLoadedInsectTargets(out int _, out ids, out positions, out string _)
                || ids == null
                || positions == null)
            {
                return;
            }

            float nearSqr = SpawnRadiusInsectPullNearMeters * SpawnRadiusInsectPullNearMeters;
            int count = ids.Count < positions.Count ? ids.Count : positions.Count;
            int sent = 0;
            for (int i = 0; i < count && sent < SpawnRadiusInsectPullsPerScan; i++)
            {
                uint netId = ids[i];
                if (netId == 0U || spawnRadiusInsectPulled.Contains(netId))
                {
                    continue;
                }

                Vector3 pos = positions[i];
                float dx = pos.x - anchor.x;
                float dy = pos.y - anchor.y;
                float dz = pos.z - anchor.z;
                if (dx * dx + dy * dy + dz * dz <= nearSqr)
                {
                    continue;
                }

                if (!this.TryPullInsectToPlace(netId))
                {
                    continue;
                }

                spawnRadiusInsectPulled.Add(netId);
                sent++;
            }
        }

        private unsafe bool TryPullInsectToPlace(uint netId)
        {
            if (spawnRadiusInsectFlyMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || !spawnRadiusAnchorValid)
            {
                return false;
            }

            // Runtime invoke, not the detour trampoline. A raw trampoline call from OnUpdate enters
            // the Mono JIT with no runtime frame and kills the process before a dump is written.
            // args[1] points at a real 12-byte Vector3; a pointer-sized slot is the stack-smash case.
            uint id = netId;
            Vector3 place = new Vector3(spawnRadiusPlaceX, spawnRadiusPlaceY, spawnRadiusPlaceZ);
            ulong pointId = 0UL;
            float flyTime = 1.2f;
            int moveState = 1; // InsectMoveState.FlyTo
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[5];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&place);
            args[2] = (IntPtr)(&pointId);
            args[3] = (IntPtr)(&flyTime);
            args[4] = (IntPtr)(&moveState);
            Breadcrumbs.Drop("SpawnRadius.pull", "net=" + id);
            auraMonoRuntimeInvoke(spawnRadiusInsectFlyMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private void NoteSpawnRadiusSpeciesFilter()
        {
            int epoch = AuraMonoWorldEpoch;
            if (epoch != spawnRadiusSpeciesEpoch)
            {
                spawnRadiusSpeciesEpoch = epoch;
                spawnRadiusSpeciesCount = 0;
                spawnRadiusPendingInsectCount = 0;
                spawnRadiusPendingBirdCount = 0;
                spawnRadiusInsectPulled.Clear();
            }

            CopySpawnRadiusFilter(this.radarInsectSpeciesFilter, ref spawnRadiusInsectFilterIds, ref spawnRadiusInsectFilterCount);
            CopySpawnRadiusFilter(this.radarBirdSpeciesFilter, ref spawnRadiusBirdFilterIds, ref spawnRadiusBirdFilterCount);
            if (InsectNetFarm.GetSpawnNearPlayerEnabled() && spawnRadiusInsectFilterCount > 0)
            {
                this.DrainSpawnRadiusPending(true);
            }

            if (BirdNetFarm.GetSpawnNearPlayerEnabled() && spawnRadiusBirdFilterCount > 0)
            {
                this.DrainSpawnRadiusPending(false);
            }
        }

        private static void CopySpawnRadiusFilter(HashSet<int> source, ref int[] dest, ref int count)
        {
            if (source == null || source.Count == 0)
            {
                count = 0;
                return;
            }

            int n = source.Count;
            if (dest == null || n > dest.Length)
            {
                dest = new int[n];
            }

            int i = 0;
            foreach (int id in source)
            {
                dest[i++] = id;
            }

            count = i;
        }

        // Move publishers carry only a netId. Spawn recorded the species; anything already in the
        // world is resolved here, a few per frame, through the entity-object GetEntityResId path.
        private void DrainSpawnRadiusPending(bool insect)
        {
            int count = insect ? spawnRadiusPendingInsectCount : spawnRadiusPendingBirdCount;
            if (count <= 0)
            {
                return;
            }

            uint[] src = insect ? spawnRadiusPendingInsect : spawnRadiusPendingBird;
            int take = count < SpawnRadiusPendingDrainPerFrame ? count : SpawnRadiusPendingDrainPerFrame;
            for (int i = 0; i < take; i++)
            {
                spawnRadiusPendingScratch[i] = src[i];
            }

            int rest = count - take;
            for (int i = 0; i < rest; i++)
            {
                src[i] = src[take + i];
            }

            if (insect)
            {
                spawnRadiusPendingInsectCount = rest;
            }
            else
            {
                spawnRadiusPendingBirdCount = rest;
            }

            this.EnsureEntityResIdMethods();
            for (int i = 0; i < take; i++)
            {
                int staticId;
                if (this.TryResolveSpawnRadiusStaticId(spawnRadiusPendingScratch[i], out staticId))
                {
                    SpawnRadiusRememberSpecies(spawnRadiusPendingScratch[i], staticId);
                }
            }
        }

        private bool TryResolveSpawnRadiusStaticId(uint netId, out int staticId)
        {
            staticId = 0;
            IntPtr entityObj;
            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(entityObj);
            if (pin == 0U)
            {
                return false;
            }

            try
            {
                return this.TryGetCollectableStaticIdViaAura(entityObj, out staticId) && staticId > 0;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // 12-byte write into the caller's Vector3 copy. No Unity, no Mono, no allocation.
        private static unsafe void SpawnRadiusMaybeRewritePosition(IntPtr positionPtr)
        {
            if (!spawnRadiusAnchorValid || positionPtr == IntPtr.Zero)
            {
                return;
            }

            float* v = (float*)positionPtr;
            v[0] = spawnRadiusPlaceX;
            v[1] = spawnRadiusPlaceY;
            v[2] = spawnRadiusPlaceZ;
        }

        // Empty filter matches the radar: every species. A positive staticId is remembered so a
        // later move, which has no species argument, can be checked without touching game Mono.
        private static bool SpawnRadiusAllowRewrite(bool insect, uint netId, int staticId)
        {
            if (staticId > 0)
            {
                SpawnRadiusRememberSpecies(netId, staticId);
            }

            int[] filterIds = insect ? spawnRadiusInsectFilterIds : spawnRadiusBirdFilterIds;
            int filterCount = insect ? spawnRadiusInsectFilterCount : spawnRadiusBirdFilterCount;
            if (filterCount == 0)
            {
                return true;
            }

            if (staticId <= 0 && !SpawnRadiusTryGetSpecies(netId, out staticId))
            {
                SpawnRadiusNotePending(insect, netId);
                return false;
            }

            return staticId > 0 && SpawnRadiusFilterContains(filterIds, filterCount, staticId);
        }

        private static bool SpawnRadiusFilterContains(int[] ids, int count, int staticId)
        {
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (ids[i] == staticId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SpawnRadiusTryGetSpecies(uint netId, out int staticId)
        {
            int count = spawnRadiusSpeciesCount;
            for (int i = 0; i < count; i++)
            {
                if (spawnRadiusSpeciesNetIds[i] == netId)
                {
                    staticId = spawnRadiusSpeciesIds[i];
                    return true;
                }
            }

            staticId = 0;
            return false;
        }

        private static void SpawnRadiusRememberSpecies(uint netId, int staticId)
        {
            if (netId == 0U || staticId <= 0)
            {
                return;
            }

            int count = spawnRadiusSpeciesCount;
            for (int i = 0; i < count; i++)
            {
                if (spawnRadiusSpeciesNetIds[i] != netId)
                {
                    continue;
                }

                spawnRadiusSpeciesIds[i] = staticId;
                return;
            }

            if (count >= SpawnRadiusSpeciesCap)
            {
                return;
            }

            spawnRadiusSpeciesNetIds[count] = netId;
            spawnRadiusSpeciesIds[count] = staticId;
            spawnRadiusSpeciesCount = count + 1;
        }

        private static void SpawnRadiusNotePending(bool insect, uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            uint[] buf = insect ? spawnRadiusPendingInsect : spawnRadiusPendingBird;
            int count = insect ? spawnRadiusPendingInsectCount : spawnRadiusPendingBirdCount;
            for (int i = 0; i < count; i++)
            {
                if (buf[i] == netId)
                {
                    return;
                }
            }

            if (count >= SpawnRadiusPendingCap)
            {
                return;
            }

            buf[count] = netId;
            if (insect)
            {
                spawnRadiusPendingInsectCount = count + 1;
            }
            else
            {
                spawnRadiusPendingBirdCount = count + 1;
            }
        }

        private static void SpawnRadiusSpawnInsectHook(uint netId, int staticId, ulong stayPointNetId, IntPtr positionPtr)
        {
            // InsectComponent.OnSpawned ignores the vector when startPointNetId resolves to a
            // level object and snaps the body onto that habitat point.
            if (spawnRadiusInsectArmed && SpawnRadiusAllowRewrite(true, netId, staticId))
            {
                SpawnRadiusMaybeRewritePosition(positionPtr);
                stayPointNetId = 0UL;
            }

            SpawnRadiusSpawnHookDelegate orig = spawnRadiusSpawnInsectTrampoline;
            if (orig != null)
            {
                orig(netId, staticId, stayPointNetId, positionPtr);
            }
        }

        private static void SpawnRadiusInsectFlyHook(
            uint netId, IntPtr tarPos, ulong stayPointNetId, float time, int moveState)
        {
            if (spawnRadiusInsectArmed && SpawnRadiusAllowRewrite(true, netId, 0))
            {
                SpawnRadiusMaybeRewritePosition(tarPos);
                stayPointNetId = 0UL;
            }

            SpawnRadiusInsectFlyHookDelegate orig = spawnRadiusInsectFlyTrampoline;
            if (orig != null)
            {
                orig(netId, tarPos, stayPointNetId, time, moveState);
            }
        }

        private static void SpawnRadiusInsectMoveHook(uint netId, IntPtr tarPos, ulong stayPointNetId, float time)
        {
            if (spawnRadiusInsectArmed && SpawnRadiusAllowRewrite(true, netId, 0))
            {
                SpawnRadiusMaybeRewritePosition(tarPos);
                stayPointNetId = 0UL;
            }

            SpawnRadiusInsectMoveHookDelegate orig = spawnRadiusInsectMoveTrampoline;
            if (orig != null)
            {
                orig(netId, tarPos, stayPointNetId, time);
            }
        }

        private static void SpawnRadiusInsectJumpHook(uint netId, IntPtr tarPos, ulong stayPointNetId, float time)
        {
            if (spawnRadiusInsectArmed && SpawnRadiusAllowRewrite(true, netId, 0))
            {
                SpawnRadiusMaybeRewritePosition(tarPos);
                stayPointNetId = 0UL;
            }

            SpawnRadiusInsectMoveHookDelegate orig = spawnRadiusInsectJumpTrampoline;
            if (orig != null)
            {
                orig(netId, tarPos, stayPointNetId, time);
            }
        }

        private static void SpawnRadiusSpawnBirdHook(uint netId, int staticId, ulong stayPointNetId, IntPtr positionPtr)
        {
            if (spawnRadiusBirdArmed && SpawnRadiusAllowRewrite(false, netId, staticId))
            {
                SpawnRadiusMaybeRewritePosition(positionPtr);
            }

            SpawnRadiusSpawnHookDelegate orig = spawnRadiusSpawnBirdTrampoline;
            if (orig != null)
            {
                orig(netId, staticId, stayPointNetId, positionPtr);
            }
        }

        private static void SpawnRadiusBirdMoveHook(
            uint netId, float needTime, IntPtr target, int moveState, int forceFly)
        {
            if (spawnRadiusBirdArmed && SpawnRadiusAllowRewrite(false, netId, 0))
            {
                SpawnRadiusMaybeRewritePosition(target);
            }

            SpawnRadiusBirdMoveHookDelegate orig = spawnRadiusBirdMoveTrampoline;
            if (orig != null)
            {
                orig(netId, needTime, target, moveState, forceFly);
            }
        }

        private static void SpawnRadiusReportBirdPosHook(uint netId, IntPtr pos, ulong pointUId)
        {
            if (spawnRadiusBirdArmed && SpawnRadiusAllowRewrite(false, netId, 0))
            {
                SpawnRadiusMaybeRewritePosition(pos);
            }

            SpawnRadiusReportBirdPosHookDelegate orig = spawnRadiusReportBirdPosTrampoline;
            if (orig != null)
            {
                orig(netId, pos, pointUId);
            }
        }
    }
}