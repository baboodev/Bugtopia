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
        private bool auraMonoApiReady = false;

        private IntPtr auraMonoRootDomain = IntPtr.Zero;

        private int auraMonoAttachedManagedThreadId = int.MinValue;

        private IntPtr auraMonoAttachedDomain = IntPtr.Zero;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoGetRootDomainDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoThreadAttachDelegate(IntPtr domain);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoImageLoadedDelegate([MarshalAs(UnmanagedType.LPStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoAssemblyForeachDelegate(MonoAssemblyForeachCallbackDelegate callback, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoAssemblyForeachCallbackDelegate(IntPtr assembly, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoAssemblyGetImageDelegate(IntPtr assembly);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoImageGetNameDelegate(IntPtr image);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassFromNameDelegate(IntPtr image, [MarshalAs(UnmanagedType.LPStr)] string nameSpace, [MarshalAs(UnmanagedType.LPStr)] string className);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetMethodFromNameDelegate(IntPtr klass, [MarshalAs(UnmanagedType.LPStr)] string name, int paramCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetFieldFromNameDelegate(IntPtr klass, [MarshalAs(UnmanagedType.LPStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldGetValueDelegate(IntPtr obj, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoFieldGetValueObjectDelegate(IntPtr domain, IntPtr field, IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldSetValueDelegate(IntPtr obj, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoRuntimeInvokeDelegate(IntPtr method, IntPtr obj, IntPtr parameters, ref IntPtr exc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectUnboxDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoStringNewDelegate(IntPtr domain, [MarshalAs(UnmanagedType.LPStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoStringToUtf8Delegate(IntPtr monoString);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFreeDelegate(IntPtr memory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectGetClassDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MonoClassIsValueTypeDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetTypeDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassBindGenericParametersDelegate(IntPtr klass, int paramType, IntPtr types, int typesLen);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonoGenericContext
        {
            public IntPtr class_inst;
            public IntPtr method_inst;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassInflateGenericMethodDelegate(IntPtr method, ref MonoGenericContext context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMetadataGetGenericInstDelegate(int typeArgc, IntPtr typeArgv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoTypeGetObjectDelegate(IntPtr domain, IntPtr type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetParentDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MonoClassGetRankDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoGcVoidDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MonoGcHandleNewDelegate(IntPtr obj, [MarshalAs(UnmanagedType.Bool)] bool pinned);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoGcHandleGetTargetDelegate(uint handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoGcHandleFreeDelegate(uint handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetElementClassDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetNameDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetNamespaceDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetMethodsDelegate(IntPtr klass, ref IntPtr iter);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetNameDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetClassDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetFieldsDelegate(IntPtr klass, ref IntPtr iter);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoFieldGetNameDelegate(IntPtr field);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodSignatureDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MonoSignatureGetParamCountDelegate(IntPtr signature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr MonoArrayLengthDelegate(IntPtr array);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoArrayAddrWithSizeDelegate(IntPtr array, int size, UIntPtr index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoArrayNewDelegate(IntPtr domain, IntPtr eclass, UIntPtr n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MonoArrayElementSizeDelegate(IntPtr arrayClass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MonoFieldGetOffsetDelegate(IntPtr field);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassVtableDelegate(IntPtr domain, IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldStaticGetValueDelegate(IntPtr vtable, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectNewDelegate(IntPtr domain, IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoRuntimeObjectInitDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoCompileMethodDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetUnmanagedThunkDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetUnmanagedDelegate(IntPtr method);

        private static MonoGetRootDomainDelegate auraMonoGetRootDomain;

        private static MonoThreadAttachDelegate auraMonoThreadAttach;

        private static MonoImageLoadedDelegate auraMonoImageLoaded;

        private static MonoAssemblyForeachDelegate auraMonoAssemblyForeach;

        private static MonoAssemblyGetImageDelegate auraMonoAssemblyGetImage;

        private static MonoImageGetNameDelegate auraMonoImageGetName;

        private static MonoClassFromNameDelegate auraMonoClassFromName;

        private static MonoClassGetMethodFromNameDelegate auraMonoClassGetMethodFromName;

        private static MonoClassGetFieldFromNameDelegate auraMonoClassGetFieldFromName;

        private static MonoFieldGetValueDelegate auraMonoFieldGetValue;

        private static MonoFieldGetValueObjectDelegate auraMonoFieldGetValueObject;

        private static MonoFieldSetValueDelegate auraMonoFieldSetValue;

        private static MonoRuntimeInvokeDelegate auraMonoRuntimeInvoke;

        private static MonoRuntimeInvokeDelegate auraMonoRuntimeInvokeRaw;

        private static MonoObjectUnboxDelegate auraMonoObjectUnbox;

        private static MonoStringNewDelegate auraMonoStringNew;

        private static MonoStringToUtf8Delegate auraMonoStringToUtf8;

        private static MonoFreeDelegate auraMonoFree;

        private static MonoObjectGetClassDelegate auraMonoObjectGetClass;

        private static MonoClassIsValueTypeDelegate auraMonoClassIsValueType;

        private static MonoClassGetTypeDelegate auraMonoClassGetType;

        private static MonoClassBindGenericParametersDelegate auraMonoClassBindGenericParameters;

        private static MonoClassInflateGenericMethodDelegate auraMonoClassInflateGenericMethod;

        private static MonoMetadataGetGenericInstDelegate auraMonoMetadataGetGenericInst;

        private static MonoTypeGetObjectDelegate auraMonoTypeGetObject;

        private static MonoClassGetParentDelegate auraMonoClassGetParent;

        private static MonoClassGetRankDelegate auraMonoClassGetRank;

        private static MonoGcVoidDelegate auraMonoGcDisable;

        private static MonoGcVoidDelegate auraMonoGcEnable;

        private static MonoGcHandleNewDelegate auraMonoGcHandleNew;

        private static MonoGcHandleGetTargetDelegate auraMonoGcHandleGetTarget;

        private static MonoGcHandleFreeDelegate auraMonoGcHandleFree;

        private static MonoClassGetElementClassDelegate auraMonoClassGetElementClass;

        private static MonoClassGetNameDelegate auraMonoClassGetName;

        private static MonoClassGetNamespaceDelegate auraMonoClassGetNamespace;

        private static MonoClassGetMethodsDelegate auraMonoClassGetMethods;

        private static MonoMethodGetNameDelegate auraMonoMethodGetName;

        private static MonoMethodGetClassDelegate auraMonoMethodGetClass;

        private static MonoClassGetFieldsDelegate auraMonoClassGetFields;

        private static MonoFieldGetNameDelegate auraMonoFieldGetName;

        private static MonoMethodSignatureDelegate auraMonoMethodSignature;

        private static MonoSignatureGetParamCountDelegate auraMonoSignatureGetParamCount;

        private static MonoArrayLengthDelegate auraMonoArrayLength;

        private static MonoArrayAddrWithSizeDelegate auraMonoArrayAddrWithSize;

        private static MonoArrayNewDelegate auraMonoArrayNew;

        private static MonoArrayElementSizeDelegate auraMonoArrayElementSize;

        private static MonoFieldGetOffsetDelegate auraMonoFieldGetOffset;

        private static MonoClassVtableDelegate auraMonoClassVtable;

        private static MonoFieldStaticGetValueDelegate auraMonoFieldStaticGetValue;

        private static MonoObjectNewDelegate auraMonoObjectNew;

        private static MonoRuntimeObjectInitDelegate auraMonoRuntimeObjectInit;

        private static MonoCompileMethodDelegate auraMonoCompileMethod;

        private static MonoMethodGetUnmanagedThunkDelegate auraMonoMethodGetUnmanagedThunk;

        private static MonoMethodGetUnmanagedDelegate auraMonoMethodGetUnmanaged;

        private IntPtr auraMonoTypeGetTypeMethodPtr = IntPtr.Zero;

        private IntPtr auraMonoActivatorCreateInstanceMethodPtr = IntPtr.Zero;

        private IntPtr auraMonoUInt64ClassPtr = IntPtr.Zero;

        private IntPtr auraMonoArrayGetValueMethodPtr = IntPtr.Zero;

        private IntPtr auraMonoUInt64ListCountMethodPtr = IntPtr.Zero;

        private IntPtr auraMonoUInt64ListGetItemMethodPtr = IntPtr.Zero;

        private IntPtr auraMonoUInt64ListClearMethodPtr = IntPtr.Zero;

        internal static int AuraMonoWorldEpoch => auraMonoWorldEpoch;

        private static int auraMonoWorldEpoch = 1;

        private int auraMonoLastSceneHandle = 0;

        private float auraMonoNextSceneEpochCheckAt = 0f;

        private void UpdateAuraMonoWorldEpoch()
        {
            if (Time.unscaledTime < this.auraMonoNextSceneEpochCheckAt)
            {
                return;
            }
            this.auraMonoNextSceneEpochCheckAt = Time.unscaledTime + 0.5f;
            try
            {
                int handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (handle != this.auraMonoLastSceneHandle)
                {
                    bool firstObservation = this.auraMonoLastSceneHandle == 0;
                    this.auraMonoLastSceneHandle = handle;
                    if (!firstObservation)
                    {
                        auraMonoWorldEpoch++;
                        ModLogger.Msg("[AuraMono] Active scene changed — world epoch -> " + auraMonoWorldEpoch + " (cached object pointers will re-resolve).");
                    }
                }
            }
            catch
            {
            }
        }

        internal static uint AuraMonoPinNew(IntPtr obj)
        {
            if (obj == IntPtr.Zero || auraMonoGcHandleNew == null)
            {
                return 0;
            }
            try { return auraMonoGcHandleNew(obj, true); }
            catch { return 0; }
        }

        internal static IntPtr AuraMonoPinTarget(uint handle)
        {
            if (handle == 0 || auraMonoGcHandleGetTarget == null)
            {
                return IntPtr.Zero;
            }
            try { return auraMonoGcHandleGetTarget(handle); }
            catch { return IntPtr.Zero; }
        }

        internal static void AuraMonoPinFree(uint handle)
        {
            if (handle == 0 || auraMonoGcHandleFree == null)
            {
                return;
            }
            try { auraMonoGcHandleFree(handle); }
            catch { }
        }

        internal static void FreeAuraMonoPins(List<uint> pins)
        {
            if (pins == null)
            {
                return;
            }
            for (int i = 0; i < pins.Count; i++)
            {
                AuraMonoPinFree(pins[i]);
            }
            pins.Clear();
        }

        private static IntPtr InvokeAuraMonoChecked(IntPtr method, IntPtr obj, IntPtr parameters, ref IntPtr exc)
        {
            MonoRuntimeInvokeDelegate raw = auraMonoRuntimeInvokeRaw;
            if (raw == null || method == IntPtr.Zero)
            {
                exc = IntPtr.Zero;
                return IntPtr.Zero;
            }

            IntPtr result = raw(method, obj, parameters, ref exc);
            if (exc != IntPtr.Zero)
            {
                RecordAuraInvokeException(method, exc);
                return IntPtr.Zero;
            }
            return result;
        }

        private static string DescribeAuraMonoClass(IntPtr klass)
        {
            if (klass == IntPtr.Zero)
            {
                return null;
            }
            string ns = auraMonoClassGetNamespace != null ? Marshal.PtrToStringAnsi(auraMonoClassGetNamespace(klass)) : null;
            string cn = auraMonoClassGetName != null ? Marshal.PtrToStringAnsi(auraMonoClassGetName(klass)) : null;
            if (string.IsNullOrEmpty(cn))
            {
                return null;
            }
            return string.IsNullOrEmpty(ns) ? cn : ns + "." + cn;
        }

        internal static bool AuraMonoMethodParamCountIs(IntPtr method, uint expected)
        {
            if (method == IntPtr.Zero)
            {
                return false;
            }
            if (auraMonoMethodSignature == null || auraMonoSignatureGetParamCount == null)
            {
                return true;
            }
            try
            {
                IntPtr sig = auraMonoMethodSignature(method);
                return sig != IntPtr.Zero && auraMonoSignatureGetParamCount(sig) == expected;
            }
            catch
            {
                return false;
            }
        }

        internal struct AuraMonoObjectCache
        {
            private uint handle;
            private IntPtr raw;
            private int epoch;

            public void Set(IntPtr obj)
            {
                this.Clear();
                if (obj == IntPtr.Zero)
                {
                    return;
                }
                this.handle = AuraMonoPinNew(obj);
                this.raw = this.handle != 0 ? IntPtr.Zero : obj;
                this.epoch = auraMonoWorldEpoch;
            }

            // Zero result means "stale or collected" — re-resolve and Set again.
            public bool TryGet(out IntPtr obj)
            {
                if ((this.handle == 0 && this.raw == IntPtr.Zero) || this.epoch != auraMonoWorldEpoch)
                {
                    this.Clear();
                    obj = IntPtr.Zero;
                    return false;
                }
                obj = this.handle != 0 ? AuraMonoPinTarget(this.handle) : this.raw;
                if (obj == IntPtr.Zero)
                {
                    this.Clear();
                    return false;
                }
                return true;
            }

            public void Clear()
            {
                AuraMonoPinFree(this.handle);
                this.handle = 0;
                this.raw = IntPtr.Zero;
                this.epoch = 0;
            }
        }

        private unsafe bool TryGetAuraMonoObjectPosition(IntPtr obj, out Vector3 position)
        {
            position = Vector3.zero;
            if (obj == IntPtr.Zero)
            {
                return false;
            }

            string[] vectorMemberNames = new string[]
            {
                "position",
                "Position",
                "pos",
                "Pos",
                "_position",
                "_pos",
                "center",
                "Center"
            };

            for (int i = 0; i < vectorMemberNames.Length; i++)
            {
                if (this.TryGetMonoVector3Member(obj, vectorMemberNames[i], out position) && position != Vector3.zero)
                {
                    return true;
                }
            }

            position = this.TryReadAuraMonoVector3Field(obj, vectorMemberNames);
            if (position != Vector3.zero)
            {
                return true;
            }

            string[] boundsMemberNames = new string[]
            {
                "bounds",
                "Bounds",
                "worldBounds",
                "WorldBounds"
            };

            for (int i = 0; i < boundsMemberNames.Length; i++)
            {
                if (this.TryGetMonoBoundsCenterMember(obj, boundsMemberNames[i], out position) && position != Vector3.zero)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAuraMonoBoxedIsValueType(IntPtr boxed)
        {
            if (boxed == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassIsValueType == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(boxed);
            return klass != IntPtr.Zero && auraMonoClassIsValueType(klass) != 0;
        }

        private unsafe bool TryUnboxMonoInt32(IntPtr boxed, out int value)
        {
            value = 0;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
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

        private unsafe bool TryUnboxMonoUInt32(IntPtr boxed, out uint value)
        {
            value = 0U;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(uint*)raw;
            return value != 0U;
        }

        private bool TrySafeGetMonoUInt32ScalarMember(IntPtr obj, string memberName, out uint value)
        {
            value = 0U;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            IntPtr boxed;
            if (!this.TryGetMonoObjectMember(obj, memberName, out boxed) || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            return this.TryUnboxMonoUInt32(boxed, out value);
        }

        private unsafe ulong TryReadMonoUnsignedIntegral(IntPtr boxed)
        {
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return 0UL;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return 0UL;
            }

            if (this.TryUnboxMonoUInt32(boxed, out uint uintValue))
            {
                return uintValue;
            }

            return *(ulong*)raw;
        }

        private unsafe bool TryUnboxMonoBoolean(IntPtr boxed, out bool value)
        {
            value = false;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = (*(byte*)raw) != 0;
            return true;
        }

        private int GetAuraMonoIntCount(IntPtr obj, IntPtr methodPtr)
        {
            if (obj == IntPtr.Zero || methodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, obj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            this.TryUnboxMonoInt32(boxed, out int count);
            return count;
        }

        private IntPtr CreateAuraMonoUInt64ArrayObject(int length)
        {
            if (length <= 0 || !this.AttachAuraMonoThread() || auraMonoArrayNew == null || this.auraMonoUInt64ClassPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return auraMonoArrayNew(this.auraMonoRootDomain, this.auraMonoUInt64ClassPtr, new UIntPtr((uint)length));
        }

        private bool EnsureAuraMonoArrayGetValueAccessor(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return false;
            }

            // CRITICAL: this object must be verified as a true mono array on EVERY call.
            // auraMonoArrayGetValueMethodPtr (System.Array.GetValue) is shared by all arrays and
            // cached globally, so an early-return on the cache alone would report "true" for ANY
            // object. Invoking mono_array_length / Array.GetValue on a non-array then aborts inside
            // the runtime (ves_icall_System_Array_GetValue g_assert -> icall.c), crashing the game.
            if (!this.IsAuraMonoArrayObject(arrayObj))
            {
                return false;
            }

            if (this.auraMonoArrayGetValueMethodPtr != IntPtr.Zero)
            {
                return true;
            }

            IntPtr arrayClass = auraMonoObjectGetClass(arrayObj);
            if (arrayClass == IntPtr.Zero)
            {
                return false;
            }

            this.auraMonoArrayGetValueMethodPtr = auraMonoClassGetMethodFromName(arrayClass, "GetValue", 1);
            return this.auraMonoArrayGetValueMethodPtr != IntPtr.Zero;
        }

        private bool IsAuraMonoArrayObject(IntPtr obj)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(obj);
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoClassGetRank != null)
            {
                try
                {
                    return auraMonoClassGetRank(klass) > 0;
                }
                catch
                {
                    return false;
                }
            }

            // Fallback when mono_class_get_rank is unavailable: array class names render with a
            // trailing "[]" (e.g. "Component[]"). Conservative — only accept on a clear match.
            string className = this.GetAuraMonoClassDisplayName(klass);
            return !string.IsNullOrEmpty(className) && className.EndsWith("]", StringComparison.Ordinal);
        }

        private bool IsAuraMonoReferenceArray(IntPtr arrayClass)
        {
            if (arrayClass == IntPtr.Zero
                || auraMonoClassGetElementClass == null
                || auraMonoClassIsValueType == null)
            {
                return false;
            }

            try
            {
                IntPtr elementClass = auraMonoClassGetElementClass(arrayClass);
                if (elementClass == IntPtr.Zero)
                {
                    return false;
                }

                return auraMonoClassIsValueType(elementClass) == 0;
            }
            catch
            {
                return false;
            }
        }

        private IntPtr GetAuraMonoArrayValue(IntPtr arrayObj, int index)
        {
            if (arrayObj == IntPtr.Zero || auraMonoRuntimeInvoke == null || !this.EnsureAuraMonoArrayGetValueAccessor(arrayObj))
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&index);
                return auraMonoRuntimeInvoke(this.auraMonoArrayGetValueMethodPtr, arrayObj, (IntPtr)args, ref exc);
            }
        }

        private bool EnsureAuraMonoApiReady()
        {
            if (this.auraMonoApiReady)
            {
                return true;
            }

            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            if (monoModule == IntPtr.Zero)
            {
                return false;
            }

            auraMonoGetRootDomain = this.GetAuraMonoExport<MonoGetRootDomainDelegate>(monoModule, "mono_get_root_domain");
            auraMonoThreadAttach = this.GetAuraMonoExport<MonoThreadAttachDelegate>(monoModule, "mono_thread_attach");
            auraMonoImageLoaded = this.GetAuraMonoExport<MonoImageLoadedDelegate>(monoModule, "mono_image_loaded");
            auraMonoAssemblyForeach = this.GetAuraMonoExport<MonoAssemblyForeachDelegate>(monoModule, "mono_assembly_foreach");
            auraMonoAssemblyGetImage = this.GetAuraMonoExport<MonoAssemblyGetImageDelegate>(monoModule, "mono_assembly_get_image");
            auraMonoImageGetName = this.GetAuraMonoExport<MonoImageGetNameDelegate>(monoModule, "mono_image_get_name");
            auraMonoClassFromName = this.GetAuraMonoExport<MonoClassFromNameDelegate>(monoModule, "mono_class_from_name");
            auraMonoClassGetMethodFromName = this.GetAuraMonoExport<MonoClassGetMethodFromNameDelegate>(monoModule, "mono_class_get_method_from_name");
            auraMonoClassGetFieldFromName = this.GetAuraMonoExport<MonoClassGetFieldFromNameDelegate>(monoModule, "mono_class_get_field_from_name");
            auraMonoFieldGetValue = this.GetAuraMonoExport<MonoFieldGetValueDelegate>(monoModule, "mono_field_get_value");
            auraMonoFieldGetValueObject = this.GetAuraMonoExport<MonoFieldGetValueObjectDelegate>(monoModule, "mono_field_get_value_object");
            auraMonoFieldSetValue = this.GetAuraMonoExport<MonoFieldSetValueDelegate>(monoModule, "mono_field_set_value");
            auraMonoRuntimeInvokeRaw = this.GetAuraMonoExport<MonoRuntimeInvokeDelegate>(monoModule, "mono_runtime_invoke");
            auraMonoRuntimeInvoke = auraMonoRuntimeInvokeRaw != null ? new MonoRuntimeInvokeDelegate(InvokeAuraMonoChecked) : null;
            auraMonoObjectUnbox = this.GetAuraMonoExport<MonoObjectUnboxDelegate>(monoModule, "mono_object_unbox");
            auraMonoStringNew = this.GetAuraMonoExport<MonoStringNewDelegate>(monoModule, "mono_string_new");
            auraMonoStringToUtf8 = this.GetAuraMonoExport<MonoStringToUtf8Delegate>(monoModule, "mono_string_to_utf8");
            auraMonoFree = this.GetAuraMonoExport<MonoFreeDelegate>(monoModule, "mono_free");
            auraMonoObjectGetClass = this.GetAuraMonoExport<MonoObjectGetClassDelegate>(monoModule, "mono_object_get_class");
            auraMonoClassIsValueType = this.GetAuraMonoExport<MonoClassIsValueTypeDelegate>(monoModule, "mono_class_is_valuetype");
            auraMonoClassGetType = this.GetAuraMonoExport<MonoClassGetTypeDelegate>(monoModule, "mono_class_get_type");
            auraMonoClassBindGenericParameters = this.GetAuraMonoExport<MonoClassBindGenericParametersDelegate>(monoModule, "mono_class_bind_generic_parameters");
            auraMonoClassInflateGenericMethod = this.GetAuraMonoExport<MonoClassInflateGenericMethodDelegate>(monoModule, "mono_class_inflate_generic_method");
            auraMonoMetadataGetGenericInst = this.GetAuraMonoExport<MonoMetadataGetGenericInstDelegate>(monoModule, "mono_metadata_get_generic_inst");
            auraMonoTypeGetObject = this.GetAuraMonoExport<MonoTypeGetObjectDelegate>(monoModule, "mono_type_get_object");
            auraMonoClassGetParent = this.GetAuraMonoExport<MonoClassGetParentDelegate>(monoModule, "mono_class_get_parent");
            auraMonoClassGetRank = this.GetAuraMonoExport<MonoClassGetRankDelegate>(monoModule, "mono_class_get_rank");
            auraMonoGcDisable = this.GetAuraMonoExport<MonoGcVoidDelegate>(monoModule, "mono_gc_disable");
            auraMonoGcEnable = this.GetAuraMonoExport<MonoGcVoidDelegate>(monoModule, "mono_gc_enable");
            auraMonoGcHandleNew = this.GetAuraMonoExport<MonoGcHandleNewDelegate>(monoModule, "mono_gchandle_new");
            auraMonoGcHandleGetTarget = this.GetAuraMonoExport<MonoGcHandleGetTargetDelegate>(monoModule, "mono_gchandle_get_target");
            auraMonoGcHandleFree = this.GetAuraMonoExport<MonoGcHandleFreeDelegate>(monoModule, "mono_gchandle_free");
            auraMonoClassGetElementClass = this.GetAuraMonoExport<MonoClassGetElementClassDelegate>(monoModule, "mono_class_get_element_class");
            auraMonoClassGetName = this.GetAuraMonoExport<MonoClassGetNameDelegate>(monoModule, "mono_class_get_name");
            auraMonoClassGetNamespace = this.GetAuraMonoExport<MonoClassGetNamespaceDelegate>(monoModule, "mono_class_get_namespace");
            auraMonoClassGetMethods = this.GetAuraMonoExport<MonoClassGetMethodsDelegate>(monoModule, "mono_class_get_methods");
            auraMonoMethodGetName = this.GetAuraMonoExport<MonoMethodGetNameDelegate>(monoModule, "mono_method_get_name");
            auraMonoMethodGetClass = this.GetAuraMonoExport<MonoMethodGetClassDelegate>(monoModule, "mono_method_get_class");
            auraMonoClassGetFields = this.GetAuraMonoExport<MonoClassGetFieldsDelegate>(monoModule, "mono_class_get_fields");
            auraMonoFieldGetName = this.GetAuraMonoExport<MonoFieldGetNameDelegate>(monoModule, "mono_field_get_name");
            auraMonoMethodSignature = this.GetAuraMonoExport<MonoMethodSignatureDelegate>(monoModule, "mono_method_signature");
            auraMonoSignatureGetParamCount = this.GetAuraMonoExport<MonoSignatureGetParamCountDelegate>(monoModule, "mono_signature_get_param_count");
            auraMonoArrayLength = this.GetAuraMonoExport<MonoArrayLengthDelegate>(monoModule, "mono_array_length");
            auraMonoArrayAddrWithSize = this.GetAuraMonoExport<MonoArrayAddrWithSizeDelegate>(monoModule, "mono_array_addr_with_size");
            auraMonoArrayElementSize = this.GetAuraMonoExport<MonoArrayElementSizeDelegate>(monoModule, "mono_array_element_size");
            auraMonoFieldGetOffset = this.GetAuraMonoExport<MonoFieldGetOffsetDelegate>(monoModule, "mono_field_get_offset");
            auraMonoArrayNew = this.GetAuraMonoExport<MonoArrayNewDelegate>(monoModule, "mono_array_new");
            auraMonoClassVtable = this.GetAuraMonoExport<MonoClassVtableDelegate>(monoModule, "mono_class_vtable");
            auraMonoFieldStaticGetValue = this.GetAuraMonoExport<MonoFieldStaticGetValueDelegate>(monoModule, "mono_field_static_get_value");
            auraMonoObjectNew = this.GetAuraMonoExport<MonoObjectNewDelegate>(monoModule, "mono_object_new");
            auraMonoRuntimeObjectInit = this.GetAuraMonoExport<MonoRuntimeObjectInitDelegate>(monoModule, "mono_runtime_object_init");
            auraMonoCompileMethod = this.GetAuraMonoExport<MonoCompileMethodDelegate>(monoModule, "mono_compile_method");
            auraMonoMethodGetUnmanagedThunk = this.GetAuraMonoExport<MonoMethodGetUnmanagedThunkDelegate>(monoModule, "mono_method_get_unmanaged_thunk");
            auraMonoMethodGetUnmanaged = this.GetAuraMonoExport<MonoMethodGetUnmanagedDelegate>(monoModule, "mono_method_get_unmanaged");

            this.auraMonoApiReady = auraMonoGetRootDomain != null
                && auraMonoThreadAttach != null
                && auraMonoImageLoaded != null
                && auraMonoClassFromName != null
                && auraMonoClassGetMethodFromName != null
                && auraMonoClassGetFieldFromName != null
                && auraMonoFieldGetValue != null
                && auraMonoRuntimeInvoke != null
                && auraMonoObjectUnbox != null
                && auraMonoStringNew != null;

            if (this.auraMonoApiReady)
            {
                this.auraMonoRootDomain = auraMonoGetRootDomain();
                if (this.auraMonoAttachedDomain != this.auraMonoRootDomain)
                {
                    this.auraMonoAttachedManagedThreadId = int.MinValue;
                    this.auraMonoAttachedDomain = IntPtr.Zero;
                }

                // Game Mono runtime is up and modules are loaded: auto-dump decrypted assemblies
                // once, but only if the opt-in DecryptedAssemblies folder already exists.
                MonoAssemblyDump.OnRuntimeReady();
            }

            return this.auraMonoApiReady && this.auraMonoRootDomain != IntPtr.Zero;
        }

        private bool AttachAuraMonoThread()
        {
            if (!this.auraMonoApiReady || auraMonoThreadAttach == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int managedThreadId = Environment.CurrentManagedThreadId;
                if (this.auraMonoAttachedManagedThreadId == managedThreadId
                    && this.auraMonoAttachedDomain == this.auraMonoRootDomain)
                {
                    return true;
                }

                auraMonoThreadAttach(this.auraMonoRootDomain);
                this.auraMonoAttachedManagedThreadId = managedThreadId;
                this.auraMonoAttachedDomain = this.auraMonoRootDomain;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IntPtr FindAuraMonoImage(string[] names)
        {
            if (!this.auraMonoApiReady || auraMonoImageLoaded == null || names == null)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                try
                {
                    IntPtr image = auraMonoImageLoaded(name);
                    if (image != IntPtr.Zero)
                    {
                        return image;
                    }
                }
                catch
                {
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr GetAuraMonoModuleHandle()
        {
            string[] candidates = new string[]
            {
                "mono-2.0-bdwgc.dll",
                "mono-2.0-sgen.dll",
                "mono.dll"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                IntPtr h = GetModuleHandle(candidates[i]);
                if (h != IntPtr.Zero)
                {
                    return h;
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr FindAuraMonoClassAcrossLoadedAssemblies(string nameSpace, string className)
        {
            if (!this.auraMonoApiReady || auraMonoClassFromName == null || string.IsNullOrWhiteSpace(nameSpace) || string.IsNullOrWhiteSpace(className))
            {
                return IntPtr.Zero;
            }

            try
            {
                Type monoHostType = Type.GetType("Il2CppMonoGame.MonoHost, Il2CppMonoGame", false);
                if (monoHostType == null)
                {
                    return IntPtr.Zero;
                }

                PropertyInfo currentProperty = monoHostType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                object monoHost = currentProperty != null ? currentProperty.GetValue(null, null) : null;
                if (monoHost == null)
                {
                    return IntPtr.Zero;
                }

                FieldInfo loadedAssembliesField = monoHostType.GetField("_loadedAssemblies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object loadedAssemblies = loadedAssembliesField != null ? loadedAssembliesField.GetValue(monoHost) : null;
                IEnumerable enumerable = loadedAssemblies as IEnumerable;
                if (enumerable == null)
                {
                    return IntPtr.Zero;
                }

                foreach (object entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    Type entryType = entry.GetType();
                    object value = null;
                    PropertyInfo valueProperty = entryType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (valueProperty != null)
                    {
                        value = valueProperty.GetValue(entry, null);
                    }

                    if (value == null)
                    {
                        continue;
                    }

                    PropertyInfo imageProperty = value.GetType().GetProperty("Image", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object image = imageProperty != null ? imageProperty.GetValue(value, null) : null;
                    if (image == null)
                    {
                        continue;
                    }

                    PropertyInfo handleProperty = image.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (handleProperty == null)
                    {
                        continue;
                    }

                    object handleValue = handleProperty.GetValue(image, null);
                    if (!(handleValue is IntPtr imageHandle) || imageHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr classPtr = auraMonoClassFromName(imageHandle, nameSpace, className);
                    if (classPtr != IntPtr.Zero)
                    {
                        return classPtr;
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private T GetAuraMonoExport<T>(IntPtr module, string exportName) where T : class
        {
            if (module == IntPtr.Zero || string.IsNullOrEmpty(exportName))
            {
                return null;
            }

            IntPtr ptr = GetProcAddress(module, exportName);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
        }

        private IntPtr GetAuraMonoUInt64ListObject()
        {
            if (!this.AttachAuraMonoThread() || auraMonoStringNew == null || auraMonoRuntimeInvoke == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, "System.Collections.Generic.List`1[System.UInt64]");
            if (typeNameStr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeNameStr;

                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    this.auraLastError = "Mono Type.GetType(List<UInt64>) returned null.";
                    return IntPtr.Zero;
                }

                exc = IntPtr.Zero;
                args[0] = typeObj;
                IntPtr listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    this.auraLastError = "Mono Activator.CreateInstance(List<UInt64>) returned null.";
                    return IntPtr.Zero;
                }

                this.CacheAuraMonoUInt64ListAccessors(listObj);
                return listObj;
            }
        }

        private void CacheAuraMonoUInt64ListAccessors(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return;
            }

            if (this.auraMonoUInt64ListCountMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListCountMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Count", 0);
            }
            if (this.auraMonoUInt64ListGetItemMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListGetItemMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Item", 1);
            }
            if (this.auraMonoUInt64ListClearMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListClearMethodPtr = auraMonoClassGetMethodFromName(listClass, "Clear", 0);
            }
        }

        private int GetAuraMonoUInt64ListCount(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || this.auraMonoUInt64ListCountMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoUInt64ListCountMethodPtr, listObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            this.TryUnboxMonoInt32(boxed, out int count);
            return count;
        }

        private ulong GetAuraMonoUInt64ListItem(IntPtr listObj, int index)
        {
            if (listObj == IntPtr.Zero || this.auraMonoUInt64ListGetItemMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0UL;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&index);
                IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoUInt64ListGetItemMethodPtr, listObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    return 0UL;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return 0UL;
                }

                return *(ulong*)raw;
            }
        }

        private IntPtr GetAuraMonoArrayObjectItem(IntPtr arrayObj, int index)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayAddrWithSize == null)
            {
                return IntPtr.Zero;
            }

            IntPtr slot = auraMonoArrayAddrWithSize(arrayObj, IntPtr.Size, (UIntPtr)index);
            return slot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(slot);
        }

    }
}
