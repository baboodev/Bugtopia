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
        private bool TryReadIntMember(object obj, Type type, string memberName, out int value)
        {
            value = 0;
            if (obj == null || type == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                FieldInfo field = this.FindFieldInHierarchy(type, memberName);
                if (field != null)
                {
                    object raw = field.GetValue(obj);
                    if (raw != null)
                    {
                        value = Convert.ToInt32(raw);
                        return true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    object raw = property.GetValue(obj, null);
                    if (raw != null)
                    {
                        value = Convert.ToInt32(raw);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private object TryGetManagedMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type type = target.GetType();
            while (type != null)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null)
                    {
                        return property.GetValue(target, null);
                    }
                }
                catch
                {
                }

                try
                {
                    FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        return field.GetValue(target);
                    }
                }
                catch
                {
                }

                type = type.BaseType;
            }

            return null;
        }

        private unsafe bool TryGetMonoInt32Member(IntPtr obj, string memberName, out int value)
        {
            value = 0;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (!this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(int*)raw;
            return true;
        }

        private bool TryInvokeZeroArgMember(object instance, out object value, params string[] methodNames)
        {
            value = null;
            if (instance == null || methodNames == null || methodNames.Length == 0)
            {
                return false;
            }

            Type currentType = instance.GetType();
            while (currentType != null)
            {
                foreach (string methodName in methodNames)
                {
                    try
                    {
                        MethodInfo method = currentType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (method == null)
                        {
                            continue;
                        }

                        value = method.Invoke(instance, null);
                        if (value != null)
                        {
                            return true;
                        }
                    }
                    catch { }
                }

                currentType = currentType.BaseType;
            }

            return false;
        }

        private Il2CppObject ReadIl2CppObjectMember(Il2CppObject obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            try
            {
                var ilType = obj.GetIl2CppType();
                if (ilType == null)
                {
                    return null;
                }

                if (this.TryReadObjectMember(ilType, obj, memberName, out Il2CppObject value))
                {
                    return value;
                }
            }
            catch { }

            return null;
        }

        private bool TryReadVector3Member(Il2CppObject obj, string memberName, out Vector3 value)
        {
            value = Vector3.zero;
            if (obj == null || string.IsNullOrEmpty(memberName))
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

                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                Il2CppObject boxed = field != null ? field.GetValue(obj) : null;
                if (boxed == null)
                {
                    var prop = ilType.GetProperty(memberName);
                    boxed = prop != null ? (prop.GetValue(obj) as Il2CppObject) : null;
                }

                if (boxed == null)
                {
                    return false;
                }

                var boxedType = boxed.GetIl2CppType();
                float x = boxedType.GetField("x").GetValue(boxed).Unbox<float>();
                float y = boxedType.GetField("y").GetValue(boxed).Unbox<float>();
                float z = boxedType.GetField("z").GetValue(boxed).Unbox<float>();
                value = new Vector3(x, y, z);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadUIntMember(Il2CppType ilType, Il2CppObject instance, string memberName, out uint value)
        {
            value = 0U;
            if (ilType == null || instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                if (field != null)
                {
                    object fieldValue = field.GetValue(instance);
                    if (this.TryConvertToUInt(fieldValue, out value))
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                var prop = ilType.GetProperty(memberName);
                if (prop != null)
                {
                    object propValue = prop.GetValue(instance);
                    if (this.TryConvertToUInt(propValue, out value))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool TryReadIntMember(Il2CppType ilType, Il2CppObject instance, string memberName, out int value)
        {
            value = 0;
            if (ilType == null || instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                if (field != null)
                {
                    object fieldValue = field.GetValue(instance);
                    if (this.TryConvertToInt(fieldValue, out value))
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                var prop = ilType.GetProperty(memberName);
                if (prop != null)
                {
                    object propValue = prop.GetValue(instance);
                    if (this.TryConvertToInt(propValue, out value))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool TryReadObjectMember(Il2CppType ilType, Il2CppObject instance, string memberName, out Il2CppObject value)
        {
            value = null;
            if (ilType == null || instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                if (field != null)
                {
                    object fieldValue = field.GetValue(instance);
                    value = fieldValue as Il2CppObject;
                    if (value != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                var prop = ilType.GetProperty(memberName);
                if (prop != null)
                {
                    object propValue = prop.GetValue(instance);
                    value = propValue as Il2CppObject;
                    if (value != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private object TryGetFieldOrPropertyValue(object obj, params string[] names)
        {
            if (obj == null || names == null || names.Length == 0)
            {
                return null;
            }

            try
            {
                Type type = obj.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    FieldInfo field = type.GetField(name, flags);
                    if (field != null)
                    {
                        return field.GetValue(obj);
                    }

                    PropertyInfo property = type.GetProperty(name, flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        return property.GetValue(obj, null);
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryReadMemberText(Il2CppType ilType, Il2CppObject instance, string memberName, out string value)
        {
            value = string.Empty;
            if (ilType == null || instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                if (field != null)
                {
                    object fieldValue = field.GetValue(instance);
                    if (fieldValue != null)
                    {
                        value = fieldValue.ToString();
                        return !string.IsNullOrEmpty(value);
                    }
                }
            }
            catch
            {
            }

            try
            {
                var prop = ilType.GetProperty(memberName);
                if (prop != null)
                {
                    object propValue = prop.GetValue(instance);
                    if (propValue != null)
                    {
                        value = propValue.ToString();
                        return !string.IsNullOrEmpty(value);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryReadAnyIntMember(Il2CppType ilType, Il2CppObject instance, string[] memberNames, out int value)
        {
            value = 0;
            if (memberNames == null)
            {
                return false;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                if (this.TryReadIntMember(ilType, instance, memberNames[i], out value))
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TrySetMonoVector2Member(IntPtr obj, string memberName, Vector2 value)
        {
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName) || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            IntPtr fieldPtr = this.FindAuraMonoFieldOnHierarchy(classPtr, memberName);
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }

            Vector2 fieldValue = value;
            auraMonoFieldSetValue(obj, fieldPtr, (IntPtr)(&fieldValue));
            return true;
        }

        private bool TryReadBooleanMember(object obj, params string[] memberNames)
        {
            if (obj == null || memberNames == null)
            {
                return false;
            }

            foreach (string memberName in memberNames)
            {
                if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToBoolean(raw);
                }
                catch
                {
                }
            }

            return false;
        }

        private float TryReadSingleMember(object obj, params string[] memberNames)
        {
            if (obj == null || memberNames == null)
            {
                return 0f;
            }

            foreach (string memberName in memberNames)
            {
                if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToSingle(raw);
                }
                catch
                {
                }
            }

            return 0f;
        }

        private string TryReadMonoStringMemberOrEmpty(IntPtr obj, string memberName)
        {
            if (obj != IntPtr.Zero && !string.IsNullOrWhiteSpace(memberName) && this.TryGetMonoStringMember(obj, memberName, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            return string.Empty;
        }

        private Il2CppReferenceArray<Il2CppObject> BuildIl2CppInvokeArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            Il2CppObject[] converted = new Il2CppObject[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                converted[i] = this.ConvertToIl2CppInvokeArg(args[i]);
            }

            return new Il2CppReferenceArray<Il2CppObject>(converted);
        }

        private Il2CppObject ConvertToIl2CppInvokeArg(object arg)
        {
            if (arg == null)
            {
                return null;
            }

            if (arg is Il2CppObject il2CppObject)
            {
                return il2CppObject;
            }

            if (arg is int intValue)
            {
                return this.BoxInt(intValue);
            }

            if (arg is bool boolValue)
            {
                return this.BoxBool(boolValue);
            }

            if (arg is Enum enumValue)
            {
                return this.BoxInt(Convert.ToInt32(enumValue));
            }

            if (arg is Vector3 vector3Value)
            {
                return vector3Value.BoxIl2CppObject();
            }

            if (arg is string stringValue)
            {
                if (string.IsNullOrEmpty(stringValue))
                {
                    return null;
                }

                throw new NotSupportedException("Non-empty string IL2CPP invoke args are not supported yet.");
            }

            throw new NotSupportedException("Unsupported IL2CPP invoke arg type: " + arg.GetType().FullName);
        }

        private bool TrySetFieldValue(Type ownerType, ref object target, string fieldName, object value)
        {
            if (ownerType == null || target == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            FieldInfo field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            field.SetValue(target, value);
            return true;
        }

        private bool TryGetIl2CppObjectPointer(object obj, out IntPtr pointer)
        {
            return this.TryGetIl2CppObjectPointer(obj, out pointer, 0);
        }

        private bool TryGetIl2CppObjectPointer(object obj, out IntPtr pointer, int depth)
        {
            pointer = IntPtr.Zero;
            if (obj == null || depth > 2)
            {
                return false;
            }

            if (obj is IntPtr directPointer)
            {
                pointer = directPointer;
                return pointer != IntPtr.Zero;
            }

            try
            {
                Type type = obj.GetType();
                foreach (string memberName in new string[] { "Pointer", "NativeObject", "Il2CppObject", "m_Pointer", "_pointer", "pointer" })
                {
                    object value = null;
                    PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null)
                    {
                        value = property.GetValue(obj, null);
                    }
                    else
                    {
                        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            value = field.GetValue(obj);
                        }
                    }

                    if (this.TryConvertObjectToIntPtr(value, out pointer))
                    {
                        return pointer != IntPtr.Zero;
                    }

                    if (value != null && !ReferenceEquals(value, obj) && this.TryGetIl2CppObjectPointer(value, out pointer, depth + 1))
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

        private bool TryReadManagedNetIdMember(object obj, string memberName, out uint value)
        {
            value = 0U;
            object raw = this.TryGetManagedMemberValue(obj, memberName);
            return this.TryConvertManagedNetIdToUInt32(raw, out value);
        }

        private bool TryGetMonoUInt64Member(IntPtr obj, string memberName, out ulong value)
        {
            value = 0UL;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero)
            {
                return false;
            }

            value = this.TryReadMonoUnsignedIntegral(boxed);
            return true;
        }

        private bool TryGetMonoBoolMember(IntPtr obj, string memberName, out bool value)
        {
            value = false;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        private bool TryReadManagedInt32Member(object obj, string memberName, out int value)
        {
            value = 0;
            object raw = this.TryGetManagedMemberValue(obj, memberName);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadManagedUInt32Member(object obj, string memberName, out uint value)
        {
            value = 0U;
            object raw = this.TryGetManagedMemberValue(obj, memberName);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadManagedUInt64Member(object obj, string memberName, out ulong value)
        {
            value = 0UL;
            object raw = this.TryGetManagedMemberValue(obj, memberName);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt64(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadManagedBoolMember(object obj, string memberName, out bool value)
        {
            value = false;
            object raw = this.TryGetManagedMemberValue(obj, memberName);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToBoolean(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadIntMember(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                if (!this.TryGetObjectMember(instance, memberName, out object raw) || raw == null)
                {
                    return false;
                }

                if (raw is Enum)
                {
                    value = Convert.ToInt32(raw);
                    return true;
                }

                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadFloatMember(object instance, string memberName, out float value)
        {
            value = 0f;
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                if (!this.TryGetObjectMember(instance, memberName, out object raw) || raw == null)
                {
                    return false;
                }

                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetMonoObjectMember(IntPtr obj, string memberName, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName) || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(obj);
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr fieldPtr = this.FindAuraMonoFieldOnHierarchy(klass, memberName);
            if (fieldPtr != IntPtr.Zero && auraMonoFieldGetValueObject != null)
            {
                valueObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, fieldPtr, obj);
                if (valueObj != IntPtr.Zero)
                {
                    return true;
                }
            }

            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(klass, "get_" + memberName, 0);
            if (getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            // mono_runtime_invoke on a VALUE-TYPE instance method takes the UNBOXED struct as
            // `this`; passing the box shifts every field read by the 16-byte object header —
            // get_Key on a boxed KeyValuePair returns the vtable pointer's low dword (the
            // constant NPC-id=371743616 bug, 2026-07-09). Unbox for the invoke and pin the box
            // across it; fail closed when the value/ref classification export is missing.
            IntPtr self = obj;
            uint pin = 0;
            if (auraMonoClassIsValueType == null)
            {
                return false;
            }
            if (auraMonoClassIsValueType(klass) != 0)
            {
                if (auraMonoObjectUnbox == null)
                {
                    return false;
                }
                pin = AuraMonoPinNew(obj);
                self = auraMonoObjectUnbox(obj);
                if (self == IntPtr.Zero)
                {
                    AuraMonoPinFree(pin);
                    return false;
                }
            }
            try
            {
                IntPtr exc = IntPtr.Zero;
                valueObj = auraMonoRuntimeInvoke(getter, self, IntPtr.Zero, ref exc);
                return exc == IntPtr.Zero && valueObj != IntPtr.Zero;
            }
            finally
            {
                if (pin != 0)
                {
                    AuraMonoPinFree(pin);
                }
            }
        }

        private unsafe bool TryGetMonoVector3Member(IntPtr obj, string memberName, out Vector3 value)
        {
            value = Vector3.zero;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(Vector3*)raw;
            return true;
        }

        private bool TryGetMonoUInt32FromObjectMember(IntPtr obj, string objectMemberName, string valueMemberName, out uint value)
        {
            value = 0U;
            IntPtr nestedObj;
            return this.TryGetMonoObjectMember(obj, objectMemberName, out nestedObj) && this.TryGetMonoUInt32Member(nestedObj, valueMemberName, out value);
        }

        private unsafe bool TryGetMonoUInt32Member(IntPtr obj, string memberName, out uint value)
        {
            value = 0U;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(uint*)raw;
            return true;
        }

        private unsafe bool TryGetMonoIntMember(IntPtr obj, string memberName, out int value)
        {
            value = 0;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(int*)raw;
            return true;
        }

        private unsafe bool TryGetMonoSingleMember(IntPtr obj, string memberName, out float value)
        {
            value = 0f;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(float*)raw;
            return true;
        }

        private unsafe bool TryGetMonoBoundsCenterMember(IntPtr obj, string memberName, out Vector3 center)
        {
            center = Vector3.zero;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            Bounds bounds = *(Bounds*)raw;
            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            center = bounds.center;
            return true;
        }

        private bool TryGetUIntMember(object instance, string memberName, out uint value)
        {
            value = 0U;
            object obj;
            if (!this.TryGetObjectMember(instance, memberName, out obj))
            {
                return false;
            }
            if (obj is uint)
            {
                value = (uint)obj;
                return true;
            }
            try
            {
                value = Convert.ToUInt32(obj);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetObjectMember(object instance, string memberName, out object value)
        {
            value = null;
            if (instance == null)
            {
                return false;
            }
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo propertyInfo = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (propertyInfo != null)
                {
                    try
                    {
                        value = propertyInfo.GetValue(instance, null);
                        return true;
                    }
                    catch
                    {
                    }
                }
                FieldInfo fieldInfo = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (fieldInfo != null)
                {
                    try
                    {
                        value = fieldInfo.GetValue(instance);
                        return true;
                    }
                    catch
                    {
                    }
                }
                type = type.BaseType;
            }
            return false;
        }

        internal Type ModFindLoadedType(params string[] names) => this.FindLoadedType(names);

        internal Type ModFindLoadedTypeByFullName(string fullName) => this.FindLoadedTypeByFullName(fullName);

        internal bool ModTryGetObjectMember(object instance, string memberName, out object value) =>
            this.TryGetObjectMember(instance, memberName, out value);

        internal bool ModTrySetObjectMember(object instance, string memberName, object value) =>
            this.TrySetObjectMember(instance, memberName, value);

        private bool TrySetObjectMember(object instance, string memberName, object value)
        {
            if (instance == null)
            {
                return false;
            }

            Type type = instance.GetType();
            PropertyInfo propertyInfo = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                propertyInfo.SetValue(instance, value, null);
                return true;
            }

            FieldInfo fieldInfo = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(instance, value);
                return true;
            }

            return false;
        }

        private bool TryReadStringMember(Il2CppType ilType, Il2CppObject instance, string memberName, out string value)
        {
            value = string.Empty;
            if (ilType == null || instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                Il2CppFieldInfo field = ilType.GetField(memberName, (Il2CppBindingFlags)62);
                object fieldValue = field != null ? field.GetValue(instance) : null;
                if (fieldValue != null)
                {
                    value = fieldValue.ToString();
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
            catch
            {
            }

            try
            {
                Il2CppPropertyInfo prop = ilType.GetProperty(memberName);
                object propValue = prop != null ? prop.GetValue(instance) : null;
                if (propValue != null)
                {
                    value = propValue.ToString();
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetMonoStringMember(IntPtr obj, string memberName, out string value)
        {
            value = string.Empty;
            IntPtr stringObj;
            return this.TryGetMonoObjectMember(obj, memberName, out stringObj) && this.TryReadMonoString(stringObj, out value);
        }

        private bool TryReadMonoString(IntPtr monoStringObj, out string value)
        {
            value = string.Empty;
            if (monoStringObj == IntPtr.Zero || auraMonoStringToUtf8 == null)
            {
                return false;
            }

            IntPtr utf8Ptr = IntPtr.Zero;
            try
            {
                utf8Ptr = auraMonoStringToUtf8(monoStringObj);
                if (utf8Ptr == IntPtr.Zero)
                {
                    return false;
                }

                value = Marshal.PtrToStringUTF8(utf8Ptr) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                value = string.Empty;
                return false;
            }
            finally
            {
                if (utf8Ptr != IntPtr.Zero && auraMonoFree != null)
                {
                    try
                    {
                        auraMonoFree(utf8Ptr);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private Type FindLoadedType(params string[] names)
        {
            string cacheKey = names != null && names.Length > 0 ? string.Join("|", names) : string.Empty;
            if (!string.IsNullOrEmpty(cacheKey) && this.loadedTypeLookupCache.TryGetValue(cacheKey, out Type cachedType) && cachedType != null)
            {
                return cachedType;
            }

            if (!string.IsNullOrEmpty(cacheKey) && this.loadedTypeMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil))
            {
                if (Time.unscaledTime < missCacheUntil)
                {
                    return null;
                }

                this.loadedTypeMissCacheUntil.Remove(cacheKey);
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (string text in names)
            {
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                Type type = Type.GetType(text, false);
                if (type != null)
                {
                    if (!string.IsNullOrEmpty(cacheKey))
                    {
                        this.loadedTypeLookupCache[cacheKey] = type;
                        this.loadedTypeMissCacheUntil.Remove(cacheKey);
                    }

                    return type;
                }
                foreach (Assembly assembly in assemblies)
                {
                    Type type2 = assembly.GetType(text, false);
                    if (type2 != null)
                    {
                        if (!string.IsNullOrEmpty(cacheKey))
                        {
                            this.loadedTypeLookupCache[cacheKey] = type2;
                            this.loadedTypeMissCacheUntil.Remove(cacheKey);
                        }

                        return type2;
                    }
                }
            }
            foreach (Assembly assembly2 in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly2.GetTypes();
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
                foreach (Type type3 in types)
                {
                    if (!(type3 == null))
                    {
                        foreach (string text2 in names)
                        {
                            if (string.IsNullOrEmpty(text2))
                            {
                                continue;
                            }

                            if (string.Equals(type3.FullName, text2, StringComparison.Ordinal) || string.Equals(type3.Name, text2, StringComparison.Ordinal))
                            {
                                if (!string.IsNullOrEmpty(cacheKey))
                                {
                                    this.loadedTypeLookupCache[cacheKey] = type3;
                                    this.loadedTypeMissCacheUntil.Remove(cacheKey);
                                }

                                return type3;
                            }
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(cacheKey))
            {
                this.loadedTypeMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedTypeMissCacheSeconds;
            }
            return null;
        }

        private Type FindLoadedTypeBySuffix(params string[] suffixes)
        {
            if (suffixes == null || suffixes.Length == 0)
            {
                return null;
            }

            string cacheKey = "suffix:" + string.Join("|", suffixes);
            if (this.loadedTypeLookupCache.TryGetValue(cacheKey, out Type cachedType) && cachedType != null)
            {
                return cachedType;
            }

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
                    if (type == null)
                    {
                        continue;
                    }

                    string fullName = type.FullName ?? string.Empty;
                    string name = type.Name ?? string.Empty;
                    foreach (string suffix in suffixes)
                    {
                        if (string.IsNullOrEmpty(suffix))
                        {
                            continue;
                        }

                        if (fullName.EndsWith(suffix, StringComparison.Ordinal) || name.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            this.loadedTypeLookupCache[cacheKey] = type;
                            return type;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(cacheKey))
            {
                this.loadedTypeMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedTypeMissCacheSeconds;
            }

            return null;
        }

        private Type FindLoadedTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            return this.FindLoadedType(fullName);
        }

        private unsafe bool TryIl2CppDictionaryTryGetIntObject(IntPtr dictObj, int key, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            return dictObj != IntPtr.Zero && this.TryIl2CppDictionaryContainsIntKey(dictObj, key);
        }

        private unsafe bool TryIl2CppDictionaryContainsIntKey(IntPtr dictObj, int key)
        {
            if (dictObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr dictClass = IL2CPP.il2cpp_object_get_class(dictObj);
            if (dictClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr containsKey = IL2CPP.il2cpp_class_get_method_from_name(dictClass, "ContainsKey", 1);
            if (containsKey == IntPtr.Zero)
            {
                return false;
            }

            int localKey = key;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&localKey);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = IL2CPP.il2cpp_runtime_invoke(containsKey, dictObj, (void**)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            IntPtr unboxed = IL2CPP.il2cpp_object_unbox(boxed);
            return unboxed != IntPtr.Zero && Marshal.ReadByte(unboxed) != 0;
        }

        private unsafe int TryReadIl2CppObjectIntField(IntPtr obj, string fieldName)
        {
            if (obj == IntPtr.Zero || string.IsNullOrWhiteSpace(fieldName))
            {
                return 0;
            }

            IntPtr klass = IL2CPP.il2cpp_object_get_class(obj);
            if (klass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, fieldName);
            if (field == IntPtr.Zero)
            {
                return 0;
            }

            int value = 0;
            IL2CPP.il2cpp_field_get_value(obj, field, &value);
            return value;
        }

        private unsafe int TryReadIl2CppKeyValuePairIntKey(IntPtr pairObj, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (pairObj == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr klass = IL2CPP.il2cpp_object_get_class(pairObj);
            if (klass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr keyField = IL2CPP.il2cpp_class_get_field_from_name(klass, "key");
            if (keyField == IntPtr.Zero)
            {
                keyField = IL2CPP.il2cpp_class_get_field_from_name(klass, "Key");
            }

            IntPtr valueField = IL2CPP.il2cpp_class_get_field_from_name(klass, "value");
            if (valueField == IntPtr.Zero)
            {
                valueField = IL2CPP.il2cpp_class_get_field_from_name(klass, "Value");
            }

            int key = 0;
            if (keyField != IntPtr.Zero)
            {
                IntPtr keyObj = IL2CPP.il2cpp_field_get_value_object(keyField, pairObj);
                if (keyObj != IntPtr.Zero)
                {
                    IntPtr unboxed = IL2CPP.il2cpp_object_unbox(keyObj);
                    if (unboxed != IntPtr.Zero)
                    {
                        key = Marshal.ReadInt32(unboxed);
                    }
                }
                else
                {
                    IL2CPP.il2cpp_field_get_value(pairObj, keyField, &key);
                }
            }

            if (valueField != IntPtr.Zero)
            {
                valueObj = IL2CPP.il2cpp_field_get_value_object(valueField, pairObj);
            }

            return key;
        }

        private unsafe IntPtr TryFindIl2CppClass(string className, params string[] nameSpaces)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return IntPtr.Zero;
            }

            if (autoSellIl2CppClassCache.TryGetValue(className, out IntPtr cachedClass) && cachedClass != IntPtr.Zero)
            {
                return cachedClass;
            }

            IntPtr domain = IL2CPP.il2cpp_domain_get();
            if (domain == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint assemblyCount = 0;
            IntPtr* assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref assemblyCount);
            if (assemblies == null || assemblyCount == 0)
            {
                return IntPtr.Zero;
            }

            for (uint i = 0; i < assemblyCount; i++)
            {
                IntPtr assembly = (IntPtr)assemblies[i];
                if (assembly == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr image = IL2CPP.il2cpp_assembly_get_image(assembly);
                if (image == IntPtr.Zero)
                {
                    continue;
                }

                if (nameSpaces != null)
                {
                    for (int n = 0; n < nameSpaces.Length; n++)
                    {
                        IntPtr klass = IL2CPP.il2cpp_class_from_name(image, nameSpaces[n] ?? string.Empty, className);
                        if (klass != IntPtr.Zero)
                        {
                            autoSellIl2CppClassCache[className] = klass;
                            return klass;
                        }
                    }
                }

                IntPtr klassEmpty = IL2CPP.il2cpp_class_from_name(image, string.Empty, className);
                if (klassEmpty != IntPtr.Zero)
                {
                    autoSellIl2CppClassCache[className] = klassEmpty;
                    return klassEmpty;
                }
            }

            IntPtr scannedClass = this.TryFindIl2CppClassByNameScan(className);
            if (scannedClass != IntPtr.Zero)
            {
                autoSellIl2CppClassCache[className] = scannedClass;
            }

            return scannedClass;
        }

        private unsafe IntPtr TryFindIl2CppClassByNameScan(string className)
        {
            IntPtr domain = IL2CPP.il2cpp_domain_get();
            if (domain == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint assemblyCount = 0;
            IntPtr* assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref assemblyCount);
            if (assemblies == null || assemblyCount == 0)
            {
                return IntPtr.Zero;
            }

            for (uint i = 0; i < assemblyCount; i++)
            {
                IntPtr assembly = (IntPtr)assemblies[i];
                if (assembly == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr image = IL2CPP.il2cpp_assembly_get_image(assembly);
                if (image == IntPtr.Zero)
                {
                    continue;
                }

                uint classCount = IL2CPP.il2cpp_image_get_class_count(image);
                for (uint c = 0; c < classCount; c++)
                {
                    IntPtr klass = IL2CPP.il2cpp_image_get_class(image, c);
                    if (klass == IntPtr.Zero)
                    {
                        continue;
                    }

                    string name = IL2CPP.il2cpp_class_get_name_(klass);
                    if (string.Equals(name, className, StringComparison.Ordinal))
                    {
                        return klass;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static unsafe int TryReadIl2CppInstanceIntFieldStatic(IntPtr obj, string fieldName)
        {
            if (obj == IntPtr.Zero || string.IsNullOrWhiteSpace(fieldName))
            {
                return 0;
            }

            IntPtr klass = IL2CPP.il2cpp_object_get_class(obj);
            if (klass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, fieldName);
            if (field == IntPtr.Zero)
            {
                return 0;
            }

            int value = 0;
            IL2CPP.il2cpp_field_get_value(obj, field, &value);
            return value;
        }

        private static int ReadIntFieldValue(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return 0;
            }

            FieldInfo f = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null)
            {
                return 0;
            }

            object v = f.GetValue(instance);
            if (v == null)
            {
                return 0;
            }

            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private bool TryGetManagedUInt32Member(object obj, string memberName, out uint value)
        {
            value = 0U;
            if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt32(raw);
                return true;
            }
            catch
            {
                value = 0U;
                return false;
            }
        }

        private bool TryGetManagedInt32Member(object obj, string memberName, out int value)
        {
            value = 0;
            if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

    }
}
