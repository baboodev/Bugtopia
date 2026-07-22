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
        private unsafe bool TryGetAuraMonoStaticObjectField(IntPtr classPtr, string fieldName, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (classPtr == IntPtr.Zero || string.IsNullOrEmpty(fieldName) || auraMonoClassGetFieldFromName == null || auraMonoClassVtable == null || auraMonoFieldStaticGetValue == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            // Fail closed until the game's Mono side is proven up: mono_field_static_get_value
            // below reads the class's static data straight off its vtable with NO initialization
            // check, so on a class the login screen never initialized it dereferences near-null and
            // takes the whole process down (uncatchable AV — see AuraMonoStaticFieldReadsAllowed).
            if (!AuraMonoStaticFieldReadsAllowed())
            {
                return false;
            }

            IntPtr fieldPtr = auraMonoClassGetFieldFromName(classPtr, fieldName);
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, classPtr);
            if (vtable == IntPtr.Zero)
            {
                return false;
            }

            IntPtr rawValue = IntPtr.Zero;
            auraMonoFieldStaticGetValue(vtable, fieldPtr, (IntPtr)(&rawValue));
            valueObj = rawValue;
            return valueObj != IntPtr.Zero;
        }

        private bool TryEnumerateAuraMonoCollectionItems(IntPtr collectionObj, List<IntPtr> output, List<uint> pins = null, int maxItems = MaxAuraMonoCollectionItems)
        {
            if (collectionObj == IntPtr.Zero || output == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            // Without the mono_gchandle exports nothing can actually be pinned on the moving
            // sgen GC — AuraMonoPinNew silently no-ops, and both this walk and every member read
            // on the returned pointers race object relocation → random native AV (field report:
            // AV in TryGetMonoInt32Member off the loot-collect scan right after enabling Aura
            // Farm on a build where the export failed to resolve). Fail closed: features degrade
            // instead of crashing the game.
            if (!AuraMonoPinningAvailable)
            {
                if (!auraMonoPinningUnavailableLogged)
                {
                    auraMonoPinningUnavailableLogged = true;
                    ModLogger.Msg("[AuraMono] mono_gchandle exports unavailable — collection enumeration disabled (pin-less walks would crash).");
                }
                return false;
            }

            // Prime suspect for the no-dump crashes: enumerating a live game collection that can be
            // mutated/reallocated mid-walk. Tick (throttled) so the trail shows if death was here.
            Breadcrumbs.Tick("AuraMono.enumerate");

            // Pin the collection object for the entire walk. The get_Item / GetEnumerator / MoveNext
            // invokes below dereference collectionObj repeatedly, and there is no mono_gc_disable on
            // this sgen (moving) build, so the GC can relocate/collect it mid-walk -> native AV. This
            // is the recurring AuraMono.enumerate no-crashlog death (incl. "show pet favorite food").
            uint collectionPin = AuraMonoPinNew(collectionObj);
            try
            {

            void AddEnumeratedItem(IntPtr itemPtr)
            {
                output.Add(itemPtr);
                if (pins != null)
                {
                    pins.Add(AuraMonoPinNew(itemPtr));
                }
            }

            IntPtr collectionClass = auraMonoObjectGetClass(collectionObj);
            if (collectionClass == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoArrayLength != null && this.IsAuraMonoArrayObject(collectionObj))
            {
                try
                {
                    int arrayCount = (int)Math.Min(auraMonoArrayLength(collectionObj).ToUInt64(), 8192UL);

                    // Fast path for reference-type arrays (e.g. the ECS GetAllComponents slot array,
                    // which can be thousands of mostly-null entries). Resolve the element-0 address
                    // ONCE and read each pointer as a contiguous managed memory read, instead of a
                    // mono_array_addr_with_size P/Invoke (or Array.GetValue runtime invoke) per
                    // element — those cost ~µs–ms each, so a few-thousand-slot array would stall the
                    // frame (symptom: inspectMs ~240/entity even after the invoke->addr change).
                    IntPtr arrayBase = (auraMonoArrayAddrWithSize != null && arrayCount > 0 && this.IsAuraMonoReferenceArray(collectionClass))
                        ? auraMonoArrayAddrWithSize(collectionObj, IntPtr.Size, UIntPtr.Zero)
                        : IntPtr.Zero;
                    if (arrayBase != IntPtr.Zero)
                    {
                        for (int i = 0; i < arrayCount; i++)
                        {
                            IntPtr itemObj = Marshal.ReadIntPtr(arrayBase, i * IntPtr.Size);
                            if (itemObj != IntPtr.Zero)
                            {
                                AddEnumeratedItem(itemObj);
                            }
                        }

                        if (output.Count > 0)
                        {
                            return true;
                        }
                    }

                    // NOTE: the old per-element Array.GetValue fallback was removed. It was the
                    // ves_icall_System_Array_GetValue / icall.c abort+AV source: it ran for VALUE-type
                    // arrays (and any object misdetected as an array), boxing each element. For object
                    // enumeration those boxed structs are useless (not entity pointers), and invoking
                    // Array.GetValue per element on a collection the game is mutating (e.g. entities
                    // loading/unloading while roaming) crashes natively (uncatchable). Reference-type
                    // arrays use the safe contiguous read above; everything else falls through to the
                    // get_Item / GetEnumerator / nested-member paths below.
                }
                catch
                {
                }
            }

            IntPtr getCountMethod = this.FindAuraMonoMethodOnHierarchy(collectionClass, "get_Count", 0);
            IntPtr getItemMethod = this.FindAuraMonoMethodOnHierarchy(collectionClass, "get_Item", 1);
            // Dictionaries also expose get_Count and a 1-arg get_Item, but that indexer takes a
            // KEY, not an index: probing it with 0..Count-1 throws KeyNotFoundException for nearly
            // every element (and could return wrong items for densely int-keyed dicts). Route
            // keyed collections straight to the enumerator path below.
            bool isKeyedCollection = this.FindAuraMonoMethodOnHierarchy(collectionClass, "ContainsKey", 1) != IntPtr.Zero;
            if (!isKeyedCollection && getCountMethod != IntPtr.Zero && getItemMethod != IntPtr.Zero)
            {
                int count = Math.Min(this.GetAuraMonoIntCount(collectionObj, getCountMethod), maxItems);
                for (int i = 0; i < count; i++)
                {
                    unsafe
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&i);
                        IntPtr itemObj = auraMonoRuntimeInvoke(getItemMethod, collectionObj, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero && itemObj != IntPtr.Zero)
                        {
                            AddEnumeratedItem(itemObj);
                        }
                    }
                }

                if (output.Count > 0)
                {
                    return true;
                }
            }

            IntPtr getEnumeratorMethod = this.FindAuraMonoMethodOnHierarchy(collectionClass, "GetEnumerator", 0);
            if (getEnumeratorMethod == IntPtr.Zero)
            {
                getEnumeratorMethod = this.FindAuraMonoMethodOnHierarchy(collectionClass, "System.Collections.IEnumerable.GetEnumerator", 0);
            }

            if (getEnumeratorMethod != IntPtr.Zero)
            {
                // A live game collection can be mutated while we walk it (entities load/unload,
                // game threads touch the dictionary), which surfaces as InvalidOperationException
                // from MoveNext mid-walk. Retry once with a fresh enumerator instead of silently
                // returning a partial result every frame.
                int baselineCount = output.Count;
                int pinsBaseline = pins != null ? pins.Count : 0;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (output.Count > baselineCount)
                    {
                        output.RemoveRange(baselineCount, output.Count - baselineCount);
                        if (pins != null && pins.Count > pinsBaseline)
                        {
                            for (int k = pinsBaseline; k < pins.Count; k++)
                            {
                                AuraMonoPinFree(pins[k]);
                            }
                            pins.RemoveRange(pinsBaseline, pins.Count - pinsBaseline);
                        }
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr enumeratorObj = auraMonoRuntimeInvoke(getEnumeratorMethod, collectionObj, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero || enumeratorObj == IntPtr.Zero)
                    {
                        break;
                    }

                    IntPtr enumeratorClass = auraMonoObjectGetClass(enumeratorObj);
                    IntPtr moveNextMethod = this.FindAuraMonoMethodOnHierarchy(enumeratorClass, "MoveNext", 0);
                    IntPtr getCurrentMethod = this.FindAuraMonoMethodOnHierarchy(enumeratorClass, "get_Current", 0);
                    if (getCurrentMethod == IntPtr.Zero)
                    {
                        getCurrentMethod = this.FindAuraMonoMethodOnHierarchy(enumeratorClass, "System.Collections.IEnumerator.get_Current", 0);
                    }

                    if (moveNextMethod == IntPtr.Zero || getCurrentMethod == IntPtr.Zero)
                    {
                        break;
                    }

                    // Struct enumerators (Dictionary<K,V>.Enumerator & co.) come back BOXED, and
                    // mono_runtime_invoke on a valuetype instance method takes the UNBOXED value as
                    // 'this' — invoking with the box shifts 'this' by the object header, so MoveNext
                    // reads/writes through garbage state → native heap corruption with no crashlog
                    // (hit via the battle-pass static-table walk with Festival For Tokens on and no
                    // active festival). Unbox structs for the invoke; if the valuetype check isn't
                    // available, skip the enumerator path entirely rather than risk the boxed call.
                    if (auraMonoClassIsValueType == null || auraMonoObjectUnbox == null)
                    {
                        break;
                    }
                    IntPtr enumeratorSelf = this.TryAuraMonoBoxedIsValueType(enumeratorObj)
                        ? auraMonoObjectUnbox(enumeratorObj)
                        : enumeratorObj;
                    if (enumeratorSelf == IntPtr.Zero)
                    {
                        break;
                    }

                    bool moveNextThrew = false;
                    // Pin the enumerator box for the walk: MoveNext/get_Current allocate boxed
                    // returns on every call, and a moving-GC pass would relocate an unpinned box
                    // (and invalidate the unboxed interior pointer above).
                    uint enumeratorPin = AuraMonoPinNew(enumeratorObj);
                    try
                    {
                        int safety = 0;
                        while (safety < maxItems)
                        {
                            exc = IntPtr.Zero;
                            IntPtr moved = auraMonoRuntimeInvoke(moveNextMethod, enumeratorSelf, IntPtr.Zero, ref exc);
                            if (exc != IntPtr.Zero)
                            {
                                moveNextThrew = true;
                                break;
                            }

                            bool hasNext = false;
                            if (moved == IntPtr.Zero)
                            {
                                break;
                            }

                            try
                            {
                                IntPtr rawMoved = auraMonoObjectUnbox(moved);
                                if (rawMoved == IntPtr.Zero)
                                {
                                    break;
                                }

                                hasNext = Marshal.ReadByte(rawMoved) != 0;
                            }
                            catch
                            {
                                break;
                            }

                            if (!hasNext)
                            {
                                break;
                            }

                            exc = IntPtr.Zero;
                            IntPtr currentObj = auraMonoRuntimeInvoke(getCurrentMethod, enumeratorSelf, IntPtr.Zero, ref exc);
                            if (exc == IntPtr.Zero && currentObj != IntPtr.Zero)
                            {
                                AddEnumeratedItem(currentObj);
                            }

                            safety++;
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(enumeratorPin);
                    }

                    if (!moveNextThrew)
                    {
                        break;
                    }
                }

                if (output.Count > baselineCount)
                {
                    return true;
                }
            }

            foreach (string memberName in new string[] { "_entries", "entries", "_values", "values", "Values", "_items", "items" })
            {
                if (this.TryGetMonoObjectMember(collectionObj, memberName, out IntPtr nested) && nested != IntPtr.Zero && nested != collectionObj)
                {
                    if (this.TryEnumerateAuraMonoCollectionItems(nested, output, pins, maxItems))
                    {
                        return output.Count > 0;
                    }
                }
            }

            foreach (string methodName in new string[] { "get_Values", "GetValues", "get_Items", "GetItems", "get_Entries", "GetEntries" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(collectionClass, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr nested = auraMonoRuntimeInvoke(methodPtr, collectionObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && nested != IntPtr.Zero && nested != collectionObj)
                {
                    if (this.TryEnumerateAuraMonoCollectionItems(nested, output, pins, maxItems))
                    {
                        return output.Count > 0;
                    }
                }
            }

            IntPtr getFirstMethod = this.FindAuraMonoMethodOnHierarchy(collectionClass, "get_First", 0);
            if (getFirstMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr nodeObj = auraMonoRuntimeInvoke(getFirstMethod, collectionObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && nodeObj != IntPtr.Zero)
                {
                    IntPtr nodeClass = auraMonoObjectGetClass(nodeObj);
                    IntPtr getValueMethod = this.FindAuraMonoMethodOnHierarchy(nodeClass, "get_Value", 0);
                    IntPtr getNextMethod = this.FindAuraMonoMethodOnHierarchy(nodeClass, "get_Next", 0);
                    int safety = 0;
                    while (nodeObj != IntPtr.Zero && getValueMethod != IntPtr.Zero && getNextMethod != IntPtr.Zero && safety < maxItems)
                    {
                        exc = IntPtr.Zero;
                        IntPtr valueObj = auraMonoRuntimeInvoke(getValueMethod, nodeObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && valueObj != IntPtr.Zero)
                        {
                            AddEnumeratedItem(valueObj);
                        }

                        exc = IntPtr.Zero;
                        nodeObj = auraMonoRuntimeInvoke(getNextMethod, nodeObj, IntPtr.Zero, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            break;
                        }

                        safety++;
                    }
                }
            }

            return output.Count > 0;
            }
            finally
            {
                AuraMonoPinFree(collectionPin);
            }
        }

        private string GetAuraMonoClassDisplayName(IntPtr classPtr)
        {
            if (classPtr == IntPtr.Zero || auraMonoClassGetName == null)
            {
                return string.Empty;
            }

            // Class pointers are stable for the lifetime of the runtime, and this is called
            // once per component during entity scans (thousands of times with the same types).
            // Cache the marshalled name to avoid repeated native string marshalling.
            if (this.auraMonoClassDisplayNameCache.TryGetValue(classPtr, out string cached))
            {
                return cached;
            }

            string result;
            try
            {
                string className = Marshal.PtrToStringAnsi(auraMonoClassGetName(classPtr)) ?? string.Empty;
                if (string.IsNullOrEmpty(className))
                {
                    result = string.Empty;
                }
                else
                {
                    string nameSpace = auraMonoClassGetNamespace != null
                        ? (Marshal.PtrToStringAnsi(auraMonoClassGetNamespace(classPtr)) ?? string.Empty)
                        : string.Empty;

                    result = string.IsNullOrEmpty(nameSpace) ? className : (nameSpace + "." + className);
                }
            }
            catch
            {
                result = string.Empty;
            }

            this.auraMonoClassDisplayNameCache[classPtr] = result;
            return result;
        }

        private bool IsAuraMonoClassAssignableTo(IntPtr classPtr, IntPtr targetClassPtr)
        {
            if (classPtr == IntPtr.Zero || targetClassPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr current = classPtr;
            while (current != IntPtr.Zero)
            {
                if (current == targetClassPtr)
                {
                    return true;
                }

                current = auraMonoClassGetParent != null ? auraMonoClassGetParent(current) : IntPtr.Zero;
            }

            return false;
        }

        private IntPtr FindAuraMonoMethodOnHierarchy(IntPtr classPtr, string methodName, int paramCount)
        {
            if (classPtr == IntPtr.Zero || string.IsNullOrEmpty(methodName) || auraMonoClassGetMethodFromName == null)
            {
                return IntPtr.Zero;
            }

            AuraMonoMethodCacheKey cacheKey = new AuraMonoMethodCacheKey(classPtr, methodName, paramCount);
            if (this.auraMonoMethodLookupCache.TryGetValue(cacheKey, out IntPtr cachedMethod))
            {
                return cachedMethod;
            }

            IntPtr current = classPtr;
            while (current != IntPtr.Zero)
            {
                IntPtr method = auraMonoClassGetMethodFromName(current, methodName, paramCount);
                if (method != IntPtr.Zero)
                {
                    this.auraMonoMethodLookupCache[cacheKey] = method;
                    return method;
                }

                current = auraMonoClassGetParent != null ? auraMonoClassGetParent(current) : IntPtr.Zero;
            }

            this.auraMonoMethodLookupCache[cacheKey] = IntPtr.Zero;
            return IntPtr.Zero;
        }

        private IntPtr FindAuraMonoFieldOnHierarchy(IntPtr classPtr, string fieldName)
        {
            if (classPtr == IntPtr.Zero || string.IsNullOrEmpty(fieldName) || auraMonoClassGetFieldFromName == null)
            {
                return IntPtr.Zero;
            }

            AuraMonoFieldCacheKey cacheKey = new AuraMonoFieldCacheKey(classPtr, fieldName);
            if (this.auraMonoFieldLookupCache.TryGetValue(cacheKey, out IntPtr cachedField))
            {
                return cachedField;
            }

            IntPtr current = classPtr;
            while (current != IntPtr.Zero)
            {
                IntPtr field = auraMonoClassGetFieldFromName(current, fieldName);
                if (field != IntPtr.Zero)
                {
                    this.auraMonoFieldLookupCache[cacheKey] = field;
                    return field;
                }

                current = auraMonoClassGetParent != null ? auraMonoClassGetParent(current) : IntPtr.Zero;
            }

            this.auraMonoFieldLookupCache[cacheKey] = IntPtr.Zero;
            return IntPtr.Zero;
        }

        private bool TryInvokeAuraMonoZeroArg(IntPtr obj, out IntPtr result, params string[] methodNames)
        {
            result = IntPtr.Zero;
            if (obj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                result = auraMonoRuntimeInvoke(method, obj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && result != IntPtr.Zero)
                {
                    return true;
                }
            }

            result = IntPtr.Zero;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoUInt64Arg(IntPtr obj, ulong argValue, out IntPtr result, params string[] methodNames)
        {
            result = IntPtr.Zero;
            if (obj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&argValue);
                result = auraMonoRuntimeInvoke(method, obj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && result != IntPtr.Zero)
                {
                    return true;
                }
            }

            result = IntPtr.Zero;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoIntArg(IntPtr obj, int argValue, out IntPtr result, params string[] methodNames)
        {
            result = IntPtr.Zero;
            if (obj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                int localArg = argValue;
                args[0] = (IntPtr)(&localArg);
                result = auraMonoRuntimeInvoke(method, obj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && result != IntPtr.Zero)
                {
                    return true;
                }
            }

            result = IntPtr.Zero;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoIntArgReturningBool(IntPtr obj, int argValue, out bool result, params string[] methodNames)
        {
            result = false;
            if (obj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                int localArg = argValue;
                args[0] = (IntPtr)(&localArg);
                IntPtr boxed = auraMonoRuntimeInvoke(method, obj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out result))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryReadAuraMonoObjectField(IntPtr obj, out IntPtr valueObj, params string[] fieldNames)
        {
            valueObj = IntPtr.Zero;
            if (obj == IntPtr.Zero || fieldNames == null || fieldNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string fieldName in fieldNames)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(classPtr, fieldName);
                if (field == IntPtr.Zero)
                {
                    continue;
                }

                valueObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, obj);
                if (valueObj != IntPtr.Zero)
                {
                    return true;
                }
            }

            valueObj = IntPtr.Zero;
            return false;
        }

        private uint TryReadAuraMonoUIntField(IntPtr obj, params string[] fieldNames)
        {
            if (obj == IntPtr.Zero || fieldNames == null || fieldNames.Length == 0 || auraMonoFieldGetValueObject == null || auraMonoObjectGetClass == null)
            {
                return 0U;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return 0U;
            }

            foreach (string fieldName in fieldNames)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(classPtr, fieldName);
                if (field == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr boxed = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, obj);
                if (boxed == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryUnboxMonoUInt32(boxed, out uint value))
                {
                    return value;
                }

                ulong fallback = this.TryReadMonoUnsignedIntegral(boxed);
                if (fallback > 0UL && fallback <= uint.MaxValue)
                {
                    return (uint)fallback;
                }
            }

            return 0U;
        }

        private unsafe Vector3 TryReadAuraMonoVector3Field(IntPtr obj, params string[] fieldNames)
        {
            if (obj == IntPtr.Zero || fieldNames == null || fieldNames.Length == 0 || auraMonoFieldGetValueObject == null || auraMonoObjectUnbox == null || auraMonoObjectGetClass == null)
            {
                return Vector3.zero;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return Vector3.zero;
            }

            foreach (string fieldName in fieldNames)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(classPtr, fieldName);
                if (field == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr boxed = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, obj);
                if (boxed == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    continue;
                }

                return *(Vector3*)raw;
            }

            return Vector3.zero;
        }

        private bool TryInvokeAuraMonoBoolGetter(IntPtr targetObj, IntPtr targetClass, out bool value, params string[] methodNames)
        {
            value = false;
            if (targetObj == IntPtr.Zero || targetClass == IntPtr.Zero || methodNames == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            for (int i = 0; i < methodNames.Length; i++)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(targetClass, methodNames[i], 0);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(method, targetObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private int TryGetAuraMonoMethodParamCount(IntPtr methodPtr)
        {
            try
            {
                if (methodPtr == IntPtr.Zero || auraMonoMethodSignature == null || auraMonoSignatureGetParamCount == null)
                {
                    return 0;
                }

                IntPtr signature = auraMonoMethodSignature(methodPtr);
                if (signature == IntPtr.Zero)
                {
                    return 0;
                }

                return unchecked((int)auraMonoSignatureGetParamCount(signature));
            }
            catch
            {
                return 0;
            }
        }

        private unsafe bool TryInvokeAuraMonoVector2Arg(IntPtr obj, string methodName, Vector2 argValue, out string status)
        {
            status = "Aura mono Vector2 invoke unavailable";

            try
            {
                if (obj == IntPtr.Zero || string.IsNullOrEmpty(methodName) || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "Aura mono Vector2 runtime unavailable";
                    return false;
                }

                IntPtr classPtr = auraMonoObjectGetClass(obj);
                if (classPtr == IntPtr.Zero)
                {
                    status = "Aura mono Vector2 class unavailable";
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "Aura mono Vector2 method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                Vector2 value = argValue;
                args[0] = (IntPtr)(&value);
                auraMonoRuntimeInvoke(methodPtr, obj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Aura mono Vector2 exception";
                    return false;
                }

                status = "OK";
                return true;
            }
            catch (Exception ex)
            {
                status = "Aura mono Vector2 failed: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticZeroArgMethod(string fullTypeName, string methodName, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "()";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "()");
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura zero-arg invoke exception: " + ex);
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticIntIntMethod(string fullTypeName, string methodName, int firstArg, int secondArg, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 2);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "(2)";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "(" + firstArg + ", " + secondArg + ")");
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                int firstValue = firstArg;
                int secondValue = secondArg;
                args[0] = (IntPtr)(&firstValue);
                args[1] = (IntPtr)(&secondValue);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura int-int invoke exception: " + ex);
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticStringBoolMethod(string fullTypeName, string methodName, string stringArg, bool boolArg, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null || auraMonoStringNew == null || this.auraMonoRootDomain == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 2);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "(2)";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "(string,bool)");
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                IntPtr stringObj = string.IsNullOrEmpty(stringArg) ? IntPtr.Zero : auraMonoStringNew(this.auraMonoRootDomain, stringArg);
                bool boolValue = boolArg;
                args[0] = stringObj;
                args[1] = (IntPtr)(&boolValue);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura string-bool invoke exception: " + ex);
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticIntMethod(string fullTypeName, string methodName, int arg, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "(1)";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "(" + arg + ")");
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                int value = arg;
                args[0] = (IntPtr)(&value);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura int invoke exception: " + ex);
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticNullBoolMethod(string fullTypeName, string methodName, bool boolArg, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 2);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "(2)";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "(null, " + boolArg.ToString() + ")");
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                bool boolValue = boolArg;
                args[0] = IntPtr.Zero;
                args[1] = (IntPtr)(&boolValue);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura null-bool invoke exception: " + ex);
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoStaticIntIntIntBoolMethod(string fullTypeName, string methodName, int firstArg, int secondArg, int thirdArg, bool boolArg, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura class not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 4);
                if (methodPtr == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura method not found: " + fullTypeName + "." + methodName + "(4)";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura static method: " + this.GetAuraMonoClassDisplayName(classPtr) + "." + methodName + "(" + firstArg + ", " + secondArg + ", " + thirdArg + ", " + boolArg.ToString() + ")");
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[4];
                int firstValue = firstArg;
                int secondValue = secondArg;
                int thirdValue = thirdArg;
                bool boolValue = boolArg;
                args[0] = (IntPtr)(&firstValue);
                args[1] = (IntPtr)(&secondValue);
                args[2] = (IntPtr)(&thirdValue);
                args[3] = (IntPtr)(&boolValue);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura invoke exception: " + fullTypeName + "." + methodName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura invoke failed: " + ex.Message;
                this.LogForceOpenShop("Aura int-int-int-bool invoke exception: " + ex);
                return false;
            }
        }

        private bool TryAuraMonoDictionaryGetIntValue(IntPtr dictObj, int key, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            return dictObj != IntPtr.Zero && key > 0 && this.TryAuraMonoDictionaryContainsIntKey(dictObj, key);
        }

        private bool TryGetAuraMonoDictionaryEntryIntKey(IntPtr entryObj, out int keyInt, out IntPtr valueObj)
        {
            keyInt = 0;
            valueObj = IntPtr.Zero;
            if (entryObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr keyObj = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(entryObj, "Key", out keyObj) || keyObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(entryObj, "key", out keyObj);
            }

            if (keyObj != IntPtr.Zero)
            {
                // The key box is fresh unpinned garbage and the probes below allocate between
                // reads — pin it so a mid-sequence moving-GC pass can't relocate it.
                uint keyPin = AuraMonoPinNew(keyObj);
                try
                {
                    if (!this.TryGetMonoInt32Member(keyObj, "m_value", out keyInt))
                    {
                        this.TryGetMonoIntMember(keyObj, "m_value", out keyInt);
                    }

                    if (keyInt <= 0)
                    {
                        this.TryGetMonoInt32Member(keyObj, "value__", out keyInt);
                    }

                    if (keyInt <= 0 && auraMonoObjectUnbox != null)
                    {
                        try
                        {
                            IntPtr unboxed = auraMonoObjectUnbox(keyObj);
                            if (unboxed != IntPtr.Zero)
                            {
                                keyInt = Marshal.ReadInt32(unboxed);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                finally
                {
                    AuraMonoPinFree(keyPin);
                }
            }

            if (!this.TryGetMonoObjectMember(entryObj, "Value", out valueObj) || valueObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(entryObj, "value", out valueObj);
            }

            return keyInt != 0 || valueObj != IntPtr.Zero;
        }

        private bool TryBoxAuraMonoInt32(int value, out IntPtr boxedObj)
        {
            boxedObj = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectNew == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            if (this.autoSellMonoInt32ClassPtr == IntPtr.Zero)
            {
                IntPtr coreImage = this.FindAuraMonoImage(new[] { "mscorlib", "mscorlib.dll", "System.Private.CoreLib", "System.Private.CoreLib.dll" });
                if (coreImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    this.autoSellMonoInt32ClassPtr = auraMonoClassFromName(coreImage, "System", "Int32");
                }
            }

            if (this.autoSellMonoInt32ClassPtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                boxedObj = auraMonoObjectNew(this.auraMonoRootDomain, this.autoSellMonoInt32ClassPtr);
                if (boxedObj == IntPtr.Zero)
                {
                    return false;
                }

                if (auraMonoRuntimeObjectInit != null)
                {
                    auraMonoRuntimeObjectInit(boxedObj);
                }

                IntPtr valueField = this.FindAuraMonoFieldOnHierarchy(this.autoSellMonoInt32ClassPtr, "m_value");
                if (valueField == IntPtr.Zero)
                {
                    return false;
                }

                unsafe
                {
                    int localValue = value;
                    auraMonoFieldSetValue(boxedObj, valueField, (IntPtr)(&localValue));
                }

                return true;
            }
            catch
            {
                boxedObj = IntPtr.Zero;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoObjectIntArg(IntPtr obj, IntPtr argObj, out IntPtr result, params string[] methodNames)
        {
            result = IntPtr.Zero;
            if (obj == IntPtr.Zero || argObj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                result = auraMonoRuntimeInvoke(method, obj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && result != IntPtr.Zero)
                {
                    return true;
                }
            }

            result = IntPtr.Zero;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoObjectIntArgReturningBool(IntPtr obj, IntPtr argObj, out bool result, params string[] methodNames)
        {
            result = false;
            if (obj == IntPtr.Zero || argObj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 1);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                IntPtr boxed = auraMonoRuntimeInvoke(method, obj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out result))
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryInvokeAuraMonoStaticTwoIntMethod(IntPtr methodPtr, int firstArg, int secondArg, out IntPtr resultObj)
        {
            resultObj = IntPtr.Zero;
            if (methodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            int localFirst = firstArg;
            int localSecond = secondArg;
            args[0] = (IntPtr)(&localFirst);
            args[1] = (IntPtr)(&localSecond);
            resultObj = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && resultObj != IntPtr.Zero;
        }

        private bool TryAuraMonoDictionaryContainsIntKey(IntPtr dictObj, int key)
        {
            if (dictObj == IntPtr.Zero || key <= 0)
            {
                return false;
            }

            if (this.TryBoxAuraMonoInt32(key, out IntPtr boxedKey)
                && this.TryInvokeAuraMonoObjectIntArgReturningBool(dictObj, boxedKey, out bool boxedContains, "ContainsKey", "Contains")
                && boxedContains)
            {
                return true;
            }

            return this.TryInvokeAuraMonoIntArgReturningBool(dictObj, key, out bool contains, "ContainsKey", "Contains")
                && contains;
        }

        private IntPtr FindAuraMonoClassInAllLoadedImages(string className, string nameSpace)
        {
            if (!this.auraMonoApiReady || auraMonoAssemblyForeach == null || string.IsNullOrWhiteSpace(className))
            {
                return IntPtr.Zero;
            }

            autoSellAuraMonoSearchHost = this;
            autoSellAuraMonoSearchClass = className;
            autoSellAuraMonoSearchNamespace = nameSpace ?? string.Empty;
            autoSellAuraMonoSearchResult = IntPtr.Zero;
            try
            {
                auraMonoAssemblyForeach(AutoSellAuraMonoAssemblySearchCallback, IntPtr.Zero);
            }
            catch
            {
            }
            finally
            {
                autoSellAuraMonoSearchHost = null;
            }

            return autoSellAuraMonoSearchResult;
        }

        // On success dictPin is a pinned gchandle rooting dictObj — the Add/set_Item invokes below
        // and the caller's protocol invoke all allocate mono-side, so an unpinned dictionary could
        // be moved by the sgen GC mid-use. The caller MUST AuraMonoPinFree(dictPin) when done.
        private unsafe bool TryCreateAuraMonoUIntIntDictionary(Dictionary<uint, int> itemsToSell, out IntPtr dictObj, out uint dictPin)
        {
            dictObj = IntPtr.Zero;
            dictPin = 0U;
            if (itemsToSell == null || itemsToSell.Count == 0)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoStringNew == null
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            string[] typeCandidates = new[]
            {
                "System.Collections.Generic.Dictionary`2[System.UInt32,System.Int32]",
                "System.Collections.Generic.Dictionary`2[[System.UInt32, mscorlib],[System.Int32, mscorlib]]"
            };

            for (int i = 0; i < typeCandidates.Length && dictObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, typeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* typeArgs = stackalloc IntPtr[1];
                typeArgs[0] = typeNameObj;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                exc = IntPtr.Zero;
                IntPtr* createArgs = stackalloc IntPtr[1];
                createArgs[0] = typeObj;
                dictObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    dictObj = IntPtr.Zero;
                }
            }

            if (dictObj == IntPtr.Zero)
            {
                return false;
            }

            dictPin = AuraMonoPinNew(dictObj);

            IntPtr dictClass = this.autoSellMonoUIntIntDictionaryClass;
            if (dictClass == IntPtr.Zero)
            {
                dictClass = auraMonoObjectGetClass(dictObj);
                this.autoSellMonoUIntIntDictionaryClass = dictClass;
            }

            IntPtr setItemMethod = this.autoSellMonoUIntIntDictionarySetItemMethod;
            if (setItemMethod == IntPtr.Zero && dictClass != IntPtr.Zero)
            {
                setItemMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "set_Item", 2);
                if (setItemMethod == IntPtr.Zero)
                {
                    setItemMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "Add", 2);
                }
                this.autoSellMonoUIntIntDictionarySetItemMethod = setItemMethod;
            }
            if (setItemMethod == IntPtr.Zero)
            {
                AuraMonoPinFree(dictPin);
                dictPin = 0U;
                dictObj = IntPtr.Zero;
                return false;
            }

            foreach (KeyValuePair<uint, int> entry in itemsToSell)
            {
                if (entry.Key == 0U || entry.Value <= 0)
                {
                    continue;
                }

                uint netId = entry.Key;
                int count = entry.Value;
                IntPtr exc = IntPtr.Zero;
                IntPtr* setArgs = stackalloc IntPtr[2];
                setArgs[0] = (IntPtr)(&netId);
                setArgs[1] = (IntPtr)(&count);
                auraMonoRuntimeInvoke(setItemMethod, dictObj, (IntPtr)setArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.AutoSellLog("AuraMono dictionary set failed for netId=" + netId + " ptr=0x" + exc.ToInt64().ToString("X"));
                    AuraMonoPinFree(dictPin);
                    dictPin = 0U;
                    dictObj = IntPtr.Zero;
                    return false;
                }
            }

            return true;
        }

        private unsafe bool TryInvokeAuraMonoZeroArgInt(IntPtr obj, out int value, params string[] methodNames)
        {
            value = 0;
            if (!this.TryInvokeAuraMonoZeroArg(obj, out IntPtr boxed, methodNames) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
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

        private bool TryResolveAuraMonoModule(string fullTypeName, out IntPtr moduleObj)
        {
            moduleObj = IntPtr.Zero;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] Mono API not ready.");
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                this.DirectBackpackVerboseLog("[DirectBackpackMono] Resolve module " + fullTypeName + ". class=0x" + classPtr.ToString("X"));
                if (classPtr != IntPtr.Zero)
                {
                    foreach (string getterName in new[] { "get_Instance", "get_instance", "Instance", "GetInstance" })
                    {
                        IntPtr getter = this.FindAuraMonoMethodOnHierarchy(classPtr, getterName, 0);
                        if (getter == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr exc = IntPtr.Zero;
                        moduleObj = auraMonoRuntimeInvoke(getter, IntPtr.Zero, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && moduleObj != IntPtr.Zero)
                        {
                            // A module handed back by a real invoke proves the game's Mono side is
                            // up, which is what unlocks the raw static-field reads below/elsewhere.
                            MarkAuraMonoGameDataLive();
                            this.DirectBackpackVerboseLog("[DirectBackpackMono] Resolved " + fullTypeName + " via " + getterName + ".");
                            return true;
                        }
                    }

                    foreach (string fieldName in new[] { "Instance", "instance", "_instance", "_manager", "manager" })
                    {
                        if (this.TryGetAuraMonoStaticObjectField(classPtr, fieldName, out moduleObj) && moduleObj != IntPtr.Zero)
                        {
                            this.DirectBackpackVerboseLog("[DirectBackpackMono] Resolved " + fullTypeName + " via static field " + fieldName + ".");
                            return true;
                        }
                    }
                }

                return this.TryResolveAuraMonoModuleFromManagers(fullTypeName, out moduleObj);
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Module resolve error for " + fullTypeName + ": " + ex.Message);
                moduleObj = IntPtr.Zero;
                return false;
            }
        }

        private bool TryResolveAuraMonoModuleFromManagers(string fullTypeName, out IntPtr moduleObj)
        {
            moduleObj = IntPtr.Zero;
            IntPtr managersClass = IntPtr.Zero;
            foreach (string managerTypeName in new[]
            {
                "Managers",
                "XDTGame.Framework.Managers",
                "XDTGame.Core.Managers",
                "XDFramework.Core.Managers",
                "XDTLevelAndEntity.Managers",
                "XDTLevelAndEntity.Core.Managers"
            })
            {
                managersClass = this.FindAuraMonoClassByFullName(managerTypeName);
                if (managersClass != IntPtr.Zero)
                {
                    this.DirectBackpackVerboseLog("[DirectBackpackMono] Managers class resolved as " + managerTypeName + ".");
                    break;
                }
            }
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "Managers");
                if (managersClass != IntPtr.Zero)
                {
                    this.DirectBackpackVerboseLog("[DirectBackpackMono] Managers class resolved by loaded assembly sweep.");
                }
            }
            if (managersClass == IntPtr.Zero)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Managers class unavailable.");
                return false;
            }

            if (this.TryResolveAuraMonoModuleViaManagersGetModule(managersClass, fullTypeName, out moduleObj) && moduleObj != IntPtr.Zero)
            {
                return true;
            }

            IntPtr moduleDicObj = IntPtr.Zero;
            foreach (string fieldName in new[] { "_moduleDic", "moduleDic", "s_moduleDic", "_modules", "modules" })
            {
                if (this.TryGetAuraMonoStaticObjectField(managersClass, fieldName, out moduleDicObj) && moduleDicObj != IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] Managers module dictionary resolved via field " + fieldName + ".");
                    break;
                }
            }
            if (moduleDicObj == IntPtr.Zero)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Managers._moduleDic unavailable for " + fullTypeName);
                return false;
            }

            if (!this.TryGetMonoObjectMember(moduleDicObj, "Values", out IntPtr valuesObj) || valuesObj == IntPtr.Zero)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Managers._moduleDic.Values unavailable for " + fullTypeName);
                return false;
            }

            List<IntPtr> modules = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(valuesObj, modules) || modules.Count == 0)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Managers._moduleDic empty/unreadable for " + fullTypeName);
                return false;
            }
            this.DirectBackpackVerboseLog("[DirectBackpackMono] Managers modules read. count=" + modules.Count + " target=" + fullTypeName);

            string typeName = fullTypeName.Split('.').Last();
            foreach (IntPtr candidate in modules)
            {
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(candidate));
                if (!string.IsNullOrEmpty(className) && className.EndsWith(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    moduleObj = candidate;
                    this.DirectBackpackVerboseLog("[DirectBackpackMono] Module matched in Managers: " + className);
                    return true;
                }
            }

            this.AutoEatRepairLog("[DirectBackpackMono] Module not found in Managers._moduleDic for " + fullTypeName);
            return false;
        }

        private unsafe bool TryResolveAuraMonoModuleViaManagersGetModule(IntPtr managersClass, string fullTypeName, out IntPtr moduleObj)
        {
            moduleObj = IntPtr.Zero;
            try
            {
                this.ResolveAuraFarmRuntimeMethodsViaMono();
                if (managersClass == IntPtr.Zero || string.IsNullOrWhiteSpace(fullTypeName) || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] Managers.GetModule(Type) prerequisites missing. managers=0x" + managersClass.ToString("X") + " runtimeInvoke=" + (auraMonoRuntimeInvoke != null) + " stringNew=" + (auraMonoStringNew != null) + " typeGet=0x" + this.auraMonoTypeGetTypeMethodPtr.ToString("X"));
                    return false;
                }

                IntPtr getModuleMethod = this.FindAuraMonoMethodOnHierarchy(managersClass, "GetModule", 1);
                if (getModuleMethod == IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] Managers.GetModule(Type) unavailable.");
                    return false;
                }

                if (!this.TryCreateAuraMonoSystemTypeObject(fullTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] System.Type object unavailable for " + fullTypeName);
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeObj;
                IntPtr exc = IntPtr.Zero;
                moduleObj = auraMonoRuntimeInvoke(getModuleMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || moduleObj == IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] Managers.GetModule(Type) returned null for " + fullTypeName);
                    moduleObj = IntPtr.Zero;
                    return false;
                }

                // Same liveness proof as the get_Instance path: this came back from a genuine
                // mono_runtime_invoke, so the game is loaded and static-field reads are safe again.
                MarkAuraMonoGameDataLive();
                this.DirectBackpackVerboseLog("[DirectBackpackMono] Resolved " + fullTypeName + " via Managers.GetModule(Type).");
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Managers.GetModule(Type) exception for " + fullTypeName + ": " + ex.Message);
                moduleObj = IntPtr.Zero;
                return false;
            }
        }

        // Every embedded-Mono game image, for the exhaustive class sweep below. Some game types live
        // in an image whose name != their namespace (XDTGUI.Module.Build.BuildModule and
        // XDTGUI.Module.Track.TrackModule compile into XDTLevelAndEntity/XDTGameUI; XDTGame.Core.UIManager
        // into XDTGameUI), so a single namespace-picked image is not enough — probe them all natively.
        private static readonly string[] AuraMonoAllGameImageNames =
        {
            "XDTGameUI", "XDTGameUI.dll",
            "XDTGameSystem", "XDTGameSystem.dll",
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "XDTBaseService", "XDTBaseService.dll",
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
            "EcsClient", "EcsClient.dll",
            "EcsSystem", "EcsSystem.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // Whether a name is safe to feed to System.Type.GetType(string) via mono_runtime_invoke.
        // That icall (mono icall.c internal_from_name) only resolves BCL/corlib names without an
        // image-specific lookup; a GAME image name crashes it (see TryCreateAuraMonoSystemTypeObject).
        // corlib namespaces are System.* / Mono.* / Microsoft.* — matches AuraFarm's framework prefixes.
        private static bool IsBclSafeMonoTypeName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return false;
            }

            string trimmed = fullTypeName.TrimStart();
            return trimmed.StartsWith("System.", StringComparison.Ordinal)
                || trimmed.Equals("System", StringComparison.Ordinal)
                || trimmed.StartsWith("Mono.", StringComparison.Ordinal)
                || trimmed.StartsWith("Microsoft.", StringComparison.Ordinal);
        }

        private unsafe bool TryCreateAuraMonoSystemTypeObject(string fullTypeName, out IntPtr typeObj)
        {
            typeObj = IntPtr.Zero;
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return false;
            }

            // Primary route for GAME types: resolve the mono class (now sweeping every game image,
            // incl. namespace != assembly cases) and convert class -> System.Type. This stays on the
            // safe mono_class_get_type -> mono_type_get_object path and never touches Type.GetType.
            if (this.TryCreateAuraMonoSystemTypeObjectFromClass(fullTypeName, out typeObj) && typeObj != IntPtr.Zero)
            {
                return true;
            }

            // Name-based fallback: System.Type.GetType(string) via mono_runtime_invoke. This icall
            // (mono icall.c internal_from_name) HARD-CRASHES the process for any name the root domain
            // cannot resolve without an image-specific lookup — i.e. every game type. Two confirmed
            // process kills went through here: XDTGUI.Module.Build.BuildModule (Jun 2026, PadBuild) and
            // XDTGUI.Module.Track.TrackModule (2026-07-09, coreclr WER dump, main-thread
            // ExecutionEngineException). It only resolves BCL/corlib names safely (proven by the
            // List`1[System.UInt64] / Dictionary`2 construction paths). So gate it: BCL-rooted names
            // only. Every game type must have resolved via the class route above, or fail closed here —
            // never crash. See memory auramono-viewmodule-resolve-typecrash + PadBuildHotkeyFeature.cs.
            if (!IsBclSafeMonoTypeName(fullTypeName)
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr typeNameObj = IntPtr.Zero;
            try
            {
                typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, fullTypeName);
                if (typeNameObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr candidateTypeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && candidateTypeObj != IntPtr.Zero)
                {
                    typeObj = candidateTypeObj;
                    this.DirectBackpackVerboseLog("[DirectBackpackMono] Type object resolved (BCL name): " + fullTypeName);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryCreateAuraMonoSystemTypeObjectFromClass(string fullTypeName, out IntPtr typeObj)
        {
            typeObj = IntPtr.Zero;
            try
            {
                if (auraMonoClassGetType == null || auraMonoTypeGetObject == null || this.auraMonoRootDomain == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName(fullTypeName);
                if (classPtr == IntPtr.Zero)
                {
                    // FindAuraMonoClassByFullName probes only the first *loaded* likely image plus the
                    // unreliable Il2CppMonoGame.MonoHost reflection sweep, so it misses types whose
                    // namespace != assembly image (BuildModule/TrackModule/UIManager). Sweep every game
                    // image natively before giving up — keeps the safe class->Type route so the caller
                    // does not fall through to the crashing Type.GetType icall.
                    classPtr = this.FindAuraMonoClassByFullNameExhaustive(fullTypeName);
                }
                if (classPtr == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr monoType = auraMonoClassGetType(classPtr);
                if (monoType == IntPtr.Zero)
                {
                    return false;
                }

                typeObj = auraMonoTypeGetObject(this.auraMonoRootDomain, monoType);
                if (typeObj == IntPtr.Zero)
                {
                    return false;
                }

                this.DirectBackpackVerboseLog("[DirectBackpackMono] Type object resolved from class " + fullTypeName + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Type object class conversion failed for " + fullTypeName + ": " + ex.Message);
                typeObj = IntPtr.Zero;
                return false;
            }
        }

        // Exhaustive, reliable class lookup: split the full name and probe every game image natively
        // (auraMonoClassFromName per image). Used when FindAuraMonoClassByFullName misses a type whose
        // namespace differs from its assembly image. This replaces the old Type.GetType(string) name
        // fallback, which crashed the runtime for game types (see TryCreateAuraMonoSystemTypeObject).
        private IntPtr FindAuraMonoClassByFullNameExhaustive(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return IntPtr.Zero;
            }

            int lastDot = fullTypeName.LastIndexOf('.');
            string ns = lastDot > 0 ? fullTypeName.Substring(0, lastDot) : string.Empty;
            string name = lastDot > 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
            return this.FindAuraMonoClassInImages(ns, name, AuraMonoAllGameImageNames);
        }

        private IntPtr FindAuraMonoClassByFullName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return IntPtr.Zero;
            }

            int lastDot = fullTypeName.LastIndexOf('.');
            string ns = lastDot > 0 ? fullTypeName.Substring(0, lastDot) : string.Empty;
            string name = lastDot > 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
            IntPtr directClass = this.FindAuraMonoClassInLikelyImages(ns, name);
            if (directClass != IntPtr.Zero)
            {
                return directClass;
            }

            return this.FindAuraMonoClassAcrossLoadedAssemblies(ns, name);
        }

        private IntPtr FindAuraMonoClassInLikelyImages(string nameSpace, string className)
        {
            if (string.IsNullOrWhiteSpace(className) || auraMonoClassFromName == null)
            {
                return IntPtr.Zero;
            }

            string[] imageNames;
            if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTGameSystem", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTGameSystem", "XDTGameSystem.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTGame.UI", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTGameUI", "XDTGameUI.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTLevelAndEntity", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && (nameSpace.StartsWith("XDTGame.Framework", StringComparison.Ordinal) || nameSpace.StartsWith("XDTGame.Core", StringComparison.Ordinal)))
            {
                imageNames = new[] { "XDTBaseService", "XDTBaseService.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDFramework", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTBaseService", "XDTBaseService.dll", "XDFramework", "XDFramework.dll", "Client", "Client.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("EcsClient", StringComparison.Ordinal))
            {
                imageNames = new[] { "EcsClient", "EcsClient.dll", "XDTDataAndProtocol", "XDTDataAndProtocol.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDT.Scene.", StringComparison.Ordinal))
            {
                imageNames = new[] { "EcsClient", "EcsClient.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTDataAndProtocol", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("ScriptsRefactory.DataAndProtocol", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTBaseService", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTBaseService", "XDTBaseService.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("XDTGUI.Module", StringComparison.Ordinal))
            {
                imageNames = new[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "XDTGameUI", "XDTGameUI.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" };
            }
            else if (!string.IsNullOrEmpty(nameSpace) && nameSpace.StartsWith("ClientSystem", StringComparison.Ordinal))
            {
                imageNames = new[] { "EcsSystem", "EcsSystem.dll", "Client", "Client.dll", "XDTDataAndProtocol", "XDTDataAndProtocol.dll" };
            }
            else
            {
                imageNames = new[]
                {
                    "XDTGameUI", "XDTGameUI.dll",
                    "XDTGameSystem", "XDTGameSystem.dll",
                    "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
                    "XDTBaseService", "XDTBaseService.dll",
                    "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
                    "EcsClient", "EcsClient.dll",
                    "Client", "Client.dll",
                    "Assembly-CSharp", "Assembly-CSharp.dll"
                };
            }

            IntPtr image = this.FindAuraMonoImage(imageNames);
            if (image == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                return auraMonoClassFromName(image, nameSpace ?? string.Empty, className);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr FindAuraMonoClassInImages(string nameSpace, string className, string[] imageNames)
        {
            if (string.IsNullOrWhiteSpace(className) || auraMonoClassFromName == null || imageNames == null)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < imageNames.Length; i++)
            {
                string imageName = imageNames[i];
                if (string.IsNullOrWhiteSpace(imageName))
                {
                    continue;
                }

                IntPtr image = this.FindAuraMonoImage(new string[] { imageName });
                if (image == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    IntPtr classPtr = auraMonoClassFromName(image, nameSpace ?? string.Empty, className);
                    if (classPtr != IntPtr.Zero)
                    {
                        return classPtr;
                    }
                }
                catch
                {
                }
            }

            return IntPtr.Zero;
        }

    }
}
