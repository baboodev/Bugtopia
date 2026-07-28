using System;
using System.Runtime.InteropServices;
using System.Threading;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // A per-frame tick sourced from the game's EMBEDDED MONO main loop instead of from the injected
    // Unity MonoBehaviour.
    //
    // WHY THIS EXISTS: every IL2CPP `.text` hook the mod is responsible for comes from ONE call —
    // `AddComponent<HeartopiaBehaviour>()` (BepInExPlugin.cs) -> ClassInjector.RegisterTypeInIl2Cpp
    // -> InjectorHelpers.Setup() -> 5 inline detours on IL2CPP VM internals. `Setup()` is LAZY: with
    // no type injection those 5 hooks are never installed, and BepInEx itself injects nothing. So if
    // the mod can get its tick (and later its coroutines) without a MonoBehaviour, that whole surface
    // can go away. This feature is step 1: prove a Mono-side tick exists and is genuinely per-frame.
    //
    // TARGET: `XDTGame.Core.GameWorld.Update(float deltaTime)` (image XDTBaseService) — the game's
    // Mono main loop: it early-outs on `!Application.isPlaying`, ticks the level transition, then
    // walks the `_currentLevel` node chain calling `levelNode.OnUpdate(deltaTime)`. It is `internal
    // static void`, i.e. no `this` and a single float — the simplest possible detour signature (float
    // arrives in XMM0; no sret concern since the return is void).
    //
    // Mono-compiled code lives in the Mono runtime's JIT memory / `mono-2.0-sgen.dll`, NOT in
    // `GameAssembly.dll`, so this hook adds ZERO bytes to the module the anti-cheat integrity model
    // hashes. It is the same proven mechanism 15 other files in this mod already use.
    //
    // ⚠️ STATUS: VERIFICATION PHASE. Nothing is driven from this tick yet. `MonoTickEnabled` gates
    // the whole thing, install happens on the world-ready gate (never a bare timer in OnUpdate), and
    // the detour body is allocation-free — a counter bump plus the trampoline forward — because it
    // runs every frame and, per this project's history, native detours that allocate in their body
    // are exactly what corrupts the heap during world-change teardown.
    public partial class HeartopiaComplete
    {
        // Master switch. Flip to false to leave the game's Mono loop completely untouched.
        internal static bool MonoTickEnabled = true;

        // Diagnostics — all read/written outside the hot path except monoTickCount.
        internal static long monoTickCount;
        internal static string monoTickStatus = "not installed";
        internal static float monoTickLastRatio;

        private const string MonoTickWorldReadyCallbackName = "MonoTick";
        private const float MonoTickVerifyIntervalSeconds = 5f;

        private static readonly string[] MonoTickImageNames =
        {
            "XDTBaseService", "XDTBaseService.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        private bool monoTickInstallTried;
        private float monoTickNextVerifyAt = -999f;
        private long monoTickLastCount;
        private int monoTickLastUnityFrame;

        private static NativeDetour monoTickDetour;
        private static MonoTickDelegate monoTickKeepAlive;
        private static MonoTickDelegate monoTickTrampoline;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoTickDelegate(float deltaTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoTickCompileMethodDelegate(IntPtr method);

        internal void RegisterMonoTickWorldReady()
        {
            if (!MonoTickEnabled)
            {
                return;
            }

            this.RegisterWorldReadyCallback(MonoTickWorldReadyCallbackName, this.TryInstallMonoTickOnWorldReady);
        }

        // Returns true once the attempt is settled (installed OR permanently failed), so the gate
        // stops re-running it. Fail-closed: any resolve miss settles rather than retrying forever.
        private bool TryInstallMonoTickOnWorldReady()
        {
            if (this.monoTickInstallTried || !MonoTickEnabled)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    // Mono not up yet — let the gate call us again.
                    return false;
                }

                this.monoTickInstallTried = true;

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                MonoTickCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<MonoTickCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    monoTickStatus = "mono_compile_method unavailable";
                    ModLogger.Msg("[MonoTick] " + monoTickStatus);
                    return true;
                }

                const string nameSpace = "XDTGame.Core";
                const string shortName = "GameWorld";
                IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, MonoTickImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
                }

                if (cls == IntPtr.Zero)
                {
                    monoTickStatus = "GameWorld class not found";
                    ModLogger.Msg("[MonoTick] " + monoTickStatus);
                    return true;
                }

                // internal static void Update(float deltaTime) — 1 parameter.
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "Update", 1);
                if (method == IntPtr.Zero)
                {
                    monoTickStatus = "GameWorld.Update(float) not found";
                    ModLogger.Msg("[MonoTick] " + monoTickStatus);
                    return true;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    monoTickStatus = "mono_compile_method returned null";
                    ModLogger.Msg("[MonoTick] " + monoTickStatus);
                    return true;
                }

                monoTickKeepAlive = MonoTickDetourBody;
                monoTickDetour = new NativeDetour(nativePtr, monoTickKeepAlive);
                monoTickTrampoline = monoTickDetour.GenerateTrampoline<MonoTickDelegate>();
                if (monoTickTrampoline == null)
                {
                    // Without a trampoline we would SWALLOW the game's main loop. Revert immediately.
                    try { monoTickDetour?.Undo(); } catch { }
                    monoTickDetour = null;
                    monoTickKeepAlive = null;
                    monoTickStatus = "trampoline unavailable; detour reverted";
                    ModLogger.Msg("[MonoTick] " + monoTickStatus);
                    return true;
                }

                this.monoTickLastCount = 0L;
                this.monoTickLastUnityFrame = Time.frameCount;
                this.monoTickNextVerifyAt = Time.unscaledTime + MonoTickVerifyIntervalSeconds;
                monoTickStatus = "installed";
                ModLogger.Msg("[MonoTick] hooked XDTGame.Core.GameWorld.Update(float) @0x"
                              + nativePtr.ToInt64().ToString("X") + " — verifying rate vs Unity frames");
                return true;
            }
            catch (Exception ex)
            {
                this.monoTickInstallTried = true;
                monoTickStatus = "install failed: " + ex.Message;
                ModLogger.Msg("[MonoTick] " + monoTickStatus);
                return true;
            }
        }

        // Runs on the game's main thread every frame, as a native->coreclr reverse P/Invoke from
        // Mono-compiled code. Allocation-free and branch-minimal ON PURPOSE: one interlocked bump and
        // the forward. Never log, allocate or touch game state here.
        private static void MonoTickDetourBody(float deltaTime)
        {
            Interlocked.Increment(ref monoTickCount);
            monoTickTrampoline?.Invoke(deltaTime);
        }

        // Verification, driven from the EXISTING MonoBehaviour pump so the two can be compared. Once
        // the ratio is confirmed ~1.0 this is what proves the Mono tick can replace that pump.
        private void ProcessMonoTickVerify()
        {
            if (monoTickDetour == null || !MonoTickEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.monoTickNextVerifyAt)
            {
                return;
            }

            this.monoTickNextVerifyAt = now + MonoTickVerifyIntervalSeconds;

            long total = Interlocked.Read(ref monoTickCount);
            long deltaTicks = total - this.monoTickLastCount;
            int unityFrame = Time.frameCount;
            int deltaFrames = unityFrame - this.monoTickLastUnityFrame;
            this.monoTickLastCount = total;
            this.monoTickLastUnityFrame = unityFrame;

            if (deltaFrames <= 0)
            {
                return;
            }

            monoTickLastRatio = (float)deltaTicks / deltaFrames;
            ModLogger.Msg("[MonoTick] monoTicks=" + deltaTicks + " unityFrames=" + deltaFrames
                          + " ratio=" + monoTickLastRatio.ToString("F3")
                          + " total=" + total
                          + (Mathf.Abs(monoTickLastRatio - 1f) <= 0.05f
                              ? "  => PER-FRAME (usable as the pump)"
                              : "  => NOT 1:1 (do NOT drive the mod from this)"));
        }
    }
}
