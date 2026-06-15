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
        private void TryAddHideAndSeekMorphSpot(HashSet<uint> seen, List<HideAndSeekMorphRadarSpot> spots, uint markerNetId, Vector3 position)
        {
            if (markerNetId == 0U || position.sqrMagnitude < 0.01f || seen == null || spots == null)
            {
                return;
            }

            if (!seen.Add(markerNetId))
            {
                return;
            }

            spots.Add(new HideAndSeekMorphRadarSpot
            {
                MarkerNetId = markerNetId,
                Position = position
            });
        }

        private void TryCollectHideAndSeekMorphFromTracks(uint selfNetId, HashSet<uint> seen, List<HideAndSeekMorphRadarSpot> spots)
        {
            try
            {
                Type trackManagerType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.Track.TrackProtocolManager",
                    "TrackProtocolManager");
                Type trackDataType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.Track.TrackData",
                    "TrackData");
                Type trackTypeEnum = this.FindLoadedType(
                    "XDT.Scene.Shared.Modules.Track.TrackType",
                    "TrackType");
                if (trackManagerType == null || trackDataType == null || trackTypeEnum == null)
                {
                    return;
                }

                MethodInfo getAllTracksMethod = trackManagerType.GetMethod("GetAllTracks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getAllTracksMethod == null)
                {
                    return;
                }

                IList trackList = Activator.CreateInstance(typeof(List<>).MakeGenericType(trackDataType)) as IList;
                if (trackList == null)
                {
                    return;
                }

                getAllTracksMethod.Invoke(null, new object[] { trackList });
                FieldInfo trackTypeField = trackDataType.GetField("TrackType");
                FieldInfo positionField = trackDataType.GetField("Position");
                FieldInfo targetNetIdField = trackDataType.GetField("TargetNetId");
                FieldInfo tokenField = trackDataType.GetField("Token");
                if (trackTypeField == null || positionField == null)
                {
                    return;
                }

                object hideAndSeekHiderValue = Enum.Parse(trackTypeEnum, "HideAndSeekHider");
                for (int i = 0; i < trackList.Count; i++)
                {
                    object track = trackList[i];
                    if (track == null)
                    {
                        continue;
                    }

                    object trackTypeValue = trackTypeField.GetValue(track);
                    if (trackTypeValue == null || !trackTypeValue.Equals(hideAndSeekHiderValue))
                    {
                        continue;
                    }

                    Vector3 position = (Vector3)positionField.GetValue(track);
                    uint markerNetId = 0U;
                    if (targetNetIdField != null)
                    {
                        this.TryConvertToUInt(targetNetIdField.GetValue(track), out markerNetId);
                    }

                    if (markerNetId == 0U && tokenField != null)
                    {
                        this.TryConvertToUInt(tokenField.GetValue(track), out markerNetId);
                    }

                    if (markerNetId == 0U || markerNetId == selfNetId)
                    {
                        continue;
                    }

                    if (this.TryGetEntityPositionByNetId(markerNetId, out Vector3 entityPos) && entityPos.sqrMagnitude > 0.01f)
                    {
                        position = entityPos;
                    }

                    this.TryAddHideAndSeekMorphSpot(seen, spots, markerNetId, position);
                }
            }
            catch
            {
            }
        }

        private void TryCollectHideAndSeekMorphFromRemotePlayers(uint selfNetId, HashSet<uint> seen, List<HideAndSeekMorphRadarSpot> spots)
        {
            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                Type remotePlayerType = this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.Component.Player.RemotePlayerComponent",
                    "RemotePlayerComponent");
                if (entitiesType == null || remotePlayerType == null)
                {
                    return;
                }

                MethodInfo getComponentsMethod = entitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getComponentsMethod == null)
                {
                    return;
                }

                Type listType = typeof(List<>).MakeGenericType(remotePlayerType);
                object listInstance = Activator.CreateInstance(listType);
                if (listInstance == null)
                {
                    return;
                }

                object[] args = new object[] { listInstance };
                getComponentsMethod.MakeGenericMethod(remotePlayerType).Invoke(null, args);
                object results = args[0] ?? listInstance;
                if (!(results is IEnumerable enumerable))
                {
                    return;
                }

                foreach (object remotePlayer in enumerable)
                {
                    if (remotePlayer == null)
                    {
                        continue;
                    }

                    object entityObj;
                    if (!(this.TryGetObjectMember(remotePlayer, "entity", out entityObj) || this.TryInvokeZeroArgMember(remotePlayer, out entityObj, "get_entity"))
                        || entityObj == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entityObj, out uint playerNetId, out _, 0) || playerNetId == 0U || playerNetId == selfNetId)
                    {
                        continue;
                    }

                    if (this.TryGetMorphGhostNetIdForPlayer(playerNetId, out uint ghostNetId) && ghostNetId != 0U
                        && this.TryGetEntityPositionByNetId(ghostNetId, out Vector3 ghostPos))
                    {
                        this.TryAddHideAndSeekMorphSpot(seen, spots, ghostNetId, ghostPos);
                    }
                }
            }
            catch
            {
            }
        }

        private void TryCollectHideAndSeekMorphFromHiderPlacedPositions(uint selfNetId, HashSet<uint> seen, List<HideAndSeekMorphRadarSpot> spots)
        {
            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                Type remotePlayerType = this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.Component.Player.RemotePlayerComponent",
                    "RemotePlayerComponent");
                if (entitiesType == null || remotePlayerType == null)
                {
                    return;
                }

                MethodInfo getComponentsMethod = entitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getComponentsMethod == null)
                {
                    return;
                }

                Type listType = typeof(List<>).MakeGenericType(remotePlayerType);
                object listInstance = Activator.CreateInstance(listType);
                if (listInstance == null)
                {
                    return;
                }

                object[] args = new object[] { listInstance };
                getComponentsMethod.MakeGenericMethod(remotePlayerType).Invoke(null, args);
                object results = args[0] ?? listInstance;
                if (!(results is IEnumerable enumerable))
                {
                    return;
                }

                foreach (object remotePlayer in enumerable)
                {
                    if (remotePlayer == null)
                    {
                        continue;
                    }

                    object entityObj;
                    if (!(this.TryGetObjectMember(remotePlayer, "entity", out entityObj) || this.TryInvokeZeroArgMember(remotePlayer, out entityObj, "get_entity"))
                        || entityObj == null)
                    {
                        continue;
                    }

                    if (!this.TryResolveBirdNetIdFromObject(entityObj, out uint playerNetId, out _, 0) || playerNetId == 0U || playerNetId == selfNetId)
                    {
                        continue;
                    }

                    if (this.TryGetHideAndSeekHiderPlacedPosition(playerNetId, out Vector3 placedPos))
                    {
                        uint markerNetId = playerNetId;
                        if (this.TryGetMorphGhostNetIdForPlayer(playerNetId, out uint ghostNetId) && ghostNetId != 0U)
                        {
                            markerNetId = ghostNetId;
                        }

                        this.TryAddHideAndSeekMorphSpot(seen, spots, markerNetId, placedPos);
                    }
                    else if (this.TryGetHideAndSeekHelperTrackPosition(playerNetId, out Vector3 helperPos))
                    {
                        this.TryAddHideAndSeekMorphSpot(seen, spots, playerNetId, helperPos);
                    }
                }
            }
            catch
            {
            }
        }

        private bool TryGetMorphGhostNetIdForPlayer(uint playerNetId, out uint ghostNetId)
        {
            ghostNetId = 0U;
            if (playerNetId == 0U || !this.TryGetEntityObjectByNetId(playerNetId, out object entity) || entity == null)
            {
                return false;
            }

            try
            {
                Type entityType = entity.GetType();
                MethodInfo tryGetMorphMethod = entityType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "TryGet")
                        {
                            return false;
                        }

                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 2 && parameters[1].IsOut;
                    });
                if (tryGetMorphMethod == null || !tryGetMorphMethod.IsGenericMethodDefinition)
                {
                    return false;
                }

                Type morphComponentType = this.FindLoadedType(
                    "XDT.Scene.Shared.Modules.PartyGame.MorphComponent",
                    "MorphComponent");
                if (morphComponentType == null)
                {
                    return false;
                }

                MethodInfo boundTryGet = tryGetMorphMethod.MakeGenericMethod(morphComponentType);
                object[] invokeArgs = new object[] { Activator.CreateInstance(morphComponentType), false };
                object invokeResult = boundTryGet.Invoke(entity, invokeArgs);
                if (!(invokeResult is bool hasMorph) || !hasMorph)
                {
                    return false;
                }

                object morphValue = invokeArgs[0];
                if (morphValue == null)
                {
                    return false;
                }

                object ghostRef;
                if (this.TryGetObjectMember(morphValue, "Ghost", out ghostRef) && ghostRef != null)
                {
                    if (this.TryGetObjectMember(ghostRef, "NetId", out object netIdObj) && this.TryConvertToUInt(netIdObj, out ghostNetId))
                    {
                        return ghostNetId != 0U;
                    }
                }

                FieldInfo ghostField = morphValue.GetType().GetField("Ghost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ghostField != null)
                {
                    object ghostStruct = ghostField.GetValue(morphValue);
                    if (ghostStruct != null && this.TryGetObjectMember(ghostStruct, "NetId", out object netIdObj2) && this.TryConvertToUInt(netIdObj2, out ghostNetId))
                    {
                        return ghostNetId != 0U;
                    }
                }
            }
            catch
            {
            }

            ghostNetId = 0U;
            return false;
        }

        private bool TryGetHideAndSeekHiderPlacedPosition(uint playerNetId, out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService");
                Type hideAndSeekServiceType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.HideAndSeek.IHideAndSeekService",
                    "IHideAndSeekService");
                if (ecsServiceType == null || hideAndSeekServiceType == null)
                {
                    return false;
                }

                MethodInfo getServiceMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                MethodInfo tryGetPlacedMethod = hideAndSeekServiceType.GetMethod(
                    "TryGetHiderPlacedPlayerPosition",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getServiceMethod == null || tryGetPlacedMethod == null)
                {
                    return false;
                }

                object service = getServiceMethod.MakeGenericMethod(hideAndSeekServiceType).Invoke(null, null);
                if (service == null)
                {
                    return false;
                }

                object[] args = new object[] { playerNetId, Vector3.zero };
                object result = tryGetPlacedMethod.Invoke(service, args);
                if (result is bool ok && ok)
                {
                    position = (Vector3)args[1];
                    return position.sqrMagnitude > 0.01f;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetHideAndSeekHelperTrackPosition(uint playerNetId, out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService");
                Type hideAndSeekServiceType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.HideAndSeek.IHideAndSeekService",
                    "IHideAndSeekService");
                if (ecsServiceType == null || hideAndSeekServiceType == null)
                {
                    return false;
                }

                MethodInfo getServiceMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                MethodInfo helperMethod = hideAndSeekServiceType.GetMethod(
                    "GetHelperTrackPosition",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getServiceMethod == null || helperMethod == null)
                {
                    return false;
                }

                object service = getServiceMethod.MakeGenericMethod(hideAndSeekServiceType).Invoke(null, null);
                if (service == null)
                {
                    return false;
                }

                object[] args = new object[] { playerNetId, Vector3.zero };
                object result = helperMethod.Invoke(service, args);
                if (result is bool ok && ok)
                {
                    position = (Vector3)args[1];
                    return position.sqrMagnitude > 0.01f;
                }
            }
            catch
            {
            }

            return false;
        }

        private struct HideAndSeekMorphRadarSpot
        {
            public uint MarkerNetId;
            public Vector3 Position;
        }

    }
}
