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
    // floor grid. A later push can also land outside the put-zone footprint: alignment.result
    // OutOfPutZoneXZ (309), or GenConfirmOptions → IsOutOfFieldBounds returning 309, both reported
    // as the same 94157 toast. A destination ray that misses the surface is worse: RayPlaceDetection
    // nulls the zone (NoBuildingZone 80064, NotMatchPutFlag 310, or Error 80066) and the confirm
    // then forces 94157 on `placeZone == null` no matter what result says.
    //
    // While this toggle is on, a push ray's XZ is restored at any distance, a 309 result is rewritten
    // to Success, and IsOutOfFieldBounds's 309 is dropped for that confirm. When the destination ray
    // left no usable zone, the original is called again from the piece position this call already
    // received (OnEnterFocus wrote craftBox.position into _dstPosition). The mode's ray is only
    // 0.2 m, so that one call widens _rayDistance, then the alignment XZ is put back on the
    // requested destination. The other
    // push refusals have their own toggles: MovingPathHasBlocked (93200, PushPathClearFeature) and
    // CantMoveIfObstacle (94156, PushObstacleAheadFeature).
    //
    // ── LEVER ────────────────────────────────────────────────────────────────────────────────────
    // One Mono NativeDetour on BuildSystemBaseMode.RayDetection(in Ray, Vector3, Quaternion, bool),
    // calling the ORIGINAL through its trampoline. Afterwards, when the toggle is on and `this` is
    // a CrafeMode_Moving (vtable→klass; the only user is SkillPrecast_ChestMove) with force set,
    // the alignment's XZ is set back to the ray origin at any distance, and an OutOfPutZoneXZ
    // result is rewritten to Success. A null zone is not patched by writing a pointer (no write
    // barrier): the trampoline runs once more from the piece so the game fills selectZone, and only
    // then is XZ restored. A second detour on BuildSingle.IsOutOfFieldBounds drops a fresh 309 from
    // GenConfirmOptions for that confirm only. The push then runs the game's whole path: the path
    // box-cast, GenConfirmCraftResult, CommandSet.CheckSuccess/Apply, GlobalSaving.Save — and,
    // because ExecuteTask succeeds, CastSkill plays the push animation.
    //
    // Calling the original from the callback is the established shape (InteractObstacleBypass,
    // the EventCenter dispatch detour). The body reads object fields and writes floats into the
    // alignment — no allocation, no logging, no game-Mono call besides the trampoline. Field
    // offsets and the class pointer are resolved once at install; a miss disarms the feature.
    // A miss on the piece-matrix offsets leaves the null-zone half off and the rest of the hook up.
    //
    // Installed on the world-ready gate the first time the toggle is on, never torn down
    // (memory: native-detours-world-change-corruption). Off = every call forwards untouched.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string PushSnapFixTag = "PushSnapFix";

        // What AlmostZero() rejects — a fix is only counted when the game would have refused.
        private const float PushSnapFixRefuseSq = 9.999999E-09f;

        // ErrorCode.OutOfPutZoneXZ. Input_ConfirmMoveTowards remaps this to 94157.
        private const int PushSnapFixOutOfPutZoneXZ = 309;

        // ErrorCode.AlignmentInValidGrid. Localization id 346 is "Out of placement range."
        // DetectAdsorption returns it when the snapped cell misses the put zone. 314 is a
        // different toast ("Target outside Home boundaries").
        private const int PushOutOfRangeInvalidGrid = 346;

        // A piece-ray hit farther than this from the piece is not the surface under it.
        private const float PushSnapFixPieceRaySq = 4f;

        // UnityEngine.Matrix4x4 translation: m03, m13, m23 at float indices 12..14.
        private const int PushSnapFixMatrixPos = 12;

        // Set for the push confirm that follows a RayDetection. Cleared by CancelCraftResult.
        private static int pushSnapFixZoneArmed;

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

        // ErrorCode IsOutOfFieldBounds(in Vector3, in Quaternion, IBuildZoneElement, BoxSide)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PushSnapFixBoundsDelegate(IntPtr self, IntPtr position, IntPtr rotation, IntPtr placeTo, int side);

        private static MonoMod.RuntimeDetour.NativeDetour pushSnapFixBoundsDetour;
        private static PushSnapFixBoundsDelegate pushSnapFixBoundsKeepAlive;
        private static PushSnapFixBoundsDelegate pushSnapFixBoundsTrampoline;
        private bool pushSnapFixBoundsMissing;
        private bool pushSnapFixPieceMatrixMissing;

        // Resolved at install, read by the native body.
        private static IntPtr pushSnapFixMovingClass;
        private static int pushSnapFixCurrentOffset;   // BuildSystemBaseMode._current (Alignment)
        private static int pushSnapFixResultOffset;    // Alignment.result (ErrorCode)
        private static int pushSnapFixPositionOffset;  // Alignment.position (Vector3)
        private static int pushSnapFixCraftBoxOffset = -1;       // BuildSystemBaseMode.craftBox
        private static int pushSnapFixRenderMatrixOffset = -1;   // BuildFocus._renderMatrix
        private static int pushSnapFixRayDistanceOffset = -1;    // CrafeMode_Moving._rayDistance (0.2 m)

        private static volatile bool pushSnapFixActive;
        private static volatile bool pushOutOfRangeActive;
        private static int pushOutOfRangeClearedCount;
        private static int pushSnapFixFixedCount;     // pushes the game would have refused
        private static int pushSnapFixLastMissCode;   // piece re-ray error, 0 = none yet
        private static int pushSnapFixProbeCode;

        private bool pushSnapFixEnabled;
        private bool pushOutOfRangeEnabled;
        private int pushOutOfRangeReported;
        private string pushOutOfRangeStatus = "Idle.";
        private bool pushSnapFixCallbackRegistered;
        private bool pushSnapFixTried;
        private int pushSnapFixReportedFixed;
        private int pushSnapFixReportedMiss;
        private string pushSnapFixStatus = "Idle.";

        private void RequestPushRayDetectionHook()
        {
            if (this.pushSnapFixCallbackRegistered)
            {
                return;
            }
            if (!this.pushSnapFixEnabled && !this.pushObstacleAheadEnabled && !this.pushOutOfRangeEnabled)
            {
                return;
            }
            // Hook installs run on the world-ready gate (AGENTS.md hard rule); registering once is
            // enough, the callback retries itself until the images are up. The obstacle-ahead toggle
            // rewrites the same RayDetection result, so it requests this hook on its own.
            this.pushSnapFixCallbackRegistered = true;
            this.RegisterWorldReadyCallback("PushSnapFix", this.TryInstallPushSnapFixOnWorldReady);
        }

        private void ProcessPushSnapFixOnUpdate()
        {
            this.RequestPushRayDetectionHook();
            pushOutOfRangeActive = this.pushOutOfRangeEnabled
                && pushSnapFixBoundsTrampoline != null
                && pushObstacleAheadCancelTrampoline != null;
            int rangeCount = Volatile.Read(ref pushOutOfRangeClearedCount);
            if (rangeCount != this.pushOutOfRangeReported)
            {
                if (rangeCount > 0 && this.pushOutOfRangeReported == 0)
                {
                    FeatureLog.Life("PushOutOfRange", "first out-of-range push let through this session");
                }
                this.pushOutOfRangeReported = rangeCount;
                this.pushOutOfRangeStatus = "Cleared " + rangeCount + " push(es).";
            }
            else if (pushOutOfRangeActive && rangeCount == 0)
            {
                this.pushOutOfRangeStatus = "Active.";
            }
            else if (!this.pushOutOfRangeEnabled)
            {
                this.pushOutOfRangeStatus = "Off.";
            }

            if (!this.pushSnapFixEnabled)
            {
                pushSnapFixActive = false;
                return;
            }

            pushSnapFixActive = pushSnapFixTrampoline != null;

            int missCode = Volatile.Read(ref pushSnapFixLastMissCode);
            if (missCode != 0 && missCode != this.pushSnapFixReportedMiss)
            {
                this.pushSnapFixReportedMiss = missCode;
                FeatureLog.Fail(PushSnapFixTag, "piece re-ray left error " + missCode);
            }

            int fixedCount = Volatile.Read(ref pushSnapFixFixedCount);
            if (fixedCount != this.pushSnapFixReportedFixed)
            {
                if (fixedCount > 0 && this.pushSnapFixReportedFixed == 0)
                {
                    FeatureLog.Life(PushSnapFixTag, "first push let through this session");
                }
                this.pushSnapFixReportedFixed = fixedCount;
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
                if (!this.pushOutOfRangeEnabled)
                {
                    pushSnapFixZoneArmed = 0;
                }
                this.pushSnapFixStatus = "Off.";
            }
        }

        private void SetPushOutOfRangeEnabled(bool value)
        {
            if (value == this.pushOutOfRangeEnabled)
            {
                return;
            }
            this.pushOutOfRangeEnabled = value;
            FeatureLog.Toggle("PushOutOfRange", value);
            if (!value)
            {
                pushOutOfRangeActive = false;
                if (!this.pushSnapFixEnabled)
                {
                    pushSnapFixZoneArmed = 0;
                }
                this.pushOutOfRangeStatus = "Off.";
            }
        }

        // World-ready callback: true when there is nothing left to do (hooked or disarmed for good),
        // false to be retried on a later frame.
        private bool TryInstallPushSnapFixOnWorldReady()
        {
            bool hooksReady = pushSnapFixTrampoline != null
                && pushSnapFixBoundsTrampoline != null
                && pushObstacleAheadCancelTrampoline != null;
            bool pieceReady = pushSnapFixRenderMatrixOffset > 0 || this.pushSnapFixPieceMatrixMissing;
            if (this.pushSnapFixTried || (hooksReady && pieceReady))
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                if (pushSnapFixTrampoline != null)
                {
                    if (!this.TryInstallPushSnapFixBoundsHook() || !this.TryEnsurePushConfirmCancelHook())
                    {
                        return false;
                    }
                    if (!this.TryResolvePushSnapFixPieceMatrix())
                    {
                        return false;
                    }
                    this.pushSnapFixTried = true;
                    return true;
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
                int rayDistanceOffset = this.ResolvePushSnapFixFieldOffset(movingCls, "_rayDistance");
                if (rayDistanceOffset <= 0)
                {
                    FeatureLog.Fail(PushSnapFixTag, "_rayDistance offset " + rayDistanceOffset
                        + " — piece re-ray stays at the game's 0.2 m");
                }

                pushSnapFixMovingClass = movingCls;
                pushSnapFixCurrentOffset = currentOffset;
                pushSnapFixResultOffset = resultOffset;
                pushSnapFixPositionOffset = positionOffset;
                pushSnapFixRayDistanceOffset = rayDistanceOffset;

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

                FeatureLog.Life(PushSnapFixTag, "hooked BuildSystemBaseMode.RayDetection @0x" + nativePtr.ToInt64().ToString("X")
                    + " (_current@" + currentOffset + " result@" + resultOffset + " position@" + positionOffset + ")");
                if (!this.TryInstallPushSnapFixBoundsHook() || !this.TryEnsurePushConfirmCancelHook())
                {
                    return false; // ray hook stays; retry the two companions next gate
                }
                if (!this.TryResolvePushSnapFixPieceMatrix())
                {
                    return false; // companions stay; retry the piece matrix next gate
                }
                this.pushSnapFixTried = true;
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
        // most rewrites the push mode's alignment: HasCollision → Success for the obstacle-ahead
        // toggle, and two XZ floats for the snap fix.
        private static unsafe byte PushSnapFixRayDetectionBody(IntPtr self, IntPtr ray, IntPtr dstPosition, IntPtr dstRotation, byte force)
        {
            // Drop a leaked arm before the original runs, so this call's own collision test is real.
            if (pushObstacleAheadTestTrampoline != null)
            {
                pushObstacleAheadArmed = 0;
            }
            pushSnapFixZoneArmed = 0;

            // OnEnterFocus stored craftBox.position in this argument, and the original does not
            // write it back. Captured before the trampoline so a later write cannot hide the piece.
            float pieceX = 0f;
            float pieceY = 0f;
            float pieceZ = 0f;
            if (dstPosition != IntPtr.Zero)
            {
                float* piece = (float*)dstPosition;
                pieceX = piece[0];
                pieceY = piece[1];
                pieceZ = piece[2];
            }

            PushSnapFixRayDetectionDelegate trampoline = pushSnapFixTrampoline;
            byte found = trampoline != null ? trampoline(self, ray, dstPosition, dstRotation, force) : (byte)0;
            if (force == 0 || self == IntPtr.Zero || ray == IntPtr.Zero)
            {
                return found;
            }
            if (!pushSnapFixActive && !pushObstacleAheadActive && !pushOutOfRangeActive)
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
            int result = *(int*)(a + pushSnapFixResultOffset);
            bool rewroteCollision = false;
            if (pushObstacleAheadActive && result == PushObstacleAheadHasCollision)
            {
                *(int*)(a + pushSnapFixResultOffset) = 0;
                result = 0;
                rewroteCollision = true;
            }

            float* origin = (float*)ray; // Ray.m_Origin is the first field
            // A non-success here is a destination with no zone the confirm will report as 94157.
            // Re-detect from the piece position this call was given, so the game fills placeZone,
            // then the block below puts XZ back on the requested destination. The extra trampoline
            // swaps _current, so the alignment pointer is re-read when that happened.
            bool swapped = false;
            if (pushSnapFixActive && result != 0 && trampoline != null && dstPosition != IntPtr.Zero
                && PushSnapFixRerayFromPiece(self, trampoline, dstPosition, dstRotation, force, pieceX, pieceY, pieceZ, origin[0], origin[2], result, out swapped))
            {
                found = 1;
                rewroteCollision = false;
                alignment = *(IntPtr*)((byte*)self + pushSnapFixCurrentOffset);
                if (alignment == IntPtr.Zero)
                {
                    return found;
                }
                a = (byte*)alignment;
                result = *(int*)(a + pushSnapFixResultOffset);
            }
            else if (swapped)
            {
                alignment = *(IntPtr*)((byte*)self + pushSnapFixCurrentOffset);
                if (alignment == IntPtr.Zero)
                {
                    return found;
                }
                a = (byte*)alignment;
                result = *(int*)(a + pushSnapFixResultOffset);
            }

            float* position = (float*)(a + pushSnapFixPositionOffset);
            float dx = origin[0] - position[0];
            float dz = origin[2] - position[2];
            float sq = dx * dx + dz * dz;
            bool already = sq < PushSnapFixRefuseSq;
            if (pushSnapFixActive && found != 0)
            {
                // Any distance. A snap past 0.1 m used to fall through to the 94157 toast.
                // Count only a push that actually leaves Success — an XZ write on a null zone
                // still toasts, and the re-ray already counted itself.
                bool changed = false;
                if (!already)
                {
                    position[0] = origin[0];
                    position[2] = origin[2];
                    changed = true;
                }
                // OutOfPutZoneXZ is reported as the same toast, from the alignment and again
                // from GenConfirmOptions. The second site is IsOutOfFieldBounds, armed below.
                if (result == PushSnapFixOutOfPutZoneXZ)
                {
                    *(int*)(a + pushSnapFixResultOffset) = 0;
                    result = 0;
                    changed = true;
                }
                if (changed && result == 0)
                {
                    Interlocked.Increment(ref pushSnapFixFixedCount);
                }
            }

            // 346 is the toast, and it is stored on the alignment before GenConfirm. Zeroing it
            // here is what removes the tip; IsOutOfFieldBounds never sees this code. XZ has to
            // match too, or the snap check turns it into 94157.
            bool passedSnap = pushSnapFixActive || already;
            if (pushOutOfRangeActive && result == PushOutOfRangeInvalidGrid)
            {
                position[0] = origin[0];
                position[2] = origin[2];
                *(int*)(a + pushSnapFixResultOffset) = 0;
                result = 0;
                found = 1;
                passedSnap = true;
                Interlocked.Increment(ref pushOutOfRangeClearedCount);
            }

            if (result != 0)
            {
                return found; // no usable zone there — the game's own answer stands
            }

            // GenConfirmOptions calls IsOutOfFieldBounds only after this confirm gets past the
            // snap check. Arming earlier leaks into the next preview, because an AlmostZero miss
            // returns before CancelCraftResult.
            if (passedSnap
                && (pushSnapFixActive || pushOutOfRangeActive)
                && pushSnapFixBoundsTrampoline != null
                && pushObstacleAheadCancelTrampoline != null)
            {
                pushSnapFixZoneArmed = 1;
            }

            // AlmostZero fails (and returns before the finally) unless the snap toggle just
            // restored XZ or the point was already on the destination.
            if (pushObstacleAheadActive && (pushSnapFixActive || already))
            {
                PushObstacleAheadArm(rewroteCollision);
            }
            return found;
        }

        // false = focus class not loaded yet, retry. true = offsets published, or the fields are
        // gone on this build (null-zone half stays off, the XZ / 309 half still runs).
        private bool TryResolvePushSnapFixPieceMatrix()
        {
            if (pushSnapFixRenderMatrixOffset > 0 || this.pushSnapFixPieceMatrixMissing)
            {
                return true;
            }

            IntPtr baseCls = this.FindAuraMonoClassInImages("XDTLevelAndEntity.Core.Craft", "BuildSystemBaseMode", PushSnapFixCraftImages);
            if (baseCls == IntPtr.Zero)
            {
                baseCls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.BuildSystemBaseMode");
            }
            IntPtr focusCls = this.FindPushObstacleAheadFocusClass();
            if (baseCls == IntPtr.Zero || focusCls == IntPtr.Zero)
            {
                return false;
            }

            int boxOffset = this.ResolvePushSnapFixFieldOffset(baseCls, "craftBox");
            int matrixOffset = this.ResolvePushSnapFixFieldOffset(focusCls, "_renderMatrix");
            if (boxOffset <= 0 || matrixOffset <= 0)
            {
                this.pushSnapFixPieceMatrixMissing = true;
                FeatureLog.Fail(PushSnapFixTag, "piece matrix offsets not resolved (craftBox " + boxOffset
                    + ", _renderMatrix " + matrixOffset + ") — null-zone half of the toast stays");
                return true;
            }

            pushSnapFixCraftBoxOffset = boxOffset;
            pushSnapFixRenderMatrixOffset = matrixOffset;
            FeatureLog.Life(PushSnapFixTag, "piece matrix craftBox@" + boxOffset + " _renderMatrix@" + matrixOffset);
            return true;
        }

        // Runs the original again from the piece so a missed destination still gets a placeZone.
        // The moving mode's ray is only 0.2 m, so this call widens _rayDistance for that one
        // probe and starts 0.1 m above the piece. True only when the hit is on the piece and its
        // code can carry a zone (Success, 309, HasCollision).
        private static unsafe bool PushSnapFixRerayFromPiece(
            IntPtr self,
            PushSnapFixRayDetectionDelegate trampoline,
            IntPtr dstPosition,
            IntPtr dstRotation,
            byte force,
            float pieceX,
            float pieceY,
            float pieceZ,
            float destX,
            float destZ,
            int failedResult,
            out bool swapped)
        {
            swapped = false;
            bool hit = PushSnapFixProbePiece(self, trampoline, dstPosition, dstRotation, force, pieceX, pieceY + 0.1f, pieceZ, destX, destZ, failedResult, out swapped);
            if (!hit)
            {
                Volatile.Write(ref pushSnapFixLastMissCode, pushSnapFixProbeCode);
            }
            return hit;
        }

        private static unsafe bool PushSnapFixProbePiece(
            IntPtr self,
            PushSnapFixRayDetectionDelegate trampoline,
            IntPtr dstPosition,
            IntPtr dstRotation,
            byte force,
            float originX,
            float originY,
            float originZ,
            float destX,
            float destZ,
            int failedResult,
            out bool swapped)
        {
            float* ray = stackalloc float[6];
            ray[0] = originX;
            ray[1] = originY;
            ray[2] = originZ;
            ray[3] = 0f;
            ray[4] = -1f;
            ray[5] = 0f;
            swapped = true;
            // The mode only casts 0.2 m. Widen it so a pivot above the floor still reaches the zone,
            // then put the game's distance back before anyone else reads it.
            float* distanceSlot = null;
            float savedDistance = 0f;
            if (pushSnapFixRayDistanceOffset > 0)
            {
                distanceSlot = (float*)((byte*)self + pushSnapFixRayDistanceOffset);
                savedDistance = *distanceSlot;
                *distanceSlot = 4f;
            }
            try
            {
                trampoline(self, (IntPtr)ray, dstPosition, dstRotation, force);
            }
            finally
            {
                if (distanceSlot != null)
                {
                    *distanceSlot = savedDistance;
                }
            }

            IntPtr alignment = *(IntPtr*)((byte*)self + pushSnapFixCurrentOffset);
            if (alignment == IntPtr.Zero)
            {
                pushSnapFixProbeCode = -1;
                return false;
            }

            byte* a = (byte*)alignment;
            int code = *(int*)(a + pushSnapFixResultOffset);
            pushSnapFixProbeCode = code;
            float* position = (float*)(a + pushSnapFixPositionOffset);
            float dx = position[0] - originX;
            float dz = position[2] - originZ;
            bool near = dx * dx + dz * dz <= PushSnapFixPieceRaySq;
            bool invalidGrid = code == PushOutOfRangeInvalidGrid;
            bool usable = code == 0 || code == PushSnapFixOutOfPutZoneXZ || code == PushObstacleAheadHasCollision
                || (invalidGrid && pushOutOfRangeActive);
            if (!near || !usable)
            {
                if (usable || (invalidGrid && !near))
                {
                    *(int*)(a + pushSnapFixResultOffset) = failedResult;
                }
                return false;
            }

            position[0] = destX;
            position[2] = destZ;
            *(int*)(a + pushSnapFixResultOffset) = 0;
            if (invalidGrid)
            {
                Interlocked.Increment(ref pushOutOfRangeClearedCount);
            }
            else
            {
                Interlocked.Increment(ref pushSnapFixFixedCount);
            }
            return true;
        }

        // false = images not up yet, retry. true = hooked or permanently unavailable.
        private bool TryInstallPushSnapFixBoundsHook()
        {
            if (pushSnapFixBoundsTrampoline != null || this.pushSnapFixBoundsMissing)
            {
                return true;
            }

            IntPtr cls = this.FindAuraMonoClassInImages("XDTLevelAndEntity.Core.Craft", "BuildSingle", PushSnapFixCraftImages);
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Core.Craft.BuildSingle");
            }
            if (cls == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "IsOutOfFieldBounds", 4);
            if (method == IntPtr.Zero)
            {
                this.pushSnapFixBoundsMissing = true;
                FeatureLog.Fail(PushSnapFixTag, "BuildSingle.IsOutOfFieldBounds(4) not found — put-zone half of the toast stays");
                return true;
            }

            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            PushSnapFixCompileMethodDelegate compile = monoModule != IntPtr.Zero
                ? this.GetAuraMonoExport<PushSnapFixCompileMethodDelegate>(monoModule, "mono_compile_method")
                : null;
            IntPtr nativePtr = compile != null ? compile(method) : IntPtr.Zero;
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            pushSnapFixBoundsKeepAlive = PushSnapFixBoundsBody;
            pushSnapFixBoundsDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, pushSnapFixBoundsKeepAlive);
            pushSnapFixBoundsTrampoline = pushSnapFixBoundsDetour.GenerateTrampoline<PushSnapFixBoundsDelegate>();
            if (pushSnapFixBoundsTrampoline == null)
            {
                try { pushSnapFixBoundsDetour.Undo(); } catch { }
                pushSnapFixBoundsDetour = null;
                pushSnapFixBoundsKeepAlive = null;
                return false;
            }

            FeatureLog.Life(PushSnapFixTag, "hooked BuildSingle.IsOutOfFieldBounds @0x" + nativePtr.ToInt64().ToString("X"));
            return true;
        }

        private static int PushSnapFixBoundsBody(IntPtr self, IntPtr position, IntPtr rotation, IntPtr placeTo, int side)
        {
            PushSnapFixBoundsDelegate trampoline = pushSnapFixBoundsTrampoline;
            int code = trampoline != null ? trampoline(self, position, rotation, placeTo, side) : 0;
            if (pushSnapFixZoneArmed != 0 && pushSnapFixActive && code == PushSnapFixOutOfPutZoneXZ)
            {
                return 0;
            }
            return code;
        }
    }
}
