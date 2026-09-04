using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void AutoFishLog(string message)
        {
            if (!MasterLogAutoFish || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[AutoFishing] " + message);
        }

        public bool TryGetFishingRodToolStatus(out bool rodEquipped, out string status)
        {
            rodEquipped = false;
            status = "Unknown";
            string monoTentativeStatus = string.Empty;

            try
            {
                if (this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
                {
                    IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                    if (interactObj != IntPtr.Zero && this.auraMonoInteractGetPlayerMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr monoPlayerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && monoPlayerObj != IntPtr.Zero)
                        {
                            if (this.TryInvokeAuraMonoZeroArg(monoPlayerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") && equipObj != IntPtr.Zero)
                            {
                                if (this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") && handholdObj != IntPtr.Zero)
                                {
                                    IntPtr handholdClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(handholdObj) : IntPtr.Zero;
                                    string handholdClassName = this.GetAuraMonoClassDisplayName(handholdClass);
                                    bool looksLikeFishingRod = !string.IsNullOrEmpty(handholdClassName)
                                        && (handholdClassName.IndexOf("FishingRod", StringComparison.OrdinalIgnoreCase) >= 0
                                            || (handholdClassName.IndexOf("Fishing", StringComparison.OrdinalIgnoreCase) >= 0
                                                && handholdClassName.IndexOf("Rod", StringComparison.OrdinalIgnoreCase) >= 0)
                                            || handholdClassName.IndexOf("HandHoldFishingRod", StringComparison.OrdinalIgnoreCase) >= 0);

                                    if (this.TryReadAuraMonoObjectField(handholdObj, out IntPtr monoFloatObj, "_float", "floatComponent", "_floatComponent", "_targetFXProxy", "_invalidTargetFXProxy")
                                        && monoFloatObj != IntPtr.Zero)
                                    {
                                        rodEquipped = true;
                                        status = "Fishing Rod Equipped";
                                        this.AutoFishLog("Rod resolver: mono handhold resolved via float field.");
                                        return true;
                                    }

                                    if (looksLikeFishingRod)
                                    {
                                        rodEquipped = true;
                                        status = "Fishing Rod Equipped";
                                        this.AutoFishLog("Rod resolver: mono handhold resolved by type name " + handholdClassName);
                                        return true;
                                    }

                                    monoTentativeStatus = "Holding Other Tool";
                                }
                                else
                                {
                                    status = "No Tool Equipped";
                                    return true;
                                }
                            }
                            else
                            {
                                status = "No Tool Equipped";
                                return true;
                            }
                        }
                    }
                }

                // The managed interact-system / self-player lookups that used to sit here resolved
                // XDT* types through FindLoadedType and can never succeed on this build, so every
                // branch below them was unreachable. That is exactly the wedge described above: the
                // resolver answered "cannot determine" whenever another tool was in hand, and
                // AutoFishingFarm.Update retried forever without reaching the equip call. The Mono
                // verdict resolved above is therefore the whole answer.
                if (!string.IsNullOrEmpty(monoTentativeStatus))
                {
                    rodEquipped = false;
                    status = monoTentativeStatus;
                    this.AutoFishLog("Rod resolver: mono verdict " + status);
                    return true;
                }

                status = "Player Unavailable";
                this.AutoFishLog("Rod resolver failed: " + status);
                return false;
            }
            catch (Exception ex)
            {
                status = "Exception: " + ex.Message;
                this.AutoFishLog("Rod resolver exception: " + ex.Message);
                return false;
            }
        }



        // Just-caught-fish ghost avoidance: right after a catch the caught fish's shadow GameObject
        // lingers a moment at the catch spot before despawning, and the scan would re-target it (empty
        // water). We skip EXACTLY that object by its Unity instance id for a short window — so other
        // fish in the same school are NOT excluded (a radius/position block would stall school farming).
        // AutoFishingFarm stamps the caught object's id (= the last scan's winner) + a window here.
        internal static int fishScanGhostInstanceId;
        internal static float fishScanGhostUntil = -999f;
        private int lastFishShadowTargetInstanceId;
        public int GetLastFishShadowTargetInstanceId() => this.lastFishShadowTargetInstanceId;

        public bool TryFindNearestFishShadowTarget(float scanRange, out uint netId, out Vector3 position, out float distance, out int detectedCount, out int inRangeCount, out string status)
        {
            netId = 0U;
            position = Vector3.zero;
            distance = 0f;
            detectedCount = 0;
            inRangeCount = 0;
            status = "No active fish shadows";

            try
            {
                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "Player position unavailable";
                    return false;
                }

                // Score/jitter from the skeleton, not transform.root: on the sea-fishing ship the
                // player is parented under the boat, so the root's position/forward are the ship's.
                GameObject playerSkeleton = HeartopiaComplete.GetLocalPlayer();
                GameObject playerRoot = playerSkeleton != null ? playerSkeleton : this.FindPlayerRoot();
                Transform playerTransform = playerRoot != null ? playerRoot.transform : null;
                Camera mainCamera = Camera.main;
                GameObject[] candidates = this.GetCachedFishShadowTargetObjects();

                // Live fish-state snapshot via AuraMono GetComponents<FishComponent> (primary).
                // Supplies each shadow's REAL state (netId/shadowState/floatNetId/playerNetId/
                // fishResId/targetPos) and identifies PHANTOM shadows: a just-caught fish's
                // GameObject lingers ~1 scan with no live fish entity behind it — casting at it
                // lands in empty water. When the snapshot is unavailable (class unresolved, infra
                // not ready, pinning unavailable, empty/failed query) the scan DEGRADES to the
                // legacy name-based behavior below — fishing must never break on this path.
                bool auraSnapshotReady = candidates.Length > 0
                    && this.TryBuildFishShadowAuraSnapshot(this.fishShadowAuraSnapshot);

                float bestDistance = float.MaxValue;
                float bestScore = float.MaxValue;
                string bestName = string.Empty;
                int bestPriority = 0;
                int bestFishId = 0;
                int bestInstanceId = 0;
                string bestPrioritySource = string.Empty;
                string bestOccupancy = string.Empty;
                for (int i = 0; i < candidates.Length; i++)
                {
                    GameObject candidate = candidates[i];
                    if (candidate == null || !candidate.activeInHierarchy)
                    {
                        continue;
                    }

                    detectedCount++;

                    Vector3 livePos = candidate.transform.position;
                    // Cylinder check: horizontal (XZ) distance only, ignore Y — the fish/player height
                    // gap shouldn't shrink the reachable radius (e.g. on a boat or raised ground).
                    float liveDistance = new Vector2(livePos.x - playerPos.x, livePos.z - playerPos.z).magnitude;
                    if (scanRange > 0f && liveDistance > scanRange)
                    {
                        continue;
                    }

                    // Raw count of fish shadows within the radius, regardless of occupancy — used by
                    // the auto-bait "no fish nearby" gate (a fish being battled still counts as present).
                    inRangeCount++;

                    uint occupiedBuoyNetId = 0U;
                    uint occupiedPlayerNetId = 0U;
                    string occupiedState = string.Empty;
                    Vector3 moveTargetPos = Vector3.zero;
                    uint snapshotNetId = 0U;
                    int snapshotFishResId = 0;
                    if (auraSnapshotReady)
                    {
                        // Snapshot primary: no live FishComponent entity at this shadow's XZ means
                        // the GameObject is a lingering phantom (just-caught fish) — never cast at it.
                        if (!this.TryMatchFishShadowAuraSnapshotEntry(this.fishShadowAuraSnapshot, livePos, out FishShadowAuraFishEntry snapshotEntry))
                        {
                            this.AutoFishLog($"Fish shadow scan: no live fish at ({livePos.x:F1},{livePos.z:F1}) -> phantom skipped (obj={candidate.name})");
                            continue;
                        }

                        occupiedBuoyNetId = snapshotEntry.FloatNetId;
                        occupiedPlayerNetId = snapshotEntry.PlayerNetId;
                        occupiedState = GetFishShadowAiStateName(snapshotEntry.ShadowState);
                        moveTargetPos = snapshotEntry.TargetPos;
                        snapshotNetId = snapshotEntry.NetId;
                        snapshotFishResId = snapshotEntry.FishResId;
                        this.AutoFishLog($"Fish shadow scan: matched netId={snapshotNetId} state={occupiedState} fishResId={snapshotFishResId} obj={candidate.name}");
                    }
                    else
                    {
                        // Degrade path: legacy interop component walk. FishComponent is an
                        // entity-system ViewComponent (not a Unity Component), so on this build the
                        // walk finds no state — identical to the pre-snapshot behavior.
                        this.TryGetFishShadowOccupancy(candidate, out occupiedBuoyNetId, out occupiedPlayerNetId, out occupiedState, out moveTargetPos);
                    }

                    // Occupied: attracted to someone's buoy (FindBuoyWaiting/AttemptForward carry
                    // floatNetId) or already being reeled (Battle carries playerNetId).
                    if (occupiedBuoyNetId != 0U
                        || occupiedPlayerNetId != 0U
                        || string.Equals(occupiedState, "Battle", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(occupiedState, "FindBuoyWaiting", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(occupiedState, "AttemptForward", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Fleeing fish (someone's failed catch) is moving fast and will be gone before the
                    // buoy lands — never target it. Succeed = already caught (despawning).
                    if (string.Equals(occupiedState, "Escape", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(occupiedState, "RunAway", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(occupiedState, "Succeed", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Just-caught ghost: skip ONLY the exact fish object we just reeled in, until it
                    // despawns (window). School-mates keep their own instance ids, so they're unaffected.
                    if (Time.unscaledTime < fishScanGhostUntil && candidate.GetInstanceID() == fishScanGhostInstanceId)
                    {
                        continue;
                    }

                    // Lead-aim: an IdleMove fish travels a server bezier ending at ComponentData.targetPos.
                    // Cast at the END of its path (re-based to the live height; the wire y is 0) so the
                    // buoy lands where the fish will be, not where it was at cast time.
                    Vector3 candidatePos = livePos;
                    bool leadAim = false;
                    if (string.Equals(occupiedState, "IdleMove", StringComparison.OrdinalIgnoreCase)
                        && moveTargetPos != Vector3.zero)
                    {
                        Vector3 leadPos = new Vector3(moveTargetPos.x, livePos.y, moveTargetPos.z);
                        if (new Vector2(leadPos.x - livePos.x, leadPos.z - livePos.z).sqrMagnitude > 0.09f)
                        {
                            candidatePos = leadPos;
                            leadAim = true;
                        }
                    }

                    float candidateDistance = new Vector2(candidatePos.x - playerPos.x, candidatePos.z - playerPos.z).magnitude;
                    if (scanRange > 0f && candidateDistance > scanRange)
                    {
                        continue; // the lead point must be castable too
                    }

                    int candidatePriority = this.GetFishShadowVisualPriority(candidate, out int candidateFishId, out string candidatePrioritySource);
                    if (snapshotFishResId > 0)
                    {
                        // Exact fish id from live FishComponentData (rarity lookup no longer relies
                        // on the prefab-name heuristic alone).
                        candidateFishId = snapshotFishResId;
                    }

                    float candidateScore = this.GetFishShadowTargetScore(candidate, candidatePos, candidateDistance, playerTransform, mainCamera, candidatePriority)
                        + this.GetFishShadowCoopJitter(candidate, playerRoot);
                    if (candidateScore >= bestScore)
                    {
                        continue;
                    }

                    // netId: snapshot entity netId when matched (the GameObject resolve below never
                    // finds one on this build — FishComponent is not a Unity Component).
                    uint candidateNetId = snapshotNetId;
                    if (candidateNetId == 0U)
                    {
                        this.TryResolveNetIdFromGameObject(candidate, out candidateNetId, out _);
                    }
                    bestScore = candidateScore;
                    bestDistance = candidateDistance;
                    bestName = candidate.name;
                    bestPriority = candidatePriority;
                    bestFishId = candidateFishId;
                    bestInstanceId = candidate.GetInstanceID();
                    bestPrioritySource = candidatePrioritySource;
                    bestOccupancy = leadAim ? occupiedState + " [lead +" + new Vector2(candidatePos.x - livePos.x, candidatePos.z - livePos.z).magnitude.ToString("F1") + "m]" : occupiedState;
                    netId = candidateNetId;
                    position = candidatePos;
                }

                if (position == Vector3.zero || bestDistance == float.MaxValue)
                {
                    status = detectedCount > 0
                        ? $"Fish shadows found but all beyond {scanRange:F0}m"
                        : "No active fish shadows";
                    this.LogFishShadowResolverMiss(status + $" detected={detectedCount}");
                    return false;
                }

                distance = bestDistance;
                this.lastFishShadowTargetInstanceId = bestInstanceId;
                status = $"Selected fish shadow {(netId != 0U ? "netId=" + netId + " " : string.Empty)}dist={bestDistance:F1}m";
                string priorityInfo = bestFishId > 0 ? " fishId=" + bestFishId : string.Empty;
                if (!string.IsNullOrEmpty(bestPrioritySource))
                {
                    priorityInfo += " source=" + bestPrioritySource;
                }

                this.AutoFishLog("Fish shadow resolver hit: " + status + " score=" + bestScore.ToString("F1") + " priority=" + bestPriority + priorityInfo + " state=" + bestOccupancy + " obj=" + bestName + " pos=" + position);
                return true;
            }
            catch (Exception ex)
            {
                status = "Fish shadow scan error: " + ex.Message;
                this.AutoFishLog("Fish shadow resolver exception: " + ex.Message);
                return false;
            }
        }

        private GameObject[] GetCachedFishShadowTargetObjects()
        {
            float now = Time.unscaledTime;
            if (this.cachedFishShadowTargetObjects != null && now < this.nextFishShadowTargetObjectScanAt)
            {
                return this.cachedFishShadowTargetObjects;
            }

            List<GameObject> candidates = new List<GameObject>(32);
            try
            {
                // Prefer the narrowed scan: enumerate only FishComponent gameObjects (a handful)
                // instead of every GameObject in the scene (thousands). Falls back to the full scan
                // when the FishComponent type can't be resolved on this build. The per-object
                // ShouldTrackFishShadowObject filter (prefab-name rarity, aquarium/decor exclusion)
                // is applied identically either way, so targeting behaviour is unchanged.
                GameObject[] sourceObjects = this.TryGetFishComponentShadowGameObjects()
                    ?? UnityEngine.Object.FindObjectsOfType<GameObject>();
                for (int i = 0; i < sourceObjects.Length; i++)
                {
                    GameObject obj = sourceObjects[i];
                    if (obj == null || !obj.activeInHierarchy)
                    {
                        continue;
                    }

                    if (this.ShouldTrackFishShadowObject(obj))
                    {
                        candidates.Add(obj);
                    }
                }
            }
            catch
            {
            }

            this.cachedFishShadowTargetObjects = candidates.ToArray();
            this.nextFishShadowTargetObjectScanAt = now + (this.cachedFishShadowTargetObjects.Length > 0 ? 0.35f : 0.9f);
            return this.cachedFishShadowTargetObjects;
        }

        // Returns the gameObjects of all live FishComponent instances, or null if the FishComponent
        // type can't be resolved / the typed scan fails (caller then does the full GameObject scan).
        private GameObject[] TryGetFishComponentShadowGameObjects()
        {
            if (!this.fishComponentIl2CppTypeResolved)
            {
                this.fishComponentIl2CppTypeResolved = true;
                try
                {
                    this.cachedFishComponentIl2CppType =
                        Il2CppType.GetType("XDTLevelAndEntity.Gameplay.Component.Fish.FishComponent")
                        ?? Il2CppType.GetType("XDTLevelAndEntity.Gameplay.Component.Fish.FishShadowResHandle");
                    this.AutoFishLog("FishComponent il2cpp type " + (this.cachedFishComponentIl2CppType != null ? "resolved" : "unavailable") + " for narrowed shadow scan.");
                }
                catch (Exception ex)
                {
                    this.cachedFishComponentIl2CppType = null;
                    this.AutoFishLog("FishComponent il2cpp type resolve failed: " + ex.Message);
                }
            }

            if (this.cachedFishComponentIl2CppType == null)
            {
                return null;
            }

            try
            {
                Il2CppReferenceArray<UnityObject> found = UnityObject.FindObjectsOfType(this.cachedFishComponentIl2CppType);
                if (found == null)
                {
                    return null;
                }

                List<GameObject> result = new List<GameObject>(found.Length);
                for (int i = 0; i < found.Length; i++)
                {
                    UnityObject o = found[i];
                    if (o == null)
                    {
                        continue;
                    }

                    Component component = o.TryCast<Component>();
                    GameObject go = component != null ? component.gameObject : null;
                    if (go != null)
                    {
                        result.Add(go);
                    }
                }

                return result.ToArray();
            }
            catch (Exception ex)
            {
                this.AutoFishLog("FishComponent narrowed shadow scan failed: " + ex.Message);
                return null;
            }
        }

        // ===== AuraMono live fish-state snapshot (phantom-shadow filter + real netId/state) =====
        //
        // FishComponent is NOT a Unity Component — it is an entity-system ViewComponent
        // (XDTLevelAndEntity.Gameplay.Component.Fish.FishComponent, held by Entity._components),
        // invisible to GameObject.GetComponents. The legacy interop walk in TryGetFishShadowOccupancy
        // therefore reads NO state on this build (occupancy always empty, netId always 0). This
        // snapshot enumerates live FishComponent instances via the game's own Entities.GetComponents<T>
        // (AuraMono inflate — TryAuraMonoGetComponentObjects, the insect/bubble/loot template) and
        // scalarizes every fish's state IMMEDIATELY: raw mono pointers live on the moving sgen GC, so
        // each pointer is pinned for exactly the reads it needs and nothing raw survives this method.
        //
        // Liveness = component `running` (ViewComponent lifecycle — false while spawning/despawning)
        // AND entity `!WillDie` (true during FadeOut/UnSpawn — the just-caught lingering window; the
        // shadow stays VISIBLE through FadeOut, so entity-exists alone is not a phantom test).
        // A shadow GameObject with no live snapshot entry at its XZ is a PHANTOM — skip, don't cast.
        //
        // Fail-closed rules:
        // - Any member-read failure on a component marks the snapshot unreliable and the WHOLE scan
        //   degrades to the legacy name-based behavior (read failures must never become "every
        //   shadow is a phantom" = broken fishing).
        // - State comes from the `_componentData` STRUCT FIELD read (mono_field_get_value_object
        //   returns a boxed COPY; field offsets on the box are header-relative and correct). NEVER
        //   invoke get_ComponentData (ref readonly return = unsafe under mono_runtime_invoke) and
        //   NEVER DataCenter.TryGetComponentData<FishComponentData> (generic over a struct = crash).
        private struct FishShadowAuraFishEntry
        {
            public uint NetId;
            public Vector3 Position;
            public int ShadowState;   // FishShadowAiState: 0 IdleDrift, 1 IdleMove, 2 FindBuoyWaiting, 3 AttemptForward, 4 Battle, 5 Escape, 6 Succeed, 7 RunAway
            public uint FloatNetId;
            public uint PlayerNetId;
            public int FishResId;
            public Vector3 TargetPos; // IdleMove bezier end point (y arrives 0 on the wire)
        }

        // Shadow GameObject <-> live fish entity match radius (XZ-only; the fish entity sits below
        // the water surface, so Y always differs). View and entity move together within a frame —
        // 0.75m absorbs hierarchy offsets without gluing school-mates together.
        private const float FishShadowAuraSnapshotMatchRadius = 0.75f;
        private IntPtr fishShadowAuraComponentClass; // mono class ptr (image lifetime — raw IntPtr ok)
        private float nextFishShadowAuraSnapshotRetryAt = -999f;
        private string lastFishShadowAuraSnapshotFailReason = string.Empty;
        private readonly List<FishShadowAuraFishEntry> fishShadowAuraSnapshot = new List<FishShadowAuraFishEntry>(16);

        private bool TryBuildFishShadowAuraSnapshot(List<FishShadowAuraFishEntry> snapshot)
        {
            snapshot.Clear();
            float now = Time.unscaledTime;
            if (now < this.nextFishShadowAuraSnapshotRetryAt)
            {
                return false; // recent failure — degrade quietly, retry later (throttled)
            }

            try
            {
                // Shared Entities.GetComponents infra gate (Entities class + generic inflate +
                // kill-switch) — deliberately NOT the farm-specific readiness (crop component
                // classes are irrelevant to fishing).
                if (!this.TryAuraMonoEntitiesGetComponentsInfraReady(out string infraStatus))
                {
                    this.NoteFishShadowAuraSnapshotFailure(now, 5f, "infra not ready: " + infraStatus);
                    return false;
                }

                // Fail closed when the gchandle exports are unresolved: an unpinned enumeration
                // would walk movable sgen memory (memory: auramono-pinning-fail-closed).
                if (!AuraMonoPinningAvailable)
                {
                    this.NoteFishShadowAuraSnapshotFailure(now, 60f, "mono_gchandle exports unavailable");
                    return false;
                }

                if (this.fishShadowAuraComponentClass == IntPtr.Zero)
                {
                    this.fishShadowAuraComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Fish.FishComponent");
                    if (this.fishShadowAuraComponentClass == IntPtr.Zero)
                    {
                        this.fishShadowAuraComponentClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.Fish.FishComponent");
                    }

                    if (this.fishShadowAuraComponentClass == IntPtr.Zero)
                    {
                        this.NoteFishShadowAuraSnapshotFailure(now, 10f, "FishComponent mono class unavailable");
                        return false;
                    }
                }

                int totalComponents = 0;
                int readFailures = 0;
                List<uint> compPins = new List<uint>();
                try
                {
                    // Returns false on empty too — an empty snapshot must NOT phantom-skip every
                    // candidate, so empty degrades to the legacy scan like any other failure.
                    if (!this.TryAuraMonoGetComponentObjects(this.fishShadowAuraComponentClass, out List<IntPtr> components, compPins) || components == null)
                    {
                        this.NoteFishShadowAuraSnapshotFailure(now, 5f, "GetComponents<FishComponent> failed or empty");
                        return false;
                    }

                    totalComponents = components.Count;
                    for (int i = 0; i < components.Count; i++)
                    {
                        IntPtr componentObj = components[i];
                        if (componentObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        // running == false => ViewComponent not in its Running phase (spawning or
                        // despawning — the phantom window). A read FAILURE is not "not live": count
                        // it and reject the whole snapshot below.
                        if (!this.TryGetMonoBoolMember(componentObj, "running", out bool compRunning))
                        {
                            readFailures++;
                            continue;
                        }

                        if (!compRunning)
                        {
                            continue;
                        }

                        if ((!this.TryGetMonoObjectMember(componentObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                        {
                            readFailures++;
                            continue;
                        }

                        uint entityPin = AuraMonoPinNew(entityObj);
                        try
                        {
                            // Entity teardown window (FadeOut/UnSpawning/Destroyed): the shadow is
                            // still rendered but the fish is already gone.
                            if (!this.TryGetMonoBoolMember(entityObj, "WillDie", out bool willDie))
                            {
                                readFailures++;
                                continue;
                            }

                            if (willDie)
                            {
                                continue;
                            }

                            if (!this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 entityPos))
                            {
                                readFailures++;
                                continue;
                            }

                            // netId 0 / read miss keeps the entry: LIVENESS decides the phantom
                            // filter, not identity — the scan then just reports netId=0 as before.
                            uint entityNetId = 0U;
                            this.TryGetAuraMonoEntityNetId(entityObj, out entityNetId);

                            // Boxed COPY of the FishComponentData struct field (see header comment).
                            if (!this.TryGetMonoObjectMember(componentObj, "_componentData", out IntPtr boxedData) || boxedData == IntPtr.Zero)
                            {
                                readFailures++;
                                continue;
                            }

                            uint dataPin = AuraMonoPinNew(boxedData);
                            try
                            {
                                if (!this.TryGetMonoIntMember(boxedData, "shadowState", out int shadowState))
                                {
                                    readFailures++;
                                    continue;
                                }

                                this.TryGetMonoUInt32Member(boxedData, "floatNetId", out uint floatNetId);
                                this.TryGetMonoUInt32Member(boxedData, "playerNetId", out uint playerNetId);
                                this.TryGetMonoIntMember(boxedData, "fishResId", out int fishResId);
                                this.TryGetMonoVector3Member(boxedData, "targetPos", out Vector3 targetPos);

                                snapshot.Add(new FishShadowAuraFishEntry
                                {
                                    NetId = entityNetId,
                                    Position = entityPos,
                                    ShadowState = shadowState,
                                    FloatNetId = floatNetId,
                                    PlayerNetId = playerNetId,
                                    FishResId = fishResId,
                                    TargetPos = targetPos
                                });
                            }
                            finally
                            {
                                AuraMonoPinFree(dataPin);
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(entityPin);
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(compPins);
                }

                if (readFailures > 0)
                {
                    // Unreliable snapshot (member rename after a game patch, transient invoke
                    // failures): using it could phantom-skip REAL fish. Degrade instead.
                    snapshot.Clear();
                    this.NoteFishShadowAuraSnapshotFailure(now, 10f, "member read failures on " + readFailures + "/" + totalComponents + " component(s)");
                    return false;
                }

                if (!string.IsNullOrEmpty(this.lastFishShadowAuraSnapshotFailReason))
                {
                    this.lastFishShadowAuraSnapshotFailReason = string.Empty;
                    this.AutoFishLog("AuraMono fish snapshot available again");
                }

                this.AutoFishLog("AuraMono fish snapshot: " + totalComponents + " comps, " + snapshot.Count + " live");
                return true;
            }
            catch (Exception ex)
            {
                snapshot.Clear();
                this.NoteFishShadowAuraSnapshotFailure(now, 10f, "exception: " + ex.Message);
                return false;
            }
        }

        private void NoteFishShadowAuraSnapshotFailure(float now, float retryDelay, string reason)
        {
            this.nextFishShadowAuraSnapshotRetryAt = now + retryDelay;
            if (!string.Equals(this.lastFishShadowAuraSnapshotFailReason, reason, StringComparison.Ordinal))
            {
                this.lastFishShadowAuraSnapshotFailReason = reason;
                this.AutoFishLog("AuraMono fish snapshot unavailable (" + reason + ") -> name-based scan fallback");
            }
        }

        private bool TryMatchFishShadowAuraSnapshotEntry(List<FishShadowAuraFishEntry> snapshot, Vector3 livePos, out FishShadowAuraFishEntry match)
        {
            match = default(FishShadowAuraFishEntry);
            if (snapshot == null || snapshot.Count == 0)
            {
                return false;
            }

            float maxSqr = FishShadowAuraSnapshotMatchRadius * FishShadowAuraSnapshotMatchRadius;
            float bestSqr = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < snapshot.Count; i++)
            {
                float dx = snapshot[i].Position.x - livePos.x;
                float dz = snapshot[i].Position.z - livePos.z;
                float distSqr = (dx * dx) + (dz * dz);
                if (distSqr <= maxSqr && distSqr < bestSqr)
                {
                    bestSqr = distSqr;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            match = snapshot[bestIndex];
            return true;
        }

        // Names mirror the FishShadowAiState enum (EcsClient XDT.Scene.Shared.Creatures) and the
        // strings the legacy interop path produced — the state checks in the scan compare on these.
        private static string GetFishShadowAiStateName(int state)
        {
            switch (state)
            {
                case 0: return "IdleDrift";
                case 1: return "IdleMove";
                case 2: return "FindBuoyWaiting";
                case 3: return "AttemptForward";
                case 4: return "Battle";
                case 5: return "Escape";
                case 6: return "Succeed";
                case 7: return "RunAway";
                default: return "State" + state;
            }
        }

        private void LogFishShadowResolverMiss(string status)
        {
            float now = Time.unscaledTime;
            if (now < this.nextFishShadowResolverMissLogAt && string.Equals(this.lastFishShadowResolverMissLogStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            this.lastFishShadowResolverMissLogStatus = status;
            this.nextFishShadowResolverMissLogAt = now + 10f;
            this.AutoFishLog("Fish shadow resolver miss: " + status);
        }

        private float GetFishShadowTargetScore(GameObject candidate, Vector3 candidatePos, float candidateDistance, Transform playerTransform, Camera mainCamera, int visualPriority)
        {
            float score = candidateDistance * 6f;

            if (playerTransform != null)
            {
                Vector3 toCandidate = candidatePos - playerTransform.position;
                toCandidate.y = 0f;
                if (toCandidate.sqrMagnitude > 0.001f)
                {
                    float playerAngle = Vector3.Angle(playerTransform.forward, toCandidate.normalized);
                    score += playerAngle * 1.4f;
                }
            }

            if (mainCamera != null)
            {
                Vector3 viewport = mainCamera.WorldToViewportPoint(candidatePos);
                if (viewport.z > 0f)
                {
                    float centerX = viewport.x - 0.5f;
                    float centerY = viewport.y - 0.5f;
                    float centerDistance = Mathf.Sqrt((centerX * centerX) + (centerY * centerY));
                    bool onScreen = viewport.x >= -0.15f && viewport.x <= 1.15f && viewport.y >= -0.15f && viewport.y <= 1.15f;
                    score += (onScreen ? 0f : 400f) + (centerDistance * 35f);
                }
                else
                {
                    score += 800f;
                }
            }

            score -= visualPriority * 360f;
            return score;
        }

        private float GetFishShadowCoopJitter(GameObject candidate, GameObject playerRoot)
        {
            if (candidate == null || playerRoot == null)
            {
                return 0f;
            }

            try
            {
                int hash = 17;
                unchecked
                {
                    string playerName = string.IsNullOrEmpty(playerRoot.name) ? "player" : playerRoot.name;
                    string candidateName = string.IsNullOrEmpty(candidate.name) ? "fish" : candidate.name;
                    hash = (hash * 31) + playerName.GetHashCode();
                    hash = (hash * 31) + candidateName.GetHashCode();
                    hash = (hash * 31) + Mathf.RoundToInt(playerRoot.transform.position.x * 10f);
                    hash = (hash * 31) + Mathf.RoundToInt(playerRoot.transform.position.z * 10f);
                    hash = (hash * 31) + Mathf.RoundToInt(candidate.transform.position.x * 10f);
                    hash = (hash * 31) + Mathf.RoundToInt(candidate.transform.position.z * 10f);
                }

                return Mathf.Abs(hash % 1000) / 1000f * 45f;
            }
            catch
            {
                return 0f;
            }
        }

        private bool TryGetFishShadowOccupancy(GameObject candidate, out uint buoyNetId, out uint playerNetId, out string state)
        {
            return this.TryGetFishShadowOccupancy(candidate, out buoyNetId, out playerNetId, out state, out _);
        }

        private bool TryGetFishShadowOccupancy(GameObject candidate, out uint buoyNetId, out uint playerNetId, out string state, out Vector3 moveTargetPos)
        {
            buoyNetId = 0U;
            playerNetId = 0U;
            state = string.Empty;
            moveTargetPos = Vector3.zero;
            if (candidate == null)
            {
                return false;
            }

            try
            {
                foreach (Component component in candidate.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        continue;
                    }

                    Il2CppObject componentObj = component.TryCast<Il2CppObject>();
                    Il2CppType componentType = componentObj?.GetIl2CppType();
                    if (componentType == null)
                    {
                        continue;
                    }

                    if (buoyNetId == 0U && this.TryReadUIntMember(componentType, componentObj, "BuoyNetId", out uint directBuoyNetId) && directBuoyNetId != 0U)
                    {
                        buoyNetId = directBuoyNetId;
                    }

                    if (buoyNetId == 0U && this.TryReadUIntMember(componentType, componentObj, "buoyNetId", out directBuoyNetId) && directBuoyNetId != 0U)
                    {
                        buoyNetId = directBuoyNetId;
                    }

                    if (playerNetId == 0U && this.TryReadUIntMember(componentType, componentObj, "playerNetId", out uint directPlayerNetId) && directPlayerNetId != 0U)
                    {
                        playerNetId = directPlayerNetId;
                    }

                    if (playerNetId == 0U && this.TryReadUIntMember(componentType, componentObj, "PlayerNetId", out directPlayerNetId) && directPlayerNetId != 0U)
                    {
                        playerNetId = directPlayerNetId;
                    }
                    if (string.IsNullOrEmpty(state))
                    {
                        this.TryReadMemberText(componentType, componentObj, "AiState", out state);
                    }

                    string[] dataMembers = new string[] { "ComponentData", "_componentData", "componentData", "data" };
                    foreach (string dataMember in dataMembers)
                    {
                        if (!this.TryReadObjectMember(componentType, componentObj, dataMember, out Il2CppObject dataObj) || dataObj == null)
                        {
                            continue;
                        }

                        Il2CppType dataType = dataObj.GetIl2CppType();
                        if (dataType == null)
                        {
                            continue;
                        }

                        if (buoyNetId == 0U)
                        {
                            if (this.TryReadUIntMember(dataType, dataObj, "BuoyNetId", out uint dataBuoyNetId) && dataBuoyNetId != 0U)
                            {
                                buoyNetId = dataBuoyNetId;
                            }
                            else if (this.TryReadUIntMember(dataType, dataObj, "floatNetId", out dataBuoyNetId) && dataBuoyNetId != 0U)
                            {
                                buoyNetId = dataBuoyNetId;
                            }
                        }

                        if (playerNetId == 0U)
                        {
                            if (this.TryReadUIntMember(dataType, dataObj, "playerNetId", out uint dataPlayerNetId) && dataPlayerNetId != 0U)
                            {
                                playerNetId = dataPlayerNetId;
                            }
                            else if (this.TryReadUIntMember(dataType, dataObj, "PlayerNetId", out dataPlayerNetId) && dataPlayerNetId != 0U)
                            {
                                playerNetId = dataPlayerNetId;
                            }
                        }

                        if (string.IsNullOrEmpty(state))
                        {
                            this.TryReadMemberText(dataType, dataObj, "shadowState", out state);
                            if (string.IsNullOrEmpty(state))
                            {
                                this.TryReadMemberText(dataType, dataObj, "AiState", out state);
                            }
                        }

                        // FishComponentData.targetPos = end point of the fish's current move (server
                        // bezier path). Used to lead-aim casts at IdleMove fish. y comes back 0 on the
                        // wire — callers must re-base it to the live fish height.
                        if (moveTargetPos == Vector3.zero)
                        {
                            this.TryReadVector3Member(dataObj, "targetPos", out moveTargetPos);
                        }
                    }
                }
            }
            catch
            {
            }

            return buoyNetId != 0U || playerNetId != 0U || !string.IsNullOrEmpty(state);
        }

        private bool TryGetFishShadowFishId(GameObject candidate, out int fishId)
        {
            fishId = 0;
            if (candidate == null)
            {
                return false;
            }

            try
            {
                if (this.TryGetFishShadowFishIdFromComponents(candidate.GetComponents<Component>(), out fishId))
                {
                    return true;
                }

                return this.TryGetFishShadowFishIdFromComponents(candidate.GetComponentsInChildren<Component>(true), out fishId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetFishShadowFishIdFromComponents(Component[] components, out int fishId)
        {
            fishId = 0;
            if (components == null)
            {
                return false;
            }

            string[] idMembers = new string[] { "FishId", "fishId", "fishResId", "FishResId", "StaticId", "staticId" };
            string[] dataMembers = new string[] { "ComponentData", "_componentData", "componentData", "data" };
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Il2CppObject componentObj = component.TryCast<Il2CppObject>();
                Il2CppType componentType = componentObj?.GetIl2CppType();
                if (componentType == null)
                {
                    continue;
                }

                for (int memberIndex = 0; memberIndex < idMembers.Length; memberIndex++)
                {
                    if (this.TryReadIntMember(componentType, componentObj, idMembers[memberIndex], out fishId) && fishId > 0)
                    {
                        return true;
                    }
                }

                for (int dataIndex = 0; dataIndex < dataMembers.Length; dataIndex++)
                {
                    if (!this.TryReadObjectMember(componentType, componentObj, dataMembers[dataIndex], out Il2CppObject dataObj) || dataObj == null)
                    {
                        continue;
                    }

                    Il2CppType dataType = dataObj.GetIl2CppType();
                    if (dataType == null)
                    {
                        continue;
                    }

                    for (int memberIndex = 0; memberIndex < idMembers.Length; memberIndex++)
                    {
                        if (this.TryReadIntMember(dataType, dataObj, idMembers[memberIndex], out fishId) && fishId > 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private int GetFishShadowVisualPriority(GameObject candidate)
        {
            return this.GetFishShadowVisualPriority(candidate, out _, out _);
        }

        private int GetFishShadowVisualPriority(GameObject candidate, out int fishId, out string source)
        {
            fishId = 0;
            source = string.Empty;
            if (candidate == null)
            {
                return 0;
            }

            this.TryGetFishShadowFishId(candidate, out fishId);
            string lowerName = string.IsNullOrEmpty(candidate.name) ? string.Empty : candidate.name.ToLowerInvariant();
            if (lowerName.Contains("p_fishshadow_shadow_l_4_t"))
            {
                source = "prefab-name-gold";
                return 3;
            }

            if (lowerName.Contains("p_fishshadow_shadow_m_2_t"))
            {
                source = "prefab-name-lightblue";
                return 2;
            }

            if (lowerName.Contains("gold") || lowerName.Contains("rare") || lowerName.Contains("rainbow"))
            {
                source = "object-name";
                return 3;
            }

            if (lowerName.Contains("blue") || lowerName.Contains("lightblue") || lowerName.Contains("light_blue"))
            {
                source = "object-name";
                return 2;
            }

            return 0;
        }

        private bool IsLocalPlayerOnFishingShip(out uint shipNetId)
        {
            shipNetId = 0U;
            if (this.TryGetLocalPlayerFishingShipNetId(out shipNetId) && shipNetId != 0U)
            {
                return true;
            }

            GameObject skeleton = HeartopiaComplete.GetLocalPlayer();
            if (skeleton == null)
            {
                return false;
            }

            for (Transform parent = skeleton.transform.parent; parent != null; parent = parent.parent)
            {
                if (HeartopiaComplete.IsLikelyFishingShipTransform(parent))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLikelyFishingShipTransform(Transform transform)
        {
            if (transform == null)
            {
                return false;
            }

            string name = transform.gameObject != null ? transform.gameObject.name ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("fishboat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fishingboat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fish_boat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("boat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetLocalPlayerFishingShipNetId(out uint shipNetId)
        {
            // The whole body was a managed self-player walk (EntityUtil / Character / DataCenter via
            // FindLoadedType). None of those types reach the BepInEx interop, so this helper has
            // always answered false; kept as an explicit false rather than a dead reflection tree.
            shipNetId = 0U;
            return false;
        }

        // XZ disagreement tolerated between the two independent self-position sources (Mono entity vs
        // the p_player_skeleton GameObject) before any position-writing facing path is refused. Normal
        // drift is centimetres of visual interpolation lag; metres means one of the two is not us.
        private const float CastFacingSelfPositionAgreementMeters = 2.5f;

        private bool TryFacePlayerTowardCastTarget(Vector3 targetPos, out string status)
        {
            status = "Player unavailable";

            try
            {
                // The position handed to BasePlayerComponent.Transfer must come from the SAME entity we
                // transfer. GetLocalPlayer() cannot provide that: it is GameObject.Find("p_player_skeleton
                // (Clone)") and remote players share that object name, so a Find landing on one (the 1s
                // cache then pins it while it stays active) fed another player's coordinates into a
                // checkCollision:false Transfer — a hard teleport onto that player. The Mono entity read is
                // authoritative; the skeleton is only a cross-check, and any disagreement or a missing Mono
                // read drops us to the rotation-only path instead of writing a position we do not trust.
                bool monoPosOk = this.TryGetCastFacingSelfPositionMono(out Vector3 monoPos, out string monoPosStatus);

                GameObject skeleton = HeartopiaComplete.GetLocalPlayer();
                GameObject positionSource = skeleton != null ? skeleton : this.FindPlayerRoot();
                bool goPosOk = positionSource != null;
                Vector3 goPos = goPosOk ? positionSource.transform.position : Vector3.zero;
                if (!monoPosOk && !goPosOk)
                {
                    return false;
                }

                Vector3 playerPos = monoPosOk ? monoPos : goPos;
                float posDrift = monoPosOk && goPosOk
                    ? new Vector2(monoPos.x - goPos.x, monoPos.z - goPos.z).magnitude
                    : -1f;

                bool allowPositionWrite;
                string writeState;
                if (!monoPosOk)
                {
                    allowPositionWrite = false;
                    writeState = "blocked:" + monoPosStatus;
                }
                else if (posDrift > CastFacingSelfPositionAgreementMeters)
                {
                    allowPositionWrite = false;
                    writeState = "blocked:drift";
                }
                else
                {
                    allowPositionWrite = true;
                    writeState = "allowed";
                }

                // The skeleton is only safe to touch (visual fallback) when the Mono anchor confirms it is
                // ours, or when there is no Mono anchor to check it against.
                bool skeletonLooksLocal = goPosOk && (!monoPosOk || posDrift <= CastFacingSelfPositionAgreementMeters);

                string anchorTrace = " playerPos=" + playerPos
                    + " src=" + (monoPosOk ? "mono-entity" : "gameobject")
                    + " go=" + (goPosOk ? goPos.ToString() : "none")
                    + (posDrift >= 0f ? " drift=" + posDrift.ToString("F2") + "m" : string.Empty)
                    + " write=" + writeState;

                bool onFishingShip = this.IsLocalPlayerOnFishingShip(out uint _);
                Vector3 flatDir = targetPos - playerPos;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude < 0.04f)
                {
                    status = "Cast target too close to rotate";
                    return false;
                }

                flatDir.Normalize();
                Quaternion faceRot = Quaternion.LookRotation(flatDir, Vector3.up);
                float targetYaw = faceRot.eulerAngles.y;
                Vector3 eulerAngles = new Vector3(0f, targetYaw, 0f);

                if (this.TrySyncLocalPlayerCastFacingMono(playerPos, eulerAngles, faceRot, flatDir, onFishingShip, allowPositionWrite, out string monoStatus))
                {
                    status = monoStatus;
                    this.AutoFishLog("Pre-cast entity facing yaw=" + targetYaw.ToString("F1") + " " + status + anchorTrace + " target=" + targetPos);
                    return true;
                }

                if (!skeletonLooksLocal)
                {
                    status = "skeleton anchor rejected" + anchorTrace;
                    return false;
                }

                if (skeleton != null)
                {
                    skeleton.transform.rotation = faceRot;
                }
                else if (!onFishingShip)
                {
                    positionSource.transform.rotation = faceRot;
                }

                status = onFishingShip
                    ? "visual-only ship-safe yaw=" + targetYaw.ToString("F1")
                    : "visual-only fallback yaw=" + targetYaw.ToString("F1");
                this.AutoFishLog("Pre-cast facing fallback " + status + anchorTrace + " target=" + targetPos);
                return true;
            }
            catch (Exception ex)
            {
                status = "Pre-cast facing failed: " + ex.Message;
                this.AutoFishLog(status);
                return false;
            }
        }

        // Authoritative self position for the pre-cast facing: read straight off the player component the
        // facing path transfers (GameplayApi.fishingMode.Player / character.player), so the position can
        // only ever be that entity's own. Deliberately re-resolves instead of taking a cached pointer —
        // holding a MonoObject* across the boxing allocations below is the stale-pointer AV pattern.
        private bool TryGetCastFacingSelfPositionMono(out Vector3 position, out string status)
        {
            position = Vector3.zero;
            status = "mono-unavailable";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr playerObj = IntPtr.Zero;
            if (!this.TryGetFishingPlayerMonoObject(out playerObj, out _, out _) || playerObj == IntPtr.Zero)
            {
                this.TryGetAuraMonoLocalPlayerObject(out playerObj);
            }

            if (playerObj == IntPtr.Zero)
            {
                status = "no-mono-player";
                return false;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                status = "no-entity";
                return false;
            }

            if (!this.TryGetMonoVector3Member(entityObj, "position", out position) || !IsFiniteVector(position))
            {
                position = Vector3.zero;
                status = "no-entity-position";
                return false;
            }

            status = "OK";
            return true;
        }

        // allowPositionWrite gates every path that writes a POSITION (Transfer, SetPositionAndRotation).
        // Both take playerPos verbatim with no collision check, so they teleport whatever they are given;
        // the caller only sets this once the anchor is proven to be our own entity. Rotation-only
        // (WorldFaceTo) always runs — it cannot move the player.
        private unsafe bool TrySyncLocalPlayerCastFacingMono(Vector3 playerPos, Vector3 eulerAngles, Quaternion faceRot, Vector3 flatDir, bool onFishingShip, bool allowPositionWrite, out string status)
        {
            status = "Mono entity facing unavailable";

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr playerObj = IntPtr.Zero;
            if (!this.TryGetFishingPlayerMonoObject(out playerObj, out _, out _) || playerObj == IntPtr.Zero)
            {
                this.TryGetAuraMonoLocalPlayerObject(out playerObj);
            }

            if (playerObj == IntPtr.Zero)
            {
                status = "Mono player unavailable";
                return false;
            }

            if (!onFishingShip)
            {
                onFishingShip = this.IsLocalPlayerOnFishingShip(out uint _);
            }

            IntPtr playerClass = auraMonoObjectGetClass(playerObj);
            if (!onFishingShip && allowPositionWrite)
            {
                IntPtr transferMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "Transfer", 4);
                if (transferMethod == IntPtr.Zero)
                {
                    transferMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "Transfer", 2);
                }

                if (transferMethod != IntPtr.Zero)
                {
                    int transferArgCount = this.TryGetAuraMonoMethodParamCount(transferMethod);
                    IntPtr exc = IntPtr.Zero;
                    if (transferArgCount >= 4)
                    {
                        Vector3 posValue = playerPos;
                        Vector3 eulerValue = eulerAngles;
                        uint parentNetId = 0U;
                        bool checkCollision = false;
                        IntPtr* transferArgs = stackalloc IntPtr[4];
                        transferArgs[0] = (IntPtr)(&posValue);
                        transferArgs[1] = (IntPtr)(&eulerValue);
                        transferArgs[2] = (IntPtr)(&parentNetId);
                        transferArgs[3] = (IntPtr)(&checkCollision);
                        auraMonoRuntimeInvoke(transferMethod, playerObj, (IntPtr)transferArgs, ref exc);
                    }
                    else
                    {
                        Vector3 posValue = playerPos;
                        Vector3 eulerValue = eulerAngles;
                        IntPtr* transferArgs = stackalloc IntPtr[2];
                        transferArgs[0] = (IntPtr)(&posValue);
                        transferArgs[1] = (IntPtr)(&eulerValue);
                        auraMonoRuntimeInvoke(transferMethod, playerObj, (IntPtr)transferArgs, ref exc);
                    }

                    if (exc == IntPtr.Zero)
                    {
                        status = "Transfer yaw=" + eulerAngles.y.ToString("F1");
                        return true;
                    }
                }
            }

            if (!this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) || moveObj == IntPtr.Zero)
            {
                status = onFishingShip ? "Ship-safe facing unavailable" : "Mono moveComponent unavailable";
                return false;
            }

            IntPtr moveClass = auraMonoObjectGetClass(moveObj);
            IntPtr worldFaceMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "WorldFaceTo", 1);
            if (worldFaceMethod == IntPtr.Zero)
            {
                status = "WorldFaceTo unavailable";
                return false;
            }

            Quaternion rotValue = faceRot;
            IntPtr exc2 = IntPtr.Zero;
            IntPtr* faceArgs = stackalloc IntPtr[1];
            faceArgs[0] = (IntPtr)(&rotValue);
            auraMonoRuntimeInvoke(worldFaceMethod, moveObj, (IntPtr)faceArgs, ref exc2);
            if (exc2 != IntPtr.Zero)
            {
                status = "WorldFaceTo exception";
                return false;
            }

            if (!onFishingShip && allowPositionWrite)
            {
                IntPtr setPosRotMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "SetPositionAndRotation", 3);
                if (setPosRotMethod != IntPtr.Zero)
                {
                    Vector3 posValue = playerPos;
                    Quaternion rotArg = faceRot;
                    bool worldSpace = true;
                    IntPtr* setArgs = stackalloc IntPtr[3];
                    setArgs[0] = (IntPtr)(&posValue);
                    setArgs[1] = (IntPtr)(&rotArg);
                    setArgs[2] = (IntPtr)(&worldSpace);
                    exc2 = IntPtr.Zero;
                    auraMonoRuntimeInvoke(setPosRotMethod, moveObj, (IntPtr)setArgs, ref exc2);
                }
            }

            Vector2 forward2D = new Vector2(flatDir.x, flatDir.z);
            this.TrySetMonoVector2Member(moveObj, "_Forward", forward2D);
            this.TrySetMonoVector2Member(moveObj, "Forward", forward2D);

            status = onFishingShip
                ? "Ship-safe WorldFaceTo yaw=" + eulerAngles.y.ToString("F1")
                : "WorldFaceTo yaw=" + eulerAngles.y.ToString("F1");
            return true;
        }

        // The stamina gate the vanilla cast has and the farm did not.
        //
        // FishingCommand.CheckExecuteThrowCommand refuses the throw unless
        //     player.dataComponent.GetStaminaCurrValue() > TableData.TableInteractions[600].staminaCost
        // and tips the player with UITipEvent 13. AutoFishingFarm never runs that command: the FSM
        // path enters at GameplayApi.EnterFishing, which is what ExecuteCommand() calls AFTER the
        // check, and Server-Side Fishing goes straight to FishingProtocolManager.CastRod. So nothing
        // on our side ever looked at energy -- while the server keeps enforcing it: an out-of-stamina
        // cast comes back CmdCastRodResult=false on the server-side path, and on the FSM path leaves
        // the state machine sitting in Waiting until it times out. Either way the cycle is burned for
        // nothing, which is why the farm asks this before it casts.
        //
        // THE COST IS A CONSTANT, not a table read: row 600 of the Interaction table carries
        // _staminaCost = 2 (decrypted tables, tools/HeartopiaTables/cn_tables.db), and pulling that
        // one int out of TableData.TableInteractions would mean inflating a
        // Dictionary<int, TableInteraction> walk over AuraMono on every cast. If a game update ever
        // raises the cost, this gate only becomes permissive again -- the server refusal and the
        // energy/durability line in DescribeServerFishResources() still report the truth.
        //
        // FAIL-OPEN BY DESIGN. Energy comes from the shared cache (PlayerStaminaUpdatedEvent, with
        // the energy panel's text as the fallback). When neither has produced a reading this returns
        // false: a gate that cannot see the value must never be the thing that stops the farm.
        private const int FishingCastStaminaCost = 2;

        public bool IsFishingEnergyTooLow(out string status)
        {
            try
            {
                if (!this.TryReadEnergy(out int current, out int max) || max <= 0 || current < 0)
                {
                    status = "energy unknown - cast allowed";
                    return false;
                }

                status = "energy " + current + "/" + max + " (cast needs > " + FishingCastStaminaCost + ")";
                return current <= FishingCastStaminaCost;
            }
            catch (Exception ex)
            {
                status = "energy read failed: " + ex.Message + " - cast allowed";
                return false;
            }
        }

        public bool TryEnterFishingAtTarget(Vector3 targetPos, out string status)
        {
            status = "GameplayApi unavailable";

            try
            {
                // FSM path only — Server-Side Fishing branches to TryServerSideFishingCast well
                // before this and never faces at all (it computes the buoy direction itself instead
                // of letting FishHelper.ComputeFloatInWaterData read entity.forward).
                if (!this.TryFacePlayerTowardCastTarget(targetPos, out string faceStatus))
                {
                    this.AutoFishLog("Pre-cast facing skipped: " + faceStatus);
                }

                if (this.TryResolveGameplayFishingApi(out Type _, out Type fishingSubStateType, out MethodInfo enterFishingMethod, out MethodInfo _))
                {
                    object waitingState = Enum.Parse(fishingSubStateType, "Waiting");
                    enterFishingMethod.Invoke(null, new object[] { waitingState, targetPos });
                    status = "EnterFishing invoked";
                    this.AutoFishLog("EnterFishing invoked at " + targetPos);
                    return true;
                }

                if (this.TryEnterFishingAtTargetMono(targetPos, out status))
                {
                    return true;
                }

                if (this.TryEnterFishingAtTargetIl2Cpp(targetPos, out status))
                {
                    return true;
                }

                if (!this.TryResolveGameplayFishingApi(out _, out _, out _, out _))
                {
                    status = "GameplayApi fishing methods unavailable";
                    return false;
                }

                status = "GameplayApi fishing methods unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "EnterFishing failed: " + ex.Message;
                this.AutoFishLog("EnterFishing exception: " + ex.Message);
                return false;
            }
        }

        public bool TryExitFishing(out string status)
        {
            status = "GameplayApi unavailable";

            try
            {
                if (this.TryResolveGameplayFishingApi(out Type _, out Type _, out MethodInfo _, out MethodInfo exitFishingMethod))
                {
                    exitFishingMethod.Invoke(null, null);
                    status = "ExitFishing invoked";
                    this.AutoFishLog("ExitFishing invoked.");
                    return true;
                }

                if (this.TryExitFishingMono(out status))
                {
                    return true;
                }

                if (this.TryExitFishingIl2Cpp(out status))
                {
                    return true;
                }

                if (!this.TryResolveGameplayFishingApi(out _, out _, out _, out _))
                {
                    status = "GameplayApi exit unavailable";
                    return false;
                }

                status = "GameplayApi exit unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "ExitFishing failed: " + ex.Message;
                this.AutoFishLog("ExitFishing exception: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveGameplayFishingApi(out Type gameplayApiType, out Type fishingSubStateType, out MethodInfo enterFishingMethod, out MethodInfo exitFishingMethod)
        {
            gameplayApiType = this.cachedFishingGameplayApiType
                ?? this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.GameplayApi", "GameplayApi")
                ?? this.FindLoadedTypeBySuffix("GameplaySystem.GameplayApi", ".GameplayApi");
            fishingSubStateType = this.cachedFishingSubStateType
                ?? this.FindLoadedType("XDT.Scene.Shared.Creatures.FishingSubState", "FishingSubState")
                ?? this.FindLoadedTypeBySuffix("Scene.Shared.Creatures.FishingSubState", ".FishingSubState");
            enterFishingMethod = this.cachedFishingEnterFishingMethod;
            exitFishingMethod = this.cachedFishingExitFishingMethod;

            if (gameplayApiType == null)
            {
                this.AutoFishLog("GameplayApi resolver failed: gameplayApiType missing.");
                return false;
            }

            if (enterFishingMethod == null)
            {
                if (fishingSubStateType != null)
                {
                    enterFishingMethod = gameplayApiType.GetMethod("EnterFishing", BindingFlags.Public | BindingFlags.Static, null, new Type[] { fishingSubStateType, typeof(Vector3) }, null);
                }

                if (enterFishingMethod == null)
                {
                    foreach (MethodInfo method in gameplayApiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!string.Equals(method.Name, "EnterFishing", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 2 || parameters[1].ParameterType != typeof(Vector3))
                        {
                            continue;
                        }

                        enterFishingMethod = method;
                        fishingSubStateType = parameters[0].ParameterType;
                        break;
                    }
                }
            }

            if (fishingSubStateType == null && enterFishingMethod != null)
            {
                ParameterInfo[] parameters = enterFishingMethod.GetParameters();
                if (parameters.Length >= 1)
                {
                    fishingSubStateType = parameters[0].ParameterType;
                }
            }

            if (exitFishingMethod == null)
            {
                exitFishingMethod = gameplayApiType.GetMethod("ExitFishing", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            }

            if (fishingSubStateType == null)
            {
                this.AutoFishLog("GameplayApi resolver failed: fishingSubStateType missing. gameplayApi=" + this.DescribeType(gameplayApiType));
                return false;
            }

            this.cachedFishingGameplayApiType = gameplayApiType;
            this.cachedFishingSubStateType = fishingSubStateType;
            this.cachedFishingEnterFishingMethod = enterFishingMethod;
            this.cachedFishingExitFishingMethod = exitFishingMethod;
            this.AutoFishLog(
                "GameplayApi resolver: api=" + this.DescribeType(gameplayApiType)
                + " subState=" + this.DescribeType(fishingSubStateType)
                + " enter=" + (enterFishingMethod != null ? enterFishingMethod.ToString() : "null")
                + " exit=" + (exitFishingMethod != null ? exitFishingMethod.ToString() : "null"));
            return enterFishingMethod != null && exitFishingMethod != null;
        }

        private unsafe bool TryEnterFishingAtTargetMono(Vector3 targetPos, out string status)
        {
            status = "GameplayApi Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "GameplayApi Mono runtime unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: runtime unavailable.");
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.GameplayApi");
                if (classPtr == IntPtr.Zero)
                {
                    status = "GameplayApi Mono class unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: class missing.");
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "EnterFishing", 2);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "GameplayApi.EnterFishing Mono method unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: EnterFishing missing on " + this.GetAuraMonoClassDisplayName(classPtr));
                    return false;
                }

                Vector3 resolvedTargetPos = targetPos;
                if (resolvedTargetPos == Vector3.zero)
                {
                    status = "Fishing throw target was zero";
                    this.AutoFishLog("EnterFishing Mono aborted: " + status);
                    return false;
                }

                if (this.TryForceFishingRodThrowTargetMono(resolvedTargetPos, out string bypassStatus))
                {
                    this.AutoFishLog("Fishing throw target bypass armed: " + bypassStatus);
                }
                else
                {
                    this.AutoFishLog("Fishing throw target bypass skipped: " + bypassStatus);
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                int waitingState = 1;
                Vector3 targetValue = resolvedTargetPos;
                args[0] = (IntPtr)(&waitingState);
                args[1] = (IntPtr)(&targetValue);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "GameplayApi.EnterFishing Mono exception";
                    this.AutoFishLog("GameplayApi Mono EnterFishing raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                this.lastFishingEnterRequestedAt = Time.unscaledTime;
                this.lastFishingExitRequestedAt = -999f;
                status = "EnterFishing invoked (Mono)";
                this.AutoFishLog("EnterFishing Mono invoked direct fish-shadow target=" + resolvedTargetPos + " class=" + this.GetAuraMonoClassDisplayName(classPtr));
                return true;
            }
            catch (Exception ex)
            {
                status = "EnterFishing Mono failed: " + ex.Message;
                this.AutoFishLog("EnterFishing Mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryForceFishingRodThrowTargetMono(Vector3 targetPos, out string status)
        {
            status = "Fishing rod throw bypass unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null
                    || auraMonoObjectGetClass == null
                    || auraMonoFieldSetValue == null)
                {
                    status = "Fishing rod throw bypass runtime unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                {
                    status = "Fishing rod throw bypass interact unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw bypass player unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") || equipObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw bypass equip unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") || handholdObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw bypass handhold unavailable";
                    return false;
                }

                IntPtr handholdClass = auraMonoObjectGetClass(handholdObj);
                string handholdClassName = this.GetAuraMonoClassDisplayName(handholdClass);
                if (string.IsNullOrEmpty(handholdClassName)
                    || handholdClassName.IndexOf("FishingRod", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    status = "Fishing rod throw bypass handhold is not fishing rod";
                    return false;
                }

                IntPtr throwTargetField = this.FindAuraMonoFieldOnHierarchy(handholdClass, "_throwTarPos");
                if (throwTargetField == IntPtr.Zero)
                {
                    throwTargetField = this.FindAuraMonoFieldOnHierarchy(handholdClass, "throwTarPos");
                }

                if (throwTargetField == IntPtr.Zero)
                {
                    status = "Fishing rod throw target field unavailable";
                    return false;
                }

                Vector3 targetValue = targetPos;
                auraMonoFieldSetValue(handholdObj, throwTargetField, (IntPtr)(&targetValue));

                IntPtr canThrowField = this.FindAuraMonoFieldOnHierarchy(handholdClass, "CanThrow");
                if (canThrowField != IntPtr.Zero)
                {
                    bool canThrow = true;
                    auraMonoFieldSetValue(handholdObj, canThrowField, (IntPtr)(&canThrow));
                }

                status = "direct=" + targetPos + " rod=" + handholdClassName;
                return true;
            }
            catch (Exception ex)
            {
                status = "Fishing rod throw bypass failed: " + ex.Message;
                this.AutoFishLog("Fishing rod throw bypass exception: " + ex.Message);
                return false;
            }
        }

        private bool TryExitFishingMono(out string status)
        {
            status = "GameplayApi Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "GameplayApi Mono runtime unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: runtime unavailable for ExitFishing.");
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.GameplayApi");
                if (classPtr == IntPtr.Zero)
                {
                    status = "GameplayApi Mono class unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: class missing for ExitFishing.");
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "ExitFishing", 0);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "GameplayApi.ExitFishing Mono method unavailable";
                    this.AutoFishLog("GameplayApi Mono resolver failed: ExitFishing missing on " + this.GetAuraMonoClassDisplayName(classPtr));
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
                bool exitInvoked = exc == IntPtr.Zero;
                if (!exitInvoked)
                {
                    this.AutoFishLog("GameplayApi Mono ExitFishing raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                }

                bool cancelInvoked = this.TryCancelFishingProtocolMono(out string cancelStatus);

                this.lastFishingExitRequestedAt = Time.unscaledTime;
                if (exitInvoked && cancelInvoked)
                {
                    status = "ExitFishing + CancelFishing invoked (Mono)";
                    this.AutoFishLog("ExitFishing Mono invoked with CancelFishing protocol.");
                    return true;
                }

                if (exitInvoked)
                {
                    status = "ExitFishing invoked (Mono); CancelFishing=" + cancelStatus;
                    this.AutoFishLog("ExitFishing Mono invoked; CancelFishing status=" + cancelStatus);
                    return true;
                }

                if (cancelInvoked)
                {
                    status = "CancelFishing invoked (Mono protocol)";
                    this.AutoFishLog("GameplayApi ExitFishing failed, but CancelFishing protocol succeeded.");
                    return true;
                }

                status = "GameplayApi.ExitFishing Mono exception; CancelFishing=" + cancelStatus;
                return false;
            }
            catch (Exception ex)
            {
                status = "ExitFishing Mono failed: " + ex.Message;
                this.AutoFishLog("ExitFishing Mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryCancelFishingProtocolMono(out string status)
        {
            status = "CancelFishing mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "CancelFishing mono runtime unavailable";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    status = "CancelFishing protocol class unavailable";
                    this.AutoFishLog("CancelFishing mono resolver failed: protocol class missing.");
                    return false;
                }

                IntPtr cancelMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "CancelFishing", 0);
                if (cancelMethod == IntPtr.Zero)
                {
                    status = "CancelFishing protocol method unavailable";
                    this.AutoFishLog("CancelFishing mono resolver failed: method missing on " + this.GetAuraMonoClassDisplayName(protocolClass));
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(cancelMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "CancelFishing mono exception";
                    this.AutoFishLog("CancelFishing mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "CancelFishing invoked (Mono protocol)";
                this.AutoFishLog("CancelFishing mono invoked.");
                return true;
            }
            catch (Exception ex)
            {
                status = "CancelFishing mono failed: " + ex.Message;
                this.AutoFishLog("CancelFishing mono exception: " + ex.Message);
                return false;
            }
        }

        private bool TryEnterFishingAtTargetIl2Cpp(Vector3 targetPos, out string status)
        {
            status = "GameplayApi IL2CPP unavailable";

            try
            {
                Il2CppType gameplayApiIlType = this.TryGetFishingAutomationIl2CppType(
                    "XDTLevelAndEntity.GameplaySystem.GameplayApi",
                    "GameplayApi");
                Il2CppType fishingSubStateIlType = this.TryGetFishingAutomationIl2CppType(
                    "XDT.Scene.Shared.Creatures.FishingSubState",
                    "FishingSubState");
                if (gameplayApiIlType == null)
                {
                    this.AutoFishLog("GameplayApi IL2CPP resolver failed: gameplayApiType missing.");
                    status = "GameplayApi IL2CPP type unavailable";
                    return false;
                }

                if (fishingSubStateIlType == null)
                {
                    this.AutoFishLog("GameplayApi IL2CPP resolver failed: fishingSubStateType missing.");
                    status = "FishingSubState IL2CPP type unavailable";
                    return false;
                }

                Il2CppMethodInfo enterMethod = gameplayApiIlType.GetMethod("EnterFishing");
                if (enterMethod == null)
                {
                    this.AutoFishLog("GameplayApi IL2CPP resolver failed: EnterFishing missing on " + gameplayApiIlType.FullName);
                    status = "GameplayApi.EnterFishing IL2CPP method unavailable";
                    return false;
                }

                object waitingStateManaged = 1;
                Type managedFishingSubStateType = this.cachedFishingSubStateType
                    ?? this.FindLoadedType("XDT.Scene.Shared.Creatures.FishingSubState", "FishingSubState")
                    ?? this.FindLoadedTypeBySuffix("Scene.Shared.Creatures.FishingSubState", ".FishingSubState");
                if (managedFishingSubStateType != null && managedFishingSubStateType.IsEnum)
                {
                    waitingStateManaged = Enum.Parse(managedFishingSubStateType, "Waiting");
                }
                else
                {
                    waitingStateManaged = 1;
                }

                Il2CppReferenceArray<Il2CppObject> invokeArgs = this.BuildIl2CppInvokeArgs(new object[] { waitingStateManaged, targetPos });
                enterMethod.Invoke(null, invokeArgs);
                status = "EnterFishing invoked (IL2CPP)";
                this.AutoFishLog("EnterFishing IL2CPP invoked at " + targetPos + " api=" + gameplayApiIlType.FullName + " subState=" + fishingSubStateIlType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                status = "EnterFishing IL2CPP failed: " + ex.Message;
                this.AutoFishLog("EnterFishing IL2CPP exception: " + ex.Message);
                return false;
            }
        }

        private bool TryExitFishingIl2Cpp(out string status)
        {
            status = "GameplayApi IL2CPP unavailable";

            try
            {
                Il2CppType gameplayApiIlType = this.TryGetFishingAutomationIl2CppType(
                    "XDTLevelAndEntity.GameplaySystem.GameplayApi",
                    "GameplayApi");
                if (gameplayApiIlType == null)
                {
                    this.AutoFishLog("GameplayApi IL2CPP resolver failed: gameplayApiType missing for ExitFishing.");
                    status = "GameplayApi IL2CPP type unavailable";
                    return false;
                }

                Il2CppMethodInfo exitMethod = gameplayApiIlType.GetMethod("ExitFishing");
                if (exitMethod == null)
                {
                    this.AutoFishLog("GameplayApi IL2CPP resolver failed: ExitFishing missing on " + gameplayApiIlType.FullName);
                    status = "GameplayApi.ExitFishing IL2CPP method unavailable";
                    return false;
                }

                exitMethod.Invoke(null, null);
                status = "ExitFishing invoked (IL2CPP)";
                this.AutoFishLog("ExitFishing IL2CPP invoked.");
                return true;
            }
            catch (Exception ex)
            {
                status = "ExitFishing IL2CPP failed: " + ex.Message;
                this.AutoFishLog("ExitFishing IL2CPP exception: " + ex.Message);
                return false;
            }
        }

        public bool TryGetFishingAutomationState(out bool inFishingState, out string fishState, out bool pressed, out float pullStrength, out float rodDurability, out uint baitingFishNetId, out string status)
        {
            inFishingState = false;
            fishState = string.Empty;
            pressed = false;
            pullStrength = 0f;
            rodDurability = 1f;
            baitingFishNetId = 0U;
            status = "Fishing status unavailable";

            if (this.TryGetFishingAutomationStateMono(out inFishingState, out fishState, out pressed, out pullStrength, out rodDurability, out baitingFishNetId, out status))
            {
                return true;
            }

            return false;
        }

        public bool TrySetFishingPressed(bool pressed, out string status)
        {
            status = "Fishing status unavailable";

            if (this.TrySetFishingPressedMono(pressed, out status))
            {
                return true;
            }

            return false;
        }

        private unsafe bool TryGetFishingAutomationStateMono(out bool inFishingState, out string fishState, out bool pressed, out float pullStrength, out float rodDurability, out uint baitingFishNetId, out string status)
        {
            inFishingState = false;
            fishState = string.Empty;
            pressed = false;
            pullStrength = 0f;
            rodDurability = 1f;
            baitingFishNetId = 0U;
            status = "Fishing status mono unavailable";

            try
            {
                if (!this.TryGetFishingStatusMonoObject(out IntPtr fishingStatusObj, out IntPtr _, out status))
                {
                    return false;
                }

                inFishingState = this.TryGetMonoBoolMember(fishingStatusObj, "InFishingState", out bool monoInFishing)
                    ? monoInFishing
                    : (this.TryGetMonoBoolMember(fishingStatusObj, "inFishingState", out monoInFishing) ? monoInFishing : false);

                if (this.TryGetMonoInt32Member(fishingStatusObj, "FishState", out int fishStateValue)
                    || this.TryGetMonoInt32Member(fishingStatusObj, "fishState", out fishStateValue)
                    || this.TryGetMonoIntMember(fishingStatusObj, "FishState", out fishStateValue)
                    || this.TryGetMonoIntMember(fishingStatusObj, "fishState", out fishStateValue))
                {
                    fishState = this.DescribeFishingSubState(fishStateValue);
                }

                pressed = this.TryGetMonoBoolMember(fishingStatusObj, "Pressed", out bool monoPressed)
                    ? monoPressed
                    : (this.TryGetMonoBoolMember(fishingStatusObj, "pressed", out monoPressed) ? monoPressed : false);

                this.TryGetMonoSingleMember(fishingStatusObj, "PullStrength", out pullStrength);
                if (pullStrength <= 0f)
                {
                    this.TryGetMonoSingleMember(fishingStatusObj, "pullStrength", out pullStrength);
                }

                this.TryGetMonoUInt32Member(fishingStatusObj, "BaitingFishNetId", out baitingFishNetId);
                if (baitingFishNetId == 0U)
                {
                    this.TryGetMonoUInt32Member(fishingStatusObj, "baitingFishNetId", out baitingFishNetId);
                }

                bool looksLikeInvalidPullStrength = pullStrength < 0f || pullStrength > 1.05f;
                if (this.TryGetFishingMotionMonoState(out string motionFishState, out float motionPullStrength, out float motionRodDurability, out string motionStatus))
                {
                    bool hasMotionState = !string.IsNullOrWhiteSpace(motionFishState);
                    bool hasMotionPull = motionPullStrength >= 0f && motionPullStrength <= 1.05f;
                    bool hasRodDurability = motionRodDurability >= 0f && motionRodDurability <= 1.05f;
                    if (hasMotionState && (!inFishingState || string.IsNullOrWhiteSpace(fishState) || string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)))
                    {
                        fishState = motionFishState;
                    }

                    if (hasMotionPull && (looksLikeInvalidPullStrength || string.Equals(fishState, "Battle", StringComparison.OrdinalIgnoreCase)))
                    {
                        pullStrength = motionPullStrength;
                        looksLikeInvalidPullStrength = false;
                    }

                    if (hasRodDurability)
                    {
                        rodDurability = motionRodDurability;
                    }
                }

                float now = Time.unscaledTime;
                bool exitWasRequestedRecently = this.lastFishingExitRequestedAt > 0f
                    && this.lastFishingExitRequestedAt >= this.lastFishingEnterRequestedAt
                    && now - this.lastFishingExitRequestedAt <= 3f;
                bool looksLikeStaleIdleState = inFishingState
                    && string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                    && !pressed
                    && pullStrength <= 0f
                    && baitingFishNetId == 0U;
                bool looksLikeImpossibleIdleBaitState = inFishingState
                    && string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                    && !pressed
                    && pullStrength <= 0f
                    && baitingFishNetId != 0U;
                bool looksLikeStaleIdlePullState = inFishingState
                    && string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                    && !pressed
                    && pullStrength > 0.05f
                    && baitingFishNetId == 0U;
                if (exitWasRequestedRecently && (looksLikeStaleIdleState || looksLikeImpossibleIdleBaitState || looksLikeStaleIdlePullState))
                {
                    inFishingState = false;
                    status = looksLikeImpossibleIdleBaitState
                        ? "Suppressed impossible idle+bait fishing state after exit"
                        : (looksLikeStaleIdlePullState
                            ? "Suppressed stale idle+pull fishing state after exit"
                            : "Suppressed stale idle fishing state after exit");
                    return true;
                }

                status = "OK";
                return true;
            }
            catch (Exception ex)
            {
                status = "Fishing status mono failed: " + ex.Message;
                this.AutoFishLog("Fishing status mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TrySetFishingPressedMono(bool pressed, out string status)
        {
            status = "Pressed mono unavailable";

            try
            {
                if (!this.TryGetFishingStatusMonoObject(out IntPtr fishingStatusObj, out IntPtr _, out status))
                {
                    return false;
                }

                bool inFishingState = this.TryGetMonoBoolMember(fishingStatusObj, "InFishingState", out bool monoInFishing)
                    ? monoInFishing
                    : (this.TryGetMonoBoolMember(fishingStatusObj, "inFishingState", out monoInFishing) ? monoInFishing : false);
                if (!inFishingState)
                {
                    status = "Fishing inactive";
                    return false;
                }

                bool stateButtonUpdated = false;
                if (this.TrySetFishingStateButtonPressedMono(pressed, out status))
                {
                    stateButtonUpdated = true;
                }

                if (this.TryInvokeFishingPullProtocolMono(pressed, out string protocolStatus))
                {
                    status = stateButtonUpdated
                        ? "Pressed updated (Mono player state + " + protocolStatus + ")"
                        : protocolStatus;
                    return true;
                }

                if (stateButtonUpdated)
                {
                    status = "Pressed updated (Mono player state; protocol skipped: " + protocolStatus + ")";
                    return true;
                }

                status = protocolStatus;
                return false;
            }
            catch (Exception ex)
            {
                status = "Fishing pull mono failed: " + ex.Message;
                this.AutoFishLog("Fishing pull mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryInvokeFishingPullProtocolMono(bool pressed, out string status)
        {
            status = "Fishing pull protocol unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "Fishing pull mono runtime unavailable";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    status = "Fishing pull protocol class unavailable";
                    this.AutoFishLog("Fishing pull mono resolver failed: protocol class missing.");
                    return false;
                }

                IntPtr pullMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "FishingRodPull", 1);
                if (pullMethod == IntPtr.Zero)
                {
                    status = "Fishing pull protocol method unavailable";
                    this.AutoFishLog("Fishing pull mono resolver failed: FishingRodPull missing on " + this.GetAuraMonoClassDisplayName(protocolClass));
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                bool pressedValue = pressed;
                args[0] = (IntPtr)(&pressedValue);
                auraMonoRuntimeInvoke(pullMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Fishing pull mono exception";
                    this.AutoFishLog("Fishing pull mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "Pressed updated (Mono protocol)";
                return true;
            }
            catch (Exception ex)
            {
                status = "Fishing pull protocol failed: " + ex.Message;
                this.AutoFishLog("Fishing pull mono exception: " + ex.Message);
                return false;
            }
        }

        // --- Instant Catch (position-report spoof) ---------------------------------------
        // The server gates the bite/battle on the buoy geometry the client reports. We re-send
        // UpdateRodBuoyPositionNetworkCommand with a collapsed successLength on ChannelType.Reliable
        // (the game's FishingProtocolManager.UpdateFloatPosition uses Unreliable). Player position
        // is never touched. All resolution goes through AuraMono.
        // Reliable-channel buoy update: WebRequestUtility + *NetworkCommand are embedded-Mono only
        // (see DrawUploadFeature.cs) — AuraMono SendCommand<T> with ChannelType.Reliable.
        private IntPtr cachedInstantCatchAuraWebRequestClass;
        private IntPtr cachedInstantCatchAuraBuoyCmdClass;
        private IntPtr cachedInstantCatchAuraSendCommandOpenMethod;
        private IntPtr cachedInstantCatchAuraSendBuoyReliableMethod;
        private IntPtr cachedInstantCatchAuraBuoyFieldBuoyNetId;
        private IntPtr cachedInstantCatchAuraBuoyFieldBuoyPos;
        private IntPtr cachedInstantCatchAuraBuoyFieldDirection;
        private IntPtr cachedInstantCatchAuraBuoyFieldSuccessLength;
        private IntPtr cachedInstantCatchAuraBuoyFieldFailureLength;
        private const int InstantCatchAuraReliableChannel = 1;
        private bool instantCatchBuoyCommandResolveLogged;
        private bool fishingInstantCatchResolveLogged;
        private float nextInstantCatchSendLogAt = -999f;
        private float nextInstantCatchLogAt = -999f;

        // EXPERIMENT — "far anchor at activation": for a brief window around the buoy LANDING (the buoy
        // consistently settles ~1.7-2.0s after cast), report a FAR buoy position so the server latches a
        // far battle anchor, then revert to the real buoy. With a far anchor, the battle win condition
        // Distance(mouth,player) < Distance(buoy,player) is met without reeling (helps "fighting" fish).
        private const float InstantCatchFarAnchorDist = 50f;
        private const float InstantCatchFarWinStart = 1.5f;
        private const float InstantCatchFarWinEnd = 1.9f;
        private int instantCatchFarLogSeq = -1;

        // Per-cast diagnostics: AutoFishingFarm bumps the sequence and timestamp on every cast so each
        // throw's log lines can be told apart and timed (cast -> bite -> result).
        public int InstantCatchCastSeq;
        public float InstantCatchCastAt;

        // Unthrottled diagnostic log (for infrequent cast/bite/result events) — visible when
        // MasterLogInstantCatch is on, unlike the throttled InstantCatchLog used for per-tick sends.
        public void InstantCatchDiag(string message)
        {
            if (!MasterLogInstantCatch || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[InstantCatch] " + message);
        }

        private void InstantCatchLog(string message, bool force = false)
        {
            if (!MasterLogInstantCatch || string.IsNullOrEmpty(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!force && now < this.nextInstantCatchLogAt)
            {
                return;
            }

            this.nextInstantCatchLogAt = now + 1f;
            ModLogger.Msg("[InstantCatch] " + message);
        }

        private static bool IsFiniteVector(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        // Reads the buoy's current world position via the equipped rod:
        // InteractSystem.GetPlayer() -> equipComponent.handhold (HandHoldFishingRod) ->
        // GetFloatPosition() == floatComponent.entity.position. All AuraMono.
        private unsafe bool TryGetFishingBuoyPositionMono(out Vector3 buoyPos, out string status)
        {
            buoyPos = Vector3.zero;
            status = "buoy position unavailable";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null || auraMonoObjectGetClass == null)
            {
                status = "mono runtime unavailable";
                return false;
            }

            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
            {
                status = "interact/system unavailable";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                status = "player unavailable";
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") || equipObj == IntPtr.Zero)
            {
                status = "equipComponent unavailable";
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") || handholdObj == IntPtr.Zero)
            {
                status = "handhold unavailable";
                return false;
            }

            IntPtr handholdClass = auraMonoObjectGetClass(handholdObj);
            string handholdClassName = this.GetAuraMonoClassDisplayName(handholdClass);
            if (string.IsNullOrEmpty(handholdClassName) || handholdClassName.IndexOf("FishingRod", StringComparison.OrdinalIgnoreCase) < 0)
            {
                status = "handhold is not fishing rod (" + handholdClassName + ")";
                return false;
            }

            IntPtr getFloatPosMethod = this.FindAuraMonoMethodOnHierarchy(handholdClass, "GetFloatPosition", 0);
            if (getFloatPosMethod == IntPtr.Zero)
            {
                status = "GetFloatPosition method missing on " + handholdClassName;
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getFloatPosMethod, handholdObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                status = "GetFloatPosition invoke failed";
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                status = "GetFloatPosition unbox failed";
                return false;
            }

            buoyPos = *(Vector3*)raw;
            status = "ok " + buoyPos;
            return true;
        }

        // Mono-only path: inflate WebRequestUtility.SendCommand<UpdateRodBuoyPositionNetworkCommand>.
        private unsafe bool TryInstantCatchInflateAuraSendCommand(IntPtr openMethod, IntPtr commandClass, out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (openMethod == IntPtr.Zero
                || commandClass == IntPtr.Zero
                || auraMonoClassGetType == null
                || auraMonoClassInflateGenericMethod == null
                || auraMonoMetadataGetGenericInst == null)
            {
                return false;
            }

            IntPtr commandType = auraMonoClassGetType(commandClass);
            if (commandType == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = commandType;
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

            if (auraMonoCompileMethod != null)
            {
                try
                {
                    auraMonoCompileMethod(inflatedMethod);
                }
                catch
                {
                }
            }

            return AuraMonoMethodParamCountIs(inflatedMethod, 3);
        }

        private unsafe bool TryEnsureInstantCatchAuraBuoySend(out string resolveStatus)
        {
            resolveStatus = "aura buoy send unresolved";
            if (this.cachedInstantCatchAuraSendBuoyReliableMethod != IntPtr.Zero)
            {
                resolveStatus = "cached";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null
                || auraMonoObjectUnbox == null)
            {
                resolveStatus = "aura mono api unavailable";
                return false;
            }

            if (this.cachedInstantCatchAuraWebRequestClass == IntPtr.Zero)
            {
                this.cachedInstantCatchAuraWebRequestClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            }

            if (this.cachedInstantCatchAuraBuoyCmdClass == IntPtr.Zero)
            {
                this.cachedInstantCatchAuraBuoyCmdClass = this.FindAuraMonoClassByFullName(
                    "XDT.Scene.Shared.GamePlay.Fishing.UpdateRodBuoyPositionNetworkCommand");
                if (this.cachedInstantCatchAuraBuoyCmdClass == IntPtr.Zero)
                {
                    this.cachedInstantCatchAuraBuoyCmdClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDT.Scene.Shared.GamePlay.Fishing",
                        "UpdateRodBuoyPositionNetworkCommand");
                }
            }

            if (this.cachedInstantCatchAuraWebRequestClass == IntPtr.Zero
                || this.cachedInstantCatchAuraBuoyCmdClass == IntPtr.Zero)
            {
                resolveStatus = "class missing web="
                    + (this.cachedInstantCatchAuraWebRequestClass != IntPtr.Zero)
                    + " cmd=" + (this.cachedInstantCatchAuraBuoyCmdClass != IntPtr.Zero);
                this.LogInstantCatchBuoyResolveOnce(resolveStatus);
                return false;
            }

            if (this.cachedInstantCatchAuraSendCommandOpenMethod == IntPtr.Zero)
            {
                this.cachedInstantCatchAuraSendCommandOpenMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.cachedInstantCatchAuraWebRequestClass,
                    "SendCommand",
                    3);
            }

            if (this.cachedInstantCatchAuraSendCommandOpenMethod == IntPtr.Zero)
            {
                resolveStatus = "SendCommand(3) missing on WebRequestUtility";
                this.LogInstantCatchBuoyResolveOnce(resolveStatus);
                return false;
            }

            if (!this.TryInstantCatchInflateAuraSendCommand(
                this.cachedInstantCatchAuraSendCommandOpenMethod,
                this.cachedInstantCatchAuraBuoyCmdClass,
                out this.cachedInstantCatchAuraSendBuoyReliableMethod))
            {
                resolveStatus = "SendCommand inflate failed";
                this.LogInstantCatchBuoyResolveOnce(resolveStatus);
                return false;
            }

            if (this.cachedInstantCatchAuraBuoyFieldBuoyNetId == IntPtr.Zero)
            {
                this.cachedInstantCatchAuraBuoyFieldBuoyNetId = this.FindAuraMonoFieldOnHierarchy(
                    this.cachedInstantCatchAuraBuoyCmdClass, "BuoyNetId");
                this.cachedInstantCatchAuraBuoyFieldBuoyPos = this.FindAuraMonoFieldOnHierarchy(
                    this.cachedInstantCatchAuraBuoyCmdClass, "BuoyPos");
                this.cachedInstantCatchAuraBuoyFieldDirection = this.FindAuraMonoFieldOnHierarchy(
                    this.cachedInstantCatchAuraBuoyCmdClass, "Direction");
                this.cachedInstantCatchAuraBuoyFieldSuccessLength = this.FindAuraMonoFieldOnHierarchy(
                    this.cachedInstantCatchAuraBuoyCmdClass, "SuccessLength");
                this.cachedInstantCatchAuraBuoyFieldFailureLength = this.FindAuraMonoFieldOnHierarchy(
                    this.cachedInstantCatchAuraBuoyCmdClass, "FailureLength");
            }

            if (this.cachedInstantCatchAuraBuoyFieldBuoyNetId == IntPtr.Zero
                || this.cachedInstantCatchAuraBuoyFieldBuoyPos == IntPtr.Zero
                || this.cachedInstantCatchAuraBuoyFieldDirection == IntPtr.Zero
                || this.cachedInstantCatchAuraBuoyFieldSuccessLength == IntPtr.Zero
                || this.cachedInstantCatchAuraBuoyFieldFailureLength == IntPtr.Zero)
            {
                resolveStatus = "buoy cmd fields missing";
                this.LogInstantCatchBuoyResolveOnce(resolveStatus);
                return false;
            }

            this.LogInstantCatchBuoyResolveOnce("web+cmd+SendCommand inflated channel=Reliable("
                + InstantCatchAuraReliableChannel + ")");

            resolveStatus = "ok";
            return true;
        }

        private void LogInstantCatchBuoyResolveOnce(string detail)
        {
            if (this.instantCatchBuoyCommandResolveLogged)
            {
                return;
            }

            this.instantCatchBuoyCommandResolveLogged = true;
            this.InstantCatchLog("aura buoy resolve: " + detail, true);
        }

        // Sends UpdateRodBuoyPositionNetworkCommand on the Reliable channel via AuraMono WebRequestUtility.
        private unsafe bool TrySendBuoyUpdateReliable(uint floatNetId, Vector3 buoyPos, Vector3 direction, float successLength, float failureLength, out string status)
        {
            status = "reliable buoy unavailable";

            if (!this.TryEnsureInstantCatchAuraBuoySend(out string resolveStatus))
            {
                status = "reliable types unresolved (" + resolveStatus + ")";
                return false;
            }

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.cachedInstantCatchAuraBuoyCmdClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "reliable cmd alloc failed";
                return false;
            }

            auraMonoFieldSetValue(cmdObj, this.cachedInstantCatchAuraBuoyFieldBuoyNetId, (IntPtr)(&floatNetId));
            auraMonoFieldSetValue(cmdObj, this.cachedInstantCatchAuraBuoyFieldBuoyPos, (IntPtr)(&buoyPos));
            auraMonoFieldSetValue(cmdObj, this.cachedInstantCatchAuraBuoyFieldDirection, (IntPtr)(&direction));
            auraMonoFieldSetValue(cmdObj, this.cachedInstantCatchAuraBuoyFieldSuccessLength, (IntPtr)(&successLength));
            auraMonoFieldSetValue(cmdObj, this.cachedInstantCatchAuraBuoyFieldFailureLength, (IntPtr)(&failureLength));

            IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
            if (cmdPtr == IntPtr.Zero)
            {
                status = "reliable cmd unbox failed";
                return false;
            }

            int needAuthed = 1;
            int channel = InstantCatchAuraReliableChannel;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = cmdPtr;
            args[1] = (IntPtr)(&needAuthed);
            args[2] = (IntPtr)(&channel);

            IntPtr exc = IntPtr.Zero;
            IntPtr resultBoxed = auraMonoRuntimeInvoke(
                this.cachedInstantCatchAuraSendBuoyReliableMethod,
                IntPtr.Zero,
                (IntPtr)args,
                ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "reliable send exception";
                this.InstantCatchLog("reliable aura send exception ptr=0x" + exc.ToInt64().ToString("X"));
                return false;
            }

            int sendCode = -1;
            if (resultBoxed != IntPtr.Zero)
            {
                IntPtr raw = auraMonoObjectUnbox(resultBoxed);
                if (raw != IntPtr.Zero)
                {
                    sendCode = *(int*)raw;
                }
            }

            if (sendCode < 0)
            {
                status = "reliable send failed (" + sendCode + ")";
                return false;
            }

            status = "reliable sent";
            return true;
        }

        // ---- NotifyFloatInWater detour: rewrite the game's own successLength to -2 at the source ----
        // The game sends the REAL successLength exactly once, in PlayerStateFishing.OnStateEnter ->
        // FishingProtocolManager.NotifyFloatInWater(uint, Vector3, Vector3, float successLength, float).
        // We NativeDetour that Mono method (resolve via AuraMono + mono_compile_method, same pattern as
        // BuildingFreeRotateFeature) and trampoline-forward to the original with successLength forced to
        // -2 while Instant Catch is active. Vector3 args pass through untouched as raw pointers (Win64
        // passes 12-byte structs by reference). This kills the condition-1 race in the source so the
        // continuous -2 resend is no longer needed for it. buoyPos is NOT touched (rewriting it here =
        // cast-time = fish chases far = bite breaks; the far-anchor for condition 2 stays on our sends).
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void NotifyFloatInWaterHookDelegate(uint floatNetId, IntPtr buoyPos, IntPtr direction, float successLength, float failureLength);

        private delegate IntPtr InstantCatchMonoCompileMethodDelegate(IntPtr method);

        internal static bool instantCatchSuccessSpoofActive;

        // Spoofed successLength sent in ActivateRodBuoy / the buoy re-send. -2 (old exploit) now FAILS
        // the battle after the 2026-07-09 patch; a small POSITIVE value WINS (live-confirmed: +1.0 and
        // +0.01 → no misses incl. previously-failing fish, reels faster). Single knob for BOTH the
        // NotifyFloatInWater detour body and the periodic re-send TryArmFishingInstantCatch.
        private const float InstantCatchSpoofedSuccessLength = 0.01f;
        private static MonoMod.RuntimeDetour.NativeDetour instantCatchNotifyDetour;
        private static NotifyFloatInWaterHookDelegate instantCatchNotifyHookKeepAlive; // anti-GC
        private static NotifyFloatInWaterHookDelegate instantCatchNotifyTrampoline;
        private bool instantCatchNotifyHookTried;

        public void EnsureNotifyFloatInWaterHook()
        {
            if (this.instantCatchNotifyHookTried || instantCatchNotifyDetour != null)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry on a later frame.
                }

                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry later.
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "NotifyFloatInWater", 5);
                if (method == IntPtr.Zero)
                {
                    this.instantCatchNotifyHookTried = true;
                    this.InstantCatchLog("NotifyFloatInWater(5) not found on FishingProtocolManager", true);
                    return;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                InstantCatchMonoCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<InstantCatchMonoCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.instantCatchNotifyHookTried = true;
                    this.InstantCatchLog("mono_compile_method export unavailable for NotifyFloatInWater hook", true);
                    return;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.instantCatchNotifyHookTried = true;
                    this.InstantCatchLog("mono_compile_method returned null for NotifyFloatInWater", true);
                    return;
                }

                instantCatchNotifyHookKeepAlive = NotifyFloatInWaterDetourBody;
                instantCatchNotifyDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, instantCatchNotifyHookKeepAlive);
                instantCatchNotifyTrampoline = instantCatchNotifyDetour.GenerateTrampoline<NotifyFloatInWaterHookDelegate>();
                this.instantCatchNotifyHookTried = true;

                if (instantCatchNotifyTrampoline == null)
                {
                    // Without a way to call the original, the buoy would never activate — bail out safely.
                    instantCatchNotifyDetour.Undo();
                    instantCatchNotifyDetour = null;
                    instantCatchNotifyHookKeepAlive = null;
                    FeatureLog.Fail("InstantCatch", "NotifyFloatInWater trampoline unavailable; detour reverted");
                    return;
                }

                FeatureLog.Life("InstantCatch", "NotifyFloatInWater detour installed @0x" + nativePtr.ToInt64().ToString("X") + " — instant catch is live");
            }
            catch (Exception ex)
            {
                this.instantCatchNotifyHookTried = true; // don't crash-loop on a hard failure
                FeatureLog.Fail("InstantCatch", "NotifyFloatInWater hook failed: " + ex.Message);
            }
        }

        // Apply/Undo the detour with the Instant Catch toggle so it is fully removed when off (a bad
        // detour then can't affect normal fishing). Safe no-op until the detour has been created.
        internal static void SetInstantCatchNotifyHookApplied(bool on)
        {
            try
            {
                if (instantCatchNotifyDetour == null)
                {
                    return;
                }

                if (on && !instantCatchNotifyDetour.IsApplied)
                {
                    instantCatchNotifyDetour.Apply();
                }
                else if (!on && instantCatchNotifyDetour.IsApplied)
                {
                    instantCatchNotifyDetour.Undo();
                }
            }
            catch
            {
            }
        }

        // Native detour body. Must NOT throw across the boundary and does only a static-field read +
        // a forward call to the original (the trampoline) — no Mono/Il2Cpp/Unity calls here.
        private static void NotifyFloatInWaterDetourBody(uint floatNetId, IntPtr buoyPos, IntPtr direction, float successLength, float failureLength)
        {
            NotifyFloatInWaterHookDelegate orig = instantCatchNotifyTrampoline;
            if (orig == null)
            {
                return;
            }

            try
            {
                float spoofed = instantCatchSuccessSpoofActive ? InstantCatchSpoofedSuccessLength : successLength;
                orig(floatNetId, buoyPos, direction, spoofed, failureLength);
            }
            catch
            {
                try { orig(floatNetId, buoyPos, direction, successLength, failureLength); } catch { }
            }
        }

        // Skip the post-catch "take fish out of water" theater (~2.7s local timer, no network at its
        // end). FishingProtocolManager.TakeUpRod(uint) is the server-reset receive handler: it only
        // dispatches ResetFishState, whose listener in PlayerStateFishing does ResetFishingStateData()
        // + CrossFade(Idle) — the game's own instant-reset path. The uint arg is ignored by the body.
        // Safe outside fishing: the listener is only registered while in the Fishing FSM state.
        // ---- Dismount before casting ------------------------------------------------------------
        // Fishing is unreachable from a vehicle seat, and not because of one flag: FishingCommand
        // wants playerState == Free, GameFishingMode.EnterFishing THROWS unless
        // GameWorld.IsMode<GameFreeMode>(), StateCollection has no Vehicle->Fishing transition, and
        // TransitionVehicle2Free only fires once Status.VehicleStatus.VehicleNetId == 0 — i.e. once
        // the seat is really vacated, which is server state. So the only way in is the game's own
        // exit, and that is exactly what ExitVehicleStateTask does: one call to
        // VehicleProtocolManager.GetOffVehicle(vehicleNetId, reason), then wait for the server's
        // PlayerVehicleTurnOffEvent and for the state to leave Vehicle. We drive the same call and
        // poll the same field the game's own transition polls, so there is nothing to spoof.
        //
        // NON-BLOCKING: this reports "still dismounting" and the farm retries on a later tick.
        // Never a wait loop inside OnUpdate.
        private const float FishingDismountResendSeconds = 2f;
        private const float FishingDismountLogInterval = 5f;

        private uint fishingDismountVehicleNetId;
        private float fishingDismountResendAt;
        private float fishingDismountFirstAttemptAt;
        private float fishingDismountNextLogAt;

        // True while the player still occupies a vehicle seat — the caller must NOT cast yet.
        public bool IsFishingDismountPending(out string status)
        {
            status = null;

            if (!this.TryGetLocalPlayerVehicleNetIdMono(out uint vehicleNetId, out string readStatus))
            {
                // Cannot read the seat: do not block fishing on a failed probe. If we really are in
                // a vehicle the cast fails on its own with the EnterFishing throw, which is logged.
                return false;
            }

            if (vehicleNetId == 0u)
            {
                if (this.fishingDismountVehicleNetId != 0u)
                {
                    this.AutoFishLog("Dismount complete after "
                        + (Time.unscaledTime - this.fishingDismountFirstAttemptAt).ToString("F1")
                        + "s — casting is unblocked.");
                    this.fishingDismountVehicleNetId = 0u;
                    this.fishingDismountResendAt = -999f;
                    this.fishingDismountNextLogAt = -999f;
                }

                return false;
            }

            float now = Time.unscaledTime;
            bool newVehicle = this.fishingDismountVehicleNetId != vehicleNetId;
            if (newVehicle)
            {
                this.fishingDismountVehicleNetId = vehicleNetId;
                this.fishingDismountFirstAttemptAt = now;
                this.fishingDismountResendAt = -999f;
                this.fishingDismountNextLogAt = -999f;
            }

            // Resend rather than give up: GetOffVehicle can legitimately be refused for a while —
            // PlayerStateVehicle.GetExitInteractTask returns null while the driver's vehicle is
            // InTheAir — and it starts working by itself once the vehicle lands.
            if (now >= this.fishingDismountResendAt)
            {
                this.fishingDismountResendAt = now + FishingDismountResendSeconds;
                bool sent = this.TryInvokeVehicleGetOffMono(vehicleNetId, out string sendStatus);
                if (newVehicle || now >= this.fishingDismountNextLogAt)
                {
                    this.fishingDismountNextLogAt = now + FishingDismountLogInterval;
                    this.AutoFishLog("Dismount before cast: vehicle=" + vehicleNetId + " " + sendStatus);
                }

                if (!sent && newVehicle)
                {
                    // A resolver miss will not fix itself — say so once instead of stalling quietly.
                    FeatureLog.Fail("AutoFish", "cannot leave the vehicle before casting: " + sendStatus);
                }
            }

            status = "Leaving vehicle before cast ("
                + (now - this.fishingDismountFirstAttemptAt).ToString("F1") + "s)";
            return true;
        }

        // Status.VehicleStatus.vehicleNetId — the exact field TransitionVehicle2Free keys on, so
        // "we may fish" and "the game will let the FSM out of PlayerStateVehicle" are one read.
        // Prefers the private backing field over the property: reading a field off the boxed struct
        // is a plain read, whereas the property would need an unbox+pin invoke.
        public bool TryGetLocalPlayerVehicleNetIdMono(out uint vehicleNetId, out string status)
        {
            vehicleNetId = 0u;
            status = "Vehicle status mono unavailable";

            try
            {
                if (!this.TryGetFishingPlayerMonoObject(out IntPtr playerObj, out IntPtr _, out status)
                    || playerObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj)
                    && !this.TryGetMonoObjectMember(playerObj, "status", out statusObj)
                    && !this.TryGetMonoObjectMember(playerObj, "_status", out statusObj))
                {
                    status = "Mono player status unavailable";
                    return false;
                }

                if (statusObj == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(statusObj, "VehicleStatus", out IntPtr vehicleStatusObj)
                    || vehicleStatusObj == IntPtr.Zero)
                {
                    status = "Mono VehicleStatus unavailable";
                    return false;
                }

                if (!this.TryGetMonoIntMember(vehicleStatusObj, "vehicleNetId", out int raw)
                    && !this.TryGetMonoIntMember(vehicleStatusObj, "VehicleNetId", out raw))
                {
                    status = "Mono VehicleStatus.vehicleNetId unavailable";
                    return false;
                }

                vehicleNetId = unchecked((uint)raw);
                status = "OK";
                return true;
            }
            catch (Exception ex)
            {
                status = "Vehicle status mono failed: " + ex.Message;
                return false;
            }
        }

        // VehicleProtocolManager.GetOffVehicle(uint vehicleNetId, VehicleGetOffReason reason) — the
        // same call ExitVehicleStateTask makes. Two params in the metadata (the default argument is
        // compile-time only), reason = VehicleGetOffReason.Default = 0.
        public unsafe bool TryInvokeVehicleGetOffMono(uint vehicleNetId, out string status)
        {
            status = "GetOffVehicle Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "GetOffVehicle Mono runtime unavailable";
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
                if (classPtr == IntPtr.Zero)
                {
                    status = "VehicleProtocolManager Mono class unavailable";
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "GetOffVehicle", 2);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "VehicleProtocolManager.GetOffVehicle(2) Mono method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                uint netIdArg = vehicleNetId;
                int reasonArg = 0; // VehicleGetOffReason.Default
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&reasonArg);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "GetOffVehicle Mono exception";
                    this.AutoFishLog("GetOffVehicle Mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "GetOffVehicle sent";
                return true;
            }
            catch (Exception ex)
            {
                status = "GetOffVehicle Mono failed: " + ex.Message;
                this.AutoFishLog("GetOffVehicle Mono exception: " + ex.Message);
                return false;
            }
        }

        public unsafe bool TryInvokeFishingTakeUpRodMono(out string status)
        {
            status = "TakeUpRod Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "TakeUpRod Mono runtime unavailable";
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (classPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager Mono class unavailable";
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "TakeUpRod", 1);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager.TakeUpRod(1) Mono method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                uint playerNetId = 0u; // ignored by the body (it just dispatches ResetFishState)
                args[0] = (IntPtr)(&playerNetId);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "TakeUpRod Mono exception";
                    this.AutoFishLog("TakeUpRod Mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "TakeUpRod invoked (Mono)";
                return true;
            }
            catch (Exception ex)
            {
                status = "TakeUpRod Mono failed: " + ex.Message;
                this.AutoFishLog("TakeUpRod Mono exception: " + ex.Message);
                return false;
            }
        }

        // Skip the rod cast/throw animation. The Free→Fishing FSM transition only completes when the
        // throw action clip ends (IsLocomotion gate), and ONLY THEN does OnStateEnter send
        // ActivateRodBuoy — i.e. the cast animation gates the server-side buoy activation (~1.5-2s).
        //
        // v1 used ActorActionGraph.Stop() and BROKE fishing: the game only calls Stop() on player
        // despawn (teardown API) — killing the clip mid-cast strands the animator/buoy state.
        // v2 instead drives the clip to its OWN finish line early: PlayerFishThrowSuccessAction polls
        // IsAnimState(Fishing) to end, so CrossFade(Fishing, 0) makes it finish naturally (same
        // pattern the game uses in ResetFishingStateFromServer with IdleOne). The clip has a Pre
        // phase that first waits for SpinningRod — crossfading during Pre would strand it until its
        // 3s timeout, so we only fire when the animator is ALREADY in SpinningRod, and the caller
        // additionally requires two consecutive sightings so the clip itself has ticked into its
        // Playing phase before we pull the state out from under it.
        private static int skipCastSpinningRodHash;
        private static int skipCastFishingHash;

        public unsafe bool TrySkipFishingCastAnimMono(bool allowCrossfade, out bool inSpinningRod, out string status)
        {
            inSpinningRod = false;
            status = "cast-skip Mono unavailable";

            try
            {
                if (skipCastSpinningRodHash == 0)
                {
                    skipCastSpinningRodHash = Animator.StringToHash("SpinningRod");
                    skipCastFishingHash = Animator.StringToHash("Fishing");
                }

                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null
                    || auraMonoObjectGetClass == null
                    || auraMonoObjectUnbox == null)
                {
                    status = "cast-skip Mono runtime unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                {
                    status = "cast-skip interact unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "cast-skip player unavailable";
                    return false;
                }

                IntPtr playerClass = auraMonoObjectGetClass(playerObj);
                IntPtr getAnimMethod = playerClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(playerClass, "get_animationComponent", 0) : IntPtr.Zero;
                if (getAnimMethod == IntPtr.Zero)
                {
                    status = "get_animationComponent unavailable";
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr animObj = auraMonoRuntimeInvoke(getAnimMethod, playerObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || animObj == IntPtr.Zero)
                {
                    status = "animationComponent instance unavailable";
                    return false;
                }

                IntPtr animClass = auraMonoObjectGetClass(animObj);
                IntPtr isAnimStateMethod = animClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(animClass, "IsAnimState", 1) : IntPtr.Zero;
                if (isAnimStateMethod == IntPtr.Zero)
                {
                    status = "IsAnimState(1) unavailable";
                    return false;
                }

                int spinHash = skipCastSpinningRodHash;
                IntPtr* checkArgs = stackalloc IntPtr[1];
                checkArgs[0] = (IntPtr)(&spinHash);
                exc = IntPtr.Zero;
                IntPtr boolBoxed = auraMonoRuntimeInvoke(isAnimStateMethod, animObj, (IntPtr)checkArgs, ref exc);
                if (exc != IntPtr.Zero || boolBoxed == IntPtr.Zero)
                {
                    status = "IsAnimState invoke failed";
                    return false;
                }

                inSpinningRod = *(byte*)auraMonoObjectUnbox(boolBoxed) != 0;
                if (!inSpinningRod)
                {
                    status = "not in SpinningRod yet";
                    return false;
                }

                if (!allowCrossfade)
                {
                    status = "SpinningRod seen; confirming";
                    return false;
                }

                IntPtr crossFadeMethod = this.FindAuraMonoMethodOnHierarchy(animClass, "CrossFade", 3);
                if (crossFadeMethod == IntPtr.Zero)
                {
                    status = "CrossFade(3) unavailable";
                    return false;
                }

                int fishHash = skipCastFishingHash;
                float duration = 0f;
                int layerIndex = -1;
                IntPtr* fadeArgs = stackalloc IntPtr[3];
                fadeArgs[0] = (IntPtr)(&fishHash);
                fadeArgs[1] = (IntPtr)(&duration);
                fadeArgs[2] = (IntPtr)(&layerIndex);
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(crossFadeMethod, animObj, (IntPtr)fadeArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "CrossFade exception";
                    this.AutoFishLog("cast-skip CrossFade Mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "CrossFade(Fishing) invoked (Mono)";
                return true;
            }
            catch (Exception ex)
            {
                status = "cast-skip Mono failed: " + ex.Message;
                this.AutoFishLog("cast-skip Mono exception: " + ex.Message);
                return false;
            }
        }

        public unsafe bool TryArmFishingInstantCatch(out string status)
        {
            status = "Instant catch unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "Instant catch mono runtime unavailable";
                    this.InstantCatchLog("resolve: mono runtime unavailable (EnsureAuraMonoApiReady/AttachAuraMonoThread/invoke ptr).");
                    return false;
                }

                // Read the active buoy netId off the self player. On the self player only floatNetId
                // is reliable in PlayerFloatData; direction/basePosition are NOT mirrored locally.
                if (!this.TryReadFishingFloatData(out uint floatNetId, out _, out _, out _, out string floatStatus))
                {
                    status = "Instant catch float data unavailable: " + floatStatus;
                    this.InstantCatchLog("floatData: " + floatStatus);
                    return false;
                }

                if (floatNetId == 0U)
                {
                    status = "Instant catch buoy not active yet (floatNetId=0)";
                    this.InstantCatchLog("floatData: buoy not active yet (floatNetId=0); " + floatStatus);
                    return false;
                }

                // Echo the buoy's real world position (HandHoldFishingRod.GetFloatPosition).
                Vector3 buoyPos;
                string buoySource;
                if (this.TryGetFishingBuoyPositionMono(out buoyPos, out string buoyStatus) && buoyPos != Vector3.zero)
                {
                    buoySource = "real-buoy GetFloatPosition";
                }
                else if (this.TryGetEntityPositionByNetId(floatNetId, out buoyPos) && buoyPos != Vector3.zero)
                {
                    buoySource = "buoy-entity netId=" + floatNetId + " (GetFloatPosition failed: " + buoyStatus + ")";
                }
                else
                {
                    status = "Instant catch buoy position unavailable: " + buoyStatus;
                    this.InstantCatchLog("buoyPos: " + buoyStatus + " (entity fallback also failed)");
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos) || playerPos == Vector3.zero)
                {
                    playerPos = buoyPos;
                }

                // "Far anchor at activation": during the brief landing window, replace the reported buoy
                // with a point far beyond it (away from the player) so the server's latched battle anchor
                // is far. Outside the window we report the real buoy (so the fish still bites normally).
                float sinceCastFar = this.InstantCatchCastAt > 0f ? Time.unscaledTime - this.InstantCatchCastAt : -1f;
                // TEMPORARILY DISABLED: testing whether the NotifyFloatInWater detour alone (successLength=-2
                // at source) handles "fighting" fish without the far-anchor condition-2 trick. Flip back to
                // the window check to re-enable.
                const bool InstantCatchFarAnchorEnabled = false;
                if (InstantCatchFarAnchorEnabled && sinceCastFar >= InstantCatchFarWinStart && sinceCastFar <= InstantCatchFarWinEnd)
                {
                    Vector3 flat = new Vector3(buoyPos.x - playerPos.x, 0f, buoyPos.z - playerPos.z);
                    float flatMag = flat.magnitude;
                    Vector3 heading;
                    if (flatMag > 0.05f)
                    {
                        heading = flat / flatMag;
                    }
                    else
                    {
                        GameObject pr = HeartopiaComplete.GetLocalPlayer();
                        if (pr == null)
                        {
                            pr = this.FindPlayerRoot();
                        }
                        heading = pr != null ? pr.transform.forward : Vector3.forward;
                        heading.y = 0f;
                        heading = heading.sqrMagnitude < 0.0004f ? Vector3.forward : heading.normalized;
                    }

                    Vector3 farBuoy = playerPos + heading * (flatMag + InstantCatchFarAnchorDist);
                    farBuoy.y = buoyPos.y;
                    buoyPos = farBuoy;
                    buoySource += " [FAR-anchor]";

                    if (this.instantCatchFarLogSeq != this.InstantCatchCastSeq)
                    {
                        this.instantCatchFarLogSeq = this.InstantCatchCastSeq;
                        this.InstantCatchDiag("cast#" + this.InstantCatchCastSeq + " FAR-anchor window @t=" + sinceCastFar.ToString("F2")
                            + "s buoy-> " + buoyPos + " (dist " + (flatMag + InstantCatchFarAnchorDist).ToString("F0") + "m)");
                    }
                }

                Vector3 direction = playerPos - buoyPos;
                if (direction.sqrMagnitude < 0.0004f)
                {
                    GameObject playerRoot = HeartopiaComplete.GetLocalPlayer();
                    if (playerRoot == null)
                    {
                        playerRoot = this.FindPlayerRoot();
                    }
                    direction = playerRoot != null ? playerRoot.transform.forward : Vector3.forward;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.0004f)
                    {
                        direction = Vector3.forward;
                    }
                }

                if (!this.fishingInstantCatchResolveLogged)
                {
                    this.fishingInstantCatchResolveLogged = true;
                    this.InstantCatchLog("resolve complete: first floatData " + floatStatus, true);
                }

                const float collapsedSuccessLength = InstantCatchSpoofedSuccessLength;
                const float spoofedFailureLength = 30f;

                if (!IsFiniteVector(buoyPos) || !IsFiniteVector(direction))
                {
                    status = "Instant catch aborted: non-finite geometry buoy=" + buoyPos + " dir=" + direction;
                    this.InstantCatchLog(status);
                    return false;
                }

                if (!this.TrySendBuoyUpdateReliable(floatNetId, buoyPos, direction, collapsedSuccessLength, spoofedFailureLength, out string channelStatus))
                {
                    status = "Instant catch buoy send failed: " + channelStatus;
                    return false;
                }

                buoySource += " [reliable]";

                float buoyFlatDist = new Vector2(buoyPos.x - playerPos.x, buoyPos.z - playerPos.z).magnitude;
                float now = Time.unscaledTime;
                status = $"InstantCatch buoyNetId={floatNetId} success={collapsedSuccessLength:F2} fail={spoofedFailureLength:F1}";
                if (now >= this.nextInstantCatchSendLogAt)
                {
                    this.nextInstantCatchSendLogAt = now + 1f;
                    float sinceCast = this.InstantCatchCastAt > 0f ? now - this.InstantCatchCastAt : -1f;
                    this.InstantCatchLog("cast#" + this.InstantCatchCastSeq + " t=" + sinceCast.ToString("F2") + "s"
                        + " sent buoy update buoyNetId=" + floatNetId
                        + " src=" + buoySource
                        + " buoyPos=" + buoyPos + " playerPos=" + playerPos
                        + " buoyDist=" + buoyFlatDist.ToString("F1") + "m dir=" + direction
                        + " success=" + collapsedSuccessLength.ToString("F2")
                        + " fail=" + spoofedFailureLength.ToString("F1"), true);
                }

                return true;
            }
            catch (Exception ex)
            {
                status = "Instant catch failed: " + ex.Message;
                this.InstantCatchLog("exception: " + ex.Message);
                return false;
            }
        }

        // Reads PlayerFloatData off the local fishing player via AuraMono. _floatData is a struct
        // field on PlayerDataComponent; TryGetMonoObjectMember boxes it, so the value-type fields
        // read back through the standard helpers without manual offset math.
        private bool TryReadFishingFloatData(out uint floatNetId, out Vector3 direction, out float failLength, out Vector3 basePos, out string status)
        {
            return this.TryReadFishingFloatData(out floatNetId, out direction, out failLength, out basePos,
                out bool _, out float _, out status);
        }

        // `available` and `successLength` are what the SERVER last told us about the buoy
        // (CmdAddRodBuoy / CmdUpdateRodBuoyData write them into PlayerFloatData), which makes them
        // the only honest read of whether the server considers the buoy live and where it thinks it
        // is. Server-Side Fishing uses them as its post-activation verdict.
        private bool TryReadFishingFloatData(out uint floatNetId, out Vector3 direction, out float failLength, out Vector3 basePos, out bool available, out float successLength, out string status)
        {
            floatNetId = 0U;
            direction = Vector3.zero;
            failLength = 0f;
            basePos = Vector3.zero;
            available = false;
            successLength = 0f;
            status = "player unavailable";

            IntPtr playerObj = IntPtr.Zero;
            if (!this.TryGetFishingPlayerMonoObject(out playerObj, out _, out _) || playerObj == IntPtr.Zero)
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out playerObj) || playerObj == IntPtr.Zero)
                {
                    status = "player object unavailable (fishing + local fallback both null)";
                    return false;
                }
            }

            // BasePlayerComponent.dataComponent { get; private set; } -> resolves via get_dataComponent.
            if (!this.TryGetMonoObjectMember(playerObj, "dataComponent", out IntPtr dataComponentObj) || dataComponentObj == IntPtr.Zero)
            {
                status = "dataComponent member unavailable on " + this.GetAuraMonoClassDisplayName(
                    auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerObj) : IntPtr.Zero);
                return false;
            }

            // PlayerDataComponent._floatData (boxed struct) -> PlayerFloatData.
            if (!this.TryGetMonoObjectMember(dataComponentObj, "_floatData", out IntPtr floatDataObj) || floatDataObj == IntPtr.Zero)
            {
                status = "_floatData field unavailable on " + this.GetAuraMonoClassDisplayName(
                    auraMonoObjectGetClass != null ? auraMonoObjectGetClass(dataComponentObj) : IntPtr.Zero);
                return false;
            }

            this.TryGetMonoUInt32Member(floatDataObj, "floatNetId", out floatNetId);
            this.TryGetMonoVector3Member(floatDataObj, "direction", out direction);
            this.TryGetMonoSingleMember(floatDataObj, "failLength", out failLength);
            this.TryGetMonoVector3Member(floatDataObj, "basePosition", out basePos);
            this.TryGetMonoBoolMember(floatDataObj, "available", out available);
            this.TryGetMonoSingleMember(floatDataObj, "successLength", out successLength);
            status = "ok netId=" + floatNetId + " dir=" + direction + " fail=" + failLength.ToString("F1") + " base=" + basePos;
            return true;
        }

        private unsafe bool TrySetFishingStateButtonPressedMono(bool pressed, out string status)
        {
            status = "Fishing state button mono unavailable";

            try
            {
                if (!this.TryGetFishingPlayerMonoObject(out IntPtr playerObj, out _, out status) || playerObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr playerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerObj) : IntPtr.Zero;
                if (playerClass == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    status = "Fishing state button player class unavailable";
                    return false;
                }

                IntPtr getCurrentStateMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "GetCurrentState", 0);
                if (getCurrentStateMethod == IntPtr.Zero)
                {
                    status = "GetCurrentState mono method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr stateObj = auraMonoRuntimeInvoke(getCurrentStateMethod, playerObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || stateObj == IntPtr.Zero)
                {
                    status = "Current fishing state unavailable";
                    return false;
                }

                IntPtr stateClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(stateObj) : IntPtr.Zero;
                string stateClassName = this.GetAuraMonoClassDisplayName(stateClass);
                if (stateClass == IntPtr.Zero || stateClassName.IndexOf("PlayerStateFishing", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    status = string.IsNullOrWhiteSpace(stateClassName)
                        ? "Current state is not fishing"
                        : "Current state is " + stateClassName;
                    return false;
                }

                IntPtr setPressedMethod = this.FindAuraMonoMethodOnHierarchy(stateClass, "SetStateButtonPressed", 1);
                if (setPressedMethod == IntPtr.Zero)
                {
                    setPressedMethod = this.FindAuraMonoMethodOnHierarchy(stateClass, "OnMainInteraction", 1);
                }

                if (setPressedMethod == IntPtr.Zero)
                {
                    status = "Fishing state SetStateButtonPressed unavailable";
                    return false;
                }

                bool pressedValue = pressed;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&pressedValue);
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(setPressedMethod, stateObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Fishing state button mono exception";
                    this.AutoFishLog("Fishing state button mono raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "Pressed updated (Mono player state)";
                return true;
            }
            catch (Exception ex)
            {
                status = "Fishing state button mono failed: " + ex.Message;
                this.AutoFishLog("Fishing state button mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryGetFishingMotionMonoState(out string fishState, out float pullStrength, out float rodDurability, out string status)
        {
            fishState = string.Empty;
            pullStrength = -1f;
            rodDurability = -1f;
            status = "Fishing motion mono unavailable";

            try
            {
                if (!this.TryGetFishingPlayerMonoObject(out IntPtr playerObj, out _, out status))
                {
                    return false;
                }

                IntPtr actionGraphObj = IntPtr.Zero;
                if (!this.TryGetMonoObjectMember(playerObj, "actionGraph", out actionGraphObj)
                    && !this.TryGetMonoObjectMember(playerObj, "ActionGraph", out actionGraphObj)
                    && !this.TryGetMonoObjectMember(playerObj, "_actionGraph", out actionGraphObj))
                {
                    status = "Fishing motion actionGraph unavailable";
                    return false;
                }

                if (actionGraphObj == IntPtr.Zero)
                {
                    status = "Fishing motion actionGraph unavailable";
                    return false;
                }

                IntPtr motionClipObj = IntPtr.Zero;
                if (!this.TryGetMonoObjectMember(actionGraphObj, "motionClip", out motionClipObj)
                    && !this.TryGetMonoObjectMember(actionGraphObj, "MotionClip", out motionClipObj)
                    && !this.TryGetMonoObjectMember(actionGraphObj, "_motionClip", out motionClipObj))
                {
                    status = "Fishing motion clip unavailable";
                    return false;
                }

                if (motionClipObj == IntPtr.Zero)
                {
                    status = "Fishing motion clip unavailable";
                    return false;
                }

                if (this.TryGetMonoInt32Member(motionClipObj, "_subState", out int motionStateValue)
                    || this.TryGetMonoInt32Member(motionClipObj, "subState", out motionStateValue)
                    || this.TryGetMonoIntMember(motionClipObj, "_subState", out motionStateValue)
                    || this.TryGetMonoIntMember(motionClipObj, "subState", out motionStateValue))
                {
                    fishState = this.DescribeFishingSubState(motionStateValue);
                }

                if (!this.TryGetMonoSingleMember(motionClipObj, "_pullStrength", out pullStrength))
                {
                    this.TryGetMonoSingleMember(motionClipObj, "pullStrength", out pullStrength);
                }

                if (!this.TryGetMonoSingleMember(motionClipObj, "_rodDurability", out rodDurability))
                {
                    this.TryGetMonoSingleMember(motionClipObj, "rodDurability", out rodDurability);
                }

                status = "OK";
                return !string.IsNullOrWhiteSpace(fishState) || pullStrength >= 0f || rodDurability >= 0f;
            }
            catch (Exception ex)
            {
                status = "Fishing motion mono failed: " + ex.Message;
                this.AutoFishLog("Fishing motion mono exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryGetFishingStatusMonoObject(out IntPtr fishingStatusObj, out IntPtr fishingModeObj, out string status)
        {
            fishingStatusObj = IntPtr.Zero;
            fishingModeObj = IntPtr.Zero;
            status = "Fishing status mono runtime unavailable";

            if (!this.TryGetFishingPlayerMonoObject(out IntPtr playerObj, out fishingModeObj, out status))
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj) && !this.TryGetMonoObjectMember(playerObj, "status", out statusObj) && !this.TryGetMonoObjectMember(playerObj, "_status", out statusObj))
            {
                status = "Mono player status unavailable";
                return false;
            }

            if (statusObj == IntPtr.Zero)
            {
                status = "Mono player status unavailable";
                return false;
            }

            if (!this.TryGetMonoObjectMember(statusObj, "FishingStatus", out fishingStatusObj) && !this.TryGetMonoObjectMember(statusObj, "fishingStatus", out fishingStatusObj))
            {
                status = "Mono FishingStatus unavailable";
                return false;
            }

            if (fishingStatusObj == IntPtr.Zero)
            {
                status = "Mono FishingStatus unavailable";
                return false;
            }

            status = "OK";
            return true;
        }

        private unsafe bool TryGetFishingPlayerMonoObject(out IntPtr playerObj, out IntPtr fishingModeObj, out string status)
        {
            playerObj = IntPtr.Zero;
            fishingModeObj = IntPtr.Zero;
            status = "Fishing player mono unavailable";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr gameplayApiClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.GameplayApi");
            if (gameplayApiClass == IntPtr.Zero)
            {
                status = "GameplayApi Mono class unavailable";
                return false;
            }

            IntPtr getFishingModeMethod = this.FindAuraMonoMethodOnHierarchy(gameplayApiClass, "get_fishingMode", 0);
            if (getFishingModeMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                fishingModeObj = auraMonoRuntimeInvoke(getFishingModeMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    fishingModeObj = IntPtr.Zero;
                }
            }

            if (fishingModeObj != IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(fishingModeObj, "Player", out playerObj);
                if (playerObj == IntPtr.Zero)
                {
                    this.TryGetMonoObjectMember(fishingModeObj, "player", out playerObj);
                }
                if (playerObj == IntPtr.Zero)
                {
                    this.TryGetMonoObjectMember(fishingModeObj, "_player", out playerObj);
                }
            }

            if (playerObj == IntPtr.Zero)
            {
                IntPtr getCharacterMethod = this.FindAuraMonoMethodOnHierarchy(gameplayApiClass, "get_character", 0);
                if (getCharacterMethod != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && characterObj != IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(characterObj, "player", out playerObj);
                        if (playerObj == IntPtr.Zero)
                        {
                            this.TryGetMonoObjectMember(characterObj, "Player", out playerObj);
                        }
                        if (playerObj == IntPtr.Zero)
                        {
                            this.TryGetMonoObjectMember(characterObj, "_player", out playerObj);
                        }
                    }
                }
            }

            if (playerObj == IntPtr.Zero)
            {
                status = "Mono player unavailable";
                return false;
            }

            status = "OK";
            return true;
        }

        private string DescribeFishingSubState(int fishState)
        {
            switch (fishState)
            {
                case 0: return "Idle";
                case 1: return "Waiting";
                case 2: return "Battle";
                case 3: return "FishingFail";
                case 4: return "BattleFailSlack";
                case 5: return "FishingOnHook";
                default: return fishState < 0 ? string.Empty : "State" + fishState.ToString();
            }
        }

        private Il2CppType TryGetFishingAutomationIl2CppType(params string[] typeNames)
        {
            if (typeNames == null)
            {
                return null;
            }

            string[] assemblies = new string[]
            {
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll",
                "EcsClient",
                "EcsClient.dll",
                "EcsSystem",
                "EcsSystem.dll",
                "Client",
                "Client.dll",
                "Assembly-CSharp",
                "Assembly-CSharp.dll"
            };

            foreach (string typeName in typeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                try
                {
                    Il2CppType direct = Il2CppType.GetType(typeName);
                    if (direct != null)
                    {
                        return direct;
                    }
                }
                catch
                {
                }

                foreach (string assemblyName in assemblies)
                {
                    try
                    {
                        Il2CppType qualified = Il2CppType.GetType(typeName + ", " + assemblyName);
                        if (qualified != null)
                        {
                            return qualified;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private bool ShouldTrackFishShadowObject(string lowerName)
        {
            if (string.IsNullOrEmpty(lowerName) || !lowerName.EndsWith("(clone)", StringComparison.Ordinal))
            {
                return false;
            }

            return lowerName.StartsWith("p_fishshadow", StringComparison.Ordinal)
                || lowerName.Contains("fishshadow")
                || lowerName.Contains("fish_shadow")
                || (lowerName.Contains("fish") && lowerName.Contains("shadow"));
        }

        private bool ShouldTrackFishShadowObject(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy || string.IsNullOrEmpty(obj.name))
            {
                return false;
            }

            string lowerName = obj.name.ToLowerInvariant();
            if (!this.ShouldTrackFishShadowObject(lowerName) && !this.HasFishShadowRuntimeComponent(obj))
            {
                return false;
            }

            if (this.HasTrackedFishShadowAncestor(obj))
            {
                return false;
            }

            string hierarchyPath = this.GetHierarchyPath(obj.transform).ToLowerInvariant();
            string[] displayKeywords = new string[]
            {
                "display",
                "showcase",
                "tank",
                "fish tank",
                "fishtank",
                "aquarium",
                "fishbowl",
                "vivarium",
                // Sea Micro Home eco-aquarium (2026-07-09 update): its fish carry the same runtime
                // fish components as wild shadows, so only the hierarchy names exclude them.
                "seamicro",
                "microhome",
                "micro_home",
                "homeitem",
                "home_item",
                "houseitem",
                "house_item",
                "furniture",
                "ornament",
                "decoration",
                "decor",
                "placement",
                "placed"
            };

            foreach (string keyword in displayKeywords)
            {
                if (hierarchyPath.Contains(keyword))
                {
                    return false;
                }
            }

            for (Transform current = obj.transform; current != null; current = current.parent)
            {
                GameObject currentObject = current.gameObject;
                if (currentObject == null)
                {
                    continue;
                }

                string currentName = string.IsNullOrEmpty(currentObject.name) ? string.Empty : currentObject.name.ToLowerInvariant();
                if (currentName.Contains("tank")
                    || currentName.Contains("fishtank")
                    || currentName.Contains("aquarium")
                    || currentName.Contains("fishbowl")
                    || currentName.Contains("vivarium")
                    || currentName.Contains("seamicro")
                    || currentName.Contains("microhome"))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasTrackedFishShadowAncestor(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            for (Transform current = obj.transform.parent; current != null; current = current.parent)
            {
                GameObject currentObject = current.gameObject;
                if (currentObject == null || string.IsNullOrEmpty(currentObject.name))
                {
                    continue;
                }

                string lowerName = currentObject.name.ToLowerInvariant();
                if (this.ShouldTrackFishShadowObject(lowerName) || this.HasFishShadowRuntimeComponent(currentObject))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasFishShadowRuntimeComponent(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            try
            {
                foreach (Component component in obj.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        continue;
                    }

                    string typeName = null;
                    try
                    {
                        typeName = component.GetIl2CppType()?.FullName?.ToString();
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrEmpty(typeName))
                    {
                        typeName = component.GetType().FullName;
                    }

                    if (string.IsNullOrEmpty(typeName))
                    {
                        continue;
                    }

                    if (typeName == "XDTLevelAndEntity.Gameplay.Component.Fish.FishShadowResHandle"
                        || typeName == "XDTLevelAndEntity.Gameplay.Component.Fish.FishComponent"
                        || typeName.EndsWith(".FishShadowComponent", StringComparison.Ordinal)
                        || typeName.EndsWith(".FishShadowResHandle", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public bool IsFishingAutomationWorldReady()
        {
            try
            {
                GameObject loginPanel = GameObject.Find(LOGIN_PANEL_PATH);
                if (loginPanel != null && loginPanel.activeInHierarchy)
                {
                    return false;
                }

                GameObject loginRoomPanel = GameObject.Find(LOGIN_ROOM_PANEL_PATH);
                if (loginRoomPanel != null && loginRoomPanel.activeInHierarchy)
                {
                    return false;
                }

                GameObject player = GetPlayer();
                if (player == null || !player.activeInHierarchy)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StopAllAutoFishing()
        {
            // Route first: its Stop restores the user's pre-route settings (range/toggles)
            // before the engine itself is force-stopped below.
            FishingRouteFeature.ForceStop(this);
            AutoFishingFarm.ForceStop(this);
            this.showFishShadowRadar = false;
        }

    }
}
