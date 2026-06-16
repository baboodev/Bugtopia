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

                bool hasManagedInteract = this.TryGetManagedInteractSystemObject(out object interactSystem, out _);
                object playerObj = null;
                bool hasManagedPlayer = this.TryGetManagedSelfPlayerObject(out playerObj, out _);
                if (!hasManagedPlayer && hasManagedInteract)
                {
                    this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out _);
                }

                if (playerObj == null)
                {
                    status = !string.IsNullOrEmpty(monoTentativeStatus) ? monoTentativeStatus : "Player Unavailable";
                    this.AutoFishLog("Rod resolver failed: " + status);
                    return false;
                }

                if (this.TryGetManagedFishingRodObject(interactSystem, playerObj, out object rodObj, out string source))
                {
                    if (rodObj != null)
                    {
                        rodEquipped = true;
                        status = "Fishing Rod Equipped";
                        this.AutoFishLog("Rod resolver: managed rod resolved from " + source);
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(source) && source.IndexOf("not fishing rod", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int marker = source.IndexOf("[not fishing rod:", StringComparison.OrdinalIgnoreCase);
                    if (marker >= 0)
                    {
                        string detail = source.Substring(marker).Trim('[', ']');
                        detail = detail.Replace("not fishing rod:", string.Empty).Trim();
                        status = "Holding " + detail;
                        return true;
                    }

                    status = "Holding Other Tool";
                    return true;
                }

                status = !string.IsNullOrEmpty(monoTentativeStatus) ? monoTentativeStatus : "No Tool Equipped";
                this.AutoFishLog("Rod resolver result: " + status);
                return true;
            }
            catch (Exception ex)
            {
                status = "Exception: " + ex.Message;
                this.AutoFishLog("Rod resolver exception: " + ex.Message);
                return false;
            }
        }

        private bool TryGetManagedFishingRodObject(object interactSystemObj, object playerObj, out object rodObj, out string source)
        {
            rodObj = null;
            source = "none";

            if (playerObj == null && interactSystemObj == null)
            {
                source = "player/interact unavailable";
                return false;
            }

            foreach (string memberName in new string[] { "handhold", "_handhold", "currHandhold", "_currHandhold" })
            {
                if (interactSystemObj != null && this.TryGetObjectMember(interactSystemObj, memberName, out rodObj) && rodObj != null)
                {
                    if (this.IsManagedFishingRodObject(rodObj))
                    {
                        source = "InteractSystem." + memberName;
                        return true;
                    }

                    source = "[not fishing rod: " + rodObj.GetType().Name + "]";
                    return false;
                }
            }

            if (playerObj != null
                && (this.TryGetObjectMember(playerObj, "equipComponent", out object equipComponent)
                    || this.TryGetObjectMember(playerObj, "_equipComponent", out equipComponent))
                && equipComponent != null)
            {
                if ((this.TryGetObjectMember(equipComponent, "handhold", out rodObj)
                        || this.TryGetObjectMember(equipComponent, "_handhold", out rodObj))
                    && rodObj != null)
                {
                    if (this.IsManagedFishingRodObject(rodObj))
                    {
                        source = equipComponent.GetType().Name + ".handhold";
                        return true;
                    }

                    source = "[not fishing rod: " + rodObj.GetType().Name + "]";
                    return false;
                }
            }

            foreach (string memberName in new string[] { "handhold", "_handhold", "currHandhold", "_currHandhold" })
            {
                if (playerObj != null && this.TryGetObjectMember(playerObj, memberName, out rodObj) && rodObj != null)
                {
                    if (this.IsManagedFishingRodObject(rodObj))
                    {
                        source = "Player." + memberName;
                        return true;
                    }

                    source = "[not fishing rod: " + rodObj.GetType().Name + "]";
                    return false;
                }
            }

            source = "managed fishing rod unavailable";
            return false;
        }

        private bool IsManagedFishingRodObject(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            Type type = obj.GetType();
            string typeName = type.FullName ?? type.Name ?? string.Empty;
            if (typeName.IndexOf("FishingRod", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("HandHoldFishingRod", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            object marker;
            return this.TryGetObjectMember(obj, "_float", out marker)
                || this.TryGetObjectMember(obj, "floatComponent", out marker)
                || this.TryGetObjectMember(obj, "_targetFXProxy", out marker)
                || this.TryGetObjectMember(obj, "_invalidTargetFXProxy", out marker);
        }

        public bool TryFindNearestFishShadowTarget(float scanRange, out uint netId, out Vector3 position, out float distance, out int detectedCount, out string status)
        {
            netId = 0U;
            position = Vector3.zero;
            distance = 0f;
            detectedCount = 0;
            status = "No active fish shadows";

            try
            {
                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "Player position unavailable";
                    return false;
                }

                GameObject playerRoot = this.FindPlayerRoot();
                Transform playerTransform = playerRoot != null ? playerRoot.transform : null;
                Camera mainCamera = Camera.main;
                GameObject[] candidates = this.GetCachedFishShadowTargetObjects();
                float bestDistance = float.MaxValue;
                float bestScore = float.MaxValue;
                string bestName = string.Empty;
                int bestPriority = 0;
                int bestFishId = 0;
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
                    if (this.TryGetFishShadowOccupancy(candidate, out uint occupiedBuoyNetId, out uint occupiedPlayerNetId, out string occupiedState)
                        && (occupiedBuoyNetId != 0U
                            || occupiedPlayerNetId != 0U
                            || string.Equals(occupiedState, "Battle", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(occupiedState, "FindBuoyWaiting", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    Vector3 candidatePos = candidate.transform.position;
                    float candidateDistance = Vector3.Distance(playerPos, candidatePos);
                    if (scanRange > 0f && candidateDistance > scanRange)
                    {
                        continue;
                    }

                    int candidatePriority = this.GetFishShadowVisualPriority(candidate, out int candidateFishId, out string candidatePrioritySource);
                    float candidateScore = this.GetFishShadowTargetScore(candidate, candidatePos, candidateDistance, playerTransform, mainCamera, candidatePriority)
                        + this.GetFishShadowCoopJitter(candidate, playerRoot);
                    if (candidateScore >= bestScore)
                    {
                        continue;
                    }

                    uint candidateNetId = 0U;
                    this.TryResolveNetIdFromGameObject(candidate, out candidateNetId, out _);
                    bestScore = candidateScore;
                    bestDistance = candidateDistance;
                    bestName = candidate.name;
                    bestPriority = candidatePriority;
                    bestFishId = candidateFishId;
                    bestPrioritySource = candidatePrioritySource;
                    bestOccupancy = occupiedState;
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
            buoyNetId = 0U;
            playerNetId = 0U;
            state = string.Empty;
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

        private bool TryGetFishShadowQuality(GameObject candidate, int fishId, out int quality, out string source)
        {
            quality = 0;
            source = string.Empty;
            if (candidate == null)
            {
                return false;
            }

            try
            {
                Component[] components = candidate.GetComponents<Component>();
                if (this.TryGetFishShadowQualityFromComponents(components, fishId, out quality, out source))
                {
                    return true;
                }

                components = candidate.GetComponentsInChildren<Component>(true);
                return this.TryGetFishShadowQualityFromComponents(components, fishId, out quality, out source);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetFishShadowQualityFromComponents(Component[] components, int fishId, out int quality, out string source)
        {
            quality = 0;
            source = string.Empty;
            if (components == null)
            {
                return false;
            }

            string[] qualityMembers = new string[] { "Quality", "quality", "FishQuality", "fishQuality" };
            string[] weightMembers = new string[] { "Weight", "weight", "FishWeight", "fishWeight" };
            string[] dataMembers = new string[] { "ComponentData", "_componentData", "componentData", "data" };
            int bestWeight = 0;

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

                if (this.TryReadAnyIntMember(componentType, componentObj, qualityMembers, out quality) && quality > 0)
                {
                    source = componentType.Name + ".Quality";
                    return true;
                }

                if (bestWeight <= 0 && this.TryReadAnyIntMember(componentType, componentObj, weightMembers, out int directWeight) && directWeight > 0)
                {
                    bestWeight = directWeight;
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

                    if (this.TryReadAnyIntMember(dataType, dataObj, qualityMembers, out quality) && quality > 0)
                    {
                        source = dataType.Name + ".Quality";
                        return true;
                    }

                    if (bestWeight <= 0 && this.TryReadAnyIntMember(dataType, dataObj, weightMembers, out int dataWeight) && dataWeight > 0)
                    {
                        bestWeight = dataWeight;
                    }
                }
            }

            if (bestWeight > 0 && fishId > 0 && this.TryGetFishQualityByWeight(fishId, bestWeight, out quality) && quality > 0)
            {
                source = "table-quality-by-weight";
                return true;
            }

            return false;
        }

        private bool TryGetFishQualityByWeight(int fishId, int weight, out int quality)
        {
            quality = 0;
            if (fishId <= 0 || weight <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null || !this.TryInvokeStaticTableGetter(tableDataType, "GetFish", fishId, out object fishTable) || fishTable == null)
                {
                    return false;
                }

                if (!this.TryGetObjectMember(fishTable, "qualityWeight", out object qualityWeightObj) || !(qualityWeightObj is Array qualityWeight) || qualityWeight.Length == 0)
                {
                    return false;
                }

                if (!this.TryGetObjectMember(fishTable, "weightSection", out object weightSectionObj) || !(weightSectionObj is Array weightSection) || weightSection.Length != 2)
                {
                    return false;
                }

                int minWeight = Convert.ToInt32(weightSection.GetValue(0));
                int maxWeight = Convert.ToInt32(weightSection.GetValue(1));
                if (minWeight >= maxWeight)
                {
                    return false;
                }

                if (weight < minWeight)
                {
                    quality = 1;
                    return true;
                }

                int step = (maxWeight - minWeight) / qualityWeight.Length;
                step = step > 0 ? step : 1;
                int currentQuality = 0;
                for (int threshold = minWeight; threshold < maxWeight; threshold += step)
                {
                    currentQuality++;
                    if (weight >= threshold && weight < threshold + step)
                    {
                        quality = currentQuality;
                        return true;
                    }
                }

                quality = qualityWeight.Length;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int GetFishShadowColorPriority(Color color)
        {
            bool looksGold = color.r > 0.68f && color.g > 0.42f && color.g < 0.92f && color.b < 0.42f;
            if (looksGold)
            {
                return 3;
            }

            bool looksLightBlue = color.b > 0.58f && color.g > 0.45f && color.r < 0.55f;
            if (looksLightBlue)
            {
                return 2;
            }

            return 0;
        }

        private int GetFishShadowTablePriority(int fishId, out string source)
        {
            source = string.Empty;
            if (fishId <= 0)
            {
                return 0;
            }

            if (this.fishShadowPriorityByFishIdCache.TryGetValue(fishId, out int cachedPriority))
            {
                this.fishShadowPrioritySourceByFishIdCache.TryGetValue(fishId, out source);
                return cachedPriority;
            }

            int priority = 0;
            string resolvedSource = string.Empty;
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType != null && this.TryInvokeStaticTableGetter(tableDataType, "GetFish", fishId, out object fishTable) && fishTable != null)
                {
                    if (this.TryReadObjectInt(fishTable, "fishShadowModel", out int shadowModel) && shadowModel > 0)
                    {
                        priority = this.GetFishShadowModelPriority(shadowModel);
                        resolvedSource = "table-model-" + shadowModel;

                        if (this.TryInvokeStaticTableGetter(tableDataType, "GetFishshadow", shadowModel, out object shadowTable) && shadowTable != null)
                        {
                            string prefabId = this.TryReadObjectString(shadowTable, "normalPrefabId");
                            if (!string.IsNullOrEmpty(prefabId))
                            {
                                int prefabPriority = this.GetFishShadowPrefabPriority(prefabId);
                                if (prefabPriority > priority)
                                {
                                    priority = prefabPriority;
                                }

                                resolvedSource += ":" + prefabId;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            this.fishShadowPriorityByFishIdCache[fishId] = priority;
            this.fishShadowPrioritySourceByFishIdCache[fishId] = resolvedSource;
            source = resolvedSource;
            return priority;
        }

        private int GetFishShadowModelPriority(int shadowModel)
        {
            if (shadowModel == 4)
            {
                return 3;
            }

            if (shadowModel == 2 || shadowModel == 3)
            {
                return 2;
            }

            return 0;
        }

        private int GetFishShadowPrefabPriority(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId))
            {
                return 0;
            }

            string lowerPrefabId = prefabId.ToLowerInvariant();
            if (lowerPrefabId.Contains("gold") || lowerPrefabId.Contains("rare") || lowerPrefabId.Contains("rainbow"))
            {
                return 3;
            }

            if (lowerPrefabId.Contains("blue") || lowerPrefabId.Contains("lightblue") || lowerPrefabId.Contains("light_blue"))
            {
                return 2;
            }

            return this.GetFishShadowNameTierPriority(lowerPrefabId);
        }

        private int GetFishShadowNameTierPriority(string lowerName)
        {
            if (string.IsNullOrEmpty(lowerName))
            {
                return 0;
            }

            bool isFishShadowPrefab = lowerName.StartsWith("p_fishshadow_shadow_", StringComparison.Ordinal) ||
                                      lowerName.Contains("p_fishshadow_shadow_");
            if (!isFishShadowPrefab)
            {
                return 0;
            }

            // The live prefab name carries the visible shadow tier, e.g. _s_1, _m_2, _l_4.
            if (lowerName.Contains("_4_") || lowerName.Contains("_4(") || lowerName.Contains("_4_t"))
            {
                return 3; // gold / highest-value shadow
            }

            if (lowerName.Contains("_3_") || lowerName.Contains("_3(") || lowerName.Contains("_3_t") ||
                lowerName.Contains("_2_") || lowerName.Contains("_2(") || lowerName.Contains("_2_t"))
            {
                return 2; // light-blue / elevated shadow
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
            shipNetId = 0U;

            try
            {
                if (!this.TryGetManagedSelfPlayerObject(out object playerObj, out _) || playerObj == null)
                {
                    return false;
                }

                if (this.TryGetManagedUInt32Member(playerObj, "attachShipNetId", out shipNetId) && shipNetId != 0U)
                {
                    return true;
                }

                object entityObj = null;
                if (!this.TryGetObjectMember(playerObj, "entity", out entityObj) || entityObj == null)
                {
                    entityObj = playerObj;
                }

                Type shipComponentType = this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.Component.Fish.PlayerFishingShipComponent",
                    "PlayerFishingShipComponent");
                if (shipComponentType != null)
                {
                    object shipComponent = this.TryInvokeManagedGetComponent(entityObj, shipComponentType);
                    if (shipComponent != null
                        && this.TryGetManagedUInt32Member(shipComponent, "attachShipNetId", out shipNetId)
                        && shipNetId != 0U)
                    {
                        return true;
                    }
                }

                if (this.TryGetManagedUInt32Member(entityObj, "netId", out uint playerNetId) && playerNetId != 0U)
                {
                    Type dataCenterType = this.FindLoadedType(
                        "XDTDataAndProtocol.ComponentsData.DataCenter",
                        "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter",
                        "DataCenter");
                    Type shipDataType = this.FindLoadedType(
                        "XDTDataAndProtocol.ComponentsData.PlayerFishingShipComponentData",
                        "ScriptsRefactory.DataAndProtocol.ComponentsData.PlayerFishingShipComponentData",
                        "PlayerFishingShipComponentData");
                    Type netIdType = this.cachedAutoSellNetIdType
                        ?? this.FindLoadedType("XDT.Scene.Shared.NetId", "NetId", "EcsClient.XDT.Scene.Shared.NetId");
                    if (dataCenterType != null && shipDataType != null && netIdType != null)
                    {
                        MethodInfo tryGetComponentData = null;
                        foreach (MethodInfo method in dataCenterType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                            {
                                continue;
                            }

                            ParameterInfo[] parameters = method.GetParameters();
                            if (parameters.Length == 2)
                            {
                                tryGetComponentData = method;
                                break;
                            }
                        }

                        if (tryGetComponentData != null)
                        {
                            MethodInfo closedMethod = tryGetComponentData.MakeGenericMethod(shipDataType);
                            object netIdArg = netIdType.IsValueType
                                ? Activator.CreateInstance(netIdType)
                                : null;
                            if (netIdType == typeof(uint))
                            {
                                netIdArg = playerNetId;
                            }
                            else
                            {
                                try
                                {
                                    netIdArg = Convert.ChangeType(playerNetId, netIdType);
                                }
                                catch
                                {
                                    netIdArg = null;
                                }
                            }

                            if (netIdArg != null)
                            {
                                object[] args = new object[] { netIdArg, null };
                                if (closedMethod.Invoke(null, args) is bool found && found && args[1] != null)
                                {
                                    if (this.TryGetManagedUInt32Member(args[1], "shipNetId", out shipNetId) && shipNetId != 0U)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return shipNetId != 0U;
        }

        private bool TryFacePlayerTowardCastTarget(Vector3 targetPos, out string status)
        {
            status = "Player unavailable";

            try
            {
                GameObject skeleton = HeartopiaComplete.GetLocalPlayer();
                GameObject positionSource = skeleton != null ? skeleton : this.FindPlayerRoot();
                if (positionSource == null)
                {
                    return false;
                }

                bool onFishingShip = this.IsLocalPlayerOnFishingShip(out uint _);
                Vector3 playerPos = positionSource.transform.position;
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

                if (this.TrySyncLocalPlayerCastFacingMono(playerPos, eulerAngles, faceRot, flatDir, onFishingShip, out string monoStatus))
                {
                    status = monoStatus;
                    this.AutoFishLog("Pre-cast entity facing yaw=" + targetYaw.ToString("F1") + " " + status + " target=" + targetPos);
                    return true;
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
                this.AutoFishLog("Pre-cast facing fallback " + status + " target=" + targetPos);
                return true;
            }
            catch (Exception ex)
            {
                status = "Pre-cast facing failed: " + ex.Message;
                this.AutoFishLog(status);
                return false;
            }
        }

        private unsafe bool TrySyncLocalPlayerCastFacingMono(Vector3 playerPos, Vector3 eulerAngles, Quaternion faceRot, Vector3 flatDir, bool onFishingShip, out string status)
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
            if (!onFishingShip)
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

            if (!onFishingShip)
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

        public bool TryEnterFishingAtTarget(Vector3 targetPos, out string status)
        {
            status = "GameplayApi unavailable";

            try
            {
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

        private unsafe bool TryResolveFishingThrowTargetMono(Vector3 desiredTargetPos, out Vector3 throwTargetPos, out string status)
        {
            throwTargetPos = Vector3.zero;
            status = "Fishing rod throw target unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "Fishing rod throw target runtime unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                {
                    status = "Fishing rod throw target interact unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw target player unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") || equipObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw target equip unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") || handholdObj == IntPtr.Zero)
                {
                    status = "Fishing rod throw target handhold unavailable";
                    return false;
                }

                IntPtr handholdClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(handholdObj) : IntPtr.Zero;
                string handholdClassName = this.GetAuraMonoClassDisplayName(handholdClass);
                if (string.IsNullOrEmpty(handholdClassName)
                    || handholdClassName.IndexOf("FishingRod", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    status = "Fishing rod throw target handhold is not fishing rod";
                    return false;
                }

                IntPtr resetOffsetMethod = this.FindAuraMonoMethodOnHierarchy(handholdClass, "ResetFloatTargetOffset", 0);
                if (resetOffsetMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(resetOffsetMethod, handholdObj, IntPtr.Zero, ref exc);
                }

                GameObject playerRoot = this.FindPlayerRoot();
                if (playerRoot != null)
                {
                    Vector3 playerPos = playerRoot.transform.position;
                    Vector3 playerForward = playerRoot.transform.forward;
                    playerForward.y = 0f;
                    if (playerForward.sqrMagnitude <= 0.0001f)
                    {
                        playerForward = desiredTargetPos - playerPos;
                        playerForward.y = 0f;
                    }

                    if (playerForward.sqrMagnitude > 0.0001f)
                    {
                        playerForward.Normalize();
                        Vector3 playerRight = new Vector3(playerForward.z, 0f, -playerForward.x);
                        Vector3 delta = desiredTargetPos - playerPos;
                        delta.y = 0f;

                        const float fishThrowMaxDistance = 2f;
                        float sideOffset = Vector3.Dot(delta, playerRight);
                        float forwardOffset = Vector3.Dot(delta, playerForward);
                        Vector2 inputOffset = new Vector2(
                            Mathf.Clamp(sideOffset / fishThrowMaxDistance, -1f, 1f),
                            Mathf.Clamp((forwardOffset - 3f) / fishThrowMaxDistance, -1f, 1f));

                        if (!this.TryInvokeAuraMonoVector2Arg(handholdObj, "UpdateFloatTargetOffset", inputOffset, out string offsetStatus))
                        {
                            status = "Fishing rod throw offset failed: " + offsetStatus;
                            return false;
                        }
                    }
                }

                IntPtr drawTargetMethod = this.FindAuraMonoMethodOnHierarchy(handholdClass, "DrawBuoyTarget", 1);
                if (drawTargetMethod == IntPtr.Zero)
                {
                    status = "Fishing rod DrawBuoyTarget unavailable";
                    return false;
                }

                IntPtr* drawArgs = stackalloc IntPtr[1];
                drawArgs[0] = playerObj;
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(drawTargetMethod, handholdObj, (IntPtr)drawArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Fishing rod DrawBuoyTarget exception";
                    return false;
                }

                if (!this.TryGetMonoBoolMember(handholdObj, "CanThrow", out bool canThrow) || !canThrow)
                {
                    status = "Fishing rod cannot throw";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(handholdObj, out IntPtr throwTargetObj, "GetThrowTarget", "get_throwTarPos", "get_ThrowTarPos") || throwTargetObj == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    status = "Fishing rod throw target invoke unavailable";
                    return false;
                }

                IntPtr rawThrowTarget = auraMonoObjectUnbox(throwTargetObj);
                if (rawThrowTarget == IntPtr.Zero)
                {
                    status = "Fishing rod throw target unbox unavailable";
                    return false;
                }

                throwTargetPos = *(Vector3*)rawThrowTarget;
                if (throwTargetPos == Vector3.zero)
                {
                    status = "Fishing rod throw target was zero";
                    return false;
                }

                status = "OK";
                this.AutoFishLog("Resolved fishing throw target desired=" + desiredTargetPos + " actual=" + throwTargetPos);
                return true;
            }
            catch (Exception ex)
            {
                status = "Fishing rod throw target failed: " + ex.Message;
                this.AutoFishLog("Fishing throw target resolver exception: " + ex.Message);
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

        private bool TryGetPlayerFishingStatusContext(out object statusObj, out object fishingStatusObj, out string status)
        {
            statusObj = null;
            fishingStatusObj = null;
            status = "Player unavailable";
            object playerObj = null;
            object interactSystemObj = null;

            bool hasManagedPlayer = this.TryGetManagedSelfPlayerObject(out playerObj, out _);
            bool hasManagedInteract = this.TryGetManagedInteractSystemObject(out interactSystemObj, out _);
            if (!hasManagedPlayer && !hasManagedInteract && playerObj == null)
            {
                this.AutoFishLog("Fishing status context unavailable: no managed player or interact system.");
                return false;
            }

            if (playerObj == null && !this.TryGetManagedInteractPlayerObject(interactSystemObj, out playerObj, out _))
            {
                status = "Interact player unavailable";
                this.AutoFishLog("Fishing status context unavailable: " + status);
                return false;
            }

            if (!(this.TryGetObjectMember(playerObj, "Status", out statusObj)
                    || this.TryGetObjectMember(playerObj, "status", out statusObj)
                    || this.TryGetObjectMember(playerObj, "_status", out statusObj))
                || statusObj == null)
            {
                status = "Player status unavailable";
                this.AutoFishLog("Fishing status context unavailable: " + status);
                return false;
            }

            if (!(this.TryGetObjectMember(statusObj, "FishingStatus", out fishingStatusObj)
                    || this.TryGetObjectMember(statusObj, "fishingStatus", out fishingStatusObj))
                || fishingStatusObj == null)
            {
                status = "FishingStatus unavailable";
                this.AutoFishLog("Fishing status context unavailable: " + status);
                return false;
            }

            status = "OK";
            return true;
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
                    || currentName.Contains("fishbowl"))
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

        private bool ClickFishingStoreItemByMatch(string match)
        {
            try
            {
                GameObject shop = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                if (shop == null) return false;
                Transform scrollTransform = shop.transform.Find("goods@scroll");
                Transform content = shop.transform.Find("goods@scroll/Content");
                if (content == null) return false;
                if (content.childCount == 0)
                {
                    LogAutoBuy("[Fishing] Shop content empty - will retry shortly");
                    return false;
                }

                ScrollRect scrollRect = null;
                if (scrollTransform != null)
                {
                    scrollRect = scrollTransform.GetComponent<ScrollRect>() ?? scrollTransform.GetComponentInChildren<ScrollRect>(true);
                }

                if (TryClickVisibleCookingStoreItemByMatch(content, match)) return true;

                if (scrollRect != null)
                {
                    const int scrollSteps = 12;
                    if (this.autoBuyFishingShopScrollStep < 0)
                    {
                        this.autoBuyFishingShopScrollStep = 0;
                        SetCookingStoreScrollPosition(scrollRect, 1f);
                        return false;
                    }
                    else if (this.autoBuyFishingShopScrollStep < scrollSteps)
                    {
                        this.autoBuyFishingShopScrollStep++;
                        float normalized = 1f - ((float)this.autoBuyFishingShopScrollStep / (float)scrollSteps);
                        SetCookingStoreScrollPosition(scrollRect, normalized);
                        return false;
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy("[Fishing] ClickFishingStoreItemByMatch error: " + ex.Message); }
            return false;
        }

        private void StartAutoBuyFishing()
        {
            try
            {
                GameObject p = GameObject.Find("p_player_skeleton(Clone)");
                if (p != null) this.autoBuyFishingSavedPosition = p.transform.position;
                this.autoBuyFishingSubState = 1;
                this.autoBuyFishingStepTimer = Time.unscaledTime + 0.1f;
                this.autoBuyFishingShopWaitStartedAt = 0f;
                this.autoBuyFishingStoreSelectRetryCount = 0;
                this.autoBuyFishingCurrentItemIndex = 0;
                this.autoBuyFishingPurchasedCount = 0;
                this.autoBuyFishingShopScrollStep = -1;
                this.autoBuyFishingPreviousGameSpeed = this.gameSpeed;
                this.SetGameSpeed(5f);
                this.autoBuyFishingForcedGameSpeed = true;
                this.TeleportToLocation(this.autoBuyFishingNearbyPos);
                LogAutoBuy("[Fishing] Started: teleporting to nearby position (Game Speed x5.0)");
            }
            catch (Exception ex) { LogAutoBuy("[Fishing] Start error: " + ex.Message); this.StopAutoBuyFishing("Start error"); }
        }

        private void StopAutoBuyFishing(string reason)
        {
            LogAutoBuy("[Fishing] Stopped: " + reason);
            this.CloseAutoBuyPanels();
            this.autoBuyFishingEnabled = false;
            this.autoBuyFishingSubState = 0;
            this.autoBuyFishingShopScrollStep = -1;
            if (this.autoBuyFishingForcedGameSpeed)
            {
                this.SetGameSpeed(Mathf.Max(1f, this.autoBuyFishingPreviousGameSpeed));
                this.autoBuyFishingForcedGameSpeed = false;
            }
            if (this.autoBuyFishingSavedPosition != Vector3.zero)
            {
                this.TeleportToLocation(this.autoBuyFishingSavedPosition);
                this.autoBuyFishingSavedPosition = Vector3.zero;
            }
        }

        private void RunAutoBuyFishingLogic()
        {
            try
            {
                if (!this.autoBuyFishingEnabled)
                {
                    if (this.autoBuyFishingForcedGameSpeed)
                    {
                        this.SetGameSpeed(Mathf.Max(1f, this.autoBuyFishingPreviousGameSpeed));
                        this.autoBuyFishingForcedGameSpeed = false;
                    }
                    return;
                }
                float now = Time.unscaledTime;

                // Close any popup that appears
                if (now >= this.autoBuyPopupCloseRetryAt && this.TryCloseAutoBuyObtainedPopup())
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.12f;
                    this.autoBuyFishingStepTimer = now + 0.12f;
                    return;
                }
                if (now >= this.autoBuyPopupCloseRetryAt)
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.2f;
                }

                switch (this.autoBuyFishingSubState)
                {
                    case 1: // teleporting to nearby position
                        if (this.teleportFramesRemaining <= 0)
                        {
                            this.autoBuyFishingSubState = 12;
                            this.autoBuyFishingStepTimer = now + 3f;
                            LogAutoBuy("[Fishing] Arrived at nearby position, waiting before approaching NPC");
                        }
                        break;
                    case 12: // waiting at nearby pos, then teleport to NPC front
                        if (now < this.autoBuyFishingStepTimer) break;
                        this.TeleportToLocation(this.autoBuyFishingTargetPos);
                        this.autoBuyFishingSubState = 2;
                        this.autoBuyFishingStepTimer = now + 0.8f;
                        LogAutoBuy("[Fishing] Teleporting to NPC front position");
                        break;
                    case 2: // waiting for dialogue - click chat icon until dialogue shows
                        if (now < this.autoBuyFishingStepTimer) break;
                        if (TryClickNpcChatIcon()) { this.autoBuyFishingStepTimer = now + 0.5f; }
                        else { this.autoBuyFishingStepTimer = now + 0.12f; }
                        GameObject dlg = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                        if (dlg != null && dlg.activeInHierarchy)
                        {
                            this.autoBuyFishingSubState = 3; this.autoBuyFishingStepTimer = now + 0.2f; LogAutoBuy("[Fishing] Dialogue opened");
                        }
                        break;
                    case 3: // select fishing store
                        if (now < this.autoBuyFishingStepTimer) break;
                        if (!HasDialogueOptionsVisible())
                        {
                            if (TryAdvanceDialogueText())
                            {
                                LogAutoBuy("[Fishing] Advanced dialogue text, waiting for options");
                            }
                            this.autoBuyFishingStepTimer = now + 0.12f;
                            break;
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "fishing store", "fishing", "store" }))
                        {
                            this.autoBuyFishingSubState = 31;
                            this.autoBuyFishingStepTimer = now + 0.25f;
                            this.autoBuyFishingShopWaitStartedAt = now;
                            this.autoBuyFishingStoreSelectRetryCount++;
                            LogAutoBuy("[Fishing] Selected Fishing Store, waiting for shop content");
                        }
                        else { this.autoBuyFishingStepTimer = now + 0.15f; }
                        break;
                    case 31: // wait for ShopPanel to appear and be populated
                        if (now < this.autoBuyFishingStepTimer) break;
                        GameObject shopChk = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                        if (shopChk != null && shopChk.activeInHierarchy)
                        {
                            Transform contentChk = shopChk.transform.Find("goods@scroll/Content");
                            if (contentChk != null && contentChk.childCount > 0)
                            {
                                this.autoBuyFishingSubState = 4; this.autoBuyFishingStepTimer = now + 0.12f; LogAutoBuy("[Fishing] ShopPanel populated, proceeding to buy");
                                break;
                            }
                            else
                            {
                                LogAutoBuy("[Fishing] Waiting for ShopPanel content to populate...");
                                this.autoBuyFishingStepTimer = now + 0.25f;
                                break;
                            }
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "fishing store", "fishing", "store" }))
                        {
                            this.autoBuyFishingStoreSelectRetryCount++;
                            this.autoBuyFishingStepTimer = now + 0.25f;
                            LogAutoBuy("[Fishing] Retried Fishing Store option while waiting for shop");
                            break;
                        }
                        if (TryAdvanceDialogueText())
                        {
                            this.autoBuyFishingStepTimer = now + 0.12f;
                            break;
                        }
                        if (this.autoBuyFishingShopWaitStartedAt <= 0f) this.autoBuyFishingShopWaitStartedAt = now;
                        if ((now - this.autoBuyFishingShopWaitStartedAt) > 2.5f)
                        {
                            LogAutoBuy("[Fishing] Shop panel did not open yet, returning to store selection");
                            this.autoBuyFishingSubState = 3;
                            this.autoBuyFishingStepTimer = now + 0.1f;
                            this.autoBuyFishingShopWaitStartedAt = 0f;
                            break;
                        }
                        this.autoBuyFishingStepTimer = now + 0.25f;
                        break;
                    case 4: // buying items
                        if (now < this.autoBuyFishingStepTimer) break;
                        if (this.autoBuyFishingCurrentItemIndex >= this.autoBuyFishingItemsMatch.Length)
                        {
                            this.autoBuyFishingSubState = 5;
                            this.autoBuyFishingStepTimer = now + 3f;
                            LogAutoBuy("[Fishing] Finished item loop, waiting before return");
                            break;
                        }
                        string match = this.autoBuyFishingItemsMatch[this.autoBuyFishingCurrentItemIndex];
                        if (this.autoBuyFishingPurchasedCount >= this.autoBuyFishingMaxPerItem)
                        {
                            this.autoBuyFishingPurchasedCount = 0; this.autoBuyFishingCurrentItemIndex++; this.autoBuyFishingShopScrollStep = -1; this.autoBuyFishingStepTimer = now + 0.1f; break;
                        }
                        bool clicked = ClickFishingStoreItemByMatch(match);
                        if (clicked)
                        {
                            this.autoBuyFishingShopScrollStep = -1;
                            this.autoBuyFishingSubState = 41;
                            this.autoBuyFishingStepTimer = now + 0.12f;
                            if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Fishing] Opened purchase dialog for {match}"); }
                        }
                        else
                        {
                            GameObject shopProbe = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                            Transform contentProbe = shopProbe != null ? shopProbe.transform.Find("goods@scroll/Content") : null;
                            if (shopProbe != null && contentProbe != null && contentProbe.childCount == 0)
                            {
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Fishing] Shop content empty for item {match}, retrying shortly"); }
                                this.autoBuyFishingStepTimer = now + 0.25f;
                            }
                            else if (this.autoBuyFishingShopScrollStep >= 0 && this.autoBuyFishingShopScrollStep < 12)
                            {
                                this.autoBuyFishingStepTimer = now + 0.15f;
                            }
                            else
                            {
                                this.autoBuyFishingPurchasedCount = 0; this.autoBuyFishingCurrentItemIndex++; this.autoBuyFishingShopScrollStep = -1; this.autoBuyFishingStepTimer = now + 0.2f; if (this.autoBuyLogsEnabled) { ModLogger.Msg("[Fishing] Item " + match + " not found or sold out, skipping"); }
                            }
                        }
                        break;
                    case 41: // handle purchase dialog
                        if (now < this.autoBuyFishingStepTimer) break;
                        GameObject sale = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Top/SalePanel(Clone)");
                        if (sale == null || !sale.activeInHierarchy)
                        {
                            this.autoBuyFishingPurchasedCount = 0; this.autoBuyFishingCurrentItemIndex++; this.autoBuyFishingShopScrollStep = -1; this.autoBuyFishingSubState = 4; LogAutoBuy("[Fishing] Sale panel not found, skipping"); break;
                        }
                        int currentCount = GetSalePanelCurrentCount(sale);
                        int remainingStock = GetSalePanelRemainingStock(sale);
                        if (currentCount < 0) currentCount = 1;
                        int effectiveMax = this.autoBuyFishingMaxPerItem;
                        if (remainingStock > 0) effectiveMax = Mathf.Min(this.autoBuyFishingMaxPerItem, remainingStock);
                        if (currentCount >= effectiveMax)
                        {
                            if (ClickSalePurchase(sale))
                            {
                                this.autoBuyFishingPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Fishing] Purchased {currentCount} items"); }
                            }
                            this.autoBuyFishingPurchasedCount = 0; this.autoBuyFishingCurrentItemIndex++; this.autoBuyFishingShopScrollStep = -1; this.autoBuyFishingSubState = 4; this.autoBuyFishingStepTimer = now + 0.25f;
                        }
                        else
                        {
                            int remaining = effectiveMax - currentCount;
                            int doClicks = Math.Min(10, remaining);
                            bool anyClicked = false;
                            for (int i = 0; i < doClicks; i++)
                            {
                                if (ClickSaleAddMore(sale)) { anyClicked = true; }
                            }
                            this.autoBuyFishingStepTimer = now + 0.12f;
                            if (!anyClicked)
                            {
                                if (currentCount > 0 && ClickSalePurchase(sale))
                                {
                                    this.autoBuyFishingPurchasedCount = currentCount;
                                    if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Fishing] Purchased {currentCount} items (shop stock limited)"); }
                                }
                                else
                                {
                                    LogAutoBuy("[Fishing] Could not purchase item - no stock available");
                                }
                                this.autoBuyFishingPurchasedCount = 0; this.autoBuyFishingCurrentItemIndex++; this.autoBuyFishingShopScrollStep = -1; this.autoBuyFishingSubState = 4; this.autoBuyFishingStepTimer = now + 0.25f;
                            }
                        }
                        break;
                    case 5: // return
                        this.CloseAutoBuyPanels();
                        if (now < this.autoBuyFishingStepTimer) break;
                        this.StopAutoBuyFishing("Done, returning");
                        break;
                }
            }
            catch (Exception ex) { LogAutoBuy("[Fishing] Run error: " + ex.Message); this.StopAutoBuyFishing("Error"); }
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

        public bool IsFishingAutomationRuntimeReady(out string status)
        {
            status = "Managed player unavailable";

            try
            {
                if (this.TryGetManagedSelfPlayerObject(out object playerObj, out string playerSource) && playerObj != null)
                {
                    status = "Managed player ready via " + playerSource;
                    return true;
                }

                if (this.TryGetManagedInteractSystemObject(out object interactSystemObj, out string interactSource) && interactSystemObj != null)
                {
                    if (this.TryGetManagedInteractPlayerObject(interactSystemObj, out object interactPlayerObj, out string interactPlayerSource) && interactPlayerObj != null)
                    {
                        status = "Managed interact player ready via " + interactPlayerSource;
                        return true;
                    }

                    status = "Interact ready but player unresolved via " + interactSource;
                    return false;
                }

                status = "Managed interact unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "Runtime readiness exception: " + ex.Message;
                return false;
            }
        }

        private void StopAllAutoFishing()
        {
            AutoFishingFarm.ForceStop(this);
            this.showFishShadowRadar = false;
        }

    }
}
