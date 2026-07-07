using System;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // --- Camera rig: drive the game's OWN camera controller axis (embedded Mono) ---
        //
        // XDTCameraManager (image XDTLevelAndEntity, Mono-only — invisible to the IL2CPP .text
        // surface) implements ILateUpdate: every frame it reads
        // CurrentController.GetAxisXvalue()/GetAxisYvalue() (axisX = camera yaw, axisY = camera
        // pitch), recomputes the camera pose from them and writes Camera.main via
        // Transform.SetPositionAndRotation. Any direct write to Camera.main.transform is
        // therefore overwritten in the same frame's LateUpdate — the old mouse-look kept
        // Transform.position/rotation setter PREFIX patches (module .text, Themis-hashable,
        // anti-cheat surface #4) just to win that fight. Driving the controller axis instead
        // lets the game position its own camera: zero .text patches. Axis writes happen in
        // OnUpdate, which runs before the game's LateUpdate in the same frame.
        //
        // The current controller is state-dependent: the FollowCameraController hierarchy
        // (on-foot / swim / vehicle) exposes the axis methods, fixed/cutscene controllers do
        // not. Resolve the four methods on the LIVE controller class on every call
        // (FindAuraMonoMethodOnHierarchy is dictionary-cached, so this is cheap) and no-op when
        // any is absent — mouse-look then correctly does not apply during those camera states.
        //
        // Set*value routes through AxisValue.Warp, which clamps/wraps to the axis min/max — no
        // manual clamping needed. Camera pose is client-local; nothing is sent to the server.

        private AuraMonoObjectCache cameraRigManagerCache;
        private float cameraRigManagerRetryAt;

        private bool TryResolveCameraControllerAxis(
            out IntPtr controllerObj,
            out IntPtr getAxisX,
            out IntPtr getAxisY,
            out IntPtr setAxisX,
            out IntPtr setAxisY)
        {
            controllerObj = IntPtr.Zero;
            getAxisX = IntPtr.Zero;
            getAxisY = IntPtr.Zero;
            setAxisX = IntPtr.Zero;
            setAxisY = IntPtr.Zero;

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            // Manager: scan XDTGame.Framework.Managers._serviceDic for the entry whose manager
            // class name contains "XDTCameraManager" (reach-manager-via-servicedic). The scan is
            // not cheap, so the result is pin-cached (world-epoch invalidated) and lookup
            // failures are throttled.
            if (!this.cameraRigManagerCache.TryGet(out IntPtr managerObj) || managerObj == IntPtr.Zero)
            {
                if (Time.unscaledTime < this.cameraRigManagerRetryAt)
                {
                    return false;
                }

                if (!this.TryGetAuraMonoManagerFromServiceDic("XDTCameraManager", out managerObj) || managerObj == IntPtr.Zero)
                {
                    this.cameraRigManagerRetryAt = Time.unscaledTime + 2f;
                    return false;
                }

                this.cameraRigManagerCache.Set(managerObj);
            }

            IntPtr managerClass = auraMonoObjectGetClass(managerObj);
            if (managerClass == IntPtr.Zero)
            {
                this.cameraRigManagerCache.Clear();
                return false;
            }

            // Controller: property getter first, backing field as fallback. Never cached — the
            // manager swaps controllers on camera-state changes.
            IntPtr ctrl = IntPtr.Zero;
            IntPtr getCurrentController = this.FindAuraMonoMethodOnHierarchy(managerClass, "get_CurrentController", 0);
            if (getCurrentController != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                ctrl = auraMonoRuntimeInvoke(getCurrentController, managerObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    // Stale manager object — drop the cache so the next call rescans _serviceDic.
                    this.cameraRigManagerCache.Clear();
                    return false;
                }
            }

            if (ctrl == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(managerObj, "_currentController", out ctrl);
            }

            if (ctrl == IntPtr.Zero)
            {
                return false;
            }

            IntPtr ctrlClass = auraMonoObjectGetClass(ctrl);
            if (ctrlClass == IntPtr.Zero)
            {
                return false;
            }

            getAxisX = this.FindAuraMonoMethodOnHierarchy(ctrlClass, "GetAxisXvalue", 0);
            getAxisY = this.FindAuraMonoMethodOnHierarchy(ctrlClass, "GetAxisYvalue", 0);
            setAxisX = this.FindAuraMonoMethodOnHierarchy(ctrlClass, "SetAxisXvalue", 1);
            setAxisY = this.FindAuraMonoMethodOnHierarchy(ctrlClass, "SetAxisYvalue", 1);
            if (getAxisX == IntPtr.Zero || getAxisY == IntPtr.Zero || setAxisX == IntPtr.Zero || setAxisY == IntPtr.Zero)
            {
                // Not an axis-capable controller (fixed/cutscene camera) — callers must no-op.
                return false;
            }

            controllerObj = ctrl;
            return true;
        }

        private unsafe bool TryReadCameraAxisValue(IntPtr controllerObj, IntPtr getMethod, out float value)
        {
            value = 0f;
            if (controllerObj == IntPtr.Zero
                || getMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getMethod, controllerObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.cameraRigManagerCache.Clear(); // re-resolve everything on the next call
                return false;
            }

            if (boxed == IntPtr.Zero)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(float*)raw;
            return true;
        }

        private unsafe bool TryWriteCameraAxisValue(IntPtr controllerObj, IntPtr setMethod, float value)
        {
            if (controllerObj == IntPtr.Zero || setMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            float arg = value;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(setMethod, controllerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.cameraRigManagerCache.Clear(); // re-resolve everything on the next call
                return false;
            }

            return true;
        }

        // One-shot yaw nudge (auto-farm "rotate camera around player"): the game's LateUpdate
        // swings Camera.main to the new yaw the same frame. No-op when the current camera
        // controller is not axis-capable — the flip is a convenience, not a requirement.
        private bool TryNudgeCameraAxisYaw(float degrees)
        {
            if (!this.TryResolveCameraControllerAxis(out IntPtr controllerObj, out IntPtr getAxisX, out _, out IntPtr setAxisX, out _))
            {
                return false;
            }

            return this.TryReadCameraAxisValue(controllerObj, getAxisX, out float yaw)
                && this.TryWriteCameraAxisValue(controllerObj, setAxisX, yaw + degrees);
        }
    }
}
