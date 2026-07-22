using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const float AntiAfkHeartbeatIntervalCapSec = 9f;

        private Type antiAfkOperateCommandType;
        private Type antiAfkServerPlayerStateType;
        private Type antiAfkPlayerProtocolManagerType;
        private MethodInfo antiAfkPlayerOperateHeartBeatMethod;
        private bool antiAfkHeartbeatUnavailableLogged;

        private void RunAntiAfkTick()
        {
            // "Menu open" = any MODAL registry surface (the UGUI shell) — showMenu is retired.
            if (!this.antiAfkEnabled || this.IsAnyModalInputSurfaceOpen())
            {
                return;
            }

            float pulseInterval = Mathf.Min(Mathf.Max(5f, this.antiAfkInterval), AntiAfkHeartbeatIntervalCapSec);
            if (Time.unscaledTime - this.lastAntiAfkPulseAt < pulseInterval)
            {
                return;
            }

            this.lastAntiAfkPulseAt = Time.unscaledTime;
            this.TrySendAntiAfkOperateHeartbeat();
        }

        private void TrySendAntiAfkOperateHeartbeat()
        {
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureAuraMonoApiReady();

            if (this.TrySendAntiAfkOperateHeartbeatViaCommand())
            {
                return;
            }

            if (this.TryInvokeAntiAfkPlayerOperateHeartBeatManaged())
            {
                return;
            }

            if (this.TryInvokeAntiAfkPlayerOperateHeartBeatAuraMono())
            {
                return;
            }

            if (!this.antiAfkHeartbeatUnavailableLogged)
            {
                this.antiAfkHeartbeatUnavailableLogged = true;
                ModLogger.Msg("[AntiAfk] PlayerOperateHeartBeat unavailable (enter a town and retry).");
            }
        }

        private bool TrySendAntiAfkOperateHeartbeatViaCommand()
        {
            if (this.antiAfkOperateCommandType == null)
            {
                this.antiAfkOperateCommandType = this.FindLoadedType(
                    "XDT.Scene.Shared.Modules.Player.PlayerOperateStateNetworkCommand",
                    "EcsClient.XDT.Scene.Shared.Modules.Player.PlayerOperateStateNetworkCommand",
                    "Il2CppXDT.Scene.Shared.Modules.Player.PlayerOperateStateNetworkCommand",
                    "PlayerOperateStateNetworkCommand");
            }

            if (this.antiAfkOperateCommandType == null)
            {
                return false;
            }

            object operatingState = this.ResolveAntiAfkOperatingPlayerState();
            if (operatingState == null)
            {
                return false;
            }

            return this.TryHomelandFarmSendCommand(
                this.antiAfkOperateCommandType,
                cmd => this.TrySetObjectMember(cmd, "State", operatingState),
                out _);
        }

        private object ResolveAntiAfkOperatingPlayerState()
        {
            if (this.antiAfkServerPlayerStateType == null)
            {
                this.antiAfkServerPlayerStateType = this.FindLoadedType(
                    "XDT.Scene.Shared.Modules.Player.ServerPlayerStateType",
                    "EcsClient.XDT.Scene.Shared.Modules.Player.ServerPlayerStateType",
                    "Il2CppXDT.Scene.Shared.Modules.Player.ServerPlayerStateType",
                    "ServerPlayerStateType");
            }

            if (this.antiAfkServerPlayerStateType == null || !this.antiAfkServerPlayerStateType.IsEnum)
            {
                return null;
            }

            try
            {
                return Enum.Parse(this.antiAfkServerPlayerStateType, "Operating");
            }
            catch
            {
                return null;
            }
        }

        private bool TryInvokeAntiAfkPlayerOperateHeartBeatManaged()
        {
            try
            {
                if (this.antiAfkPlayerProtocolManagerType == null)
                {
                    this.antiAfkPlayerProtocolManagerType = this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.Player.PlayerProtocolManager",
                        "PlayerProtocolManager");
                }

                if (this.antiAfkPlayerProtocolManagerType == null)
                {
                    return false;
                }

                if (this.antiAfkPlayerOperateHeartBeatMethod == null)
                {
                    this.antiAfkPlayerOperateHeartBeatMethod = this.antiAfkPlayerProtocolManagerType
                        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, "PlayerOperateHeartBeat", StringComparison.Ordinal)
                            && m.GetParameters().Length == 0);
                }

                if (this.antiAfkPlayerOperateHeartBeatMethod == null)
                {
                    return false;
                }

                this.antiAfkPlayerOperateHeartBeatMethod.Invoke(null, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryInvokeAntiAfkPlayerOperateHeartBeatAuraMono()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            const string fullTypeName = "XDTDataAndProtocol.ProtocolService.Player.PlayerProtocolManager";
            IntPtr protocolClass = this.FindAuraMonoClassByFullName(fullTypeName);
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTDataAndProtocol.ProtocolService.Player",
                    "PlayerProtocolManager");
            }

            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(protocolClass, "PlayerOperateHeartBeat", 0);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }
    }
}
