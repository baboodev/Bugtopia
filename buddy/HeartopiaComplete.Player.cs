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
        private bool TryGetManagedSelfPlayerObject(out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";

            try
            {
                Type entityUtilType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "EntityUtil");
                if (entityUtilType != null)
                {
                    MethodInfo getSelfPlayerMethod = entityUtilType.GetMethod("GetSelfPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (getSelfPlayerMethod != null)
                    {
                        playerObj = getSelfPlayerMethod.Invoke(null, null);
                        if (playerObj != null)
                        {
                            source = "EntityUtil.GetSelfPlayer()";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                Type characterType = this.FindLoadedType("XDTLevelAndEntity.Game.GameMode.Character", "Character");
                if (characterType != null)
                {
                    PropertyInfo characterProperty = characterType.GetProperty("character", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object characterObj = characterProperty != null ? characterProperty.GetValue(null, null) : null;
                    if (characterObj != null)
                    {
                        if (this.TryGetObjectMember(characterObj, "player", out playerObj) && playerObj != null)
                        {
                            source = "Character.character.player";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (this.TryGetManagedViewModuleSelfPlayerObject(out playerObj, out source))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryGetManagedInteractPlayerObject(object interactSystemObj, out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";
            if (interactSystemObj == null)
            {
                return false;
            }

            foreach (string memberName in new string[] { "player", "_interactor", "interactor" })
            {
                if (this.TryGetObjectMember(interactSystemObj, memberName, out playerObj) && playerObj != null)
                {
                    source = interactSystemObj.GetType().Name + "." + memberName;
                    return true;
                }
            }

            return false;
        }

        public GameObject GetPlayerObject()
        {
            return GetPlayer();
        }

        private bool TryGetManagedViewModuleSelfPlayerObject(out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";

            try
            {
                Type entityManagerType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntityManager",
                    "ScriptsRefactory.LevelAndEntity.BaseSystem.EntityManager",
                    "Il2CppXDTLevelAndEntity.BaseSystem.EntityManager",
                    "EntityManager");
                if (entityManagerType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = entityManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object entityManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (entityManager == null)
                {
                    return false;
                }

                if (this.TryGetObjectMember(entityManager, "selfPlayer", out object selfPlayerObj) && selfPlayerObj != null)
                {
                    playerObj = selfPlayerObj;
                    source = "EntityManager.Instance.selfPlayer";
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsLocalPlayerSkeletonGameObject(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            string name = obj.name;
            if (string.IsNullOrEmpty(name) || !name.Contains("p_player_skeleton"))
            {
                return false;
            }

            GameObject localPlayer = GetLocalPlayer();
            if (localPlayer == null)
            {
                return false;
            }

            if (ReferenceEquals(obj, localPlayer))
            {
                return true;
            }

            if (obj.GetInstanceID() == localPlayer.GetInstanceID())
            {
                return true;
            }

            try
            {
                if (obj.transform != null && localPlayer.transform != null
                    && obj.transform.GetInstanceID() == localPlayer.transform.GetInstanceID())
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if ((obj.transform.position - localPlayer.transform.position).sqrMagnitude < 0.04f)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsOtherPlayerSkeletonGameObject(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy)
            {
                return false;
            }

            if (this.IsLocalPlayerSkeletonGameObject(obj))
            {
                return false;
            }

            string name = obj.name;
            return !string.IsNullOrEmpty(name) && name.Contains("p_player_skeleton");
        }

        private bool TryGetLocalPlayerPosition(out Vector3 playerPos)
        {
            playerPos = Vector3.zero;
            try
            {
                GameObject player = this.FindPlayerRoot();
                if (player != null)
                {
                    playerPos = player.transform.position;
                    return true;
                }
            }
            catch
            {
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                playerPos = cam.transform.position;
                return true;
            }

            return false;
        }

        private float GetNearestPlayerDistance()
        {
            if (Time.unscaledTime < this.nextNearestPlayerDistanceRefreshAt)
            {
                return this.cachedNearestPlayerDistance;
            }

            if (this.cachedPlayerObject == null || !this.cachedPlayerObject.activeInHierarchy)
            {
                this.cachedPlayerObject = GameObject.Find("p_player_skeleton(Clone)");
                if (this.cachedPlayerObject == null)
                {
                    this.cachedNearestPlayerDistance = 999f;
                    this.nextNearestPlayerDistanceRefreshAt = Time.unscaledTime + 1.5f;
                    return this.cachedNearestPlayerDistance;
                }
            }

            Vector3 myPosition = this.cachedPlayerObject.transform.position;
            float nearest = 999f;

            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj == null) continue;

                // Find other player skeletons (not our own)
                if (obj.name.Contains("p_player_skeleton") && obj != this.cachedPlayerObject)
                {
                    float distance = Vector3.Distance(myPosition, obj.transform.position);
                    if (distance < nearest)
                    {
                        nearest = distance;
                    }
                }
            }

            this.cachedNearestPlayerDistance = nearest;
            this.nextNearestPlayerDistanceRefreshAt = Time.unscaledTime + 1.5f;
            return this.cachedNearestPlayerDistance;
        }

        // Token: 0x06000022 RID: 34 RVA: 0x00006C04 File Offset: 0x00004E04
        private void SetHomePosition()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                this.homePosition = gameObject.transform.position;
                this.homePositionSet = true;
                this.autoHomeStatus = "Manual home saved";
                ModLogger.Msg($"[HOME] Home position set to: {this.homePosition}");
            }
        }

        private void RefreshAutoHomePosition(bool force = false)
        {
            float unscaledTime = Time.unscaledTime;
            if (!force && this.autoHomePositionValid)
            {
                return;
            }

            if (!force && unscaledTime < this.autoHomeResolveNextAt)
            {
                return;
            }
            this.autoHomeResolveNextAt = unscaledTime + AutoHomeResolveRetryInterval;
            Vector3 vector;
            uint num;
            string text;
            if (this.TryResolveCurrentHomePosition(out vector, out num, out text))
            {
                this.autoHomePosition = vector;
                this.autoHomeNetId = num;
                this.autoHomePositionValid = true;
                this.autoHomeResolveNextAt = float.PositiveInfinity;
                this.autoHomeStatus = "Home Ready";
            }
            else
            {
                this.autoHomePositionValid = false;
                this.autoHomeNetId = 0U;
                this.autoHomeResolveNextAt = unscaledTime + AutoHomeResolveRetryInterval;
                this.autoHomeStatus = text;
            }
        }

        private bool TryResolveCurrentHomePosition(out Vector3 position, out uint homeNetId, out string status)
        {
            position = Vector3.zero;
            homeNetId = 0U;
            status = "Auto home unavailable";
            string monoFailureStatus = string.Empty;
            uint selfPlayerNetId = 0U;
            bool selfResolved = this.TryResolveSelfPlayerNetId(out selfPlayerNetId);
            if (selfResolved && selfPlayerNetId != 0U)
            {
                object ownerField = this.TryResolveFieldByOwnerId(selfPlayerNetId);
                if (ownerField == null)
                {
                    status = $"Auto home: owner path null [self={selfPlayerNetId}]";
                }
                else
                {
                    status = $"Auto home: owner path found [{ownerField.GetType().Name}]";
                }
                if (this.TryExtractHomePosition(ownerField, out position))
                {
                    uint resolvedHomeNetId;
                    if (this.TryGetUIntMember(ownerField, "homeNetId", out resolvedHomeNetId))
                    {
                        homeNetId = resolvedHomeNetId;
                    }
                    status = "Home Ready";
                    return true;
                }
                if (ownerField != null)
                {
                    status = $"Auto home: owner extract failed [{ownerField.GetType().Name}]";
                }
                if (this.TryResolveHomePositionMono(selfPlayerNetId, out position, out uint monoOwnerHomeNetId, out string monoOwnerStatus))
                {
                    if (monoOwnerHomeNetId != 0U)
                    {
                        homeNetId = monoOwnerHomeNetId;
                    }
                    else
                    {
                        this.TryResolveCurrentHomeNetIdMono(out homeNetId);
                    }
                    status = "Home Ready";
                    return true;
                }
                status = monoOwnerStatus;
                monoFailureStatus = monoOwnerStatus;
            }
            else
            {
                status = "Auto home: self player id unavailable";
            }
            if (!this.TryResolveCurrentHomeNetIdMono(out homeNetId))
            {
                Type type = this.FindLoadedType("XDTDataAndProtocol.PlayerDataCenter", "PlayerDataCenter");
                if (type == null)
                {
                    status = "Auto home: PlayerDataCenter not found";
                    return false;
                }
                FieldInfo field = type.GetField("homeNetId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                {
                    status = "Auto home: homeNetId not found";
                    return false;
                }
                object value = field.GetValue(null);
                if (!(value is uint))
                {
                    status = "Auto home: invalid homeNetId";
                    return false;
                }
                homeNetId = (uint)value;
            }
            if (homeNetId == 0U)
            {
                status = "Auto home: homeNetId=0";
                return false;
            }
            if (this.TryResolveHomePositionMono(0U, out position, out uint monoHomeNetId, out string monoHomeStatus))
            {
                if (monoHomeNetId != 0U)
                {
                    homeNetId = monoHomeNetId;
                }
                status = "Home Ready";
                return true;
            }
            monoFailureStatus = monoHomeStatus;
            Type type2 = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
            if (type2 != null)
            {
                object obj = null;
                PropertyInfo property = type2.GetProperty("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null)
                {
                    obj = property.GetValue(null, null);
                }
                else
                {
                    FieldInfo field2 = type2.GetField("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field2 != null)
                    {
                        obj = field2.GetValue(null);
                    }
                }
                if (obj != null)
                {
                    MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo m)
                    {
                        if (m.Name != "GetField")
                        {
                            return false;
                        }
                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(uint);
                    });
                    if (methodInfo != null)
                    {
                        object obj2 = methodInfo.Invoke(obj, new object[]
                        {
                            homeNetId
                        });
                        if (obj2 != null)
                        {
                            status = $"Auto home: GetField found [{obj2.GetType().Name}]";
                        }
                        else
                        {
                            status = $"Auto home: GetField null [{homeNetId}]";
                        }
                        if (this.TryExtractHomePosition(obj2, out position))
                        {
                            status = "Home Ready";
                            return true;
                        }
                        if (obj2 != null)
                        {
                            status = $"Auto home: GetField extract failed [{obj2.GetType().Name}]";
                        }
                    }
                    else
                    {
                        status = "Auto home: fieldSystem.GetField missing";
                    }
                }
                else
                {
                    status = "Auto home: fieldSystem unavailable";
                }
            }
            else
            {
                status = "Auto home: Entities type unavailable";
            }
            Type type3 = this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.CraftingSystem.FieldComponent", "FieldComponent");
            if (type3 != null)
            {
                Il2CppType il2CppType = Il2CppType.GetType(type3.AssemblyQualifiedName);
                if (il2CppType != null)
                {
                    UnityObject[] array = Resources.FindObjectsOfTypeAll(il2CppType);
                    foreach (UnityObject unityObject in array)
                    {
                        if (!(unityObject == null))
                        {
                            object obj3 = unityObject;
                            uint num;
                            if (this.TryGetUIntMember(obj3, "homeNetId", out num) && num == homeNetId && this.TryExtractHomePosition(obj3, out position))
                            {
                                status = "Home Ready";
                                return true;
                            }
                            if (this.TryGetUIntMember(obj3, "homeNetId", out num) && num == homeNetId)
                            {
                                status = $"Auto home: il2cpp match extract failed [{obj3.GetType().Name}]";
                            }
                        }
                    }
                }
                else
                {
                    status = "Auto home: FieldComponent il2cpp type unavailable";
                }
            }
            else
            {
                status = "Auto home: FieldComponent type unavailable";
            }
            if (status == "Auto home unavailable" || status == "Auto home: self player id unavailable")
            {
                status = $"Auto home: field {homeNetId} not found";
            }
            if (!string.IsNullOrEmpty(monoFailureStatus) && (status == "Auto home: Entities type unavailable" || status == "Auto home: FieldComponent type unavailable" || status == "Auto home: FieldComponent il2cpp type unavailable"))
            {
                status = monoFailureStatus;
            }
            return false;
        }

        private unsafe bool TryResolveHomePositionMono(uint ownerId, out Vector3 position, out uint resolvedHomeNetId, out string status)
        {
            position = Vector3.zero;
            resolvedHomeNetId = 0U;
            status = "Auto home: mono resolve unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldGetValueObject == null)
            {
                return false;
            }
            IntPtr levelImage = this.FindAuraMonoImage(new string[]
            {
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll"
            });
            IntPtr homelandClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTLevelAndEntity.GameplaySystem.HomeLand", "HomelandEntitySystem") : IntPtr.Zero;
            if (homelandClass == IntPtr.Zero)
            {
                homelandClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.GameplaySystem.HomeLand", "HomelandEntitySystem");
            }
            if (homelandClass == IntPtr.Zero)
            {
                status = "Auto home: HomelandEntitySystem mono missing";
                return false;
            }
            IntPtr homelandObj = IntPtr.Zero;
            if (ownerId != 0U)
            {
                IntPtr getPlayerField = auraMonoClassGetMethodFromName(homelandClass, "GetPlayerField", 1);
                if (getPlayerField != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    uint ownerArg = ownerId;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&ownerArg);
                    homelandObj = auraMonoRuntimeInvoke(getPlayerField, IntPtr.Zero, (IntPtr)args, ref exc);
                }
            }
            if (homelandObj == IntPtr.Zero)
            {
                IntPtr getSelfField = auraMonoClassGetMethodFromName(homelandClass, "GetSelfField", 0);
                if (getSelfField != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    homelandObj = auraMonoRuntimeInvoke(getSelfField, IntPtr.Zero, IntPtr.Zero, ref exc);
                }
            }
            if (homelandObj == IntPtr.Zero)
            {
                status = ownerId != 0U ? $"Auto home: mono owner field null [self={ownerId}]" : "Auto home: mono self field null";
                return false;
            }
            if (!this.TryGetMonoUInt32FromObjectMember(homelandObj, "entity", "netId", out resolvedHomeNetId))
            {
                this.TryGetMonoUInt32Member(homelandObj, "homeNetId", out resolvedHomeNetId);
            }
            if (this.TryExtractHomePositionMonoObject(homelandObj, out position))
            {
                return true;
            }
            status = "Auto home: mono extract failed";
            return false;
        }

        private bool TryExtractHomePositionMonoObject(IntPtr obj, out Vector3 position)
        {
            position = Vector3.zero;
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldGetValueObject == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            IntPtr fieldComponentObj;
            if (this.TryGetMonoObjectMember(obj, "fieldComponent", out fieldComponentObj) || this.TryGetMonoObjectMember(obj, "_fieldComponent", out fieldComponentObj))
            {
                if (this.TryExtractHomePositionMonoObject(fieldComponentObj, out position))
                {
                    return true;
                }
            }
            if (this.TryGetMonoVector3Member(obj, "position", out position))
            {
                return true;
            }
            if (this.TryGetMonoBoundsCenterMember(obj, "Bounds", out position) || this.TryGetMonoBoundsCenterMember(obj, "LocalBounds", out position))
            {
                return true;
            }
            IntPtr entityObj;
            if (this.TryGetMonoObjectMember(obj, "entity", out entityObj) || this.TryGetMonoObjectMember(obj, "_entity", out entityObj))
            {
                if (this.TryExtractHomePositionMonoObject(entityObj, out position))
                {
                    return true;
                }
            }
            IntPtr transformObj;
            if (this.TryGetMonoObjectMember(obj, "transform", out transformObj) || this.TryGetMonoObjectMember(obj, "_transform", out transformObj))
            {
                if (this.TryExtractHomePositionMonoObject(transformObj, out position))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryResolveSelfPlayerNetId(out uint selfPlayerNetId)
        {
            selfPlayerNetId = 0U;
            if (this.TryResolveSelfPlayerNetIdMono(out selfPlayerNetId))
            {
                return selfPlayerNetId != 0U;
            }
            Type type = this.FindLoadedType("XDTDataAndProtocol.PlayerDataCenter", "PlayerDataCenter");
            if (type == null)
            {
                return false;
            }
            MethodInfo method = type.GetMethod("GetSelfNetPlayerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                return false;
            }
            try
            {
                object value = method.Invoke(null, null);
                if (value is uint)
                {
                    selfPlayerNetId = (uint)value;
                    return selfPlayerNetId != 0U;
                }
                selfPlayerNetId = Convert.ToUInt32(value);
                return selfPlayerNetId != 0U;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryResolveSelfPlayerNetIdMono(out uint selfPlayerNetId)
        {
            selfPlayerNetId = 0U;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr image = this.FindAuraMonoImage(new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll"
            });
            IntPtr classPtr = image != IntPtr.Zero ? auraMonoClassFromName(image, "XDTDataAndProtocol", "PlayerDataCenter") : IntPtr.Zero;
            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol", "PlayerDataCenter");
            }
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr methodPtr = auraMonoClassGetMethodFromName(classPtr, "GetSelfNetPlayerId", 0);
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            selfPlayerNetId = *(uint*)raw;
            return selfPlayerNetId != 0U;
        }

        private unsafe bool TryResolveCurrentHomeNetIdMono(out uint homeNetId)
        {
            homeNetId = 0U;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetFieldFromName == null || auraMonoClassVtable == null || auraMonoFieldStaticGetValue == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }
            IntPtr image = this.FindAuraMonoImage(new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll"
            });
            IntPtr classPtr = image != IntPtr.Zero ? auraMonoClassFromName(image, "XDTDataAndProtocol", "PlayerDataCenter") : IntPtr.Zero;
            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol", "PlayerDataCenter");
            }
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr fieldPtr = auraMonoClassGetFieldFromName(classPtr, "homeNetId");
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, classPtr);
            if (vtable == IntPtr.Zero)
            {
                return false;
            }
            uint value = 0U;
            auraMonoFieldStaticGetValue(vtable, fieldPtr, (IntPtr)(&value));
            homeNetId = value;
            return homeNetId != 0U;
        }

        private bool TryExtractHomePosition(object fieldComponent, out Vector3 position)
        {
            position = Vector3.zero;
            if (fieldComponent == null)
            {
                return false;
            }
            object homelandFieldComponent;
            if (this.TryGetObjectMember(fieldComponent, "fieldComponent", out homelandFieldComponent) && homelandFieldComponent != null)
            {
                if (this.TryExtractHomePosition(homelandFieldComponent, out position))
                {
                    return true;
                }
            }
            object obj;
            if (this.TryGetObjectMember(fieldComponent, "Bounds", out obj) && obj is Bounds)
            {
                Bounds bounds = (Bounds)obj;
                if (bounds.size.sqrMagnitude > 0.001f)
                {
                    position = bounds.center;
                    return true;
                }
            }
            Component component = fieldComponent as Component;
            if (component != null)
            {
                position = component.transform.position;
                return true;
            }
            object obj2;
            if (this.TryGetObjectMember(fieldComponent, "entity", out obj2))
            {
                object obj3;
                if (this.TryGetObjectMember(obj2, "position", out obj3) && obj3 is Vector3)
                {
                    position = (Vector3)obj3;
                    return true;
                }
                object obj4;
                if (this.TryGetObjectMember(obj2, "transform", out obj4) && this.TryExtractHomePosition(obj4, out position))
                {
                    return true;
                }
            }
            return false;
        }

        internal bool ModTryGetManagedSelfPlayerObject(out object playerObj, out string source) =>
            this.TryGetManagedSelfPlayerObject(out playerObj, out source);

        // Token: 0x06000026 RID: 38 RVA: 0x00006E94 File Offset: 0x00005094
        private void InspectPlayerComponents()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                ModLogger.Msg("=== PLAYER COMPONENTS (Il2Cpp) ===");
                Component[] array = gameObject.GetComponents<Component>();
                foreach (Component component in array)
                {
                    bool flag2 = component == null;
                    if (!flag2)
                    {
                        try
                        {
                            string fullName = component.GetType().FullName;
                            ModLogger.Msg("GetType().FullName: " + fullName);
                            Il2CppType il2CppType = component.GetIl2CppType();
                            bool flag3 = il2CppType != null;
                            if (flag3)
                            {
                                ModLogger.Msg("Il2CppType.Name: " + il2CppType.Name);
                                ModLogger.Msg("Il2CppType.FullName: " + il2CppType.FullName);
                            }
                            Type baseType = component.GetType().BaseType;
                            bool flag4 = baseType != null;
                            if (flag4)
                            {
                                ModLogger.Msg("BaseType: " + baseType.FullName);
                            }
                            ModLogger.Msg("comp.name: " + component.name);
                            ModLogger.Msg("---");
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Msg("Error inspecting component: " + ex.Message);
                        }
                    }
                }
                ModLogger.Msg("=== END ===");
            }
        }

        public static GameObject GetLocalPlayer()
        {
            // Quick return if cached and valid
            try
            {
                if (cachedLocalPlayer != null && cachedLocalPlayer.activeInHierarchy)
                {
                    return cachedLocalPlayer;
                }
            }
            catch
            {
                cachedLocalPlayer = null;
            }

            // Throttle re-resolves to once per interval REGARDLESS of cache state, so a missing player
            // (world loading, between worlds, despawned) doesn't hit GameObject.Find every call from the
            // hot Transform.position / CharacterController.Move patches. A miss returns the (null/stale)
            // cache and retries at most once per second.
            if (Time.unscaledTime - lastLocalPlayerCheckTime < LOCAL_PLAYER_CACHE_INTERVAL)
            {
                return cachedLocalPlayer;
            }

            lastLocalPlayerCheckTime = Time.unscaledTime;
            cachedLocalPlayer = GameObject.Find("p_player_skeleton(Clone)");
            return cachedLocalPlayer;
        }

        private GameObject GetPlayer() => GetLocalPlayer();

        private GameObject FindPlayerRoot()
        {
            try
            {
                GameObject p = GetPlayer();
                if (p == null) return null;
                if (p.transform == null) return p;
                Transform root = p.transform.root;
                if (root != null && root.gameObject != null) return root.gameObject;
                return p;
            }
            catch
            {
                return GetPlayer();
            }
        }

        // Token: 0x06000027 RID: 39 RVA: 0x00007014 File Offset: 0x00005214
        private void InspectMovementComponent()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                Component[] array = gameObject.GetComponents<Component>();
                Component component = null;
                foreach (Component component2 in array)
                {
                    bool flag2 = component2 == null;
                    if (!flag2)
                    {
                        Il2CppType il2CppType = component2.GetIl2CppType();
                        bool flag3 = il2CppType != null && il2CppType.Name == "DynamicMonoBehaviour";
                        if (flag3)
                        {
                            component = component2;
                            break;
                        }
                    }
                }
                bool flag4 = component == null;
                if (flag4)
                {
                    ModLogger.Msg("DynamicMonoBehaviour not found!");
                }
                else
                {
                    ModLogger.Msg($"=== DynamicMonoBehaviour INSPECTION ===");
                    FieldInfo[] fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ModLogger.Msg($"Total fields: {fields.Length}");
                    foreach (FieldInfo fieldInfo in fields)
                    {
                        try
                        {
                            object value = fieldInfo.GetValue(component);
                            ModLogger.Msg($"Field: {fieldInfo.Name} = {value} ({fieldInfo.FieldType.Name})");
                        }
                        catch
                        {
                            ModLogger.Msg("Field: " + fieldInfo.Name + " (couldn't read value)");
                        }
                    }
                    MethodInfo[] methods = component.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ModLogger.Msg($"\nTotal methods: {methods.Length}");
                    foreach (MethodInfo methodInfo in methods)
                    {
                        string value2 = string.Join(", ", from p in methodInfo.GetParameters()
                                                          select p.ParameterType.Name);
                        ModLogger.Msg($"Method: {methodInfo.ReturnType.Name} {methodInfo.Name}({value2})");
                    }
                    ModLogger.Msg("=== END ===");
                }
            }
        }

    }
}
