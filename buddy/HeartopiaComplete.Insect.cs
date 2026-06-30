﻿using HarmonyLib;
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
        public List<Vector3> GetTrackedInsectPositions()
        {
            List<Vector3> res = new List<Vector3>();
            foreach (KeyValuePair<int, GameObject> kv in this.trackedObjectMarkers)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                GameObject target = null;
                foreach (KeyValuePair<GameObject, GameObject> mapping in this.markerToTarget)
                {
                    if (mapping.Key != null && mapping.Key.name == kv.Value.name)
                    {
                        target = mapping.Value;
                        break;
                    }
                }

                if (!this.ShouldTrackInsectObject(target))
                {
                    continue;
                }

                try
                {
                    res.Add(target.transform.position);
                }
                catch
                {
                }
            }
            return res;
        }

        public bool TryNetCatchNearbyInsects(float scanRange, int batchSize, out int detectedCount, out int resolvedCount, out int sentCount, out string status)
        {
            detectedCount = 0;
            resolvedCount = 0;
            sentCount = 0;
            status = "Idle";
            this.lastInsectFarmSentNetIds.Clear();

            try
            {
                GameObject player = this.GetPlayerObject();
                Vector3 playerPos = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

                List<uint> ids = new List<uint>();
                List<Vector3> positions = new List<Vector3>();
                string managerStatus = "Manager path not attempted";
                if (this.TryCollectCatchTargetsViaSweepNetManager(player, playerPos, scanRange, batchSize, ids, positions, out managerStatus))
                {
                    detectedCount = ids.Count;
                    this.InsectFarmNetLog($"Manager path produced {detectedCount} target(s) without radar dependency.");
                    this.InsectFarmNetLog("Using sweep-net manager targets.");
                }
                else
                {
                    this.InsectFarmNetLog("Sweep-net manager path failed: " + managerStatus);
                    status = managerStatus;
                    return false;
                }

                resolvedCount = ids.Count;
                if (ids.Count == 0)
                {
                    status = string.IsNullOrEmpty(managerStatus) ? "No insect netIds resolved" : managerStatus;
                    return false;
                }

                if (!this.TryInvokeInsectCatchProtocol(ids, positions, out sentCount, out string protocolStatus))
                {
                    status = protocolStatus;
                    return false;
                }

                status = $"Caught {sentCount}/{resolvedCount}";
                return true;
            }
            catch (Exception ex)
            {
                status = "Exception: " + ex.Message;
                this.InsectFarmNetLog("TryNetCatchNearbyInsects exception: " + ex);
                return false;
            }
        }

        public bool TryScanNearbyInsectTargets(float scanRange, int batchSize, out int detectedCount, out List<Vector3> positions, out string status)
        {
            detectedCount = 0;
            positions = new List<Vector3>();
            status = "Idle";

            try
            {
                GameObject player = this.GetPlayerObject();
                Vector3 playerPos = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
                List<uint> ids = new List<uint>();
                if (!this.TryCollectCatchTargetsViaSweepNetManager(player, playerPos, scanRange, batchSize, ids, positions, out status))
                {
                    detectedCount = ids.Count;
                    return false;
                }

                detectedCount = ids.Count;
                return ids.Count > 0 && positions.Count > 0;
            }
            catch (Exception ex)
            {
                status = "Scan exception: " + ex.Message;
                this.InsectFarmNetLog("TryScanNearbyInsectTargets exception: " + ex);
                return false;
            }
        }

        public bool TryGetLoadedInsectTargets(out int detectedCount, out List<uint> ids, out List<Vector3> positions, out string status)
        {
            detectedCount = 0;
            ids = new List<uint>();
            positions = new List<Vector3>();
            status = "Idle";

            try
            {
                string componentsStatus;
                if (this.TryGetLoadedInsectTargetsViaGetComponents(ids, positions, out componentsStatus))
                {
                    detectedCount = ids.Count;
                    status = componentsStatus;
                    return detectedCount > 0;
                }

                string auraStatus;
                if (this.TryGetLoadedInsectTargetsAuraMono(ids, positions, out auraStatus))
                {
                    detectedCount = ids.Count;
                    status = auraStatus;
                    return detectedCount > 0;
                }

                object moduleObj = null;
                Type moduleType = this.FindLoadedType(
                    "XDTLevelAndEntity.GameplaySystem.Insect.LevelInscetManager",
                    "LevelInscetManager");
                if (moduleType != null)
                {
                    this.TryGetManagedModule(moduleType, out moduleObj);
                }

                if (moduleObj == null)
                {
                    if (this.TryGetManagedInteractSystemObject(out object interactSystem, out string interactSource))
                    {
                        object playerObj = null;
                        string playerSource = string.Empty;
                        if (this.TryGetManagedSelfPlayerObject(out playerObj, out playerSource) || this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out playerSource))
                        {
                            if (this.TryGetManagedSweepNetManagerObject(interactSystem, playerObj, out object managerObj, out string managerSource) && managerObj != null)
                            {
                                moduleObj = managerObj;
                                this.InsectFarmNetLog($"Loaded insect scan using active sweep-net manager fallback. interact={interactSource} player={playerSource} manager={managerSource}");
                            }
                        }
                    }
                }

                if (moduleObj == null)
                {
                    status = moduleType == null ? "LevelInscetManager type unavailable" : "LevelInscetManager module unavailable";
                    return false;
                }

                if (!(this.TryGetObjectMember(moduleObj, "insects", out object insectsObj) || this.TryGetObjectMember(moduleObj, "_insects", out insectsObj)) || insectsObj == null)
                {
                    status = "LevelInscetManager.insects unavailable";
                    return false;
                }

                if (!(insectsObj is System.Collections.IEnumerable insects))
                {
                    status = "LevelInscetManager.insects unreadable";
                    return false;
                }

                HashSet<uint> seen = new HashSet<uint>();
                int inspected = 0;
                foreach (object insect in insects)
                {
                    if (insect == null)
                    {
                        continue;
                    }

                    inspected++;
                    if (!this.TryResolveNetIdFromManagedObject(insect, out uint netId, out string netSource) || netId == 0U)
                    {
                        continue;
                    }

                    if (!seen.Add(netId))
                    {
                        continue;
                    }

                    if (!this.TryResolvePositionFromManagedObject(insect, out Vector3 position) || position == Vector3.zero)
                    {
                        if (!this.TryGetEntityPositionByNetId(netId, out position) && !this.TryGetEntityPositionByNetIdMono(netId, out position))
                        {
                            continue;
                        }
                    }

                    ids.Add(netId);
                    positions.Add(position);
                    this.InsectFarmNetLog($"Loaded insect target netId={netId} via {netSource} pos={position}");
                }

                detectedCount = ids.Count;
                status = detectedCount > 0
                    ? $"Loaded insect targets ready ({detectedCount}/{inspected})"
                    : (inspected > 0 ? "No loaded insect targets resolved" : "Loaded insect list empty");
                this.InsectFarmNetLog("Loaded insect scan: inspected=" + inspected + " resolved=" + detectedCount + " status=" + status);
                return detectedCount > 0;
            }
            catch (Exception ex)
            {
                status = "Loaded insect scan exception: " + ex.Message;
                this.InsectFarmNetLog("TryGetLoadedInsectTargets exception: " + ex);
                return false;
            }
        }

        // Primary loaded-insect source: enumerate InsectComponent (a ViewComponent) directly via
        // Entities.GetComponents<T>. Unlike the manager chains below, this does NOT depend on the
        // sweep net being equipped (the `_insectManager.insects` path only exists while the net is
        // held), so the teleport farm can find insects to hop to even before/without the net. No
        // entity-graph walk involved. Falls through to the manager chains if the query yields nothing
        // or is unavailable on this build.
        private bool TryGetLoadedInsectTargetsViaGetComponents(List<uint> ids, List<Vector3> positions, out string status)
        {
            status = "Insect GetComponents unavailable";
            ids.Clear();
            positions.Clear();

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                return false;
            }

            IntPtr insectClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Insect.InsectComponent");
            if (insectClass == IntPtr.Zero)
            {
                insectClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.Insect.InsectComponent");
            }

            if (insectClass == IntPtr.Zero)
            {
                status = "InsectComponent class unavailable";
                return false;
            }

            // Pin components + each derived entity across the field reads (moving sgen GC stale-pointer
            // guard): an unpinned component/entity relocated mid-loop -> bad read -> heap corruption.
            List<uint> insectPins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(insectClass, out List<IntPtr> insectComponents, insectPins) || insectComponents == null)
            {
                FreeAuraMonoPins(insectPins);
                status = "No loaded insects (GetComponents)";
                return false;
            }

            HashSet<uint> seen = new HashSet<uint>();
            try
            {
                for (int i = 0; i < insectComponents.Count; i++)
                {
                    IntPtr componentObj = insectComponents[i];
                    if (componentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Owner insect entity is the component's back-reference.
                    IntPtr entityObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(componentObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                    {
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U || !seen.Add(netId))
                        {
                            continue;
                        }

                        Vector3 position;
                        if (!this.TryGetAuraMonoEntityPosition(entityObj, out position) || position == Vector3.zero)
                        {
                            if (!this.TryGetEntityPositionByNetIdMono(netId, out position) && !this.TryGetEntityPositionByNetId(netId, out position))
                            {
                                continue;
                            }
                        }

                        ids.Add(netId);
                        positions.Add(position);
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(insectPins);
            }

            status = ids.Count > 0
                ? $"Loaded insect targets via GetComponents ({ids.Count})"
                : "GetComponents found no loaded insects";
            this.InsectFarmNetLog("Loaded insect scan via GetComponents<InsectComponent>: resolved=" + ids.Count);
            return ids.Count > 0;
        }

        private bool TryGetLoadedInsectTargetsAuraMono(List<uint> ids, List<Vector3> positions, out string status)
        {
            status = "Aura mono loaded insect scan unavailable";
            ids.Clear();
            positions.Clear();

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "Aura mono API unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                {
                    status = "Mono InteractSystem instance unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "Mono player unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") || equipObj == IntPtr.Zero)
                {
                    status = "Mono equipComponent unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") || handholdObj == IntPtr.Zero)
                {
                    status = "Mono handhold unavailable";
                    return false;
                }

                if (!this.TryReadAuraMonoObjectField(handholdObj, out IntPtr managerObj, "_insectManager", "insectManager") || managerObj == IntPtr.Zero)
                {
                    status = "Active sweep-net manager unavailable";
                    return false;
                }

                IntPtr insectsObj = IntPtr.Zero;
                if (!this.TryReadAuraMonoObjectField(managerObj, out insectsObj, "insects", "_insects")
                    && !this.TryInvokeAuraMonoZeroArg(managerObj, out insectsObj, "get_insects", "GetInsects"))
                {
                    status = "Aura mono manager insects unavailable";
                    return false;
                }

                if (insectsObj == IntPtr.Zero)
                {
                    status = "Aura mono manager insects unavailable";
                    return false;
                }

                List<IntPtr> insectItems = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(insectsObj, insectItems) || insectItems.Count == 0)
                {
                    status = "Aura mono manager insects empty";
                    return false;
                }

                HashSet<uint> seen = new HashSet<uint>();
                int inspected = 0;
                foreach (IntPtr itemObj in insectItems)
                {
                    if (itemObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    inspected++;
                    if (!this.TryResolveAuraMonoInsectEntityObject(itemObj, out IntPtr entityObj, 0) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U || !seen.Add(netId))
                    {
                        continue;
                    }

                    Vector3 position;
                    if (!this.TryGetAuraMonoEntityPosition(entityObj, out position) || position == Vector3.zero)
                    {
                        if (!this.TryGetEntityPositionByNetIdMono(netId, out position) && !this.TryGetEntityPositionByNetId(netId, out position))
                        {
                            continue;
                        }
                    }

                    ids.Add(netId);
                    positions.Add(position);
                    this.InsectFarmNetLog($"Loaded insect target netId={netId} via aura-mono pos={position}");
                }

                status = ids.Count > 0
                    ? $"Aura mono loaded insect targets ready ({ids.Count}/{inspected})"
                    : (inspected > 0 ? "Aura mono loaded insect targets unresolved" : "Aura mono loaded insect list empty");
                this.InsectFarmNetLog("Aura mono loaded insect scan: inspected=" + inspected + " resolved=" + ids.Count + " status=" + status);
                return ids.Count > 0;
            }
            catch (Exception ex)
            {
                status = "Aura mono loaded insect scan exception: " + ex.Message;
                this.InsectFarmNetLog("TryGetLoadedInsectTargetsAuraMono exception: " + ex);
                return false;
            }
        }

        public bool TryGetInsectNetToolStatus(out bool netEquipped, out string status)
        {
            netEquipped = false;
            status = "Unknown";
            string auraStatus = "Aura path not tried";
            string managedStatus = "Managed path not tried";

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
                {
                    IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                    if (interactObj != IntPtr.Zero && this.auraMonoInteractGetPlayerMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && playerObj != IntPtr.Zero)
                        {
                            if (this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") && equipObj != IntPtr.Zero)
                            {
                                if (this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") && handholdObj != IntPtr.Zero)
                                {
                                    if (this.TryReadAuraMonoObjectField(handholdObj, out IntPtr managerObj, "_insectManager", "insectManager") && managerObj != IntPtr.Zero)
                                    {
                                        netEquipped = true;
                                        status = "Net Equipped";
                                        return true;
                                    }

                                    auraStatus = "Aura handhold ready but sweep-net manager unavailable";
                                    status = "Holding Other Tool";
                                    return true;
                                }

                                auraStatus = "Aura equipComponent ready but handhold unavailable";
                                status = "No Tool Equipped";
                                return true;
                            }

                            auraStatus = "Aura player ready but equipComponent unavailable";
                            status = "No Tool Equipped";
                            return true;
                        }

                        auraStatus = exc != IntPtr.Zero ? "Aura player invoke raised exception" : "Aura player unavailable";
                    }
                    else
                    {
                        auraStatus = interactObj == IntPtr.Zero
                            ? "Aura InteractSystem unavailable"
                            : (this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero ? "Aura get_player unavailable" : "Aura runtime invoke unavailable");
                    }
                }
                else
                {
                    auraStatus = "Aura API unavailable";
                }
            }
            catch { }

            try
            {
                if (this.TryGetManagedInteractSystemObject(out object interactSystem, out _))
                {
                    object playerObj = null;
                    if (!this.TryGetManagedSelfPlayerObject(out playerObj, out _) && !this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out _))
                    {
                        managedStatus = "Managed interact player unavailable";
                    }
                    else if (!this.TryGetManagedSweepNetManagerObject(interactSystem, playerObj, out object managerObj, out string source))
                    {
                        managedStatus = "Managed sweep-net manager unavailable";
                        if (!string.IsNullOrEmpty(source) && source.IndexOf("not sweep net", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int marker = source.IndexOf("[not sweep net:", StringComparison.OrdinalIgnoreCase);
                            if (marker >= 0)
                            {
                                string detail = source.Substring(marker).Trim('[', ']');
                                detail = detail.Replace("not sweep net:", string.Empty).Trim();
                                status = "Holding " + detail;
                                return true;
                            }
                            status = "Holding Other Tool";
                            return true;
                        }

                        status = "No Tool Equipped";
                        return true;
                    }
                    else if (managerObj != null)
                    {
                        netEquipped = true;
                        status = "Net Equipped";
                        return true;
                    }
                }
                else
                {
                    managedStatus = "Managed InteractSystem unavailable";
                }
            }
            catch { }

            try
            {
                GameObject player = this.GetPlayerObject();
                if (player != null)
                {
                    if (this.TryFindSweepNetManagerObject(player, out Il2CppObject managerObj, out string source) && managerObj != null)
                    {
                        netEquipped = true;
                        status = "Net Equipped";
                        return true;
                    }

                    status = "Player ready but sweep-net manager unavailable"
                        + (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "none", StringComparison.Ordinal)
                            ? " (" + source + ")"
                            : string.Empty)
                        + " | aura=" + auraStatus
                        + " | managed=" + managedStatus;
                    return false;
                }
            }
            catch { }

            status = "Player Unavailable | aura=" + auraStatus + " | managed=" + managedStatus;
            return false;
        }

        public List<uint> GetLastInsectFarmSentNetIds()
        {
            return new List<uint>(this.lastInsectFarmSentNetIds);
        }

        private bool TryResolveAuraMonoInsectEntityObject(IntPtr candidateObj, out IntPtr entityObj, int depth)
        {
            entityObj = IntPtr.Zero;
            if (candidateObj == IntPtr.Zero || depth > 3)
            {
                return false;
            }

            if (this.LooksLikeAuraMonoEntityObject(candidateObj))
            {
                entityObj = candidateObj;
                return true;
            }

            foreach (string fieldName in new string[] { "entity", "Entity", "_entity", "ownerEntity", "OwnerEntity", "targetEntity", "TargetEntity" })
            {
                if (this.TryReadAuraMonoObjectField(candidateObj, out IntPtr nestedObj, fieldName) && nestedObj != IntPtr.Zero)
                {
                    if (this.TryResolveAuraMonoInsectEntityObject(nestedObj, out entityObj, depth + 1))
                    {
                        return true;
                    }
                }
            }

            if (this.TryInvokeAuraMonoZeroArg(candidateObj, out IntPtr methodObj, "get_entity", "GetEntity") && methodObj != IntPtr.Zero)
            {
                if (this.TryResolveAuraMonoInsectEntityObject(methodObj, out entityObj, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHasInsectComponentViaAuraMono(IntPtr entityObj, out int staticId)
        {
            staticId = 0;
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoInsectComponentClasses(out IntPtr serverInsectComponentClass, out IntPtr insectAiStateComponentClass))
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            if (entityClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getAlivedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_alived", 0);
            if (getAlivedMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr alivedResult = auraMonoRuntimeInvoke(getAlivedMethod, entityObj, IntPtr.Zero, ref exc);
                if (alivedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(alivedResult, out bool isAlive) && !isAlive)
                {
                    return false;
                }
            }

            IntPtr getSpawnedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_spawned", 0);
            if (getSpawnedMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr spawnedResult = auraMonoRuntimeInvoke(getSpawnedMethod, entityObj, IntPtr.Zero, ref exc);
                if (spawnedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(spawnedResult, out bool isSpawned) && !isSpawned)
                {
                    return false;
                }
            }

            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr invokeExc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref invokeExc);
            if (invokeExc != IntPtr.Zero || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            for (int i = 0; i < components.Count && i < 64; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr componentClass = auraMonoObjectGetClass(componentObj);
                if (componentClass == IntPtr.Zero)
                {
                    continue;
                }

                bool isServerInsect = serverInsectComponentClass != IntPtr.Zero
                    && this.IsAuraMonoClassAssignableTo(componentClass, serverInsectComponentClass);
                bool isInsectAiState = !isServerInsect
                    && insectAiStateComponentClass != IntPtr.Zero
                    && this.IsAuraMonoClassAssignableTo(componentClass, insectAiStateComponentClass);

                if (!isServerInsect && !isInsectAiState)
                {
                    string classDisplayName = this.GetAuraMonoClassDisplayName(componentClass);
                    if (!string.IsNullOrEmpty(classDisplayName))
                    {
                        isServerInsect = classDisplayName.EndsWith(".ServerInsectComponent", StringComparison.OrdinalIgnoreCase)
                            || classDisplayName.IndexOf("ServerInsectComponent", StringComparison.OrdinalIgnoreCase) >= 0;
                        isInsectAiState = !isServerInsect && (classDisplayName.EndsWith(".InsectAIStateComponent", StringComparison.OrdinalIgnoreCase)
                            || classDisplayName.IndexOf("InsectAIStateComponent", StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }

                if (!isServerInsect && !isInsectAiState)
                {
                    continue;
                }

                if (isServerInsect)
                {
                    this.TryGetMonoInt32Member(componentObj, "InsectStaticId", out staticId);
                    if (staticId <= 0)
                    {
                        this.TryGetMonoInt32Member(componentObj, "insectStaticId", out staticId);
                    }
                    if (staticId <= 0)
                    {
                        this.TryGetMonoInt32Member(componentObj, "_insectStaticId", out staticId);
                    }
                }

                return true;
            }

            return false;
        }

        private bool TryResolveAuraMonoInsectComponentClasses(out IntPtr serverInsectComponentClass, out IntPtr insectAiStateComponentClass)
        {
            serverInsectComponentClass = IntPtr.Zero;
            insectAiStateComponentClass = IntPtr.Zero;

            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage != IntPtr.Zero && auraMonoClassFromName != null)
            {
                serverInsectComponentClass = auraMonoClassFromName(ecsImage, "XDT.Scene.Shared.Modules.InsectCatching", "ServerInsectComponent");
                insectAiStateComponentClass = auraMonoClassFromName(ecsImage, "XDT.Scene.Shared.Modules.InsectCatching", "InsectAIStateComponent");
            }

            if (serverInsectComponentClass == IntPtr.Zero)
            {
                serverInsectComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.InsectCatching", "ServerInsectComponent");
            }

            if (insectAiStateComponentClass == IntPtr.Zero)
            {
                insectAiStateComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.InsectCatching", "InsectAIStateComponent");
            }

            return serverInsectComponentClass != IntPtr.Zero || insectAiStateComponentClass != IntPtr.Zero;
        }

        private void InsectFarmNetLog(string message)
        {
            if (!InsectNetFarm.IsDebugLoggingEnabled())
            {
                return;
            }

            ModLogger.Msg("[InsectFarmNet] " + message);
        }

        private bool TryResolveInsectNetId(GameObject target, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";

            if (target == null)
            {
                return false;
            }

            if (this.TryResolveNetIdFromGameObject(target, out netId, out source))
            {
                return true;
            }

            Transform parent = target.transform.parent;
            int parentDepth = 0;
            while (parent != null && parentDepth < 3)
            {
                if (this.TryResolveNetIdFromGameObject(parent.gameObject, out netId, out source))
                {
                    source = "parent/" + source;
                    return true;
                }
                parent = parent.parent;
                parentDepth++;
            }

            int childCount = target.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = target.transform.GetChild(i);
                if (child == null) continue;
                if (this.TryResolveNetIdFromGameObject(child.gameObject, out netId, out source))
                {
                    source = "child/" + source;
                    return true;
                }
            }

            return false;
        }

        private bool TryCollectCatchTargetsViaSweepNetManager(GameObject player, Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions, out string status)
        {
            status = "Sweep-net manager unavailable";
            ids.Clear();
            positions.Clear();

            try
            {
                string auraStatus;
                if (this.TryCollectCatchTargetsViaAuraMonoSweepNetManager(playerPos, scanRange, batchSize, ids, positions, out auraStatus))
                {
                    status = auraStatus;
                    return true;
                }
                if (this.IsFinalInsectFarmStatus(auraStatus))
                {
                    status = auraStatus;
                    return false;
                }

                string managedStatus;
                if (this.TryCollectCatchTargetsViaManagedInteractSystem(playerPos, scanRange, batchSize, ids, positions, out managedStatus))
                {
                    status = managedStatus;
                    return true;
                }
                if (this.IsFinalInsectFarmStatus(managedStatus))
                {
                    status = managedStatus;
                    return false;
                }

                if (player == null)
                {
                    status = "Player object unavailable";
                    return false;
                }

                if (!this.TryFindPlayerEntityObject(player, out Il2CppObject playerEntity, out string entitySource))
                {
                    status = "Player entity unavailable";
                    return false;
                }

                if (!this.TryFindSweepNetManagerObject(player, out Il2CppObject managerObj, out string managerSource))
                {
                    status = "Active sweep-net manager unavailable";
                    return false;
                }

                this.InsectFarmNetLog($"Fallback manager source={managerSource} playerEntity={entitySource}");

                Il2CppObject bugDictObj = this.ReadIl2CppObjectMember(managerObj, "_bugWithStatusDic") ?? this.ReadIl2CppObjectMember(managerObj, "bugWithStatusDic");
                if (bugDictObj == null)
                {
                    status = "manager._bugWithStatusDic missing";
                    return false;
                }

                var managerType = managerObj.GetIl2CppType();
                Il2CppMethodInfo clearMethod = bugDictObj.GetIl2CppType().GetMethod("Clear");
                Il2CppMethodInfo findMethod = managerType.GetMethod("FindInRangeBugs");
                Il2CppMethodInfo getMethod = managerType.GetMethod("GetCatchingInsects") ?? managerType.GetMethod("GetCatchingInsect");
                if (findMethod == null || getMethod == null)
                {
                    status = "Manager methods missing";
                    return false;
                }

                clearMethod?.Invoke(bugDictObj, null);
                findMethod.Invoke(managerObj, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[] { playerEntity, bugDictObj }));
                int markedCount = this.TryMarkManagerFindEntitiesSelected(managerObj, bugDictObj);
                this.InsectFarmNetLog($"Fallback FindInRangeBugs invoked. Marked={markedCount}");
                if (markedCount <= 0)
                {
                    status = "No insects nearby";
                    return false;
                }

                Il2CppObject listObj = getMethod.Invoke(managerObj, null);
                if (listObj == null)
                {
                    status = "No insects nearby";
                    return false;
                }

                if (!this.TryExtractCatchTargetsFromManagerResult(listObj, playerPos, scanRange, batchSize, ids, positions))
                {
                    status = "No insects nearby";
                    return false;
                }

                status = "Manager targets ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Manager exception: " + ex.Message;
                this.InsectFarmNetLog("TryCollectCatchTargetsViaSweepNetManager error: " + ex);
                return false;
            }
        }

        private bool TryCollectCatchTargetsViaAuraMonoSweepNetManager(Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions, out string status)
        {
            status = "Aura mono path unavailable";
            ids.Clear();
            positions.Clear();

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "Aura mono API unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero)
                {
                    status = "Mono InteractSystem instance unavailable";
                    return false;
                }

                if (this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    status = "Mono get_player unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "Mono player unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr playerEntityObj, "get_entity", "GetEntity"))
                {
                    status = "Player entity unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent"))
                {
                    status = "Mono equipComponent unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold"))
                {
                    status = "Mono handhold unavailable";
                    return false;
                }

                if (!this.TryReadAuraMonoObjectField(handholdObj, out IntPtr managerObj, "_insectManager", "insectManager"))
                {
                    status = "Active sweep-net manager unavailable";
                    return false;
                }

                if (!this.TryReadAuraMonoObjectField(managerObj, out IntPtr bugDictObj, "_bugWithStatusDic", "bugWithStatusDic"))
                {
                    status = "manager._bugWithStatusDic missing";
                    return false;
                }

                IntPtr bugDictClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(bugDictObj) : IntPtr.Zero;
                IntPtr clearMethod = this.FindAuraMonoMethodOnHierarchy(bugDictClass, "Clear", 0);
                if (clearMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(clearMethod, bugDictObj, IntPtr.Zero, ref exc);
                }

                IntPtr managerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(managerObj) : IntPtr.Zero;
                IntPtr findMethod = this.FindAuraMonoMethodOnHierarchy(managerClass, "FindInRangeBugs", 2);
                IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(managerClass, "GetCatchingInsects", 0);
                if (getMethod == IntPtr.Zero)
                {
                    getMethod = this.FindAuraMonoMethodOnHierarchy(managerClass, "GetCatchingInsect", 0);
                }
                if (findMethod == IntPtr.Zero || getMethod == IntPtr.Zero)
                {
                    status = "Mono manager methods missing";
                    return false;
                }

                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = playerEntityObj;
                    args[1] = bugDictObj;
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(findMethod, managerObj, (IntPtr)args, ref exc);
                }
                if (exc != IntPtr.Zero)
                {
                    status = "Mono FindInRangeBugs failed";
                    return false;
                }

                int markedCount = this.TryMarkManagerFindEntitiesSelectedAuraMono(managerObj, bugDictObj);
                this.InsectFarmNetLog($"Aura mono FindInRangeBugs invoked. Marked={markedCount}");
                if (markedCount <= 0)
                {
                    status = "No insects nearby";
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr listObj = auraMonoRuntimeInvoke(getMethod, managerObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    status = "No insects nearby";
                    return false;
                }

                if (!this.TryExtractCatchTargetsFromManagerResultAuraMono(listObj, playerPos, scanRange, batchSize, ids, positions))
                {
                    status = "No insects nearby";
                    return false;
                }

                status = "Aura mono manager targets ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Aura mono exception: " + ex.Message;
                this.InsectFarmNetLog("TryCollectCatchTargetsViaAuraMonoSweepNetManager error: " + ex);
                return false;
            }
        }

        private bool TryCollectCatchTargetsViaManagedInteractSystem(Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions, out string status)
        {
            status = "Managed interact system unavailable";
            ids.Clear();
            positions.Clear();

            try
            {
                if (!this.TryGetManagedInteractSystemObject(out object interactSystem, out string interactSource))
                {
                    status = "InteractSystem unavailable";
                    return false;
                }

                object playerObj = null;
                string playerSource = string.Empty;
                if (!this.TryGetManagedSelfPlayerObject(out playerObj, out playerSource))
                {
                    if (!this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out playerSource))
                    {
                        status = "InteractSystem player unavailable";
                        return false;
                    }
                }

                object playerEntity = null;
                string entitySource = string.Empty;
                if (!this.TryGetManagedSelfPlayerEntityObject(out playerEntity, out entitySource)
                    && !this.TryGetManagedPlayerEntityObject(playerObj, out playerEntity, out entitySource))
                {
                    status = "Player entity unavailable";
                    return false;
                }

                if (!this.TryGetManagedSweepNetManagerObject(interactSystem, playerObj, out object managerObj, out string managerSource))
                {
                    status = "Active sweep-net manager unavailable";
                    return false;
                }

                this.InsectFarmNetLog($"Managed sources: interact={interactSource} player={playerSource} entity={entitySource} manager={managerSource}");

                if (!this.TryGetObjectMember(managerObj, "_bugWithStatusDic", out object bugDictObj) && !this.TryGetObjectMember(managerObj, "bugWithStatusDic", out bugDictObj))
                {
                    status = "manager._bugWithStatusDic missing";
                    return false;
                }

                if (bugDictObj == null)
                {
                    status = "manager._bugWithStatusDic null";
                    return false;
                }

                Type managerType = managerObj.GetType();
                Type bugDictType = bugDictObj.GetType();
                MethodInfo clearMethod = bugDictType.GetMethod("Clear", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo findMethod = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "FindInRangeBugs" && m.GetParameters().Length == 2);
                MethodInfo getMethod = managerType.GetMethod("GetCatchingInsects", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? managerType.GetMethod("GetCatchingInsect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (findMethod == null || getMethod == null)
                {
                    status = "Manager methods missing";
                    return false;
                }

                clearMethod?.Invoke(bugDictObj, null);
                findMethod.Invoke(managerObj, new object[] { playerEntity, bugDictObj });
                int markedCount = this.TryMarkManagerFindEntitiesSelectedManaged(managerObj, bugDictObj);
                this.InsectFarmNetLog($"Managed FindInRangeBugs invoked. Marked={markedCount}");

                object listObj = getMethod.Invoke(managerObj, null);
                if (listObj == null)
                {
                    status = "GetCatchingInsects returned null";
                    return false;
                }

                if (!this.TryExtractCatchTargetsFromManagerResultManaged(listObj, playerPos, scanRange, batchSize, ids, positions))
                {
                    status = "No catchable manager targets";
                    return false;
                }

                status = "Manager targets ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Managed manager exception: " + ex.Message;
                this.InsectFarmNetLog("TryCollectCatchTargetsViaManagedInteractSystem error: " + ex);
                return false;
            }
        }

        private bool IsFinalInsectFarmStatus(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return false;
            }

            return string.Equals(status, "No insects nearby", StringComparison.Ordinal)
                || string.Equals(status, "No Tool Equipped", StringComparison.Ordinal)
                || string.Equals(status, "Holding Other Tool", StringComparison.Ordinal)
                || status.StartsWith("Holding ", StringComparison.Ordinal);
        }

        private bool TryGetManagedSweepNetManagerObject(object interactSystemObj, object playerObj, out object managerObj, out string source)
        {
            managerObj = null;
            source = "none";

            object handholdObj = null;
            string handholdSource = string.Empty;
            if (interactSystemObj != null)
            {
                foreach (string memberName in new string[] { "_handhold", "handhold" })
                {
                    if (this.TryGetObjectMember(interactSystemObj, memberName, out handholdObj) && handholdObj != null)
                    {
                        handholdSource = interactSystemObj.GetType().Name + "." + memberName;
                        break;
                    }
                }
            }

            if (handholdObj == null && playerObj != null)
            {
                object equipComponent;
                if (this.TryInvokeZeroArgMember(playerObj, out equipComponent, "get_equipComponent", "GetEquipComponent")
                    || this.TryGetObjectMember(playerObj, "equipComponent", out equipComponent)
                    || this.TryGetObjectMember(playerObj, "_equipComponent", out equipComponent))
                {
                    if (equipComponent != null)
                    {
                        if ((this.TryInvokeZeroArgMember(equipComponent, out handholdObj, "get_handhold", "GetHandhold")
                            || this.TryGetObjectMember(equipComponent, "handhold", out handholdObj)
                            || this.TryGetObjectMember(equipComponent, "_handhold", out handholdObj)) && handholdObj != null)
                        {
                            handholdSource = equipComponent.GetType().Name + ".handhold";
                        }
                    }
                }
            }

            if (handholdObj == null)
            {
                return false;
            }

            string className = handholdObj.GetType().FullName ?? handholdObj.GetType().Name ?? string.Empty;
            if (className.IndexOf("SweepNet", StringComparison.OrdinalIgnoreCase) < 0)
            {
                source = handholdSource + " [not sweep net: " + className + "]";
                return false;
            }

            if (!(this.TryGetObjectMember(handholdObj, "_insectManager", out managerObj) || this.TryGetObjectMember(handholdObj, "insectManager", out managerObj)) || managerObj == null)
            {
                source = handholdSource + "._insectManager";
                return false;
            }

            source = handholdSource + "._insectManager";
            return true;
        }

        private bool TryExtractCatchTargetsFromManagerResultAuraMono(IntPtr listObj, Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions)
        {
            if (listObj == IntPtr.Zero || ids == null || positions == null || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            ids.Clear();
            positions.Clear();
            float effectiveCatchRange = scanRange > 0f ? Mathf.Min(scanRange, 20f) : 20f;

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            IntPtr getCountMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0);
            IntPtr getItemMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Item", 1);
            if (getCountMethod == IntPtr.Zero || getItemMethod == IntPtr.Zero)
            {
                return false;
            }

            int count = this.GetAuraMonoIntCount(listObj, getCountMethod);
            List<(uint id, Vector3 pos, float dist)> collected = new List<(uint, Vector3, float)>();
            for (int i = 0; i < count; i++)
            {
                IntPtr itemObj;
                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&i);
                    itemObj = auraMonoRuntimeInvoke(getItemMethod, listObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || itemObj == IntPtr.Zero)
                    {
                        continue;
                    }
                }

                uint netId = this.TryReadAuraMonoUIntField(itemObj, "netId", "ownerNetId", "NetId", "insectNetId", "entityNetId", "Item1");
                if (netId == 0U)
                {
                    continue;
                }

                Vector3 pos = this.TryReadAuraMonoVector3Field(itemObj, "position", "pos", "Position", "Item2");
                float dist = Vector3.Distance(playerPos, pos);
                if (effectiveCatchRange > 0f && dist > effectiveCatchRange)
                {
                    continue;
                }

                collected.Add((netId, pos, dist));
            }

            if (collected.Count == 0)
            {
                return false;
            }

            collected.Sort((a, b) => a.dist.CompareTo(b.dist));
            HashSet<uint> seen = new HashSet<uint>();
            foreach ((uint id, Vector3 pos, float dist) item in collected)
            {
                if (ids.Count >= batchSize)
                {
                    break;
                }

                if (!seen.Add(item.id))
                {
                    continue;
                }

                ids.Add(item.id);
                positions.Add(item.pos);
                this.InsectFarmNetLog($"Aura mono target netId={item.id} distance={item.dist:F2} pos={item.pos}");
            }

            return ids.Count > 0;
        }

        private bool TryExtractCatchTargetsFromManagerResultManaged(object listObj, Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                float effectiveCatchRange = scanRange > 0f ? Mathf.Min(scanRange, 20f) : 20f;
                Type listType = listObj.GetType();
                MethodInfo getCountMethod = listType.GetMethod("get_Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getItemMethod = listType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getCountMethod == null || getItemMethod == null)
                {
                    return false;
                }

                int count = Convert.ToInt32(getCountMethod.Invoke(listObj, null));
                List<(uint id, Vector3 pos, float dist)> collected = new List<(uint, Vector3, float)>();
                for (int i = 0; i < count; i++)
                {
                    object item = getItemMethod.Invoke(listObj, new object[] { i });
                    if (item == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveNetIdFromManagedObject(item, out uint netId, out string source) || netId == 0U)
                    {
                        continue;
                    }

                    Vector3 pos;
                    if (!this.TryResolvePositionFromManagedObject(item, out pos))
                    {
                        pos = Vector3.zero;
                    }

                    float dist = Vector3.Distance(playerPos, pos);
                    if (pos != Vector3.zero && dist <= effectiveCatchRange)
                    {
                        collected.Add((netId, pos, dist));
                        this.InsectFarmNetLog($"Managed target netId={netId} via {source} dist={dist:F2}");
                    }
                }

                collected.Sort((a, b) => a.dist.CompareTo(b.dist));
                HashSet<uint> seen = new HashSet<uint>();
                foreach (var item in collected)
                {
                    if (ids.Count >= batchSize)
                    {
                        break;
                    }

                    if (seen.Add(item.id))
                    {
                        ids.Add(item.id);
                        positions.Add(item.pos);
                    }
                }

                return ids.Count > 0;
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryExtractCatchTargetsFromManagerResultManaged error: " + ex);
                return false;
            }
        }

        private bool TryFindSweepNetManagerObject(GameObject player, out Il2CppObject managerObj, out string source)
        {
            managerObj = null;
            source = "none";
            if (player == null)
            {
                return false;
            }

            Component[] components = player.GetComponentsInChildren<Component>(true);
            foreach (Component comp in components)
            {
                if (comp == null)
                {
                    continue;
                }

                var ilType = comp.GetIl2CppType();
                if (ilType == null)
                {
                    continue;
                }

                string typeName = (ilType.FullName ?? ilType.Name ?? string.Empty);
                bool looksLikeSweepNet = typeName.IndexOf("SweepNet", StringComparison.OrdinalIgnoreCase) >= 0;
                Il2CppObject found = this.ReadIl2CppObjectMember(comp.TryCast<Il2CppObject>(), "_insectManager")
                    ?? this.ReadIl2CppObjectMember(comp.TryCast<Il2CppObject>(), "insectManager");
                if (found != null)
                {
                    managerObj = found;
                    source = typeName + (looksLikeSweepNet ? " [SweepNet]" : string.Empty);
                    return true;
                }
            }

            return false;
        }

        private bool TryExtractCatchTargetsFromManagerResult(Il2CppObject listObj, Vector3 playerPos, float scanRange, int batchSize, List<uint> ids, List<Vector3> positions)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                float effectiveCatchRange = scanRange > 0f ? Mathf.Min(scanRange, 20f) : 20f;
                var listType = listObj.GetIl2CppType();
                var countProp = listType.GetProperty("Count");
                var itemMethod = listType.GetMethod("get_Item");
                if (countProp == null || itemMethod == null)
                {
                    return false;
                }

                int count = countProp.GetValue(listObj).Unbox<int>();
                List<(uint id, Vector3 pos, float dist)> collected = new List<(uint, Vector3, float)>();
                for (int i = 0; i < count; i++)
                {
                    Il2CppObject item = itemMethod.Invoke(listObj, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[] { this.BoxInt(i) }));
                    if (item == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveNetIdFromIl2CppObject(item, out uint netId, out string source) || netId == 0U)
                    {
                        continue;
                    }

                    Vector3 pos = this.TryReadVector3Member(item, "position", out Vector3 positionValue)
                        ? positionValue
                        : this.TryReadVector3Member(item, "pos", out Vector3 posValue)
                            ? posValue
                            : this.TryReadVector3Member(item, "Position", out Vector3 posValue2)
                                ? posValue2
                                : Vector3.zero;

                    float dist = Vector3.Distance(playerPos, pos);
                    if (pos != Vector3.zero && dist <= effectiveCatchRange)
                    {
                        collected.Add((netId, pos, dist));
                        this.InsectFarmNetLog($"Manager target netId={netId} via {source} dist={dist:F2}");
                    }
                }

                collected.Sort((a, b) => a.dist.CompareTo(b.dist));
                HashSet<uint> seen = new HashSet<uint>();
                foreach (var item in collected)
                {
                    if (ids.Count >= batchSize)
                    {
                        break;
                    }

                    if (seen.Add(item.id))
                    {
                        ids.Add(item.id);
                        positions.Add(item.pos);
                    }
                }

                return ids.Count > 0;
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryExtractCatchTargetsFromManagerResult error: " + ex);
                return false;
            }
        }

        private bool TryInvokeInsectCatchProtocol(List<uint> ids, List<Vector3> positions, out int sentCount, out string status)
        {
            sentCount = 0;
            status = "Protocol unavailable";

            try
            {
                return this.TryInvokeAuraMonoInsectCatchProtocol(ids, positions, out sentCount, out status);
            }
            catch (Exception ex)
            {
                status = "Protocol exception: " + ex.Message;
                this.InsectFarmNetLog("TryInvokeInsectCatchProtocol error: " + ex);
                return false;
            }
        }

        private bool TryInvokeAuraMonoInsectCatchProtocol(List<uint> ids, List<Vector3> positions, out int sentCount, out string status)
        {
            sentCount = 0;
            status = "Protocol unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                if (auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoInsectCatchMethod(out IntPtr methodPtr, out int paramCount, out string source))
                {
                    this.InsectFarmNetLog("Aura mono insect catch method unavailable.");
                    return false;
                }

                var idList = new Il2CppSystem.Collections.Generic.List<uint>();
                foreach (uint id in ids)
                {
                    idList.Add(id);
                }

                var posList = new Il2CppSystem.Collections.Generic.List<Vector3>();
                foreach (Vector3 pos in positions)
                {
                    posList.Add(pos);
                }

                int actualParamCount = this.TryGetAuraMonoMethodParamCount(methodPtr);
                if (actualParamCount > 0)
                {
                    paramCount = actualParamCount;
                }

                this.InsectFarmNetLog($"Using aura mono catch protocol ptr=0x{methodPtr.ToInt64():X} paramCount={paramCount} source={source}");

                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    if (paramCount == 1)
                    {
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = idList.Pointer;
                        auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    }
                    else if (paramCount == 2)
                    {
                        IntPtr* args = stackalloc IntPtr[2];
                        args[0] = idList.Pointer;
                        args[1] = posList.Pointer;
                        auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    }
                    else
                    {
                        bool usePressCoolDown = false;
                        bool withNoAlert = false;
                        bool isSelected = false;
                        int bubbleNetId = 0;
                        IntPtr* args = stackalloc IntPtr[6];
                        args[0] = idList.Pointer;
                        args[1] = posList.Pointer;
                        args[2] = (IntPtr)(&usePressCoolDown);
                        args[3] = (IntPtr)(&withNoAlert);
                        args[4] = (IntPtr)(&isSelected);
                        args[5] = (IntPtr)(&bubbleNetId);
                        auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    }

                    if (exc != IntPtr.Zero)
                    {
                        status = "Aura mono protocol exception";
                        this.InsectFarmNetLog($"Aura mono catch protocol raised exception ptr=0x{exc.ToInt64():X}");
                        return false;
                    }
                }

                this.lastInsectFarmSentNetIds.Clear();
                this.lastInsectFarmSentNetIds.AddRange(ids);
                sentCount = ids.Count;
                status = "Protocol sent";
                return true;
            }
            catch (Exception ex)
            {
                status = "Aura mono protocol exception: " + ex.Message;
                this.InsectFarmNetLog("TryInvokeAuraMonoInsectCatchProtocol error: " + ex);
                return false;
            }
        }

        private bool TryResolveAuraMonoInsectCatchMethod(out IntPtr methodPtr, out int paramCount, out string source)
        {
            methodPtr = IntPtr.Zero;
            paramCount = 0;
            source = "none";

            if (this.cachedInsectCatchMonoMethod != IntPtr.Zero)
            {
                methodPtr = this.cachedInsectCatchMonoMethod;
                paramCount = this.cachedInsectCatchMonoMethodParamCount;
                source = "cache";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero && auraMonoClassFromName != null
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Insect", "InsectProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Insect", "InsectProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                this.InsectFarmNetLog("Aura mono protocol class unavailable.");
                return false;
            }

            string[] methodNames = new string[] { "CatchingInsect", "CatchInsect", "SendCatchingInsect", "CmdCatchInsect" };
            int[] preferredCounts = new int[] { 1, 2 };

            foreach (string methodName in methodNames)
            {
                foreach (int preferredCount in preferredCounts)
                {
                    IntPtr candidate = this.FindAuraMonoMethodOnHierarchy(protocolClass, methodName, preferredCount);
                    if (candidate == IntPtr.Zero)
                    {
                        continue;
                    }

                    int candidateParamCount = this.TryGetAuraMonoMethodParamCount(candidate);
                    if (candidateParamCount <= 0)
                    {
                        candidateParamCount = preferredCount;
                    }

                    this.InsectFarmNetLog($"Aura mono protocol candidate {methodName} ptr=0x{candidate.ToInt64():X} preferred={preferredCount} actual={candidateParamCount}");
                    if (candidateParamCount == 2 || candidateParamCount == 6 || candidateParamCount == 1)
                    {
                        this.cachedInsectCatchMonoMethod = candidate;
                        this.cachedInsectCatchMonoMethodParamCount = candidateParamCount;
                        methodPtr = candidate;
                        paramCount = candidateParamCount;
                        source = "class_get_method_from_name";
                        return true;
                    }
                }
            }

            if (auraMonoClassGetMethods != null && auraMonoMethodGetName != null)
            {
                IntPtr iter = IntPtr.Zero;
                while (true)
                {
                    IntPtr candidate = auraMonoClassGetMethods(protocolClass, ref iter);
                    if (candidate == IntPtr.Zero)
                    {
                        break;
                    }

                    string candidateName = Marshal.PtrToStringAnsi(auraMonoMethodGetName(candidate)) ?? string.Empty;
                    if (!methodNames.Contains(candidateName))
                    {
                        continue;
                    }

                    int candidateParamCount = this.TryGetAuraMonoMethodParamCount(candidate);
                    this.InsectFarmNetLog($"Aura mono enumerated protocol candidate {candidateName} ptr=0x{candidate.ToInt64():X} actual={candidateParamCount}");
                    if (candidateParamCount == 2 || candidateParamCount == 6 || candidateParamCount == 1)
                    {
                        this.cachedInsectCatchMonoMethod = candidate;
                        this.cachedInsectCatchMonoMethodParamCount = candidateParamCount;
                        methodPtr = candidate;
                        paramCount = candidateParamCount;
                        source = "class_get_methods";
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ShouldTrackInsectObject(string lowerName)
        {
            // Must have p_insect_insect prefix AND (clone) suffix
            return lowerName.Contains("p_insect_insect") && lowerName.Contains("(clone)");
        }

        private bool IsDisplayInsectObject(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            string hierarchyPath = this.GetHierarchyPath(obj.transform).ToLowerInvariant();
            string[] displayKeywords = new string[]
            {
                "aquarium",
                "display",
                "showcase",
                "terrarium",
                "vivarium",
                "cabinet",
                "furniture",
                "ornament",
                "decoration",
                "decor",
                "placement",
                "placed",
                "homeitem",
                "home_item",
                "houseitem",
                "house_item"
            };

            foreach (string keyword in displayKeywords)
            {
                if (hierarchyPath.Contains(keyword))
                {
                    return true;
                }
            }

            if (obj.GetComponent("InsectInVivariumComponent") != null)
            {
                return true;
            }

            for (Transform current = obj.transform; current != null; current = current.parent)
            {
                GameObject currentObject = current.gameObject;
                if (currentObject == null)
                {
                    continue;
                }

                if (currentObject.GetComponent("VivariumComponent") != null)
                {
                    return true;
                }

                string currentName = string.IsNullOrEmpty(currentObject.name) ? string.Empty : currentObject.name.ToLowerInvariant();
                if (currentName.Contains("vivarium")
                    || currentName.Contains("vivaruim")
                    || currentName.Contains("aquarium")
                    || currentName.Contains("terrarium")
                    || currentName.Contains("insectarium"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldTrackInsectObject(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy || obj.name == null)
            {
                return false;
            }

            string lowerName = obj.name.ToLowerInvariant();
            if (!this.ShouldTrackInsectObject(lowerName))
            {
                return false;
            }

            return !this.IsDisplayInsectObject(obj);
        }

    }
}
