using System;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int VehicleTeleportHoldFrames = 24;

        private static readonly string[] VehicleTeleportProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll",
        };

        private int vehicleTeleportHoldFrames;
        private Vector3 vehicleTeleportTargetPos;
        private bool vehicleTeleportHasTargetRot;
        private Quaternion vehicleTeleportTargetRot;
        private AuraMonoObjectCache vehicleTeleportComponentCache;
        private AuraMonoObjectCache vehicleTeleportControllerCache;
        private IntPtr vehicleTeleportTransformMethod;
        private bool vehicleTeleportTransformMethodResolved;
        private bool vehicleTeleportTransformMethodResolveFailed;

        private bool TryTeleportVehicleToLocation(Vector3 targetPos, Quaternion? targetRot)
        {
            if (!this.IsPlayerDrivingVehicle())
            {
                return false;
            }

            if (this.TryGetSelfEntityVehicleComponentMono() == IntPtr.Zero)
            {
                return false;
            }

            this.EnsureNoclipVehicleAuraMono(logIfPending: true);
            if (!this.noclipVehicleAuraReady)
            {
                return false;
            }

            if (!this.TryResolveNoclipVehicleContext(out IntPtr vehicleComponentObj, out IntPtr vehicleControllerObj, out _))
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(vehicleComponentObj, "entity", out IntPtr entityObj)
                || entityObj == IntPtr.Zero
                || !this.TryGetMonoUInt32Member(entityObj, "netId", out uint vehicleNetId)
                || vehicleNetId == 0)
            {
                return false;
            }

            float yAxis;
            if (targetRot.HasValue)
            {
                yAxis = targetRot.Value.eulerAngles.y;
            }
            else
            {
                GameObject player = GetPlayer();
                yAxis = player != null ? player.transform.rotation.eulerAngles.y : 0f;
            }

            this.ActivateNoclipVehicleDrivingOverride(vehicleComponentObj, vehicleControllerObj);
            this.ApplyNoclipVehicleWorldPlace(vehicleComponentObj, targetPos);
            if (targetRot.HasValue)
            {
                Vector3 flatDir = targetRot.Value * Vector3.forward;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude >= 0.04f)
                {
                    this.ApplyNoclipVehicleFacing(vehicleComponentObj, targetPos, targetRot.Value, flatDir);
                }
            }

            this.TryVehicleTeleportSendTransform(vehicleNetId, targetPos, yAxis);

            this.vehicleTeleportComponentCache.Set(vehicleComponentObj);
            this.vehicleTeleportControllerCache.Set(vehicleControllerObj);
            this.vehicleTeleportTargetPos = targetPos;
            this.vehicleTeleportHasTargetRot = targetRot.HasValue;
            if (targetRot.HasValue)
            {
                this.vehicleTeleportTargetRot = targetRot.Value;
            }

            this.vehicleTeleportHoldFrames = VehicleTeleportHoldFrames;
            int passengerCount = this.TryVehicleTeleportCountPassengers(vehicleComponentObj);
            ModLogger.Msg(
                "[VehicleTeleport] warped vehicle netId="
                + vehicleNetId
                + " to "
                + targetPos.ToString("F1")
                + (passengerCount > 0 ? (" (+ " + passengerCount + " passenger(s))") : string.Empty));
            return true;
        }

        private void ProcessVehicleTeleportOnLateUpdate()
        {
            if (this.noclipEnabled || this.vehicleTeleportHoldFrames <= 0)
            {
                return;
            }

            this.vehicleTeleportComponentCache.TryGet(out IntPtr vehicleComponentObj);
            this.vehicleTeleportControllerCache.TryGet(out IntPtr vehicleControllerObj);
            if (vehicleComponentObj == IntPtr.Zero)
            {
                this.vehicleTeleportHoldFrames = 0;
                return;
            }

            this.ActivateNoclipVehicleDrivingOverride(vehicleComponentObj, vehicleControllerObj);
            this.ApplyNoclipVehicleWorldPlace(vehicleComponentObj, this.vehicleTeleportTargetPos);
            if (this.vehicleTeleportHasTargetRot)
            {
                Vector3 flatDir = this.vehicleTeleportTargetRot * Vector3.forward;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude >= 0.04f)
                {
                    this.ApplyNoclipVehicleFacing(
                        vehicleComponentObj,
                        this.vehicleTeleportTargetPos,
                        this.vehicleTeleportTargetRot,
                        flatDir);
                }
            }

            this.vehicleTeleportHoldFrames--;
            if (this.vehicleTeleportHoldFrames <= 0)
            {
                this.RestoreNoclipVehicleDrivingState(vehicleComponentObj, vehicleControllerObj);
                this.vehicleTeleportComponentCache.Clear();
                this.vehicleTeleportControllerCache.Clear();
            }
        }

        private int TryVehicleTeleportCountPassengers(IntPtr vehicleComponentObj)
        {
            if (vehicleComponentObj == IntPtr.Zero
                || auraMonoObjectGetClass == null
                || auraMonoClassGetMethodFromName == null
                || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr vehicleClass = auraMonoObjectGetClass(vehicleComponentObj);
            IntPtr getAllMethod = this.FindAuraMonoMethodOnHierarchy(vehicleClass, "GetAllPassenger", 0);
            if (getAllMethod == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr listObj = auraMonoRuntimeInvoke(getAllMethod, vehicleComponentObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                return 0;
            }

            System.Collections.Generic.List<IntPtr> items = new System.Collections.Generic.List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items))
            {
                return 0;
            }

            int count = items.Count;
            if (this.TryGetSelfPlayerNetId(out uint selfNetId) && selfNetId != 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != IntPtr.Zero
                        && this.TryUnboxMonoUInt32(items[i], out uint passengerNetId)
                        && passengerNetId == selfNetId)
                    {
                        count--;
                        break;
                    }
                }
            }

            return Math.Max(0, count);
        }

        private bool TryEnsureVehicleTeleportTransformMethod()
        {
            if (this.vehicleTeleportTransformMethodResolved)
            {
                return this.vehicleTeleportTransformMethod != IntPtr.Zero;
            }

            if (this.vehicleTeleportTransformMethodResolveFailed)
            {
                return false;
            }

            IntPtr protocolClass = this.ResolveVehicleBypassClass(
                "XDTDataAndProtocol.ProtocolService.Vehicle",
                "VehicleProtocolManager",
                VehicleTeleportProtocolImages,
                "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            }

            if (protocolClass != IntPtr.Zero)
            {
                this.vehicleTeleportTransformMethod = this.FindVehicleBypassMethod(
                    protocolClass,
                    "VehicleTransform",
                    4);
            }

            this.vehicleTeleportTransformMethodResolved = true;
            if (this.vehicleTeleportTransformMethod == IntPtr.Zero)
            {
                this.vehicleTeleportTransformMethodResolveFailed = true;
                return false;
            }

            return true;
        }

        private unsafe bool TryVehicleTeleportSendTransform(uint vehicleNetId, Vector3 position, float yAxis)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryEnsureVehicleTeleportTransformMethod())
            {
                return false;
            }

            uint netId = vehicleNetId;
            Vector3 pos = position;
            float axis = yAxis;
            float speed = 0f;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[4];
            args[0] = (IntPtr)(&netId);
            args[1] = (IntPtr)(&pos);
            args[2] = (IntPtr)(&axis);
            args[3] = (IntPtr)(&speed);
            auraMonoRuntimeInvoke(this.vehicleTeleportTransformMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }
    }
}
