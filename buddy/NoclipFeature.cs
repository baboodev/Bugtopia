using System;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public static class NoclipFeature
    {
        public static bool OverrideVehiclePosition;
        public static Vector3 OverrideVehicleTarget;
        public const float DefaultVehicleSpeedCap = 9f;
        public static float VehicleSpeedCap = DefaultVehicleSpeedCap;
    }

    public partial class HeartopiaComplete
    {
        private static readonly string[] NoclipAuraVehicleManagerFullNames =
        {
            "XDTLevelAndEntity.GameplaySystem.Vehicle.VehicleManager",
            "ScriptsRefactory.LevelAndEntity.GameplaySystem.Vehicle.VehicleManager",
        };

        private static readonly string[] NoclipAuraVehicleComponentFullNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Vehicle.VehicleComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Vehicle.VehicleComponent",
        };

        private static readonly string[] NoclipAuraVehicleMoveComponentFullNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Vehicle.VehicleMoveComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Vehicle.VehicleMoveComponent",
        };

        private float noclipVehicleAuraRetryAt;
        private bool noclipVehicleAuraReady;
        private bool noclipVehicleAuraProbeLogged;
        private AuraMonoObjectCache cachedNoclipVehicleComponentObj;
        private AuraMonoObjectCache cachedNoclipVehicleControllerObj;
        private IntPtr noclipAuraVehicleManagerClass;
        private IntPtr noclipAuraGetSelfEntityVehicleMethod;
        private IntPtr noclipAuraGetPassengerVehicleMethod;
        private IntPtr noclipAuraGetPassengerSeatMethod;
        private IntPtr noclipAuraWorldPlaceToMethod;
        private IntPtr noclipAuraWorldFaceToMethod;
        private IntPtr noclipAuraSetPositionAndRotationMethod;
        private IntPtr noclipAuraForceDisplacementMethod;
        private IntPtr noclipAuraResetVirtualInputMethod;
        private AuraMonoObjectCache noclipAuraMonoInputManagerObj;
        private IntPtr noclipAuraDisableInputMethod;
        private IntPtr noclipAuraEnableInputMethod;
        private bool noclipVehicleJumpInputSuppressed;

        private void EnsureNoclipVehicleAuraMono(bool logIfPending = false)
        {
            if (this.noclipVehicleAuraReady)
            {
                return;
            }

            if (Time.unscaledTime < this.noclipVehicleAuraRetryAt)
            {
                return;
            }

            this.noclipVehicleAuraRetryAt = Time.unscaledTime + 3f;
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.TryResolveNoclipVehicleSpeedCap();

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                if (logIfPending)
                {
                    this.LogNoclipVehicleAuraStatusOnce();
                }

                return;
            }

            if (this.noclipAuraVehicleManagerClass == IntPtr.Zero)
            {
                this.noclipAuraVehicleManagerClass = this.ResolveNoclipAuraClass(NoclipAuraVehicleManagerFullNames, "VehicleManager", "XDTLevelAndEntity.GameplaySystem.Vehicle");
            }

            if (this.noclipAuraVehicleManagerClass != IntPtr.Zero)
            {
                if (this.noclipAuraGetSelfEntityVehicleMethod == IntPtr.Zero)
                {
                    this.noclipAuraGetSelfEntityVehicleMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.noclipAuraVehicleManagerClass,
                        "GetSelfEntityVehicle",
                        0);
                }

                if (this.noclipAuraGetPassengerVehicleMethod == IntPtr.Zero)
                {
                    this.noclipAuraGetPassengerVehicleMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.noclipAuraVehicleManagerClass,
                        "GetPassengerVehicle",
                        1);
                }

                if (this.noclipAuraGetPassengerSeatMethod == IntPtr.Zero)
                {
                    this.noclipAuraGetPassengerSeatMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.noclipAuraVehicleManagerClass,
                        "GetPassengerSeat",
                        1);
                }
            }

            IntPtr vehicleMoveComponentClass = IntPtr.Zero;
            if (this.noclipAuraWorldPlaceToMethod == IntPtr.Zero
                || this.noclipAuraWorldFaceToMethod == IntPtr.Zero
                || this.noclipAuraSetPositionAndRotationMethod == IntPtr.Zero)
            {
                vehicleMoveComponentClass = this.ResolveNoclipAuraClass(
                    NoclipAuraVehicleMoveComponentFullNames,
                    "VehicleMoveComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Vehicle");
                if (vehicleMoveComponentClass != IntPtr.Zero)
                {
                    if (this.noclipAuraWorldPlaceToMethod == IntPtr.Zero)
                    {
                        this.noclipAuraWorldPlaceToMethod = this.FindAuraMonoMethodOnHierarchy(vehicleMoveComponentClass, "WorldPlaceTo", 1);
                    }

                    if (this.noclipAuraWorldFaceToMethod == IntPtr.Zero)
                    {
                        this.noclipAuraWorldFaceToMethod = this.FindAuraMonoMethodOnHierarchy(vehicleMoveComponentClass, "WorldFaceTo", 1);
                    }

                    if (this.noclipAuraSetPositionAndRotationMethod == IntPtr.Zero)
                    {
                        this.noclipAuraSetPositionAndRotationMethod = this.FindAuraMonoMethodOnHierarchy(
                            vehicleMoveComponentClass,
                            "SetPositionAndRotation",
                            3);
                    }
                }
            }

            if (this.noclipAuraForceDisplacementMethod == IntPtr.Zero || this.noclipAuraResetVirtualInputMethod == IntPtr.Zero)
            {
                IntPtr vehicleComponentClass = this.ResolveNoclipAuraClass(
                    NoclipAuraVehicleComponentFullNames,
                    "VehicleComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Vehicle");
                if (vehicleComponentClass != IntPtr.Zero)
                {
                    if (this.noclipAuraForceDisplacementMethod == IntPtr.Zero)
                    {
                        this.noclipAuraForceDisplacementMethod = this.FindAuraMonoMethodOnHierarchy(vehicleComponentClass, "ForceDisplacement", 1);
                    }

                    if (this.noclipAuraResetVirtualInputMethod == IntPtr.Zero)
                    {
                        this.noclipAuraResetVirtualInputMethod = this.FindAuraMonoMethodOnHierarchy(vehicleComponentClass, "ResetVirtualInput", 0);
                    }
                }
            }

            this.noclipVehicleAuraReady = this.noclipAuraVehicleManagerClass != IntPtr.Zero
                && this.noclipAuraGetSelfEntityVehicleMethod != IntPtr.Zero
                && this.noclipAuraWorldPlaceToMethod != IntPtr.Zero
                && (this.noclipAuraForceDisplacementMethod != IntPtr.Zero
                    || this.ResolveNoclipAuraClass(NoclipAuraVehicleComponentFullNames, "VehicleComponent", "XDTLevelAndEntity.Gameplay.Component.Vehicle") != IntPtr.Zero);

            if (logIfPending || this.noclipVehicleAuraReady)
            {
                this.LogNoclipVehicleAuraStatusOnce();
            }
        }

        private IntPtr ResolveNoclipAuraClass(string[] fullNames, string shortName, string namespaceName)
        {
            if (fullNames != null)
            {
                for (int i = 0; i < fullNames.Length; i++)
                {
                    IntPtr candidate = this.FindAuraMonoClassByFullName(fullNames[i]);
                    if (candidate != IntPtr.Zero)
                    {
                        return candidate;
                    }
                }
            }

            if (!string.IsNullOrEmpty(shortName))
            {
                IntPtr candidate = this.FindAuraMonoClassAcrossLoadedAssemblies(namespaceName, shortName);
                if (candidate != IntPtr.Zero)
                {
                    return candidate;
                }
            }

            return IntPtr.Zero;
        }

        private void LogNoclipVehicleAuraStatusOnce()
        {
            if (this.noclipVehicleAuraProbeLogged)
            {
                return;
            }

            this.noclipVehicleAuraProbeLogged = true;
            if (this.noclipVehicleAuraReady)
            {
                ModLogger.Msg("[Noclip] Vehicle Aura Mono ready (VehicleManager, WorldPlaceTo, ForceDisplacement).");
                return;
            }

            ModLogger.Msg(
                "[Noclip] Vehicle types are Mono-only; Aura Mono resolver pending"
                + " (manager="
                + this.DescribeNoclipAuraClass(this.noclipAuraVehicleManagerClass)
                + ", GetSelfEntityVehicle="
                + (this.noclipAuraGetSelfEntityVehicleMethod != IntPtr.Zero ? "ok" : "missing")
                + ", WorldPlaceTo="
                + (this.noclipAuraWorldPlaceToMethod != IntPtr.Zero ? "ok" : "missing")
                + ", ForceDisplacement="
                + (this.noclipAuraForceDisplacementMethod != IntPtr.Zero ? "ok" : "missing")
                + ").");
        }

        private string DescribeNoclipAuraClass(IntPtr classPtr)
        {
            if (classPtr == IntPtr.Zero)
            {
                return "missing";
            }

            string displayName = this.GetAuraMonoClassDisplayName(classPtr);
            return string.IsNullOrEmpty(displayName) ? "resolved" : displayName;
        }

        private void TryResolveNoclipVehicleSpeedCap()
        {
            try
            {
                Type movementAntiCheatType = this.FindLoadedType(
                    "XDT.Scene.Shared.Data.Scriptable.MovementAntiCheating",
                    "MovementAntiCheating",
                    "Il2CppXDT.Scene.Shared.Data.Scriptable.MovementAntiCheating");
                if (movementAntiCheatType == null)
                {
                    return;
                }

                FieldInfo speedField = movementAntiCheatType.GetField(
                    "SpeedThresholdOnVehicle",
                    BindingFlags.Public | BindingFlags.Instance);
                if (speedField == null || speedField.FieldType != typeof(float))
                {
                    return;
                }

                object defaults = Activator.CreateInstance(movementAntiCheatType);
                if (defaults != null)
                {
                    NoclipFeature.VehicleSpeedCap = (float)speedField.GetValue(defaults);
                }
            }
            catch
            {
                NoclipFeature.VehicleSpeedCap = NoclipFeature.DefaultVehicleSpeedCap;
            }
        }

        private void ClearNoclipVehicleOverride()
        {
            NoclipFeature.OverrideVehiclePosition = false;
            HeartopiaComplete.OverridePlayerRotation = false;

            this.cachedNoclipVehicleComponentObj.TryGet(out IntPtr vehicleComponentObj);
            this.cachedNoclipVehicleControllerObj.TryGet(out IntPtr vehicleControllerObj);
            if (vehicleComponentObj == IntPtr.Zero && this.IsPlayerDrivingVehicle())
            {
                this.TryResolveNoclipVehicleContext(out vehicleComponentObj, out vehicleControllerObj, out _);
            }

            this.RestoreNoclipVehicleDrivingState(vehicleComponentObj, vehicleControllerObj);
            this.cachedNoclipVehicleComponentObj.Clear();
            this.cachedNoclipVehicleControllerObj.Clear();
        }

        private void InitializeNoclipOverridePosition()
        {
            this.EnsurePositionOverridePatched();
            if (this.TryResolveNoclipVehicleContext(out IntPtr vehicleComponentObj, out IntPtr vehicleControllerObj, out Vector3 vehiclePosition))
            {
                HeartopiaComplete.OverridePlayerPosition = false;
                NoclipFeature.OverrideVehiclePosition = true;
                NoclipFeature.OverrideVehicleTarget = vehiclePosition;
                this.cachedNoclipVehicleComponentObj.Set(vehicleComponentObj);
                this.cachedNoclipVehicleControllerObj.Set(vehicleControllerObj);
                this.ActivateNoclipVehicleDrivingOverride(vehicleComponentObj, vehicleControllerObj);
                return;
            }

            this.ClearNoclipVehicleOverride();
            GameObject player = GetPlayer();
            if (player != null)
            {
                HeartopiaComplete.OverridePosition = player.transform.position;
            }
            HeartopiaComplete.OverridePlayerPosition = true;
        }

        private void ProcessNoclipMovementOnUpdate()
        {
            if (!this.noclipEnabled)
            {
                this.ClearNoclipVehicleOverride();
                return;
            }

            if (!this.noclipVehicleAuraReady)
            {
                this.EnsureNoclipVehicleAuraMono();
            }

            if (this.TryResolveNoclipVehicleContext(out IntPtr vehicleComponentObj, out IntPtr vehicleControllerObj, out Vector3 vehiclePosition))
            {
                HeartopiaComplete.OverridePlayerPosition = false;
                this.cachedNoclipVehicleComponentObj.Set(vehicleComponentObj);
                this.cachedNoclipVehicleControllerObj.Set(vehicleControllerObj);
                this.ActivateNoclipVehicleDrivingOverride(vehicleComponentObj, vehicleControllerObj);

                Vector3 moveDirection = this.BuildNoclipMoveDirection();
                float currentSpeed = this.GetNoclipSpeed(true);

                Vector3 targetPosition = vehiclePosition;
                if (moveDirection != Vector3.zero)
                {
                    moveDirection.Normalize();
                    targetPosition += moveDirection * currentSpeed * Time.deltaTime;
                }

                NoclipFeature.OverrideVehiclePosition = true;
                NoclipFeature.OverrideVehicleTarget = targetPosition;
                this.ApplyNoclipVehicleWorldPlace(vehicleComponentObj, targetPosition);
                this.ApplyNoclipMovementFacing(moveDirection, onVehicle: true, vehicleComponentObj, targetPosition);
                return;
            }

            this.ClearNoclipVehicleOverride();
            GameObject player = GetPlayer();
            if (player == null)
            {
                return;
            }

            Vector3 playerMoveDirection = this.BuildNoclipMoveDirection();
            float playerSpeed = this.GetNoclipSpeed(false);
            if (playerMoveDirection != Vector3.zero)
            {
                playerMoveDirection.Normalize();
                Vector3 newPosition = player.transform.position + playerMoveDirection * playerSpeed * Time.deltaTime;
                HeartopiaComplete.OverridePlayerPosition = true;
                HeartopiaComplete.OverridePosition = newPosition;
                this.ApplyNoclipMovementFacing(playerMoveDirection, onVehicle: false, IntPtr.Zero, newPosition);
            }
            else
            {
                HeartopiaComplete.OverridePlayerRotation = false;
            }
        }

        private void ProcessNoclipVehicleOnLateUpdate()
        {
            if (!this.noclipEnabled || !NoclipFeature.OverrideVehiclePosition)
            {
                return;
            }

            this.cachedNoclipVehicleComponentObj.TryGet(out IntPtr vehicleComponentObj);
            this.cachedNoclipVehicleControllerObj.TryGet(out IntPtr vehicleControllerObj);
            if (vehicleComponentObj == IntPtr.Zero)
            {
                if (!this.TryResolveNoclipVehicleContext(out vehicleComponentObj, out vehicleControllerObj, out _))
                {
                    return;
                }
                this.cachedNoclipVehicleComponentObj.Set(vehicleComponentObj);
                this.cachedNoclipVehicleControllerObj.Set(vehicleControllerObj);
            }

            this.ActivateNoclipVehicleDrivingOverride(vehicleComponentObj, vehicleControllerObj);
            Vector3 vehicleMoveDirection = this.BuildNoclipMoveDirection();
            this.ApplyNoclipVehicleWorldPlace(vehicleComponentObj, NoclipFeature.OverrideVehicleTarget);
            this.ApplyNoclipMovementFacing(vehicleMoveDirection, onVehicle: true, vehicleComponentObj, NoclipFeature.OverrideVehicleTarget);
        }

        private void ActivateNoclipVehicleDrivingOverride(IntPtr vehicleComponentObj, IntPtr vehicleControllerObj)
        {
            this.SetNoclipVehicleJumpInputSuppressed(true);
            this.SetNoclipVehicleForceDisplacement(vehicleComponentObj, true);
            this.TrySetNoclipVehicleControllerStopMove(vehicleControllerObj, true);
            this.TryZeroNoclipVehicleControllerInput(vehicleControllerObj);
        }

        private void RestoreNoclipVehicleDrivingState(IntPtr vehicleComponentObj, IntPtr vehicleControllerObj)
        {
            this.SetNoclipVehicleJumpInputSuppressed(false);
            this.SetNoclipVehicleForceDisplacement(vehicleComponentObj, false);
            this.TrySetNoclipVehicleControllerStopMove(vehicleControllerObj, false);
            this.TryInvokeNoclipVehicleResetVirtualInput(vehicleComponentObj);
        }

        private bool TryGetNoclipFlatMoveDirection(Vector3 moveDirection, out Vector3 flatDir)
        {
            flatDir = moveDirection;
            flatDir.y = 0f;
            return flatDir.sqrMagnitude >= 0.04f;
        }

        private void ApplyNoclipMovementFacing(Vector3 moveDirection, bool onVehicle, IntPtr vehicleComponentObj, Vector3 anchorPosition)
        {
            if (!this.TryGetNoclipFlatMoveDirection(moveDirection, out Vector3 flatDir))
            {
                if (!onVehicle)
                {
                    HeartopiaComplete.OverridePlayerRotation = false;
                }

                return;
            }

            flatDir.Normalize();
            Quaternion faceRot = Quaternion.LookRotation(flatDir, Vector3.up);
            Vector3 eulerAngles = new Vector3(0f, faceRot.eulerAngles.y, 0f);

            if (onVehicle)
            {
                this.ApplyNoclipVehicleFacing(vehicleComponentObj, anchorPosition, faceRot, flatDir);
                return;
            }

            this.EnsureRotationOverridePatched();
            HeartopiaComplete.PlayerOverrideRot = faceRot;
            HeartopiaComplete.OverridePlayerRotation = true;

            GameObject skeleton = HeartopiaComplete.GetLocalPlayer();
            if (this.TrySyncNoclipPlayerFacingMono(anchorPosition, eulerAngles, faceRot, flatDir))
            {
                return;
            }

            if (skeleton != null)
            {
                skeleton.transform.rotation = faceRot;
            }
        }

        private unsafe bool TrySyncNoclipPlayerFacingMono(Vector3 playerPos, Vector3 eulerAngles, Quaternion faceRot, Vector3 flatDir)
        {
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr playerObj = IntPtr.Zero;
            if (!this.TryGetAuraMonoLocalPlayerObject(out playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr playerClass = auraMonoObjectGetClass(playerObj);
            IntPtr transferMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "Transfer", 4);
            if (transferMethod == IntPtr.Zero)
            {
                transferMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "Transfer", 2);
            }

            if (transferMethod != IntPtr.Zero)
            {
                int transferArgCount = this.TryGetAuraMonoMethodParamCount(transferMethod);
                IntPtr exc = IntPtr.Zero;
                if (transferArgCount >= 4)
                {
                    Vector3 posValue = playerPos;
                    Vector3 eulerValue = eulerAngles;
                    uint parentNetId = 0U;
                    bool checkCollision = false;
                    IntPtr* transferArgs = stackalloc IntPtr[4];
                    transferArgs[0] = (IntPtr)(&posValue);
                    transferArgs[1] = (IntPtr)(&eulerValue);
                    transferArgs[2] = (IntPtr)(&parentNetId);
                    transferArgs[3] = (IntPtr)(&checkCollision);
                    auraMonoRuntimeInvoke(transferMethod, playerObj, (IntPtr)transferArgs, ref exc);
                }
                else
                {
                    Vector3 posValue = playerPos;
                    Vector3 eulerValue = eulerAngles;
                    IntPtr* transferArgs = stackalloc IntPtr[2];
                    transferArgs[0] = (IntPtr)(&posValue);
                    transferArgs[1] = (IntPtr)(&eulerValue);
                    auraMonoRuntimeInvoke(transferMethod, playerObj, (IntPtr)transferArgs, ref exc);
                }

                if (exc == IntPtr.Zero)
                {
                    return true;
                }
            }

            if (!this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) || moveObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr moveClass = auraMonoObjectGetClass(moveObj);
            IntPtr worldFaceMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "WorldFaceTo", 1);
            if (worldFaceMethod == IntPtr.Zero)
            {
                return false;
            }

            Quaternion rotValue = faceRot;
            IntPtr exc2 = IntPtr.Zero;
            IntPtr* faceArgs = stackalloc IntPtr[1];
            faceArgs[0] = (IntPtr)(&rotValue);
            auraMonoRuntimeInvoke(worldFaceMethod, moveObj, (IntPtr)faceArgs, ref exc2);
            if (exc2 != IntPtr.Zero)
            {
                return false;
            }

            IntPtr setPosRotMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "SetPositionAndRotation", 3);
            if (setPosRotMethod != IntPtr.Zero)
            {
                Vector3 posValue = playerPos;
                Quaternion rotArg = faceRot;
                bool worldSpace = true;
                IntPtr* setArgs = stackalloc IntPtr[3];
                setArgs[0] = (IntPtr)(&posValue);
                setArgs[1] = (IntPtr)(&rotArg);
                setArgs[2] = (IntPtr)(&worldSpace);
                exc2 = IntPtr.Zero;
                auraMonoRuntimeInvoke(setPosRotMethod, moveObj, (IntPtr)setArgs, ref exc2);
            }

            Vector2 forward2D = new Vector2(flatDir.x, flatDir.z);
            this.TrySetMonoVector2Member(moveObj, "_Forward", forward2D);
            this.TrySetMonoVector2Member(moveObj, "Forward", forward2D);
            return true;
        }

        private unsafe void ApplyNoclipVehicleFacing(IntPtr vehicleComponentObj, Vector3 targetPosition, Quaternion faceRot, Vector3 flatDir)
        {
            if (vehicleComponentObj == IntPtr.Zero
                || !this.EnsureNoclipVehicleAuraMono()
                || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(vehicleComponentObj, "MoveComponent", out IntPtr moveComponentObj) || moveComponentObj == IntPtr.Zero)
            {
                return;
            }

            if (this.noclipAuraWorldFaceToMethod != IntPtr.Zero)
            {
                Quaternion rotValue = faceRot;
                IntPtr exc = IntPtr.Zero;
                IntPtr* faceArgs = stackalloc IntPtr[1];
                faceArgs[0] = (IntPtr)(&rotValue);
                auraMonoRuntimeInvoke(this.noclipAuraWorldFaceToMethod, moveComponentObj, (IntPtr)faceArgs, ref exc);
            }

            if (this.noclipAuraSetPositionAndRotationMethod != IntPtr.Zero)
            {
                Vector3 posValue = targetPosition;
                Quaternion rotArg = faceRot;
                bool worldSpace = true;
                IntPtr exc = IntPtr.Zero;
                IntPtr* setArgs = stackalloc IntPtr[3];
                setArgs[0] = (IntPtr)(&posValue);
                setArgs[1] = (IntPtr)(&rotArg);
                setArgs[2] = (IntPtr)(&worldSpace);
                auraMonoRuntimeInvoke(this.noclipAuraSetPositionAndRotationMethod, moveComponentObj, (IntPtr)setArgs, ref exc);
            }

            Vector2 forward2D = new Vector2(flatDir.x, flatDir.z);
            this.TrySetMonoVector2Member(moveComponentObj, "_Forward", forward2D);
            this.TrySetMonoVector2Member(moveComponentObj, "Forward", forward2D);
        }

        private Vector3 BuildNoclipMoveDirection()
        {
            Vector3 moveDirection = Vector3.zero;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 cameraForward = mainCamera.transform.forward;
                Vector3 cameraRight = mainCamera.transform.right;
                cameraForward.y = 0f;
                cameraRight.y = 0f;
                cameraForward.Normalize();
                cameraRight.Normalize();

                if (Input.GetKey(KeyCode.W)) moveDirection += cameraForward;
                if (Input.GetKey(KeyCode.S)) moveDirection -= cameraForward;
                if (Input.GetKey(KeyCode.A)) moveDirection -= cameraRight;
                if (Input.GetKey(KeyCode.D)) moveDirection += cameraRight;
            }
            else
            {
                if (Input.GetKey(KeyCode.W)) moveDirection += Vector3.forward;
                if (Input.GetKey(KeyCode.S)) moveDirection += Vector3.back;
                if (Input.GetKey(KeyCode.A)) moveDirection += Vector3.left;
                if (Input.GetKey(KeyCode.D)) moveDirection += Vector3.right;
            }

            if (Input.GetKey(KeyCode.Space)) moveDirection += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) moveDirection -= Vector3.up;
            return moveDirection;
        }

        private float GetNoclipSpeed(bool onVehicle)
        {
            float currentSpeed = this.noclipSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                currentSpeed *= this.noclipBoostMultiplier;
            }

            if (onVehicle)
            {
                currentSpeed = Mathf.Min(currentSpeed, NoclipFeature.VehicleSpeedCap);
            }

            return currentSpeed;
        }

        private bool TryResolveNoclipVehicleContext(out IntPtr vehicleComponentObj, out IntPtr vehicleControllerObj, out Vector3 vehiclePosition)
        {
            vehicleComponentObj = IntPtr.Zero;
            vehicleControllerObj = IntPtr.Zero;
            vehiclePosition = NoclipFeature.OverrideVehicleTarget;

            if (!this.IsPlayerDrivingVehicle())
            {
                return false;
            }

            vehicleComponentObj = this.TryGetSelfEntityVehicleComponentMono();
            if (vehicleComponentObj == IntPtr.Zero)
            {
                vehicleComponentObj = this.TryGetSelfPassengerVehicleComponentMono();
            }

            if (vehicleComponentObj == IntPtr.Zero)
            {
                return false;
            }

            this.TryGetMonoObjectMember(vehicleComponentObj, "controller", out vehicleControllerObj);
            this.TryReadNoclipVehiclePositionMono(vehicleComponentObj, out vehiclePosition);
            return true;
        }

        private bool IsPlayerDrivingVehicle()
        {
            try
            {
                if (this.TryGetSelfEntityVehicleComponentMono() != IntPtr.Zero)
                {
                    return true;
                }

                if (this.TryGetManagedViewModuleSelfPlayerObject(out object selfPlayer, out _)
                    && this.TryGetObjectMember(selfPlayer, "IsDriving", out object isDrivingObj)
                    && isDrivingObj is bool isDriving
                    && isDriving)
                {
                    return true;
                }
            }
            catch
            {
            }

            GameObject player = GetPlayer();
            return player != null && player.transform.parent != null;
        }

        private unsafe IntPtr TryGetSelfEntityVehicleComponentMono()
        {
            if (!this.EnsureNoclipVehicleAuraMono() || this.noclipAuraGetSelfEntityVehicleMethod == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (!this.TryGetNoclipVehicleManagerMonoObject(out IntPtr managerObj) || managerObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr vehicleComponentObj = auraMonoRuntimeInvoke(this.noclipAuraGetSelfEntityVehicleMethod, managerObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero ? vehicleComponentObj : IntPtr.Zero;
        }

        private unsafe IntPtr TryGetSelfPassengerVehicleComponentMono()
        {
            if (!this.EnsureNoclipVehicleAuraMono()
                || this.noclipAuraGetPassengerVehicleMethod == IntPtr.Zero
                || !this.TryGetSelfPlayerNetId(out uint playerNetId)
                || playerNetId == 0)
            {
                return IntPtr.Zero;
            }

            if (!this.TryGetNoclipVehicleManagerMonoObject(out IntPtr managerObj) || managerObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&playerNetId);
            IntPtr vehicleComponentObj = auraMonoRuntimeInvoke(this.noclipAuraGetPassengerVehicleMethod, managerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || vehicleComponentObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (this.noclipAuraGetPassengerSeatMethod != IntPtr.Zero)
            {
                exc = IntPtr.Zero;
                IntPtr seatInfoObj = auraMonoRuntimeInvoke(this.noclipAuraGetPassengerSeatMethod, managerObj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && seatInfoObj != IntPtr.Zero)
                {
                    if (this.TryGetMonoIntMember(seatInfoObj, "index", out int seatIndex) && seatIndex != 0)
                    {
                        return IntPtr.Zero;
                    }
                }
            }

            return vehicleComponentObj;
        }

        private unsafe bool TryGetNoclipVehicleManagerMonoObject(out IntPtr managerObj)
        {
            managerObj = IntPtr.Zero;
            if (this.noclipAuraVehicleManagerClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetAuraMonoStaticObjectField(this.noclipAuraVehicleManagerClass, "_instance", out managerObj) && managerObj != IntPtr.Zero)
            {
                return true;
            }

            IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(this.noclipAuraVehicleManagerClass, "get_Instance", 0);
            if (getInstanceMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            managerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && managerObj != IntPtr.Zero;
        }

        private bool TryGetSelfPlayerNetId(out uint netId)
        {
            netId = 0;
            try
            {
                if (!this.TryGetManagedViewModuleSelfPlayerObject(out object selfPlayer, out _))
                {
                    return false;
                }

                if (this.TryGetObjectMember(selfPlayer, "entity", out object entityObj)
                    && this.TryGetObjectMember(entityObj, "netId", out object netIdObj))
                {
                    netId = netIdObj is uint u ? u : Convert.ToUInt32(netIdObj);
                    return netId != 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryReadNoclipVehiclePositionMono(IntPtr vehicleComponentObj, out Vector3 position)
        {
            position = NoclipFeature.OverrideVehicleTarget;
            if (vehicleComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(vehicleComponentObj, "entity", out IntPtr entityObj)
                && entityObj != IntPtr.Zero
                && this.TryGetMonoVector3Member(entityObj, "position", out position))
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(vehicleComponentObj, "controller", out IntPtr controllerObj)
                && controllerObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(controllerObj, "Res", out IntPtr resObj)
                && resObj != IntPtr.Zero
                && this.TryExtractHomePositionMonoObject(resObj, out position))
            {
                return true;
            }

            return false;
        }

        private unsafe void ApplyNoclipVehicleWorldPlace(IntPtr vehicleComponentObj, Vector3 targetPosition)
        {
            if (vehicleComponentObj == IntPtr.Zero || !this.EnsureNoclipVehicleAuraMono() || this.noclipAuraWorldPlaceToMethod == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(vehicleComponentObj, "MoveComponent", out IntPtr moveComponentObj) || moveComponentObj == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            Vector3 positionValue = targetPosition;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&positionValue);
            auraMonoRuntimeInvoke(this.noclipAuraWorldPlaceToMethod, moveComponentObj, (IntPtr)args, ref exc);
        }

        private bool TryEnsureNoclipMonoInputManagerMethods()
        {
            if (this.noclipAuraMonoInputManagerObj.TryGet(out _)
                && this.noclipAuraDisableInputMethod != IntPtr.Zero
                && this.noclipAuraEnableInputMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoManagerFromServiceDic("MonoInputManager", out IntPtr managerObj) || managerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr managerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(managerObj) : IntPtr.Zero;
            if (managerClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr disableMethod = this.FindAuraMonoMethodOnHierarchy(managerClass, "DisableInput", 1);
            IntPtr enableMethod = this.FindAuraMonoMethodOnHierarchy(managerClass, "EnableInput", 1);
            if (disableMethod == IntPtr.Zero || enableMethod == IntPtr.Zero)
            {
                return false;
            }

            this.noclipAuraMonoInputManagerObj.Set(managerObj);
            this.noclipAuraDisableInputMethod = disableMethod;
            this.noclipAuraEnableInputMethod = enableMethod;
            return true;
        }

        private bool TryGetAuraMonoManagerFromServiceDic(string managerNameToken, out IntPtr managerObj)
        {
            managerObj = IntPtr.Zero;
            if (string.IsNullOrEmpty(managerNameToken))
            {
                return false;
            }

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                return false;
            }

            if ((!this.TryGetAuraMonoStaticObjectField(managersClass, "_serviceDic", out IntPtr serviceDicObj) || serviceDicObj == IntPtr.Zero)
                && (!this.TryGetAuraMonoStaticObjectField(managersClass, "serviceDic", out serviceDicObj) || serviceDicObj == IntPtr.Zero))
            {
                return false;
            }

            System.Collections.Generic.List<IntPtr> entries = new System.Collections.Generic.List<IntPtr>(32);
            if (!this.TryEnumerateAuraMonoCollectionItems(serviceDicObj, entries) || entries.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entryObj = entries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                if ((!this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr serviceObj) || serviceObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "value", out serviceObj) || serviceObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "_value", out serviceObj) || serviceObj == IntPtr.Zero))
                {
                    continue;
                }

                if ((!this.TryGetMonoObjectMember(serviceObj, "manager", out IntPtr candidateObj) || candidateObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(serviceObj, "_manager", out candidateObj) || candidateObj == IntPtr.Zero))
                {
                    continue;
                }

                IntPtr candidateClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(candidateObj) : IntPtr.Zero;
                string managerName = candidateClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(candidateClass) : string.Empty;
                if (managerName.IndexOf(managerNameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    managerObj = candidateObj;
                    return true;
                }
            }

            return false;
        }

        private unsafe void SetNoclipVehicleJumpInputSuppressed(bool suppressed)
        {
            if (suppressed == this.noclipVehicleJumpInputSuppressed)
            {
                return;
            }

            if (!this.TryEnsureNoclipMonoInputManagerMethods())
            {
                return;
            }

            IntPtr method = suppressed ? this.noclipAuraDisableInputMethod : this.noclipAuraEnableInputMethod;
            if (method == IntPtr.Zero || !this.noclipAuraMonoInputManagerObj.TryGet(out IntPtr inputManagerObj))
            {
                return;
            }

            // ScriptsRefactory.BaseService.Input.InputEvent.Jump
            int jumpInputEvent = 1;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&jumpInputEvent);
            auraMonoRuntimeInvoke(method, inputManagerObj, (IntPtr)args, ref exc);
            if (exc == IntPtr.Zero)
            {
                this.noclipVehicleJumpInputSuppressed = suppressed;
            }
        }

        // Calls MonoInputManager.DisableInput / EnableInput for a given InputEvent. The game
        // keeps a per-event disable refcount, so every Disable must be balanced by exactly one
        // Enable. Returns true if the invoke succeeded. InputEvent values: Move=0, Jump=1
        // (ScriptsRefactory.BaseService.Input.InputEvent).
        private unsafe bool TrySetMonoInputDisabled(int inputEvent, bool disable)
        {
            if (!this.TryEnsureNoclipMonoInputManagerMethods())
            {
                return false;
            }

            IntPtr method = disable ? this.noclipAuraDisableInputMethod : this.noclipAuraEnableInputMethod;
            if (method == IntPtr.Zero || !this.noclipAuraMonoInputManagerObj.TryGet(out IntPtr inputManagerObj))
            {
                return false;
            }

            int ev = inputEvent;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&ev);
            auraMonoRuntimeInvoke(method, inputManagerObj, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe void TryInvokeNoclipVehicleResetVirtualInput(IntPtr vehicleComponentObj)
        {
            if (vehicleComponentObj == IntPtr.Zero
                || this.noclipAuraResetVirtualInputMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.noclipAuraResetVirtualInputMethod, vehicleComponentObj, IntPtr.Zero, ref exc);
        }

        private unsafe void SetNoclipVehicleForceDisplacement(IntPtr vehicleComponentObj, bool enabled)
        {
            if (vehicleComponentObj == IntPtr.Zero)
            {
                return;
            }

            if (!this.EnsureNoclipVehicleAuraMono())
            {
                return;
            }

            if (this.noclipAuraForceDisplacementMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                bool value = enabled;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&value);
                auraMonoRuntimeInvoke(this.noclipAuraForceDisplacementMethod, vehicleComponentObj, (IntPtr)args, ref exc);
                return;
            }

            this.TrySetNoclipMonoBoolMember(vehicleComponentObj, "_isForceDisplacement", enabled);
        }

        private void TrySetNoclipVehicleControllerStopMove(IntPtr vehicleControllerObj, bool stop)
        {
            if (vehicleControllerObj == IntPtr.Zero)
            {
                return;
            }

            string className = this.GetAuraMonoClassDisplayName(
                auraMonoObjectGetClass != null ? auraMonoObjectGetClass(vehicleControllerObj) : IntPtr.Zero);
            if (className.IndexOf("SelfVehicleController", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            this.TrySetNoclipMonoBoolMember(vehicleControllerObj, "stopMove", stop);
        }

        private void TryZeroNoclipVehicleControllerInput(IntPtr vehicleControllerObj)
        {
            if (vehicleControllerObj == IntPtr.Zero || !NoclipFeature.OverrideVehiclePosition)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(vehicleControllerObj, "_inputData", out IntPtr inputDataObj) || inputDataObj == IntPtr.Zero)
            {
                return;
            }

            this.TrySetMonoVector2Member(inputDataObj, "moveAxis", Vector2.zero);
        }

        private unsafe bool TrySetNoclipMonoBoolMember(IntPtr obj, string memberName, bool value)
        {
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName) || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            IntPtr fieldPtr = this.FindAuraMonoFieldOnHierarchy(classPtr, memberName);
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }

            bool fieldValue = value;
            auraMonoFieldSetValue(obj, fieldPtr, (IntPtr)(&fieldValue));
            return true;
        }

        private bool EnsureNoclipVehicleAuraMono()
        {
            if (this.noclipVehicleAuraReady)
            {
                return true;
            }

            this.EnsureNoclipVehicleAuraMono(logIfPending: false);
            return this.noclipVehicleAuraReady;
        }
    }
}
