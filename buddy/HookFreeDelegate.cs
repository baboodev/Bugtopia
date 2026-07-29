using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;

namespace HeartopiaMod
{
    // Hands il2cpp a callback WITHOUT causing class injection — the mechanism proven by
    // HookFreeDelegateProbe.cs (read its header for the why; this file is the production factory).
    //
    // Short version: `DelegateSupport.ConvertDelegate` parks the managed delegate in an INJECTED
    // il2cpp class so the trampoline can find it again on the way back, and injecting a class writes
    // inline detours into `GameAssembly.dll` `.text`. A `[UnmanagedCallersOnly]` static thunk has
    // nothing to find, so the same four field writes work without the injected type. The managed
    // handler is kept alive by an ordinary managed dictionary instead — strictly simpler than
    // rooting it through an il2cpp object's lifetime across two garbage collectors.
    //
    // THE ABI, verified by disassembling `UnityAction<bool>::Invoke` in this exact GameAssembly:
    //     mov qword ptr [rsp+0x28], rbx   ; [delegate+0x28] = method     -> MethodInfo*, TRAILING
    //     mov byte  ptr [rsp+0x20], bpl   ; the declared argument
    //     mov rcx, rdi                    ; [delegate+0x20] = m_target   -> RCX, FIRST
    //     call rsi                        ; [delegate+0x10] = method_ptr
    // so every thunk here is `(IntPtr m_target, <declared args>, IntPtr methodInfo)`, Cdecl.
    //
    // ⭐ `Slot = 0xFFFF` IS LOAD-BEARING, not boilerplate. The dispatcher does
    // `cmp word ptr [methodInfo+0x48], 0xFFFF` and on a mismatch branches into vtable/slot
    // resolution instead of calling `method_ptr`. `ParametersCount` is checked too, so it must match
    // the delegate's real arity.
    //
    // DISPATCH KEY = the `MethodInfo*`, not `m_target`. Both arrive (measured), but the MethodInfo
    // is an `AllocHGlobal` and therefore cannot move, whereas an object pointer is at the mercy of
    // the il2cpp GC.
    //
    // NO HARDCODED OFFSETS ANYWHERE. Everything goes through Il2CppInterop's public API, whose field
    // accessors resolve their own offsets at runtime via `il2cpp_field_get_offset`. A layout change
    // breaks this exactly as it would break `ConvertDelegate` itself — no new failure mode. Keep it
    // that way: the moment someone writes a literal offset here, this becomes a silent-corruption
    // risk instead of an inherited-correctness one.
    //
    // FAIL-CLOSED. Every factory returns null on any problem, and callers fall back to the ordinary
    // `AddListener(managedDelegate)`. That path costs class injection (3 `.text` patches) but keeps
    // the UI working, which is the right trade for a diagnostic-grade optimisation.
    //
    // ⚠️ LIFETIME, stated honestly: each wire allocates one `MethodInfo` (~80 bytes, unmanaged) and
    // one `Il2CppSystem.Object`, and nothing frees them — `ConvertDelegate` relies on the injected
    // type's FINALIZER for that, which is exactly what we removed. Freeing early is not an option:
    // il2cpp still holds `method`, so releasing it under a live delegate is a use-after-free. The
    // shell is ~350 controls ≈ 36 KB per build, and it is rebuilt only on a theme change, so the
    // growth is bounded and user-paced rather than continuous. `Wired` is logged so it stays visible.
    internal static unsafe class HookFreeDelegate
    {
        // Master switch. Off = every wire uses the ordinary injecting path, i.e. exactly the
        // behaviour before this file existed.
        internal const bool Enabled = true;

        internal static int Wired;
        internal static int FellBack;

        private static readonly object Gate = new object();
        private static readonly Dictionary<IntPtr, object> Handlers = new Dictionary<IntPtr, object>();
        private static bool thunkFaultLogged;

        internal static void LogSummary(string when)
        {
            // Carries the gate counts on the same line so a healthy session has ONE line of positive
            // confirmation instead of a repeated block — InjectionGateCanary and the hook verifier
            // both stay silent unless something actually changes, and silence is a poor signal.
            ModLogger.Msg("[HookFreeDelegate] " + when + ": " + Wired + " hook-free wire(s), "
                          + FellBack + " fell back to the injecting path"
                          + (FellBack > 0 ? " — injection has happened, see [InjectionGate]" : "")
                          + " | " + InjectionGateCanary.Counts);
        }

        // ---- factories ---------------------------------------------------------------------------

        // Button.onClick — by far the most common wire in the mod (18 sites).
        internal static UnityEngine.Events.UnityAction ForVoid(Action handler)
        {
            return Create<UnityEngine.Events.UnityAction>(handler, 0,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&ThunkVoid);
        }

        // The async sprite-load completion callback (HeartopiaComplete.GameIcons.cs). Its il2cpp
        // delegate type is resolved at RUNTIME, so it cannot go through the generic factory —
        // hence CreateForType. Returns the wrapper object to hand to LoadSpriteAsync, or null.
        internal static object ForSpriteAction(Type il2cppActionType, Action<UnityEngine.Sprite> handler)
        {
            return CreateForType(il2cppActionType, handler, 1,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&ThunkSprite);
        }

        internal static UnityEngine.Events.UnityAction<bool> ForBool(Action<bool> handler)
        {
            return Create<UnityEngine.Events.UnityAction<bool>>(handler, 1,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)&ThunkBool);
        }

        internal static UnityEngine.Events.UnityAction<float> ForFloat(Action<float> handler)
        {
            return Create<UnityEngine.Events.UnityAction<float>>(handler, 1,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, float, IntPtr, void>)&ThunkFloat);
        }

        internal static UnityEngine.Events.UnityAction<int> ForInt(Action<int> handler)
        {
            return Create<UnityEngine.Events.UnityAction<int>>(handler, 1,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&ThunkInt);
        }

        internal static UnityEngine.Events.UnityAction<string> ForString(Action<string> handler)
        {
            return Create<UnityEngine.Events.UnityAction<string>>(handler, 1,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&ThunkString);
        }

        // ---- the thunks --------------------------------------------------------------------------
        //
        // `byte` rather than `bool` for the bool overload: `[UnmanagedCallersOnly]` requires blittable
        // parameters, and the disassembly shows the argument arriving as a single byte anyway.
        // NONE of these may throw — they return straight into native code, where an escaping
        // exception is undefined behaviour. Hence the blanket catch and the one-shot fault log.

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkVoid(IntPtr self, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action h) { h(); }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkSprite(IntPtr self, IntPtr sprite, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action<UnityEngine.Sprite> h)
                {
                    h(sprite == IntPtr.Zero ? null : new UnityEngine.Sprite(sprite));
                }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkBool(IntPtr self, byte arg, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action<bool> h) { h(arg != 0); }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkFloat(IntPtr self, float arg, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action<float> h) { h(arg); }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkInt(IntPtr self, int arg, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action<int> h) { h(arg); }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ThunkString(IntPtr self, IntPtr arg, IntPtr methodInfo)
        {
            try
            {
                if (Lookup(methodInfo) is Action<string> h)
                {
                    h(arg == IntPtr.Zero ? string.Empty : IL2CPP.Il2CppStringToManaged(arg));
                }
            }
            catch (Exception ex) { ReportThunkFault(ex); }
        }

        // ---- core --------------------------------------------------------------------------------

        private static T Create<T>(object handler, byte argCount, IntPtr thunk) where T : Il2CppObjectBase
        {
            if (!Enabled || handler == null)
            {
                return null;
            }

            try
            {
                IntPtr klass = Il2CppClassPointerStore<T>.NativeClassPtr;
                if (klass == IntPtr.Zero)
                {
                    return null;
                }

                // The delegate type's own `Invoke` metadata — the game's, not something we fabricate.
                Il2CppSystem.Reflection.MethodInfo invokeInfo =
                    Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(klass)).GetMethod("Invoke");
                if (invokeInfo == null)
                {
                    return null;
                }

                INativeMethodInfoStruct fake = UnityVersionHandler.NewMethod();
                fake.MethodPointer = thunk;
                fake.ParametersCount = argCount;
                fake.Slot = ushort.MaxValue;          // see header — forces direct method_ptr dispatch
                fake.IsMarshalledFromNative = true;

                Il2CppSystem.Delegate del = new Il2CppSystem.Delegate(IL2CPP.il2cpp_object_new(klass));
                del.method_ptr = fake.MethodPointer;
                del.method_info = invokeInfo;
                del.method = fake.Pointer;
                del.m_target = new Il2CppSystem.Object();

                // Register BEFORE handing the delegate out: a listener could in principle fire during
                // the same frame it is wired.
                lock (Gate)
                {
                    Handlers[fake.Pointer] = handler;
                    Wired++;
                }

                return del.Cast<T>();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[HookFreeDelegate] build failed (" + typeof(T).Name + "): "
                              + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        // Same construction as Create<T>, but for a delegate type only known at runtime. Split out
        // rather than folded in because the class-pointer lookup has to go through reflection over
        // `Il2CppClassPointerStore<>` — which is exactly what ConvertDelegate does internally, so it
        // is a supported shape, just not one a generic constraint can express.
        private static object CreateForType(Type il2cppDelegateType, object handler, byte argCount, IntPtr thunk)
        {
            if (!Enabled || handler == null || il2cppDelegateType == null)
            {
                return null;
            }

            try
            {
                object stored = typeof(Il2CppClassPointerStore<>).MakeGenericType(il2cppDelegateType)
                    .GetField("NativeClassPtr").GetValue(null);
                if (stored is not IntPtr klass || klass == IntPtr.Zero)
                {
                    return null;
                }

                Il2CppSystem.Reflection.MethodInfo invokeInfo =
                    Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(klass)).GetMethod("Invoke");
                if (invokeInfo == null)
                {
                    return null;
                }

                INativeMethodInfoStruct fake = UnityVersionHandler.NewMethod();
                fake.MethodPointer = thunk;
                fake.ParametersCount = argCount;
                fake.Slot = ushort.MaxValue;
                fake.IsMarshalledFromNative = true;

                Il2CppSystem.Delegate del = new Il2CppSystem.Delegate(IL2CPP.il2cpp_object_new(klass));
                del.method_ptr = fake.MethodPointer;
                del.method_info = invokeInfo;
                del.method = fake.Pointer;
                del.m_target = new Il2CppSystem.Object();

                lock (Gate)
                {
                    Handlers[fake.Pointer] = handler;
                    Wired++;
                }

                // The caller needs the concrete wrapper type, not Il2CppSystem.Delegate — every
                // Il2CppObjectBase subclass has an IntPtr constructor for exactly this.
                return Activator.CreateInstance(il2cppDelegateType, del.Pointer);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[HookFreeDelegate] build failed (" + il2cppDelegateType.Name + "): "
                              + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        internal static void NoteFallback()
        {
            FellBack++;
        }

        private static object Lookup(IntPtr methodInfo)
        {
            lock (Gate)
            {
                return Handlers.TryGetValue(methodInfo, out object handler) ? handler : null;
            }
        }

        // One line per session: a thunk that faults every frame would otherwise drown the log, and
        // the first fault is the one that says what went wrong.
        private static void ReportThunkFault(Exception ex)
        {
            try
            {
                if (thunkFaultLogged)
                {
                    return;
                }

                thunkFaultLogged = true;
                ModLogger.Msg("[HookFreeDelegate] THUNK FAULTED (further faults suppressed): "
                              + ex.GetType().Name + ": " + ex.Message);
            }
            catch
            {
            }
        }
    }
}
