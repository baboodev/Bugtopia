using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HeartopiaMod
{
    // ============================================================================================
    // Push Obstacle Ahead — lets a ChestMove push ignore "Obstacle ahead. Try another direction!"
    // (Localization id 94156, ErrorCode.CantMoveIfObstacle). Separate from the snap fix (94157) and
    // the path box-cast (93200). Default off.
    //
    // ── WHY THE GAME REFUSES ─────────────────────────────────────────────────────────────────────
    // CrafeMode_Moving.Input_ConfirmMoveTowards remaps ErrorCode.HasCollision (92349) to 94156.
    // That value arrives two ways, both after the 0.1 mm snap check (a snap miss returns 94157
    // first and never reaches this toast):
    //   * RayDetection(force) stored HasCollision on the alignment — the downward probe at the
    //     destination overlapped something. The method then skips the confirm and remaps.
    //   * The alignment was Success, and BuildSingle.GenConfirmOptions → IntersectionTesting.Test
    //     returned HasCollision. CheckSuccess does not re-test collision (currency only).
    //
    // ── LEVER ────────────────────────────────────────────────────────────────────────────────────
    // The RayDetection detour (installed for this toggle or the snap fix) rewrites a push-mode
    // HasCollision result to Success, and — only when the snap check will pass, so the method
    // enters its try/finally — arms a one-shot. Two more detours, live only while that arm is set:
    //   * IntersectionTesting.Test(3): a HasCollision return becomes Success. Other codes pass
    //     through. Placing preview is unaffected because the arm is cleared at the start of every
    //     RayDetection, before that call's own Test, and again by BuildFocus.CancelCraftResult,
    //     which the push's finally always runs. The arm is set only when the snap check will pass,
    //     so the method cannot return before that finally.
    //   * BuildFocus.CancelCraftResult(): clears the arm, then calls the original.
    // Off = every call forwards untouched. Hooks install once and stay.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string PushObstacleAheadTag = "PushObstacleAhead";

        // ErrorCode.HasCollision. The toast is the remap to CantMoveIfObstacle (94156).
        internal const int PushObstacleAheadHasCollision = 92349;

        // 0 = not in a push confirm, 1 = armed and not yet counted, 2 = armed and already counted.
        private static int pushObstacleAheadArmed;

        private static readonly string[] PushObstacleAheadImages = { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" };

        // ErrorCode Test(in IntersectionTestContext, List<IBuildBoxElement>, List<XDCollider>)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PushObstacleAheadTestDelegate(IntPtr context, IntPtr collectElements, IntPtr collectColliders);

        // void CancelCraftResult()
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PushObstacleAheadCancelDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PushObstacleAheadCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour pushObstacleAheadTestDetour;
        private static PushObstacleAheadTestDelegate pushObstacleAheadTestKeepAlive;
        private static PushObstacleAheadTestDelegate pushObstacleAheadTestTrampoline;

        private static MonoMod.RuntimeDetour.NativeDetour pushObstacleAheadCancelDetour;
        private static PushObstacleAheadCancelDelegate pushObstacleAheadCancelKeepAlive;
        private static PushObstacleAheadCancelDelegate pushObstacleAheadCancelTrampoline;

        private static volatile bool pushObstacleAheadActive;
        private static int pushObstacleAheadClearedCount;

        private bool pushObstacleAheadEnabled;
        private bool pushObstacleAheadCallbackRegistered;
        private bool pushObstacleAheadTried;
        private int pushObstacleAheadReportedCleared;
        private string pushObstacleAheadStatus = "Idle.";

        private void ProcessPushObstacleAheadOnUpdate()
        {
            // The result rewrite lives in the RayDetection detour. Install that hook for this
            // toggle even when the snap fix itself is off.
            this.RequestPushRayDetectionHook();

            if (!this.pushObstacleAheadEnabled)
            {
                pushObstacleAheadActive = false;
                return;
            }

            if (!this.pushObstacleAheadCallbackRegistered)
            {
                this.pushObstacleAheadCallbackRegistered = true;
                this.RegisterWorldReadyCallback("PushObstacleAhead", this.TryInstallPushObstacleAheadOnWorldReady);
            }

            pushObstacleAheadActive = pushSnapFixTrampoline != null
                && pushObstacleAheadTestTrampoline != null
                && pushObstacleAheadCancelTrampoline != null;

            int cleared = Volatile.Read(ref pushObstacleAheadClearedCount);
            if (cleared != this.pushObstacleAheadReportedCleared)
            {
                if (cleared > 0 && this.pushObstacleAheadReportedCleared == 0)
                {
                    FeatureLog.Life(PushObstacleAheadTag, "first obstacle-ahead push let through this session");
                }
                this.pushObstacleAheadReportedCleared = cleared;
                this.pushObstacleAheadStatus = "Cleared " + cleared + " obstacle(s).";
            }
            else if (pushObstacleAheadActive && cleared == 0)
            {
                this.pushObstacleAheadStatus = "Active.";
            }
        }

        private void SetPushObstacleAheadEnabled(bool value)
        {
            if (value == this.pushObstacleAheadEnabled)
            {
                return;
            }
            this.pushObstacleAheadEnabled = value;
            FeatureLog.Toggle(PushObstacleAheadTag, value);
            if (!value)
            {
                pushObstacleAheadActive = false;
                pushObstacleAheadArmed = 0;
                this.pushObstacleAheadStatus = "Off.";
            }
        }

        // Called from the RayDetection detour once the snap check is known to pass. rewrote means
        // the alignment result was HasCollision and has already been set to Success.
        private static void PushObstacleAheadArm(bool rewrote)
        {
            if (rewrote)
            {
                pushObstacleAheadArmed = 2;
                Interlocked.Increment(ref pushObstacleAheadClearedCount);
                return;
            }
            pushObstacleAheadArmed = 1;
        }

        private bool TryInstallPushObstacleAheadOnWorldReady()
        {
            if ((pushObstacleAheadTestTrampoline != null && pushObstacleAheadCancelTrampoline != null)
                || this.pushObstacleAheadTried)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr testClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", "IntersectionTesting", PushObstacleAheadImages);
                if (testClass == IntPtr.Zero)
                {
                    testClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.IntersectionTesting");
                }
                IntPtr focusClass = this.FindPushObstacleAheadFocusClass();
                if (testClass == IntPtr.Zero || focusClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr testMethod = this.FindAuraMonoMethodOnHierarchy(testClass, "Test", 3);
                IntPtr cancelMethod = this.FindAuraMonoMethodOnHierarchy(focusClass, "CancelCraftResult", 0);
                if (testMethod == IntPtr.Zero || cancelMethod == IntPtr.Zero)
                {
                    return this.DisarmPushObstacleAhead("IntersectionTesting.Test(3) or BuildFocus.CancelCraftResult not found — game update?");
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                PushObstacleAheadCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<PushObstacleAheadCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    return this.DisarmPushObstacleAhead("mono_compile_method unavailable");
                }

                IntPtr testNative = compile(testMethod);
                IntPtr cancelNative = compile(cancelMethod);
                if (testNative == IntPtr.Zero || cancelNative == IntPtr.Zero)
                {
                    return this.DisarmPushObstacleAhead("mono_compile_method returned null");
                }

                if (pushObstacleAheadTestTrampoline == null)
                {
                    pushObstacleAheadTestKeepAlive = PushObstacleAheadTestBody;
                    pushObstacleAheadTestDetour = new MonoMod.RuntimeDetour.NativeDetour(testNative, pushObstacleAheadTestKeepAlive);
                    pushObstacleAheadTestTrampoline = pushObstacleAheadTestDetour.GenerateTrampoline<PushObstacleAheadTestDelegate>();
                    if (pushObstacleAheadTestTrampoline == null)
                    {
                        try { pushObstacleAheadTestDetour.Undo(); } catch { }
                        pushObstacleAheadTestDetour = null;
                        pushObstacleAheadTestKeepAlive = null;
                        return this.DisarmPushObstacleAhead("Test trampoline unavailable; detour reverted");
                    }
                }

                if (pushObstacleAheadCancelTrampoline == null)
                {
                    pushObstacleAheadCancelKeepAlive = PushObstacleAheadCancelBody;
                    pushObstacleAheadCancelDetour = new MonoMod.RuntimeDetour.NativeDetour(cancelNative, pushObstacleAheadCancelKeepAlive);
                    pushObstacleAheadCancelTrampoline = pushObstacleAheadCancelDetour.GenerateTrampoline<PushObstacleAheadCancelDelegate>();
                    if (pushObstacleAheadCancelTrampoline == null)
                    {
                        try { pushObstacleAheadCancelDetour.Undo(); } catch { }
                        pushObstacleAheadCancelDetour = null;
                        pushObstacleAheadCancelKeepAlive = null;
                        return this.DisarmPushObstacleAhead("CancelCraftResult trampoline unavailable; detour reverted");
                    }
                }

                this.pushObstacleAheadTried = true;
                FeatureLog.Life(PushObstacleAheadTag, "hooked IntersectionTesting.Test @0x" + testNative.ToInt64().ToString("X")
                    + " and BuildFocus.CancelCraftResult @0x" + cancelNative.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                try { pushObstacleAheadTestDetour?.Undo(); } catch { }
                try { pushObstacleAheadCancelDetour?.Undo(); } catch { }
                pushObstacleAheadTestDetour = null;
                pushObstacleAheadCancelDetour = null;
                pushObstacleAheadTestKeepAlive = null;
                pushObstacleAheadCancelKeepAlive = null;
                pushObstacleAheadTestTrampoline = null;
                pushObstacleAheadCancelTrampoline = null;
                return this.DisarmPushObstacleAhead("hook install failed: " + ex.Message);
            }
        }

        // Shared with the snap fix: the push finally always calls this, so both arms die here.
        // false = the class is not loaded yet (retry). true = hooked, or the method is gone.
        private bool TryEnsurePushConfirmCancelHook()
        {
            if (pushObstacleAheadCancelTrampoline != null)
            {
                return true;
            }

            IntPtr focusClass = this.FindPushObstacleAheadFocusClass();
            if (focusClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(focusClass, "CancelCraftResult", 0);
            if (method == IntPtr.Zero)
            {
                FeatureLog.Fail(PushObstacleAheadTag, "BuildFocus.CancelCraftResult not found — push zone arm stays off");
                return true;
            }

            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            PushObstacleAheadCompileMethodDelegate compile = monoModule != IntPtr.Zero
                ? this.GetAuraMonoExport<PushObstacleAheadCompileMethodDelegate>(monoModule, "mono_compile_method")
                : null;
            IntPtr nativePtr = compile != null ? compile(method) : IntPtr.Zero;
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            pushObstacleAheadCancelKeepAlive = PushObstacleAheadCancelBody;
            pushObstacleAheadCancelDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, pushObstacleAheadCancelKeepAlive);
            pushObstacleAheadCancelTrampoline = pushObstacleAheadCancelDetour.GenerateTrampoline<PushObstacleAheadCancelDelegate>();
            if (pushObstacleAheadCancelTrampoline == null)
            {
                try { pushObstacleAheadCancelDetour.Undo(); } catch { }
                pushObstacleAheadCancelDetour = null;
                pushObstacleAheadCancelKeepAlive = null;
                return false;
            }

            FeatureLog.Life(PushObstacleAheadTag, "hooked BuildFocus.CancelCraftResult @0x" + nativePtr.ToInt64().ToString("X"));
            return true;
        }

        private IntPtr FindPushObstacleAheadFocusClass()
        {
            string[] names = { "BuildSystemBaseMode/BuildFocus", "BuildSystemBaseMode+BuildFocus", "BuildFocus" };
            for (int i = 0; i < names.Length; i++)
            {
                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", names[i], PushObstacleAheadImages);
                if (cls != IntPtr.Zero)
                {
                    return cls;
                }
            }
            return this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.BuildSystemBaseMode/BuildFocus");
        }

        private bool DisarmPushObstacleAhead(string why)
        {
            this.pushObstacleAheadTried = true;
            this.pushObstacleAheadStatus = "Unavailable on this game version.";
            FeatureLog.Fail(PushObstacleAheadTag, why);
            return true;
        }

        private static int PushObstacleAheadTestBody(IntPtr context, IntPtr collectElements, IntPtr collectColliders)
        {
            PushObstacleAheadTestDelegate trampoline = pushObstacleAheadTestTrampoline;
            int code = trampoline != null ? trampoline(context, collectElements, collectColliders) : 0;
            int state = pushObstacleAheadArmed;
            if (!pushObstacleAheadActive || state == 0 || code != PushObstacleAheadHasCollision)
            {
                return code;
            }
            if (state == 1)
            {
                pushObstacleAheadArmed = 2;
                Interlocked.Increment(ref pushObstacleAheadClearedCount);
            }
            return 0;
        }

        private static void PushObstacleAheadCancelBody(IntPtr self)
        {
            pushObstacleAheadArmed = 0;
            pushSnapFixZoneArmed = 0;
            PushObstacleAheadCancelDelegate trampoline = pushObstacleAheadCancelTrampoline;
            if (trampoline != null)
            {
                trampoline(self);
            }
        }
    }
}
