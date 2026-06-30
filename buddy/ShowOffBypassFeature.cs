using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const string FlauntActionEventName = "ScriptsRefactory.DataAndProtocol.Events.FlauntActionEvent";
        private const string FlauntActionWithNetIdEventName = "ScriptsRefactory.DataAndProtocol.Events.FlauntActionWithNetIdEvent";
        private const string NextFlauntActionEventName = "ScriptsRefactory.DataAndProtocol.Events.NextFlauntActionEvent";
        private const int FlauntActionEventPayloadBytes = 64;
        private const int PlayerStateShowOff = 27;

        internal const bool MasterLogShowOffBypass = true;

        private bool skipShowOffAnimations;
        private bool showOffBypassHooksRegistered;
        private bool showOffBypassFlauntHookInstallLogged;
        private bool showOffBypassFlauntNetIdHookInstallLogged;
        private bool showOffPollSkipLoggedOnce;
        private float showOffPollSkipAt;
        private float showOffNextFlauntResolveRetryAt;
        private IntPtr showOffNextFlauntDispatchMethod = IntPtr.Zero;
        private MethodInfo showOffIsStateMethod;

        private void ProcessShowOffBypassOnUpdate()
        {
            this.EnsureShowOffBypassEventHooks();
            this.SetGameEventHookSuppressForward(FlauntActionEventName, this.skipShowOffAnimations);
            this.SetGameEventHookSuppressForward(FlauntActionWithNetIdEventName, this.skipShowOffAnimations);
            this.LogShowOffBypassHookInstallState();

            if (this.skipShowOffAnimations)
            {
                this.TryShowOffPollSkipActiveAnimation();
            }
        }

        private void EnsureShowOffBypassEventHooks()
        {
            if (this.showOffBypassHooksRegistered)
            {
                return;
            }

            this.showOffBypassHooksRegistered = true;
            if (MasterLogShowOffBypass)
            {
                ModLogger.Msg("[ShowOffBypass] registering hooks: " + FlauntActionEventName + ", " + FlauntActionWithNetIdEventName);
            }

            bool flauntOk = this.RegisterGameEventHook(FlauntActionEventName, FlauntActionEventPayloadBytes, this.OnFlauntActionEventHook);
            bool flauntNetIdOk = this.RegisterGameEventHook(FlauntActionWithNetIdEventName, FlauntActionEventPayloadBytes, this.OnFlauntActionWithNetIdEventHook);
            if (MasterLogShowOffBypass)
            {
                ModLogger.Msg("[ShowOffBypass] register result FlauntAction=" + flauntOk + " FlauntActionWithNetId=" + flauntNetIdOk);
            }
        }

        private void LogShowOffBypassHookInstallState()
        {
            if (!MasterLogShowOffBypass)
            {
                return;
            }

            if (!this.showOffBypassFlauntHookInstallLogged && this.IsGameEventHookInstalled(FlauntActionEventName))
            {
                this.showOffBypassFlauntHookInstallLogged = true;
                ModLogger.Msg("[ShowOffBypass] hook installed: " + FlauntActionEventName + " suppress=" + this.skipShowOffAnimations);
            }

            if (!this.showOffBypassFlauntNetIdHookInstallLogged && this.IsGameEventHookInstalled(FlauntActionWithNetIdEventName))
            {
                this.showOffBypassFlauntNetIdHookInstallLogged = true;
                ModLogger.Msg("[ShowOffBypass] hook installed: " + FlauntActionWithNetIdEventName + " suppress=" + this.skipShowOffAnimations);
            }
        }

        private void OnFlauntActionEventHook(GameEventSnapshot e)
        {
            if (!MasterLogShowOffBypass)
            {
                return;
            }

            int staticId = e.ReadInt32(0);
            ModLogger.Msg("[ShowOffBypass] FlauntActionEvent staticId=" + staticId
                + " suppress=" + this.skipShowOffAnimations
                + " len=" + e.Length);
        }

        private void OnFlauntActionWithNetIdEventHook(GameEventSnapshot e)
        {
            if (!MasterLogShowOffBypass)
            {
                return;
            }

            uint netId = e.ReadUInt32(4);
            int staticId = e.ReadInt32(8);
            ModLogger.Msg("[ShowOffBypass] FlauntActionWithNetIdEvent netId=" + netId
                + " staticId=" + staticId
                + " suppress=" + this.skipShowOffAnimations
                + " len=" + e.Length);
        }

        private void TryShowOffPollSkipActiveAnimation()
        {
            if (Time.unscaledTime < this.showOffPollSkipAt)
            {
                return;
            }

            this.showOffPollSkipAt = Time.unscaledTime + 0.05f;
            if (!this.TryGetManagedSelfPlayerObject(out object playerObj, out _) || playerObj == null)
            {
                return;
            }

            if (this.showOffIsStateMethod == null || this.showOffIsStateMethod.DeclaringType != playerObj.GetType())
            {
                this.showOffIsStateMethod = playerObj.GetType().GetMethod("IsState", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(int) }, null)
                    ?? playerObj.GetType().GetMethod("IsState", BindingFlags.Public | BindingFlags.Instance);
            }

            if (this.showOffIsStateMethod == null)
            {
                return;
            }

            object stateArg = this.showOffIsStateMethod.GetParameters().Length == 1
                && this.showOffIsStateMethod.GetParameters()[0].ParameterType.IsEnum
                ? Enum.ToObject(this.showOffIsStateMethod.GetParameters()[0].ParameterType, PlayerStateShowOff)
                : PlayerStateShowOff;

            object inShowOffObj = this.showOffIsStateMethod.Invoke(playerObj, new object[] { stateArg });
            if (!(inShowOffObj is bool inShowOff) || !inShowOff)
            {
                return;
            }

            if (!this.TryAuraDispatchNextFlauntActionEvent())
            {
                return;
            }

            if (MasterLogShowOffBypass && !this.showOffPollSkipLoggedOnce)
            {
                this.showOffPollSkipLoggedOnce = true;
                ModLogger.Msg("[ShowOffBypass] poll skip: dispatched NextFlauntActionEvent while PlayerState.ShowOff");
            }
        }

        private bool TryAuraDispatchNextFlauntActionEvent()
        {
            try
            {
                if (this.showOffNextFlauntDispatchMethod == IntPtr.Zero)
                {
                    if (Time.unscaledTime < this.showOffNextFlauntResolveRetryAt)
                    {
                        return false;
                    }

                    this.showOffNextFlauntResolveRetryAt = Time.unscaledTime + 2f;
                    if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                        || auraMonoClassGetType == null || auraMonoMetadataGetGenericInst == null
                        || auraMonoClassInflateGenericMethod == null || auraMonoRuntimeInvoke == null)
                    {
                        return false;
                    }

                    IntPtr eventCenterClass = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
                    if (eventCenterClass == IntPtr.Zero)
                    {
                        eventCenterClass = this.FindAuraMonoClassInImages("XDTGame.Core", "EventCenter",
                            new string[] { "XDTBaseService", "XDTBaseService.dll" });
                    }

                    IntPtr openDispatch = eventCenterClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(eventCenterClass, "DispatchEvent", 1)
                        : IntPtr.Zero;
                    IntPtr nextFlauntClass = this.ResolveGameEventClass(NextFlauntActionEventName);
                    if (openDispatch == IntPtr.Zero || nextFlauntClass == IntPtr.Zero)
                    {
                        if (MasterLogShowOffBypass)
                        {
                            ModLogger.Msg("[ShowOffBypass] NextFlaunt resolve failed dispatch=" + (openDispatch != IntPtr.Zero)
                                + " eventClass=" + (nextFlauntClass != IntPtr.Zero));
                        }

                        return false;
                    }

                    if (!this.TryInflateDispatchForEvent(openDispatch, nextFlauntClass, 1, out IntPtr inflated))
                    {
                        if (MasterLogShowOffBypass)
                        {
                            ModLogger.Msg("[ShowOffBypass] NextFlaunt inflate failed");
                        }

                        return false;
                    }

                    this.showOffNextFlauntDispatchMethod = inflated;
                    if (MasterLogShowOffBypass)
                    {
                        ModLogger.Msg("[ShowOffBypass] NextFlauntActionEvent dispatch resolved");
                    }
                }

                IntPtr exc = IntPtr.Zero;
                int payload = 0;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&payload);
                    auraMonoRuntimeInvoke(this.showOffNextFlauntDispatchMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                }

                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
