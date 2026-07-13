using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Swim Sprint vertical guard — Space (ascend) / Ctrl (descend) no longer cancel the underwater
    // dash.
    //
    // Root cause (SwimLocomotion.cs:569-597, `SetSwimVerticalInput(bool isAscending, bool
    // isPressed)`): every vertical KEY PRESS unconditionally kills an active sprint
    // (_isSprinting=false, _sprintPhase=None, _sprintTimer=0, PopSprintCamera()). The turn-cancel
    // at :1091 is a separate mechanic and stays untouched.
    //
    // Fix: NativeDetour on the compiled SetSwimVerticalInput with a trampoline (the mod's proven
    // conditional-passthrough shape). While the toggle is ON the hook snapshots the three sprint
    // fields via RAW instance-offset reads (pure pointer math on `this` — no allocation, no Mono
    // calls, hook-body safe), forwards to the original (so the vertical input itself fully
    // applies: buffering, _swimVerticalInput, camera pitch), and if the call flipped the sprint
    // off it writes the snapshot back — the dash continues while ascending/descending.
    // PopSprintCamera has already run inside the original by then, so the sprint camera zoom is
    // lost for the rest of that dash — cosmetic only (re-invoking Push from a native hook is
    // forbidden). Field offsets come from mono_field_get_offset on the CLASS instance (offset is
    // from the object start, header included — usable directly on the object pointer;
    // cf. memory/auramono-struct-field-offsets: the 2*IntPtr subtraction is only for unboxed
    // struct buffers). _sprintPhase is an int-backed enum (None/Accelerating/Decelerating).
    public partial class HeartopiaComplete
    {
        private const string SwimLocomotionTypeName = "XDTLevelAndEntity.Gameplay.Locomotion.SwimLocomotion";

        // Toggle (persisted; default OFF). Drawn under the Custom Swim Sprint sliders.
        private bool swimSprintVerticalGuardEnabled;

        // Mirrored for the native hook (managed tick writes, hook reads).
        private static volatile bool swimSprintVerticalGuardActive;

        // Raw instance offsets on SwimLocomotion (resolved at install; hook does `this + offset`).
        private static int swimSprintOffIsSprinting;
        private static int swimSprintOffSprintPhase;
        private static int swimSprintOffSprintTimer;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SwimVerticalInputHookDelegate(IntPtr self, byte isAscending, byte isPressed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SwimVerticalCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour swimVerticalDetour;
        private static SwimVerticalInputHookDelegate swimVerticalHookKeepAlive; // anti-GC
        private static SwimVerticalInputHookDelegate swimVerticalTrampoline;
        private bool swimVerticalHookTried;
        private float swimVerticalNextAttemptAt = -999f;

        // Called every frame from OnUpdate. Mirrors the toggle into the hook flag and installs the
        // detour lazily once in-world (startup-crash lesson: never resolve/invoke from the menu).
        private void ProcessSwimSprintVerticalGuardOnUpdate()
        {
            if (!this.swimSprintVerticalGuardEnabled)
            {
                swimSprintVerticalGuardActive = false; // installed hook becomes pass-through
                return;
            }

            if (this.GetPlayer() == null)
            {
                swimSprintVerticalGuardActive = false;
                return;
            }

            this.EnsureSwimVerticalGuardHook();
            swimSprintVerticalGuardActive = swimVerticalTrampoline != null;
        }

        // Standard lazy install (Transfer.cs shape): transient failures retry on a 5s cadence,
        // permanent ones burn the tried-flag with one log line. The detour installs once and stays;
        // the toggle only drives the flag. Reverts if the trampoline or any field offset fails —
        // without them the body could not preserve/forward correctly.
        private void EnsureSwimVerticalGuardHook()
        {
            if (swimVerticalTrampoline != null || this.swimVerticalHookTried)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.swimVerticalNextAttemptAt)
            {
                return;
            }
            this.swimVerticalNextAttemptAt = now + 5f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry later
                }

                if (auraMonoFieldGetOffset == null)
                {
                    this.swimVerticalHookTried = true;
                    ModLogger.Msg("[SwimSprintGuard] mono_field_get_offset unavailable — vertical guard idle.");
                    return;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                SwimVerticalCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<SwimVerticalCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.swimVerticalHookTried = true;
                    ModLogger.Msg("[SwimSprintGuard] mono_compile_method unavailable — vertical guard idle.");
                    return;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName(SwimLocomotionTypeName);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInImages(
                        "XDTLevelAndEntity.Gameplay.Locomotion", "SwimLocomotion",
                        new[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
                }
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry on the cadence
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "SetSwimVerticalInput", 2);
                if (method == IntPtr.Zero)
                {
                    this.swimVerticalHookTried = true;
                    ModLogger.Msg("[SwimSprintGuard] SetSwimVerticalInput(2) not found — vertical guard idle (game update?).");
                    return;
                }

                IntPtr fIsSprinting = this.FindAuraMonoFieldOnHierarchy(cls, "_isSprinting");
                IntPtr fPhase = this.FindAuraMonoFieldOnHierarchy(cls, "_sprintPhase");
                IntPtr fTimer = this.FindAuraMonoFieldOnHierarchy(cls, "_sprintTimer");
                if (fIsSprinting == IntPtr.Zero || fPhase == IntPtr.Zero || fTimer == IntPtr.Zero)
                {
                    this.swimVerticalHookTried = true;
                    ModLogger.Msg("[SwimSprintGuard] sprint fields not found — vertical guard idle (game update?).");
                    return;
                }

                swimSprintOffIsSprinting = (int)auraMonoFieldGetOffset(fIsSprinting);
                swimSprintOffSprintPhase = (int)auraMonoFieldGetOffset(fPhase);
                swimSprintOffSprintTimer = (int)auraMonoFieldGetOffset(fTimer);
                if (swimSprintOffIsSprinting <= 0 || swimSprintOffSprintPhase <= 0 || swimSprintOffSprintTimer <= 0)
                {
                    this.swimVerticalHookTried = true;
                    ModLogger.Msg("[SwimSprintGuard] sprint field offsets unreadable — vertical guard idle.");
                    return;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    return; // JIT entry unavailable — retry on the cadence
                }

                this.swimVerticalHookTried = true;
                swimVerticalHookKeepAlive = SwimVerticalInputDetourBody;
                swimVerticalDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, swimVerticalHookKeepAlive);
                swimVerticalTrampoline = swimVerticalDetour.GenerateTrampoline<SwimVerticalInputHookDelegate>();
                if (swimVerticalTrampoline == null)
                {
                    try { swimVerticalDetour?.Undo(); } catch { }
                    swimVerticalDetour = null;
                    swimVerticalHookKeepAlive = null;
                    ModLogger.Msg("[SwimSprintGuard] trampoline unavailable; detour reverted — vertical guard idle.");
                    return;
                }

                ModLogger.Msg("[SwimSprintGuard] Hooked SwimLocomotion.SetSwimVerticalInput @0x" + nativePtr.ToInt64().ToString("X")
                    + " (offsets sprint=" + swimSprintOffIsSprinting + " phase=" + swimSprintOffSprintPhase
                    + " timer=" + swimSprintOffSprintTimer + ") — Space/Ctrl no longer cancel the dash while the toggle is on.");
            }
            catch (Exception ex)
            {
                this.swimVerticalHookTried = true;
                try { swimVerticalDetour?.Undo(); } catch { }
                swimVerticalDetour = null;
                swimVerticalHookKeepAlive = null;
                swimVerticalTrampoline = null;
                ModLogger.Msg("[SwimSprintGuard] hook install failed: " + ex.Message + " — vertical guard idle.");
            }
        }

        // Reverse-pinvoke body compiled game code calls instead of SetSwimVerticalInput. Rules kept:
        // allocation-free, no Mono/Unity/log calls — only raw reads/writes on `this` (rooted by the
        // caller for the duration of the call) plus the trampoline forward (the stock call itself).
        private static unsafe void SwimVerticalInputDetourBody(IntPtr self, byte isAscending, byte isPressed)
        {
            SwimVerticalInputHookDelegate trampoline = swimVerticalTrampoline;
            if (trampoline == null)
            {
                return; // unreachable: the installer reverts the detour when no trampoline exists
            }

            if (!swimSprintVerticalGuardActive || self == IntPtr.Zero)
            {
                trampoline(self, isAscending, isPressed);
                return;
            }

            byte* pIsSprinting = (byte*)self + swimSprintOffIsSprinting;
            bool wasSprinting = *pIsSprinting != 0;
            int savedPhase = 0;
            float savedTimer = 0f;
            if (wasSprinting)
            {
                savedPhase = *(int*)((byte*)self + swimSprintOffSprintPhase);
                savedTimer = *(float*)((byte*)self + swimSprintOffSprintTimer);
            }

            trampoline(self, isAscending, isPressed);

            if (wasSprinting && *pIsSprinting == 0)
            {
                // The vertical press cancelled the dash — undo the cancel. The vertical input
                // itself stays applied (ascend/descend works mid-sprint).
                *pIsSprinting = 1;
                *(int*)((byte*)self + swimSprintOffSprintPhase) = savedPhase;
                *(float*)((byte*)self + swimSprintOffSprintTimer) = savedTimer;
            }
        }
    }
}
