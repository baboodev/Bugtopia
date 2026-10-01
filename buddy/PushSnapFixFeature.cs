using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HeartopiaMod
{
    // ============================================================================================
    // Push Snap Fix — lets the "push" interaction on UGC pieces (chess pieces and other ChestMove
    // items) work instead of refusing with "It's too crowded ahead. Try another direction!"
    // (Localization / ErrorCode 94157, ErrorCode.CantMoveIfTooCrowded).
    //
    // ── WHY THE GAME REFUSES ─────────────────────────────────────────────────────────────────────
    // SkillPrecast_ChestMove.ExecuteTask computes destination = piece position + one step along the
    // piece's own axis facing away from the player, then CrafeMode_Moving.Input_ConfirmMoveTowards:
    //
    //   RayDetection(new Ray(destination + 0.1 up, down), _dstPosition, _dstRotation, force: true)
    //   _dstPosition = alignment.position;                    // the destination snapped to the grid
    //   if (!(_dstPosition - destination).XZ().AlmostZero())  // sqrMagnitude < 1e-8, i.e. 0.1 mm
    //       callback(new OperationResult(0, 94157));          // "too crowded"
    //
    // Measured live 2026-10-01 in a home whose field root is yawed 7.2°: the re-detection found the
    // zone (result Success, placeZone set) every time, yet missed by 0.2–0.7 mm from millimetre
    // rounding alone, even along the grid axes — and by 4.3 cm for a piece standing at 45° to the
    // floor grid. So the refusal is a tolerance artefact, not an obstacle: real blocks are separate
    // codes (CantMoveIfObstacle 94156, MovingPathHasBlocked 93200, InValidBuildingZone) and the
    // "no zone at all" branches still return 94157 untouched.
    //
    // ── LEVER ────────────────────────────────────────────────────────────────────────────────────
    // One Mono NativeDetour on BuildSystemBaseMode.RayDetection(in Ray, Vector3, Quaternion, bool),
    // calling the ORIGINAL through its trampoline. Afterwards, and only when ALL of these hold:
    //   * the toggle is on,
    //   * force == true and `this` is exactly a CrafeMode_Moving (the push mode — the only user of
    //     that class is SkillPrecast_ChestMove; the class is read off the object's vtable, no call),
    //   * the detection succeeded (alignment.result == Success),
    //   * the snap moved the point by at most PushSnapFixMaxSnapMetres in XZ,
    // the alignment's XZ is set back to the ray origin's XZ — exactly the destination the game asked
    // for. The check above then passes and the push runs the game's whole path: the path box-cast,
    // GenConfirmCraftResult, CommandSet.CheckSuccess/Apply, GlobalSaving.Save — and, because
    // ExecuteTask now succeeds, CastSkill plays the push animation (Action_Skill_MoveChess ->
    // PlayerMovingChess). A larger snap means the ray landed on a different surface; that is left
    // to the game.
    //
    // Calling the original from the callback is the established shape (InteractObstacleBypass,
    // the EventCenter dispatch detour). The body after it reads two object fields and writes two
    // floats into the alignment — no allocation, no logging, no game-Mono call of our own. Field
    // offsets and the class pointer are resolved once at install; a miss disarms the feature.
    //
    // Installed on the world-ready gate the first time the toggle is on, never torn down
    // (memory: native-detours-world-change-corruption). Off = every call forwards untouched.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string PushSnapFixTag = "PushSnapFix";

        // A grid snap within one push step's rounding; anything further is a different surface.
        private const float PushSnapFixMaxSnapMetres = 0.1f;
        private const float PushSnapFixMaxSnapSq = PushSnapFixMaxSnapMetres * PushSnapFixMaxSnapMetres;

        // What AlmostZero() rejects — a fix is only counted when the game would have refused.
        private const float PushSnapFixRefuseSq = 9.999999E-09f;

        private static readonly string[] PushSnapFixCraftImages = { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" };
        private static readonly string[] PushSnapFixModeImages = { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" };

        // bool RayDetection(in Ray ray, Vector3 dstPosition, Quaternion dstRotation, bool force).
        // Win64: `in Ray` and both >8-byte structs arrive as pointers; bool returns in AL (byte,
        // never Boolean marshalling, which would read all of EAX).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PushSnapFixRayDetectionDelegate(IntPtr self, IntPtr ray, IntPtr dstPosition, IntPtr dstRotation, byte force);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PushSnapFixCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour pushSnapFixDetour;
        private static PushSnapFixRayDetectionDelegate pushSnapFixKeepAlive; // anti-GC
        private static PushSnapFixRayDetectionDelegate pushSnapFixTrampoline;

        // Resolved at install, read by the native body.
        private static IntPtr pushSnapFixMovingClass;
        private static int pushSnapFixCurrentOffset;   // BuildSystemBaseMode._current (Alignment)
        private static int pushSnapFixResultOffset;    // Alignment.result (ErrorCode)
        private static int pushSnapFixPositionOffset;  // Alignment.position (Vector3)

        private static volatile bool pushSnapFixActive;
        private static int pushSnapFixFixedCount;     // pushes the game would have refused
        private static int pushSnapFixSkippedCount;   // snaps too far to be a rounding miss

        private bool pushSnapFixEnabled;
        private bool pushSnapFixCallbackRegistered;
        private bool pushSnapFixTried;
        private int pushSnapFixReportedFixed;
        private int pushSnapFixReportedSkipped;
        private string pushSnapFixStatus = "Idle.";

        private void ProcessPushSnapFixOnUpdate()
        {
            if (!this.pushSnapFixEnabled)
            {
                pushSnapFixActive = false;
                return;
            }

            // Hook installs run on the world-ready gate (AGENTS.md hard rule); registering once is
            // enough, the callback retries itself until the images are up.
            if (!this.pushSnapFixCallbackRegistered)
            {
                this.pushSnapFixCallbackRegistered = true;
                this.RegisterWorldReadyCallback("PushSnapFix", this.TryInstallPushSnapFixOnWorldReady);
            }

            pushSnapFixActive = pushSnapFixTrampoline != null;

            int fixedCount = Volatile.Read(ref pushSnapFixFixedCount);
            int skipped = Volatile.Read(ref pushSnapFixSkippedCount);
            if (fixedCount != this.pushSnapFixReportedFixed || skipped != this.pushSnapFixReportedSkipped)
            {
                if (fixedCount > 0 && this.pushSnapFixReportedFixed == 0)
                {
                    FeatureLog.Life(PushSnapFixTag, "first push let through this session");
                }
                if (skipped != this.pushSnapFixReportedSkipped)
                {
                    FeatureLog.Life(PushSnapFixTag, "left a push to the game: the grid snap moved it more than "
                        + PushSnapFixMaxSnapMetres.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                        + " m (" + skipped + " so far)");
                }
                this.pushSnapFixReportedFixed = fixedCount;
                this.pushSnapFixReportedSkipped = skipped;
                this.pushSnapFixStatus = "Fixed " + fixedCount + " push(es).";
            }
            else if (pushSnapFixActive && fixedCount == 0)
            {
                this.pushSnapFixStatus = "Active.";
            }
        }

        private void SetPushSnapFixEnabled(bool value)
        {
            if (value == this.pushSnapFixEnabled)
            {
                return;
            }
            this.pushSnapFixEnabled = value;
            FeatureLog.Toggle(PushSnapFixTag, value);
            if (!value)
            {
                pushSnapFixActive = false;
                this.pushSnapFixStatus = "Off.";
            }
        }

        // World-ready callback: true when there is nothing left to do (hooked or disarmed for good),
        // false to be retried on a later frame.
        private bool TryInstallPushSnapFixOnWorldReady()
        {
            if (pushSnapFixTrampoline != null || this.pushSnapFixTried)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr baseCls = this.FindAuraMonoClassInImages("XDTLevelAndEntity.Core.Craft", "BuildSystemBaseMode", PushSnapFixCraftImages);
                if (baseCls == IntPtr.Zero)
                {
                    baseCls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.BuildSystemBaseMode");
                }
                IntPtr alignCls = this.FindAuraMonoClassInImages("XDTLevelAndEntity.Core.Craft", "Alignment", PushSnapFixCraftImages);
                if (alignCls == IntPtr.Zero)
                {
                    alignCls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.Alignment");
                }
                IntPtr movingCls = this.FindAuraMonoClassInImages("XDTLevelAndEntity.GameplaySystem.CraftingSystem", "CrafeMode_Moving", PushSnapFixModeImages);
                if (movingCls == IntPtr.Zero)
                {
                    movingCls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.CraftingSystem.CrafeMode_Moving");
                }
                if (baseCls == IntPtr.Zero || alignCls == IntPtr.Zero || movingCls == IntPtr.Zero)
                {
                    return false; // images not loaded yet — retry
                }

                int currentOffset = this.ResolvePushSnapFixFieldOffset(baseCls, "_current");
                int resultOffset = this.ResolvePushSnapFixFieldOffset(alignCls, "result");
                int positionOffset = this.ResolvePushSnapFixFieldOffset(alignCls, "position");
                if (currentOffset <= 0 || resultOffset <= 0 || positionOffset <= 0)
                {
                    return this.DisarmPushSnapFix("field offsets not resolved (_current " + currentOffset
                        + ", result " + resultOffset + ", position " + positionOffset + ") — game update?");
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(baseCls, "RayDetection", 4);
                if (method == IntPtr.Zero)
                {
                    return this.DisarmPushSnapFix("BuildSystemBaseMode.RayDetection(4) not found — game update?");
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                PushSnapFixCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<PushSnapFixCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                IntPtr nativePtr = compile != null ? compile(method) : IntPtr.Zero;
                if (nativePtr == IntPtr.Zero)
                {
                    return this.DisarmPushSnapFix("mono_compile_method unavailable for RayDetection");
                }

                // Published before the detour goes live: the body reads them on its first call.
                pushSnapFixMovingClass = movingCls;
                pushSnapFixCurrentOffset = currentOffset;
                pushSnapFixResultOffset = resultOffset;
                pushSnapFixPositionOffset = positionOffset;

                pushSnapFixKeepAlive = PushSnapFixRayDetectionBody;
                pushSnapFixDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, pushSnapFixKeepAlive);
                pushSnapFixTrampoline = pushSnapFixDetour.GenerateTrampoline<PushSnapFixRayDetectionDelegate>();
                if (pushSnapFixTrampoline == null)
                {
                    // Install rollback, not a live-detour teardown — the only case where Undo is safe.
                    try { pushSnapFixDetour.Undo(); } catch { }
                    pushSnapFixDetour = null;
                    pushSnapFixKeepAlive = null;
                    return this.DisarmPushSnapFix("trampoline unavailable; detour reverted");
                }

                this.pushSnapFixTried = true;
                FeatureLog.Life(PushSnapFixTag, "hooked BuildSystemBaseMode.RayDetection @0x" + nativePtr.ToInt64().ToString("X")
                    + " (_current@" + currentOffset + " result@" + resultOffset + " position@" + positionOffset + ")");
                return true;
            }
            catch (Exception ex)
            {
                try { pushSnapFixDetour?.Undo(); } catch { }
                pushSnapFixDetour = null;
                pushSnapFixKeepAlive = null;
                pushSnapFixTrampoline = null;
                return this.DisarmPushSnapFix("hook install failed: " + ex.Message);
            }
        }

        private bool DisarmPushSnapFix(string why)
        {
            this.pushSnapFixTried = true;
            this.pushSnapFixStatus = "Unavailable on this game version.";
            FeatureLog.Fail(PushSnapFixTag, why);
            return true;
        }

        private int ResolvePushSnapFixFieldOffset(IntPtr cls, string fieldName)
        {
            IntPtr field = this.FindAuraMonoFieldOnHierarchy(cls, fieldName);
            if (field == IntPtr.Zero || auraMonoFieldGetOffset == null)
            {
                return -1;
            }
            return (int)auraMonoFieldGetOffset(field);
        }

        // Native -> coreclr reverse-pinvoke body. Forwards first (exactly vanilla's work), then at
        // most rewrites two floats of the push mode's alignment.
        private static unsafe byte PushSnapFixRayDetectionBody(IntPtr self, IntPtr ray, IntPtr dstPosition, IntPtr dstRotation, byte force)
        {
            PushSnapFixRayDetectionDelegate trampoline = pushSnapFixTrampoline;
            byte found = trampoline != null ? trampoline(self, ray, dstPosition, dstRotation, force) : (byte)0;
            if (!pushSnapFixActive || force == 0 || self == IntPtr.Zero || ray == IntPtr.Zero)
            {
                return found;
            }

            // MonoObject.vtable -> MonoVTable.klass: the object's exact class, no Mono call.
            IntPtr vtable = *(IntPtr*)self;
            if (vtable == IntPtr.Zero || *(IntPtr*)vtable != pushSnapFixMovingClass)
            {
                return found;
            }

            IntPtr alignment = *(IntPtr*)((byte*)self + pushSnapFixCurrentOffset);
            if (alignment == IntPtr.Zero)
            {
                return found;
            }

            byte* a = (byte*)alignment;
            if (*(int*)(a + pushSnapFixResultOffset) != 0)
            {
                return found; // no usable zone there — the game's own answer stands
            }

            float* position = (float*)(a + pushSnapFixPositionOffset);
            float* origin = (float*)ray; // Ray.m_Origin is the first field
            float dx = origin[0] - position[0];
            float dz = origin[2] - position[2];
            float sq = dx * dx + dz * dz;
            if (sq > PushSnapFixMaxSnapSq)
            {
                Interlocked.Increment(ref pushSnapFixSkippedCount);
                return found;
            }
            if (sq >= PushSnapFixRefuseSq)
            {
                position[0] = origin[0];
                position[2] = origin[2];
                Interlocked.Increment(ref pushSnapFixFixedCount);
            }
            return found;
        }
    }
}
