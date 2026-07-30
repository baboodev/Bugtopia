using System;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // --- Camera rig: two small, unrelated jobs ---
        //
        // 1. A ONE-SHOT YAW NUDGE for auto-farm (`RotateCameraAroundPlayer`, three call sites in
        //    HeartopiaComplete.Farm.cs): add N degrees to the game camera controller's yaw axis and
        //    let XDTCameraManager's own LateUpdate swing Camera.main around the player. It replaced
        //    manual orbit math plus Transform-setter patches plus a 60-frame pin, so it costs zero
        //    `.text` patches. No-op when the live controller is not axis-capable (fixed/cutscene/
        //    transition cameras) — the flip is a convenience, not a requirement.
        //
        // 2. THE CAMERA TOGGLE, which flips the GAME'S own free-look setting
        //    (`GameSettingSystem.MouseControlMode`). See the block on TrySetGameMouseControlMode for
        //    why the mod no longer implements mouse-look itself, and for the nine attempts that
        //    proved it should not.
        //
        // ⚠️ This file used to drive the camera axis EVERY FRAME as the mouse-look implementation.
        // All of that is gone: nothing here steers the camera continuously any more, and nothing
        // here should. If a future feature wants the camera, drive the game's own input or setting,
        // not the axis — the axis only survives here because a single instantaneous nudge is
        // immune to the blend/transition problem that killed the continuous version.
        //
        // XDTCameraManager lives in image XDTLevelAndEntity and is Mono-only, so everything below is
        // AuraMono reflection and invisible to the IL2CPP `.text` surface. Controller pointers are
        // NEVER cached across frames — the manager creates and drops these instances constantly.

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

        // ⭐⭐⭐⭐ WHAT CAMERA TOGGLE ACTUALLY DOES NOW: it flips the GAME'S OWN free-look setting.
        //
        // The game ships a PC free-look mode — `GameSettingSystem.MouseControlMode = MoveRotate`,
        // driven by `XDTGUI.Core.ResourceLife.MouseControl`. Verified by the user: in that mode the
        // camera is NEVER lost, including above the jump height where every version of the mod's own
        // mouse-look died. Nine attempts to reproduce it from outside all failed, for reasons that
        // are now understood and recorded below — so stop reproducing it and just turn it on.
        //
        // What the game's mode does that no external driver could: `MouseControl` owns the cursor,
        // the UI gating (`hasViewOpen`), the allowed player states, the Alt-to-release bracket AND
        // the `SendValueToControl(ScreenTouch, Perform/Cancel)` pair that suspends the camera
        // target's auto-yaw remediation. Every one of those is a separate thing the mod would have
        // had to re-derive correctly, and each was a way to be subtly wrong.
        //
        // ⛔ THE NINE DEAD ENDS, so nobody walks them again. The symptom: jumping above a "Player 3C"
        // trigger volume's `heightAbove` pops that area's camera shot, landing re-pushes it, and the
        // second `XDTCameraManager.Switch` interrupts the first — nested `BlendCameraController`s,
        // up to a second of overlapping blends, during which the mod's steering did nothing.
        //   1-4, 6, 8: wrote the axis (or `mouseMove`) on `CurrentController` — during a storm a
        //     `BlendCameraController`, which has neither an axis nor a `mouseMove` field — and on
        //     `TargetController`, whose blend weight is ~0 early. The "from" side, which dominates
        //     the picture, was never reached. #8 additionally re-seeded from the wrapper's LERPED
        //     axis and zeroed the integrator speeds, feeding lerp(leaves) back onto the leaves:
        //     nine nested blends, 10-21° lurches on a 0.05 mouse delta, axis decoupled from camera.
        //   5: cancelling `isOpenInterpolationValue` — irrelevant, its only caller is
        //     `ApplyInteractConfig`.
        //   7, 9: the right pipe (`SendTouchLookValueToControl`) in the wrong dialect, then the
        //     right dialect (`delta * 20`, every frame, zeros included, as `MouseControl` does).
        //     Even correct, it did not fix it — the multicast reaches every live follow camera, but
        //     evidently that alone is not the whole of what the native mode gets right.
        // The lesson worth keeping: when the game already implements the feature, drive ITS switch.
        private const int GameMouseControlModeMoveRotate = 0;
        private const int GameMouseControlModeDragRotate = 1;

        private bool gameMouseControlModeLogged;

        // Returns true when the mode was applied (or already correct). Fail-closed: on any
        // resolution failure it returns false and nothing is touched.
        private bool TrySetGameMouseControlMode(int mode)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                // `GameSettingSystem.Instance` is `M<IGameSetting>.Inst as GameSettingSystem` — a
                // static property routed through a generic manager lookup. The project's proven way
                // to reach a manager is the service dictionary (reach-manager-via-servicedic); the
                // static getter is only the fallback.
                IntPtr settings = IntPtr.Zero;
                if (!this.TryGetAuraMonoManagerFromServiceDic("GameSettingSystem", out settings) || settings == IntPtr.Zero)
                {
                    IntPtr klass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.GameSetting", "GameSettingSystem");
                    IntPtr getInstance = klass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(klass, "get_Instance", 0)
                        : IntPtr.Zero;
                    if (getInstance != IntPtr.Zero)
                    {
                        IntPtr instExc = IntPtr.Zero;
                        settings = auraMonoRuntimeInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, ref instExc);
                        if (instExc != IntPtr.Zero)
                        {
                            settings = IntPtr.Zero;
                        }
                    }
                }

                if (settings == IntPtr.Zero)
                {
                    this.LogGameMouseControlModeFailureOnce("GameSettingSystem instance not resolvable "
                                                            + "(service dictionary and static Instance both failed)");
                    return false;
                }

                IntPtr settingsClass = auraMonoObjectGetClass(settings);
                if (settingsClass == IntPtr.Zero)
                {
                    this.LogGameMouseControlModeFailureOnce("GameSettingSystem class unreadable");
                    return false;
                }

                IntPtr getMode = this.FindAuraMonoMethodOnHierarchy(settingsClass, "get_MouseControlMode", 0);
                IntPtr setMode = this.FindAuraMonoMethodOnHierarchy(settingsClass, "set_MouseControlMode", 1);
                if (setMode == IntPtr.Zero)
                {
                    this.LogGameMouseControlModeFailureOnce("set_MouseControlMode not found on "
                                                            + this.GetAuraMonoClassDisplayName(settingsClass));
                    return false;
                }

                IntPtr exc;

                if (!this.TryInvokeGameMouseControlModeSetter(setMode, settings, mode))
                {
                    this.LogGameMouseControlModeFailureOnce("set_MouseControlMode threw");
                    return false;
                }

                // The setter persists to PlayerPrefs and fires PCShortCutShowEvent, but MouseControl
                // only re-evaluates in Refresh() (or on a player-state change), so poke it directly
                // or the switch does not take effect until the player happens to change state.
                this.TryRefreshGameMouseControl();

                if (!this.gameMouseControlModeLogged)
                {
                    this.gameMouseControlModeLogged = true;
                    ModLogger.Msg("[CameraRig] Camera Toggle drives the game's own free-look "
                                  + "(GameSettingSystem.MouseControlMode: 0 = MoveRotate, 1 = DragRotate).");
                }

                // Read it straight back: this separates "we never set it" from "we set it and
                // something put it back", which look identical from the player's side.
                if (getMode != IntPtr.Zero && auraMonoObjectUnbox != null)
                {
                    exc = IntPtr.Zero;
                    IntPtr check = auraMonoRuntimeInvoke(getMode, settings, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && check != IntPtr.Zero)
                    {
                        IntPtr raw = auraMonoObjectUnbox(check);
                        if (raw != IntPtr.Zero)
                        {
                            int now = System.Runtime.InteropServices.Marshal.ReadInt32(raw);
                            ModLogger.Msg("[CameraRig] MouseControlMode -> requested " + mode
                                          + ", reads back " + now
                                          + (now == mode ? " (applied)" : " (NOT applied)"));
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void LogGameMouseControlModeFailureOnce(string reason)
        {
            if (this.gameMouseControlModeLogged)
            {
                return;
            }

            this.gameMouseControlModeLogged = true;
            ModLogger.Msg("[CameraRig] Camera Toggle could not switch the game's free-look: " + reason
                          + ". The toggle will do nothing until this resolves.");
        }

        private unsafe bool TryInvokeGameMouseControlModeSetter(IntPtr setter, IntPtr settings, int mode)
        {
            IntPtr exc = IntPtr.Zero;
            int value = mode;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            auraMonoRuntimeInvoke(setter, settings, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // UIManager.mouseControl.Refresh() — the field is public, Refresh() is public.
        private void TryRefreshGameMouseControl()
        {
            try
            {
                if (!this.ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out _)
                    || uiManagerObj == IntPtr.Zero)
                {
                    return;
                }

                if (!this.TryGetMonoObjectMember(uiManagerObj, "mouseControl", out IntPtr mouseControl)
                    || mouseControl == IntPtr.Zero)
                {
                    return;
                }

                IntPtr mcClass = auraMonoObjectGetClass(mouseControl);
                IntPtr refresh = mcClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(mcClass, "Refresh", 0)
                    : IntPtr.Zero;
                if (refresh == IntPtr.Zero)
                {
                    return;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(refresh, mouseControl, IntPtr.Zero, ref exc);
            }
            catch
            {
            }
        }

        // Off means off: put the game back on DragRotate.
        //
        // ⚠️ This deliberately does NOT restore "whatever the player had before". That was the first
        // design and it behaved wrongly for the obvious case: if the player already had MoveRotate
        // selected, the mod saved 0 as the value to restore and switching the toggle off put it back
        // to 0 — the log read `requested 0` on both edges and the feature looked stuck on. A toggle
        // that cannot turn its own thing off is worse than one that overrides a preference, and the
        // preference is one click away in the game's own settings.
        internal void RestoreGameMouseControlMode()
        {
            this.TrySetGameMouseControlMode(GameMouseControlModeDragRotate);
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
