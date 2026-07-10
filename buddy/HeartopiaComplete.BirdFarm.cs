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
        private void LoadBirdFarmSettings()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config?.BirdFarm != null)
                {
                    BirdNetFarm.ApplyBirdFarmConfig(config.BirdFarm);
                    ModLogger.Msg("Bird farm settings loaded.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error loading bird farm settings: " + ex.Message);
            }
        }

        public List<Vector3> GetTrackedBirdPositions()
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

                if (target == null)
                {
                    continue;
                }

                string lowerName = target.name != null ? target.name.ToLowerInvariant() : string.Empty;
                if (!this.ShouldTrackBirdObject(lowerName))
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

        public void SetBirdRadarEnabledForFarm(bool enabled)
        {
            // Farm no longer mutates bird radar state.
        }

        public List<GameObject> GetTrackedBirdObjects()
        {
            List<GameObject> result = new List<GameObject>();
            HashSet<int> seenIds = new HashSet<int>();

            foreach (KeyValuePair<GameObject, GameObject> mapping in this.markerToTarget)
            {
                GameObject target = mapping.Value;
                if (target == null)
                {
                    continue;
                }

                string lowerName = target.name != null ? target.name.ToLowerInvariant() : string.Empty;
                if (!this.ShouldTrackBirdObject(lowerName))
                {
                    continue;
                }

                int instanceId = target.GetInstanceID();
                if (seenIds.Add(instanceId))
                {
                    result.Add(target);
                }
            }

            return result;
        }

        public bool TryGetBirdScannerToolStatus(out bool scannerEquipped, out string status)
        {
            scannerEquipped = false;
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
                                    bool looksLikeBirdScanner = !string.IsNullOrEmpty(handholdClassName)
                                        && (handholdClassName.IndexOf("BirdScanner", StringComparison.OrdinalIgnoreCase) >= 0
                                            || (handholdClassName.IndexOf("bird", StringComparison.OrdinalIgnoreCase) >= 0
                                                && handholdClassName.IndexOf("scan", StringComparison.OrdinalIgnoreCase) >= 0)
                                            || handholdClassName.IndexOf("Scanner", StringComparison.OrdinalIgnoreCase) >= 0);

                                    if (this.TryReadAuraMonoObjectField(handholdObj, out IntPtr monoScannerObj, "_scanner", "scanner", "_birdScanner", "birdScanner", "_scannerStatusPanel", "scannerStatusPanel", "_scanComponent", "scanComponent", "_photoTargetManager", "photoTargetManager", "_birdManager", "birdManager") && monoScannerObj != IntPtr.Zero)
                                    {
                                        scannerEquipped = true;
                                        status = "Bird Scanner Equipped";
                                        return true;
                                    }

                                    if (looksLikeBirdScanner)
                                    {
                                        scannerEquipped = true;
                                        status = "Bird Scanner Equipped";
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
                if (!hasManagedInteract && !hasManagedPlayer)
                {
                    status = !string.IsNullOrEmpty(monoTentativeStatus) ? monoTentativeStatus : "Tool state unavailable";
                    return false;
                }

                if (!hasManagedPlayer && hasManagedInteract && !this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out _))
                {
                    status = !string.IsNullOrEmpty(monoTentativeStatus) ? monoTentativeStatus : "Player Unavailable";
                    return false;
                }

                if (!this.TryGetManagedBirdScannerObject(interactSystem, playerObj, out object scannerObj, out string source))
                {
                    if (!string.IsNullOrEmpty(source) && source.IndexOf("not bird scanner", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int marker = source.IndexOf("[not bird scanner:", StringComparison.OrdinalIgnoreCase);
                        if (marker >= 0)
                        {
                            string detail = source.Substring(marker).Trim('[', ']');
                            detail = detail.Replace("not bird scanner:", string.Empty).Trim();
                            status = "Holding " + detail;
                            return true;
                        }

                        status = "Holding Other Tool";
                        return true;
                    }

                    if (!string.IsNullOrEmpty(monoTentativeStatus))
                    {
                        status = monoTentativeStatus;
                        return true;
                    }

                    status = "No Tool Equipped";
                    return true;
                }

                if (scannerObj != null)
                {
                    scannerEquipped = true;
                    status = "Bird Scanner Equipped";
                    return true;
                }

                status = "No Tool Equipped";
                return true;
            }
            catch (Exception ex)
            {
                status = "Exception: " + ex.Message;
                return false;
            }
        }

        public bool TryTakeNearbyBirdPhotos(float scanRange, bool perfectPhotoEnabled, out int detectedCount, out int resolvedCount, out int sentCount, out string status)
        {
            return this.TryTakeNearbyBirdPhotos(scanRange, perfectPhotoEnabled, false, out detectedCount, out resolvedCount, out sentCount, out status);
        }

        public bool TryTakeNearbyBirdPhotos(float scanRange, bool perfectPhotoEnabled, bool spamMaxPhotoMode, out int detectedCount, out int resolvedCount, out int sentCount, out string status)
        {
            detectedCount = 0;
            resolvedCount = 0;
            sentCount = 0;
            status = "Idle";
            this.lastBirdFarmSentNetIds.Clear();
            this.CleanupBirdFarmRuntimeCachesIfDue();
            bool previousSpamMaxPhotoMode = this.birdFarmSpamMaxPhotoModeActive;
            this.birdFarmSpamMaxPhotoModeActive = spamMaxPhotoMode;

            try
            {
                if (this.TryGetCurrentScannerBirdTarget(out uint scannerNetId, out int scannerStaticId, out float scannerDistance, out string scannerStatus))
                {
                    detectedCount = 1;
                    bool scannerTargetUsable = true;
                    if (scannerDistance > 0f && scanRange > 0f && scannerDistance > scanRange)
                    {
                        status = $"Scanner target out of range ({scannerDistance:F2}m)";
                        scannerTargetUsable = false;
                    }

                    if (scannerTargetUsable && !this.IsBirdFarmPhotoTargetCapturable(scannerNetId, scannerStaticId, 0, 0U, out string scannerCapturableStatus))
                    {
                        status = scannerCapturableStatus;
                        scannerTargetUsable = false;
                    }

                    if (scannerTargetUsable)
                    {
                        resolvedCount = 1;
                        if (this.TryInvokeBirdPhotoProtocol(scannerStaticId, scannerNetId, 0, 0U, perfectPhotoEnabled, out string scannerProtocolStatus))
                        {
                            sentCount = 1;
                            this.lastBirdFarmSentNetIds.Add(scannerNetId);
                            status = $"Sent request {sentCount}/{resolvedCount}";
                            return true;
                        }

                        status = scannerProtocolStatus;
                        return false;
                    }
                }

                // The gameplay photo-mode/current-target path has been the most likely source
                // of recent hard runtime crashes (c0000005) because it touches transient
                // GamePhotoMode state before the safer Aura entity scan even starts.
                // For Bird Farm stability, route directly to entity scanning instead.
                bool canUseGameplayTarget = false;
                bool justEnteredPhotoMode = false;
                string photoModeStatus = "Gameplay target path bypassed";

                bool shouldFallbackToEntityTargets = true;
                if (canUseGameplayTarget && this.TryGetCurrentGameplayBirdTarget(out uint gameplayNetId, out int gameplayStaticId, out float gameplayDistance, out int gameplayBirdActionType, out uint gameplayBirdStandNetId, out string gameplayStatus))
                {
                    detectedCount = 1;

                    if (gameplayDistance > 0f && scanRange > 0f && gameplayDistance > scanRange)
                    {
                        status = $"Gameplay target out of range ({gameplayDistance:F2}m)";
                        shouldFallbackToEntityTargets = true;
                    }
                    else if (gameplayNetId == 0U || gameplayStaticId <= 0)
                    {
                        status = "Invalid bird target";
                        this.BirdFarmNetLog($"Gameplay target rejected: netId={gameplayNetId} staticId={gameplayStaticId}");
                        shouldFallbackToEntityTargets = true;
                    }
                    else if (!this.IsBirdFarmPhotoTargetCapturable(gameplayNetId, gameplayStaticId, gameplayBirdActionType, gameplayBirdStandNetId, out string gameplayCapturableStatus))
                    {
                        status = gameplayCapturableStatus;
                        shouldFallbackToEntityTargets = true;
                    }
                    else
                    {
                        resolvedCount = 1;
                        this.BirdFarmNetLog($"Gameplay target resolved: netId={gameplayNetId} staticId={gameplayStaticId} distance={(gameplayDistance > 0f ? gameplayDistance.ToString("F2") : "unknown")} pose={gameplayBirdActionType} standNetId={gameplayBirdStandNetId} source=GameplayCurrentAimTarget");
                        if (this.TryInvokeBirdPhotoProtocol(gameplayStaticId, gameplayNetId, gameplayBirdActionType, gameplayBirdStandNetId, perfectPhotoEnabled, out string gameplayProtocolStatus))
                        {
                            sentCount = 1;
                            this.lastBirdFarmSentNetIds.Add(gameplayNetId);
                            status = gameplayProtocolStatus;
                            this.BirdFarmNetLog($"Gameplay target photo protocol sent for netId={gameplayNetId} staticId={gameplayStaticId}");
                            shouldFallbackToEntityTargets = false;
                        }
                        else
                        {
                            status = gameplayProtocolStatus;
                            this.BirdFarmNetLog($"Gameplay target photo protocol failed for netId={gameplayNetId} staticId={gameplayStaticId} status={gameplayProtocolStatus}");
                            shouldFallbackToEntityTargets = false;
                        }

                        if (sentCount > 0)
                        {
                            status = $"Sent request {sentCount}/{resolvedCount}";
                            return true;
                        }

                        return false;
                    }
                }

                Vector3 playerPos = Vector3.zero;
                if (!this.TryGetLocalPlayerPosition(out playerPos))
                {
                    status = "Local player position unavailable";
                    return false;
                }
                string gameplayFallbackReason = canUseGameplayTarget ? "Gameplay current target unavailable" : photoModeStatus;
                if (!shouldFallbackToEntityTargets)
                {
                    status = string.IsNullOrWhiteSpace(status) ? gameplayFallbackReason : status;
                    return false;
                }

                int entitySearchAttempts = 0;
                while (entitySearchAttempts < 2)
                {
                    entitySearchAttempts++;
                    if (!this.TryFindNearestBirdEntityTarget(playerPos, scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int entityDetectedCount, out string entitySource, out string entityStatus))
                    {
                        detectedCount = Math.Max(detectedCount, entityDetectedCount);
                        status = string.IsNullOrWhiteSpace(entityStatus) ? gameplayFallbackReason : entityStatus;
                        return false;
                    }

                    detectedCount = Math.Max(detectedCount, Math.Max(entityDetectedCount, 1));

                    if (distance > 0f && scanRange > 0f && distance > scanRange)
                    {
                        status = $"{entitySource} target out of range ({distance:F2}m)";
                        continue;
                    }

                    if (netId == 0U || staticId <= 0)
                    {
                        status = "Invalid bird target";
                        this.BirdFarmNetLog($"Entity target rejected: netId={netId} staticId={staticId} source={entitySource}");
                        continue;
                    }

                    if (!this.IsBirdFarmPhotoTargetCapturable(netId, staticId, birdActionType, birdStandNetId, out string capturableStatus))
                    {
                        status = capturableStatus;
                        continue;
                    }

                    resolvedCount = 1;
                    this.BirdFarmNetLog($"Entity target resolved: netId={netId} staticId={staticId} distance={(distance > 0f ? distance.ToString("F2") : "unknown")} pose={birdActionType} standNetId={birdStandNetId} source={entitySource}");
                    if (this.TryInvokeBirdPhotoProtocol(staticId, netId, birdActionType, birdStandNetId, perfectPhotoEnabled, out string entityProtocolStatus))
                    {
                        sentCount = 1;
                        this.lastBirdFarmSentNetIds.Add(netId);
                        status = entityProtocolStatus;
                        this.BirdFarmNetLog($"Entity target photo protocol sent for netId={netId} staticId={staticId}");
                    }
                    else
                    {
                        status = entityProtocolStatus;
                        this.BirdFarmNetLog($"Entity target photo protocol failed for netId={netId} staticId={staticId} status={entityProtocolStatus}");
                    }

                    if (sentCount > 0)
                    {
                        status = $"Sent request {sentCount}/{resolvedCount}";
                        return true;
                    }

                    return false;
                }

                if (resolvedCount == 0 && string.Equals(status, "Idle", StringComparison.Ordinal))
                {
                    status = "No bird targets resolved";
                }

                return false;
            }
            catch (Exception ex)
            {
                status = "Exception: " + ex.Message;
                return false;
            }
            finally
            {
                this.birdFarmSpamMaxPhotoModeActive = previousSpamMaxPhotoMode;
            }
        }

        private bool TryEnsureBirdPhotoModeActive(out bool justEntered, out string status)
        {
            justEntered = false;
            status = "Bird photo mode ready";

            try
            {
                if (!this.TryResolveBirdPhotoModeContext(out object photoModeObj, out string photoModeSource))
                {
                    status = "GameplayApi photo mode unavailable";
                    return false;
                }

                PropertyInfo photoTypeProperty = photoModeObj.GetType().GetProperty("photoType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? photoModeObj.GetType().GetProperty("type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (photoTypeProperty != null)
                {
                    object currentPhotoType = photoTypeProperty.GetValue(photoModeObj, null);
                    if (currentPhotoType != null && string.Equals(currentPhotoType.ToString(), "Bird", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                MethodInfo setPhotoTypeMethod = photoModeObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "SetPhotoType" && m.GetParameters().Length == 1);
                if (setPhotoTypeMethod == null)
                {
                    status = "GamePhotoMode.SetPhotoType unavailable";
                    return false;
                }

                Type takingPictureType = setPhotoTypeMethod.GetParameters()[0].ParameterType;
                if (takingPictureType == null || !takingPictureType.IsEnum)
                {
                    status = "TakingPictureType enum unavailable";
                    return false;
                }

                object birdPhotoType = Enum.Parse(takingPictureType, "Bird");
                object rawResult = setPhotoTypeMethod.Invoke(photoModeObj, new object[] { birdPhotoType });
                bool entered = rawResult is bool enteredBool && enteredBool;
                if (!entered)
                {
                    status = "Failed to enter bird photo mode";
                    return false;
                }

                justEntered = true;
                status = "Entered bird photo mode via " + photoModeSource;
                return true;
            }
            catch (Exception ex)
            {
                status = "Bird photo mode exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentGameplayBirdTarget(out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            status = "Gameplay bird target unavailable";

            try
            {
                if (!this.TryResolveBirdPhotoModeContext(out object photoModeObj, out string photoModeSource))
                {
                    status = "GameplayApi unavailable";
                    return false;
                }

                this.TryRefreshManagedBirdPhotoModeComponents(photoModeObj);

                MethodInfo tickBirdTargetMethod = photoModeObj.GetType().GetMethod("TickBirdTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(float) }, null);
                tickBirdTargetMethod?.Invoke(photoModeObj, new object[] { Time.time });

                if (!this.TryGetObjectMember(photoModeObj, "_currentAimTarget", out object currentAimTarget) || currentAimTarget == null)
                {
                    status = "Gameplay photo mode has no current target";
                    return false;
                }

                if (!this.TryResolveBirdNetIdFromObject(currentAimTarget, out netId, out string netSource, 0) || netId == 0U)
                {
                    status = "Gameplay current target has no netId";
                    return false;
                }

                if (this.TryGetObjectMember(photoModeObj, "_targetResId", out object targetResIdObj) && targetResIdObj != null)
                {
                    try
                    {
                        staticId = Convert.ToInt32(targetResIdObj);
                    }
                    catch
                    {
                        staticId = 0;
                    }
                }

                if (staticId <= 0)
                {
                    staticId = this.TryGetEntityStaticId(netId);
                }

                if (this.TryGetObjectMember(photoModeObj, "_targetDistance", out object targetDistanceObj) && targetDistanceObj != null)
                {
                    try
                    {
                        distance = Convert.ToSingle(targetDistanceObj);
                    }
                    catch
                    {
                        distance = -1f;
                    }
                }

                this.TryGetBirdPhotoDetailsForNetId(netId, out birdActionType, out birdStandNetId);
                status = "Gameplay bird target ready via " + photoModeSource + " / " + netSource;
                return staticId > 0;
            }
            catch (Exception ex)
            {
                status = "Gameplay bird target exception: " + ex.Message;
                return false;
            }
        }

        private bool TryResolveBirdPhotoModeContext(out object photoModeObj, out string source)
        {
            photoModeObj = null;
            source = "none";
            this.lastBirdPhotoModeResolveStatus = "resolver started";

            try
            {
                Type gameplayApiType = this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.GameplayApi", "GameplayApi");
                if (gameplayApiType == null)
                {
                    this.lastBirdPhotoModeResolveStatus = "GameplayApi type not found";
                    this.BirdFarmNetLog("PhotoMode resolver: GameplayApi type not found.");
                }
                else
                {
                    PropertyInfo photoModeProperty = gameplayApiType.GetProperty("photoMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (photoModeProperty == null)
                    {
                        this.lastBirdPhotoModeResolveStatus = "GameplayApi.photoMode property missing";
                        this.BirdFarmNetLog("PhotoMode resolver: GameplayApi.photoMode property missing.");
                    }
                    object gameplayPhotoModeObj = photoModeProperty != null ? photoModeProperty.GetValue(null, null) : null;
                    if (gameplayPhotoModeObj != null)
                    {
                        photoModeObj = gameplayPhotoModeObj;
                        source = "GameplayApi.photoMode";
                        this.lastBirdPhotoModeResolveStatus = "resolved via GameplayApi.photoMode";
                        return true;
                    }

                    this.lastBirdPhotoModeResolveStatus = "GameplayApi.photoMode returned null";
                    this.BirdFarmNetLog("PhotoMode resolver: GameplayApi.photoMode returned null.");
                }
            }
            catch (Exception ex)
            {
                this.lastBirdPhotoModeResolveStatus = "GameplayApi path threw " + ex.GetType().Name + ": " + ex.Message;
                this.BirdFarmNetLog("PhotoMode resolver: GameplayApi path threw " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                Type characterType = this.FindLoadedType("XDTLevelAndEntity.Game.GameMode.Character", "Character");
                Type gamePhotoModeType = this.FindLoadedType("XDTLevelAndEntity.Game.GameMode.GamePhotoMode", "GamePhotoMode");
                if (characterType == null)
                {
                    this.lastBirdPhotoModeResolveStatus = "Character type missing";
                    this.BirdFarmNetLog("PhotoMode resolver: Character type missing.");
                    return false;
                }

                if (gamePhotoModeType == null)
                {
                    this.BirdFarmNetLog("PhotoMode resolver: GamePhotoMode type not found. Using runtime member fallback.");
                }

                PropertyInfo characterProperty = characterType.GetProperty("character", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object characterObj = characterProperty != null ? characterProperty.GetValue(null, null) : null;
                if (characterObj == null)
                {
                    this.BirdFarmNetLog("PhotoMode resolver: Character.character returned null. Trying player.character fallback.");
                    object playerObj = null;
                    object interactSystem = null;
                    bool gotSelfPlayer = this.TryGetManagedSelfPlayerObject(out playerObj, out _);
                    bool gotInteractSystem = this.TryGetManagedInteractSystemObject(out interactSystem, out _);
                    if (!gotSelfPlayer && (!gotInteractSystem || interactSystem == null))
                    {
                        this.lastBirdPhotoModeResolveStatus = "self player and interact system unavailable";
                        return false;
                    }

                    if (playerObj == null && interactSystem != null)
                    {
                        this.TryGetManagedInteractPlayerObject(interactSystem, out playerObj, out _);
                    }

                    if (playerObj == null || !this.TryGetObjectMember(playerObj, "character", out characterObj) || characterObj == null)
                    {
                        this.lastBirdPhotoModeResolveStatus = "player.character fallback unavailable";
                        this.BirdFarmNetLog("PhotoMode resolver: player.character fallback unavailable.");
                        return false;
                    }
                }

                if (gamePhotoModeType != null)
                {
                    MethodInfo getModeMethod = characterObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetMode" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                    if (getModeMethod != null)
                    {
                        object resolvedPhotoMode = getModeMethod.MakeGenericMethod(gamePhotoModeType).Invoke(characterObj, null);
                        if (resolvedPhotoMode != null)
                        {
                            photoModeObj = resolvedPhotoMode;
                            source = "Character.character.GetMode<GamePhotoMode>()";
                            this.lastBirdPhotoModeResolveStatus = "resolved via Character.GetMode<GamePhotoMode>()";
                            return true;
                        }

                        this.lastBirdPhotoModeResolveStatus = "Character.GetMode<GamePhotoMode>() returned null";
                        this.BirdFarmNetLog("PhotoMode resolver: Character.GetMode<GamePhotoMode>() returned null.");
                    }
                    else
                    {
                        this.BirdFarmNetLog("PhotoMode resolver: generic GetMode<T>() not found on Character.");
                    }
                }

                MethodInfo getModeByEnumMethod = characterObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetMode" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
                if (getModeByEnumMethod != null)
                {
                    Type gameplayModeEnumType = getModeByEnumMethod.GetParameters()[0].ParameterType;
                    try
                    {
                        object photoEnumValue = Enum.Parse(gameplayModeEnumType, "Photo");
                        object resolvedByEnum = getModeByEnumMethod.Invoke(characterObj, new object[] { photoEnumValue });
                        if (this.IsBirdPhotoModeCandidate(resolvedByEnum, gamePhotoModeType))
                        {
                            photoModeObj = resolvedByEnum;
                            source = "Character.GetMode(EGameplayMode.Photo)";
                            this.lastBirdPhotoModeResolveStatus = "resolved via Character.GetMode(EGameplayMode.Photo)";
                            return true;
                        }
                        this.lastBirdPhotoModeResolveStatus = "Character.GetMode(EGameplayMode.Photo) returned non-photo/null";
                        this.BirdFarmNetLog("PhotoMode resolver: Character.GetMode(EGameplayMode.Photo) returned non-photo/null object.");
                    }
                    catch (Exception ex)
                    {
                        this.lastBirdPhotoModeResolveStatus = "GetMode(EGameplayMode.Photo) threw " + ex.GetType().Name + ": " + ex.Message;
                        this.BirdFarmNetLog("PhotoMode resolver: GetMode(EGameplayMode.Photo) threw " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                else
                {
                    this.BirdFarmNetLog("PhotoMode resolver: non-generic GetMode(EGameplayMode) not found on Character.");
                }

                if (this.TryGetObjectMember(characterObj, "_states", out object statesObj) && statesObj is System.Collections.IEnumerable states)
                {
                    foreach (object state in states)
                    {
                        if (this.IsBirdPhotoModeCandidate(state, gamePhotoModeType))
                        {
                            photoModeObj = state;
                            source = "Character._states[photo-mode-candidate]";
                            this.lastBirdPhotoModeResolveStatus = "resolved via Character._states";
                            return true;
                        }
                    }

                    this.BirdFarmNetLog("PhotoMode resolver: Character._states present but no photo-mode candidate was found.");
                }
                else
                {
                    this.BirdFarmNetLog("PhotoMode resolver: Character._states unavailable.");
                }

                if (this.TryGetObjectMember(characterObj, "currMode", out object currModeObj) && currModeObj != null)
                {
                    if (this.IsBirdPhotoModeCandidate(currModeObj, gamePhotoModeType))
                    {
                        photoModeObj = currModeObj;
                        source = "Character.currMode";
                        this.lastBirdPhotoModeResolveStatus = "resolved via Character.currMode";
                        return true;
                    }
                }

                this.lastBirdPhotoModeResolveStatus = "Character fallback paths found no GamePhotoMode instance";
                this.BirdFarmNetLog("PhotoMode resolver: Character fallback paths found no GamePhotoMode instance.");
            }
            catch (Exception ex)
            {
                this.lastBirdPhotoModeResolveStatus = "Character path threw " + ex.GetType().Name + ": " + ex.Message;
                this.BirdFarmNetLog("PhotoMode resolver: Character path threw " + ex.GetType().Name + ": " + ex.Message);
            }

            return false;
        }

        private bool IsBirdPhotoModeCandidate(object candidate, Type exactGamePhotoModeType)
        {
            if (candidate == null)
            {
                return false;
            }

            Type candidateType = candidate.GetType();
            if (exactGamePhotoModeType != null && exactGamePhotoModeType.IsAssignableFrom(candidateType))
            {
                return true;
            }

            if (string.Equals(candidateType.Name, "GamePhotoMode", StringComparison.Ordinal))
            {
                return true;
            }

            bool hasSetPhotoType = candidateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(m => m.Name == "SetPhotoType" && m.GetParameters().Length == 1);
            if (!hasSetPhotoType)
            {
                return false;
            }

            bool hasUpdateAllComponent = candidateType.GetMethod("UpdateAllComponent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
            bool hasTickBirdTarget = candidateType.GetMethod("TickBirdTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
            bool hasPhotoTypeProperty = candidateType.GetProperty("photoType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null
                || candidateType.GetProperty("type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;

            return hasUpdateAllComponent && hasTickBirdTarget && hasPhotoTypeProperty;
        }

        public List<uint> GetLastBirdFarmSentNetIds()
        {
            return new List<uint>(this.lastBirdFarmSentNetIds);
        }

        public IReadOnlyList<uint> GetLastBirdFarmSentNetIdsView()
        {
            return this.lastBirdFarmSentNetIds;
        }

        public void BeginBirdFarmBurst()
        {
            this.birdFarmBurstSentNetIds.Clear();
        }

        public void RememberBirdFarmBurstNetId(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            this.birdFarmBurstSentNetIds.Add(netId);
        }

        public void EndBirdFarmBurst()
        {
            this.birdFarmBurstSentNetIds.Clear();
        }

        public bool TryConsumeRecentBirdFarmCapture(out uint netId)
        {
            netId = 0U;
            if (this.pendingBirdFarmAttemptedNetIds.Count == 0)
            {
                return false;
            }

            if (Time.unscaledTime - this.lastBirdFarmSendAt > 2.5f)
            {
                this.pendingBirdFarmAttemptedNetIds.Clear();
                this.lastBirdFarmAttemptedNetId = 0U;
                return false;
            }

            string toast = this.lastDetectedToast ?? string.Empty;
            if (toast.IndexOf("Target Bird does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                || toast.IndexOf("bird does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.lastBirdFarmAttemptedNetId = 0U;
                this.pendingBirdFarmAttemptedNetIds.Clear();
                return false;
            }

            netId = this.pendingBirdFarmAttemptedNetIds.Dequeue();
            this.lastBirdFarmAttemptedNetId = this.pendingBirdFarmAttemptedNetIds.Count > 0 ? this.pendingBirdFarmAttemptedNetIds.Peek() : 0U;
            return netId != 0U;
        }

        private bool TryPeekRecentBirdFarmAttempt(out uint netId)
        {
            netId = this.lastBirdFarmAttemptedNetId;
            if (netId != 0U)
            {
                return true;
            }

            if (this.pendingBirdFarmAttemptedNetIds.Count > 0)
            {
                netId = this.pendingBirdFarmAttemptedNetIds.Peek();
                return netId != 0U;
            }

            if (this.lastBirdFarmRecentPhotoNetId != 0U
                && Time.unscaledTime - this.lastBirdFarmRecentPhotoNetIdAt <= 8f)
            {
                netId = this.lastBirdFarmRecentPhotoNetId;
                return true;
            }

            return false;
        }

        private void RemovePendingBirdFarmAttempt(uint netId)
        {
            if (netId == 0U || this.pendingBirdFarmAttemptedNetIds.Count == 0)
            {
                return;
            }

            int count = this.pendingBirdFarmAttemptedNetIds.Count;
            for (int i = 0; i < count; i++)
            {
                uint pending = this.pendingBirdFarmAttemptedNetIds.Dequeue();
                if (pending != netId)
                {
                    this.pendingBirdFarmAttemptedNetIds.Enqueue(pending);
                }
            }

            this.lastBirdFarmAttemptedNetId = this.pendingBirdFarmAttemptedNetIds.Count > 0 ? this.pendingBirdFarmAttemptedNetIds.Peek() : 0U;
        }

        public void ConfirmRecentBirdFarmCapture(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            this.RememberRecentBirdFarmTarget(netId);
            this.RememberRecentBirdFarmNetId(netId, 30f);
        }

        /// <summary>
        /// Called by BirdNetFarm when a pending confirmation times out (server did not acknowledge).
        /// Blacklists the netId for 60 seconds to prevent spamming a bird the server has rejected.
        /// </summary>
        public void BlacklistBirdFarmNetId(uint netId, float seconds = 60f)
        {
            if (netId == 0U) return;
            this.RememberRecentBirdFarmNetId(netId, seconds);
            this.BirdFarmNetLog($"Blacklisted bird netId={netId} for {seconds:F0}s after pending timeout");
        }

        public void ClearBirdFarmRuntimeState()
        {
            this.lastBirdFarmSentNetIds.Clear();
            this.birdFarmBurstSentNetIds.Clear();
            this.recentBirdFarmPhotoNetIds.Clear();
            this.birdFarmPhotoCountByNetId.Clear();
            this.cachedBirdFarmAuraCandidates.Clear();
            this.birdFarmAuraPhotoModeScannablesBuffer.Clear();
            this.birdFarmAuraPhotoModeSeenNetIds.Clear();
            this.birdFarmAuraComponentBuffer.Clear();
            this.birdFarmAuraLevelEntityComponentsBuffer.Clear();
            this.birdFarmAuraStandComponentsBuffer.Clear();
            this.birdFarmAuraStateBuffer.Clear();
            this.cachedBirdFarmAuraCandidatesAt = -999f;
            this.cachedBirdFarmAuraNextScanAt = -999f;
            this.cachedBirdFarmAuraRange = -1f;
            this.cachedBirdFarmAuraOrigin = Vector3.zero;
            this.cachedBirdFarmAuraCacheTtl = 5f;
            this.cachedBirdFarmAuraMoveTolerance = 4f;
            this.cachedBirdFarmAuraEntityCount = 0;
            this.nextBirdFarmPhotoModeMissingBackoffAt = -999f;
            this.nextBirdFarmPhotoModeComponentRefreshAt = -999f;
            this.birdFarmPhotoModeListSuspectStale = true;
            this.nextBirdFarmManagedFallbackScanAt = -999f;
            this.nextBirdFarmCleanupAt = -999f;
            this.birdFarmDenseVerifyOffset = 0;
            this.birdFarmDenseEmptyScanStreak = 0;
            this._verifiedBirdEntityNetIds.Clear();
            this._rejectedBirdEntityNetIds.Clear();
            this._birdFarmResolvedDetailsByNetId.Clear();
            this.lastBirdFarmSendAt = -999f;
            this.lastBirdFarmAttemptedNetId = 0U;
            this.lastBirdFarmRecentPhotoNetId = 0U;
            this.lastBirdFarmRecentPhotoNetIdAt = -999f;
            this.pendingBirdFarmAttemptedNetIds.Clear();
        }

        public void PrewarmBirdFarmRuntime()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                this.TryResolveAuraMonoBirdPhotoMethod(out _, out _, out _);
                this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                this.TryResolveAuraMonoBirdPhotoDetailInfoFields();
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("PrewarmBirdFarmRuntime error: " + ex.Message);
            }
        }

        private bool ShouldSkipRecentBirdFarmNetId(uint netId)
        {
            if (netId != 0U && this.birdFarmBurstSentNetIds.Contains(netId))
            {
                return true;
            }

            if (this.birdFarmSpamMaxPhotoModeActive)
            {
                return false;
            }

            if (netId == 0U)
            {
                return false;
            }

            if (!this.recentBirdFarmPhotoNetIds.TryGetValue(netId, out float until))
            {
                return false;
            }

            if (Time.unscaledTime <= until)
            {
                return true;
            }

            this.recentBirdFarmPhotoNetIds.Remove(netId);
            return false;
        }

        private void RememberRecentBirdFarmNetId(uint netId, float durationSeconds = 180f)
        {
            if (netId == 0U)
            {
                return;
            }

            this.recentBirdFarmPhotoNetIds[netId] = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        }

        private void RememberRecentBirdFarmTarget(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            this.lastBirdFarmRecentPhotoNetId = netId;
            this.lastBirdFarmRecentPhotoNetIdAt = Time.unscaledTime;
        }

        private bool HasReachedBirdFarmPhotoLimit(uint netId)
        {
            // Bird Farm now relies on live bird state changes instead of a fixed per-netId cap.
            // A bird may be photographed repeatedly while it remains in a capturable state.
            return false;
        }

        private void RegisterBirdFarmPhoto(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            this.birdFarmPhotoCountByNetId.TryGetValue(netId, out int count);
            this.birdFarmPhotoCountByNetId[netId] = count + 1;
        }

        private void CleanupBirdFarmRuntimeCachesIfDue()
        {
            float now = Time.unscaledTime;
            if (now < this.nextBirdFarmCleanupAt)
            {
                return;
            }

            this.nextBirdFarmCleanupAt = now + BirdFarmCleanupInterval;
            this.CleanupRecentBirdFarmNetIds();
            this.CleanupBirdEntityVerificationCache();
        }

        private void CleanupRecentBirdFarmNetIds()
        {
            if (this.recentBirdFarmPhotoNetIds.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            this.birdFarmExpiredNetIdBuffer.Clear();
            foreach (KeyValuePair<uint, float> pair in this.recentBirdFarmPhotoNetIds)
            {
                if (pair.Value > now)
                {
                    continue;
                }

                this.birdFarmExpiredNetIdBuffer.Add(pair.Key);
            }

            if (this.birdFarmExpiredNetIdBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < this.birdFarmExpiredNetIdBuffer.Count; i++)
            {
                this.recentBirdFarmPhotoNetIds.Remove(this.birdFarmExpiredNetIdBuffer[i]);
            }
        }

        private void CleanupBirdEntityVerificationCache()
        {
            // Prevent memory leaks by cleaning up expired bird entity verification cache entries
            if (this._verifiedBirdEntityNetIds.Count == 0 && this._rejectedBirdEntityNetIds.Count == 0 && this._birdFarmResolvedDetailsByNetId.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            this.birdFarmExpiredNetIdBuffer.Clear();

            foreach (KeyValuePair<uint, float> pair in this._verifiedBirdEntityNetIds)
            {
                if (pair.Value > now)
                {
                    continue;
                }
                this.birdFarmExpiredNetIdBuffer.Add(pair.Key);
            }

            if (this.birdFarmExpiredNetIdBuffer.Count > 0)
            {
                for (int i = 0; i < this.birdFarmExpiredNetIdBuffer.Count; i++)
                {
                    this._verifiedBirdEntityNetIds.Remove(this.birdFarmExpiredNetIdBuffer[i]);
                }
                this.birdFarmExpiredNetIdBuffer.Clear();
            }

            foreach (KeyValuePair<uint, float> pair in this._rejectedBirdEntityNetIds)
            {
                if (pair.Value > now)
                {
                    continue;
                }
                this.birdFarmExpiredNetIdBuffer.Add(pair.Key);
            }

            if (this.birdFarmExpiredNetIdBuffer.Count > 0)
            {
                for (int i = 0; i < this.birdFarmExpiredNetIdBuffer.Count; i++)
                {
                    this._rejectedBirdEntityNetIds.Remove(this.birdFarmExpiredNetIdBuffer[i]);
                }
                this.birdFarmExpiredNetIdBuffer.Clear();
            }

            foreach (KeyValuePair<uint, BirdFarmAuraResolvedDetail> pair in this._birdFarmResolvedDetailsByNetId)
            {
                if (pair.Value != null && pair.Value.ExpiresAt > now)
                {
                    continue;
                }
                this.birdFarmExpiredNetIdBuffer.Add(pair.Key);
            }

            if (this.birdFarmExpiredNetIdBuffer.Count > 0)
            {
                for (int i = 0; i < this.birdFarmExpiredNetIdBuffer.Count; i++)
                {
                    this._birdFarmResolvedDetailsByNetId.Remove(this.birdFarmExpiredNetIdBuffer[i]);
                }
                this.birdFarmExpiredNetIdBuffer.Clear();
            }
        }

        private bool TryGetCurrentScannerBirdTarget(out uint netId, out int staticId, out float distance, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            status = "No scanner bird target";

            try
            {
                if (this.cachedScannerStatusPanelGetScanningBirdNetIdMethod == null)
                {
                    Type scannerPanelType = this.FindLoadedType("XDTGame.UI.Panel.ScannerStatusPanel", "ScannerStatusPanel");
                    if (scannerPanelType != null)
                    {
                        this.cachedScannerStatusPanelGetScanningBirdNetIdMethod = scannerPanelType.GetMethod("GetScanningBirdNetId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }
                }

                if (this.cachedScannerStatusPanelGetScanningBirdNetIdMethod != null)
                {
                    object rawNetId = this.cachedScannerStatusPanelGetScanningBirdNetIdMethod.Invoke(null, null);
                    if (this.TryConvertToUInt(rawNetId, out uint scannerNetId))
                    {
                        netId = scannerNetId;
                    }
                }

                if (netId == 0U)
                {
                    status = "No scanner bird target";
                    return false;
                }

                if (this.cachedEntityUtilGetEntityResIdMethod == null)
                {
                    Type entityUtilType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "EntityUtil");
                    if (entityUtilType != null)
                    {
                        this.cachedEntityUtilGetEntityResIdMethod = entityUtilType.GetMethod("GetEntityResId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(uint) }, null);
                    }
                }

                if (this.cachedEntityUtilGetEntityResIdMethod == null)
                {
                    status = "EntityUtil.GetEntityResId unavailable";
                    return false;
                }

                object rawStaticId = this.cachedEntityUtilGetEntityResIdMethod.Invoke(null, new object[] { netId });
                try
                {
                    staticId = Convert.ToInt32(rawStaticId);
                }
                catch
                {
                    staticId = 0;
                }

                if (staticId <= 0)
                {
                    status = $"Scanner target netId={netId} has no staticId";
                    return false;
                }

                if (this.TryGetScannerBirdDistance(netId, out float scannerDistance))
                {
                    distance = scannerDistance;
                }

                status = "Scanner target ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Scanner target exception: " + ex.Message;
                return false;
            }
        }

        private bool TryFindNearestBirdEntityTarget(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;
            source = "none";
            status = "No bird entity targets found";

            try
            {
                string photoModeEntityStatus = null;

                if (this.TryTakeBirdFarmAuraCandidateFromCache(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount))
                {
                    source = "BirdFarm.CandidateCache";
                    status = "Cached bird target ready";
                    return true;
                }

                if (this.TryFindNearestBirdEntityViaAuraMonoPhotoModeBirdScannables(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount, out source, out status))
                {
                    return true;
                }
                photoModeEntityStatus = status;
                int photoModeDetectedCount = detectedCount;

                float now = Time.unscaledTime;
                if (now < this.nextBirdFarmManagedFallbackScanAt)
                {
                    status = this.NormalizeBirdFarmScanStatus(photoModeEntityStatus);
                    detectedCount = photoModeDetectedCount;
                    return false;
                }

                this.nextBirdFarmManagedFallbackScanAt = now + BirdFarmManagedFallbackScanInterval;

                if (this.TryFindNearestBirdEntityViaPhotoModeBirdScannables(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount, out source, out status))
                {
                    return true;
                }
                photoModeEntityStatus = this.MergeBirdFarmScanStatus(photoModeEntityStatus, status);
                photoModeDetectedCount = Math.Max(photoModeDetectedCount, detectedCount);

                if (this.TryFindNearestBirdEntityViaBirdScannables(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount, out source, out status))
                {
                    return true;
                }
                detectedCount = Math.Max(detectedCount, photoModeDetectedCount);

                if (birdFarmDisableAuraEntityScan)
                {
                    status = this.MergeBirdFarmScanStatus(photoModeEntityStatus, status);
                    status = string.IsNullOrWhiteSpace(status) ? "Waiting for bird scannable target" : status;
                    return false;
                }

                if (this.TryFindNearestBirdEntityViaAuraMonoLoadedEntities(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount, out source, out status))
                {
                    return true;
                }

                status = this.MergeBirdFarmScanStatus(photoModeEntityStatus, status);
                return false;
            }
            catch (Exception ex)
            {
                status = "Bird entity scan exception: " + ex.Message;
                return false;
            }
        }

        private string MergeBirdFarmScanStatus(string existingStatus, string nextStatus)
        {
            if (string.IsNullOrWhiteSpace(nextStatus))
            {
                return existingStatus;
            }

            if (nextStatus.StartsWith("No fresh birds available", StringComparison.Ordinal))
            {
                return nextStatus;
            }

            if (string.IsNullOrWhiteSpace(existingStatus))
            {
                return nextStatus;
            }

            if (existingStatus.StartsWith("No fresh birds available", StringComparison.Ordinal))
            {
                return existingStatus;
            }

            if (nextStatus.StartsWith("BirdScannable runtime type unavailable", StringComparison.Ordinal)
                && (existingStatus.StartsWith("PhotoMode unavailable:", StringComparison.Ordinal)
                    || existingStatus.StartsWith("Aura PhotoMode unavailable:", StringComparison.Ordinal)
                    || existingStatus.StartsWith("Aura PhotoMode bird list empty", StringComparison.Ordinal)))
            {
                return this.NormalizeBirdFarmScanStatus(existingStatus);
            }

            if (existingStatus.StartsWith("PhotoMode unavailable:", StringComparison.Ordinal)
                || existingStatus.StartsWith("Aura PhotoMode unavailable:", StringComparison.Ordinal))
            {
                return nextStatus;
            }

            if (string.Equals(existingStatus, nextStatus, StringComparison.Ordinal))
            {
                return existingStatus;
            }

            return existingStatus + "; " + nextStatus;
        }

        private string NormalizeBirdFarmScanStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            if (status.StartsWith("Aura PhotoMode bird list empty", StringComparison.Ordinal))
            {
                return "Waiting for Aura PhotoMode bird list";
            }

            return status;
        }

        private bool TryRefreshAuraMonoBirdPhotoModeComponents(IntPtr photoModeObj)
        {
            if (photoModeObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (now < this.nextBirdFarmPhotoModeComponentRefreshAt)
            {
                return false;
            }

            this.nextBirdFarmPhotoModeComponentRefreshAt = now + BirdFarmPhotoModeComponentRefreshInterval;
            try
            {
                IntPtr photoModeClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(photoModeObj) : IntPtr.Zero;
                IntPtr updateAllComponentMethod = this.FindAuraMonoMethodOnHierarchy(photoModeClass, "UpdateAllComponent", 0);
                if (updateAllComponentMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(updateAllComponentMethod, photoModeObj, IntPtr.Zero, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private bool TryRefreshManagedBirdPhotoModeComponents(object photoModeObj)
        {
            if (photoModeObj == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (now < this.nextBirdFarmPhotoModeComponentRefreshAt)
            {
                return false;
            }

            this.nextBirdFarmPhotoModeComponentRefreshAt = now + BirdFarmManagedFallbackScanInterval;
            try
            {
                MethodInfo updateAllComponentMethod = photoModeObj.GetType().GetMethod("UpdateAllComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updateAllComponentMethod?.Invoke(photoModeObj, null);
                return updateAllComponentMethod != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryFindNearestBirdEntityViaAuraMonoPhotoModeBirdScannables(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;
            source = "none";
            status = "Aura PhotoMode bird list unavailable";

            try
            {
                if (!this.TryResolveAuraMonoGamePhotoModeObject(out IntPtr photoModeObj, out string photoModeSource) || photoModeObj == IntPtr.Zero)
                {
                    status = "Aura PhotoMode unavailable: " + photoModeSource;
                    return false;
                }

                // The game only maintains _birdScannables while the scanner mode is actively
                // ticking; with the scanner lowered the list keeps its last snapshot, and after a
                // capture wave that snapshot is all despawned/never-capturable entries. A non-empty
                // stale list never hit the empty-branch refresh below, so the farm wedged until the
                // user pressed F (which re-activates the mode and re-runs UpdateAllComponent).
                // When the previous pass yielded nothing usable, run that same refresh ourselves.
                if (this.birdFarmPhotoModeListSuspectStale && this.TryRefreshAuraMonoBirdPhotoModeComponents(photoModeObj))
                {
                    this.birdFarmPhotoModeListSuspectStale = false;
                }

                if (!this.TryGetMonoObjectMember(photoModeObj, "_birdScannables", out IntPtr birdListObj) || birdListObj == IntPtr.Zero)
                {
                    status = "Aura PhotoMode _birdScannables unavailable";
                    return false;
                }

                // Scannables are raw MonoObject*; the loop below reads members of each (boxing =
                // mono-side allocations), and SGen can move not-yet-processed items meanwhile.
                // Pin every item at enumeration time, release after the loop scalarizes them.
                List<IntPtr> scannables = this.birdFarmAuraPhotoModeScannablesBuffer;
                List<uint> scannablePins = this.birdFarmAuraPhotoModeScannablePins;
                scannables.Clear();
                FreeAuraMonoPins(scannablePins);
                int noEntityCount = 0;
                int noNetIdCount = 0;
                int filteredCount = 0;
                int noStaticIdCount = 0;
                int notBirdCount = 0;
                int outOfRangeCount = 0;
                int unresolvedActionCount = 0;
                int uncapturableCount = 0;
                try
                {
                if (!this.TryEnumerateAuraMonoCollectionItems(birdListObj, scannables, scannablePins) || scannables.Count == 0)
                {
                    if (!this.TryRefreshAuraMonoBirdPhotoModeComponents(photoModeObj)
                        || !this.TryEnumerateAuraMonoCollectionItems(birdListObj, scannables, scannablePins)
                        || scannables.Count == 0)
                    {
                        status = "Aura PhotoMode bird list empty";
                        this.birdFarmPhotoModeListSuspectStale = true;
                        return false;
                    }
                }

                HashSet<uint> seenNetIds = this.birdFarmAuraPhotoModeSeenNetIds;
                seenNetIds.Clear();
                this.cachedBirdFarmAuraCandidates.Clear();
                this.cachedBirdFarmAuraCandidatesAt = Time.unscaledTime;
                this.cachedBirdFarmAuraOrigin = playerPos;
                this.cachedBirdFarmAuraRange = scanRange;
                this.cachedBirdFarmAuraCacheTtl = 3f;
                this.cachedBirdFarmAuraMoveTolerance = 3f;
                this.cachedBirdFarmAuraEntityCount = scannables.Count;
                this._auraMonoBirdRadarPositions.Clear();
                for (int i = 0; i < scannables.Count; i++)
                {
                    IntPtr scannableObj = scannables[i];
                    if (scannableObj == IntPtr.Zero)
                    {
                        noEntityCount++;
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(scannableObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        noEntityCount++;
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint candidateNetId) || candidateNetId == 0U)
                    {
                        noNetIdCount++;
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId) || this.ShouldSkipRecentBirdFarmNetId(candidateNetId) || this.HasReachedBirdFarmPhotoLimit(candidateNetId))
                    {
                        filteredCount++;
                        continue;
                    }

                    IntPtr dataObj = IntPtr.Zero;
                    bool hasDataObj = (this.TryGetMonoObjectMember(scannableObj, "data", out dataObj) || this.TryGetMonoObjectMember(scannableObj, "_data", out dataObj)) && dataObj != IntPtr.Zero;
                    float now = Time.unscaledTime;
                    bool hasResolvedDetailCache = this.TryGetCachedBirdFarmAuraResolvedDetail(candidateNetId, now, out BirdFarmAuraResolvedDetail cachedDetail);
                    int candidateStaticId = hasResolvedDetailCache ? cachedDetail.StaticId : 0;
                    if (candidateStaticId <= 0 && hasDataObj)
                    {
                        candidateStaticId = this.TryReadBirdStaticIdViaAuraMonoObject(dataObj);
                    }
                    if (candidateStaticId <= 0)
                    {
                        candidateStaticId = this.TryReadBirdStaticIdViaAuraMonoObject(scannableObj);
                    }
                    if (candidateStaticId <= 0)
                    {
                        candidateStaticId = this.TryReadLevelEntityStaticIdViaAuraMonoEntity(entityObj);
                    }
                    if (candidateStaticId <= 0)
                    {
                        candidateStaticId = this.TryReadBirdStaticIdViaAuraMono(entityObj);
                    }
                    if (candidateStaticId <= 0)
                    {
                        noStaticIdCount++;
                        continue;
                    }

                    // Same gate the take-side applies (StaticId >= 10000 / bird id families).
                    // Without it, non-bird scannables (pendant decorations, camouflage) enter the
                    // cache only to be discarded by every take → "cached target unavailable" loop.
                    if (!this.IsBirdFarmStaticIdLikelyBird(candidateStaticId))
                    {
                        notBirdCount++;
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 candidatePosition))
                    {
                        candidatePosition = playerPos;
                    }

                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    if (candidateDistance > scanRange)
                    {
                        outOfRangeCount++;
                        continue;
                    }

                    int candidateActionType = hasResolvedDetailCache ? cachedDetail.BirdActionType : 0;
                    int candidateBirdState = hasResolvedDetailCache ? cachedDetail.BirdState : -1;
                    bool candidateIsPerchBird = hasResolvedDetailCache && cachedDetail.IsPerchBird;
                    int scannableActionType = candidateActionType;
                    if (hasDataObj)
                    {
                        this.TryGetMonoInt32Member(dataObj, "pose", out scannableActionType);
                        candidateActionType = scannableActionType;
                    }

                    uint candidateStandNetId = hasResolvedDetailCache ? cachedDetail.BirdStandNetId : 0U;
                    if (!hasResolvedDetailCache)
                    {
                        if (this.TryGetBirdPhotoDetailsViaAuraMonoEntity(entityObj, out int detailActionType, out candidateBirdState, out candidateStandNetId, out candidateIsPerchBird)
                            && detailActionType != 0)
                        {
                            candidateActionType = detailActionType;
                        }
                    }
                    else if (candidateIsPerchBird && candidateStandNetId == 0U)
                    {
                        this.TryGetAuraMonoBirdStandNetIdFromEntity(entityObj, out candidateStandNetId);
                    }

                    if (candidateIsPerchBird && candidateStandNetId == 0U)
                    {
                        unresolvedActionCount++;
                        continue;
                    }

                    if (!hasResolvedDetailCache)
                    {
                        this.CacheBirdFarmAuraResolvedDetail(candidateNetId, candidateStaticId, candidateActionType, candidateBirdState, candidateStandNetId, candidateIsPerchBird, now + BirdEntityVerifyCacheTtl);
                    }

                    // Take-side state gate applied at accept time: fly/alert/interval birds would
                    // be removed by every TryTakeBirdFarmAuraCandidateFromCache anyway, and a cache
                    // made only of them reads as "cached target unavailable" forever.
                    if (!this.IsBirdFarmStateCapturable(candidateBirdState, out _))
                    {
                        uncapturableCount++;
                        continue;
                    }

                    detectedCount++;
                    this.cachedBirdFarmAuraCandidates.Add(new BirdFarmAuraCandidate
                    {
                        NetId = candidateNetId,
                        StaticId = candidateStaticId,
                        Distance = candidateDistance,
                        BirdActionType = candidateActionType,
                        BirdState = candidateBirdState,
                        BirdStandNetId = candidateStandNetId,
                        IsPerchBird = candidateIsPerchBird
                    });
                    this._auraMonoBirdRadarPositions.Add(candidatePosition);
                }
                }
                finally
                {
                    // Candidates are fully scalarized above — the raw pointers are dead now.
                    FreeAuraMonoPins(scannablePins);
                }

                if (this.cachedBirdFarmAuraCandidates.Count == 0)
                {
                    // Nothing usable came out of a non-empty list — treat the snapshot as stale
                    // and force the game-side UpdateAllComponent refresh on the next pass (also
                    // covers the pure "recently attempted" case, where the refresh is harmless).
                    this.birdFarmPhotoModeListSuspectStale = true;

                    if (scannables.Count > 0
                        && detectedCount == 0
                        && filteredCount > 0
                        && filteredCount + noEntityCount + noNetIdCount + noStaticIdCount + notBirdCount + outOfRangeCount + unresolvedActionCount + uncapturableCount >= scannables.Count)
                    {
                        status = $"No fresh birds available ({filteredCount}/{scannables.Count} recently attempted or confirmed)";
                        return false;
                    }

                    if (unresolvedActionCount + uncapturableCount > 0 && detectedCount == 0)
                    {
                        status = $"Waiting for capturable bird pose ({unresolvedActionCount + uncapturableCount}/{scannables.Count} birds unresolved or not capturable)";
                        return false;
                    }

                    status = $"Aura PhotoMode no target list={scannables.Count} noEntity={noEntityCount} noNet={noNetIdCount} filtered={filteredCount} unresolved={unresolvedActionCount} uncap={uncapturableCount} notBird={notBirdCount} noStatic={noStaticIdCount} outRange={outOfRangeCount} accepted={detectedCount}";
                    return false;
                }

                this.cachedBirdFarmAuraCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (!this.TryTakeBirdFarmAuraCandidateFromCache(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out int cachedDetectedCount))
                {
                    this.birdFarmPhotoModeListSuspectStale = true;
                    status = "Aura PhotoMode cached target unavailable";
                    return false;
                }

                detectedCount = Mathf.Max(detectedCount, cachedDetectedCount);
                source = "AuraMono.GamePhotoMode._birdScannables/" + photoModeSource;
                status = "Aura PhotoMode bird target ready";
                this.birdFarmPhotoModeListSuspectStale = false;
                return true;
            }
            catch (Exception ex)
            {
                status = "Aura PhotoMode bird list exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetAuraMonoBirdStandNetIdFromEntity(IntPtr entityObj, out uint standNetId)
        {
            standNetId = 0U;
            if (entityObj == IntPtr.Zero || !this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = this.birdFarmAuraStandComponentsBuffer;
            components.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            for (int i = 0; i < components.Count; i++)
            {
                IntPtr componentObj = components[i];
                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass != null ? auraMonoObjectGetClass(componentObj) : IntPtr.Zero);
                if (className.IndexOf("PerchBirdComponent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if ((this.TryGetMonoObjectMember(componentObj, "ComponentData", out IntPtr componentDataObj) || this.TryGetMonoObjectMember(componentObj, "_componentData", out componentDataObj)) && componentDataObj != IntPtr.Zero
                    && this.TryGetMonoObjectMember(componentDataObj, "perch", out IntPtr perchObj) && perchObj != IntPtr.Zero
                    && this.TryGetMonoObjectMember(perchObj, "entity", out IntPtr standEntityObj) && standEntityObj != IntPtr.Zero
                    && this.TryGetAuraMonoEntityNetId(standEntityObj, out standNetId) && standNetId != 0U)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindNearestBirdEntityViaAuraMonoLoadedEntities(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;
            source = "none";
            status = "Scanning...";

            try
            {
                bool stationaryThrottleActive = BirdNetFarm.IsStationaryThrottleActive();
                int noTargetTicks = BirdNetFarm.GetConsecutiveNoTargetTicks();
                float scanRefreshSeconds = stationaryThrottleActive ? Mathf.Clamp(8f + (noTargetTicks * 0.5f), 8f, 12f) : 4f;
                int maxEntitiesToInspect = stationaryThrottleActive ? 96 : int.MaxValue;

                if (this.TryTakeBirdFarmAuraCandidateFromCache(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out detectedCount))
                {
                    source = "AuraMono.Entities.Cache";
                    status = "Target ready";
                    return true;
                }

                if (detectedCount > 0)
                {
                    // Cached birds were present, but every cached candidate became invalid
                    // by the time we tried to consume it (temporary blacklist, state change,
                    // etc.). Do not sit on the stale cache for 8-16s; force an immediate
                    // refresh so nearby birds can be reconsidered right away.
                    this.cachedBirdFarmAuraCandidates.Clear();
                    this.cachedBirdFarmAuraCandidatesAt = -999f;
                    this.cachedBirdFarmAuraNextScanAt = -999f;
                    this.cachedBirdFarmAuraEntityCount = 0;
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan cache invalidated after filtered candidates detected={detectedCount}");
                }

                if (Time.unscaledTime < this.cachedBirdFarmAuraNextScanAt)
                {
                    status = "Waiting for bird scan refresh";
                    return false;
                }

                BirdNetFarm.TraceCrashBreadcrumb($"Scan start range={scanRange:F1} cacheCount={this.cachedBirdFarmAuraCandidates.Count} nextScanAt={this.cachedBirdFarmAuraNextScanAt:F2} stationary={stationaryThrottleActive} interval={scanRefreshSeconds:F1} inspectCap={maxEntitiesToInspect}");
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    BirdNetFarm.TraceCrashBreadcrumb("Scan unavailable: AuraMono API not ready");
                    status = "Scanner unavailable";
                    return false;
                }

                // Prefer the direct-ECS bird component query (GetComponents<BirdComponent/
                // BirdScannableComponent/PerchBirdComponent>): it touches only bird entities, so it
                // avoids the crash-prone full entity-graph walk on dense/streaming fields and is far
                // cheaper. Fall back to the legacy walk only when GetComponents is unavailable.
                List<IntPtr> entityObjects;
                string enumerateStatus;
                bool birdEntitiesViaComponents = this.TryCollectBirdFarmAuraBirdEntities(out entityObjects, out enumerateStatus);
                if (birdEntitiesViaComponents)
                {
                    BirdNetFarm.TraceCrashBreadcrumb("Scan via GetComponents birdEntities=" + entityObjects.Count);
                }
                else if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out entityObjects, out enumerateStatus))
                {
                    BirdNetFarm.TraceCrashBreadcrumb("Scan enumerate failed: " + enumerateStatus);
                    this.cachedBirdFarmAuraNextScanAt = Time.unscaledTime + scanRefreshSeconds;
                    status = enumerateStatus;
                    this.BirdFarmNetLog("Aura mono entity path unavailable: " + enumerateStatus);
                    return false;
                }

                BirdNetFarm.TraceCrashBreadcrumb($"Scan enumerated entityCount={entityObjects.Count}");
                if (entityObjects.Count >= MaxAuraMonoEntities)
                {
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan entity enumeration hit cap={MaxAuraMonoEntities}; nearby birds may be past earlier non-bird entities");
                }
                this.BirdFarmNetLog("Aura mono entity enumeration collected " + entityObjects.Count + " entity objects.");

                this.cachedBirdFarmAuraCandidates.Clear();
                this._auraMonoBirdRadarPositions.Clear(); // Reset radar feed each fresh scan
                this.cachedBirdFarmAuraCandidatesAt = Time.unscaledTime;
                this.cachedBirdFarmAuraEntityCount = entityObjects.Count;
                this.cachedBirdFarmAuraCacheTtl = 5f;
                this.cachedBirdFarmAuraMoveTolerance = 4f;
                if (entityObjects.Count >= 300)
                {
                    scanRefreshSeconds = Mathf.Max(scanRefreshSeconds, 9f);
                    this.cachedBirdFarmAuraCacheTtl = 10f;
                    this.cachedBirdFarmAuraMoveTolerance = 8f;
                }
                else if (entityObjects.Count >= 220)
                {
                    scanRefreshSeconds = Mathf.Max(scanRefreshSeconds, 7f);
                    this.cachedBirdFarmAuraCacheTtl = 8f;
                    this.cachedBirdFarmAuraMoveTolerance = 6f;
                }
                else if (entityObjects.Count >= 140)
                {
                    scanRefreshSeconds = Mathf.Max(scanRefreshSeconds, 5.5f);
                    this.cachedBirdFarmAuraCacheTtl = 6.5f;
                    this.cachedBirdFarmAuraMoveTolerance = 5f;
                }
                else if (stationaryThrottleActive && entityObjects.Count >= 250)
                {
                    scanRefreshSeconds = Mathf.Max(scanRefreshSeconds, 10f);
                }
                this.cachedBirdFarmAuraNextScanAt = Time.unscaledTime + scanRefreshSeconds;
                this.cachedBirdFarmAuraOrigin = playerPos;
                this.cachedBirdFarmAuraRange = scanRange;
                BirdNetFarm.TraceCrashBreadcrumb($"Scan cache tuned entityCount={entityObjects.Count} nextScanDelay={scanRefreshSeconds:F1} cacheTtl={this.cachedBirdFarmAuraCacheTtl:F1} moveTol={this.cachedBirdFarmAuraMoveTolerance:F1}");

                this._birdFarmSeenNetIds.Clear();
                HashSet<uint> seenNetIds = this._birdFarmSeenNetIds;
                List<BirdFarmAuraInspectCandidate> inspectCandidates = new List<BirdFarmAuraInspectCandidate>(Mathf.Min(entityObjects.Count, 256));
                for (int i = 0; i < entityObjects.Count; i++)
                {
                    IntPtr entityObj = entityObjects[i];
                    if (entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint candidateNetId) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId))
                    {
                        continue;
                    }

                    if (this.ShouldSkipRecentBirdFarmNetId(candidateNetId))
                    {
                        continue;
                    }

                    if (this.HasReachedBirdFarmPhotoLimit(candidateNetId))
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 candidatePosition))
                    {
                        continue;
                    }

                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    if (candidateNetId >= 0x80000000U || candidateDistance < 1.5f)
                    {
                        continue;
                    }

                    if (scanRange > 0f && candidateDistance > scanRange)
                    {
                        continue;
                    }

                    inspectCandidates.Add(new BirdFarmAuraInspectCandidate
                    {
                        EntityObj = entityObj,
                        NetId = candidateNetId,
                        Position = candidatePosition,
                        Distance = candidateDistance
                    });
                }

                inspectCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                int entityInspectCount = maxEntitiesToInspect == int.MaxValue
                    ? inspectCandidates.Count
                    : Mathf.Min(inspectCandidates.Count, maxEntitiesToInspect);
                int verifyLimit = entityInspectCount;
                if (entityInspectCount >= 500)
                {
                    verifyLimit = Mathf.Min(entityInspectCount, 64);
                }
                else if (entityInspectCount >= 400)
                {
                    verifyLimit = Mathf.Min(entityInspectCount, 80);
                }
                else if (entityInspectCount >= 300)
                {
                    verifyLimit = Mathf.Min(entityInspectCount, 96);
                }
                // Keep crash breadcrumbs out of the per-entity hot path; synchronous file IO here tanks FPS.
                bool traceEntityStages = false;
                if (inspectCandidates.Count > entityInspectCount)
                {
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan truncating entity inspection to nearest {entityInspectCount}/{inspectCandidates.Count}");
                }
                if (verifyLimit < entityInspectCount)
                {
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan verify limited to nearest {verifyLimit}/{entityInspectCount} dense candidates");
                }

                int verifyStartIndex = 0;
                bool rotatingDenseVerifyWindow = verifyLimit < entityInspectCount;
                if (rotatingDenseVerifyWindow && entityInspectCount > 0)
                {
                    verifyStartIndex = this.birdFarmDenseVerifyOffset % entityInspectCount;
                    this.birdFarmDenseVerifyOffset = (verifyStartIndex + verifyLimit) % entityInspectCount;
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan verify window offset={verifyStartIndex} limit={verifyLimit} candidates={entityInspectCount}");
                }
                else
                {
                    this.birdFarmDenseVerifyOffset = 0;
                }

                int verifiedCandidates = 0;
                for (int scanSlot = 0; scanSlot < entityInspectCount; scanSlot++)
                {
                    int i = rotatingDenseVerifyWindow ? (verifyStartIndex + scanSlot) % entityInspectCount : scanSlot;
                    BirdFarmAuraInspectCandidate inspectCandidate = inspectCandidates[i];
                    IntPtr entityObj = inspectCandidate.EntityObj;
                    if (entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (traceEntityStages && (scanSlot % 32) == 0)
                    {
                        BirdNetFarm.TraceCrashBreadcrumb($"Scan entity progress index={scanSlot}/{entityInspectCount}");
                    }

                    uint candidateNetId = inspectCandidate.NetId;

                    Vector3 candidatePosition = inspectCandidate.Position;
                    // Cheap checks first - before any expensive Mono component invocations.
                    // Skip entities with NetIds in the high local-only range (>= 0x80000000).
                    float candidateDistance = inspectCandidate.Distance;

                    if (verifiedCandidates >= verifyLimit)
                    {
                        break;
                    }

                    verifiedCandidates++;
                    if (traceEntityStages)
                    {
                        BirdNetFarm.TraceCrashBreadcrumb($"Scan entity candidate index={i} netId={candidateNetId} dist={candidateDistance:F2} stage=verify slot={verifiedCandidates}/{verifyLimit}");
                    }

                    // Component check: use per-netId cache to avoid repeated GetAllComponents invocations.
                    float now = Time.unscaledTime;
                    if (this._rejectedBirdEntityNetIds.TryGetValue(candidateNetId, out float rejExpiry) && now < rejExpiry)
                    {
                        continue; // Already confirmed NOT a bird recently - skip expensive check
                    }

                    bool hasBirdComponent;
                    if (this._verifiedBirdEntityNetIds.TryGetValue(candidateNetId, out float verExpiry) && now < verExpiry)
                    {
                        hasBirdComponent = true; // Already confirmed IS a bird recently
                    }
                    else
                    {
                        hasBirdComponent = this.TryHasBirdComponentViaAuraMono(entityObj);
                        if (hasBirdComponent)
                            this._verifiedBirdEntityNetIds[candidateNetId] = now + BirdEntityVerifyCacheTtl;
                        else
                            this._rejectedBirdEntityNetIds[candidateNetId] = now + BirdEntityVerifyCacheTtl;
                    }

                    if (!hasBirdComponent)
                    {
                        continue;
                    }

                    int candidateStaticId;
                    int candidateBirdActionType;
                    int candidateBirdState;
                    uint candidateBirdStandNetId;
                    bool candidateIsPerchBird;
                    bool hasResolvedDetailCache = this.TryGetCachedBirdFarmAuraResolvedDetail(candidateNetId, now, out BirdFarmAuraResolvedDetail cachedDetail);
                    if (hasResolvedDetailCache)
                    {
                        candidateStaticId = cachedDetail.StaticId;
                        candidateBirdActionType = cachedDetail.BirdActionType;
                        candidateBirdState = cachedDetail.BirdState;
                        candidateBirdStandNetId = cachedDetail.BirdStandNetId;
                        candidateIsPerchBird = cachedDetail.IsPerchBird;
                    }
                    else
                    {
                        if (traceEntityStages)
                        {
                            BirdNetFarm.TraceCrashBreadcrumb($"Scan entity candidate index={i} netId={candidateNetId} stage=staticId");
                        }

                        candidateStaticId = this.TryReadBirdStaticIdViaAuraMono(entityObj);
                        if (candidateStaticId <= 0)
                        {
                            candidateStaticId = this.TryGetEntityStaticId(candidateNetId);
                        }

                        candidateBirdActionType = 0;
                        candidateBirdState = -1;
                        candidateBirdStandNetId = 0U;
                        candidateIsPerchBird = false;
                        if (traceEntityStages)
                        {
                            BirdNetFarm.TraceCrashBreadcrumb($"Scan entity candidate index={i} netId={candidateNetId} stage=details");
                        }

                        this.TryGetBirdPhotoDetailsViaAuraMonoEntity(entityObj, out candidateBirdActionType, out candidateBirdState, out candidateBirdStandNetId, out candidateIsPerchBird);
                    }

                    if (!this.IsBirdFarmStaticIdLikelyBird(candidateStaticId))
                    {
                        this.BirdFarmNetLog($"Skipping non-bird staticId: netId={candidateNetId} staticId={candidateStaticId}");
                        continue;
                    }

                    if (candidateIsPerchBird && candidateBirdStandNetId == 0U)
                    {
                        continue;
                    }

                    if (!this.IsBirdFarmStateCapturable(candidateBirdState, out string birdStateStatus))
                    {
                        this.BirdFarmNetLog($"Skipping bird by state: netId={candidateNetId} staticId={candidateStaticId} state={this.GetBirdFarmStateName(candidateBirdState)} ({candidateBirdState}) status={birdStateStatus}");
                        continue;
                    }

                    if (!hasResolvedDetailCache)
                    {
                        this.CacheBirdFarmAuraResolvedDetail(candidateNetId, candidateStaticId, candidateBirdActionType, candidateBirdState, candidateBirdStandNetId, candidateIsPerchBird, now + BirdEntityVerifyCacheTtl);
                    }

                    detectedCount++;
                    this.BirdFarmNetLog($"Aura mono candidate: netId={candidateNetId} staticId={candidateStaticId} dist={candidateDistance:F2}");
                    this.cachedBirdFarmAuraCandidates.Add(new BirdFarmAuraCandidate
                    {
                        NetId = candidateNetId,
                        StaticId = candidateStaticId,
                        Distance = candidateDistance,
                        BirdActionType = candidateBirdActionType,
                        BirdState = candidateBirdState,
                        BirdStandNetId = candidateBirdStandNetId,
                        IsPerchBird = candidateIsPerchBird
                    });
                    // Also populate radar position list so the bird radar can display without its own Mono scan.
                    // Only add valid birds (staticId > 1) to the radar to avoid showing invalid targets.
                    this._auraMonoBirdRadarPositions.Add(candidatePosition);
                }

                if (this.cachedBirdFarmAuraCandidates.Count == 0)
                {
                    bool denseEmptyScan = entityObjects.Count >= 300 || inspectCandidates.Count >= 128;
                    if (denseEmptyScan)
                    {
                        this.birdFarmDenseEmptyScanStreak++;
                    }
                    else
                    {
                        this.birdFarmDenseEmptyScanStreak = 0;
                    }

                    float emptyScanDelay;
                    if (entityObjects.Count >= MaxAuraMonoEntities)
                    {
                        emptyScanDelay = stationaryThrottleActive ? 30f : 15f;
                    }
                    else if (entityObjects.Count >= 300)
                    {
                        emptyScanDelay = stationaryThrottleActive
                            ? Mathf.Clamp(18f + (this.birdFarmDenseEmptyScanStreak * 8f), 18f, 45f)
                            : 12f;
                    }
                    else if (inspectCandidates.Count >= 128)
                    {
                        emptyScanDelay = stationaryThrottleActive
                            ? Mathf.Clamp(14f + (this.birdFarmDenseEmptyScanStreak * 6f), 14f, 35f)
                            : Mathf.Max(scanRefreshSeconds, 8f);
                    }
                    else
                    {
                        emptyScanDelay = Mathf.Min(scanRefreshSeconds, 5f);
                    }

                    this.cachedBirdFarmAuraNextScanAt = Time.unscaledTime + emptyScanDelay;
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan empty backoff nextScanDelay={emptyScanDelay:F1}");
                    BirdNetFarm.TraceCrashBreadcrumb($"Scan completed with no valid candidates detected={detectedCount} inspectCandidates={inspectCandidates.Count} verified={verifiedCandidates}/{verifyLimit}");
                    status = detectedCount > 0 ? "No valid targets in range" : "No birds detected";
                    return false;
                }

                this.birdFarmDenseEmptyScanStreak = 0;
                this.cachedBirdFarmAuraCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (!this.TryTakeBirdFarmAuraCandidateFromCache(playerPos, scanRange, out netId, out staticId, out distance, out birdActionType, out birdStandNetId, out int cachedDetectedCount))
                {
                    status = detectedCount > 0 ? "No valid targets in range" : "No birds detected";
                    return false;
                }

                detectedCount = Mathf.Max(detectedCount, cachedDetectedCount);
                BirdNetFarm.TraceCrashBreadcrumb($"Scan success candidates={this.cachedBirdFarmAuraCandidates.Count} detected={detectedCount}");
                source = "AuraMono.Entities";
                status = "Target ready";
                return true;
            }
            catch (Exception ex)
            {
                BirdNetFarm.TraceCrashBreadcrumb("Scan exception: " + ex.GetType().Name + ": " + ex.Message);
                status = "Scan error: " + ex.Message;
                this.BirdFarmNetLog("Aura mono bird entity scan error: " + ex);
                return false;
            }
        }

        private bool TryTakeBirdFarmAuraCandidateFromCache(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;

            if (!this.IsBirdFarmAuraCacheUsable(playerPos, scanRange))
            {
                return false;
            }

            detectedCount = this.cachedBirdFarmAuraCandidates.Count;
            for (int i = this.cachedBirdFarmAuraCandidates.Count - 1; i >= 0; i--)
            {
                BirdFarmAuraCandidate candidate = this.cachedBirdFarmAuraCandidates[i];
                if (candidate == null
                    || candidate.NetId == 0U
                    || candidate.StaticId < 10000
                    || this.ShouldSkipRecentBirdFarmNetId(candidate.NetId)
                    || this.HasReachedBirdFarmPhotoLimit(candidate.NetId)
                    || !this.IsBirdFarmStateCapturable(candidate.BirdState, out _))
                {
                    this.cachedBirdFarmAuraCandidates.RemoveAt(i);
                }
            }

            if (this.cachedBirdFarmAuraCandidates.Count == 0)
            {
                return false;
            }

            BirdFarmAuraCandidate best = this.cachedBirdFarmAuraCandidates[0];
            this.cachedBirdFarmAuraCandidates.RemoveAt(0);
            netId = best.NetId;
            staticId = best.StaticId;
            distance = best.Distance;
            birdActionType = best.BirdActionType;
            birdStandNetId = best.BirdStandNetId;
            detectedCount = this.cachedBirdFarmAuraCandidates.Count + 1;
            return true;
        }

        private bool TryGetCachedBirdFarmAuraResolvedDetail(uint netId, float now, out BirdFarmAuraResolvedDetail detail)
        {
            detail = null;
            if (netId == 0U)
            {
                return false;
            }

            if (!this._birdFarmResolvedDetailsByNetId.TryGetValue(netId, out detail) || detail == null)
            {
                return false;
            }

            if (detail.ExpiresAt < now)
            {
                this._birdFarmResolvedDetailsByNetId.Remove(netId);
                detail = null;
                return false;
            }

            return true;
        }

        private void CacheBirdFarmAuraResolvedDetail(uint netId, int staticId, int birdActionType, int birdState, uint birdStandNetId, bool isPerchBird, float expiresAt)
        {
            if (netId == 0U || !this.IsBirdFarmStaticIdLikelyBird(staticId))
            {
                return;
            }

            this._birdFarmResolvedDetailsByNetId[netId] = new BirdFarmAuraResolvedDetail
            {
                StaticId = staticId,
                BirdActionType = birdActionType,
                BirdState = birdState,
                BirdStandNetId = birdStandNetId,
                IsPerchBird = isPerchBird,
                ExpiresAt = expiresAt
            };
        }

        // Direct-ECS bird discovery: enumerate bird component objects via Entities.GetComponents<T>
        // (safe, no full entity-graph walk) and return their owner bird entities. Replaces the
        // crash-prone TryEnumerateAuraMonoLoadedEntityObjects walk for the bird scan. Gated on
        // GetComponents readiness: once ready, this is authoritative (returns true even with 0 birds,
        // so the caller never falls back to the walk on a dense/bird-free field). Returns false only
        // when GetComponents is unavailable on this build (then the caller uses the legacy walk).
        // The bird entity pointers returned here are pinned into birdFarmScanEntityPins so they survive
        // the moving sgen GC until the caller reads them; released at the start of the next scan (by then
        // the previous caller has consumed its list). Per-scan, not per-frame.
        private readonly List<uint> birdFarmScanEntityPins = new List<uint>();

        private bool TryCollectBirdFarmAuraBirdEntities(out List<IntPtr> birdEntities, out string status)
        {
            birdEntities = null;
            status = string.Empty;
            FreeAuraMonoPins(this.birdFarmScanEntityPins);

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "GetComponents not ready";
                return false;
            }

            this.TryResolveAuraMonoBirdComponentClasses(out IntPtr birdComponentClass, out IntPtr birdScannableClass, out IntPtr perchBirdComponentClass, out _, out _);
            if (birdComponentClass == IntPtr.Zero && birdScannableClass == IntPtr.Zero && perchBirdComponentClass == IntPtr.Zero)
            {
                status = "Bird component classes unavailable";
                return false;
            }

            List<IntPtr> result = new List<IntPtr>(64);
            HashSet<IntPtr> seen = new HashSet<IntPtr>();
            IntPtr[] classes = { birdComponentClass, birdScannableClass, perchBirdComponentClass };
            for (int c = 0; c < classes.Length; c++)
            {
                IntPtr cls = classes[c];
                // Pin components for the loop (the "entity" member read below would otherwise touch a
                // relocated component); each accepted bird entity is pinned for the caller's lifetime.
                List<uint> compPins = new List<uint>();
                if (cls == IntPtr.Zero || !this.TryAuraMonoGetComponentObjects(cls, out List<IntPtr> components, compPins) || components == null)
                {
                    FreeAuraMonoPins(compPins);
                    continue;
                }

                try
                {
                    for (int i = 0; i < components.Count; i++)
                    {
                        IntPtr componentObj = components[i];
                        if (componentObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        // Owner bird entity is the component's back-reference.
                        IntPtr entityObj = IntPtr.Zero;
                        if ((!this.TryGetMonoObjectMember(componentObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                        {
                            continue;
                        }

                        if (seen.Add(entityObj))
                        {
                            result.Add(entityObj);
                            uint entityPin = AuraMonoPinNew(entityObj);
                            if (entityPin != 0U)
                            {
                                this.birdFarmScanEntityPins.Add(entityPin);
                            }
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(compPins);
                }
            }

            birdEntities = result;
            status = "Bird entities via GetComponents=" + result.Count;
            return true;
        }

        private bool TryGetBirdPhotoDetailsViaAuraMonoEntity(IntPtr entityObj, out int birdActionType, out int birdState, out uint birdStandNetId, out bool isPerchBird)
        {
            birdActionType = 0;
            birdState = -1;
            birdStandNetId = 0U;
            isPerchBird = false;

            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                this.TryResolveAuraMonoBirdComponentClasses(out IntPtr birdComponentClass, out IntPtr birdScannableClass, out IntPtr perchBirdComponentClass, out _, out _);
                IntPtr entityClass = auraMonoObjectGetClass(entityObj);
                IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
                if (getAllComponentsMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> components = this.birdFarmAuraComponentBuffer;
                components.Clear();
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

                    if (birdComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, birdComponentClass))
                    {
                        this.TryGetMonoInt32Member(componentObj, "_state", out birdState);
                        if (birdState < 0)
                        {
                            this.TryGetMonoInt32Member(componentObj, "state", out birdState);
                        }
                    }

                    if (birdScannableClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, birdScannableClass))
                    {
                        IntPtr dataObj;
                        if ((this.TryGetMonoObjectMember(componentObj, "data", out dataObj) || this.TryGetMonoObjectMember(componentObj, "_data", out dataObj) || this.TryGetMonoObjectMember(componentObj, "ComponentData", out dataObj)) && dataObj != IntPtr.Zero)
                        {
                            this.TryGetMonoInt32Member(dataObj, "pose", out birdActionType);
                        }
                    }

                    if (perchBirdComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, perchBirdComponentClass))
                    {
                        isPerchBird = true;
                        IntPtr perchObj = IntPtr.Zero;
                        IntPtr componentDataObj;
                        if ((this.TryGetMonoObjectMember(componentObj, "ComponentData", out componentDataObj) || this.TryGetMonoObjectMember(componentObj, "_componentData", out componentDataObj)) && componentDataObj != IntPtr.Zero)
                        {
                            this.TryGetMonoObjectMember(componentDataObj, "perch", out perchObj);
                        }

                        if (perchObj == IntPtr.Zero)
                        {
                            this.TryGetMonoObjectMember(componentObj, "perch", out perchObj);
                        }

                        if (perchObj != IntPtr.Zero && this.TryGetMonoObjectMember(perchObj, "entity", out IntPtr standEntityObj) && standEntityObj != IntPtr.Zero)
                        {
                            this.TryGetAuraMonoEntityNetId(standEntityObj, out birdStandNetId);
                        }

                        if (birdStandNetId == 0U)
                        {
                            this.TryGetMonoUInt32FromObjectMember(perchObj, "entity", "netId", out birdStandNetId);
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsBirdFarmStateCapturable(int birdState, out string status)
        {
            status = "Bird state ready";
            if (birdState < 0)
            {
                return true;
            }

            switch (birdState)
            {
                case 0:
                case 1:
                case 2:
                case 7:
                    return true;
                case 5:
                    status = "Skipping bird in Fly state";
                    return false;
                case 8:
                case 9:
                    status = "Skipping alerted bird";
                    return false;
                case 3:
                case 4:
                case 6:
                    status = "Skipping bird in takeoff/run state";
                    return false;
                case 10:
                    status = "Skipping bird in interval state";
                    return false;
                default:
                    status = $"Skipping unsupported bird state {birdState}";
                    return false;
            }
        }

        private string GetBirdFarmStateName(int birdState)
        {
            switch (birdState)
            {
                case 0: return "Idle";
                case 1: return "Walk";
                case 2: return "Swim";
                case 3: return "OpenWing";
                case 4: return "RunSpeedUp";
                case 5: return "Fly";
                case 6: return "RunSlowDown";
                case 7: return "Petty";
                case 8: return "AlertTurn";
                case 9: return "Alert";
                case 10: return "Interval";
                default: return birdState < 0 ? "Unknown" : "State" + birdState.ToString();
            }
        }

        private bool TryFindNearestBirdEntityViaPhotoModeBirdScannables(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;
            source = "none";
            status = "PhotoMode bird scannables unavailable";

            try
            {
                if (!this.TryResolveBirdPhotoModeContext(out object photoModeObj, out string photoModeSource) || photoModeObj == null)
                {
                    status = "PhotoMode unavailable: " + this.lastBirdPhotoModeResolveStatus;
                    return false;
                }

                object scannablesObj = null;
                int componentCount = 0;
                bool hasList = this.TryGetObjectMember(photoModeObj, "_birdScannables", out scannablesObj) && scannablesObj is IEnumerable;
                if (hasList && scannablesObj is ICollection initialCollection)
                {
                    componentCount = initialCollection.Count;
                }

                if (!hasList || componentCount == 0)
                {
                    this.TryRefreshManagedBirdPhotoModeComponents(photoModeObj);
                    hasList = this.TryGetObjectMember(photoModeObj, "_birdScannables", out scannablesObj) && scannablesObj is IEnumerable;
                    if (hasList && scannablesObj is ICollection refreshedCollection)
                    {
                        componentCount = refreshedCollection.Count;
                    }
                }

                if (!hasList || !(scannablesObj is IEnumerable enumerable))
                {
                    status = "PhotoMode bird list unavailable";
                    return false;
                }

                this.BirdFarmNetLog($"PhotoMode bird scannable list count={componentCount} source={photoModeSource}");

                float bestDistance = float.MaxValue;
                HashSet<uint> seenNetIds = new HashSet<uint>();
                foreach (object birdScannable in enumerable)
                {
                    if (birdScannable == null)
                    {
                        continue;
                    }

                    if (!(this.TryGetObjectMember(birdScannable, "entity", out object entityObj) || this.TryInvokeZeroArgMember(birdScannable, out entityObj, "get_entity")) || entityObj == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entityObj, out uint candidateNetId, out string netSource, 0) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId) || this.ShouldSkipRecentBirdFarmNetId(candidateNetId) || this.HasReachedBirdFarmPhotoLimit(candidateNetId))
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdStaticIdFromScannable(entityObj, birdScannable, candidateNetId, out int candidateStaticId, out string staticSource))
                    {
                        continue;
                    }

                    Vector3 candidatePosition = playerPos;
                    this.TryExtractBirdEntityPosition(entityObj, out candidatePosition);
                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    if (candidateDistance > scanRange)
                    {
                        continue;
                    }

                    int candidateActionType = 0;
                    if ((this.TryGetObjectMember(birdScannable, "data", out object dataObj) || this.TryInvokeZeroArgMember(birdScannable, out dataObj, "get_data")) && dataObj != null)
                    {
                        if (this.TryGetObjectMember(dataObj, "pose", out object poseObj))
                        {
                            try
                            {
                                candidateActionType = Convert.ToInt32(poseObj);
                            }
                            catch
                            {
                                candidateActionType = 0;
                            }
                        }
                    }

                    this.TryGetBirdStandNetIdFromEntity(entityObj, out uint candidateBirdStandNetId);

                    detectedCount++;
                    if (candidateDistance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = candidateDistance;
                    netId = candidateNetId;
                    staticId = candidateStaticId;
                    distance = candidateDistance;
                    birdActionType = candidateActionType;
                    birdStandNetId = candidateBirdStandNetId;
                    source = "GamePhotoMode._birdScannables / " + netSource + " / " + staticSource;
                }

                if (netId == 0U || staticId <= 0)
                {
                    status = componentCount > 0 ? "PhotoMode bird list found but none were in range/resolvable" : "No PhotoMode bird scannables found";
                    return false;
                }

                status = "PhotoMode bird scannable target ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "PhotoMode bird scannable exception: " + ex.Message;
                return false;
            }
        }

        private bool IsBirdFarmAuraCacheUsable(Vector3 playerPos, float scanRange)
        {
            if (this.cachedBirdFarmAuraCandidates.Count == 0)
            {
                return false;
            }

            float cacheTtl = this.cachedBirdFarmAuraCacheTtl > 0f ? this.cachedBirdFarmAuraCacheTtl : 5f;
            if (Time.unscaledTime - this.cachedBirdFarmAuraCandidatesAt > cacheTtl)
            {
                return false;
            }

            if (Mathf.Abs(this.cachedBirdFarmAuraRange - scanRange) > 0.5f)
            {
                return false;
            }

            float moveTolerance = this.cachedBirdFarmAuraMoveTolerance > 0f ? this.cachedBirdFarmAuraMoveTolerance : 4f;
            return Vector3.Distance(this.cachedBirdFarmAuraOrigin, playerPos) <= moveTolerance;
        }

        private bool TryFindNearestBirdEntityViaBirdScannables(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int birdActionType, out uint birdStandNetId, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            birdActionType = 0;
            birdStandNetId = 0U;
            detectedCount = 0;
            source = "none";
            status = "BirdScannable components unavailable";

            try
            {
                Type entitiesType = this.FindEntitiesRuntimeType();
                Type birdScannableType = this.FindBirdScannableRuntimeType();
                if (entitiesType == null || birdScannableType == null)
                {
                    this.BirdFarmNetLog("BirdScannable path unavailable: EntitiesType=" + (entitiesType != null) + " BirdScannableType=" + (birdScannableType != null));
                    status = "BirdScannable runtime type unavailable";
                    return false;
                }

                MethodInfo getComponentsMethod = entitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getComponentsMethod == null)
                {
                    this.BirdFarmNetLog("BirdScannable path: Entities.GetComponents<T>(ref List<T>) not found.");
                    status = "Entities.GetComponents unavailable";
                    return false;
                }

                Type listType = typeof(List<>).MakeGenericType(birdScannableType);
                object listInstance = Activator.CreateInstance(listType);
                if (listInstance == null)
                {
                    status = "Failed to allocate bird scannable list";
                    return false;
                }

                object[] args = new object[] { listInstance };
                getComponentsMethod.MakeGenericMethod(birdScannableType).Invoke(null, args);
                object results = args[0] ?? listInstance;
                if (!(results is IEnumerable enumerable))
                {
                    status = "Bird scannable list not enumerable";
                    return false;
                }

                int componentCount = 0;
                if (results is ICollection collection)
                {
                    componentCount = collection.Count;
                }

                this.BirdFarmNetLog($"BirdScannable query returned {componentCount} components");

                float bestDistance = float.MaxValue;
                HashSet<uint> seenNetIds = new HashSet<uint>();
                foreach (object birdScannable in enumerable)
                {
                    if (birdScannable == null)
                    {
                        continue;
                    }

                    if (!(this.TryGetObjectMember(birdScannable, "entity", out object entityObj) || this.TryInvokeZeroArgMember(birdScannable, out entityObj, "get_entity")) || entityObj == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entityObj, out uint candidateNetId, out string netSource, 0) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId))
                    {
                        continue;
                    }

                    if (this.ShouldSkipRecentBirdFarmNetId(candidateNetId))
                    {
                        continue;
                    }

                    if (this.HasReachedBirdFarmPhotoLimit(candidateNetId))
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdStaticIdFromScannable(entityObj, birdScannable, candidateNetId, out int candidateStaticId, out string staticSource))
                    {
                        continue;
                    }

                    Vector3 candidatePosition = playerPos;
                    this.TryExtractBirdEntityPosition(entityObj, out candidatePosition);
                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    if (candidateDistance > scanRange)
                    {
                        continue;
                    }

                    bool isPendant = false;
                    int candidateActionType = 0;
                    if ((this.TryGetObjectMember(birdScannable, "data", out object dataObj) || this.TryInvokeZeroArgMember(birdScannable, out dataObj, "get_data")) && dataObj != null)
                    {
                        if (this.TryGetObjectMember(dataObj, "isPendant", out object isPendantObj) && isPendantObj is bool pendantFlag)
                        {
                            isPendant = pendantFlag;
                        }

                        if (this.TryGetObjectMember(dataObj, "pose", out object poseObj))
                        {
                            try
                            {
                                candidateActionType = Convert.ToInt32(poseObj);
                            }
                            catch
                            {
                                candidateActionType = 0;
                            }
                        }
                    }

                    this.TryGetBirdStandNetIdFromEntity(entityObj, out uint candidateBirdStandNetId);

                    detectedCount++;
                    this.BirdFarmNetLog($"BirdScannable candidate: netId={candidateNetId} staticId={candidateStaticId} dist={candidateDistance:F2} pose={candidateActionType} pendant={isPendant} standNetId={candidateBirdStandNetId} net={netSource} static={staticSource}");

                    if (candidateDistance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = candidateDistance;
                    netId = candidateNetId;
                    staticId = candidateStaticId;
                    distance = candidateDistance;
                    birdActionType = candidateActionType;
                    birdStandNetId = candidateBirdStandNetId;
                    source = "Entities.GetComponents<BirdScannableComponent> / " + netSource + " / " + staticSource;
                }

                if (netId == 0U || staticId <= 0)
                {
                    status = componentCount > 0 ? "BirdScannable components found but none were in range/resolvable" : "No BirdScannable components found";
                    return false;
                }

                status = "Bird scannable target ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "BirdScannable query exception: " + ex.Message;
                return false;
            }
        }

        private Type FindBirdScannableRuntimeType()
        {
            Type resolved = this.FindLoadedType("ScriptsRefactory.LevelAndEntity.Entity.Components.BirdScannableComponent", "BirdScannableComponent");
            if (resolved != null)
            {
                return resolved;
            }

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type type in types)
                    {
                        if (type == null)
                        {
                            continue;
                        }

                        string fullName = type.FullName ?? type.Name ?? string.Empty;
                        if (fullName.IndexOf("BirdScannableComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return type;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryFindNearestBirdEntityViaEntitiesSystem(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            detectedCount = 0;
            source = "none";
            status = "Entities system unavailable";

            try
            {
                Type entitiesType = this.FindEntitiesRuntimeType();
                Type entityType = this.FindEntityRuntimeType();
                if (entitiesType == null || entityType == null)
                {
                    this.BirdFarmNetLog("Entities system path unavailable: EntitiesType=" + (entitiesType != null) + " EntityType=" + (entityType != null));
                    return false;
                }

                Type listType = typeof(List<>).MakeGenericType(entityType);
                object results = Activator.CreateInstance(listType);
                if (results == null)
                {
                    status = "Failed to allocate entity result list";
                    return false;
                }

                MethodInfo sphereQueryMethod = entitiesType.GetMethod("SphereQueryEntities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(Vector3), typeof(float), listType }, null);
                if (sphereQueryMethod == null)
                {
                    this.BirdFarmNetLog("Entities system path: SphereQueryEntities not found.");
                    status = "Entities.SphereQueryEntities unavailable";
                    return false;
                }

                object rawCount = sphereQueryMethod.Invoke(null, new object[] { playerPos, scanRange, results });
                int queryCount = 0;
                try
                {
                    queryCount = Convert.ToInt32(rawCount);
                }
                catch
                {
                }
                this.BirdFarmNetLog("Entities system query returned count=" + queryCount);

                if (!(results is IEnumerable enumerable))
                {
                    status = "Entity result list not enumerable";
                    return false;
                }

                float bestDistance = float.MaxValue;
                HashSet<uint> seenNetIds = new HashSet<uint>();
                foreach (object entity in enumerable)
                {
                    if (entity == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entity, out uint candidateNetId, out string netSource, 0) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId))
                    {
                        continue;
                    }

                    if (!this.TryInvokeZeroArgMember(entity, out object componentsObj, "GetAllComponents") || componentsObj == null || !(componentsObj is IEnumerable components))
                    {
                        continue;
                    }

                    bool matchedBirdComponent = false;
                    int candidateStaticId = 0;
                    string staticSource = "none";
                    foreach (object component in components)
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        string typeName = component.GetType().FullName ?? component.GetType().Name ?? string.Empty;
                        if (!this.IsBirdEntityComponentType(typeName))
                        {
                            continue;
                        }

                        matchedBirdComponent = true;
                        if (this.TryResolveBirdStaticIdFromObject(component, out candidateStaticId, out staticSource) && candidateStaticId > 0)
                        {
                            break;
                        }
                    }

                    if (!matchedBirdComponent)
                    {
                        continue;
                    }

                    if (candidateStaticId <= 0)
                    {
                        candidateStaticId = this.TryGetEntityStaticId(candidateNetId);
                        if (candidateStaticId > 0)
                        {
                            staticSource = "EntityUtil.GetEntityResId";
                        }
                    }

                    if (candidateStaticId <= 0)
                    {
                        continue;
                    }

                    Vector3 candidatePosition = playerPos;
                    if (this.TryExtractBirdEntityPosition(entity, out Vector3 resolvedPosition))
                    {
                        candidatePosition = resolvedPosition;
                    }

                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    detectedCount++;
                    this.BirdFarmNetLog($"Entity-system candidate: netId={candidateNetId} staticId={candidateStaticId} dist={candidateDistance:F2} net={netSource} static={staticSource}");

                    if (candidateDistance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = candidateDistance;
                    netId = candidateNetId;
                    staticId = candidateStaticId;
                    distance = candidateDistance;
                    source = "Entities.SphereQueryEntities / " + netSource + " / " + staticSource;
                }

                if (netId == 0U || staticId <= 0)
                {
                    status = queryCount > 0 ? "No bird entities matched query" : "Entities query returned no results";
                    return false;
                }

                status = "Bird entity target ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Entities query exception: " + ex.Message;
                return false;
            }
        }

        private bool TryFindNearestBirdEntityViaEntitiesInstanceEnumeration(Vector3 playerPos, float scanRange, out uint netId, out int staticId, out float distance, out int detectedCount, out string source, out string status)
        {
            netId = 0U;
            staticId = 0;
            distance = -1f;
            detectedCount = 0;
            source = "none";
            status = "Entities instance enumeration unavailable";

            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                if (entitiesType == null)
                {
                    this.BirdFarmNetLog("Entities-instance path: Entities type not found.");
                    return false;
                }

                object entitiesInstance = null;
                PropertyInfo instanceProperty = entitiesType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? entitiesType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    entitiesInstance = instanceProperty.GetValue(null, null);
                }

                if (entitiesInstance == null)
                {
                    MethodInfo getInstanceMethod = entitiesType.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        ?? entitiesType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (getInstanceMethod != null)
                    {
                        entitiesInstance = getInstanceMethod.Invoke(null, null);
                    }
                }

                if (entitiesInstance == null)
                {
                    this.BirdFarmNetLog("Entities-instance path: could not resolve Entities.Instance.");
                    return false;
                }

                List<object> entityCandidates = new List<object>();
                HashSet<object> visited = new HashSet<object>();
                this.CollectManagedBirdEntityCandidates(entitiesInstance, entityCandidates, visited, 0);
                this.BirdFarmNetLog("Entities-instance path collected " + entityCandidates.Count + " candidate entity objects.");

                float bestDistance = float.MaxValue;
                HashSet<uint> seenNetIds = new HashSet<uint>();
                foreach (object entity in entityCandidates)
                {
                    if (entity == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entity, out uint candidateNetId, out string netSource, 0) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenNetIds.Add(candidateNetId))
                    {
                        continue;
                    }

                    if (!this.TryInvokeZeroArgMember(entity, out object componentsObj, "GetAllComponents") || componentsObj == null || !(componentsObj is IEnumerable components))
                    {
                        continue;
                    }

                    bool matchedBirdComponent = false;
                    int candidateStaticId = 0;
                    string staticSource = "none";
                    foreach (object component in components)
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        string typeName = component.GetType().FullName ?? component.GetType().Name ?? string.Empty;
                        if (!this.IsBirdEntityComponentType(typeName))
                        {
                            continue;
                        }

                        matchedBirdComponent = true;
                        if (this.TryResolveBirdStaticIdFromObject(component, out candidateStaticId, out staticSource) && candidateStaticId > 0)
                        {
                            break;
                        }
                    }

                    if (!matchedBirdComponent)
                    {
                        continue;
                    }

                    if (candidateStaticId <= 0)
                    {
                        candidateStaticId = this.TryGetEntityStaticId(candidateNetId);
                        if (candidateStaticId > 0)
                        {
                            staticSource = "EntityUtil.GetEntityResId";
                        }
                    }

                    if (candidateStaticId <= 0)
                    {
                        continue;
                    }

                    Vector3 candidatePosition = playerPos;
                    if (this.TryExtractBirdEntityPosition(entity, out Vector3 resolvedPosition))
                    {
                        candidatePosition = resolvedPosition;
                    }

                    float candidateDistance = Vector3.Distance(playerPos, candidatePosition);
                    if (candidateDistance > scanRange)
                    {
                        continue;
                    }

                    detectedCount++;
                    this.BirdFarmNetLog($"Entities-instance candidate: netId={candidateNetId} staticId={candidateStaticId} dist={candidateDistance:F2} net={netSource} static={staticSource}");
                    if (candidateDistance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = candidateDistance;
                    netId = candidateNetId;
                    staticId = candidateStaticId;
                    distance = candidateDistance;
                    source = "Entities.Instance enumeration / " + netSource + " / " + staticSource;
                }

                if (netId == 0U || staticId <= 0)
                {
                    status = entityCandidates.Count > 0 ? "Entities instance had candidates but none were bird/resolvable" : "No entities from instance enumeration";
                    return false;
                }

                status = "Bird entity target ready via instance enumeration";
                return true;
            }
            catch (Exception ex)
            {
                status = "Entities instance enumeration exception: " + ex.Message;
                this.BirdFarmNetLog("Entities-instance path exception: " + ex);
                return false;
            }
        }

        private void CollectManagedBirdEntityCandidates(object current, List<object> output, HashSet<object> visited, int depth)
        {
            if (current == null || output == null || visited == null || depth > 4)
            {
                return;
            }

            if (!visited.Add(current))
            {
                return;
            }

            Type type = current.GetType();
            string typeName = type.FullName ?? type.Name ?? string.Empty;
            if ((typeName.EndsWith(".Entity", StringComparison.OrdinalIgnoreCase) || string.Equals(type.Name, "Entity", StringComparison.OrdinalIgnoreCase))
                && type.GetMethod("GetAllComponents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
            {
                output.Add(current);
                return;
            }

            if (current is string)
            {
                return;
            }

            if (current is IEnumerable enumerable && !(current is Vector3))
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    this.CollectManagedBirdEntityCandidates(item, output, visited, depth + 1);
                    count++;
                    if (count >= 256)
                    {
                        break;
                    }
                }
            }

            foreach (string memberName in new string[] { "entities", "_entities", "entityWorld", "_entityWorld", "_levelEntityWorld", "_entityWorlds", "_items", "Values", "Keys" })
            {
                if (this.TryGetObjectMember(current, memberName, out object nested) && nested != null)
                {
                    this.CollectManagedBirdEntityCandidates(nested, output, visited, depth + 1);
                }
            }
        }

        private bool IsBirdEntityComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            return typeName.IndexOf("BirdScannable", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("BirdComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("BirdScannableComponent", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryResolveBirdStaticIdFromScannable(object entityObj, object birdScannableObj, uint netId, out int staticId, out string source)
        {
            staticId = 0;
            source = "none";

            try
            {
                if (this.TryGetEntityLevelEntityComponent(entityObj, out object levelEntityComponent) && levelEntityComponent != null)
                {
                    if ((this.TryGetObjectMember(levelEntityComponent, "resId", out object resIdObj) || this.TryInvokeZeroArgMember(levelEntityComponent, out resIdObj, "get_resId")) && resIdObj != null)
                    {
                        if (this.TryGetObjectMember(resIdObj, "resId", out object valueObj) && valueObj != null)
                        {
                            try
                            {
                                int parsed = Convert.ToInt32(valueObj);
                                if (parsed > 0)
                                {
                                    staticId = parsed;
                                    source = "LevelEntityComponent.resId.resId";
                                    return true;
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (this.TryInvokeZeroArgMember(resIdObj, out object loadResIdObj, "get_loadResId") && loadResIdObj != null)
                        {
                            try
                            {
                                int parsed = Convert.ToInt32(loadResIdObj);
                                if (parsed > 0)
                                {
                                    staticId = parsed;
                                    source = "LevelEntityComponent.resId.loadResId";
                                    return true;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }

                if (this.TryResolveBirdStaticIdFromObject(birdScannableObj, out staticId, out source) && staticId > 0)
                {
                    source = "BirdScannable/" + source;
                    return true;
                }

                if (this.TryResolveBirdStaticIdFromObject(entityObj, out staticId, out source) && staticId > 0)
                {
                    source = "Entity/" + source;
                    return true;
                }

                staticId = this.TryGetEntityStaticId(netId);
                if (staticId > 0)
                {
                    source = "EntityUtil.GetEntityResId";
                    return true;
                }
            }
            catch
            {
            }

            staticId = 0;
            source = "none";
            return false;
        }

        private bool TryGetBirdStandNetIdFromEntity(object entityObj, out uint birdStandNetId)
        {
            birdStandNetId = 0U;

            try
            {
                if (!this.TryInvokeZeroArgMember(entityObj, out object componentsObj, "GetAllComponents") || componentsObj == null || !(componentsObj is IEnumerable components))
                {
                    return false;
                }

                foreach (object component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    string typeName = component.GetType().FullName ?? component.GetType().Name ?? string.Empty;
                    if (typeName.IndexOf("PerchBirdComponent", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    object componentDataObj;
                    if (!(this.TryGetObjectMember(component, "ComponentData", out componentDataObj) || this.TryInvokeZeroArgMember(component, out componentDataObj, "get_ComponentData")) || componentDataObj == null)
                    {
                        continue;
                    }

                    if (!(this.TryGetObjectMember(componentDataObj, "perch", out object perchObj) || this.TryGetObjectMember(component, "perch", out perchObj) || this.TryInvokeZeroArgMember(component, out perchObj, "get_perch")) || perchObj == null)
                    {
                        continue;
                    }

                    if (!(this.TryGetObjectMember(perchObj, "entity", out object standEntityObj) || this.TryInvokeZeroArgMember(perchObj, out standEntityObj, "get_entity")) || standEntityObj == null)
                    {
                        continue;
                    }

                    if (this.TryResolveBirdNetIdFromObject(standEntityObj, out uint resolvedStandNetId, out _, 0) && resolvedStandNetId != 0U)
                    {
                        birdStandNetId = resolvedStandNetId;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetBirdPhotoDetailsForNetId(uint netId, out int birdActionType, out uint birdStandNetId)
        {
            birdActionType = 0;
            birdStandNetId = 0U;

            try
            {
                if (!this.TryFindBirdScannableByNetId(netId, out object birdScannableObj, out object entityObj))
                {
                    return false;
                }

                if ((this.TryGetObjectMember(birdScannableObj, "data", out object dataObj) || this.TryInvokeZeroArgMember(birdScannableObj, out dataObj, "get_data")) && dataObj != null && this.TryGetObjectMember(dataObj, "pose", out object poseObj))
                {
                    try
                    {
                        birdActionType = Convert.ToInt32(poseObj);
                    }
                    catch
                    {
                        birdActionType = 0;
                    }
                }

                this.TryGetBirdStandNetIdFromEntity(entityObj, out birdStandNetId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryFindBirdScannableByNetId(uint netId, out object birdScannableObj, out object entityObj)
        {
            birdScannableObj = null;
            entityObj = null;

            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                Type birdScannableType = this.FindLoadedType("ScriptsRefactory.LevelAndEntity.Entity.Components.BirdScannableComponent", "BirdScannableComponent");
                if (entitiesType == null || birdScannableType == null)
                {
                    return false;
                }

                MethodInfo getComponentsMethod = entitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getComponentsMethod == null)
                {
                    return false;
                }

                Type listType = typeof(List<>).MakeGenericType(birdScannableType);
                object listInstance = Activator.CreateInstance(listType);
                if (listInstance == null)
                {
                    return false;
                }

                object[] args = new object[] { listInstance };
                getComponentsMethod.MakeGenericMethod(birdScannableType).Invoke(null, args);
                object results = args[0] ?? listInstance;
                if (!(results is IEnumerable enumerable))
                {
                    return false;
                }

                foreach (object candidate in enumerable)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    object candidateEntity;
                    if (!(this.TryGetObjectMember(candidate, "entity", out candidateEntity) || this.TryInvokeZeroArgMember(candidate, out candidateEntity, "get_entity")) || candidateEntity == null)
                    {
                        continue;
                    }

                    if (this.TryResolveBirdNetIdFromObject(candidateEntity, out uint candidateNetId, out _, 0) && candidateNetId == netId)
                    {
                        birdScannableObj = candidate;
                        entityObj = candidateEntity;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryExtractBirdEntityPosition(object entityObj, out Vector3 position)
        {
            position = Vector3.zero;

            try
            {
                if (this.TryGetObjectMember(entityObj, "position", out object directPos) && directPos is Vector3 directVector)
                {
                    position = directVector;
                    return true;
                }

                if (this.TryGetObjectMember(entityObj, "worldPosition", out object worldPos) && worldPos is Vector3 worldVector)
                {
                    position = worldVector;
                    return true;
                }

                if (this.TryGetObjectMember(entityObj, "transformComponent", out object transformComponent) && transformComponent != null)
                {
                    if (this.TryGetObjectMember(transformComponent, "position", out object transformPos) && transformPos is Vector3 transformVector)
                    {
                        position = transformVector;
                        return true;
                    }

                    if (this.TryGetObjectMember(transformComponent, "worldPosition", out object transformWorldPos) && transformWorldPos is Vector3 transformWorldVector)
                    {
                        position = transformWorldVector;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryHasBirdComponentViaAuraMono(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoBirdComponentClasses(out IntPtr birdComponentClass, out IntPtr birdScannableClass, out _, out _, out IntPtr birdCamouflageComponentClass))
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);

            // Validate entity is alive and spawned before using it as a target
            IntPtr getAlivedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_alived", 0);
            if (getAlivedMethod != IntPtr.Zero)
            {
                IntPtr exc2 = IntPtr.Zero;
                IntPtr alivedResult = auraMonoRuntimeInvoke(getAlivedMethod, entityObj, IntPtr.Zero, ref exc2);
                if (alivedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(alivedResult, out bool isAlive) && !isAlive)
                {
                    return false; // Entity is not alive
                }
            }

            IntPtr getSpawnedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_spawned", 0);
            if (getSpawnedMethod != IntPtr.Zero)
            {
                IntPtr exc3 = IntPtr.Zero;
                IntPtr spawnedResult = auraMonoRuntimeInvoke(getSpawnedMethod, entityObj, IntPtr.Zero, ref exc3);
                if (spawnedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(spawnedResult, out bool isSpawned) && !isSpawned)
                {
                    return false; // Entity is not spawned
                }
            }

            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
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

                // Explicitly exclude BirdCamouflage entities (players wearing bird disguise).
                // These match bird heuristics but are rejected server-side as "Target Bird does not exist".
                if (birdCamouflageComponentClass != IntPtr.Zero
                    && this.IsAuraMonoClassAssignableTo(componentClass, birdCamouflageComponentClass))
                {
                    return false;
                }

                string componentClassName = this.GetAuraMonoClassDisplayName(componentClass);
                if (!string.IsNullOrEmpty(componentClassName)
                    && (componentClassName.IndexOf("Camouflage", StringComparison.OrdinalIgnoreCase) >= 0
                        || componentClassName.IndexOf("BirdCamouflage", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return false;
                }

                if (this.LooksLikeAuraMonoBirdComponentObject(componentObj, componentClass, birdComponentClass, birdScannableClass, IntPtr.Zero, IntPtr.Zero))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveAuraMonoBirdComponentClasses(out IntPtr birdComponentClass, out IntPtr birdScannableClass, out IntPtr perchBirdComponentClass, out IntPtr levelEntityComponentClass, out IntPtr birdCamouflageComponentClass)
        {
            birdComponentClass = IntPtr.Zero;
            birdScannableClass = IntPtr.Zero;
            perchBirdComponentClass = IntPtr.Zero;
            levelEntityComponentClass = IntPtr.Zero;
            birdCamouflageComponentClass = IntPtr.Zero;

            IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
            if (levelImage != IntPtr.Zero && auraMonoClassFromName != null)
            {
                birdComponentClass = auraMonoClassFromName(levelImage, "ScriptsRefactory.LevelAndEntity.Entity.Components", "BirdComponent");
                birdScannableClass = auraMonoClassFromName(levelImage, "ScriptsRefactory.LevelAndEntity.Entity.Components", "BirdScannableComponent");
                perchBirdComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Component.Homeland", "PerchBirdComponent");
                if (perchBirdComponentClass == IntPtr.Zero)
                {
                    perchBirdComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.GamePlay.Component.Homeland", "PerchBirdComponent");
                }
                levelEntityComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.EntityView", "LevelEntityComponent");
                // BirdCamouflageComponent = players wearing a bird disguise costume. Must NOT be treated as a photo target.
                birdCamouflageComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Interaction", "BirdCamouflageComponent");
                if (birdCamouflageComponentClass == IntPtr.Zero)
                    birdCamouflageComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.GamePlay.Component", "BirdCamouflageComponent");
            }

            if (birdComponentClass == IntPtr.Zero)
            {
                birdComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ScriptsRefactory.LevelAndEntity.Entity.Components", "BirdComponent");
            }
            if (birdScannableClass == IntPtr.Zero)
            {
                birdScannableClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ScriptsRefactory.LevelAndEntity.Entity.Components", "BirdScannableComponent");
            }
            if (perchBirdComponentClass == IntPtr.Zero)
            {
                perchBirdComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Homeland", "PerchBirdComponent");
                if (perchBirdComponentClass == IntPtr.Zero)
                {
                    perchBirdComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.GamePlay.Component.Homeland", "PerchBirdComponent");
                }
            }
            if (levelEntityComponentClass == IntPtr.Zero)
            {
                levelEntityComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.EntityView", "LevelEntityComponent");
            }
            if (birdCamouflageComponentClass == IntPtr.Zero)
            {
                birdCamouflageComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Interaction", "BirdCamouflageComponent");
            }

            return birdComponentClass != IntPtr.Zero || birdScannableClass != IntPtr.Zero;
        }

        private bool LooksLikeAuraMonoBirdComponentObject(IntPtr componentObj, IntPtr componentClass, IntPtr birdComponentClass, IntPtr birdScannableClass, IntPtr perchBirdComponentClass, IntPtr levelEntityComponentClass)
        {
            if (componentObj == IntPtr.Zero)
            {
                return false;
            }

            // ONLY accept actual BirdComponent or BirdScannableComponent classes
            // Do NOT accept LevelEntityComponent or other generic components that might have bird-like fields
            if (componentClass != IntPtr.Zero)
            {
                if (this.IsAuraMonoClassAssignableTo(componentClass, birdComponentClass)
                    || this.IsAuraMonoClassAssignableTo(componentClass, birdScannableClass))
                {
                    return true;
                }
            }

            string classDisplayName = this.GetAuraMonoClassDisplayName(componentClass);
            if (!string.IsNullOrEmpty(classDisplayName))
            {
                if (classDisplayName.EndsWith(".BirdComponent", StringComparison.OrdinalIgnoreCase)
                    || classDisplayName.EndsWith(".BirdScannableComponent", StringComparison.OrdinalIgnoreCase)
                    || classDisplayName.IndexOf("BirdComponent", StringComparison.OrdinalIgnoreCase) >= 0
                    || classDisplayName.IndexOf("BirdScannable", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // NOTE: Removed overly permissive field name checks that were flagging non-bird entities
            // Valid birds MUST have explicit BirdComponent or BirdScannableComponent classes
            // Checking field names like "bird", "birdData", "targetResId" catches false positives

            return false;
        }

        private int TryReadBirdStaticIdViaAuraMono(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            try
            {
                this.TryResolveAuraMonoBirdComponentClasses(out IntPtr birdComponentClass, out IntPtr birdScannableClass, out IntPtr perchBirdComponentClass, out IntPtr levelEntityComponentClass, out _);
                IntPtr entityClass = auraMonoObjectGetClass(entityObj);
                IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
                if (getAllComponentsMethod == IntPtr.Zero)
                {
                    return 0;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
                {
                    return 0;
                }

                List<IntPtr> components = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
                {
                    return 0;
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

                    bool relevant = this.LooksLikeAuraMonoBirdComponentObject(componentObj, componentClass, birdComponentClass, birdScannableClass, perchBirdComponentClass, levelEntityComponentClass);
                    if (!relevant)
                    {
                        continue;
                    }

                    int value = this.TryReadBirdStaticIdViaAuraMonoObject(componentObj);
                    if (value > 0)
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("TryReadBirdStaticIdViaAuraMono error: " + ex.Message);
            }

            return 0;
        }

        private int TryReadBirdStaticIdViaAuraMonoObject(IntPtr obj)
        {
            if (obj == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryGetMonoObjectMember(obj, "resId", out IntPtr resIdObj) && resIdObj != IntPtr.Zero)
            {
                foreach (string memberName in new string[] { "resId", "ResId", "_resId", "loadResId", "LoadResId", "_loadResId", "targetResId", "TargetResId", "_targetResId", "id", "Id", "_id", "staticId", "StaticId", "_staticId" })
                {
                    if (this.TryGetMonoInt32Member(resIdObj, memberName, out int nestedValue) && nestedValue > 0)
                    {
                        return nestedValue;
                    }
                }
            }

            foreach (string memberName in new string[]
            {
                "staticId", "StaticId", "_staticId",
                "tableId", "TableId", "_tableId",
                "configId", "ConfigId", "_configId",
                "birdId", "BirdId", "_birdId",
                "resId", "ResId", "_resId",
                "loadResId", "LoadResId", "_loadResId",
                "targetResId", "TargetResId", "_targetResId",
                "targetId", "TargetId", "_targetId",
                "Id", "id", "_id"
            })
            {
                if (this.TryGetMonoInt32Member(obj, memberName, out int value) && value > 0 && value < 100000)
                {
                    return value;
                }
            }

            foreach (string memberName in new string[] { "tableBird", "_tableBird", "birdData", "_birdData", "data", "_data" })
            {
                if (this.TryGetMonoObjectMember(obj, memberName, out IntPtr nested) && nested != IntPtr.Zero)
                {
                    int nestedValue = this.TryReadBirdStaticIdViaAuraMonoObject(nested);
                    if (nestedValue > 0)
                    {
                        return nestedValue;
                    }
                }
            }

            return 0;
        }

        private bool TryGetScannerBirdDistance(uint netId, out float distance)
        {
            distance = -1f;

            try
            {
                Type scannerPanelType = this.FindLoadedType("XDTGame.UI.Panel.ScannerStatusPanel", "ScannerStatusPanel");
                if (scannerPanelType == null)
                {
                    return false;
                }

                foreach (FieldInfo field in scannerPanelType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field == null || field.FieldType != typeof(uint))
                    {
                        continue;
                    }

                    string name = field.Name ?? string.Empty;
                    if (name.IndexOf("scanningBirdNetId", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    object rawNetId = field.GetValue(null);
                    if (!this.TryConvertToUInt(rawNetId, out uint currentNetId) || currentNetId != netId)
                    {
                        continue;
                    }

                    break;
                }
            }
            catch
            {
            }

            return false;
        }

        private void BirdFarmNetLog(string message)
        {
            if (!BirdNetFarm.IsDebugLoggingEnabled())
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (message.StartsWith("PhotoMode resolver:", StringComparison.Ordinal)
                || message.StartsWith("Aura mono entity enumeration:", StringComparison.Ordinal)
                || message.StartsWith("Aura mono entity source ", StringComparison.Ordinal)
                || message.StartsWith("Aura mono entity collection ", StringComparison.Ordinal)
                || message.StartsWith("Aura mono entity world ", StringComparison.Ordinal)
                || message.StartsWith("Aura mono candidate:", StringComparison.Ordinal)
                || message.StartsWith("BirdScannable path unavailable:", StringComparison.Ordinal)
                || message.StartsWith("Entities system path unavailable:", StringComparison.Ordinal)
                || message.StartsWith("Entities-instance path:", StringComparison.Ordinal)
                || message.StartsWith("Scene-object bird component matches=", StringComparison.Ordinal))
            {
                return;
            }

            ModLogger.Msg("[BirdFarmNet] " + message);
        }

        private bool TryResolveBirdNetId(GameObject target, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";

            if (target == null)
            {
                return false;
            }

            if (this.TryResolveBirdNetIdFromObject(target, out netId, out source, 0))
            {
                return true;
            }

            Transform parent = target.transform.parent;
            int parentDepth = 0;
            while (parent != null && parentDepth < 5)
            {
                if (this.TryResolveBirdNetIdFromObject(parent.gameObject, out netId, out source, 0))
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
                if (child == null)
                {
                    continue;
                }

                if (this.TryResolveBirdNetIdFromObject(child.gameObject, out netId, out source, 0))
                {
                    source = "child/" + source;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveBirdNetIdFromObject(object candidate, out uint netId, out string source, int depth)
        {
            netId = 0U;
            source = "none";

            if (candidate == null || depth > 4)
            {
                return false;
            }

            if (candidate is GameObject gameObject)
            {
                if (this.TryResolveNetIdFromGameObject(gameObject, out netId, out source))
                {
                    return true;
                }

                try
                {
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        if (this.TryResolveBirdNetIdFromObject(component, out netId, out source, depth + 1))
                        {
                            source = component.GetType().Name + "->" + source;
                            return true;
                        }
                    }
                }
                catch
                {
                }

                return false;
            }

            if (this.TryReadBirdNetIdValue(candidate, out netId, out source))
            {
                return true;
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity", "ownerEntity", "targetEntity", "componentData", "ComponentData", "_componentData", "perch", "Perch", "_perch", "bird", "Bird", "_bird" })
            {
                if (!this.TryGetObjectMember(candidate, memberName, out object nested) || nested == null)
                {
                    continue;
                }

                if (this.TryResolveBirdNetIdFromObject(nested, out netId, out source, depth + 1))
                {
                    source = candidate.GetType().Name + "." + memberName + "->" + source;
                    return true;
                }
            }

            return false;
        }

        private bool TryReadBirdNetIdValue(object obj, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";

            if (obj == null)
            {
                return false;
            }

            foreach (string memberName in new string[] { "netId", "NetId", "_netId", "Id", "id", "targetNetId", "TargetNetId", "birdNetId", "BirdNetId" })
            {
                if (!this.TryGetObjectMember(obj, memberName, out object value) || value == null)
                {
                    continue;
                }

                if (this.TryConvertToUInt(value, out netId))
                {
                    source = memberName;
                    return true;
                }
            }

            foreach (string methodName in new string[] { "GetNetId", "get_NetId", "get_netId", "get_Id", "GetId" })
            {
                if (!this.TryInvokeZeroArgMember(obj, out object value, methodName) || value == null)
                {
                    continue;
                }

                if (this.TryConvertToUInt(value, out netId))
                {
                    source = methodName + "()";
                    return true;
                }
            }

            return false;
        }

        private bool TryGetManagedBirdScannerObject(object interactSystemObj, object playerObj, out object scannerObj, out string source)
        {
            scannerObj = null;
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
            bool looksLikeBirdScanner = className.IndexOf("BirdScanner", StringComparison.OrdinalIgnoreCase) >= 0
                || (className.IndexOf("bird", StringComparison.OrdinalIgnoreCase) >= 0 && className.IndexOf("scan", StringComparison.OrdinalIgnoreCase) >= 0)
                || className.IndexOf("Scanner", StringComparison.OrdinalIgnoreCase) >= 0;

            if (this.TryGetObjectMember(handholdObj, "_scanner", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "scanner", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "_birdScanner", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "birdScanner", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "_scannerStatusPanel", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "scannerStatusPanel", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "_scanComponent", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "scanComponent", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "_photoTargetManager", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "photoTargetManager", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "_birdManager", out scannerObj)
                || this.TryGetObjectMember(handholdObj, "birdManager", out scannerObj))
            {
                if (scannerObj != null)
                {
                    source = handholdSource + ".scanner";
                    return true;
                }
            }

            if (!looksLikeBirdScanner)
            {
                source = handholdSource + " [not bird scanner: " + className + "]";
                return false;
            }

            scannerObj = handholdObj;
            source = handholdSource;
            return true;
        }

        private bool TryResolveBirdStaticIdFromGameObject(GameObject obj, out int staticId, out string source)
        {
            staticId = 0;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            if (this.TryResolveBirdStaticIdFromObject(obj, out staticId, out source))
            {
                return true;
            }

            Transform parent = obj.transform.parent;
            int parentDepth = 0;
            while (parent != null && parentDepth < 3)
            {
                if (this.TryResolveBirdStaticIdFromObject(parent.gameObject, out staticId, out source))
                {
                    source = "parent/" + source;
                    return true;
                }
                parent = parent.parent;
                parentDepth++;
            }

            int childCount = obj.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = obj.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (this.TryResolveBirdStaticIdFromObject(child.gameObject, out staticId, out source))
                {
                    source = "child/" + source;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveBirdStaticIdFromObject(object candidate, out int staticId, out string source)
        {
            staticId = 0;
            source = "none";
            if (candidate == null)
            {
                return false;
            }

            if (candidate is GameObject gameObject)
            {
                try
                {
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        if (this.TryResolveBirdStaticIdFromObject(component, out staticId, out source))
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

            Type type = candidate.GetType();
            string typeName = type.FullName ?? type.Name ?? string.Empty;

            object tableBird = null;
            if (this.TryGetObjectMember(candidate, "_tableBird", out tableBird) || this.TryGetObjectMember(candidate, "tableBird", out tableBird))
            {
                if (tableBird != null && this.TryReadBirdStaticIdValue(tableBird, out staticId, out string tableSource))
                {
                    source = type.Name + "." + tableSource;
                    return true;
                }
            }

            if (this.TryReadBirdStaticIdValue(candidate, out staticId, out string directSource))
            {
                source = type.Name + "." + directSource;
                return true;
            }

            return false;
        }

        private bool TryReadBirdStaticIdValue(object obj, out int staticId, out string source)
        {
            staticId = 0;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            string[] memberNames = new string[]
            {
                "Id", "id", "_id", "ID", "staticId", "StaticId", "_staticId",
                "resId", "ResId", "_resId", "birdId", "BirdId", "_birdId",
                "tableId", "TableId", "_tableId",
                "loadResId", "LoadResId", "_loadResId",
                "targetResId", "TargetResId", "_targetResId",
                "targetId", "TargetId", "_targetId"
            };

            foreach (string memberName in memberNames)
            {
                if (!this.TryGetObjectMember(obj, memberName, out object value) || value == null)
                {
                    continue;
                }

                try
                {
                    int parsed = Convert.ToInt32(value);
                    if (parsed > 0 && parsed < 100000)
                    {
                        staticId = parsed;
                        source = memberName;
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (string methodName in new string[] { "get_Id", "get_id", "get_StaticId", "get_ResId", "get_LoadResId", "get_TargetResId", "get_TargetId", "get_BirdId" })
            {
                if (!this.TryInvokeZeroArgMember(obj, out object value, methodName) || value == null)
                {
                    continue;
                }

                try
                {
                    int parsed = Convert.ToInt32(value);
                    if (parsed > 0 && parsed < 100000)
                    {
                        staticId = parsed;
                        source = methodName + "()";
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryInvokeBirdPhotoProtocol(int staticId, uint birdNetId, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out string status)
        {
            status = "Bird protocol unavailable";
            Breadcrumbs.Drop("BirdFarm.photo", "net=" + birdNetId + " static=" + staticId + " stand=" + birdStandNetId);

            if (birdNetId == 0U || staticId <= 0)
            {
                status = "Invalid bird target";
                return false;
            }

            if (this.TryInvokeAuraMonoBirdPhotoProtocol(staticId, birdNetId, birdActionType, birdStandNetId, perfectPhotoEnabled, out string auraStatus))
            {
                this.BirdFarmNetLog($"Bird photo protocol path=AuraMono netId={birdNetId} staticId={staticId} pose={birdActionType} standNetId={birdStandNetId} auraStatus={auraStatus}");
                this.RegisterBirdPhotoProtocolSend(birdNetId);
                status = auraStatus;
                return true;
            }

            this.BirdFarmNetLog($"Bird photo protocol path=Failed netId={birdNetId} staticId={staticId} pose={birdActionType} standNetId={birdStandNetId} auraStatus={auraStatus}");
            status = auraStatus;
            return false;
        }

        private void RegisterBirdPhotoProtocolSend(uint birdNetId)
        {
            this.lastBirdFarmAttemptedNetId = birdNetId;
            this.pendingBirdFarmAttemptedNetIds.Enqueue(birdNetId);
            this.lastBirdFarmSendAt = Time.unscaledTime;
            this.RememberRecentBirdFarmTarget(birdNetId);
            this.RememberRecentBirdFarmNetId(birdNetId, 10f);
            this.RegisterBirdFarmPhoto(birdNetId);
        }

        private bool IsBirdFarmPhotoTargetCapturable(uint birdNetId, int staticId, int birdActionType, uint birdStandNetId, out string status)
        {
            status = "Bird target ready";

            if (birdNetId == 0U || staticId <= 0)
            {
                status = "Invalid bird target";
                return false;
            }

            if (!this.IsBirdFarmStaticIdLikelyBird(staticId))
            {
                status = $"Skipping non-bird staticId {staticId}";
                return false;
            }

            if (this.ShouldSkipRecentBirdFarmNetId(birdNetId))
            {
                status = "Waiting for bird capture cooldown";
                return false;
            }

            return true;
        }

        private bool IsBirdFarmStaticIdLikelyBird(int staticId)
        {
            if (staticId <= 0)
            {
                return false;
            }

            // Observed bird IDs are in the 51xxx/61xxx/62xxx/63xxx families.
            // Resource trees/props can also have high staticIds, so ">=10000" is not safe enough.
            return (staticId >= 51000 && staticId < 52000)
                || (staticId >= 61000 && staticId < 64000);
        }

        private bool TryInvokeDirectBirdPhotoProtocol(int staticId, uint birdNetId, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out string status)
        {
            status = "Direct protocol unavailable";

            try
            {
                if (!this.TryResolveDirectBirdPhotoProtocol(out MethodInfo protocolMethod, out Type detailInfoType, out string resolveStatus))
                {
                    status = resolveStatus;
                    return false;
                }

                if (!this.TryBuildDirectBirdPhotoDetailInfo(detailInfoType, birdActionType, birdStandNetId, perfectPhotoEnabled, out object detailInfo, out string detailStatus))
                {
                    status = detailStatus;
                    return false;
                }

                int resolvedStaticId = staticId > 0 ? staticId : unchecked((int)birdNetId);
                bool isSuccess = resolvedStaticId > 0;
                uint resolvedBirdNetId = birdStandNetId == 0U ? birdNetId : 0U;
                this.BirdFarmNetLog($"Direct bird photo invoke prepared: staticId={resolvedStaticId} isSuccess={isSuccess} sendBirdNetId={resolvedBirdNetId} detailType={detailInfoType.FullName}");
                protocolMethod.Invoke(null, new object[] { resolvedStaticId, isSuccess, detailInfo, resolvedBirdNetId });
                status = perfectPhotoEnabled ? "Photo sent (direct runtime perfect payload Stretch+Perfect)" : "Photo sent (direct runtime)";
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                BirdNetFarm.TraceCrashBreadcrumb("Direct protocol exception: " + inner.GetType().Name + ": " + inner.Message);
                this.BirdFarmNetLog("Direct bird photo protocol exception: " + inner.Message);
                status = "Direct protocol error: " + inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                BirdNetFarm.TraceCrashBreadcrumb("Direct protocol exception: " + ex.GetType().Name + ": " + ex.Message);
                this.BirdFarmNetLog("Direct bird photo protocol error: " + ex.Message);
                status = "Direct protocol error: " + ex.Message;
                return false;
            }
        }

        private bool TryInvokeDirectBirdPhotoClientCommand(int staticId, uint birdNetId, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out string status)
        {
            status = "Direct client command unavailable";

            try
            {
                if (!this.EnsureBirdPhotoDirectClientPeerMethod(out string resolveStatus))
                {
                    status = resolveStatus;
                    return false;
                }

                Type detailInfoType = this.cachedBirdPhotoDetailInfoRuntimeType;
                if (detailInfoType == null)
                {
                    status = "Direct bird detail type unavailable";
                    return false;
                }

                if (!this.TryBuildDirectBirdPhotoDetailInfo(detailInfoType, birdActionType, birdStandNetId, perfectPhotoEnabled, out object detailInfo, out string detailStatus))
                {
                    status = detailStatus;
                    return false;
                }

                object command = Activator.CreateInstance(this.cachedBirdPhotoCommandRuntimeType);
                if (command == null)
                {
                    status = "Direct bird command allocation failed";
                    return false;
                }

                int resolvedStaticId = staticId > 0 ? staticId : unchecked((int)birdNetId);
                bool isSuccess = resolvedStaticId > 0;
                uint resolvedBirdNetId = birdStandNetId == 0U ? birdNetId : 0U;
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "BirdNetId", resolvedBirdNetId);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "BirdStaticId", resolvedStaticId);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "DetailInfo", detailInfo);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "IsSuccess", isSuccess);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "ScannerNetId", 0U);
                this.BirdFarmNetLog($"Direct bird client command prepared: staticId={resolvedStaticId} isSuccess={isSuccess} sendBirdNetId={resolvedBirdNetId} commandType={this.cachedBirdPhotoCommandRuntimeType.FullName} clientPeerType={this.cachedBirdPhotoClientPeerRuntimeObject?.GetType().FullName}");
                object result = this.cachedBirdPhotoClientPeerSendMethod.Invoke(this.cachedBirdPhotoClientPeerRuntimeObject, new object[] { command, true, this.cachedBirdPhotoReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                if (sendCode < 0)
                {
                    status = "Direct client command failed (" + sendCode + ")";
                    return false;
                }

                status = perfectPhotoEnabled ? "Photo sent (direct client command perfect payload Stretch+Perfect)" : "Photo sent (direct client command)";
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.BirdFarmNetLog("Direct bird client command exception: " + inner.Message);
                status = "Direct client command error: " + inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("Direct bird client command error: " + ex.Message);
                status = "Direct client command error: " + ex.Message;
                return false;
            }
        }

        private bool TryInvokeDirectBirdPhotoCommand(int staticId, uint birdNetId, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out string status)
        {
            status = "Direct command unavailable";

            try
            {
                if (!this.EnsureBirdPhotoDirectCommandMethod(out string resolveStatus))
                {
                    status = resolveStatus;
                    return false;
                }

                Type detailInfoType = this.cachedBirdPhotoDetailInfoRuntimeType;
                if (detailInfoType == null)
                {
                    status = "Direct bird detail type unavailable";
                    return false;
                }

                if (!this.TryBuildDirectBirdPhotoDetailInfo(detailInfoType, birdActionType, birdStandNetId, perfectPhotoEnabled, out object detailInfo, out string detailStatus))
                {
                    status = detailStatus;
                    return false;
                }

                object command = Activator.CreateInstance(this.cachedBirdPhotoCommandRuntimeType);
                if (command == null)
                {
                    status = "Direct bird command allocation failed";
                    return false;
                }

                int resolvedStaticId = staticId > 0 ? staticId : unchecked((int)birdNetId);
                bool isSuccess = resolvedStaticId > 0;
                uint resolvedBirdNetId = birdStandNetId == 0U ? birdNetId : 0U;
                uint scannerNetId = 0U;
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "BirdNetId", resolvedBirdNetId);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "BirdStaticId", resolvedStaticId);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "DetailInfo", detailInfo);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "IsSuccess", isSuccess);
                this.TrySetFieldValue(this.cachedBirdPhotoCommandRuntimeType, ref command, "ScannerNetId", scannerNetId);
                this.BirdFarmNetLog($"Direct bird photo command prepared: staticId={resolvedStaticId} isSuccess={isSuccess} sendBirdNetId={resolvedBirdNetId} commandType={this.cachedBirdPhotoCommandRuntimeType.FullName}");
                object result = this.cachedBirdPhotoSendCommandMethod.Invoke(null, new object[] { command, true, this.cachedBirdPhotoReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                if (sendCode < 0)
                {
                    status = "Direct command failed (" + sendCode + ")";
                    return false;
                }

                status = perfectPhotoEnabled ? "Photo sent (direct command perfect payload Stretch+Perfect)" : "Photo sent (direct command)";
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.BirdFarmNetLog("Direct bird photo command exception: " + inner.Message);
                status = "Direct command error: " + inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("Direct bird photo command error: " + ex.Message);
                status = "Direct command error: " + ex.Message;
                return false;
            }
        }

        private bool EnsureBirdPhotoDirectCommandMethod(out string status)
        {
            status = this.cachedBirdPhotoDirectCommandResolveStatus;
            if (this.cachedBirdPhotoSendCommandMethod != null
                && this.cachedBirdPhotoCommandRuntimeType != null
                && this.cachedBirdPhotoReliableChannelValue != null
                && this.cachedBirdPhotoDetailInfoRuntimeType != null)
            {
                status = "Direct bird command cached";
                return true;
            }

            float now = Time.unscaledTime;
            if (this.cachedBirdPhotoDirectCommandNextAttemptAt > now)
            {
                return false;
            }

            try
            {
                if (!this.TryResolveBirdPhotoCommandRuntimeTypes(out Type commandType, out Type detailInfoType, out string typeStatus))
                {
                    status = typeStatus;
                    this.cachedBirdPhotoDirectCommandResolveStatus = status;
                    this.cachedBirdPhotoDirectCommandNextAttemptAt = now + 2f;
                    return false;
                }

                Type webRequestType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                    "WebRequestUtility");
                if (webRequestType == null)
                {
                    status = "WebRequestUtility unavailable";
                    this.cachedBirdPhotoDirectCommandResolveStatus = status;
                    this.cachedBirdPhotoDirectCommandNextAttemptAt = now + 2f;
                    return false;
                }

                MethodInfo sendCommandOpen = null;
                foreach (MethodInfo method in webRequestType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "SendCommand" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 3)
                    {
                        sendCommandOpen = method;
                        break;
                    }
                }

                if (sendCommandOpen == null)
                {
                    status = "SendCommand unavailable";
                    this.cachedBirdPhotoDirectCommandResolveStatus = status;
                    this.cachedBirdPhotoDirectCommandNextAttemptAt = now + 2f;
                    return false;
                }

                Type channelType = this.FindLoadedType("XD.GameGerm.Network.ChannelType", "ChannelType");
                if (channelType == null)
                {
                    status = "ChannelType unavailable";
                    this.cachedBirdPhotoDirectCommandResolveStatus = status;
                    this.cachedBirdPhotoDirectCommandNextAttemptAt = now + 2f;
                    return false;
                }

                this.cachedBirdPhotoCommandRuntimeType = commandType;
                this.cachedBirdPhotoDetailInfoRuntimeType = detailInfoType;
                this.cachedBirdPhotoSendCommandMethod = sendCommandOpen.MakeGenericMethod(commandType);
                this.cachedBirdPhotoReliableChannelValue = Enum.Parse(channelType, "Reliable");
                this.cachedBirdPhotoDirectCommandResolveStatus = "Direct bird command ready";
                this.cachedBirdPhotoDirectCommandNextAttemptAt = -999f;
                this.BirdFarmNetLog("Direct bird command resolver: resolved commandType=" + commandType.FullName + " detailType=" + detailInfoType.FullName);
                status = this.cachedBirdPhotoDirectCommandResolveStatus;
                return true;
            }
            catch (Exception ex)
            {
                status = "Direct bird command resolve error: " + ex.Message;
                this.cachedBirdPhotoDirectCommandResolveStatus = status;
                this.cachedBirdPhotoDirectCommandNextAttemptAt = now + 2f;
                this.BirdFarmNetLog("EnsureBirdPhotoDirectCommandMethod error: " + ex.Message);
                return false;
            }
        }

        private bool EnsureBirdPhotoDirectClientPeerMethod(out string status)
        {
            status = this.cachedBirdPhotoDirectClientResolveStatus;
            if (this.cachedBirdPhotoClientPeerRuntimeObject != null
                && this.cachedBirdPhotoClientPeerSendMethod != null
                && this.cachedBirdPhotoCommandRuntimeType != null
                && this.cachedBirdPhotoReliableChannelValue != null
                && this.cachedBirdPhotoDetailInfoRuntimeType != null)
            {
                status = "Direct bird client command cached";
                return true;
            }

            float now = Time.unscaledTime;
            if (this.cachedBirdPhotoDirectClientNextAttemptAt > now)
            {
                return false;
            }

            try
            {
                if (!this.TryResolveBirdPhotoCommandRuntimeTypes(out Type commandType, out Type detailInfoType, out string typeStatus))
                {
                    status = typeStatus;
                    this.cachedBirdPhotoDirectClientResolveStatus = status;
                    this.cachedBirdPhotoDirectClientNextAttemptAt = now + 2f;
                    return false;
                }

                object clientPeer = this.TryResolveBirdPhotoClientPeerRuntimeObject();
                if (clientPeer == null)
                {
                    status = "ClientPeer unavailable";
                    this.cachedBirdPhotoDirectClientResolveStatus = status;
                    this.cachedBirdPhotoDirectClientNextAttemptAt = now + 2f;
                    this.BirdFarmNetLog("Direct bird client resolver: could not resolve ClientPeer.");
                    return false;
                }

                MethodInfo sendMethod = null;
                Type clientPeerType = clientPeer.GetType();
                foreach (MethodInfo method in clientPeerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "Send" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 3)
                    {
                        sendMethod = method.MakeGenericMethod(commandType);
                        break;
                    }
                }

                if (sendMethod == null)
                {
                    status = "ClientPeer.Send unavailable";
                    this.cachedBirdPhotoDirectClientResolveStatus = status;
                    this.cachedBirdPhotoDirectClientNextAttemptAt = now + 2f;
                    this.BirdFarmNetLog("Direct bird client resolver: Send<T> unavailable on " + clientPeerType.FullName);
                    return false;
                }

                this.cachedBirdPhotoCommandRuntimeType = commandType;
                this.cachedBirdPhotoDetailInfoRuntimeType = detailInfoType;
                this.cachedBirdPhotoClientPeerRuntimeObject = clientPeer;
                this.cachedBirdPhotoClientPeerSendMethod = sendMethod;
                this.cachedBirdPhotoDirectClientResolveStatus = "Direct bird client command ready";
                this.cachedBirdPhotoDirectClientNextAttemptAt = -999f;
                this.BirdFarmNetLog("Direct bird client resolver: resolved clientPeerType=" + clientPeerType.FullName + " commandType=" + commandType.FullName + " detailType=" + detailInfoType.FullName);
                status = this.cachedBirdPhotoDirectClientResolveStatus;
                return true;
            }
            catch (Exception ex)
            {
                status = "Direct bird client resolve error: " + ex.Message;
                this.cachedBirdPhotoDirectClientResolveStatus = status;
                this.cachedBirdPhotoDirectClientNextAttemptAt = now + 2f;
                this.BirdFarmNetLog("EnsureBirdPhotoDirectClientPeerMethod error: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveBirdPhotoCommandRuntimeTypes(out Type commandType, out Type detailInfoType, out string status)
        {
            commandType = this.cachedBirdPhotoCommandRuntimeType;
            detailInfoType = this.cachedBirdPhotoDetailInfoRuntimeType;
            status = "Bird photo command types unavailable";

            if (commandType != null
                && detailInfoType != null
                && this.cachedBirdPhotoReliableChannelValue != null
                && this.IsBirdPhotoCommandShape(commandType, out Type cachedDetailType))
            {
                this.cachedBirdPhotoDetailInfoRuntimeType = cachedDetailType;
                detailInfoType = cachedDetailType;
                status = "Bird photo command types cached";
                return true;
            }

            commandType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.BirdWatching.TakingBirdPhotoCommand",
                "TakingBirdPhotoCommand");
            if (commandType == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    if (types == null)
                    {
                        continue;
                    }

                    foreach (Type candidateType in types)
                    {
                        if (candidateType == null || !string.Equals(candidateType.Name, "TakingBirdPhotoCommand", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (this.IsBirdPhotoCommandShape(candidateType, out Type candidateDetailType))
                        {
                            commandType = candidateType;
                            detailInfoType = candidateDetailType;
                            break;
                        }
                    }

                    if (commandType != null)
                    {
                        break;
                    }
                }
            }

            if (commandType == null || !this.IsBirdPhotoCommandShape(commandType, out detailInfoType))
            {
                status = "TakingBirdPhotoCommand unavailable";
                this.BirdFarmNetLog("Direct bird command resolver: could not find TakingBirdPhotoCommand.");
                return false;
            }

            if (this.cachedBirdPhotoReliableChannelValue == null)
            {
                Type channelType = this.FindLoadedType("XD.GameGerm.Network.ChannelType", "ChannelType");
                if (channelType == null)
                {
                    status = "ChannelType unavailable";
                    return false;
                }

                this.cachedBirdPhotoReliableChannelValue = Enum.Parse(channelType, "Reliable");
            }

            this.cachedBirdPhotoCommandRuntimeType = commandType;
            this.cachedBirdPhotoDetailInfoRuntimeType = detailInfoType;
            status = "Bird photo command types ready";
            return true;
        }

        private object TryResolveBirdPhotoClientPeerRuntimeObject()
        {
            if (this.cachedBirdPhotoClientPeerRuntimeObject != null)
            {
                return this.cachedBirdPhotoClientPeerRuntimeObject;
            }

            Type[] managerTypes = new Type[]
            {
                this.FindLoadedType("EcsSystem.World.XDTownClientNetworkManager", "XDTownClientNetworkManager"),
                this.FindLoadedType("EcsSystem.XD.GameGerm.Ecs.Boost.Client.ClientNetworkManager", "ClientNetworkManager")
            };

            foreach (Type managerType in managerTypes)
            {
                if (managerType == null)
                {
                    continue;
                }

                object manager = this.TryGetStaticObjectAcrossHierarchy(managerType, "Instance", "_instance", "instance", "Current", "Singleton");
                if (manager == null && typeof(UnityEngine.Object).IsAssignableFrom(managerType))
                {
                    try
                    {
                        UnityEngine.Object[] sceneObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                        if (sceneObjects != null)
                        {
                            foreach (UnityEngine.Object sceneObject in sceneObjects)
                            {
                                if (sceneObject == null)
                                {
                                    continue;
                                }

                                Type sceneObjectType = sceneObject.GetType();
                                if (sceneObjectType != null && managerType.IsAssignableFrom(sceneObjectType))
                                {
                                    manager = sceneObject;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (manager == null)
                {
                    continue;
                }

                if (this.TryGetObjectMember(manager, "ClientPeer", out object clientPeer) && clientPeer != null)
                {
                    return clientPeer;
                }

                if (this.TryGetObjectMember(manager, "EcsClientNetwork", out object ecsClientNetwork) && ecsClientNetwork != null)
                {
                    if (this.TryGetObjectMember(ecsClientNetwork, "ClientPeer", out clientPeer) && clientPeer != null)
                    {
                        return clientPeer;
                    }
                }
            }

            return null;
        }

        private bool IsBirdPhotoCommandShape(Type commandType, out Type detailInfoType)
        {
            detailInfoType = null;
            if (commandType == null)
            {
                return false;
            }

            FieldInfo birdNetIdField = this.FindFieldInHierarchy(commandType, "BirdNetId");
            FieldInfo birdStaticIdField = this.FindFieldInHierarchy(commandType, "BirdStaticId");
            FieldInfo detailInfoField = this.FindFieldInHierarchy(commandType, "DetailInfo");
            FieldInfo isSuccessField = this.FindFieldInHierarchy(commandType, "IsSuccess");
            FieldInfo scannerNetIdField = this.FindFieldInHierarchy(commandType, "ScannerNetId");
            if (birdNetIdField == null
                || birdStaticIdField == null
                || detailInfoField == null
                || isSuccessField == null
                || scannerNetIdField == null)
            {
                return false;
            }

            if (birdNetIdField.FieldType != typeof(uint)
                || birdStaticIdField.FieldType != typeof(int)
                || isSuccessField.FieldType != typeof(bool)
                || scannerNetIdField.FieldType != typeof(uint))
            {
                return false;
            }

            detailInfoType = detailInfoField.FieldType;
            return this.IsBirdPhotoDetailInfoShape(detailInfoType, out _);
        }

        private bool TryResolveDirectBirdPhotoProtocol(out MethodInfo protocolMethod, out Type detailInfoType, out string status)
        {
            protocolMethod = this.cachedBirdPhotoDirectMethod;
            detailInfoType = this.cachedBirdPhotoDetailInfoRuntimeType;
            status = this.cachedBirdPhotoDirectResolveStatus;

            if (protocolMethod != null && detailInfoType != null)
            {
                status = "Direct runtime cached";
                this.BirdFarmNetLog("Direct bird protocol resolver: using cached runtime method.");
                return true;
            }

            float now = Time.unscaledTime;
            if (this.cachedBirdPhotoDirectResolveNextAttemptAt > now)
            {
                return false;
            }

            try
            {
                Type protocolType = this.cachedBirdProtocolManagerRuntimeType
                    ?? this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.GamePlay.Bird.BirdProtocolManager",
                        "BirdProtocolManager");
                detailInfoType = this.cachedBirdPhotoDetailInfoRuntimeType
                    ?? this.FindLoadedType(
                        "XDT.Scene.Shared.Modules.BirdWatching.BirdPhotoDetailInfo",
                        "BirdPhotoDetailInfo");
                if (protocolType != null && detailInfoType != null)
                {
                    Type resolvedDetailInfoType = detailInfoType;
                    protocolMethod = protocolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .FirstOrDefault(m =>
                        {
                            if (!string.Equals(m.Name, "TakingBirdPhoto", StringComparison.Ordinal))
                            {
                                return false;
                            }

                            ParameterInfo[] parameters = m.GetParameters();
                            return parameters.Length == 4
                                && parameters[0].ParameterType == typeof(int)
                                && parameters[1].ParameterType == typeof(bool)
                                && parameters[2].ParameterType == resolvedDetailInfoType
                                && parameters[3].ParameterType == typeof(uint);
                        });
                }

                if ((protocolMethod == null || detailInfoType == null || protocolType == null)
                    && !this.TryResolveDirectBirdPhotoProtocolBySignature(out protocolType, out protocolMethod, out detailInfoType, out string signatureStatus))
                {
                    status = signatureStatus;
                    this.cachedBirdPhotoDirectResolveStatus = status;
                    this.cachedBirdPhotoDirectResolveNextAttemptAt = now + 2f;
                    return false;
                }

                if (protocolMethod == null)
                {
                    status = "Direct protocol method unavailable";
                    this.cachedBirdPhotoDirectResolveStatus = status;
                    this.cachedBirdPhotoDirectResolveNextAttemptAt = now + 2f;
                    return false;
                }

                if (!this.TryResolveDirectBirdPhotoDetailInfoFields(detailInfoType, out status))
                {
                    this.cachedBirdPhotoDirectResolveStatus = status;
                    this.cachedBirdPhotoDirectResolveNextAttemptAt = now + 2f;
                    return false;
                }

                this.cachedBirdProtocolManagerRuntimeType = protocolType;
                this.cachedBirdPhotoDetailInfoRuntimeType = detailInfoType;
                this.cachedBirdPhotoDirectMethod = protocolMethod;
                this.cachedBirdPhotoDirectResolveStatus = "Direct runtime ready";
                this.cachedBirdPhotoDirectResolveNextAttemptAt = -999f;
                this.BirdFarmNetLog("Direct bird protocol resolver: resolved " + protocolType.FullName + ".TakingBirdPhoto with detail type " + detailInfoType.FullName);
                status = this.cachedBirdPhotoDirectResolveStatus;
                return true;
            }
            catch (Exception ex)
            {
                status = "Direct resolve error: " + ex.Message;
                this.cachedBirdPhotoDirectResolveStatus = status;
                this.cachedBirdPhotoDirectResolveNextAttemptAt = now + 2f;
                this.BirdFarmNetLog("TryResolveDirectBirdPhotoProtocol error: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveDirectBirdPhotoProtocolBySignature(out Type protocolType, out MethodInfo protocolMethod, out Type detailInfoType, out string status)
        {
            protocolType = null;
            protocolMethod = null;
            detailInfoType = null;
            status = "Direct protocol type unavailable";

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] assemblyTypes;
                    try
                    {
                        assemblyTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        assemblyTypes = ex.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    if (assemblyTypes == null)
                    {
                        continue;
                    }

                    foreach (Type candidateType in assemblyTypes)
                    {
                        if (candidateType == null)
                        {
                            continue;
                        }

                        MethodInfo[] methods;
                        try
                        {
                            methods = candidateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (MethodInfo candidateMethod in methods)
                        {
                            if (candidateMethod == null
                                || !string.Equals(candidateMethod.Name, "TakingBirdPhoto", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            ParameterInfo[] parameters;
                            try
                            {
                                parameters = candidateMethod.GetParameters();
                            }
                            catch
                            {
                                continue;
                            }

                            if (parameters.Length != 4
                                || parameters[0].ParameterType != typeof(int)
                                || parameters[1].ParameterType != typeof(bool)
                                || parameters[3].ParameterType != typeof(uint))
                            {
                                continue;
                            }

                            Type candidateDetailType = parameters[2].ParameterType;
                            string detailReason;
                            if (!this.IsBirdPhotoDetailInfoShape(candidateDetailType, out detailReason))
                            {
                                continue;
                            }

                            protocolType = candidateType;
                            protocolMethod = candidateMethod;
                            detailInfoType = candidateDetailType;
                            status = "Direct runtime signature match";
                            this.BirdFarmNetLog("Direct bird protocol resolver: signature matched " + candidateType.FullName + "." + candidateMethod.Name + " detailType=" + candidateDetailType.FullName);
                            return true;
                        }
                    }
                }

                status = "Direct protocol type unavailable";
                this.BirdFarmNetLog("Direct bird protocol resolver: signature scan found no TakingBirdPhoto candidate.");
                return false;
            }
            catch (Exception ex)
            {
                status = "Direct signature resolve error: " + ex.Message;
                this.BirdFarmNetLog("TryResolveDirectBirdPhotoProtocolBySignature error: " + ex.Message);
                return false;
            }
        }

        private bool IsBirdPhotoDetailInfoShape(Type candidateType, out string reason)
        {
            reason = "unknown";
            if (candidateType == null)
            {
                reason = "type null";
                return false;
            }

            if (!candidateType.IsValueType && !candidateType.IsClass)
            {
                reason = "not object-like";
                return false;
            }

            FieldInfo actionStarField = this.FindFieldInHierarchy(candidateType, "ActionStar");
            FieldInfo actionTypeField = this.FindFieldInHierarchy(candidateType, "BirdActionType");
            FieldInfo standNetIdField = this.FindFieldInHierarchy(candidateType, "BirdStandNetId");
            FieldInfo coverStarField = this.FindFieldInHierarchy(candidateType, "IsCoverStar");
            FieldInfo perfectStarField = this.FindFieldInHierarchy(candidateType, "IsPerfectStar");
            FieldInfo usingZoomField = this.FindFieldInHierarchy(candidateType, "IsUsingZoom");

            bool matched = actionStarField != null
                && actionTypeField != null
                && standNetIdField != null
                && coverStarField != null
                && perfectStarField != null
                && usingZoomField != null
                && actionStarField.FieldType == typeof(int)
                && actionTypeField.FieldType == typeof(int)
                && standNetIdField.FieldType == typeof(uint)
                && coverStarField.FieldType == typeof(bool)
                && perfectStarField.FieldType == typeof(bool)
                && usingZoomField.FieldType == typeof(bool);

            reason = matched ? "shape match" : "shape mismatch";
            return matched;
        }

        private bool TryResolveDirectBirdPhotoDetailInfoFields(Type detailInfoType, out string status)
        {
            status = "Direct bird detail fields ready";
            if (detailInfoType == null)
            {
                status = "Direct bird detail type unavailable";
                return false;
            }

            if (this.cachedBirdPhotoDetailInfoRuntimeActionStarField != null
                && this.cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField != null
                && this.cachedBirdPhotoDetailInfoRuntimeIsCoverStarField != null
                && this.cachedBirdPhotoDetailInfoRuntimeActionTypeField != null
                && this.cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField != null
                && this.cachedBirdPhotoDetailInfoRuntimeStandNetIdField != null)
            {
                return true;
            }

            this.cachedBirdPhotoDetailInfoRuntimeActionStarField = this.cachedBirdPhotoDetailInfoRuntimeActionStarField ?? this.FindFieldInHierarchy(detailInfoType, "ActionStar");
            this.cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField = this.cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField ?? this.FindFieldInHierarchy(detailInfoType, "IsPerfectStar");
            this.cachedBirdPhotoDetailInfoRuntimeIsCoverStarField = this.cachedBirdPhotoDetailInfoRuntimeIsCoverStarField ?? this.FindFieldInHierarchy(detailInfoType, "IsCoverStar");
            this.cachedBirdPhotoDetailInfoRuntimeActionTypeField = this.cachedBirdPhotoDetailInfoRuntimeActionTypeField ?? this.FindFieldInHierarchy(detailInfoType, "BirdActionType");
            this.cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField = this.cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField ?? this.FindFieldInHierarchy(detailInfoType, "IsUsingZoom");
            this.cachedBirdPhotoDetailInfoRuntimeStandNetIdField = this.cachedBirdPhotoDetailInfoRuntimeStandNetIdField ?? this.FindFieldInHierarchy(detailInfoType, "BirdStandNetId");

            if (this.cachedBirdPhotoDetailInfoRuntimeActionStarField == null
                || this.cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField == null
                || this.cachedBirdPhotoDetailInfoRuntimeIsCoverStarField == null
                || this.cachedBirdPhotoDetailInfoRuntimeActionTypeField == null
                || this.cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField == null
                || this.cachedBirdPhotoDetailInfoRuntimeStandNetIdField == null)
            {
                status = "Direct bird detail fields unavailable";
                return false;
            }

            return true;
        }

        private bool TryBuildDirectBirdPhotoDetailInfo(Type detailInfoType, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out object detailInfo, out string status)
        {
            detailInfo = null;
            status = "Direct bird detail unavailable";

            try
            {
                if (!this.TryResolveDirectBirdPhotoDetailInfoFields(detailInfoType, out status))
                {
                    return false;
                }

                detailInfo = Activator.CreateInstance(detailInfoType);
                if (detailInfo == null)
                {
                    status = "Direct bird detail allocation failed";
                    return false;
                }

                int resolvedBirdActionType = perfectPhotoEnabled ? BirdPoseStretch : birdActionType;
                int actionStar = perfectPhotoEnabled ? 4 : 0;
                this.cachedBirdPhotoDetailInfoRuntimeActionStarField.SetValue(detailInfo, actionStar);
                this.cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField.SetValue(detailInfo, perfectPhotoEnabled);
                this.cachedBirdPhotoDetailInfoRuntimeIsCoverStarField.SetValue(detailInfo, false);
                this.cachedBirdPhotoDetailInfoRuntimeActionTypeField.SetValue(detailInfo, resolvedBirdActionType);
                this.cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField.SetValue(detailInfo, false);
                this.cachedBirdPhotoDetailInfoRuntimeStandNetIdField.SetValue(detailInfo, birdStandNetId);
                status = "Direct bird detail ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Direct bird detail error: " + ex.Message;
                this.BirdFarmNetLog("TryBuildDirectBirdPhotoDetailInfo error: " + ex.Message);
                return false;
            }
        }

        private bool TryInvokeAuraMonoBirdPhotoProtocol(int staticId, uint birdNetId, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled, out string status)
        {
            status = "Protocol unavailable";
            Breadcrumbs.Drop("BirdFarm.photo.aura", "net=" + birdNetId + " static=" + staticId + " stand=" + birdStandNetId);

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoBirdPhotoMethod(out IntPtr methodPtr, out int paramCount, out string source))
                {
                    status = "Protocol method unavailable";
                    return false;
                }

                int resolvedStaticId = staticId > 0 ? staticId : unchecked((int)birdNetId);
                int usePhoto = 1;
                uint resolvedBirdNetId = birdStandNetId == 0U ? birdNetId : 0U;
                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();

                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[4];
                    args[0] = (IntPtr)(&resolvedStaticId);
                    args[1] = (IntPtr)(&usePhoto);

                    IntPtr detailArg;
                    if (paramCount == 4
                        && detailClass != IntPtr.Zero
                        && auraMonoObjectNew != null
                        && auraMonoObjectUnbox != null
                        && this.auraMonoRootDomain != IntPtr.Zero)
                    {
                        IntPtr detailObj = auraMonoObjectNew(this.auraMonoRootDomain, detailClass);
                        if (detailObj != IntPtr.Zero)
                        {
                            detailArg = auraMonoObjectUnbox(detailObj);
                            if (detailArg != IntPtr.Zero)
                            {
                                this.TryWriteBirdPhotoDetailInfoFields(detailObj, birdActionType, birdStandNetId, perfectPhotoEnabled);
                            }
                        }
                        else
                        {
                            byte* detailPtrFallback = stackalloc byte[64];
                            for (int i = 0; i < 64; i++)
                            {
                                detailPtrFallback[i] = 0;
                            }
                            detailArg = (IntPtr)detailPtrFallback;
                        }
                    }
                    else
                    {
                        byte* detailPtr = stackalloc byte[64];
                        for (int i = 0; i < 64; i++)
                        {
                            detailPtr[i] = 0;
                        }
                        detailArg = (IntPtr)detailPtr;
                    }

                    args[2] = detailArg;
                    args[3] = (IntPtr)(&resolvedBirdNetId);
                    auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "Protocol error";
                        BirdNetFarm.TraceCrashBreadcrumb($"Protocol invoke exception netId={birdNetId}; no fallback command sent");
                        this.BirdFarmNetLog("Bird photo protocol threw; no fallback command sent.");
                        return false;
                    }
                }

                status = perfectPhotoEnabled ? "Photo sent (perfect payload Stretch+Perfect)" : "Photo sent";
                return true;
            }
            catch (Exception ex)
            {
                BirdNetFarm.TraceCrashBreadcrumb("Protocol exception: " + ex.GetType().Name + ": " + ex.Message);
                this.BirdFarmNetLog("Bird photo protocol exception; no fallback command sent: " + ex.Message);
                status = "Protocol error: " + ex.Message;
                this.BirdFarmNetLog("TryInvokeAuraMonoBirdPhotoProtocol error: " + ex);
                return false;
            }
        }

        // MaxPhoto auto-scare detection — anti-cheat-safe path.
        // The game dispatches TakingBirdPhotoResultEvent through EventCenter (BirdProtocolManager.InformTakingPhotoResult
        // -> EventCenter.DispatchEvent(in evt)), so we subscribe via the embedded-Mono dispatch-detour engine
        // (RegisterGameEventHook) instead of the old IL2CPP Harmony postfixes on BirdWatchingSystem.InformUiResult /
        // BirdManager.BirdPhotoErrorToast — those wrote GameAssembly .text and were visible to the Themis integrity hash.
        // Payload: { BirdErrorCode errorCode @0 (int32); bool IsTriggerPassive @4 }.
        private const string BirdFarmMaxPhotoResultEventName = "ScriptsRefactory.DataAndProtocol.Events.TakingBirdPhotoResultEvent";
        private const int BirdErrorCodeMaxPhoto = 15; // BirdErrorCode.MaxPhoto (enum ordinal)

        public void TryEnsureBirdFarmMaxPhotoEventHook()
        {
            if (this.birdFarmMaxPhotoHookRegistered)
            {
                return;
            }

            // RegisterGameEventHook only records handler metadata here; the native detour installs lazily.
            if (this.RegisterGameEventHook(BirdFarmMaxPhotoResultEventName, 8, this.OnTakingBirdPhotoResultMaxPhotoEvent))
            {
                this.birdFarmMaxPhotoHookRegistered = true;
                ModLogger.Msg("[BirdFarm] Subscribed to TakingBirdPhotoResultEvent for MaxPhoto auto-scare (EventCenter, no IL2CPP patch).");
            }
        }

        // Runs on the Unity main thread during the EventCenter drain (same thread the old Harmony postfix used),
        // so the AuraMono escape/remove invokes in TryHandleBirdFarmMaxPhotoAutoScare stay main-thread-safe.
        private void OnTakingBirdPhotoResultMaxPhotoEvent(GameEventSnapshot e)
        {
            // errorCode @0 (BirdErrorCode, int32-backed). Only MaxPhoto triggers the auto-scare.
            if (e.ReadInt32(0) != BirdErrorCodeMaxPhoto)
            {
                return;
            }

            this.BirdFarmNetLog("[MaxPhotoDirect] TakingBirdPhotoResultEvent observed: MaxPhoto");
            this.TryHandleBirdFarmMaxPhotoAutoScare("TakingBirdPhotoResultEvent");
        }

        private void TryHandleBirdFarmMaxPhotoAutoScare(string source)
        {
            if (!BirdNetFarm.IsAutoScareMaxPhotoEnabled)
            {
                return;
            }

            if (!this.TryPeekRecentBirdFarmAttempt(out uint netId) || netId == 0U)
            {
                this.BirdFarmNetLog($"MaxPhoto observed via {source} but no recent bird farm netId was available to scare.");
                return;
            }

            float now = Time.unscaledTime;
            if (this.lastBirdFarmMaxPhotoScareNetId == netId && now - this.lastBirdFarmMaxPhotoScareAt < 2f)
            {
                return;
            }

            this.lastBirdFarmMaxPhotoScareNetId = netId;
            this.lastBirdFarmMaxPhotoScareAt = now;
            this.RemovePendingBirdFarmAttempt(netId);

            bool escapeSuccess = this.TryInvokeAuraMonoBirdEscapeProtocol(netId, 0, out string escapeStatus);
            bool removeSuccess = this.TryInvokeAuraMonoBirdRemove(netId, out string removeStatus);
            bool success = escapeSuccess || removeSuccess;
            string status = escapeStatus + "; " + removeStatus;
            if (success)
            {
                this.RememberRecentBirdFarmNetId(netId, 20f);
                if (this.lastBirdFarmRecentPhotoNetId == netId)
                {
                    this.lastBirdFarmRecentPhotoNetId = 0U;
                    this.lastBirdFarmRecentPhotoNetIdAt = -999f;
                }

                this.cachedBirdFarmAuraCandidates.Clear();
                this.cachedBirdFarmAuraCandidatesAt = -999f;
                this.BirdFarmNetLog($"MaxPhoto auto-scare sent for netId={netId} source={source} escape={escapeSuccess} remove={removeSuccess}.");
            }
            else
            {
                this.BirdFarmNetLog($"MaxPhoto auto-scare failed for netId={netId}: {status}");
            }

            int scaredTotal = BirdNetFarm.NotifyMaxPhotoAutoScare(netId, success, status);
            if (success)
            {
                int notificationTotal = this.GetNextBirdMaxPhotoScareNotificationTotal("bird-max-photo-scare");
                this.AddOrUpdateMenuNotification("bird-max-photo-scare", $"Bird scared away: {notificationTotal}", new Color(0.55f, 0.88f, 1f), 10f, true);
                this.BirdFarmNetLog($"MaxPhoto auto-scare notification updated visibleTotal={notificationTotal} sessionTotal={scaredTotal} netId={netId}.");
            }
        }

        private bool TryInvokeAuraMonoBirdEscapeProtocol(uint birdNetId, int delayMilliSec, out string status)
        {
            status = "Bird escape protocol unavailable";

            try
            {
                if (birdNetId == 0U
                    || !this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoBirdEscapeMethod(out IntPtr methodPtr, out int paramCount, out string source))
                {
                    status = "BirdEscape method unavailable";
                    return false;
                }

                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[2];
                    uint netIdArg = birdNetId;
                    int delayArg = delayMilliSec;

                    if (paramCount >= 2)
                    {
                        args[0] = (IntPtr)(&netIdArg);
                        args[1] = (IntPtr)(&delayArg);
                    }
                    else
                    {
                        args[0] = (IntPtr)(&netIdArg);
                    }

                    auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "BirdEscape threw";
                        return false;
                    }
                }

                status = "BirdEscape sent via " + source;
                return true;
            }
            catch (Exception ex)
            {
                status = "BirdEscape error: " + ex.Message;
                this.BirdFarmNetLog("TryInvokeAuraMonoBirdEscapeProtocol error: " + ex);
                return false;
            }
        }

        private bool TryInvokeAuraMonoBirdRemove(uint birdNetId, out string status)
        {
            status = "Bird remove protocol unavailable";

            try
            {
                if (birdNetId == 0U
                    || !this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoBirdRemoveMethod(out IntPtr methodPtr, out int paramCount, out string source))
                {
                    status = "RemoveBird method unavailable";
                    return false;
                }

                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    uint netIdArg = birdNetId;

                    args[0] = (IntPtr)(&netIdArg);
                    auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, paramCount > 0 ? (IntPtr)args : IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "RemoveBird threw";
                        return false;
                    }
                }

                status = "RemoveBird invoked via " + source;
                return true;
            }
            catch (Exception ex)
            {
                status = "RemoveBird error: " + ex.Message;
                this.BirdFarmNetLog("TryInvokeAuraMonoBirdRemove error: " + ex);
                return false;
            }
        }

        private bool TryResolveAuraMonoBirdRemoveMethod(out IntPtr methodPtr, out int paramCount, out string source)
        {
            methodPtr = IntPtr.Zero;
            paramCount = 0;
            source = "none";

            if (this.cachedBirdRemoveMonoMethod != IntPtr.Zero)
            {
                methodPtr = this.cachedBirdRemoveMonoMethod;
                paramCount = this.cachedBirdRemoveMonoMethodParamCount;
                source = "cache";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero && auraMonoClassFromName != null
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr candidate = this.FindAuraMonoMethodOnHierarchy(protocolClass, "RemoveBird", 1);
            if (candidate == IntPtr.Zero)
            {
                return false;
            }

            int candidateParamCount = this.TryGetAuraMonoMethodParamCount(candidate);
            if (candidateParamCount <= 0)
            {
                candidateParamCount = 1;
            }

            this.cachedBirdRemoveMonoMethod = candidate;
            this.cachedBirdRemoveMonoMethodParamCount = candidateParamCount;
            methodPtr = candidate;
            paramCount = candidateParamCount;
            source = "BirdProtocolManager.RemoveBird";
            return true;
        }

        private bool TryResolveAuraMonoBirdEscapeMethod(out IntPtr methodPtr, out int paramCount, out string source)
        {
            methodPtr = IntPtr.Zero;
            paramCount = 0;
            source = "none";

            if (this.cachedBirdEscapeMonoMethod != IntPtr.Zero)
            {
                methodPtr = this.cachedBirdEscapeMonoMethod;
                paramCount = this.cachedBirdEscapeMonoMethodParamCount;
                source = "cache";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero && auraMonoClassFromName != null
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            foreach (int preferredCount in new int[] { 2, 1 })
            {
                IntPtr candidate = this.FindAuraMonoMethodOnHierarchy(protocolClass, "BirdEscape", preferredCount);
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                int candidateParamCount = this.TryGetAuraMonoMethodParamCount(candidate);
                if (candidateParamCount <= 0)
                {
                    candidateParamCount = preferredCount;
                }

                this.cachedBirdEscapeMonoMethod = candidate;
                this.cachedBirdEscapeMonoMethodParamCount = candidateParamCount;
                methodPtr = candidate;
                paramCount = candidateParamCount;
                source = "BirdProtocolManager.BirdEscape";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Writes BirdActionType / BirdStandNetId and optional perfect-photo fields into a
        /// BirdPhotoDetailInfo struct pointer.
        /// Safe to call even if field resolution fails - the zeroed defaults are acceptable fallbacks.
        /// </summary>
        private void TryWriteBirdPhotoDetailInfoFields(IntPtr detailObj, int birdActionType, uint birdStandNetId, bool perfectPhotoEnabled)
        {
            if (detailObj == IntPtr.Zero || auraMonoFieldSetValue == null)
            {
                return;
            }

            try
            {
                this.TryResolveAuraMonoBirdPhotoDetailInfoFields();

                unsafe
                {
                    if (perfectPhotoEnabled)
                    {
                        if (this.cachedBirdPhotoDetailInfoActionStarField != IntPtr.Zero)
                        {
                            int actionStarValue = 4;
                            auraMonoFieldSetValue(detailObj, this.cachedBirdPhotoDetailInfoActionStarField, (IntPtr)(&actionStarValue));
                        }

                        if (this.cachedBirdPhotoDetailInfoIsPerfectStarField != IntPtr.Zero)
                        {
                            bool perfectStarValue = true;
                            auraMonoFieldSetValue(detailObj, this.cachedBirdPhotoDetailInfoIsPerfectStarField, (IntPtr)(&perfectStarValue));
                        }
                    }

                    if (this.cachedBirdPhotoDetailInfoActionTypeField != IntPtr.Zero)
                    {
                        int actionTypeValue = perfectPhotoEnabled ? BirdPoseStretch : birdActionType;
                        auraMonoFieldSetValue(detailObj, this.cachedBirdPhotoDetailInfoActionTypeField, (IntPtr)(&actionTypeValue));
                    }

                    if (this.cachedBirdPhotoDetailInfoStandNetIdField != IntPtr.Zero && birdStandNetId != 0U)
                    {
                        uint standNetIdValue = birdStandNetId;
                        auraMonoFieldSetValue(detailObj, this.cachedBirdPhotoDetailInfoStandNetIdField, (IntPtr)(&standNetIdValue));
                    }
                }
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("TryWriteBirdPhotoDetailInfoFields error: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves and caches the MonoClassField pointers for the BirdPhotoDetailInfo fields we write.
        /// </summary>
        private void TryResolveAuraMonoBirdPhotoDetailInfoFields()
        {
            if (this.cachedBirdPhotoDetailInfoFieldsResolved)
            {
                return;
            }

            if (this.cachedBirdPhotoDetailInfoActionStarField == IntPtr.Zero)
            {
                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                if (detailClass != IntPtr.Zero)
                {
                    IntPtr f = this.FindAuraMonoFieldOnHierarchy(detailClass, "ActionStar");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "actionStar");
                    this.cachedBirdPhotoDetailInfoActionStarField = f;
                }
            }

            if (this.cachedBirdPhotoDetailInfoIsPerfectStarField == IntPtr.Zero)
            {
                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                if (detailClass != IntPtr.Zero)
                {
                    IntPtr f = this.FindAuraMonoFieldOnHierarchy(detailClass, "IsPerfectStar");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "isPerfectStar");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "PerfectStar");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "perfectStar");
                    this.cachedBirdPhotoDetailInfoIsPerfectStarField = f;
                }
            }

            if (this.cachedBirdPhotoDetailInfoActionTypeField != IntPtr.Zero
                && this.cachedBirdPhotoDetailInfoStandNetIdField != IntPtr.Zero)
            {
                this.cachedBirdPhotoDetailInfoFieldsResolved = true;
                return;
            }

            try
            {
                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                if (detailClass == IntPtr.Zero)
                {
                    return;
                }

                // BirdActionType (int) - maps to the server's Pose/ActionType enum
                if (this.cachedBirdPhotoDetailInfoActionTypeField == IntPtr.Zero)
                {
                    IntPtr f = this.FindAuraMonoFieldOnHierarchy(detailClass, "BirdActionType");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "birdActionType");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "ActionType");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "actionType");
                    this.cachedBirdPhotoDetailInfoActionTypeField = f;
                }

                // BirdStandNetId (uint) - stand perch association
                if (this.cachedBirdPhotoDetailInfoStandNetIdField == IntPtr.Zero)
                {
                    IntPtr f = this.FindAuraMonoFieldOnHierarchy(detailClass, "BirdStandNetId");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "birdStandNetId");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "StandNetId");
                    if (f == IntPtr.Zero) f = this.FindAuraMonoFieldOnHierarchy(detailClass, "standNetId");
                    this.cachedBirdPhotoDetailInfoStandNetIdField = f;
                }
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("TryResolveAuraMonoBirdPhotoDetailInfoFields error: " + ex.Message);
            }
            finally
            {
                this.cachedBirdPhotoDetailInfoFieldsResolved = true;
            }
        }

        private bool TryInvokeAuraMonoBirdCamouflage(uint birdNetId, out string status)
        {
            status = "Aura mono bird camouflage unavailable";

            try
            {
                if (birdNetId == 0U
                    || !this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || this.auraMonoRootDomain == IntPtr.Zero
                    || auraMonoObjectNew == null
                    || auraMonoRuntimeInvoke == null
                    || auraMonoFieldSetValue == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoBirdCamouflageMembers(out IntPtr classPtr, out IntPtr useMethod, out IntPtr ctorMethod, out IntPtr netIdField))
                {
                    status = "Aura mono bird camouflage members unavailable";
                    return false;
                }

                IntPtr commandObj = auraMonoObjectNew(this.auraMonoRootDomain, classPtr);
                if (commandObj == IntPtr.Zero)
                {
                    status = "Aura mono bird camouflage allocation failed";
                    return false;
                }

                if (ctorMethod != IntPtr.Zero)
                {
                    IntPtr ctorExc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(ctorMethod, commandObj, IntPtr.Zero, ref ctorExc);
                    if (ctorExc != IntPtr.Zero)
                    {
                        this.BirdFarmNetLog("Aura mono bird camouflage .ctor threw; continuing.");
                    }
                }

                unsafe
                {
                    uint netIdValue = birdNetId;
                    auraMonoFieldSetValue(commandObj, netIdField, (IntPtr)(&netIdValue));
                }

                IntPtr exc = IntPtr.Zero;
                BirdNetFarm.TraceCrashBreadcrumb($"Camouflage start netId={birdNetId}");
                auraMonoRuntimeInvoke(useMethod, commandObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    BirdNetFarm.TraceCrashBreadcrumb($"Camouflage invoke exception netId={birdNetId}");
                    status = "Aura mono bird camouflage exception";
                    return false;
                }

                BirdNetFarm.TraceCrashBreadcrumb($"Camouflage success netId={birdNetId}");
                status = "Bird camouflage sent via aura mono";
                return true;
            }
            catch (Exception ex)
            {
                BirdNetFarm.TraceCrashBreadcrumb("Camouflage exception: " + ex.GetType().Name + ": " + ex.Message);
                status = "Aura mono bird camouflage exception: " + ex.Message;
                this.BirdFarmNetLog("TryInvokeAuraMonoBirdCamouflage error: " + ex);
                return false;
            }
        }

        private bool TryResolveAuraMonoBirdCamouflageMembers(out IntPtr classPtr, out IntPtr useMethod, out IntPtr ctorMethod, out IntPtr netIdField)
        {
            classPtr = this.cachedBirdCamouflageBackpackClass;
            useMethod = this.cachedBirdCamouflageUseMethod;
            ctorMethod = this.cachedBirdCamouflageCtorMethod;
            netIdField = this.cachedBirdCamouflageNetIdField;
            if (classPtr != IntPtr.Zero && useMethod != IntPtr.Zero && netIdField != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
            if (levelImage != IntPtr.Zero && auraMonoClassFromName != null)
            {
                classPtr = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Interaction", "BackpackBirdCamouflage");
            }

            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Interaction", "BackpackBirdCamouflage");
            }

            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            useMethod = this.FindAuraMonoMethodOnHierarchy(classPtr, "UseBirdCamouflage", 0);
            netIdField = this.FindAuraMonoFieldOnHierarchy(classPtr, "_netId");
            if (netIdField == IntPtr.Zero)
            {
                netIdField = this.FindAuraMonoFieldOnHierarchy(classPtr, "netId");
            }

            ctorMethod = auraMonoClassGetMethodFromName != null
                ? auraMonoClassGetMethodFromName(classPtr, ".ctor", 0)
                : IntPtr.Zero;

            if (useMethod == IntPtr.Zero || netIdField == IntPtr.Zero)
            {
                return false;
            }

            this.cachedBirdCamouflageBackpackClass = classPtr;
            this.cachedBirdCamouflageUseMethod = useMethod;
            this.cachedBirdCamouflageCtorMethod = ctorMethod;
            this.cachedBirdCamouflageNetIdField = netIdField;
            return true;
        }

        private IntPtr TryCreateAuraMonoBirdPhotoDetailArg()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || this.auraMonoRootDomain == IntPtr.Zero
                    || auraMonoObjectNew == null
                    || auraMonoRuntimeObjectInit == null
                    || auraMonoObjectUnbox == null)
                {
                    return IntPtr.Zero;
                }

                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                if (detailClass == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                IntPtr detailObj = auraMonoObjectNew(this.auraMonoRootDomain, detailClass);
                if (detailObj == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                auraMonoRuntimeObjectInit(detailObj);
                IntPtr unboxed = auraMonoObjectUnbox(detailObj);
                return unboxed;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr TryResolveAuraMonoBirdPhotoDetailInfoClass()
        {
            if (this.cachedBirdPhotoDetailInfoMonoClass != IntPtr.Zero)
            {
                return this.cachedBirdPhotoDetailInfoMonoClass;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return IntPtr.Zero;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            if (dataImage != IntPtr.Zero)
            {
                this.cachedBirdPhotoDetailInfoMonoClass = auraMonoClassFromName(dataImage, "XDT.Scene.Shared.Modules.BirdWatching", "BirdPhotoDetailInfo");
            }

            if (this.cachedBirdPhotoDetailInfoMonoClass == IntPtr.Zero)
            {
                IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage != IntPtr.Zero)
                {
                    this.cachedBirdPhotoDetailInfoMonoClass = auraMonoClassFromName(ecsImage, "XDT.Scene.Shared.Modules.BirdWatching", "BirdPhotoDetailInfo");
                }
            }

            if (this.cachedBirdPhotoDetailInfoMonoClass == IntPtr.Zero)
            {
                this.cachedBirdPhotoDetailInfoMonoClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.BirdWatching", "BirdPhotoDetailInfo");
            }

            if (this.cachedBirdPhotoDetailInfoMonoClass == IntPtr.Zero)
            {
                this.cachedBirdPhotoDetailInfoMonoClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdPhotoDetailInfo");
            }

            return this.cachedBirdPhotoDetailInfoMonoClass;
        }

        private bool TryResolveAuraMonoBirdPhotoMethod(out IntPtr methodPtr, out int paramCount, out string source)
        {
            methodPtr = IntPtr.Zero;
            paramCount = 0;
            source = "none";

            if (this.cachedBirdPhotoMonoMethod != IntPtr.Zero)
            {
                methodPtr = this.cachedBirdPhotoMonoMethod;
                paramCount = this.cachedBirdPhotoMonoMethodParamCount;
                source = "cache";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero && auraMonoClassFromName != null
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                this.BirdFarmNetLog("Aura mono bird protocol class unavailable.");
                return false;
            }

            string[] methodNames = new string[] { "TakingBirdPhoto", "SendTakingBirdPhoto", "CmdTakingBirdPhoto", "BirdPhoto" };
            int[] preferredCounts = new int[] { 4, 3, 5 };
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

                    this.cachedBirdPhotoMonoMethod = candidate;
                    this.cachedBirdPhotoMonoMethodParamCount = candidateParamCount;
                    methodPtr = candidate;
                    paramCount = candidateParamCount;
                    source = methodName;
                    return true;
                }
            }

            return false;
        }

        private int GetNextBirdMaxPhotoScareNotificationTotal(string key)
        {
            float now = Time.unscaledTime;
            bool activeNotificationFound = false;
            for (int i = 0; i < this.menuNotifications.Count; i++)
            {
                HeartopiaComplete.MenuNotification existing = this.menuNotifications[i];
                if (existing != null
                    && existing.ExpireAt > now
                    && string.Equals(existing.Key, key, StringComparison.Ordinal))
                {
                    activeNotificationFound = true;
                    break;
                }
            }

            if (!activeNotificationFound)
            {
                this.birdMaxPhotoScareNotificationTotal = 0;
            }

            this.birdMaxPhotoScareNotificationTotal++;
            return this.birdMaxPhotoScareNotificationTotal;
        }

        private void EnsureBirdPhotoRuntimeProbePatch()
        {
            if (this.birdPhotoAuraMonoDiscoveryComplete)
            {
                return;
            }

            if (Time.unscaledTime < this.nextBirdPhotoRuntimeProbePatchAttemptAt)
            {
                return;
            }
            this.nextBirdPhotoRuntimeProbePatchAttemptAt = Time.unscaledTime + 10f;

            this.TryRunBirdPhotoAuraMonoDiscoveryProbe();
        }

        private void TryRunBirdPhotoAuraMonoDiscoveryProbe()
        {
            if (this.birdPhotoAuraMonoDiscoveryComplete)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                IntPtr protocolMethod = IntPtr.Zero;
                int protocolParamCount = 0;
                string protocolSource = "none";
                bool protocolReady = this.TryResolveAuraMonoBirdPhotoMethod(out protocolMethod, out protocolParamCount, out protocolSource);
                IntPtr detailClass = this.TryResolveAuraMonoBirdPhotoDetailInfoClass();
                IntPtr commandClass = this.TryResolveAuraMonoBirdPhotoCommandClass();
                IntPtr networkClientClass = this.TryResolveAuraMonoNetworkClientClass();
                IntPtr networkSendMethod = networkClientClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(networkClientClass, "Send", 3)
                    : IntPtr.Zero;

                this.BirdFarmNetLog("Bird photo AuraMono discovery: protocol=" + protocolReady
                    + " method=0x" + protocolMethod.ToInt64().ToString("X")
                    + " source=" + protocolSource
                    + " params=" + protocolParamCount
                    + " detailClass=0x" + detailClass.ToInt64().ToString("X")
                    + " commandClass=0x" + commandClass.ToInt64().ToString("X")
                    + " networkClientClass=0x" + networkClientClass.ToInt64().ToString("X")
                    + " networkSend=0x" + networkSendMethod.ToInt64().ToString("X"));

                this.birdPhotoAuraMonoDiscoveryComplete = protocolReady || detailClass != IntPtr.Zero || commandClass != IntPtr.Zero || networkSendMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("Bird photo AuraMono discovery failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private IntPtr TryResolveAuraMonoBirdPhotoCommandClass()
        {
            if (this.cachedBirdPhotoCommandMonoClass != IntPtr.Zero)
            {
                return this.cachedBirdPhotoCommandMonoClass;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return IntPtr.Zero;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage != IntPtr.Zero)
            {
                this.cachedBirdPhotoCommandMonoClass = auraMonoClassFromName(ecsImage, "XDT.Scene.Shared.Modules.BirdWatching", "TakingBirdPhotoCommand");
            }

            if (this.cachedBirdPhotoCommandMonoClass == IntPtr.Zero)
            {
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero)
                {
                    this.cachedBirdPhotoCommandMonoClass = auraMonoClassFromName(dataImage, "XDT.Scene.Shared.Modules.BirdWatching", "TakingBirdPhotoCommand");
                }
            }

            if (this.cachedBirdPhotoCommandMonoClass == IntPtr.Zero)
            {
                this.cachedBirdPhotoCommandMonoClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.BirdWatching", "TakingBirdPhotoCommand");
            }

            return this.cachedBirdPhotoCommandMonoClass;
        }

        private bool ShouldTrackBirdObject(string lowerName)
        {
            return !string.IsNullOrEmpty(lowerName)
                && lowerName.StartsWith("p_bird_bird", StringComparison.Ordinal)
                && lowerName.EndsWith("(clone)", StringComparison.Ordinal)
                && !lowerName.Contains("birdscanner");
        }

        // Token: 0x06000020 RID: 32 RVA: 0x000061FC File Offset: 0x000043FC
        private void VacuumBirds()
        {
            Breadcrumbs.Tick("BirdFarm.vacuum");
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (!flag)
            {
                Transform transform = gameObject.transform;
                Vector3 position = transform.position + transform.forward * 3f;
                position.y = transform.position.y;
                GameObject[] array = Object.FindObjectsOfType<GameObject>();
                foreach (GameObject gameObject2 in array)
                {
                    bool flag2 = gameObject2 == null || gameObject2.name == null;
                    if (!flag2)
                    {
                        string text = gameObject2.name.ToLower();
                        bool flag3 = text.Contains("p_bird") && !text.Contains("birdscanner");
                        if (flag3)
                        {
                            gameObject2.transform.position = position;
                        }
                    }
                }
            }
        }

        private void StartAutoBuyBird()
        {
            try
            {
                GameObject p = GameObject.Find("p_player_skeleton(Clone)");
                if (p != null) this.autoBuyBirdSavedPosition = p.transform.position;
                this.autoBuyBirdSubState = 1;
                this.autoBuyBirdStepTimer = Time.unscaledTime + 0.1f;
                this.autoBuyBirdShopWaitStartedAt = 0f;
                this.autoBuyBirdStoreSelectRetryCount = 0;
                this.autoBuyBirdCurrentItemIndex = 0;
                this.autoBuyBirdPurchasedCount = 0;
                this.autoBuyBirdShopScrollStep = -1;
                this.autoBuyBirdPreviousGameSpeed = this.gameSpeed;
                this.SetGameSpeed(5f);
                this.autoBuyBirdForcedGameSpeed = true;
                this.TeleportToLocation(this.autoBuyBirdNearbyPos);
                LogAutoBuy("[Birdwatching] Started: teleporting to nearby position (Game Speed x5.0)");
            }
            catch (Exception ex) { LogAutoBuy("[Birdwatching] Start error: " + ex.Message); this.StopAutoBuyBird("Start error"); }
        }

        private void StopAutoBuyBird(string reason)
        {
            LogAutoBuy("[Birdwatching] Stopped: " + reason);
            this.CloseAutoBuyPanels();
            this.autoBuyBirdEnabled = false;
            this.autoBuyBirdSubState = 0;
            this.autoBuyBirdShopScrollStep = -1;
            if (this.autoBuyBirdForcedGameSpeed)
            {
                this.SetGameSpeed(Mathf.Max(1f, this.autoBuyBirdPreviousGameSpeed));
                this.autoBuyBirdForcedGameSpeed = false;
            }
            if (this.autoBuyBirdSavedPosition != Vector3.zero)
            {
                this.TeleportToLocation(this.autoBuyBirdSavedPosition);
                this.autoBuyBirdSavedPosition = Vector3.zero;
            }
        }

        private void RunAutoBuyBirdLogic()
        {
            try
            {
                if (!this.autoBuyBirdEnabled)
                {
                    if (this.autoBuyBirdForcedGameSpeed)
                    {
                        this.SetGameSpeed(Mathf.Max(1f, this.autoBuyBirdPreviousGameSpeed));
                        this.autoBuyBirdForcedGameSpeed = false;
                    }
                    return;
                }
                float now = Time.unscaledTime;

                // Close any popup that appears
                if (now >= this.autoBuyPopupCloseRetryAt && this.TryCloseAutoBuyObtainedPopup())
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.12f;
                    this.autoBuyBirdStepTimer = now + 0.12f;
                    return;
                }
                if (now >= this.autoBuyPopupCloseRetryAt)
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.2f;
                }

                switch (this.autoBuyBirdSubState)
                {
                    case 1: // teleporting to nearby position
                        if (this.teleportFramesRemaining <= 0)
                        {
                            this.autoBuyBirdSubState = 12;
                            this.autoBuyBirdStepTimer = now + 3f;
                            LogAutoBuy("[Birdwatching] Arrived at nearby position, waiting before approaching NPC");
                        }
                        break;
                    case 12: // waiting at nearby pos, then teleport to NPC front
                        if (now < this.autoBuyBirdStepTimer) break;
                        this.TeleportToLocation(this.autoBuyBirdTargetPos);
                        this.autoBuyBirdSubState = 2;
                        this.autoBuyBirdStepTimer = now + 0.8f;
                        LogAutoBuy("[Birdwatching] Teleporting to NPC front position");
                        break;
                    case 2: // waiting for dialogue - click chat icon until dialogue shows
                        if (now < this.autoBuyBirdStepTimer) break;
                        if (TryClickNpcChatIcon()) { this.autoBuyBirdStepTimer = now + 0.5f; }
                        else { this.autoBuyBirdStepTimer = now + 0.12f; }
                        GameObject dlg = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                        if (dlg != null && dlg.activeInHierarchy)
                        {
                            this.autoBuyBirdSubState = 3; this.autoBuyBirdStepTimer = now + 0.2f; LogAutoBuy("[Birdwatching] Dialogue opened");
                        }
                        break;
                    case 3: // select birdwatching store
                        if (now < this.autoBuyBirdStepTimer) break;
                        if (!HasDialogueOptionsVisible())
                        {
                            if (TryAdvanceDialogueText())
                            {
                                LogAutoBuy("[Birdwatching] Advanced dialogue text, waiting for options");
                            }
                            this.autoBuyBirdStepTimer = now + 0.12f;
                            break;
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "birdwatching", "bird", "store" }))
                        {
                            this.autoBuyBirdSubState = 31;
                            this.autoBuyBirdStepTimer = now + 0.25f;
                            this.autoBuyBirdShopWaitStartedAt = now;
                            this.autoBuyBirdStoreSelectRetryCount++;
                            LogAutoBuy("[Birdwatching] Selected Birdwatching Store, waiting for shop content");
                        }
                        else { this.autoBuyBirdStepTimer = now + 0.15f; }
                        break;
                    case 31: // wait for ShopPanel to appear and be populated
                        if (now < this.autoBuyBirdStepTimer) break;
                        GameObject shopChk = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                        if (shopChk != null && shopChk.activeInHierarchy)
                        {
                            Transform contentChk = shopChk.transform.Find("goods@scroll/Content");
                            if (contentChk != null && contentChk.childCount > 0)
                            {
                                this.autoBuyBirdSubState = 4; this.autoBuyBirdStepTimer = now + 0.12f; LogAutoBuy("[Birdwatching] ShopPanel populated, proceeding to buy");
                                break;
                            }
                            else
                            {
                                LogAutoBuy("[Birdwatching] Waiting for ShopPanel content to populate...");
                                this.autoBuyBirdStepTimer = now + 0.25f;
                                break;
                            }
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "birdwatching", "bird", "store" }))
                        {
                            this.autoBuyBirdStoreSelectRetryCount++;
                            this.autoBuyBirdStepTimer = now + 0.25f;
                            LogAutoBuy("[Birdwatching] Retried Birdwatching Store option while waiting for shop");
                            break;
                        }
                        if (TryAdvanceDialogueText())
                        {
                            this.autoBuyBirdStepTimer = now + 0.12f;
                            break;
                        }
                        if (this.autoBuyBirdShopWaitStartedAt <= 0f) this.autoBuyBirdShopWaitStartedAt = now;
                        if ((now - this.autoBuyBirdShopWaitStartedAt) > 2.5f)
                        {
                            LogAutoBuy("[Birdwatching] Shop panel did not open yet, returning to store selection");
                            this.autoBuyBirdSubState = 3;
                            this.autoBuyBirdStepTimer = now + 0.1f;
                            this.autoBuyBirdShopWaitStartedAt = 0f;
                            break;
                        }
                        this.autoBuyBirdStepTimer = now + 0.25f;
                        break;
                    case 4: // buying items
                        if (now < this.autoBuyBirdStepTimer) break;
                        if (this.autoBuyBirdCurrentItemIndex >= this.autoBuyBirdItemsMatch.Length)
                        {
                            this.autoBuyBirdSubState = 5;
                            this.autoBuyBirdStepTimer = now + 3f;
                            LogAutoBuy("[Birdwatching] Finished item loop, waiting before return");
                            break;
                        }
                        string match = this.autoBuyBirdItemsMatch[this.autoBuyBirdCurrentItemIndex];
                        // Max is 10 for bird items - buy up to max
                        if (this.autoBuyBirdPurchasedCount >= this.autoBuyBirdMaxPerItem)
                        {
                            this.autoBuyBirdPurchasedCount = 0; this.autoBuyBirdCurrentItemIndex++; this.autoBuyBirdShopScrollStep = -1; this.autoBuyBirdStepTimer = now + 0.1f; break;
                        }
                        bool clicked = ClickCookingStoreItemByMatch(match);
                        if (clicked)
                        {
                            this.autoBuyBirdShopScrollStep = -1;
                            this.autoBuyBirdSubState = 41;
                            this.autoBuyBirdStepTimer = now + 0.12f;
                            if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Birdwatching] Opened purchase dialog for {match}"); }
                        }
                        else
                        {
                            GameObject shopProbe = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                            Transform contentProbe = shopProbe != null ? shopProbe.transform.Find("goods@scroll/Content") : null;
                            if (shopProbe != null && contentProbe != null && contentProbe.childCount == 0)
                            {
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Birdwatching] Shop content empty for item {match}, retrying shortly"); }
                                this.autoBuyBirdStepTimer = now + 0.25f;
                            }
                            else if (this.autoBuyBirdShopScrollStep >= 0 && this.autoBuyBirdShopScrollStep < 12)
                            {
                                this.autoBuyBirdStepTimer = now + 0.15f;
                            }
                            else
                            {
                                this.autoBuyBirdPurchasedCount = 0; this.autoBuyBirdCurrentItemIndex++; this.autoBuyBirdShopScrollStep = -1; this.autoBuyBirdStepTimer = now + 0.2f;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg("[Birdwatching] Item " + match + " not found or sold out, skipping"); }
                            }
                        }
                        break;
                    case 41: // handle purchase dialog: press +10 until target then Purchase
                        if (now < this.autoBuyBirdStepTimer) break;
                        GameObject sale = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Top/SalePanel(Clone)");
                        if (sale == null || !sale.activeInHierarchy)
                        {
                            this.autoBuyBirdPurchasedCount = 0; this.autoBuyBirdCurrentItemIndex++; this.autoBuyBirdShopScrollStep = -1; this.autoBuyBirdStepTimer = now + 0.2f; this.autoBuyBirdSubState = 4;
                            LogAutoBuy("[Birdwatching] Sale panel not found, skipping"); break;
                        }
                        int currentCount = GetSalePanelCurrentCount(sale);
                        int remainingStock = GetSalePanelRemainingStock(sale);
                        if (currentCount < 0) currentCount = 1;
                        int effectiveMax = this.autoBuyBirdMaxPerItem;
                        if (remainingStock > 0) effectiveMax = Mathf.Min(this.autoBuyBirdMaxPerItem, remainingStock);
                        if (currentCount >= effectiveMax)
                        {
                            if (ClickSalePurchase(sale))
                            {
                                this.autoBuyBirdPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Birdwatching] Purchased {currentCount} items (target: {effectiveMax})"); }
                            }
                            this.autoBuyBirdPurchasedCount = 0; this.autoBuyBirdCurrentItemIndex++; this.autoBuyBirdShopScrollStep = -1; this.autoBuyBirdSubState = 4; this.autoBuyBirdStepTimer = now + 0.25f;
                            break;
                        }
                        int needed = effectiveMax - currentCount;
                        int clicks = Mathf.CeilToInt((float)needed / 10f);
                        int doClicks = Mathf.Min(clicks, 3);
                        bool anyClicked = false;
                        for (int i = 0; i < doClicks; i++)
                        {
                            if (ClickSaleAddMore(sale)) { anyClicked = true; }
                        }
                        this.autoBuyBirdStepTimer = now + 0.12f;
                        if (!anyClicked)
                        {
                            if (currentCount > 0 && ClickSalePurchase(sale))
                            {
                                this.autoBuyBirdPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Birdwatching] Purchased {currentCount} items (shop stock limited, target was {effectiveMax})"); }
                            }
                            else
                            {
                                LogAutoBuy("[Birdwatching] Could not purchase item - no stock available");
                            }
                            this.autoBuyBirdPurchasedCount = 0; this.autoBuyBirdCurrentItemIndex++; this.autoBuyBirdShopScrollStep = -1; this.autoBuyBirdSubState = 4; this.autoBuyBirdStepTimer = now + 0.25f;
                        }
                        break;
                    case 5: // return
                        this.CloseAutoBuyPanels();
                        if (now < this.autoBuyBirdStepTimer) break;
                        this.StopAutoBuyBird("Done, returning");
                        break;
                }
            }
            catch (Exception ex) { LogAutoBuy("[Birdwatching] Run error: " + ex.Message); this.StopAutoBuyBird("Error"); }
        }

        private bool IsBirdFarmMaxPhotoToastMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            string lower = message.Trim().ToLowerInvariant();
            return lower.Contains("maxphoto")
                || (lower.Contains("photo") && lower.Contains("limit"))
                || (lower.Contains("info") && lower.Contains("limit"))
                || (lower.Contains("card") && lower.Contains("limit"))
                || (lower.Contains("max") && lower.Contains("photo"));
        }

        private sealed class BirdFarmAuraCandidate
        {
            public uint NetId;
            public int StaticId;
            public float Distance;
            public int BirdActionType;
            public int BirdState;
            public uint BirdStandNetId;
            public bool IsPerchBird;
        }

        private sealed class BirdFarmAuraInspectCandidate
        {
            public IntPtr EntityObj;
            public uint NetId;
            public Vector3 Position;
            public float Distance;
        }

        private sealed class BirdFarmAuraResolvedDetail
        {
            public int StaticId;
            public int BirdActionType;
            public int BirdState;
            public uint BirdStandNetId;
            public bool IsPerchBird;
            public float ExpiresAt;
        }

    }
}
