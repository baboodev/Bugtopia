using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Build-mode transform levers for the homeland build panel: the god-mode free X/Y/Z jog,
    // the build-plane height (SetPlaneHeight) and the free rotation jog, all driven through
    // GodControl/BuildModule via AuraMono.
    //
    // A second, server-side approach ("Plan B": pack an arbitrary angle into
    // BuildTransformData.Angle and send BuildMoveData, bypassing the client 45/90 snap in
    // CraftMath.ReducePrecision) was drafted here and never wired to a panel; it was deleted
    // 2026-08-07. The format analysis it rested on is still in
    // docs/plans/2026-06-10-pad-build-api-migration.md if it is ever wanted again.
    public partial class HeartopiaComplete
    {

        // Floor height (god-mode build plane). SetPlaneHeight(offset) raises _standardHeight so placement
        // floats at the chosen height above the field floor. Applied on change; god-mode only.
        private float buildingFloorHeight;       // world-Y (height above floor) jog of the focused object
        private float buildingFloorHeightApplied;
        private float buildingFreeZ;             // world-Z jog of the focused object (god mode)
        private float buildingFreeZApplied;
        private float buildingFreeX;             // world-X jog of the focused object (god mode)
        private float buildingFreeXApplied;
        private float buildingPlaneHeight;       // god-mode build-plane height (SetPlaneHeight, 0..24)
        private float buildingPlaneHeightApplied = -1f;
        private float buildingRotX;              // free rotation jog around the field-local axes (god mode)
        private float buildingRotXApplied;
        private float buildingRotY;
        private float buildingRotYApplied;
        private float buildingRotZ;
        private float buildingRotZApplied;

        // God-mode camera WASD pan. BuildModule._buildCamera (BuildFreeCamera).Move(Vector2) pans the
        // camera in the XZ plane along the flattened forward/right (the same path mouse-drag uses).
        private IntPtr godCamMoveMethod = IntPtr.Zero;
        private IntPtr godCamMoveDirectMethod = IntPtr.Zero; // BuildFreeCamera._Move(Vector3) — raw position set
        private const float GodCameraMoveSpeed = 500f;       // horizontal pan units/sec (game-scaled inside Move)
        private const float GodCameraVerticalSpeed = 6f;     // Space/Ctrl vertical, world units/sec

        // Auto move-panel: appears while CraftState.Focus (object grabbed/being moved) and the menu is closed.
        private bool buildingMovePanelActive;
        private bool buildingMovePanelGodMode;   // BuildModule.InGodMode (gates the X/Y/Z jog sliders)
        // (buildingMovePanelMouseOver/Rect are gone with the IMGUI panel — the UGUI panel,
        // HeartopiaComplete.UguiBuildingContent.cs, owns its own window state and registers its
        // own floating input-ownership surface.)
        private float buildingMovePanelNextPollAt = -999f;
        private int buildingMovePanelSubState = -1;
        private Vector3 buildingMovePanelObjPos;
        private float buildingMovePanelObjYaw;
        private bool buildingMovePanelHasPos;

        // Free-snap toggles. While on + an object focused, the focused BuildComponent's snap config
        // is overridden to the finest step: angle = _buildBoxData.putDatas[0].rotateAngle (the 45/90
        // step source, used by interactive rotate, alignment, and confirm-ReducePrecision), grid =
        // _putitem.precision (cell = Clamp(precision,1,8)*0.25, min 0.25 m). Both are shared config
        // ref-objects, so originals are cached per object and restored when the toggle turns off.
        private int buildingFreeAngleStep = 1;     // user-set angle step (deg), 1..90
        private float buildingFreeGridCell = 0.25f; // user-set cell size (m), 0.01..0.25
        private bool buildingFreeAngleEnabled;
        private bool buildingFreeGridEnabled;
        private bool buildingFreeAnglePrev;
        private bool buildingFreeGridPrev;
        private float buildingFreeSnapNextApplyAt = -999f;
        private readonly System.Collections.Generic.Dictionary<IntPtr, int> buildingAngleOriginals = new System.Collections.Generic.Dictionary<IntPtr, int>();
        private readonly System.Collections.Generic.Dictionary<IntPtr, float> buildingGridOriginals = new System.Collections.Generic.Dictionary<IntPtr, float>();

        // Cell-size patch. The grid toggle's precision field-write (above) can only reach 0.25 m,
        // because CraftMath.PrecisionToCellSize floors the cell at Clamp(precision,1,8)*0.25. CraftMath
        // has no managed interop stub (docs/TYPE_RESOLUTION.md), so Harmony can't see it — but the build
        // logic runs on the embedded Mono runtime (proven: our AuraMono field-writes change placement).
        // So we resolve the Mono method via AuraMono, take its JIT-compiled native entry
        // (mono_compile_method, NOT the unmanaged thunk — see memory auramono-native-hook-and-settings),
        // and install a MonoMod NativeDetour (Iced-based relocation, not a hand-rolled byte steal).
        //
        // The detour is installed once and left in place: PrecisionToCellSize is a pure function, so the
        // hook fully reimplements it. When buildingFreeCellOverride == 0 it reproduces the original
        // exactly (pass-through); when > 0 it returns (v,v,v), giving a true sub-0.25 m cell.
        //
        // ABI (Windows x64, Mono static valuetype return): a 12-byte Vector3 is returned via a hidden
        // buffer pointer in the first integer slot (RCX), and `float precision` lands in XMM1. Hence the
        // native delegate is (IntPtr retBuf, float precision) -> IntPtr (returns the buffer, as in RAX).
        // The hook touches only the static override + System math — no Unity/Il2Cpp calls (GC/thread-safe).
        private static float buildingFreeCellOverride;
        private bool buildingCellPatchTried;
        private static MonoMod.RuntimeDetour.NativeDetour buildingCellDetour;
        private static PrecisionToCellSizeHookDelegate buildingCellHookDelegate; // keep alive (anti-GC)
        private static BuildingMonoCompileMethodDelegate buildingMonoCompileMethod;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PrecisionToCellSizeHookDelegate(IntPtr retBuf, float precision);

        // mono_compile_method returns the native code pointer; AuraFarm's shared delegate is declared
        // void-return (it only forces JIT), so we declare our own IntPtr-returning variant here.
        private delegate IntPtr BuildingMonoCompileMethodDelegate(IntPtr method);

        // Surface-limit bypass. "Target exceeds available placement surface" = ErrorCode.OutOfPutZoneXZ
        // (309). On the placing path it is raised by Alignment.DetectAlignmentWithoutCheckZone when no
        // aligned cell fits the put-zone area (line ~755), gated by _IsAlignmentPosInArea. We detour
        // that single leaf predicate and force it true (the object's footprint always "fits"):
        //   _IsAlignmentPosInArea(Vector3, in Quaternion, BuildFocus) -> bool ⇒ return true.
        // DetectCollisionAndFieldArea still runs afterwards, so collisions and homeland bounds stay
        // enforced (only the put-zone surface limit is lifted).
        //
        // CRITICAL safety lesson (this hung+crashed the first time): the hook MUST NOT call back into
        // game-Mono code from the reverse-pinvoke callback. A previous design also detoured
        // DetectAlignment and called the original via a trampoline (+ re-ran WCZ) every placing tick —
        // those coreCLR→Mono re-entries on the game thread froze then crashed the process (cf. memory
        // coreclr-coroutine-bridge-gc). So the hook is a pure constant (return 1, like the cell hook),
        // and we APPLY/UNDO the detour with the toggle instead of branching to the original inside it.
        // The hook only runs while applied (= toggle on), so the unconditional true is correct.
        // (The size gate at DetectAlignment line ~622 is left intact — it only blocks an object wholly
        //  larger than the surface, which the area gate above does not cover; revisit only if needed.)
        private static bool buildingIgnoreSurfaceLimit;
        private bool buildingSurfacePatchTried;                              // detour created once
        private static MonoMod.RuntimeDetour.NativeDetour buildingInAreaDetour;
        private static AlignmentInAreaDelegate buildingInAreaHook;           // anti-GC

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte AlignmentInAreaDelegate(IntPtr self, IntPtr worldPos, IntPtr quatRef, IntPtr placeBox);

        // Range/height/area-limit bypass. The placing confirm shows UITipEvent{ tipId = (int)ErrorCode }
        // for homeland-bounds failures — "Out of placement range" / "Current plane exceeds Home height
        // limit" / "Target position exceeds the Restricted area" (ErrorCode.OutOfHomeLandXZ /
        // OutOfHomeLandY / OutOfLayerHeight / AreaLocked), ALL returned by
        // OutOfBoundsTesting.Test(in OutOfBoundsTestContext)->ErrorCode (AreaLocked comes from its
        // InUnlockPartition check inside IsOutOfFieldZone). We detour that single static and force
        // ErrorCode.Success → no tip is dispatched and the client range/height/area gate is lifted.
        // Same callback-free constant + Apply/Undo pattern as the surface detour. ABI: static, `in` struct
        // arg = pointer, enum return = int in RAX, so the native delegate is (IntPtr ctx) -> int → return 0.
        private static bool buildingIgnoreRangeHeight;
        private bool buildingRangePatchTried;
        private static MonoMod.RuntimeDetour.NativeDetour buildingRangeDetour;
        private static OutOfBoundsTestDelegate buildingRangeHook;            // anti-GC

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OutOfBoundsTestDelegate(IntPtr ctx);

        // Overlap bypass — replaces the old IL2CPP Harmony PhysicsManager.Overlap* patch (GameAssembly
        // .text) with two Mono NativeDetours (Mono JIT heap, NOT a module .text → off the anti-cheat
        // integrity surface). The IL2CPP PhysicsManager.Overlap* is reached only via icalls from the
        // Mono craft path, so hooking the Mono decision gate lifts collision the same way. Apply/Undo per
        // toggle; callback-free constant hooks (see the surface-patch freeze/crash lesson above).
        //  1) IntersectionTesting.Test(in ctx, List, List) -> ErrorCode ⇒ 0 (Success): the SINGLE
        //     collision chokepoint under BOTH the preview gate (Alignment/IsPlacingCollisionSafe) and the
        //     confirm gate (Gen*ConfirmOption -> IsCollisionSafe). Do NOT hook TestAndCollectElements
        //     (AggressiveInlining forwarder — may inline and orphan the detour).
        //  2) BuildSingle.OverlapCompleteWithSlab(out IBuildBoxElement) -> bool ⇒ out null + true: kills
        //     the slab-on-slab confirm deny (ErrorCode.WallOverlapFail). Guarded independently so a
        //     slab-resolve miss still leaves the primary hook active.
        private bool buildingOverlapPatchTried;
        private static MonoMod.RuntimeDetour.NativeDetour buildingOverlapDetour;
        private static IntersectionTestDelegate buildingOverlapHook;         // anti-GC
        private bool buildingSlabOverlapPatchTried;
        private static MonoMod.RuntimeDetour.NativeDetour buildingSlabOverlapDetour;
        private static SlabOverlapDelegate buildingSlabOverlapHook;          // anti-GC

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int IntersectionTestDelegate(IntPtr ctx, IntPtr collectElements, IntPtr collectColliders);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte SlabOverlapDelegate(IntPtr self, IntPtr outOverlapElement);



        // Rotate the FOCUSED object by deltaDeg around the field-local axis. Writes GodControl._dstRotation
        // (the focus tick slerps to it and confirm packs all 3 euler axes — ToBuildingRotValue). localAxis
        // is mapped to world via the field-root rotation so the axes match the panel coordinate frame.
        private unsafe bool TryRotateFocused(Vector3 localAxis, float deltaDeg)
        {
            if (Mathf.Approximately(deltaDeg, 0f))
            {
                return false;
            }
            if (auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null
                || auraMonoFieldGetValue == null || auraMonoFieldSetValue == null)
            {
                return false;
            }
            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj) || moduleObj == IntPtr.Zero)
            {
                return false;
            }
            if (!(this.TryGetMonoBoolMember(moduleObj, "InGodMode", out bool inGod) && inGod))
            {
                this.BuildingLog("rot: not god mode");
                return false;
            }
            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr godCtrl, "get_GodControl") || godCtrl == IntPtr.Zero)
            {
                this.BuildingLog("rot: GodControl unavailable");
                return false;
            }

            Vector3 worldAxis = localAxis;
            if (this.TryGetFocusedFieldRootRotation(out Quaternion rootRot))
            {
                worldAxis = rootRot * localAxis;
            }

            try
            {
                IntPtr cls = auraMonoObjectGetClass(godCtrl);
                IntPtr field = auraMonoClassGetFieldFromName(cls, "_dstRotation");
                if (field == IntPtr.Zero)
                {
                    this.BuildingLog("rot: _dstRotation field not found");
                    return false;
                }
                Quaternion q = Quaternion.identity;
                auraMonoFieldGetValue(godCtrl, field, (IntPtr)(&q));
                q = Quaternion.AngleAxis(deltaDeg, worldAxis) * q;
                auraMonoFieldSetValue(godCtrl, field, (IntPtr)(&q));
                this.BuildingLog("rot: _dstRotation += " + deltaDeg + "° around " + localAxis);
                return true;
            }
            catch (Exception ex)
            {
                this.BuildingLog("rot exception: " + ex.Message);
                return false;
            }
        }

        // WASD pans the god-mode camera in the XZ plane (mirrors the mouse drag-pan). Only acts in god
        // mode and when no keys are held it does no AuraMono work. Gated off while the mod menu is open.
        private void ProcessGodCameraMoveOnUpdate()
        {
            // "Menu open" = any MODAL registry surface (the UGUI shell) — showMenu is retired.
            if (this.IsAnyModalInputSurfaceOpen())
            {
                return;
            }
            float x = 0f, ydir = 0f;
            if (Input.GetKey(KeyCode.W)) ydir -= 1f; // forward
            if (Input.GetKey(KeyCode.S)) ydir += 1f; // back
            if (Input.GetKey(KeyCode.A)) x += 1f;    // left
            if (Input.GetKey(KeyCode.D)) x -= 1f;    // right
            if (x != 0f || ydir != 0f)
            {
                this.TryGodCameraMove(new Vector2(x, ydir) * (GodCameraMoveSpeed * Time.unscaledDeltaTime));
            }

            float dy = 0f;
            if (Input.GetKey(KeyCode.Space)) dy += 1f;                                       // up
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) dy -= 1f; // down
            if (dy != 0f)
            {
                this.TryGodCameraVertical(dy * GodCameraVerticalSpeed * Time.unscaledDeltaTime);
            }
        }

        // Move the god camera vertically by dy world units (Space up / Ctrl down). Writes the camera
        // group position via BuildFreeCamera._Move (the canonical setter); the camera Update leaves an
        // Idle/zero-velocity position untouched, so it holds.
        private unsafe bool TryGodCameraVertical(float dy)
        {
            if (auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }
            if (!this.TryGetPadBuildAuraModule(out IntPtr module) || module == IntPtr.Zero)
            {
                return false;
            }
            if (!(this.TryGetMonoBoolMember(module, "InGodMode", out bool g) && g))
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(module, "_buildCamera", out IntPtr cam) || cam == IntPtr.Zero
                || !this.TryGetMonoObjectMember(cam, "_freeCameraGroup", out IntPtr group) || group == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryReadBuildingVector3Prop(group, "position", out Vector3 pos))
            {
                return false;
            }

            try
            {
                if (this.godCamMoveDirectMethod == IntPtr.Zero)
                {
                    this.godCamMoveDirectMethod = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(cam), "_Move", 1);
                }
                if (this.godCamMoveDirectMethod == IntPtr.Zero)
                {
                    return false;
                }
                Vector3 newPos = new Vector3(pos.x, pos.y + dy, pos.z);
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&newPos);
                auraMonoRuntimeInvoke(this.godCamMoveDirectMethod, cam, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryGodCameraMove(Vector2 delta)
        {
            if (auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }
            if (!this.TryGetPadBuildAuraModule(out IntPtr module) || module == IntPtr.Zero)
            {
                return false;
            }
            if (!(this.TryGetMonoBoolMember(module, "InGodMode", out bool g) && g))
            {
                return false; // build free camera only exists in god mode
            }
            if (!this.TryGetMonoObjectMember(module, "_buildCamera", out IntPtr cam) || cam == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (this.godCamMoveMethod == IntPtr.Zero)
                {
                    this.godCamMoveMethod = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(cam), "Move", 1);
                }
                if (this.godCamMoveMethod == IntPtr.Zero)
                {
                    return false;
                }
                Vector2 d = delta;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&d);
                auraMonoRuntimeInvoke(this.godCamMoveMethod, cam, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        // Reset the X/Y/Z position + rotation jog sliders to 0 without moving the object. Called on the
        // confirm hotkey so the next placement starts fresh.
        private void ResetBuildingAxisSliders()
        {
            this.buildingFloorHeight = 0f;
            this.buildingFloorHeightApplied = 0f;
            this.buildingFreeX = 0f;
            this.buildingFreeXApplied = 0f;
            this.buildingFreeZ = 0f;
            this.buildingFreeZApplied = 0f;
            this.buildingRotX = 0f;
            this.buildingRotXApplied = 0f;
            this.buildingRotY = 0f;
            this.buildingRotYApplied = 0f;
            this.buildingRotZ = 0f;
            this.buildingRotZApplied = 0f;
        }


        // Poll the build module's CraftState (throttled) to drive the auto move-panel visibility, and
        // while focused refresh the object's live local coordinates for the panel readout.
        private void UpdateBuildingMovePanelState()
        {
            float now = Time.unscaledTime;
            if (now < this.buildingMovePanelNextPollAt)
            {
                return;
            }
            this.buildingMovePanelNextPollAt = now + 0.1f;

            bool active = false;
            if (this.TryGetPadBuildAuraModule(out IntPtr module) && module != IntPtr.Zero)
            {
                this.buildingMovePanelGodMode = this.TryGetMonoBoolMember(module, "InGodMode", out bool g) && g;
                if (this.TryGetPadBuildAuraSubState(module, out int sub))
                {
                    this.buildingMovePanelSubState = sub;
                    active = sub == 2; // CraftState.Focus — an object is focused / being moved
                }
            }
            this.buildingMovePanelActive = active;

            if (active && this.TryReadFocusedTransformQuiet(out Vector3 pos, out float yaw))
            {
                this.buildingMovePanelObjPos = pos;
                this.buildingMovePanelObjYaw = yaw;
                this.buildingMovePanelHasPos = true;
            }
            else
            {
                this.buildingMovePanelHasPos = false;
                this.ResetBuildingAxisSliders(); // focus ended → next object starts fresh
            }
        }

        // Quiet (no logging) read of the focused object's local position + yaw, for the live panel
        // readout. Resolves fresh each call (no pointers held across frames) — same chain as the
        // focused-object resolver but without the diagnostic logging that would spam at poll rate.
        private bool TryReadFocusedTransformQuiet(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;

            if (!this.TryGetBuildingFocusedElementQuiet(out IntPtr elementObj) || elementObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr entityObj;
            if ((!this.TryGetMonoObjectMember(elementObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(elementObj, out entityObj, "get_entity") || entityObj == IntPtr.Zero))
            {
                return false;
            }

            if (!this.TryReadBuildingVector3Prop(entityObj, "localPosition", out pos))
            {
                return false;
            }

            if (this.TryReadBuildingQuaternionProp(entityObj, "localRotation", out Quaternion q))
            {
                yaw = q.eulerAngles.y;
            }
            return true;
        }

        // Move the FOCUSED object by `localDelta` metres along the field-root LOCAL axes (the same frame
        // the panel coordinates are shown in). In god mode the focus tick lerps the object toward
        // GodControl._dstPosition and does NOT recompute it while idle, so we add to _dstPosition and it
        // stays (Focus_Confirm saves it). _dstPosition is WORLD-space and the field root is yaw-rotated
        // vs world, so we rotate localDelta into world first (else X/Z come out swapped / diagonal).
        private unsafe bool TryNudgeFocused(Vector3 localDelta)
        {
            if (localDelta == Vector3.zero)
            {
                return false;
            }
            if (auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null
                || auraMonoFieldGetValue == null || auraMonoFieldSetValue == null)
            {
                this.BuildingLog("nudge: AuraMono not ready");
                return false;
            }
            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj) || moduleObj == IntPtr.Zero)
            {
                this.BuildingLog("nudge: BuildModule unavailable");
                return false;
            }
            bool god = this.TryGetMonoBoolMember(moduleObj, "InGodMode", out bool inGod) && inGod;
            if (!god)
            {
                this.BuildingLog("nudge: not god mode (focused-object move is god-mode only)");
                return false;
            }
            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr godCtrl, "get_GodControl") || godCtrl == IntPtr.Zero)
            {
                this.BuildingLog("nudge: GodControl unavailable");
                return false;
            }

            // Convert the local-frame delta to world via the field root's rotation.
            Vector3 worldDelta = localDelta;
            if (this.TryGetFocusedFieldRootRotation(out Quaternion rootRot))
            {
                worldDelta = rootRot * localDelta;
            }

            try
            {
                IntPtr cls = auraMonoObjectGetClass(godCtrl);
                bool ok = this.TryNudgeMonoVector3Field(godCtrl, cls, "_dstPosition", worldDelta);
                this.TryNudgeMonoVector3Field(godCtrl, cls, "_rotatePosition", worldDelta); // keep rotate pivot aligned
                this.BuildingLog(ok ? ("nudge: local " + localDelta + " -> world " + worldDelta) : "nudge: _dstPosition field not found");
                return ok;
            }
            catch (Exception ex)
            {
                this.BuildingLog("nudge exception: " + ex.Message);
                return false;
            }
        }

        // Field-root world rotation = focused entity's worldRot * inverse(localRot). Maps the field's
        // local axes (panel-coordinate frame) to world, so a per-axis slider moves the right axis.
        private bool TryGetFocusedFieldRootRotation(out Quaternion rootRot)
        {
            rootRot = Quaternion.identity;
            if (!this.TryGetBuildingFocusedElementQuiet(out IntPtr element) || element == IntPtr.Zero)
            {
                return false;
            }
            IntPtr entity;
            if ((!this.TryGetMonoObjectMember(element, "entity", out entity) || entity == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(element, out entity, "get_entity") || entity == IntPtr.Zero))
            {
                return false;
            }
            if (!this.TryReadBuildingQuaternionProp(entity, "rotation", out Quaternion worldRot)
                || !this.TryReadBuildingQuaternionProp(entity, "localRotation", out Quaternion localRot))
            {
                return false;
            }
            rootRot = worldRot * Quaternion.Inverse(localRot);
            return true;
        }

        // Raise the god-mode build plane: BuildModule.SetPlaneHeight(float offset, bool setCamera). The
        // placing ray then hits the virtual plane at _basePlaneHeight+offset, so NEW objects place at
        // that height. God-mode only. Value-type args passed as pointers to the unboxed value.
        private unsafe bool TrySetBuildingPlaneHeight(float offset)
        {
            if (auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }
            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj) || moduleObj == IntPtr.Zero)
            {
                this.BuildingLog("plane: BuildModule unavailable");
                return false;
            }
            if (!(this.TryGetMonoBoolMember(moduleObj, "InGodMode", out bool inGod) && inGod))
            {
                this.BuildingLog("plane: not god mode");
                return false;
            }

            try
            {
                IntPtr cls = auraMonoObjectGetClass(moduleObj);
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "SetPlaneHeight", 2);
                if (method == IntPtr.Zero)
                {
                    this.BuildingLog("plane: SetPlaneHeight(2) not found");
                    return false;
                }

                float off = offset;
                byte setCamera = 1;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&off);
                args[1] = (IntPtr)(&setCamera);
                auraMonoRuntimeInvoke(method, moduleObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.BuildingLog("plane: SetPlaneHeight exc");
                    return false;
                }
                this.BuildingLog("plane: SetPlaneHeight(" + offset + ")");
                return true;
            }
            catch (Exception ex)
            {
                this.BuildingLog("plane exception: " + ex.Message);
                return false;
            }
        }

        // Read a Vector3 instance field, add the delta vector, write it back. Returns false if not found.
        private unsafe bool TryNudgeMonoVector3Field(IntPtr obj, IntPtr cls, string fieldName, Vector3 delta)
        {
            IntPtr field = auraMonoClassGetFieldFromName(cls, fieldName);
            if (field == IntPtr.Zero)
            {
                return false;
            }
            Vector3 v = default(Vector3);
            auraMonoFieldGetValue(obj, field, (IntPtr)(&v));
            v += delta;
            auraMonoFieldSetValue(obj, field, (IntPtr)(&v));
            return true;
        }

        private void BuildingLog(string message)
        {
            this.PadBuildHotkeyLog("[Building] " + message);
        }

        private bool TryGetPadBuildAuraSubState(IntPtr moduleObj, out int subState)
        {
            subState = -1;
            if (moduleObj == IntPtr.Zero || this.padBuildAuraGetSubStateMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(this.padBuildAuraGetSubStateMethod, moduleObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return false;
                }
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }
                unsafe { subState = *(byte*)raw; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryReadBuildingVector3Prop(IntPtr obj, string propName, out Vector3 value)
        {
            value = Vector3.zero;
            if (!this.TryInvokeAuraMonoZeroArg(obj, out IntPtr boxed, "get_" + propName) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(Vector3*)raw;
            return true;
        }

        private unsafe bool TryReadBuildingQuaternionProp(IntPtr obj, string propName, out Quaternion value)
        {
            value = Quaternion.identity;
            if (!this.TryInvokeAuraMonoZeroArg(obj, out IntPtr boxed, "get_" + propName) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(Quaternion*)raw;
            return true;
        }

        // --- Free-snap toggles: override focused object's angle/grid config -----------------------

        private void UpdateBuildingFreeSnapOverrides()
        {
            // Drive the static cell-size override every frame (the Harmony prefix reads it). When the
            // grid toggle is on, install the patch lazily and force the slider's cell; off ⇒ 0 (pass).
            if (this.buildingFreeGridEnabled)
            {
                this.EnsureBuildingCellPatch();
                buildingFreeCellOverride = Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f);
            }
            else
            {
                buildingFreeCellOverride = 0f;
            }

            // Surface-limit bypass: apply the detour while the toggle is on, undo it when off
            // (independent of the angle/grid toggles, which the early-return below gates on).
            if (buildingIgnoreSurfaceLimit)
            {
                this.EnsureBuildingSurfacePatch();
            }
            else if (buildingInAreaDetour != null)
            {
                this.RemoveBuildingSurfacePatch();
            }

            if (buildingIgnoreRangeHeight)
            {
                this.EnsureBuildingRangePatch();
            }
            else if (buildingRangeDetour != null)
            {
                this.RemoveBuildingRangePatch();
            }

            // Overlap bypass: Mono detours applied while the toggle is on, undone when off.
            if (this.bypassOverlapEnabled)
            {
                this.EnsureBuildingOverlapPatch();
            }
            else if (buildingOverlapDetour != null || buildingSlabOverlapDetour != null)
            {
                this.RemoveBuildingOverlapPatch();
            }

            bool anyOn = this.buildingFreeAngleEnabled || this.buildingFreeGridEnabled;
            bool anyCached = this.buildingAngleOriginals.Count > 0 || this.buildingGridOriginals.Count > 0;
            if (!anyOn && !anyCached)
            {
                return;
            }

            // Restore-on-toggle-off (edge): when a toggle goes on→off, put the originals back.
            if (this.buildingFreeAnglePrev && !this.buildingFreeAngleEnabled)
            {
                this.RestoreBuildingAngleOriginals();
            }
            if (this.buildingFreeGridPrev && !this.buildingFreeGridEnabled)
            {
                this.RestoreBuildingGridOriginals();
            }
            this.buildingFreeAnglePrev = this.buildingFreeAngleEnabled;
            this.buildingFreeGridPrev = this.buildingFreeGridEnabled;

            if (!this.buildingFreeAngleEnabled && !this.buildingFreeGridEnabled)
            {
                return;
            }

            // Throttle the focus resolve + apply (cheap, runs every frame otherwise).
            float now = Time.unscaledTime;
            if (now < this.buildingFreeSnapNextApplyAt)
            {
                return;
            }
            this.buildingFreeSnapNextApplyAt = now + 0.2f;

            if (!this.TryGetBuildingFocusedElementQuiet(out IntPtr elementObj) || elementObj == IntPtr.Zero)
            {
                return;
            }

            if (this.buildingFreeAngleEnabled)
            {
                this.TryApplyBuildingFreeAngle(elementObj);
            }
            if (this.buildingFreeGridEnabled)
            {
                this.TryApplyBuildingFreeGrid(elementObj);
            }
        }

        // BuildComponent._buildBoxData.putDatas[0] (BuildBoxData, a class) -> set rotateAngle.
        private unsafe void TryApplyBuildingFreeAngle(IntPtr elementObj)
        {
            try
            {
                if (!this.TryGetMonoObjectMember(elementObj, "_buildBoxData", out IntPtr scriptDataObj) || scriptDataObj == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(scriptDataObj, "putDatas", out IntPtr arrObj) || arrObj == IntPtr.Zero
                    || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
                {
                    return;
                }
                if (auraMonoArrayLength(arrObj).ToUInt64() == 0UL)
                {
                    return;
                }
                IntPtr slot = auraMonoArrayAddrWithSize(arrObj, IntPtr.Size, UIntPtr.Zero);
                IntPtr boxDataObj = slot != IntPtr.Zero ? Marshal.ReadIntPtr(slot) : IntPtr.Zero;
                if (boxDataObj == IntPtr.Zero)
                {
                    return;
                }

                int step = Mathf.Clamp(this.buildingFreeAngleStep, 1, 90);
                if (!this.buildingAngleOriginals.ContainsKey(boxDataObj))
                {
                    if (this.TryGetMonoInt32Member(boxDataObj, "rotateAngle", out int orig))
                    {
                        this.buildingAngleOriginals[boxDataObj] = orig;
                        this.BuildingLog("free-angle: override rotateAngle " + orig + "->" + step);
                    }
                }
                this.TrySetBuildingIntField(boxDataObj, "rotateAngle", step);
            }
            catch (Exception ex)
            {
                this.BuildingLog("free-angle exc: " + ex.Message);
            }
        }

        // BuildComponent._putitem (TablePutitem) -> set precision (cell size source).
        private void TryApplyBuildingFreeGrid(IntPtr elementObj)
        {
            try
            {
                if (!this.TryGetMonoObjectMember(elementObj, "_putitem", out IntPtr putitemObj) || putitemObj == IntPtr.Zero)
                {
                    return;
                }
                // Fallback when the CraftMath.PrecisionToCellSize patch isn't active: cell =
                // ToCellSize(precision) = Clamp(precision,1,8)*0.25 → precision = cell/0.25. The engine
                // floors cell at 0.25 m here; true sub-0.25 m comes from BuildingPrecisionToCellSizePrefix.
                float precision = Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f) / 0.25f;
                if (!this.buildingGridOriginals.ContainsKey(putitemObj))
                {
                    if (this.TryGetMonoSingleMember(putitemObj, "precision", out float orig))
                    {
                        this.buildingGridOriginals[putitemObj] = orig;
                        this.BuildingLog("free-grid: override precision " + orig + "->" + precision + " (cell~" + this.buildingFreeGridCell + ")");
                    }
                }
                this.TrySetBuildingFloatField(putitemObj, "precision", precision);
            }
            catch (Exception ex)
            {
                this.BuildingLog("free-grid exc: " + ex.Message);
            }
        }

        private void RestoreBuildingAngleOriginals()
        {
            foreach (System.Collections.Generic.KeyValuePair<IntPtr, int> kv in this.buildingAngleOriginals)
            {
                this.TrySetBuildingIntField(kv.Key, "rotateAngle", kv.Value);
            }
            this.BuildingLog("free-angle: restored " + this.buildingAngleOriginals.Count + " original(s)");
            this.buildingAngleOriginals.Clear();
        }

        private void RestoreBuildingGridOriginals()
        {
            foreach (System.Collections.Generic.KeyValuePair<IntPtr, float> kv in this.buildingGridOriginals)
            {
                this.TrySetBuildingFloatField(kv.Key, "precision", kv.Value);
            }
            this.BuildingLog("free-grid: restored " + this.buildingGridOriginals.Count + " original(s)");
            this.buildingGridOriginals.Clear();
        }

        private unsafe void TrySetBuildingIntField(IntPtr obj, string fieldName, int value)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldSetValue == null)
            {
                return;
            }
            IntPtr field = auraMonoClassGetFieldFromName(auraMonoObjectGetClass(obj), fieldName);
            if (field != IntPtr.Zero)
            {
                int v = value;
                auraMonoFieldSetValue(obj, field, (IntPtr)(&v));
            }
        }

        private unsafe void TrySetBuildingFloatField(IntPtr obj, string fieldName, float value)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldSetValue == null)
            {
                return;
            }
            IntPtr field = auraMonoClassGetFieldFromName(auraMonoObjectGetClass(obj), fieldName);
            if (field != IntPtr.Zero)
            {
                float v = value;
                auraMonoFieldSetValue(obj, field, (IntPtr)(&v));
            }
        }

        // Lightweight, non-logging resolve of the focused BuildComponent (element).
        private bool TryGetBuildingFocusedElementQuiet(out IntPtr elementObj)
        {
            elementObj = IntPtr.Zero;
            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj))
            {
                return false;
            }
            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr craftBoxObj, "GetCraftBox") || craftBoxObj == IntPtr.Zero)
            {
                return false;
            }
            IntPtr buildObj;
            if ((!this.TryInvokeAuraMonoZeroArg(craftBoxObj, out buildObj, "get_buildObject") || buildObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(craftBoxObj, "buildObject", out buildObj) || buildObj == IntPtr.Zero))
            {
                return false;
            }
            if ((!this.TryGetMonoObjectMember(buildObj, "element", out elementObj) || elementObj == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(buildObj, out elementObj, "get_Element", "get_element") || elementObj == IntPtr.Zero))
            {
                elementObj = IntPtr.Zero;
                return false;
            }
            return true;
        }

        // Resolve CraftMath.PrecisionToCellSize(float) on the embedded Mono runtime and install a
        // MonoMod NativeDetour onto its JIT-compiled entry. Lazy; retried until AuraMono is ready and
        // the class/method resolve (transient misses do not burn buildingCellPatchTried).
        private void EnsureBuildingCellPatch()
        {
            if (this.buildingCellPatchTried)
            {
                return;
            }
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet (e.g. not in-world) — retry on a later frame.
                }

                // CraftMath is compiled into the XDTDataAndProtocol image (namespace ≠ assembly).
                IntPtr craftMath = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", "CraftMath",
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (craftMath == IntPtr.Zero)
                {
                    craftMath = this.FindAuraMonoClassInAllLoadedImages("CraftMath", "XDTLevelAndEntity.Core.Craft");
                }
                if (craftMath == IntPtr.Zero)
                {
                    return; // image may not be loaded yet — retry later.
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(craftMath, "PrecisionToCellSize", 1);
                if (method == IntPtr.Zero)
                {
                    this.buildingCellPatchTried = true; // class found but no such method — permanent.
                    this.BuildingLog("cell-patch: PrecisionToCellSize(1 arg) not found on CraftMath");
                    return;
                }

                // mono_compile_method → native code entry. Resolve our IntPtr-returning delegate once.
                if (buildingMonoCompileMethod == null)
                {
                    IntPtr monoModule = this.GetAuraMonoModuleHandle();
                    if (monoModule != IntPtr.Zero)
                    {
                        buildingMonoCompileMethod = this.GetAuraMonoExport<BuildingMonoCompileMethodDelegate>(monoModule, "mono_compile_method");
                    }
                }
                if (buildingMonoCompileMethod == null)
                {
                    this.buildingCellPatchTried = true;
                    this.BuildingLog("cell-patch: mono_compile_method export unavailable");
                    return;
                }

                IntPtr nativePtr = buildingMonoCompileMethod(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.buildingCellPatchTried = true;
                    this.BuildingLog("cell-patch: mono_compile_method returned null");
                    return;
                }

                buildingCellHookDelegate = BuildingPrecisionToCellSizeNative; // anti-GC: keep alive
                buildingCellDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, buildingCellHookDelegate);
                this.buildingCellPatchTried = true;
                this.BuildingLog("cell-patch: NativeDetour installed on CraftMath.PrecisionToCellSize @ 0x" + nativePtr.ToString("X") + " (sub-0.25 m grid enabled)");
            }
            catch (Exception ex)
            {
                this.buildingCellPatchTried = true; // don't loop on a hard failure (e.g. detour throw)
                this.BuildingLog("cell-patch failed: " + ex.Message);
            }
        }

        // Native detour body for Mono CraftMath.PrecisionToCellSize(float) -> Vector3.
        // Windows x64 sret ABI: retBuf is the hidden return-buffer pointer, precision is the arg.
        // When buildingFreeCellOverride > 0 we force (v,v,v); otherwise we reproduce the original
        // formula EXACTLY so the game behaves identically while the detour stays installed.
        // No Unity/Il2Cpp calls here — only the static field + System math (GC/thread-safe).
        private static unsafe IntPtr BuildingPrecisionToCellSizeNative(IntPtr retBuf, float precision)
        {
            try
            {
                if (retBuf == IntPtr.Zero)
                {
                    return retBuf;
                }
                float x, y, z;
                float v = buildingFreeCellOverride;
                if (v > 0f)
                {
                    x = y = z = v;
                }
                else if (precision > 100f)
                {
                    int num = (int)Math.Round((double)precision, MidpointRounding.ToEven);
                    x = BuildingToCellSize(num / 100);
                    y = BuildingToCellSize(num % 100 / 10);
                    z = BuildingToCellSize(num % 10);
                }
                else
                {
                    x = y = z = BuildingToCellSize(precision);
                }
                float* p = (float*)retBuf;
                p[0] = x;
                p[1] = y;
                p[2] = z;
            }
            catch
            {
                // Never let a native callback throw across the Mono boundary.
            }
            return retBuf;
        }

        // Mirror of CraftMath's local ToCellSize: (int)Clamp(value,1,8) * 0.25f. System math only.
        private static float BuildingToCellSize(float value)
        {
            float c = value < 1f ? 1f : (value > 8f ? 8f : value);
            return (int)c * 0.25f;
        }

        // Resolve a method on a Mono class and JIT-compile it to its native entry pointer.
        private IntPtr ResolveBuildingMonoNative(IntPtr cls, string method, int argc)
        {
            IntPtr m = this.FindAuraMonoMethodOnHierarchy(cls, method, argc);
            if (m == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            if (buildingMonoCompileMethod == null)
            {
                IntPtr mod = this.GetAuraMonoModuleHandle();
                if (mod != IntPtr.Zero)
                {
                    buildingMonoCompileMethod = this.GetAuraMonoExport<BuildingMonoCompileMethodDelegate>(mod, "mono_compile_method");
                }
            }
            return buildingMonoCompileMethod != null ? buildingMonoCompileMethod(m) : IntPtr.Zero;
        }

        // Create the _IsAlignmentPosInArea detour once (lazy; retried until AuraMono is up and the
        // class/method resolve) and ensure it is APPLIED. Apply/Undo (not an in-hook branch) is how we
        // honour the toggle — see the field-block comment on why the hook must stay callback-free.
        private void EnsureBuildingSurfacePatch()
        {
            try
            {
                if (buildingInAreaDetour != null)
                {
                    if (!buildingInAreaDetour.IsApplied)
                    {
                        buildingInAreaDetour.Apply();
                    }
                    return;
                }
                if (this.buildingSurfacePatchTried)
                {
                    return; // creation already failed permanently
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry on a later frame
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", "Alignment",
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInAllLoadedImages("Alignment", "XDTLevelAndEntity.Core.Craft");
                }
                if (cls == IntPtr.Zero)
                {
                    return; // image may not be loaded yet — retry later
                }

                IntPtr inAreaPtr = this.ResolveBuildingMonoNative(cls, "_IsAlignmentPosInArea", 3);
                if (inAreaPtr == IntPtr.Zero)
                {
                    this.buildingSurfacePatchTried = true;
                    this.BuildingLog("surface-patch: _IsAlignmentPosInArea(3) not resolved");
                    return;
                }

                buildingInAreaHook = BuildingInAreaNative;
                buildingInAreaDetour = new MonoMod.RuntimeDetour.NativeDetour(inAreaPtr, buildingInAreaHook);
                this.buildingSurfacePatchTried = true;
                this.BuildingLog("surface-patch: detour installed on Alignment._IsAlignmentPosInArea (applied)");
            }
            catch (Exception ex)
            {
                this.buildingSurfacePatchTried = true;
                this.BuildingLog("surface-patch failed: " + ex.Message);
            }
        }

        // Undo the detour (toggle off) — leaves the NativeDetour object reusable for a later Apply.
        private void RemoveBuildingSurfacePatch()
        {
            try
            {
                if (buildingInAreaDetour != null && buildingInAreaDetour.IsApplied)
                {
                    buildingInAreaDetour.Undo();
                    this.BuildingLog("surface-patch: detour undone (surface limit re-enforced)");
                }
            }
            catch (Exception ex)
            {
                this.BuildingLog("surface-patch undo failed: " + ex.Message);
            }
        }

        // Detour body: the position always counts as inside the put-zone area. Callback-free constant
        // (only installed/applied while the toggle is on). Ignores its args, so the value-type arg ABI
        // is irrelevant — it just returns 1 in AL.
        private static byte BuildingInAreaNative(IntPtr self, IntPtr worldPos, IntPtr quatRef, IntPtr placeBox)
        {
            return 1;
        }

        // Create the OutOfBoundsTesting.Test detour once and ensure it is APPLIED (toggle on). Same
        // lazy/Apply pattern as the surface detour. Forces Success → range/height tips are suppressed.
        private void EnsureBuildingRangePatch()
        {
            try
            {
                if (buildingRangeDetour != null)
                {
                    if (!buildingRangeDetour.IsApplied)
                    {
                        buildingRangeDetour.Apply();
                    }
                    return;
                }
                if (this.buildingRangePatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", "OutOfBoundsTesting",
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInAllLoadedImages("OutOfBoundsTesting", "XDTLevelAndEntity.Core.Craft");
                }
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry later
                }

                IntPtr testPtr = this.ResolveBuildingMonoNative(cls, "Test", 1);
                if (testPtr == IntPtr.Zero)
                {
                    this.buildingRangePatchTried = true;
                    this.BuildingLog("range-patch: OutOfBoundsTesting.Test(1) not resolved");
                    return;
                }

                buildingRangeHook = BuildingOutOfBoundsTestNative;
                buildingRangeDetour = new MonoMod.RuntimeDetour.NativeDetour(testPtr, buildingRangeHook);
                this.buildingRangePatchTried = true;
                this.BuildingLog("range-patch: detour installed on OutOfBoundsTesting.Test (applied)");
            }
            catch (Exception ex)
            {
                this.buildingRangePatchTried = true;
                this.BuildingLog("range-patch failed: " + ex.Message);
            }
        }

        private void RemoveBuildingRangePatch()
        {
            try
            {
                if (buildingRangeDetour != null && buildingRangeDetour.IsApplied)
                {
                    buildingRangeDetour.Undo();
                    this.BuildingLog("range-patch: detour undone (range/height limit re-enforced)");
                }
            }
            catch (Exception ex)
            {
                this.BuildingLog("range-patch undo failed: " + ex.Message);
            }
        }

        // ErrorCode.Success (0) — placement is always within homeland bounds / height while applied.
        private static int BuildingOutOfBoundsTestNative(IntPtr ctx)
        {
            return 0;
        }

        // Overlap bypass: install BOTH detours (each guarded independently) and ensure APPLIED. Same
        // lazy-create-once + Apply pattern as the surface/range detours (see the field-block comment).
        private void EnsureBuildingOverlapPatch()
        {
            // Primary: IntersectionTesting.Test -> Success (the single preview+confirm collision gate).
            try
            {
                if (buildingOverlapDetour != null)
                {
                    if (!buildingOverlapDetour.IsApplied)
                    {
                        buildingOverlapDetour.Apply();
                    }
                }
                else if (!this.buildingOverlapPatchTried && this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
                {
                    IntPtr cls = this.FindAuraMonoClassInImages(
                        "XDTLevelAndEntity.Core.Craft", "IntersectionTesting",
                        new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                    if (cls == IntPtr.Zero)
                    {
                        cls = this.FindAuraMonoClassInAllLoadedImages("IntersectionTesting", "XDTLevelAndEntity.Core.Craft");
                    }
                    if (cls != IntPtr.Zero)
                    {
                        IntPtr testPtr = this.ResolveBuildingMonoNative(cls, "Test", 3);
                        if (testPtr != IntPtr.Zero)
                        {
                            buildingOverlapHook = BuildingIntersectionTestNative;
                            buildingOverlapDetour = new MonoMod.RuntimeDetour.NativeDetour(testPtr, buildingOverlapHook);
                            this.buildingOverlapPatchTried = true;
                            this.BuildingLog("overlap-patch: detour installed on IntersectionTesting.Test (applied)");
                        }
                        else
                        {
                            this.buildingOverlapPatchTried = true;
                            this.BuildingLog("overlap-patch: IntersectionTesting.Test(3) not resolved");
                        }
                    }
                    // cls == 0 ⇒ image not loaded yet: leave tried=false and retry on a later frame.
                }
            }
            catch (Exception ex)
            {
                this.buildingOverlapPatchTried = true;
                this.BuildingLog("overlap-patch failed: " + ex.Message);
            }

            // Secondary: BuildSingle.OverlapCompleteWithSlab -> out null, true (slab-on-slab confirm deny).
            try
            {
                if (buildingSlabOverlapDetour != null)
                {
                    if (!buildingSlabOverlapDetour.IsApplied)
                    {
                        buildingSlabOverlapDetour.Apply();
                    }
                }
                else if (!this.buildingSlabOverlapPatchTried && this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
                {
                    IntPtr cls = this.FindAuraMonoClassInImages(
                        "XDTLevelAndEntity.Core.Craft", "BuildSingle",
                        new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                    if (cls == IntPtr.Zero)
                    {
                        cls = this.FindAuraMonoClassInAllLoadedImages("BuildSingle", "XDTLevelAndEntity.Core.Craft");
                    }
                    if (cls != IntPtr.Zero)
                    {
                        IntPtr slabPtr = this.ResolveBuildingMonoNative(cls, "OverlapCompleteWithSlab", 1);
                        if (slabPtr != IntPtr.Zero)
                        {
                            buildingSlabOverlapHook = BuildingSlabOverlapNative;
                            buildingSlabOverlapDetour = new MonoMod.RuntimeDetour.NativeDetour(slabPtr, buildingSlabOverlapHook);
                            this.buildingSlabOverlapPatchTried = true;
                            this.BuildingLog("overlap-patch: detour installed on BuildSingle.OverlapCompleteWithSlab (applied)");
                        }
                        else
                        {
                            this.buildingSlabOverlapPatchTried = true;
                            this.BuildingLog("overlap-patch: BuildSingle.OverlapCompleteWithSlab(1) not resolved");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.buildingSlabOverlapPatchTried = true;
                this.BuildingLog("overlap-patch (slab) failed: " + ex.Message);
            }
        }

        // Undo both detours (toggle off) — leaves the NativeDetour objects reusable for a later Apply.
        private void RemoveBuildingOverlapPatch()
        {
            try
            {
                if (buildingOverlapDetour != null && buildingOverlapDetour.IsApplied)
                {
                    buildingOverlapDetour.Undo();
                    this.BuildingLog("overlap-patch: detour undone (overlap re-enforced)");
                }
            }
            catch (Exception ex)
            {
                this.BuildingLog("overlap-patch undo failed: " + ex.Message);
            }
            try
            {
                if (buildingSlabOverlapDetour != null && buildingSlabOverlapDetour.IsApplied)
                {
                    buildingSlabOverlapDetour.Undo();
                    this.BuildingLog("overlap-patch: slab detour undone");
                }
            }
            catch (Exception ex)
            {
                this.BuildingLog("overlap-patch (slab) undo failed: " + ex.Message);
            }
        }

        // Detour bodies — callback-free constants (only applied while the toggle is on).
        // IntersectionTesting.Test -> ErrorCode.Success(0): the collision test always passes.
        private static int BuildingIntersectionTestNative(IntPtr ctx, IntPtr collectElements, IntPtr collectColliders)
        {
            return 0;
        }

        // BuildSingle.OverlapCompleteWithSlab(out IBuildBoxElement) -> true + out null: mirrors the old
        // num==0 outcome so slab-on-slab confirm no longer denies. Writing null into a reference-type out
        // slot (a caller stack local) is barrier-free and safe.
        private static unsafe byte BuildingSlabOverlapNative(IntPtr self, IntPtr outOverlapElement)
        {
            if (outOverlapElement != IntPtr.Zero)
            {
                *(IntPtr*)outOverlapElement = IntPtr.Zero;
            }
            return 1;
        }
    }
}
