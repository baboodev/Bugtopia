using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // Client-side bypass for homeland vehicle permission and forbidden-vehicle zones (variant B).
    // Mono NativeDetours on AuraMono methods; Apply while toggle on, Undo when off (BuildingFreeRotate pattern).
    public partial class HeartopiaComplete
    {
        public bool vehicleBypassEnabled;

        private const float VehicleBypassHookRetrySeconds = 3f;
        private float vehicleBypassNextHookAttemptAt = -999f;
        private bool vehicleBypassHooksHardFailed;
        private bool vehicleBypassReadyLogged;

        private static readonly string[] VehicleBypassLevelEntityImages =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll",
        };

        private static readonly string[] VehicleBypassGameSystemImages =
        {
            "XDTGameSystem", "XDTGameSystem.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll",
        };

        private static readonly string[] VehicleBypassEcsSystemImages =
        {
            "EcsSystem", "EcsSystem.dll",
            "EcsClient", "EcsClient.dll",
            "Client", "Client.dll",
        };

        private static VehicleBypassCompileMethodDelegate vehicleBypassCompileMethod;

        private static VehicleBypassDetourSlot vehicleBypassHomeStaySlot;
        private static VehicleBypassDetourSlot vehicleBypassForbiddenEnterSlot;
        private static VehicleBypassDetourSlot vehicleBypassPosForbiddenSlot;
        private static VehicleBypassDetourSlot vehicleBypassHomePermissionSlot;
        private static VehicleBypassDetourSlot vehicleBypassPartyForbidSlot;
        private static VehicleBypassDetourSlot vehicleBypassInNoPermissionSlot;
        private static VehicleBypassDetourSlot vehicleBypassCreateDrivingSlot;

        private static VehicleBypassVoid4HookDelegate vehicleBypassHomeStayHook;
        private static VehicleBypassVoid3HookDelegate vehicleBypassForbiddenEnterHook;
        private static VehicleBypassBoolSelfPosHookDelegate vehicleBypassPosForbiddenHook;
        private static VehicleBypassBoolSelfUintHookDelegate vehicleBypassHomePermissionHook;
        private static VehicleBypassBoolSelfUintHookDelegate vehicleBypassPartyForbidHook;
        private static VehicleBypassBoolSelfOnlyHookDelegate vehicleBypassInNoPermissionHook;

        private enum VehicleBypassDelegateKind
        {
            Void4,
            Void3,
            BoolSelfPosFalse,
            BoolSelfUintFalse,
            BoolSelfUintTrue,
            BoolSelfOnlyFalse,
        }

        private struct VehicleBypassDetourSlot
        {
            public NativeDetour Detour;
            public bool InstallTried;
            public bool MissingLogged;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr VehicleBypassCompileMethodDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassVoid4HookDelegate(IntPtr self, IntPtr arg0, IntPtr arg1, IntPtr arg2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassVoid3HookDelegate(IntPtr self, IntPtr arg0, IntPtr arg1);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassBoolSelfPosHookDelegate(IntPtr self, IntPtr pos);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassBoolSelfUintHookDelegate(IntPtr self, uint netId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassBoolSelfOnlyHookDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassCreateDrivingHookDelegate(IntPtr self, int itemId, int getOnVehicle);

        private static VehicleBypassCreateDrivingHookDelegate vehicleBypassCreateDrivingHook;
        private static VehicleBypassCreateDrivingHookDelegate vehicleBypassCreateDrivingTrampoline;

        private static volatile bool vehicleBypassPendingForceSummon;
        private static int vehicleBypassPendingItemId;
        private static bool vehicleBypassPendingGetOnVehicle;
        private static int vehicleBypassPendingAttempts;

        private IntPtr vehicleBypassGetLevelObjectIdMethod;
        private IntPtr vehicleBypassPlayerCallVehicleMethod;
        private IntPtr vehicleBypassCallVehicleCmdClass;
        private IntPtr vehicleBypassSendCommandMethod;
        private IntPtr vehicleBypassCmdStaticIdField;
        private IntPtr vehicleBypassCmdPositionField;
        private IntPtr vehicleBypassCmdYAxisField;
        private IntPtr vehicleBypassCmdLevelObjectIdField;
        private IntPtr vehicleBypassCmdGetOnField;
        private bool vehicleBypassSummonMethodsResolved;
        private bool vehicleBypassSummonMethodsResolveFailed;

        private const float VehicleBypassSpawnExtraHeight = 0.001f;

        private void ProcessVehicleBypassOnUpdate()
        {
            if (this.vehicleBypassHooksHardFailed)
            {
                return;
            }

            if (!this.vehicleBypassEnabled)
            {
                this.RemoveVehicleBypassDetours();
                this.vehicleBypassReadyLogged = false;
                return;
            }

            if (!this.VehicleBypassAllHooksApplied())
            {
                if (Time.unscaledTime >= this.vehicleBypassNextHookAttemptAt)
                {
                    this.vehicleBypassNextHookAttemptAt = Time.unscaledTime + VehicleBypassHookRetrySeconds;
                    this.EnsureVehicleBypassDetours();
                }
            }
            else if (!this.vehicleBypassReadyLogged)
            {
                this.vehicleBypassReadyLogged = true;
                ModLogger.Msg("[VehicleBypass] all detours applied (homeland + forbidden zone + summon).");
            }

            this.TryVehicleBypassProcessPendingSummon();
        }

        private bool VehicleBypassAllHooksApplied()
        {
            return VehicleBypassSlotApplied(vehicleBypassHomeStaySlot)
                && VehicleBypassSlotApplied(vehicleBypassForbiddenEnterSlot)
                && VehicleBypassSlotApplied(vehicleBypassPosForbiddenSlot)
                && VehicleBypassSlotApplied(vehicleBypassHomePermissionSlot)
                && VehicleBypassSlotApplied(vehicleBypassPartyForbidSlot)
                && VehicleBypassSlotApplied(vehicleBypassInNoPermissionSlot)
                && VehicleBypassSlotApplied(vehicleBypassCreateDrivingSlot);
        }

        private static bool VehicleBypassSlotApplied(VehicleBypassDetourSlot slot)
            => slot.Detour != null && slot.Detour.IsApplied;

        private void EnsureVehicleBypassDetours()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                if (vehicleBypassCompileMethod == null)
                {
                    IntPtr monoModule = this.GetAuraMonoModuleHandle();
                    if (monoModule == IntPtr.Zero)
                    {
                        return;
                    }

                    vehicleBypassCompileMethod = this.GetAuraMonoExport<VehicleBypassCompileMethodDelegate>(monoModule, "mono_compile_method");
                    if (vehicleBypassCompileMethod == null)
                    {
                        this.vehicleBypassHooksHardFailed = true;
                        ModLogger.Msg("[VehicleBypass] mono_compile_method unavailable — feature disabled");
                        return;
                    }
                }

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassHomeStaySlot,
                    VehicleBypassDelegateKind.Void4,
                    "XDTLevelAndEntity.Gameplay.Triggers",
                    "HomeVehiclePermissionTriggerCase",
                    VehicleBypassLevelEntityImages,
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Triggers.HomeVehiclePermissionTriggerCase",
                    "OnTriggerStay",
                    3);

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassForbiddenEnterSlot,
                    VehicleBypassDelegateKind.Void3,
                    "XDTLevelAndEntity.Gameplay.Triggers",
                    "ForbiddenVehicleTriggerCase",
                    VehicleBypassLevelEntityImages,
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Triggers.ForbiddenVehicleTriggerCase",
                    "OnTriggerEnter",
                    2);

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassPosForbiddenSlot,
                    VehicleBypassDelegateKind.BoolSelfPosFalse,
                    "ClientSystem.Area",
                    "MetaAreaClientService",
                    VehicleBypassEcsSystemImages,
                    "ClientSystem.Area.MetaAreaClientService",
                    "IsPosForbiddenForVehicle",
                    1);

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassHomePermissionSlot,
                    VehicleBypassDelegateKind.BoolSelfUintTrue,
                    "XDTGameSystem.GameplaySystem.Homeland",
                    "HomelandSystem",
                    VehicleBypassGameSystemImages,
                    "XDTGameSystem.GameplaySystem.Homeland.HomelandSystem",
                    "CheckHomeVehiclePermission",
                    1);

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassPartyForbidSlot,
                    VehicleBypassDelegateKind.BoolSelfUintFalse,
                    "XDTGameSystem.GameplaySystem.Party",
                    "PartySystem",
                    VehicleBypassGameSystemImages,
                    "XDTGameSystem.GameplaySystem.Party.PartySystem",
                    "IsHomelandForbidVehicleByParty",
                    1);

                this.TryInstallVehicleBypassDetour(
                    ref vehicleBypassInNoPermissionSlot,
                    VehicleBypassDelegateKind.BoolSelfOnlyFalse,
                    "XDTGameSystem.GameplaySystem.Vehicle",
                    "VehicleSystem",
                    VehicleBypassGameSystemImages,
                    "XDTGameSystem.GameplaySystem.Vehicle.VehicleSystem",
                    "get_InNoVehiclePermissionHomeland",
                    0);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassCreateDrivingSlot,
                    ref vehicleBypassCreateDrivingHook,
                    ref vehicleBypassCreateDrivingTrampoline,
                    VehicleBypassCreateDrivingVehicleNative,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle",
                    "VehicleManager",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle.VehicleManager",
                    "CreateDrivingVehicle",
                    1,
                    2);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[VehicleBypass] install pass failed: " + ex.Message);
            }
        }

        private void TryInstallVehicleBypassTrampolineDetour<TDelegate>(
            ref VehicleBypassDetourSlot slot,
            ref TDelegate hookKeepAlive,
            ref TDelegate trampoline,
            TDelegate hookBody,
            string nameSpace,
            string shortName,
            string[] imageNames,
            string fullTypeFallback,
            string methodName,
            params int[] paramCounts) where TDelegate : Delegate
        {
            if (slot.Detour != null)
            {
                if (!slot.Detour.IsApplied)
                {
                    slot.Detour.Apply();
                }

                return;
            }

            if (slot.InstallTried)
            {
                return;
            }

            IntPtr cls = this.ResolveVehicleBypassClass(nameSpace, shortName, imageNames, fullTypeFallback);
            if (cls == IntPtr.Zero)
            {
                return;
            }

            IntPtr method = this.FindVehicleBypassMethod(cls, methodName, paramCounts);
            if (method == IntPtr.Zero)
            {
                slot.InstallTried = true;
                if (!slot.MissingLogged)
                {
                    slot.MissingLogged = true;
                    ModLogger.Msg("[VehicleBypass] " + shortName + "." + methodName + " not found");
                }

                return;
            }

            IntPtr nativePtr = vehicleBypassCompileMethod(method);
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            hookKeepAlive = hookBody;
            slot.Detour = new NativeDetour(nativePtr, hookKeepAlive);
            trampoline = slot.Detour.GenerateTrampoline<TDelegate>();
            if (trampoline == null)
            {
                try
                {
                    slot.Detour.Undo();
                }
                catch
                {
                }

                slot.Detour = null;
                hookKeepAlive = null;
                slot.InstallTried = true;
                ModLogger.Msg("[VehicleBypass] trampoline unavailable for " + shortName + "." + methodName);
                return;
            }

            slot.InstallTried = true;
            slot.Detour.Apply();
            ModLogger.Msg("[VehicleBypass] hooked " + shortName + "." + methodName + " @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private IntPtr FindVehicleBypassMethod(IntPtr classPtr, string methodName, params int[] paramCounts)
        {
            if (classPtr == IntPtr.Zero || paramCounts == null || paramCounts.Length == 0)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < paramCounts.Length; i++)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, paramCounts[i]);
                if (method != IntPtr.Zero)
                {
                    return method;
                }
            }

            return IntPtr.Zero;
        }

        private void TryInstallVehicleBypassDetour(
            ref VehicleBypassDetourSlot slot,
            VehicleBypassDelegateKind delegateKind,
            string nameSpace,
            string shortName,
            string[] imageNames,
            string fullTypeFallback,
            string methodName,
            int paramCount)
        {
            if (slot.Detour != null)
            {
                if (!slot.Detour.IsApplied)
                {
                    slot.Detour.Apply();
                }

                return;
            }

            if (slot.InstallTried)
            {
                return;
            }

            IntPtr cls = this.ResolveVehicleBypassClass(nameSpace, shortName, imageNames, fullTypeFallback);
            if (cls == IntPtr.Zero)
            {
                return;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, methodName, paramCount);
            if (method == IntPtr.Zero)
            {
                slot.InstallTried = true;
                if (!slot.MissingLogged)
                {
                    slot.MissingLogged = true;
                    ModLogger.Msg("[VehicleBypass] " + shortName + "." + methodName + "(" + paramCount + ") not found");
                }

                return;
            }

            IntPtr nativePtr = vehicleBypassCompileMethod(method);
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            Delegate hook = this.CreateVehicleBypassHookDelegate(delegateKind);
            if (hook == null)
            {
                slot.InstallTried = true;
                ModLogger.Msg("[VehicleBypass] unsupported delegate kind for " + shortName + "." + methodName);
                return;
            }

            this.AssignVehicleBypassHookKeepAlive(delegateKind, hook);
            slot.Detour = new NativeDetour(nativePtr, hook);
            slot.InstallTried = true;
            slot.Detour.Apply();
            ModLogger.Msg("[VehicleBypass] hooked " + shortName + "." + methodName + " @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void AssignVehicleBypassHookKeepAlive(VehicleBypassDelegateKind delegateKind, Delegate hook)
        {
            switch (delegateKind)
            {
                case VehicleBypassDelegateKind.Void4:
                    vehicleBypassHomeStayHook = (VehicleBypassVoid4HookDelegate)hook;
                    break;
                case VehicleBypassDelegateKind.Void3:
                    vehicleBypassForbiddenEnterHook = (VehicleBypassVoid3HookDelegate)hook;
                    break;
                case VehicleBypassDelegateKind.BoolSelfPosFalse:
                    vehicleBypassPosForbiddenHook = (VehicleBypassBoolSelfPosHookDelegate)hook;
                    break;
                case VehicleBypassDelegateKind.BoolSelfUintTrue:
                    vehicleBypassHomePermissionHook = (VehicleBypassBoolSelfUintHookDelegate)hook;
                    break;
                case VehicleBypassDelegateKind.BoolSelfUintFalse:
                    vehicleBypassPartyForbidHook = (VehicleBypassBoolSelfUintHookDelegate)hook;
                    break;
                case VehicleBypassDelegateKind.BoolSelfOnlyFalse:
                    vehicleBypassInNoPermissionHook = (VehicleBypassBoolSelfOnlyHookDelegate)hook;
                    break;
            }
        }

        private Delegate CreateVehicleBypassHookDelegate(VehicleBypassDelegateKind delegateKind)
        {
            switch (delegateKind)
            {
                case VehicleBypassDelegateKind.Void4:
                    return (VehicleBypassVoid4HookDelegate)VehicleBypassVoidNoop4;
                case VehicleBypassDelegateKind.Void3:
                    return (VehicleBypassVoid3HookDelegate)VehicleBypassVoidNoop3;
                case VehicleBypassDelegateKind.BoolSelfPosFalse:
                    return (VehicleBypassBoolSelfPosHookDelegate)VehicleBypassReturnFalsePos;
                case VehicleBypassDelegateKind.BoolSelfUintTrue:
                    return (VehicleBypassBoolSelfUintHookDelegate)VehicleBypassReturnTrueUint;
                case VehicleBypassDelegateKind.BoolSelfUintFalse:
                    return (VehicleBypassBoolSelfUintHookDelegate)VehicleBypassReturnFalseUint;
                case VehicleBypassDelegateKind.BoolSelfOnlyFalse:
                    return (VehicleBypassBoolSelfOnlyHookDelegate)VehicleBypassReturnFalseSelfOnly;
                default:
                    return null;
            }
        }

        private IntPtr ResolveVehicleBypassClass(string nameSpace, string shortName, string[] imageNames, string fullTypeFallback)
        {
            IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, imageNames);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            cls = this.FindAuraMonoClassInAllLoadedImages(shortName, nameSpace);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            if (!string.IsNullOrWhiteSpace(fullTypeFallback))
            {
                cls = this.FindAuraMonoClassByFullName(fullTypeFallback);
            }

            return cls;
        }

        private void RemoveVehicleBypassDetours()
        {
            this.UndoVehicleBypassSlot(ref vehicleBypassHomeStaySlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassForbiddenEnterSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassPosForbiddenSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassHomePermissionSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassPartyForbidSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassInNoPermissionSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassCreateDrivingSlot);
            vehicleBypassCreateDrivingTrampoline = null;
            vehicleBypassPendingForceSummon = false;
            vehicleBypassPendingAttempts = 0;
        }

        private void UndoVehicleBypassSlot(ref VehicleBypassDetourSlot slot)
        {
            try
            {
                if (slot.Detour != null && slot.Detour.IsApplied)
                {
                    slot.Detour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[VehicleBypass] undo failed: " + ex.Message);
            }
        }

        private static void VehicleBypassVoidNoop4(IntPtr self, IntPtr arg0, IntPtr arg1, IntPtr arg2)
        {
        }

        private static void VehicleBypassVoidNoop3(IntPtr self, IntPtr arg0, IntPtr arg1)
        {
        }

        private static byte VehicleBypassReturnFalsePos(IntPtr self, IntPtr pos) => 0;

        private static byte VehicleBypassReturnTrueUint(IntPtr self, uint netId) => 1;

        private static byte VehicleBypassReturnFalseUint(IntPtr self, uint netId) => 0;

        private static byte VehicleBypassReturnFalseSelfOnly(IntPtr self) => 0;

        private static byte VehicleBypassCreateDrivingVehicleNative(IntPtr self, int itemId, int getOnVehicle)
        {
            if (vehicleBypassCreateDrivingTrampoline == null)
            {
                return 0;
            }

            byte result = vehicleBypassCreateDrivingTrampoline(self, itemId, getOnVehicle);
            if (result != 0)
            {
                return result;
            }

            vehicleBypassPendingItemId = itemId;
            vehicleBypassPendingGetOnVehicle = getOnVehicle != 0;
            vehicleBypassPendingAttempts = 0;
            vehicleBypassPendingForceSummon = true;
            return 0;
        }

        private void TryVehicleBypassProcessPendingSummon()
        {
            if (!this.vehicleBypassEnabled || !vehicleBypassPendingForceSummon)
            {
                return;
            }

            vehicleBypassPendingForceSummon = false;
            int itemId = vehicleBypassPendingItemId;
            bool getOnVehicle = vehicleBypassPendingGetOnVehicle;
            if (itemId == 0)
            {
                return;
            }

            if (!this.TryVehicleBypassForceSummon(itemId, getOnVehicle, out string error))
            {
                if (vehicleBypassPendingAttempts < 3)
                {
                    vehicleBypassPendingAttempts++;
                    vehicleBypassPendingForceSummon = true;
                }

                ModLogger.Msg("[VehicleBypass] force summon failed (" + vehicleBypassPendingAttempts + "/3): " + error);
                return;
            }

            vehicleBypassPendingAttempts = 0;
            ModLogger.Msg("[VehicleBypass] force summon sent itemId=" + itemId + " getOn=" + getOnVehicle);
        }

        private bool TryVehicleBypassForceSummon(int itemId, bool getOnVehicle, out string error)
        {
            error = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono not ready";
                return false;
            }

            if (!this.TryVehicleBypassEnsureSummonMethods())
            {
                error = "summon methods unavailable";
                return false;
            }

            if (!this.TryGetNoclipVehicleManagerMonoObject(out IntPtr managerObj) || managerObj == IntPtr.Zero)
            {
                error = "VehicleManager unavailable";
                return false;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
            {
                error = "player position unavailable";
                return false;
            }

            Transform playerTransform = GameObject.Find("p_player_skeleton(Clone)")?.transform;
            Quaternion playerRot = playerTransform != null ? playerTransform.rotation : Quaternion.identity;
            Vector3 forward = playerRot * Vector3.forward;
            Vector3 vehiclePos = new Vector3(
                playerPos.x + forward.x,
                playerPos.y + VehicleBypassSpawnExtraHeight,
                playerPos.z + forward.z);
            float yAxis = playerRot.eulerAngles.y;

            if (!this.TryVehicleBypassGetLevelObjectId(managerObj, itemId, out int levelObjectId))
            {
                error = "levelObjectId unavailable";
                return false;
            }

            return this.TryVehicleBypassInvokePlayerCallVehicle(itemId, vehiclePos, yAxis, levelObjectId, getOnVehicle, out error)
                || this.TryVehicleBypassSendCallVehicleCommand(itemId, vehiclePos, yAxis, levelObjectId, getOnVehicle, out error);
        }

        private bool TryVehicleBypassEnsureSummonMethods()
        {
            if (this.vehicleBypassSummonMethodsResolved)
            {
                return (this.vehicleBypassGetLevelObjectIdMethod != IntPtr.Zero
                        && (this.vehicleBypassPlayerCallVehicleMethod != IntPtr.Zero
                            || this.vehicleBypassSendCommandMethod != IntPtr.Zero));
            }

            if (this.vehicleBypassSummonMethodsResolveFailed)
            {
                return false;
            }

            IntPtr managerClass = this.ResolveVehicleBypassClass(
                "XDTLevelAndEntity.GameplaySystem.Vehicle",
                "VehicleManager",
                VehicleBypassLevelEntityImages,
                "XDTLevelAndEntity.GameplaySystem.Vehicle.VehicleManager");
            if (managerClass != IntPtr.Zero)
            {
                this.vehicleBypassGetLevelObjectIdMethod = this.FindVehicleBypassMethod(
                    managerClass,
                    "GetVehicleLevleObjectId",
                    2,
                    3);
            }

            IntPtr protocolClass = this.ResolveVehicleBypassClass(
                "XDTDataAndProtocol.ProtocolService.Vehicle",
                "VehicleProtocolManager",
                VehicleBypassLevelEntityImages,
                "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            }

            if (protocolClass != IntPtr.Zero)
            {
                this.vehicleBypassPlayerCallVehicleMethod = this.FindVehicleBypassMethod(
                    protocolClass,
                    "PlayerCallVehicle",
                    5);
            }

            IntPtr cmdClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Vehicle.CallVehicleCommand");
            if (cmdClass == IntPtr.Zero)
            {
                cmdClass = this.ResolveVehicleBypassClass(
                    "XDT.Scene.Shared.Modules.Vehicle",
                    "CallVehicleCommand",
                    VehicleBypassEcsSystemImages,
                    "XDT.Scene.Shared.Modules.Vehicle.CallVehicleCommand");
            }

            IntPtr webUtilityClass = this.ResolveVehicleBypassClass(
                "XDTDataAndProtocol.ProtocolService",
                "WebRequestUtility",
                VehicleBypassLevelEntityImages,
                "XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            if (cmdClass != IntPtr.Zero && webUtilityClass != IntPtr.Zero)
            {
                this.vehicleBypassCallVehicleCmdClass = cmdClass;
                this.vehicleBypassCmdStaticIdField = this.FindAuraMonoFieldOnHierarchy(cmdClass, "StaticId");
                this.vehicleBypassCmdPositionField = this.FindAuraMonoFieldOnHierarchy(cmdClass, "Position");
                this.vehicleBypassCmdYAxisField = this.FindAuraMonoFieldOnHierarchy(cmdClass, "YAxis");
                this.vehicleBypassCmdLevelObjectIdField = this.FindAuraMonoFieldOnHierarchy(cmdClass, "LevelObjectId");
                this.vehicleBypassCmdGetOnField = this.FindAuraMonoFieldOnHierarchy(cmdClass, "IsAutoGetOnDriveSeat");
                IntPtr sendOpen = this.FindVehicleBypassMethod(webUtilityClass, "SendCommand", 3);
                if (sendOpen != IntPtr.Zero
                    && this.TryInstantCatchInflateAuraSendCommand(sendOpen, cmdClass, out IntPtr inflatedSend))
                {
                    this.vehicleBypassSendCommandMethod = inflatedSend;
                }
            }

            this.vehicleBypassSummonMethodsResolved = true;
            if (this.vehicleBypassGetLevelObjectIdMethod == IntPtr.Zero
                || (this.vehicleBypassPlayerCallVehicleMethod == IntPtr.Zero
                    && this.vehicleBypassSendCommandMethod == IntPtr.Zero))
            {
                this.vehicleBypassSummonMethodsResolveFailed = true;
                return false;
            }

            return true;
        }

        private unsafe bool TryVehicleBypassGetLevelObjectId(IntPtr managerObj, int itemId, out int levelObjectId)
        {
            levelObjectId = 0;
            if (managerObj == IntPtr.Zero || this.vehicleBypassGetLevelObjectIdMethod == IntPtr.Zero)
            {
                return false;
            }

            int seatIndex = 0;
            int outLevelObjectId = 0;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&itemId);
            args[1] = (IntPtr)(&seatIndex);
            args[2] = (IntPtr)(&outLevelObjectId);
            IntPtr resultObj = auraMonoRuntimeInvoke(
                this.vehicleBypassGetLevelObjectIdMethod,
                managerObj,
                (IntPtr)args,
                ref exc);
            if (exc != IntPtr.Zero)
            {
                return false;
            }

            levelObjectId = outLevelObjectId;
            if (resultObj != IntPtr.Zero && auraMonoObjectUnbox != null)
            {
                IntPtr raw = auraMonoObjectUnbox(resultObj);
                if (raw != IntPtr.Zero)
                {
                    return *(byte*)raw != 0;
                }
            }

            return levelObjectId != 0;
        }

        private unsafe bool TryVehicleBypassInvokePlayerCallVehicle(
            int staticId,
            Vector3 position,
            float yAxis,
            int levelObjectId,
            bool getOnVehicle,
            out string error)
        {
            error = string.Empty;
            if (this.vehicleBypassPlayerCallVehicleMethod == IntPtr.Zero)
            {
                error = "PlayerCallVehicle missing";
                return false;
            }

            int id = staticId;
            Vector3 pos = position;
            float axis = yAxis;
            int levelId = levelObjectId;
            bool getOn = getOnVehicle;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[5];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&pos);
            args[2] = (IntPtr)(&axis);
            args[3] = (IntPtr)(&levelId);
            args[4] = (IntPtr)(&getOn);
            auraMonoRuntimeInvoke(this.vehicleBypassPlayerCallVehicleMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "PlayerCallVehicle exception";
                return false;
            }

            return true;
        }

        private unsafe bool TryVehicleBypassSendCallVehicleCommand(
            int staticId,
            Vector3 position,
            float yAxis,
            int levelObjectId,
            bool getOnVehicle,
            out string error)
        {
            error = string.Empty;
            if (this.vehicleBypassSendCommandMethod == IntPtr.Zero
                || this.vehicleBypassCallVehicleCmdClass == IntPtr.Zero
                || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null
                || auraMonoObjectUnbox == null)
            {
                error = "SendCommand path unavailable";
                return false;
            }

            IntPtr cmdObj = auraMonoObjectNew(auraMonoRootDomain, this.vehicleBypassCallVehicleCmdClass);
            if (cmdObj == IntPtr.Zero)
            {
                error = "CallVehicleCommand alloc failed";
                return false;
            }

            int staticValue = staticId;
            Vector3 posValue = position;
            float axisValue = yAxis;
            int levelValue = levelObjectId;
            bool getOnValue = getOnVehicle;
            if (this.vehicleBypassCmdStaticIdField != IntPtr.Zero)
            {
                auraMonoFieldSetValue(cmdObj, this.vehicleBypassCmdStaticIdField, (IntPtr)(&staticValue));
            }

            if (this.vehicleBypassCmdPositionField != IntPtr.Zero)
            {
                auraMonoFieldSetValue(cmdObj, this.vehicleBypassCmdPositionField, (IntPtr)(&posValue));
            }

            if (this.vehicleBypassCmdYAxisField != IntPtr.Zero)
            {
                auraMonoFieldSetValue(cmdObj, this.vehicleBypassCmdYAxisField, (IntPtr)(&axisValue));
            }

            if (this.vehicleBypassCmdLevelObjectIdField != IntPtr.Zero)
            {
                auraMonoFieldSetValue(cmdObj, this.vehicleBypassCmdLevelObjectIdField, (IntPtr)(&levelValue));
            }

            if (this.vehicleBypassCmdGetOnField != IntPtr.Zero)
            {
                auraMonoFieldSetValue(cmdObj, this.vehicleBypassCmdGetOnField, (IntPtr)(&getOnValue));
            }

            IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
            if (cmdPtr == IntPtr.Zero)
            {
                error = "CallVehicleCommand unbox failed";
                return false;
            }

            int needAuthed = 1;
            int channel = 1;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = cmdPtr;
            args[1] = (IntPtr)(&needAuthed);
            args[2] = (IntPtr)(&channel);
            IntPtr resultBoxed = auraMonoRuntimeInvoke(
                this.vehicleBypassSendCommandMethod,
                IntPtr.Zero,
                (IntPtr)args,
                ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "SendCommand exception";
                return false;
            }

            if (resultBoxed != IntPtr.Zero && auraMonoObjectUnbox != null)
            {
                IntPtr raw = auraMonoObjectUnbox(resultBoxed);
                if (raw != IntPtr.Zero)
                {
                    int sendCode = *(int*)raw;
                    if (sendCode < 0)
                    {
                        error = "SendCommand failed (" + sendCode + ")";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
