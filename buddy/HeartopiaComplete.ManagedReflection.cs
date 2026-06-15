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
        private bool TryDescribeDynamicMonoBehaviour(Component component, out string description)
        {
            description = null;
            if (component == null)
            {
                return false;
            }

            try
            {
                Type wrapperType = component.GetType();
                string wrapperName = wrapperType.FullName ?? wrapperType.Name ?? "<unknown>";
                if (wrapperName.IndexOf("DynamicMonoBehaviour", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Il2CppObject obj = component.TryCast<Il2CppObject>();
                    string ilName = obj?.GetIl2CppType()?.FullName?.ToString();
                    if (string.IsNullOrEmpty(ilName) || ilName.IndexOf("DynamicMonoBehaviour", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                    wrapperName = ilName;
                }

                string behaviourType = null;
                MethodInfo getBehaviourTypeMethod = wrapperType.GetMethod("GetBehaviourType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getBehaviourTypeMethod != null)
                {
                    try
                    {
                        behaviourType = getBehaviourTypeMethod.Invoke(component, null)?.ToString();
                    }
                    catch
                    {
                    }
                }

                object implObject = null;
                string implTypeName = null;
                PropertyInfo implProperty = wrapperType.GetProperty("Impl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (implProperty != null)
                {
                    try
                    {
                        implObject = implProperty.GetValue(component, null);
                    }
                    catch
                    {
                    }
                }

                if (implObject == null)
                {
                    FieldInfo implInternalField = this.FindFieldInHierarchy(wrapperType, "ImplInternal");
                    if (implInternalField != null)
                    {
                        try
                        {
                            implObject = implInternalField.GetValue(component);
                        }
                        catch
                        {
                        }
                    }
                }

                if (implObject != null)
                {
                    Type implType = implObject.GetType();
                    bool hasRatio = this.FindFieldInHierarchy(implType, "_durabilityRatio") != null;
                    bool hasNode = this.FindFieldInHierarchy(implType, "durabilityNode") != null;
                    implTypeName = (implType.FullName ?? implType.Name ?? "<unknown>") + $"[ratio={hasRatio},node={hasNode}]";
                }
                else
                {
                    implTypeName = "impl=<null>";
                }

                description = string.IsNullOrEmpty(behaviourType)
                    ? wrapperName + "->" + implTypeName
                    : wrapperName + "[" + behaviourType + "]->" + implTypeName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            List<string> parts = new List<string>();
            Transform current = transform;
            int depth = 0;
            while (current != null && depth < 32)
            {
                parts.Add(current.name);
                current = current.parent;
                depth++;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private bool TryResolveNetIdFromManagedObject(object obj, out uint netId, out string source, int depth = 0)
        {
            netId = 0U;
            source = "none";
            if (obj == null || depth > 3)
            {
                return false;
            }

            foreach (string memberName in new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id", "Item1" })
            {
                if (this.TryGetUIntMember(obj, memberName, out netId) && netId != 0U)
                {
                    source = obj.GetType().Name + "." + memberName;
                    return true;
                }
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity", "target", "Target", "Item1" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object nestedObj) && nestedObj != null)
                {
                    if (this.TryResolveNetIdFromManagedObject(nestedObj, out netId, out string nestedSource, depth + 1))
                    {
                        source = obj.GetType().Name + "." + memberName + "->" + nestedSource;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryResolvePositionFromManagedObject(object obj, out Vector3 position, int depth = 0)
        {
            position = Vector3.zero;
            if (obj == null || depth > 3)
            {
                return false;
            }

            foreach (string memberName in new string[] { "position", "pos", "Position", "Item2" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object rawValue) && rawValue is Vector3 vector)
                {
                    position = vector;
                    return true;
                }
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity", "transform", "_transform" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object nestedObj) && nestedObj != null)
                {
                    if (nestedObj is Transform transform)
                    {
                        position = transform.position;
                        return true;
                    }

                    if (this.TryResolvePositionFromManagedObject(nestedObj, out position, depth + 1))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryResolveNetIdFromGameObject(GameObject obj, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            try
            {
                foreach (Component comp in obj.GetComponents<Component>())
                {
                    if (comp == null)
                    {
                        continue;
                    }

                    if (this.TryResolveNetIdFromComponent(comp, out netId, out source))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromGameObject error on " + obj.name + ": " + ex.Message);
            }

            return false;
        }

        private bool TryResolveNetIdFromComponent(Component comp, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (comp == null)
            {
                return false;
            }

            try
            {
                var ilType = comp.GetIl2CppType();
                if (ilType == null)
                {
                    return false;
                }

                string[] directMembers = new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id" };
                foreach (string member in directMembers)
                {
                    if (this.TryReadUIntMember(ilType, comp.TryCast<Il2CppObject>(), member, out netId))
                    {
                        source = ilType.Name + "." + member;
                        return true;
                    }
                }

                string[] entityMembers = new string[] { "entity", "Entity", "_entity", "ownerEntity", "targetEntity" };
                foreach (string member in entityMembers)
                {
                    if (this.TryReadObjectMember(ilType, comp.TryCast<Il2CppObject>(), member, out Il2CppObject nestedObj) && nestedObj != null)
                    {
                        if (this.TryResolveNetIdFromIl2CppObject(nestedObj, out netId, out string nestedSource))
                        {
                            source = ilType.Name + "." + member + "->" + nestedSource;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromComponent error: " + ex.Message);
            }

            return false;
        }

        private bool TryResolveNetIdFromIl2CppObject(Il2CppObject obj, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            try
            {
                var ilType = obj.GetIl2CppType();
                if (ilType == null)
                {
                    return false;
                }

                string[] members = new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id", "Item1" };
                foreach (string member in members)
                {
                    if (this.TryReadUIntMember(ilType, obj, member, out netId))
                    {
                        source = ilType.Name + "." + member;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromIl2CppObject error: " + ex.Message);
            }

            return false;
        }

        private bool TryConvertToUInt(object rawValue, out uint value)
        {
            value = 0U;
            if (rawValue == null)
            {
                return false;
            }

            try
            {
                if (rawValue is uint uintValue)
                {
                    value = uintValue;
                    return value != 0U;
                }
                if (rawValue is int intValue)
                {
                    if (intValue > 0)
                    {
                        value = (uint)intValue;
                        return true;
                    }
                    return false;
                }

                string s = rawValue.ToString();
                if (string.IsNullOrEmpty(s))
                {
                    return false;
                }

                s = new string(s.Where(char.IsDigit).ToArray());
                if (uint.TryParse(s, out value))
                {
                    return value != 0U;
                }
            }
            catch { }

            return false;
        }

        private bool TryConvertToInt(object rawValue, out int value)
        {
            value = 0;
            if (rawValue == null)
            {
                return false;
            }

            try
            {
                if (rawValue is int intValue)
                {
                    value = intValue;
                    return value != 0;
                }
                if (rawValue is uint uintValue && uintValue <= int.MaxValue)
                {
                    value = (int)uintValue;
                    return value != 0;
                }

                string s = rawValue.ToString();
                if (string.IsNullOrEmpty(s))
                {
                    return false;
                }

                s = new string(s.Where(c => char.IsDigit(c) || c == '-').ToArray());
                if (int.TryParse(s, out value))
                {
                    return value != 0;
                }
            }
            catch { }

            return false;
        }

        private IEnumerable<Type> EnumerateLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                yield break;
            }

            Type[] types = null;
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
                yield break;
            }

            if (types == null)
            {
                yield break;
            }

            foreach (Type type in types)
            {
                if (type != null)
                {
                    yield return type;
                }
            }
        }

        private bool TryReadObjectInt(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                if (this.TryGetObjectMember(instance, memberName, out object raw) && this.TryConvertToInt(raw, out value))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private object TryInvokeManagedGetComponent(object entityObj, Type componentType)
        {
            if (entityObj == null || componentType == null)
            {
                return null;
            }

            try
            {
                foreach (MethodInfo method in entityObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method == null || method.Name != "GetComponent" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    if (method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    return method.MakeGenericMethod(componentType).Invoke(entityObj, null);
                }
            }
            catch
            {
            }

            return null;
        }

        private string TryReadObjectString(object obj, string memberName)
        {
            object valueObj;
            if (obj != null && !string.IsNullOrWhiteSpace(memberName) && this.TryGetObjectMember(obj, memberName, out valueObj) && valueObj is string)
            {
                return ((string)valueObj).Trim();
            }

            return string.Empty;
        }

        private bool TryInvokeMethodByName(object target, string methodName, out object result, object[] args)
        {
            result = null;
            if (target == null || string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            try
            {
                MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if ((args == null ? 0 : args.Length) != parameters.Length)
                    {
                        continue;
                    }

                    result = method.Invoke(target, args);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private object CreateCompatibleUIntList(Type listType, List<uint> values)
        {
            values = values ?? new List<uint>(0);
            if (listType == null || listType.IsAssignableFrom(typeof(List<uint>)))
            {
                return new List<uint>(values);
            }

            object listObj = Activator.CreateInstance(listType);
            MethodInfo addMethod = listType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(uint) }, null);
            if (addMethod == null)
            {
                return listObj;
            }

            for (int i = 0; i < values.Count; i++)
            {
                addMethod.Invoke(listObj, new object[] { values[i] });
            }

            return listObj;
        }

        private PropertyInfo GetDataModuleInstanceProperty(Type moduleType)
        {
            if (moduleType == null || !moduleType.IsClass)
            {
                return null;
            }

            try
            {
                if (this.cachedDataModuleOpenGenericType == null)
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

                        foreach (Type type in types)
                        {
                            if (type == null || !type.IsGenericTypeDefinition || type.Name != "DataModule`1")
                            {
                                continue;
                            }

                            PropertyInfo instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceProperty != null)
                            {
                                this.cachedDataModuleOpenGenericType = type;
                                break;
                            }
                        }

                        if (this.cachedDataModuleOpenGenericType != null)
                        {
                            break;
                        }
                    }
                }

                if (this.cachedDataModuleOpenGenericType == null)
                {
                    return null;
                }

                Type closedDataModuleType = this.cachedDataModuleOpenGenericType.MakeGenericType(moduleType);
                return closedDataModuleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch
            {
                return null;
            }
        }

        private bool TryEnumerateManagedCollectionItems(object collectionObj, List<object> items)
        {
            if (collectionObj == null || items == null)
            {
                return false;
            }

            items.Clear();

            if (collectionObj is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    items.Add(item);
                }

                return true;
            }

            try
            {
                Type collectionType = collectionObj.GetType();
                MethodInfo getCountMethod = collectionType.GetMethod("get_Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getItemMethod = collectionType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getCountMethod == null || getItemMethod == null)
                {
                    return false;
                }

                int count = Convert.ToInt32(getCountMethod.Invoke(collectionObj, null));
                for (int i = 0; i < count; i++)
                {
                    items.Add(getItemMethod.Invoke(collectionObj, new object[] { i }));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryConvertObjectToIntPtr(object value, out IntPtr pointer)
        {
            pointer = IntPtr.Zero;
            if (value == null)
            {
                return false;
            }

            if (value is IntPtr intPtr)
            {
                pointer = intPtr;
                return pointer != IntPtr.Zero;
            }

            try
            {
                if (value is long longValue)
                {
                    pointer = new IntPtr(longValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is ulong ulongValue && ulongValue <= long.MaxValue)
                {
                    pointer = new IntPtr((long)ulongValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is int intValue)
                {
                    pointer = new IntPtr(intValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is uint uintValue)
                {
                    pointer = new IntPtr((long)uintValue);
                    return pointer != IntPtr.Zero;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryInvokeManagedNetIdMethod(object obj, string methodName, out uint value)
        {
            value = 0U;
            object raw = this.TryInvokeManagedObjectMethod(obj, methodName);
            return this.TryConvertManagedNetIdToUInt32(raw, out value);
        }

        private object TryInvokeManagedObjectMethod(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            try
            {
                MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                return method?.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private bool TryConvertManagedNetIdToUInt32(object raw, out uint value)
        {
            value = 0U;
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt32(raw);
                return value != 0U;
            }
            catch
            {
            }

            string[] valueMemberNames = { "value", "Value", "id", "Id", "_value", "m_Value" };
            for (int i = 0; i < valueMemberNames.Length; i++)
            {
                object innerValue = this.TryGetManagedMemberValue(raw, valueMemberNames[i]);
                if (innerValue == null || ReferenceEquals(innerValue, raw))
                {
                    continue;
                }

                try
                {
                    value = Convert.ToUInt32(innerValue);
                    return value != 0U;
                }
                catch
                {
                }
            }

            string text = raw.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                string digits = new string(text.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits) && uint.TryParse(digits, out value))
                {
                    return value != 0U;
                }
            }

            return false;
        }

        private bool TryConvertManagedNetIdToUInt64(object raw, out ulong value)
        {
            value = 0UL;
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt64(raw);
                return value != 0UL;
            }
            catch
            {
            }

            string[] valueMemberNames = { "value", "Value", "id", "Id", "_value", "m_Value" };
            for (int i = 0; i < valueMemberNames.Length; i++)
            {
                object innerValue = this.TryGetManagedMemberValue(raw, valueMemberNames[i]);
                if (innerValue == null || ReferenceEquals(innerValue, raw))
                {
                    continue;
                }

                try
                {
                    value = Convert.ToUInt64(innerValue);
                    return value != 0UL;
                }
                catch
                {
                }
            }

            string text = raw.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                string digits = new string(text.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits) && ulong.TryParse(digits, out value))
                {
                    return value != 0UL;
                }
            }

            return false;
        }

        private bool HasChildComponentNamed(Transform root, string componentTypeName)
        {
            if (root == null || string.IsNullOrEmpty(componentTypeName))
            {
                return false;
            }

            try
            {
                int childCount = root.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    Component[] components = child.GetComponents<Component>();
                    if (components != null)
                    {
                        for (int j = 0; j < components.Length; j++)
                        {
                            Component component = components[j];
                            if (component != null && string.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }

                    if (this.HasChildComponentNamed(child, componentTypeName))
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

        private Component FindChildComponentByName(Transform root, string componentTypeName)
        {
            if (root == null || string.IsNullOrEmpty(componentTypeName))
            {
                return null;
            }

            try
            {
                int childCount = root.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    Component[] components = child.GetComponents<Component>();
                    if (components != null)
                    {
                        for (int j = 0; j < components.Length; j++)
                        {
                            Component component = components[j];
                            if (component != null && string.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal))
                            {
                                return component;
                            }
                        }
                    }

                    Component nested = this.FindChildComponentByName(child, componentTypeName);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private Il2CppObject BoxInt(int val)
        {
            // FIX: Direct field assignment for maximum compatibility
            return new Il2CppSystem.Int32 { m_value = val }.BoxIl2CppObject();
        }

        private Il2CppObject BoxBool(bool val)
        {
            return new Il2CppSystem.Boolean { m_value = val }.BoxIl2CppObject();
        }

        private object TryResolveFieldByOwnerId(uint ownerId)
        {
            if (ownerId == 0U)
            {
                return null;
            }

            Type homelandEntitySystemType = this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.HomeLand.HomelandEntitySystem", "HomelandEntitySystem");
            if (homelandEntitySystemType != null)
            {
                MethodInfo getPlayerFieldMethod = homelandEntitySystemType.GetMethod("GetPlayerField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getPlayerFieldMethod != null)
                {
                    try
                    {
                        object homelandField = getPlayerFieldMethod.Invoke(null, new object[] { ownerId });
                        if (homelandField != null)
                        {
                            return homelandField;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
            if (entitiesType == null)
            {
                return null;
            }

            object fieldSystem = null;
            PropertyInfo fieldSystemProperty = entitiesType.GetProperty("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fieldSystemProperty != null)
            {
                fieldSystem = fieldSystemProperty.GetValue(null, null);
            }
            else
            {
                FieldInfo fieldSystemField = entitiesType.GetField("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (fieldSystemField != null)
                {
                    fieldSystem = fieldSystemField.GetValue(null);
                }
            }

            if (fieldSystem == null)
            {
                return null;
            }

            MethodInfo getFieldByOwnerIdMethod = fieldSystem.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo m)
            {
                if (m.Name != "GetFieldByOwnerId")
                {
                    return false;
                }
                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(uint);
            });
            if (getFieldByOwnerIdMethod == null)
            {
                return null;
            }

            try
            {
                return getFieldByOwnerIdMethod.Invoke(fieldSystem, new object[] { ownerId });
            }
            catch
            {
                return null;
            }
        }

        internal void ClearModReflectionLookupMissCaches()
        {
            this.loadedTypeMissCacheUntil.Clear();
            this.methodMissCacheUntil.Clear();
        }

        internal bool ModTryInvokeInstanceMethod(object instance, string methodName, params object[] args)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                Type type = instance.GetType();
                Type[] argTypes = args == null || args.Length == 0
                    ? Type.EmptyTypes
                    : args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
                MethodInfo method = this.GetMethodQuiet(
                    type,
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    argTypes);
                if (method == null && (args == null || args.Length == 0))
                {
                    method = this.GetMethodQuiet(
                        type,
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        Type.EmptyTypes);
                }

                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildMethodLookupCacheKey(Type type, string name, BindingFlags flags, Type[] parameterTypes, int paramCountOnly)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string typeName = type.FullName ?? type.Name ?? type.GetHashCode().ToString();
            if (paramCountOnly >= 0)
            {
                return typeName + "|pc:" + paramCountOnly + "|" + name + "|" + (int)flags;
            }

            Type[] types = parameterTypes ?? Type.EmptyTypes;
            if (types.Length == 0)
            {
                return typeName + "|sig:|" + name + "|" + (int)flags;
            }

            StringBuilder sb = new StringBuilder(typeName.Length + name.Length + (types.Length * 16));
            sb.Append(typeName).Append("|sig:").Append(name).Append('|').Append((int)flags);
            for (int i = 0; i < types.Length; i++)
            {
                Type pt = types[i];
                sb.Append('|').Append(pt?.FullName ?? "_");
            }

            return sb.ToString();
        }

        private MethodInfo ResolveCachedMethodQuiet(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Type[] resolvedParameterTypes = parameterTypes ?? Type.EmptyTypes;
            const BindingFlags flatten = BindingFlags.FlattenHierarchy;
            BindingFlags resolvedFlags = flags | flatten;
            string cacheKey = BuildMethodLookupCacheKey(type, name, resolvedFlags, resolvedParameterTypes, -1);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (this.methodLookupCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }

                if (this.methodMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil))
                {
                    if (Time.unscaledTime < missCacheUntil)
                    {
                        return null;
                    }

                    this.methodMissCacheUntil.Remove(cacheKey);
                }
            }

            MethodInfo method = type.GetMethod(name, resolvedFlags, null, resolvedParameterTypes, null);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (method != null)
                {
                    this.methodLookupCache[cacheKey] = method;
                    this.methodMissCacheUntil.Remove(cacheKey);
                }
                else
                {
                    this.methodMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedMethodMissCacheSeconds;
                }
            }

            return method;
        }

        private MethodInfo ResolveCachedMethodByParamCountQuiet(Type type, string name, int paramCount)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            string cacheKey = BuildMethodLookupCacheKey(type, name, flags, null, paramCount);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (this.methodLookupCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }

                if (this.methodMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil))
                {
                    if (Time.unscaledTime < missCacheUntil)
                    {
                        return null;
                    }

                    this.methodMissCacheUntil.Remove(cacheKey);
                }
            }

            MethodInfo resolved = null;
            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters != null && parameters.Length == paramCount)
                {
                    resolved = candidate;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (resolved != null)
                {
                    this.methodLookupCache[cacheKey] = resolved;
                    this.methodMissCacheUntil.Remove(cacheKey);
                }
                else
                {
                    this.methodMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedMethodMissCacheSeconds;
                }
            }

            return resolved;
        }

        private MethodInfo GetMethodQuiet(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        {
            return this.ResolveCachedMethodQuiet(type, name, flags, parameterTypes);
        }

        private MethodInfo GetMethodByNameAndParamCountQuiet(Type type, string name, int paramCount)
        {
            return this.ResolveCachedMethodByParamCountQuiet(type, name, paramCount);
        }

        private string DescribeType(Type type)
        {
            if (type == null)
            {
                return "null";
            }

            string assemblyName = string.Empty;
            try
            {
                Assembly ownerAssembly = type.Assembly;
                assemblyName = ownerAssembly != null ? ownerAssembly.GetName().Name : string.Empty;
            }
            catch
            {
            }

            return string.IsNullOrEmpty(assemblyName)
                ? (type.FullName ?? type.Name ?? "unknown")
                : (type.FullName ?? type.Name ?? "unknown") + "@" + assemblyName;
        }

        private static Dictionary<uint, int> ReadUIntIntDictionaryField(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            FieldInfo f = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null)
            {
                return null;
            }

            object v = f.GetValue(instance);
            if (v is Dictionary<uint, int> direct)
            {
                return direct;
            }

            if (v is IDictionary dict)
            {
                Dictionary<uint, int> map = new Dictionary<uint, int>();
                foreach (DictionaryEntry e in dict)
                {
                    try
                    {
                        uint key = Convert.ToUInt32(e.Key);
                        int value = Convert.ToInt32(e.Value);
                        map[key] = value;
                    }
                    catch
                    {
                    }
                }
                return map;
            }

            return null;
        }

        private bool TryGetManagedModule(Type moduleType, out object moduleObj)
        {
            moduleObj = null;
            if (moduleType == null)
            {
                return false;
            }

            try
            {
                Type managersType = this.FindLoadedType("XDTGame.Framework.Managers", "Managers");
                if (managersType == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackManaged] Managers type unavailable.");
                    return false;
                }

                MethodInfo getModule = managersType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "GetModule" || m.IsGenericMethodDefinition)
                        {
                            return false;
                        }
                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(Type);
                    });
                if (getModule != null)
                {
                    moduleObj = getModule.Invoke(null, new object[] { moduleType });
                    if (moduleObj != null)
                    {
                        this.AutoEatRepairLog("[DirectBackpackManaged] Resolved module via Managers.GetModule(Type): " + moduleType.FullName);
                        return true;
                    }
                }

                FieldInfo moduleDicField = managersType.GetField("_moduleDic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object moduleDicObj = moduleDicField != null ? moduleDicField.GetValue(null) : null;
                if (moduleDicObj is IDictionary moduleDic && moduleDic.Contains(moduleType))
                {
                    object moduleObject = moduleDic[moduleType];
                    if (moduleObject != null && this.TryGetObjectMember(moduleObject, "module", out moduleObj) && moduleObj != null)
                    {
                        this.AutoEatRepairLog("[DirectBackpackManaged] Resolved module via Managers._moduleDic: " + moduleType.FullName);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.AutoEatRepairLog("[DirectBackpackManaged] Module resolve exception for " + moduleType.FullName + ": " + inner.GetType().Name + ": " + inner.Message);
                moduleObj = null;
                return false;
            }
        }

        private object TryGetStaticObjectAcrossHierarchy(Type type, params string[] memberNames)
        {
            Type current = type;
            while (current != null)
            {
                foreach (string memberName in memberNames)
                {
                    PropertyInfo property = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (property != null)
                    {
                        try
                        {
                            object value = property.GetValue(null, null);
                            if (value != null)
                            {
                                return value;
                            }
                        }
                        catch { }
                    }

                    FieldInfo field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (field != null)
                    {
                        try
                        {
                            object value = field.GetValue(null);
                            if (value != null)
                            {
                                return value;
                            }
                        }
                        catch { }
                    }
                }

                current = current.BaseType;
            }

            return null;
        }

    }
}
