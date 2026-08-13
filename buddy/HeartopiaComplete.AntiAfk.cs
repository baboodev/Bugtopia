using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const float AntiAfkHeartbeatIntervalCapSec = 9f;

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
