using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Analog movement-input bridge.
    //
    // Goal: feed the local player an analog move axis "as if from the joystick", without
    // teleport/noclip. The game drives movement through the Unity Input System "Move"
    // action (InputEvent.Move == 0), NOT legacy Input.GetAxis:
    //
    //   SendMoveValueToControl(raw axis)  → InputSystem virtual control
    //     → MonoInputManager move listener (gated by IsInputDisabled(Move))
    //     → LocalPlayerComponent.OnLeftJoystickPerformed(axis) → _joystickQueue
    //     → _TickProcessJoystick: moveDir = cameraComponent.ToCameraSpaceJoystick(axis)  ← camera yaw
    //     → PlayerMoveComponent.SetMoveJoystick(joystick, moveDir)
    //         → SendSignal(StandMoveInput)  ← real velocity + server sync
    //
    // The axis we inject is RAW joystick space (x = right, y = forward, |axis| 0..1). The game
    // applies the camera-relative transform downstream, so we must NOT pre-rotate. Movement runs
    // through the genuine move component at legitimate speed, so it passes the server-side
    // MovementAntiCheating checks and leaves no InputCheatManager touch trace.
    //
    // Primary path: XDTGameSystem.MonoInputManager.SendMoveValueToControl(Vector2) — honors the
    //   game's own IsInputDisabled(Move) refcount, which the mod already toggles via
    //   UpdateMenuMovementInputBlock, so menu blocking is respected for free.
    // Fallback path: LocalPlayerComponent.OnLeftJoystickPerformed(Vector2) /
    //   OnLeftJoystickCanceled() — used only if SendMoveValueToControl can't be resolved. This
    //   path bypasses IsInputDisabled(Move), so we replicate the ShouldBlockGameplayInput gate.
    public partial class HeartopiaComplete
    {
        // Axes below this magnitude are treated as released (game itself zeroes <0.1).
        private const float MovementBridgeDeadzone = 0.12f;

        // User toggle for the self-driven WASD→joystick demo bridge (config-persisted).
        private bool analogMoveBridgeEnabled;

        private bool movementInputReady;
        private bool movementInputPendingLogged;
        private bool movementInputReadyLogged;
        private float movementInputRetryAt;
        private AuraMonoObjectCache cachedMovementInputManagerObj;
        private IntPtr movementSendMoveValueMethod;
        private IntPtr movementOnLeftJoyMethod;
        private IntPtr movementOnLeftJoyCancelMethod;
        private IntPtr movementCameraToJoyMethod;
        private IntPtr movementSetMoveJoystickMethod;
        private IntPtr movementJoystickQueueClearMethod;
        private IntPtr movementVehicleControllerClass;
        private IntPtr movementVehicleOnLeftJoyMethod;
        private IntPtr movementVehicleOnLeftJoyCancelMethod;
        private bool movementWasInjecting;
        private bool movementBridgeInjectLogged;
        private bool movementBridgeHasHeldAxis;
        private Vector2 movementBridgeHeldAxis;
        private FeatureBreakerState movementBridgeBreaker;

        // Resolve and cache the MonoInputManager (primary) + LocalPlayerComponent joystick methods
        // (fallback). Retry-throttled (3s) like the other AuraMono resolvers. Returns readiness.
        private bool TryEnsureMovementInputRuntime(bool logIfPending = false)
        {
            if (this.movementInputReady
                && (this.movementSendMoveValueMethod == IntPtr.Zero || this.cachedMovementInputManagerObj.TryGet(out _)))
            {
                return true;
            }

            if (Time.unscaledTime < this.movementInputRetryAt)
            {
                return this.movementInputReady;
            }

            this.movementInputRetryAt = Time.unscaledTime + 3f;
            this.movementInputReady = false;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                if (logIfPending)
                {
                    this.LogMovementInputStatusOnce();
                }

                return false;
            }

            // Primary: MonoInputManager.SendMoveValueToControl(Vector2)
            if (this.movementSendMoveValueMethod == IntPtr.Zero
                && this.TryGetAuraMonoManagerFromServiceDic("MonoInputManager", out IntPtr managerObj)
                && managerObj != IntPtr.Zero)
            {
                IntPtr managerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(managerObj) : IntPtr.Zero;
                if (managerClass != IntPtr.Zero)
                {
                    IntPtr method = this.FindAuraMonoMethodOnHierarchy(managerClass, "SendMoveValueToControl", 1);
                    if (method != IntPtr.Zero)
                    {
                        this.cachedMovementInputManagerObj.Set(managerObj);
                        this.movementSendMoveValueMethod = method;
                    }
                }
            }

            // Fallback: LocalPlayerComponent.OnLeftJoystickPerformed(Vector2) / OnLeftJoystickCanceled()
            if (this.movementOnLeftJoyMethod == IntPtr.Zero
                && this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj)
                && playerObj != IntPtr.Zero)
            {
                IntPtr playerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerObj) : IntPtr.Zero;
                if (playerClass != IntPtr.Zero)
                {
                    this.movementOnLeftJoyMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "OnLeftJoystickPerformed", 1);
                    this.movementOnLeftJoyCancelMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "OnLeftJoystickCanceled", 0);
                }
            }

            this.movementInputReady = this.movementSendMoveValueMethod != IntPtr.Zero || this.movementOnLeftJoyMethod != IntPtr.Zero;
            if (logIfPending || this.movementInputReady)
            {
                this.LogMovementInputStatusOnce();
            }

            return this.movementInputReady;
        }

        private void LogMovementInputStatusOnce()
        {
            if (this.movementInputReady)
            {
                if (this.movementInputReadyLogged)
                {
                    return;
                }

                this.movementInputReadyLogged = true;
                ModLogger.Msg(
                    "[MovementBridge] ready (SendMoveValueToControl="
                    + (this.movementSendMoveValueMethod != IntPtr.Zero ? "ok" : "missing")
                    + ", OnLeftJoystickPerformed="
                    + (this.movementOnLeftJoyMethod != IntPtr.Zero ? "ok" : "missing")
                    + ", XInput="
                    + (xinputAvailable == -1 ? "none" : "ok")
                    + ").");
                return;
            }

            if (this.movementInputPendingLogged)
            {
                return;
            }

            this.movementInputPendingLogged = true;
            ModLogger.Msg("[MovementBridge] input types are Mono-only; Aura Mono resolver pending (enter a town and retry).");
        }

        // Debug read: PlayerMoveComponent._joystick (the live analog axis the game is acting on).
        private bool TryReadGameMoveAxis(out Vector2 axis)
        {
            axis = Vector2.zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if ((!this.TryGetMonoObjectMember(playerObj, "moveComponent", out IntPtr moveObj) || moveObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(playerObj, "_moveComponent", out moveObj) || moveObj == IntPtr.Zero))
            {
                return false;
            }

            return this.TryGetMonoVector2Member(moveObj, "_joystick", out axis);
        }

        // Inject an analog move axis. axis is RAW joystick space, magnitude clamped to 0..1; the
        // game applies the camera-relative transform. Prefers a direct SetMoveJoystick path that
        // bypasses LocalPlayerComponent._joystickQueue (one dequeue per player tick → input lag).
        private bool TrySetGameMoveAxis(Vector2 axis)
        {
            if (!this.TryEnsureMovementInputRuntime())
            {
                return false;
            }

            float magnitude = axis.magnitude;
            if (magnitude > 1f)
            {
                axis /= magnitude;
            }

            // While driving, movement goes through the vehicle controller, not the player move
            // component — route the axis to SelfVehicleController.OnLeftJoystickPerformed instead.
            if (this.TryInjectVehicleMoveAxis(axis, release: false))
            {
                return true;
            }

            if (this.TryInjectDirectPlayerMoveAxis(axis, release: false))
            {
                return true;
            }

            // Legacy fallbacks (enqueue into _joystickQueue — can lag if used every Update frame).
            if (this.movementOnLeftJoyMethod != IntPtr.Zero
                && this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj)
                && playerObj != IntPtr.Zero
                && this.TryInvokeMovementVector2(this.movementOnLeftJoyMethod, playerObj, axis))
            {
                return true;
            }

            if (this.movementSendMoveValueMethod != IntPtr.Zero
                && this.cachedMovementInputManagerObj.TryGet(out IntPtr managerObj)
                && managerObj != IntPtr.Zero)
            {
                return this.TryInvokeMovementVector2(this.movementSendMoveValueMethod, managerObj, axis);
            }

            return false;
        }

        // Release the injected axis (zero / cancel) so physical input regains control.
        private bool TryClearGameMoveAxis()
        {
            if (!this.TryEnsureMovementInputRuntime())
            {
                return false;
            }

            bool any = false;

            any |= this.TryInjectVehicleMoveAxis(Vector2.zero, release: true);
            any |= this.TryInjectDirectPlayerMoveAxis(Vector2.zero, release: true);

            if (this.movementSendMoveValueMethod != IntPtr.Zero
                && this.cachedMovementInputManagerObj.TryGet(out IntPtr managerObj)
                && managerObj != IntPtr.Zero)
            {
                any |= this.TryInvokeMovementVector2(this.movementSendMoveValueMethod, managerObj, Vector2.zero);
            }

            if (this.movementOnLeftJoyCancelMethod != IntPtr.Zero
                && this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj)
                && playerObj != IntPtr.Zero)
            {
                any |= this.TryInvokeMovementZeroArg(this.movementOnLeftJoyCancelMethod, playerObj);
            }

            return any;
        }

        // Route the axis to the driven vehicle's controller (SelfVehicleController implements
        // ILeftJoystick; OnLeftJoystickPerformed sets _inputData.moveAxis, which its update consumes).
        // Returns false when the player is not the driver, so callers fall back to the player path.
        private bool TryInjectVehicleMoveAxis(Vector2 axis, bool release)
        {
            if (!this.TryGetDrivingVehicleControllerObject(out IntPtr controllerObj) || controllerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr controllerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(controllerObj) : IntPtr.Zero;
            if (controllerClass == IntPtr.Zero)
            {
                return false;
            }

            // Method IntPtrs are tied to the controller's MonoClass — re-resolve if it ever changes
            // (e.g. a different vehicle type) so we never invoke a method against the wrong class.
            if (controllerClass != this.movementVehicleControllerClass)
            {
                this.movementVehicleControllerClass = controllerClass;
                this.movementVehicleOnLeftJoyMethod = this.FindAuraMonoMethodOnHierarchy(controllerClass, "OnLeftJoystickPerformed", 1);
                this.movementVehicleOnLeftJoyCancelMethod = this.FindAuraMonoMethodOnHierarchy(controllerClass, "OnLeftJoystickCanceled", 0);
            }

            if (release)
            {
                if (this.movementVehicleOnLeftJoyCancelMethod != IntPtr.Zero)
                {
                    return this.TryInvokeMovementZeroArg(this.movementVehicleOnLeftJoyCancelMethod, controllerObj);
                }

                return this.movementVehicleOnLeftJoyMethod != IntPtr.Zero
                    && this.TryInvokeMovementVector2(this.movementVehicleOnLeftJoyMethod, controllerObj, Vector2.zero);
            }

            return this.movementVehicleOnLeftJoyMethod != IntPtr.Zero
                && this.TryInvokeMovementVector2(this.movementVehicleOnLeftJoyMethod, controllerObj, axis);
        }

        // The controller of the vehicle the local player is currently DRIVING (not riding as a
        // passenger). Reuses NoclipFeature's Aura-Mono vehicle resolution.
        private bool TryGetDrivingVehicleControllerObject(out IntPtr controllerObj)
        {
            controllerObj = IntPtr.Zero;

            IntPtr vehicleComponentObj = this.TryGetSelfEntityVehicleComponentMono();
            if (vehicleComponentObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetMonoObjectMember(vehicleComponentObj, "controller", out controllerObj) && controllerObj != IntPtr.Zero;
        }

        // Mod's own input source → raw joystick-space analog axis (NOT camera-transformed; the
        // game does that). WASD/arrows are digital, with a guarded legacy-gamepad-axis fallback.
        private Vector2 ReadUnityMovementAxes()
        {
            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;

            Vector2 axis = new Vector2(x, y);
            if (axis.sqrMagnitude > 1f)
            {
                axis.Normalize();
            }

            // The game reads the gamepad through the new Input System action map, where the stick
            // is not bound (native stick movement does nothing) — and legacy Input.GetAxis returns
            // 0 under "Input System (New)". So read the Xbox stick directly via XInput instead.
            if (axis.sqrMagnitude < MovementBridgeDeadzone * MovementBridgeDeadzone)
            {
                Vector2 pad = ReadXInputLeftStick();
                if (pad.sqrMagnitude >= MovementBridgeDeadzone * MovementBridgeDeadzone)
                {
                    axis = pad;
                }
            }

            return axis;
        }

        // Camera-relative transform mirroring CameraComponent.ToCameraSpaceJoystick (yaw only).
        // Provided for parity/debugging only — do NOT feed the result to TrySetGameMoveAxis, since
        // the game already transforms the raw axis. Useful when writing world-space directly.
        private Vector2 BuildCameraRelativeMove(Vector2 raw)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return raw;
            }

            Vector3 world = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f) * new Vector3(raw.x, 0f, raw.y);
            return new Vector2(world.x, world.z);
        }

        // True when a natively-working input source is already driving Move this frame, so the
        // bridge yields (merge, not overwrite). Only the on-screen touch joystick qualifies — the
        // gamepad stick is NOT native here (that's exactly what the bridge supplies), so it must
        // not be treated as physical or we would refuse to inject it.
        private bool IsPhysicalMoveInputActive()
        {
            return Input.touchCount > 0;
        }

        // Per-frame tick (called from OnUpdate). Drives the self-injecting WASD bridge while
        // honoring the menu movement block and yielding to physical input.
        private void UpdateMovementInputBridge()
        {
            if (!this.analogMoveBridgeEnabled)
            {
                this.ReleaseMovementBridgeIfInjecting();
                return;
            }

            if (!this.movementBridgeBreaker.ShouldRun(Time.unscaledTime))
            {
                return;
            }

            try
            {
                // Surface resolver status in the log while the feature is on (once ready / once pending).
                this.TryEnsureMovementInputRuntime(logIfPending: true);

                // Respect the mod's menu movement block (the game's IsInputDisabled(Move) state).
                // Also gate on window focus (XInput reads the pad globally) and on a focused game
                // text field: the direct SetMoveJoystick path bypasses IsInputDisabled(Move), so
                // without this check the W/A/S/D letters typed into chat would move the player.
                if (ShouldBlockGameplayInput() || this.menuMoveInputDisabled || this.IsPhysicalMoveInputActive()
                    || !IsGameWindowFocused()
                    || this.IsGameTextInputFocused())
                {
                    this.ReleaseMovementBridgeIfInjecting();
                    return;
                }

                Vector2 axis = this.ReadUnityMovementAxes();
                float activeDeadzoneSq = MovementBridgeDeadzone * MovementBridgeDeadzone;
                float releaseDeadzone = MovementBridgeDeadzone * 0.65f;
                float releaseDeadzoneSq = releaseDeadzone * releaseDeadzone;
                if (axis.sqrMagnitude >= activeDeadzoneSq)
                {
                    this.movementBridgeHeldAxis = axis;
                    this.movementBridgeHasHeldAxis = true;
                }
                else if (this.movementBridgeHasHeldAxis && axis.sqrMagnitude >= releaseDeadzoneSq)
                {
                    this.movementBridgeHeldAxis = axis;
                }
                else
                {
                    this.movementBridgeHasHeldAxis = false;
                    this.ReleaseMovementBridgeIfInjecting();
                    return;
                }

                if (this.TrySetGameMoveAxis(this.movementBridgeHeldAxis))
                {
                    this.movementWasInjecting = true;
                    if (!this.movementBridgeInjectLogged)
                    {
                        this.movementBridgeInjectLogged = true;
                        ModLogger.Msg("[MovementBridge] injecting analog move axis (first sample " + axis.ToString("F2") + ").");
                    }
                }

                this.movementBridgeBreaker.Success();
            }
            catch (Exception ex)
            {
                this.movementBridgeBreaker.Failure("MovementBridge", ex, Time.unscaledTime);
            }
        }

        private void ReleaseMovementBridgeIfInjecting()
        {
            if (!this.movementWasInjecting)
            {
                return;
            }

            this.movementBridgeHasHeldAxis = false;
            this.TryClearGameMoveAxis();
            this.movementWasInjecting = false;
        }

        private bool TryEnsureDirectPlayerMoveMethods(IntPtr playerObj, out IntPtr cameraObj, out IntPtr moveObj)
        {
            cameraObj = IntPtr.Zero;
            moveObj = IntPtr.Zero;
            if (playerObj == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if ((!this.TryGetMonoObjectMember(playerObj, "cameraComponent", out cameraObj) || cameraObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(playerObj, "_cameraComponent", out cameraObj) || cameraObj == IntPtr.Zero))
            {
                return false;
            }

            if (!this.TryGetBunnyHopMonoMoveComponent(playerObj, out moveObj) || moveObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr cameraClass = auraMonoObjectGetClass(cameraObj);
            IntPtr moveClass = auraMonoObjectGetClass(moveObj);
            if (cameraClass == IntPtr.Zero || moveClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.movementCameraToJoyMethod == IntPtr.Zero)
            {
                this.movementCameraToJoyMethod = this.FindAuraMonoMethodOnHierarchy(cameraClass, "ToCameraSpaceJoystick", 1);
            }

            if (this.movementSetMoveJoystickMethod == IntPtr.Zero)
            {
                this.movementSetMoveJoystickMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "SetMoveJoystick", 2);
                if (this.movementSetMoveJoystickMethod == IntPtr.Zero)
                {
                    this.movementSetMoveJoystickMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "SetMoveJoystick", 3);
                }
            }

            return this.movementCameraToJoyMethod != IntPtr.Zero && this.movementSetMoveJoystickMethod != IntPtr.Zero;
        }

        private unsafe bool TryClearMovementJoystickQueue(IntPtr playerObj)
        {
            if (playerObj == IntPtr.Zero
                || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "_joystickQueue", out IntPtr queueObj) || queueObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.movementJoystickQueueClearMethod == IntPtr.Zero)
            {
                IntPtr queueClass = auraMonoObjectGetClass(queueObj);
                if (queueClass == IntPtr.Zero)
                {
                    return false;
                }

                this.movementJoystickQueueClearMethod = this.FindAuraMonoMethodOnHierarchy(queueClass, "Clear", 0);
            }

            if (this.movementJoystickQueueClearMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.movementJoystickQueueClearMethod, queueObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe bool TryInjectDirectPlayerMoveAxis(Vector2 axis, bool release)
        {
            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryEnsureDirectPlayerMoveMethods(playerObj, out IntPtr cameraObj, out IntPtr moveObj))
            {
                return false;
            }

            this.TryClearMovementJoystickQueue(playerObj);

            Vector2 joystick = release ? Vector2.zero : axis;
            Vector2 moveDir = Vector2.zero;
            if (joystick.sqrMagnitude > 0f)
            {
                IntPtr exc = IntPtr.Zero;
                Vector2 joyValue = joystick;
                IntPtr* toCameraArgs = stackalloc IntPtr[1];
                toCameraArgs[0] = (IntPtr)(&joyValue);
                IntPtr moveDirObj = auraMonoRuntimeInvoke(this.movementCameraToJoyMethod, cameraObj, (IntPtr)toCameraArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    return false;
                }

                if (moveDirObj != IntPtr.Zero && auraMonoObjectUnbox != null)
                {
                    IntPtr raw = auraMonoObjectUnbox(moveDirObj);
                    if (raw != IntPtr.Zero)
                    {
                        moveDir = *(Vector2*)raw;
                    }
                }
                else
                {
                    moveDir = this.BuildCameraRelativeMove(joystick);
                }
            }

            int setArgCount = this.TryGetAuraMonoMethodParamCount(this.movementSetMoveJoystickMethod);
            IntPtr exc2 = IntPtr.Zero;
            if (setArgCount >= 3)
            {
                Vector2 joyArg = joystick;
                Vector2 moveArg = moveDir;
                Vector3 move3DArg = Vector3.zero;
                IntPtr* setArgs = stackalloc IntPtr[3];
                setArgs[0] = (IntPtr)(&joyArg);
                setArgs[1] = (IntPtr)(&moveArg);
                setArgs[2] = (IntPtr)(&move3DArg);
                auraMonoRuntimeInvoke(this.movementSetMoveJoystickMethod, moveObj, (IntPtr)setArgs, ref exc2);
            }
            else
            {
                Vector2 joyArg = joystick;
                Vector2 moveArg = moveDir;
                IntPtr* setArgs = stackalloc IntPtr[2];
                setArgs[0] = (IntPtr)(&joyArg);
                setArgs[1] = (IntPtr)(&moveArg);
                auraMonoRuntimeInvoke(this.movementSetMoveJoystickMethod, moveObj, (IntPtr)setArgs, ref exc2);
            }

            return exc2 == IntPtr.Zero;
        }

        // ── XInput (Xbox controller) direct read ───────────────────────────
        // Bypasses Unity input entirely so it works even though the game's Input-System action map
        // has no gamepad-stick binding. Returns the left stick as a normalized, deadzoned Vector2
        // in joystick space (x = right, y = forward).

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_14(int dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_910(int dwUserIndex, out XINPUT_STATE pState);

        // 0 = untried, 1 = xinput1_4, 2 = xinput9_1_0, -1 = no XInput dll available.
        private static int xinputAvailable;

        private const float XInputLeftThumbDeadzone = 7849f; // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
        private const float XInputThumbMax = 32767f;

        private static bool TryXInputGetState(int index, out XINPUT_STATE state)
        {
            state = default;
            if (xinputAvailable == -1)
            {
                return false;
            }

            if (xinputAvailable == 0 || xinputAvailable == 1)
            {
                try
                {
                    int result = XInputGetState_14(index, out state);
                    xinputAvailable = 1;
                    return result == 0;
                }
                catch (DllNotFoundException) { xinputAvailable = 2; }
                catch (EntryPointNotFoundException) { xinputAvailable = 2; }
                catch { return false; }
            }

            if (xinputAvailable == 2)
            {
                try
                {
                    int result = XInputGetState_910(index, out state);
                    return result == 0;
                }
                catch (DllNotFoundException) { xinputAvailable = -1; }
                catch (EntryPointNotFoundException) { xinputAvailable = -1; }
                catch { return false; }
            }

            return false;
        }

        private static Vector2 ReadXInputLeftStick()
        {
            if (!TryGetFirstConnectedPad(out XINPUT_STATE state, out _))
            {
                return Vector2.zero;
            }

            return ApplyRadialDeadzone(new Vector2(state.Gamepad.sThumbLX, state.Gamepad.sThumbLY));
        }

        private static bool TryReadXInputGamepad(out XINPUT_GAMEPAD gamepad)
        {
            gamepad = default;
            if (!TryGetFirstConnectedPad(out XINPUT_STATE state, out _))
            {
                return false;
            }

            gamepad = state.Gamepad;
            return true;
        }

        private static Vector2 ApplyRadialDeadzone(Vector2 raw)
        {
            float magnitude = raw.magnitude;
            if (magnitude < XInputLeftThumbDeadzone)
            {
                return Vector2.zero;
            }

            // Rescale [deadzone..max] → [0..1] along the stick direction (radial deadzone).
            float normalized = Mathf.Min((magnitude - XInputLeftThumbDeadzone) / (XInputThumbMax - XInputLeftThumbDeadzone), 1f);
            return raw / magnitude * normalized;
        }

        private static bool TryGetFirstConnectedPad(out XINPUT_STATE state, out int index)
        {
            for (int i = 0; i < 4; i++)
            {
                if (TryXInputGetState(i, out state))
                {
                    index = i;
                    return true;
                }
            }

            state = default;
            index = -1;
            return false;
        }

        private unsafe bool TryInvokeMovementVector2(IntPtr method, IntPtr instance, Vector2 axis)
        {
            if (method == IntPtr.Zero || instance == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            Vector2 value = axis;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe bool TryInvokeMovementZeroArg(IntPtr method, IntPtr instance)
        {
            if (method == IntPtr.Zero || instance == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, instance, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe bool TryGetMonoVector2Member(IntPtr obj, string memberName, out Vector2 value)
        {
            value = Vector2.zero;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(Vector2*)raw;
            return true;
        }
    }
}
