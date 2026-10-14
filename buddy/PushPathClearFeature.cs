using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HeartopiaMod
{
    // ============================================================================================
    // Push Path Clear — lets a ChestMove push ignore "Obstacles in the movement path."
    // (Localization id 93200, ErrorCode.MovingPathHasBlocked). Separate from Push Snap Fix, which
    // only rewrites the 94157 grid-snap equality. Default off.
    //
    // ── WHY THE GAME REFUSES ─────────────────────────────────────────────────────────────────────
    // After the snap check in CrafeMode_Moving.Input_ConfirmMoveTowards, the push box-casts from
    // the destination back to the piece (half extents = craftBox.boxSize * 0.4, layer
    // LayerMasks.Building). Any non-trigger hit that is not the piece itself sets
    // ErrorCode.MovingPathHasBlocked and the toast is UITipEvent{ tipId = 93200 }.
    //
    // That cast is the only caller of PhysicsExtension.BoxCastNonAlloc with 7 arguments. Vehicle
    // and ice casts use the 4-arg and 8-arg overloads, so hooking this one method does not touch
    // them. Destination overlap (HasCollision → CantMoveIfObstacle 94156, "Obstacle ahead") is a
    // separate toggle, PushObstacleAheadFeature.
    //
    // ── LEVER ────────────────────────────────────────────────────────────────────────────────────
    // One Mono NativeDetour on the 7-arg BoxCastNonAlloc. The body calls the original. While the
    // toggle is on and the original reports one or more hits, the count returned to the game is 0,
    // so the for-loop that assigns 93200 does not run. A clear cast (return 0) is forwarded as-is.
    // Off = every call forwards untouched. Installed once on the world-ready gate, never torn down.
    //
    // ABI (Windows x64 Mono, same shape as PushSnapFix / BuildingFreeRotate): each Vector3 and the
    // Quaternion are >8 bytes and arrive as pointers; the wrapper is an object pointer; float and
    // int past the fourth slot are stack args; the hit count returns in EAX. The body never calls
    // game Mono itself — only the trampoline, then a scalar compare.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string PushPathClearTag = "PushPathClear";

        private static readonly string[] PushPathClearImages = { "EngineWrapper", "EngineWrapper.dll" };

        // int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction,
        //     XDRaycastHitArrayWrapper results, Quaternion orientation, float maxDistance, int layerMask)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PushPathClearBoxCastDelegate(
            IntPtr center, IntPtr halfExtents, IntPtr direction, IntPtr results,
            IntPtr orientation, float maxDistance, int layerMask);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PushPathClearCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour pushPathClearDetour;
        private static PushPathClearBoxCastDelegate pushPathClearKeepAlive;
        private static PushPathClearBoxCastDelegate pushPathClearTrampoline;

        private static volatile bool pushPathClearActive;
        private static int pushPathClearClearedCount;

        private bool pushPathClearEnabled;
        private bool pushPathClearCallbackRegistered;
        private bool pushPathClearTried;
        private int pushPathClearReportedCleared;
        private string pushPathClearStatus = "Idle.";

        private void ProcessPushPathClearOnUpdate()
        {
            if (!this.pushPathClearEnabled)
            {
                pushPathClearActive = false;
                return;
            }

            if (!this.pushPathClearCallbackRegistered)
            {
                this.pushPathClearCallbackRegistered = true;
                this.RegisterWorldReadyCallback("PushPathClear", this.TryInstallPushPathClearOnWorldReady);
            }

            pushPathClearActive = pushPathClearTrampoline != null;

            int cleared = Volatile.Read(ref pushPathClearClearedCount);
            if (cleared != this.pushPathClearReportedCleared)
            {
                if (cleared > 0 && this.pushPathClearReportedCleared == 0)
                {
                    FeatureLog.Life(PushPathClearTag, "first blocked push path let through this session");
                }
                this.pushPathClearReportedCleared = cleared;
                this.pushPathClearStatus = "Cleared " + cleared + " blocked path(s).";
            }
            else if (pushPathClearActive && cleared == 0)
            {
                this.pushPathClearStatus = "Active.";
            }
        }

        private void SetPushPathClearEnabled(bool value)
        {
            if (value == this.pushPathClearEnabled)
            {
                return;
            }
            this.pushPathClearEnabled = value;
            FeatureLog.Toggle(PushPathClearTag, value);
            if (!value)
            {
                pushPathClearActive = false;
                this.pushPathClearStatus = "Off.";
            }
        }

        private bool TryInstallPushPathClearOnWorldReady()
        {
            if (pushPathClearTrampoline != null || this.pushPathClearTried)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "MonoGame.ScriptFramework", "PhysicsExtension", PushPathClearImages);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("MonoGame.ScriptFramework.PhysicsExtension");
                }
                if (cls == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "BoxCastNonAlloc", 7);
                if (method == IntPtr.Zero)
                {
                    return this.DisarmPushPathClear("PhysicsExtension.BoxCastNonAlloc(7) not found — game update?");
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                PushPathClearCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<PushPathClearCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                IntPtr nativePtr = compile != null ? compile(method) : IntPtr.Zero;
                if (nativePtr == IntPtr.Zero)
                {
                    return this.DisarmPushPathClear("mono_compile_method unavailable for BoxCastNonAlloc(7)");
                }

                pushPathClearKeepAlive = PushPathClearBoxCastBody;
                pushPathClearDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, pushPathClearKeepAlive);
                pushPathClearTrampoline = pushPathClearDetour.GenerateTrampoline<PushPathClearBoxCastDelegate>();
                if (pushPathClearTrampoline == null)
                {
                    try { pushPathClearDetour.Undo(); } catch { }
                    pushPathClearDetour = null;
                    pushPathClearKeepAlive = null;
                    return this.DisarmPushPathClear("trampoline unavailable; detour reverted");
                }

                this.pushPathClearTried = true;
                FeatureLog.Life(PushPathClearTag, "hooked PhysicsExtension.BoxCastNonAlloc(7) @0x"
                    + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                try { pushPathClearDetour?.Undo(); } catch { }
                pushPathClearDetour = null;
                pushPathClearKeepAlive = null;
                pushPathClearTrampoline = null;
                return this.DisarmPushPathClear("hook install failed: " + ex.Message);
            }
        }

        private bool DisarmPushPathClear(string why)
        {
            this.pushPathClearTried = true;
            this.pushPathClearStatus = "Unavailable on this game version.";
            FeatureLog.Fail(PushPathClearTag, why);
            return true;
        }

        private static int PushPathClearBoxCastBody(
            IntPtr center, IntPtr halfExtents, IntPtr direction, IntPtr results,
            IntPtr orientation, float maxDistance, int layerMask)
        {
            PushPathClearBoxCastDelegate trampoline = pushPathClearTrampoline;
            int hits = trampoline != null
                ? trampoline(center, halfExtents, direction, results, orientation, maxDistance, layerMask)
                : 0;
            if (pushPathClearActive && hits > 0)
            {
                Interlocked.Increment(ref pushPathClearClearedCount);
                return 0;
            }
            return hits;
        }
    }
}
