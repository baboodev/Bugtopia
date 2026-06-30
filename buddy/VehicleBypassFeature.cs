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
        public bool vehicleBypassServerEventsEnabled;

        private const float VehicleBypassHookRetrySeconds = 3f;
        private float vehicleBypassNextHookAttemptAt = -999f;
        private bool vehicleBypassHooksHardFailed;
        private bool vehicleBypassClientReadyLogged;
        private bool vehicleBypassServerReadyLogged;

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

        private static readonly string[] VehicleBypassProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll",
        };

        private const int VehicleBypassAreaForbidGetOffReason = 1;
        private const int VehicleBypassGetOffReasonDefault = 0;
        private const int VehicleBypassAreaForbidReCallType = 5;
        private const int VehicleBypassAreaForbidErrorCode = 28;
        private const int VehicleBypassErrorCodeSuccess = 0;

        private static volatile bool vehicleBypassEnabledStatic;
        private static volatile bool vehicleBypassServerEventsEnabledStatic;
        private static volatile uint vehicleBypassSelfPlayerNetId;
        private static volatile uint vehicleBypassSelfVehicleNetId;
        private static volatile uint vehicleBypassLatchedVehicleNetId;
        private static volatile uint vehicleBypassPendingUserGetOffVehicleNetId;

        private static VehicleBypassCompileMethodDelegate vehicleBypassCompileMethod;

        private static VehicleBypassDetourSlot vehicleBypassHomeStaySlot;
        private static VehicleBypassDetourSlot vehicleBypassForbiddenEnterSlot;
        private static VehicleBypassDetourSlot vehicleBypassPosForbiddenSlot;
        private static VehicleBypassDetourSlot vehicleBypassHomePermissionSlot;
        private static VehicleBypassDetourSlot vehicleBypassPartyForbidSlot;
        private static VehicleBypassDetourSlot vehicleBypassInNoPermissionSlot;
        private static VehicleBypassDetourSlot vehicleBypassCreateDrivingSlot;
        private static VehicleBypassDetourSlot vehicleBypassGetOffVehicleSlot;
        private static VehicleBypassDetourSlot vehicleBypassGetOffResultSlot;
        private static VehicleBypassDetourSlot vehicleBypassReCallResultSlot;
        private static VehicleBypassDetourSlot vehicleBypassTransitionIsSatisfySlot;
        private static VehicleBypassDetourSlot vehicleBypassDoPassengerLeaveSlot;
        private static VehicleBypassDetourSlot vehicleBypassStatusField0Slot;
        private static VehicleBypassDetourSlot vehicleBypassStatusField1Slot;
        private static VehicleBypassDetourSlot vehicleBypassDriveExitSlot;
        private static VehicleBypassDetourSlot vehicleBypassServerRemoveVehicleSlot;
        private static VehicleBypassDetourSlot vehicleBypassRemovePlayerVehicleSlot;

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassGetOffVehicleHookDelegate(uint vehicleNetId, int reason);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassGetOffVehicleResultHookDelegate(int errorCode, uint playerNetId, uint vehicleNetId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassReCallVehicleResultHookDelegate(
            uint vehicleNetId,
            int vehicleStaticId,
            int reCallEventType,
            long recallUnixMs,
            int destroy);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassBoolSelf3HookDelegate(IntPtr self, IntPtr arg0, IntPtr arg1, IntPtr arg2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassVoidSelf1HookDelegate(IntPtr self, IntPtr arg0);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte VehicleBypassStatusFieldOnHandleDelegate(IntPtr command, IntPtr status);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassRemovePlayerVehicleHookDelegate(IntPtr self, IntPtr removeEvent);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VehicleBypassServerRemoveVehicleHookDelegate(uint netId, int realDel);

        private static VehicleBypassCreateDrivingHookDelegate vehicleBypassCreateDrivingHook;
        private static VehicleBypassCreateDrivingHookDelegate vehicleBypassCreateDrivingTrampoline;

        private static VehicleBypassGetOffVehicleHookDelegate vehicleBypassGetOffVehicleHook;
        private static VehicleBypassGetOffVehicleHookDelegate vehicleBypassGetOffVehicleTrampoline;
        private static VehicleBypassGetOffVehicleResultHookDelegate vehicleBypassGetOffVehicleResultHook;
        private static VehicleBypassGetOffVehicleResultHookDelegate vehicleBypassGetOffVehicleResultTrampoline;
        private static VehicleBypassReCallVehicleResultHookDelegate vehicleBypassReCallVehicleResultHook;
        private static VehicleBypassReCallVehicleResultHookDelegate vehicleBypassReCallVehicleResultTrampoline;
        private static VehicleBypassBoolSelf3HookDelegate vehicleBypassTransitionIsSatisfyHook;
        private static VehicleBypassBoolSelf3HookDelegate vehicleBypassTransitionIsSatisfyTrampoline;
        private static VehicleBypassVoidSelf1HookDelegate vehicleBypassDoPassengerLeaveHook;
        private static VehicleBypassVoidSelf1HookDelegate vehicleBypassDoPassengerLeaveTrampoline;
        private static VehicleBypassStatusFieldOnHandleDelegate vehicleBypassStatusField0Hook;
        private static VehicleBypassStatusFieldOnHandleDelegate vehicleBypassStatusField0Trampoline;
        private static VehicleBypassStatusFieldOnHandleDelegate vehicleBypassStatusField1Hook;
        private static VehicleBypassStatusFieldOnHandleDelegate vehicleBypassStatusField1Trampoline;
        private static VehicleBypassBoolSelfOnlyHookDelegate vehicleBypassDriveExitHook;
        private static VehicleBypassBoolSelfOnlyHookDelegate vehicleBypassDriveExitTrampoline;
        private static VehicleBypassServerRemoveVehicleHookDelegate vehicleBypassServerRemoveVehicleHook;
        private static VehicleBypassServerRemoveVehicleHookDelegate vehicleBypassServerRemoveVehicleTrampoline;
        private static VehicleBypassRemovePlayerVehicleHookDelegate vehicleBypassRemovePlayerVehicleHook;
        private static VehicleBypassRemovePlayerVehicleHookDelegate vehicleBypassRemovePlayerVehicleTrampoline;

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
            vehicleBypassEnabledStatic = this.vehicleBypassEnabled;
            vehicleBypassServerEventsEnabledStatic = this.vehicleBypassServerEventsEnabled;

            if (this.vehicleBypassHooksHardFailed)
            {
                return;
            }

            bool clientOn = this.vehicleBypassEnabled;
            bool serverOn = this.vehicleBypassServerEventsEnabled;
            if (!clientOn && !serverOn)
            {
                this.RemoveVehicleBypassDetours();
                this.vehicleBypassClientReadyLogged = false;
                this.vehicleBypassServerReadyLogged = false;
                return;
            }

            if (!clientOn)
            {
                this.RemoveVehicleBypassClientDetours();
            }

            if (!serverOn)
            {
                this.RemoveVehicleBypassServerDetours();
            }

            if (serverOn)
            {
                this.TryVehicleBypassRefreshDrivingContext();
            }

            bool needClientHooks = clientOn && !this.VehicleBypassClientHooksApplied();
            bool needServerHooks = serverOn && !this.VehicleBypassServerHooksApplied();
            if (needClientHooks || needServerHooks)
            {
                if (Time.unscaledTime >= this.vehicleBypassNextHookAttemptAt)
                {
                    this.vehicleBypassNextHookAttemptAt = Time.unscaledTime + VehicleBypassHookRetrySeconds;
                    this.EnsureVehicleBypassDetours(clientOn, serverOn);
                }
            }
            else
            {
                if (clientOn && !this.vehicleBypassClientReadyLogged)
                {
                    this.vehicleBypassClientReadyLogged = true;
                    ModLogger.Msg("[VehicleBypass] client detours applied (triggers + summon).");
                }

                if (serverOn && !this.vehicleBypassServerReadyLogged)
                {
                    this.vehicleBypassServerReadyLogged = true;
                    ModLogger.Msg("[VehicleBypass] server-event detours applied (protocol + sync/status).");
                }
            }

            if (clientOn)
            {
                this.TryVehicleBypassProcessPendingSummon();
            }
        }

        private bool VehicleBypassClientHooksApplied()
        {
            return VehicleBypassSlotApplied(vehicleBypassHomeStaySlot)
                && VehicleBypassSlotApplied(vehicleBypassForbiddenEnterSlot)
                && VehicleBypassSlotApplied(vehicleBypassPosForbiddenSlot)
                && VehicleBypassSlotApplied(vehicleBypassHomePermissionSlot)
                && VehicleBypassSlotApplied(vehicleBypassPartyForbidSlot)
                && VehicleBypassSlotApplied(vehicleBypassInNoPermissionSlot)
                && VehicleBypassSlotApplied(vehicleBypassCreateDrivingSlot);
        }

        private bool VehicleBypassServerHooksApplied()
        {
            return VehicleBypassSlotApplied(vehicleBypassGetOffVehicleSlot)
                && VehicleBypassSlotApplied(vehicleBypassGetOffResultSlot)
                && VehicleBypassSlotApplied(vehicleBypassReCallResultSlot)
                && VehicleBypassSlotApplied(vehicleBypassTransitionIsSatisfySlot)
                && VehicleBypassSlotApplied(vehicleBypassDoPassengerLeaveSlot)
                && VehicleBypassSlotApplied(vehicleBypassStatusField0Slot)
                && VehicleBypassSlotApplied(vehicleBypassStatusField1Slot)
                && VehicleBypassSlotApplied(vehicleBypassDriveExitSlot)
                && VehicleBypassSlotApplied(vehicleBypassServerRemoveVehicleSlot)
                && VehicleBypassSlotApplied(vehicleBypassRemovePlayerVehicleSlot);
        }

        private static bool VehicleBypassSlotApplied(VehicleBypassDetourSlot slot)
            => slot.Detour != null && slot.Detour.IsApplied;

        private void EnsureVehicleBypassDetours(bool installClient, bool installServer)
        {
            try
            {
                if (!installClient && !installServer)
                {
                    return;
                }

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

                if (installClient)
                {
                    this.EnsureVehicleBypassClientDetours();
                }

                if (installServer)
                {
                    this.EnsureVehicleBypassServerDetours();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[VehicleBypass] install pass failed: " + ex.Message);
            }
        }

        private void EnsureVehicleBypassClientDetours()
        {
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

        private void EnsureVehicleBypassServerDetours()
        {
                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassGetOffVehicleSlot,
                    ref vehicleBypassGetOffVehicleHook,
                    ref vehicleBypassGetOffVehicleTrampoline,
                    VehicleBypassGetOffVehicleNative,
                    "XDTDataAndProtocol.ProtocolService.Vehicle",
                    "VehicleProtocolManager",
                    VehicleBypassProtocolImages,
                    "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager",
                    "GetOffVehicle",
                    1,
                    2);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassGetOffResultSlot,
                    ref vehicleBypassGetOffVehicleResultHook,
                    ref vehicleBypassGetOffVehicleResultTrampoline,
                    VehicleBypassGetOffVehicleResultNative,
                    "XDTDataAndProtocol.ProtocolService.Vehicle",
                    "VehicleProtocolManager",
                    VehicleBypassProtocolImages,
                    "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager",
                    "GetOffVehicleResult",
                    3);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassReCallResultSlot,
                    ref vehicleBypassReCallVehicleResultHook,
                    ref vehicleBypassReCallVehicleResultTrampoline,
                    VehicleBypassReCallVehicleResultNative,
                    "XDTDataAndProtocol.ProtocolService.Vehicle",
                    "VehicleProtocolManager",
                    VehicleBypassProtocolImages,
                    "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager",
                    "ReCallVehicleResult",
                    5);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassTransitionIsSatisfySlot,
                    ref vehicleBypassTransitionIsSatisfyHook,
                    ref vehicleBypassTransitionIsSatisfyTrampoline,
                    VehicleBypassTransitionVehicle2FreeIsSatisfyNative,
                    "XDTLevelAndEntity.Gameplay.Component.Player",
                    "TransitionVehicle2Free",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.Gameplay.Component.Player.TransitionVehicle2Free",
                    "IsSatisfy",
                    3);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassDoPassengerLeaveSlot,
                    ref vehicleBypassDoPassengerLeaveHook,
                    ref vehicleBypassDoPassengerLeaveTrampoline,
                    VehicleBypassDoPassengerLeaveNative,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle",
                    "VehicleManager",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle.VehicleManager",
                    "DoPassengerLeave",
                    1);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassStatusField0Slot,
                    ref vehicleBypassStatusField0Hook,
                    ref vehicleBypassStatusField0Trampoline,
                    VehicleBypassVehicleStatusField0OnHandleNative,
                    "XDTLevelAndEntity.Gameplay.Component.Player",
                    "VehicleStatus_Field_0",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.Gameplay.Component.Player.VehicleStatus_Field_0",
                    "OnHandle",
                    2);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassStatusField1Slot,
                    ref vehicleBypassStatusField1Hook,
                    ref vehicleBypassStatusField1Trampoline,
                    VehicleBypassVehicleStatusField1OnHandleNative,
                    "XDTLevelAndEntity.Gameplay.Component.Player",
                    "VehicleStatus_Field_1",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.Gameplay.Component.Player.VehicleStatus_Field_1",
                    "OnHandle",
                    2);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassDriveExitSlot,
                    ref vehicleBypassDriveExitHook,
                    ref vehicleBypassDriveExitTrampoline,
                    VehicleBypassDriveModeCheckExitNative,
                    "XDTLevelAndEntity.Game.GameMode",
                    "GameDriveMode",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.Game.GameMode.GameDriveMode",
                    "CheckExitCondition",
                    0);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassServerRemoveVehicleSlot,
                    ref vehicleBypassServerRemoveVehicleHook,
                    ref vehicleBypassServerRemoveVehicleTrampoline,
                    VehicleBypassServerRemoveVehicleNative,
                    "XDTDataAndProtocol.ProtocolService.Vehicle",
                    "VehicleProtocolManager",
                    VehicleBypassProtocolImages,
                    "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager",
                    "ServerRemoveVehicle",
                    2);

                this.TryInstallVehicleBypassTrampolineDetour(
                    ref vehicleBypassRemovePlayerVehicleSlot,
                    ref vehicleBypassRemovePlayerVehicleHook,
                    ref vehicleBypassRemovePlayerVehicleTrampoline,
                    VehicleBypassRemovePlayerVehicleNative,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle",
                    "VehicleManager",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.GameplaySystem.Vehicle.VehicleManager",
                    "RemovePlayerVehicle",
                    1);
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
            this.RemoveVehicleBypassClientDetours();
            this.RemoveVehicleBypassServerDetours();
            vehicleBypassCreateDrivingTrampoline = null;
            vehicleBypassGetOffVehicleTrampoline = null;
            vehicleBypassGetOffVehicleResultTrampoline = null;
            vehicleBypassReCallVehicleResultTrampoline = null;
            vehicleBypassTransitionIsSatisfyTrampoline = null;
            vehicleBypassDoPassengerLeaveTrampoline = null;
            vehicleBypassStatusField0Trampoline = null;
            vehicleBypassStatusField1Trampoline = null;
            vehicleBypassDriveExitTrampoline = null;
            vehicleBypassServerRemoveVehicleTrampoline = null;
            vehicleBypassRemovePlayerVehicleTrampoline = null;
        }

        private void RemoveVehicleBypassClientDetours()
        {
            this.UndoVehicleBypassSlot(ref vehicleBypassHomeStaySlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassForbiddenEnterSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassPosForbiddenSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassHomePermissionSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassPartyForbidSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassInNoPermissionSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassCreateDrivingSlot);
            vehicleBypassPendingForceSummon = false;
            vehicleBypassPendingAttempts = 0;
        }

        private void RemoveVehicleBypassServerDetours()
        {
            this.UndoVehicleBypassSlot(ref vehicleBypassGetOffVehicleSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassGetOffResultSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassReCallResultSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassTransitionIsSatisfySlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassDoPassengerLeaveSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassStatusField0Slot);
            this.UndoVehicleBypassSlot(ref vehicleBypassStatusField1Slot);
            this.UndoVehicleBypassSlot(ref vehicleBypassDriveExitSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassServerRemoveVehicleSlot);
            this.UndoVehicleBypassSlot(ref vehicleBypassRemovePlayerVehicleSlot);
            vehicleBypassPendingUserGetOffVehicleNetId = 0;
            vehicleBypassSelfPlayerNetId = 0;
            vehicleBypassSelfVehicleNetId = 0;
            vehicleBypassLatchedVehicleNetId = 0;
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

        private void TryVehicleBypassRefreshDrivingContext()
        {
            vehicleBypassSelfPlayerNetId = 0;
            vehicleBypassSelfVehicleNetId = 0;
            if (!this.vehicleBypassServerEventsEnabled)
            {
                vehicleBypassLatchedVehicleNetId = 0;
                return;
            }

            if (this.TryGetSelfPlayerNetId(out uint playerNetId) && playerNetId != 0)
            {
                vehicleBypassSelfPlayerNetId = playerNetId;
            }

            IntPtr vehicleComponentObj = this.TryGetSelfEntityVehicleComponentMono();
            if (vehicleComponentObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(vehicleComponentObj, "entity", out IntPtr entityObj)
                && entityObj != IntPtr.Zero
                && this.TryGetMonoUInt32Member(entityObj, "netId", out uint vehicleNetId)
                && vehicleNetId != 0)
            {
                vehicleBypassSelfVehicleNetId = vehicleNetId;
                vehicleBypassLatchedVehicleNetId = vehicleNetId;
            }
            else if (this.TryVehicleBypassReadLocalVehicleStatusNetId(out uint statusVehicleNetId) && statusVehicleNetId != 0)
            {
                vehicleBypassSelfVehicleNetId = statusVehicleNetId;
                vehicleBypassLatchedVehicleNetId = statusVehicleNetId;
            }
        }

        private bool TryVehicleBypassReadLocalVehicleStatusNetId(out uint vehicleNetId)
        {
            vehicleNetId = 0;
            try
            {
                if (!this.TryGetManagedViewModuleSelfPlayerObject(out object selfPlayer, out _)
                    || !this.TryGetObjectMember(selfPlayer, "Status", out object statusObj)
                    || !this.TryGetObjectMember(statusObj, "VehicleStatus", out object vehicleStatusObj)
                    || !this.TryGetObjectMember(vehicleStatusObj, "VehicleNetId", out object netIdObj))
                {
                    return false;
                }

                vehicleNetId = netIdObj is uint u ? u : Convert.ToUInt32(netIdObj);
                return vehicleNetId != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool VehicleBypassBlockForcedVehicleExit()
        {
            return vehicleBypassServerEventsEnabledStatic
                && vehicleBypassPendingUserGetOffVehicleNetId == 0
                && vehicleBypassLatchedVehicleNetId != 0;
        }

        private static bool VehicleBypassShouldBlockGetOffResult(int errorCode, uint playerNetId, uint vehicleNetId)
        {
            if (!vehicleBypassServerEventsEnabledStatic)
            {
                return false;
            }

            if (errorCode == VehicleBypassAreaForbidErrorCode)
            {
                return true;
            }

            if (errorCode != VehicleBypassErrorCodeSuccess)
            {
                return false;
            }

            if (vehicleBypassSelfPlayerNetId == 0 || playerNetId != vehicleBypassSelfPlayerNetId)
            {
                return false;
            }

            if (vehicleBypassPendingUserGetOffVehicleNetId != 0
                && vehicleNetId == vehicleBypassPendingUserGetOffVehicleNetId)
            {
                vehicleBypassPendingUserGetOffVehicleNetId = 0;
                vehicleBypassLatchedVehicleNetId = 0;
                return false;
            }

            return true;
        }

        private static void VehicleBypassGetOffVehicleNative(uint vehicleNetId, int reason)
        {
            if (vehicleBypassEnabledStatic && reason == VehicleBypassAreaForbidGetOffReason)
            {
                return;
            }

            if (vehicleBypassServerEventsEnabledStatic && reason == VehicleBypassGetOffReasonDefault && vehicleNetId != 0)
            {
                vehicleBypassPendingUserGetOffVehicleNetId = vehicleNetId;
            }

            if (vehicleBypassGetOffVehicleTrampoline != null)
            {
                vehicleBypassGetOffVehicleTrampoline(vehicleNetId, reason);
            }
        }

        private static void VehicleBypassGetOffVehicleResultNative(int errorCode, uint playerNetId, uint vehicleNetId)
        {
            if (VehicleBypassShouldBlockGetOffResult(errorCode, playerNetId, vehicleNetId))
            {
                return;
            }

            if (vehicleBypassGetOffVehicleResultTrampoline != null)
            {
                vehicleBypassGetOffVehicleResultTrampoline(errorCode, playerNetId, vehicleNetId);
            }
        }

        private static void VehicleBypassReCallVehicleResultNative(
            uint vehicleNetId,
            int vehicleStaticId,
            int reCallEventType,
            long recallUnixMs,
            int destroy)
        {
            if (vehicleBypassServerEventsEnabledStatic && reCallEventType == VehicleBypassAreaForbidReCallType)
            {
                return;
            }

            if (vehicleBypassReCallVehicleResultTrampoline != null)
            {
                vehicleBypassReCallVehicleResultTrampoline(
                    vehicleNetId,
                    vehicleStaticId,
                    reCallEventType,
                    recallUnixMs,
                    destroy);
            }
        }

        private static byte VehicleBypassTransitionVehicle2FreeIsSatisfyNative(
            IntPtr self,
            IntPtr actor,
            IntPtr current,
            IntPtr next)
        {
            if (VehicleBypassBlockForcedVehicleExit())
            {
                return 0;
            }

            if (vehicleBypassTransitionIsSatisfyTrampoline != null)
            {
                return vehicleBypassTransitionIsSatisfyTrampoline(self, actor, current, next);
            }

            return 0;
        }

        private static byte VehicleBypassVehicleStatusField0OnHandleNative(IntPtr command, IntPtr status)
        {
            if (VehicleBypassBlockForcedVehicleExit())
            {
                return 0;
            }

            if (vehicleBypassStatusField0Trampoline != null)
            {
                return vehicleBypassStatusField0Trampoline(command, status);
            }

            return 0;
        }

        private static byte VehicleBypassVehicleStatusField1OnHandleNative(IntPtr command, IntPtr status)
        {
            if (VehicleBypassBlockForcedVehicleExit())
            {
                return 0;
            }

            if (vehicleBypassStatusField1Trampoline != null)
            {
                return vehicleBypassStatusField1Trampoline(command, status);
            }

            return 0;
        }

        private static byte VehicleBypassDriveModeCheckExitNative(IntPtr self)
        {
            if (VehicleBypassBlockForcedVehicleExit())
            {
                return 0;
            }

            if (vehicleBypassDriveExitTrampoline != null)
            {
                return vehicleBypassDriveExitTrampoline(self);
            }

            return 0;
        }

        private static void VehicleBypassServerRemoveVehicleNative(uint netId, int realDel)
        {
            if (VehicleBypassBlockForcedVehicleExit()
                && netId != 0
                && netId == vehicleBypassLatchedVehicleNetId)
            {
                return;
            }

            if (vehicleBypassServerRemoveVehicleTrampoline != null)
            {
                vehicleBypassServerRemoveVehicleTrampoline(netId, realDel);
            }
        }

        private static void VehicleBypassRemovePlayerVehicleNative(IntPtr self, IntPtr removeEvent)
        {
            if (VehicleBypassBlockForcedVehicleExit() && removeEvent != IntPtr.Zero)
            {
                uint vehicleNetId = (uint)Marshal.ReadInt32(removeEvent);
                if (vehicleNetId != 0 && vehicleNetId == vehicleBypassLatchedVehicleNetId)
                {
                    return;
                }
            }

            if (vehicleBypassRemovePlayerVehicleTrampoline != null)
            {
                vehicleBypassRemovePlayerVehicleTrampoline(self, removeEvent);
            }
        }

        private static void VehicleBypassDoPassengerLeaveNative(IntPtr self, IntPtr player)
        {
            if (VehicleBypassBlockForcedVehicleExit())
            {
                return;
            }

            if (vehicleBypassDoPassengerLeaveTrampoline != null)
            {
                vehicleBypassDoPassengerLeaveTrampoline(self, player);
            }
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
