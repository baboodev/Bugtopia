using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;

namespace HeartopiaMod
{
    // STAGE 1 of "callbacks from il2cpp without causing class injection".
    //
    // THE PROBLEM. Handing a managed method to an il2cpp `UnityEvent` (every button, checkbox and
    // slider in the mod's UI) goes through `DelegateSupport.ConvertDelegate`, which keeps the
    // managed delegate alive by parking it in an INJECTED il2cpp class,
    // `Il2CppToMonoDelegateReference`. Injecting a class calls `InjectorHelpers.Setup()`, which
    // writes inline detours into `GameAssembly.dll` `.text`. `Setup()` is lazy and one-shot, so the
    // cost is binary: a session that never converts a delegate has ZERO patches (measured), and one
    // that converts even once has 3 for the rest of the process.
    //
    // THE IDEA. Read `ConvertDelegate` and the injected class turns out NOT to be load-bearing for
    // the delegate — it is load-bearing for FINDING THE MANAGED HANDLER AGAIN when the call comes
    // back. A `[UnmanagedCallersOnly]` static thunk has nothing to find: it is a plain function
    // pointer into JIT'd code, with no object to keep alive. So the same four field writes work with
    // `m_target` set to an ordinary `Il2CppSystem.Object` used purely as a dispatch key.
    //
    // WHAT THIS FILE PROVES (or disproves) — and nothing more:
    //   1. that a hand-built delegate survives `UnityEvent.AddListener` + `Invoke()`;
    //   2. that our thunk is actually reached;
    //   3. WHICH key arrives — arg0 should be the `m_target` pointer (Il2CppInterop's own generated
    //      trampoline starts `ldarg_0` → `GetMonoObjectFromIl2CppPointer` → `castclass`, so this is
    //      near-certain), and the trailing argument should be the `MethodInfo*` we allocated. The
    //      MethodInfo is an `AllocHGlobal` and therefore cannot move, so if it does arrive it is the
    //      better dispatch key of the two;
    //   4. that doing all of the above injects NOTHING — the canary samples on both sides.
    //
    // WHY IT IS SAFE TO TRY. Every value written here comes from Il2CppInterop's own public API
    // (`Il2CppClassPointerStore<T>.NativeClassPtr`, `UnityVersionHandler.NewMethod()`, the
    // `Il2CppSystem.Delegate` field accessors, which resolve their offsets at runtime via
    // `il2cpp_field_get_offset`). There is not a single hardcoded offset in this file, so a layout
    // change breaks it exactly as it would break `ConvertDelegate` itself — no new failure mode.
    // Everything is wrapped so a failure logs and returns; nothing else in the mod depends on it.
    //
    // NOT WIRED INTO ANY REAL UI. Stage 1 builds one delegate, fires one synthetic `UnityEvent`, and
    // throws it away.
    internal static unsafe class HookFreeDelegateProbe
    {
        internal const bool Stage1Enabled = true;

        internal const string WorldReadyCallbackName = "HookFreeDelegateProbe";

        private static bool ran;

        // Written by the thunk, read after Invoke(). Plain fields: the thunk must not allocate or
        // call anything that could throw back into native code.
        private static int thunkHits;
        private static IntPtr seenArg0;
        private static IntPtr seenMethodInfo;

        // World-ready callback contract: true = done for this world epoch.
        internal static bool RunOnWorldReady()
        {
            if (ran || !Stage1Enabled)
            {
                return true;
            }

            ran = true;
            Run();
            return true;
        }

        // The thunk. Signature mirrors what Il2CppInterop generates for a `hasThis` trampoline over
        // a parameterless delegate: (instance, MethodInfo*). MUST NOT THROW — it returns straight
        // into native code, where an escaping exception is undefined behaviour.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ProbeThunk(IntPtr self, IntPtr methodInfo)
        {
            try
            {
                seenArg0 = self;
                seenMethodInfo = methodInfo;
                thunkHits++;
            }
            catch
            {
            }
        }

        private static void Run()
        {
            InjectionGateCanary.SampleForced("hookfree-probe:before");

            IntPtr expectedTarget = IntPtr.Zero;
            IntPtr expectedMethodInfo = IntPtr.Zero;

            try
            {
                PreJitThunk();

                IntPtr klass = Il2CppClassPointerStore<UnityEngine.Events.UnityAction>.NativeClassPtr;
                if (klass == IntPtr.Zero)
                {
                    ModLogger.Msg("[HookFreeDelegate] stage 1 ABORT: UnityAction class pointer is null");
                    return;
                }

                // The real il2cpp MethodInfo for `UnityAction::Invoke`. ConvertDelegate stores this
                // in `method_info`; it is the game's own metadata, not something we fabricate.
                Il2CppSystem.Reflection.MethodInfo invokeInfo =
                    Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(klass)).GetMethod("Invoke");
                if (invokeInfo == null)
                {
                    ModLogger.Msg("[HookFreeDelegate] stage 1 ABORT: UnityAction::Invoke MethodInfo not found");
                    return;
                }

                // The fabricated MethodInfo whose methodPointer is our thunk. Same four assignments
                // ConvertDelegate makes; ParametersCount is 0 because UnityAction takes none.
                INativeMethodInfoStruct fake = UnityVersionHandler.NewMethod();
                fake.MethodPointer = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&ProbeThunk;
                fake.ParametersCount = 0;
                fake.Slot = ushort.MaxValue;
                fake.IsMarshalledFromNative = true;
                expectedMethodInfo = fake.Pointer;

                // The dispatch key. ConvertDelegate puts the injected reference object here purely so
                // the trampoline can find the managed delegate again; an ordinary object serves the
                // same purpose for a static thunk, and injects nothing.
                Il2CppSystem.Object target = new Il2CppSystem.Object();
                expectedTarget = target.Pointer;

                Il2CppSystem.Delegate del = new Il2CppSystem.Delegate(IL2CPP.il2cpp_object_new(klass));
                del.method_ptr = fake.MethodPointer;
                del.method_info = invokeInfo;
                del.method = fake.Pointer;
                del.m_target = target;

                UnityEngine.Events.UnityAction action = del.Cast<UnityEngine.Events.UnityAction>();
                ModLogger.Msg("[HookFreeDelegate] built UnityAction @0x" + del.Pointer.ToString("X")
                              + " thunk=0x" + fake.MethodPointer.ToString("X")
                              + " methodInfo=0x" + expectedMethodInfo.ToString("X")
                              + " target=0x" + expectedTarget.ToString("X"));

                // Fire it through a real UnityEvent — the same type and the same dispatch path as
                // `Button.onClick`, minus the GameObject.
                thunkHits = 0;
                seenArg0 = IntPtr.Zero;
                seenMethodInfo = IntPtr.Zero;

                UnityEngine.Events.UnityEvent evt = new UnityEngine.Events.UnityEvent();
                evt.AddListener(action);
                evt.Invoke();

                if (thunkHits == 0)
                {
                    ModLogger.Msg("[HookFreeDelegate] stage 1 FAIL: Invoke() did not reach the thunk "
                                  + "— the delegate was built but never dispatched.");
                }
                else
                {
                    bool targetMatch = seenArg0 == expectedTarget;
                    bool miMatch = seenMethodInfo == expectedMethodInfo;
                    ModLogger.Msg("[HookFreeDelegate] stage 1 PASS: thunk fired " + thunkHits + "x"
                                  + " | arg0=0x" + seenArg0.ToString("X") + " (m_target match=" + targetMatch + ")"
                                  + " | trailing=0x" + seenMethodInfo.ToString("X") + " (MethodInfo match=" + miMatch + ")");
                    ModLogger.Msg("[HookFreeDelegate] usable dispatch key: "
                                  + (miMatch ? "MethodInfo (stable, AllocHGlobal — prefer this)"
                                             : targetMatch ? "m_target pointer only"
                                                           : "NEITHER — do not proceed to stage 2"));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[HookFreeDelegate] stage 1 threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // If injectedClasses moved between the two samples, the probe itself caused injection
                // and the whole idea is dead — that is the reading that matters most here.
                InjectionGateCanary.SampleForced("hookfree-probe:after");
            }
        }

        // The thunk's address is a precode stub until first call; JIT it up front so the first
        // dispatch does not land in the JIT while the stack is native (same precaution the
        // PlayerLoop pump takes).
        private static void PreJitThunk()
        {
            try
            {
                System.Reflection.MethodInfo m = typeof(HookFreeDelegateProbe).GetMethod(
                    nameof(ProbeThunk),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (m != null)
                {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                }
            }
            catch
            {
            }
        }
    }
}
