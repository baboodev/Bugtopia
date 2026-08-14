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
        private const int FlauntActionEventPayloadBytes = 64;

        // The "Obtained" popup that follows a pickup: AlertRewardPanel, a full-screen mask over a
        // reward grid ("Tap empty area to close."). Its only trigger is this event, and
        // UIEventBridge.OnAlertRewards — the ONLY listener — does nothing but
        // TipDecorator.AlertRewards -> AlertRewardPanel.Open. The rewards themselves were already
        // granted server-side by the time it fires (OpenGiftAction / PickupOpenGiftAction /
        // GiftModule / MailSyncSystem all dispatch AlertRewardEvent after the fact), so swallowing
        // the dispatch removes the panel and nothing else.
        //
        // Deliberately NOT hooked one level up at AlertRewardEvent: PetModule (pet-time reward
        // cache) and the two Monopoly panels listen there and drive real logic off it.
        //
        // Gacha keeps its popups — SanrioGachaDirectBuyPanel, GachaDirectBuyPieceWidget,
        // GachaPoolNoBackDisplayWidget and ToyCapsuleActivityPanel call AlertRewardPanel.Open
        // directly, bypassing the event.
        private const string AlertRewardsEventName = "XDTGameSystem.UI.AlertRewardsEvent";
        private const int AlertRewardsEventPayloadBytes = 0; // one List<> reference — nothing to read

        internal static bool MasterLogShowOffBypass = false;

        private bool skipShowOffAnimations;
        private bool showOffBypassHooksRegistered;
        private bool showOffBypassFlauntHookInstallLogged;
        private bool showOffBypassFlauntNetIdHookInstallLogged;
        private bool showOffBypassAlertRewardsHookInstallLogged;

        private void ProcessShowOffBypassOnUpdate()
        {
            this.EnsureShowOffBypassEventHooks();
            this.SetGameEventHookSuppressForward(FlauntActionEventName, this.skipShowOffAnimations);
            this.SetGameEventHookSuppressForward(FlauntActionWithNetIdEventName, this.skipShowOffAnimations);
            this.SetGameEventHookSuppressForward(AlertRewardsEventName, this.skipShowOffAnimations);
            this.LogShowOffBypassHookInstallState();
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
                ModLogger.Msg("[ShowOffBypass] registering hooks: " + FlauntActionEventName + ", " + FlauntActionWithNetIdEventName
                    + ", " + AlertRewardsEventName);
            }

            bool flauntOk = this.RegisterGameEventHook(FlauntActionEventName, FlauntActionEventPayloadBytes, this.OnFlauntActionEventHook);
            bool flauntNetIdOk = this.RegisterGameEventHook(FlauntActionWithNetIdEventName, FlauntActionEventPayloadBytes, this.OnFlauntActionWithNetIdEventHook);
            bool alertRewardsOk = this.RegisterGameEventHook(AlertRewardsEventName, AlertRewardsEventPayloadBytes, this.OnAlertRewardsEventHook);
            if (MasterLogShowOffBypass)
            {
                ModLogger.Msg("[ShowOffBypass] register result FlauntAction=" + flauntOk
                    + " FlauntActionWithNetId=" + flauntNetIdOk
                    + " AlertRewards=" + alertRewardsOk);
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

            if (!this.showOffBypassAlertRewardsHookInstallLogged && this.IsGameEventHookInstalled(AlertRewardsEventName))
            {
                this.showOffBypassAlertRewardsHookInstallLogged = true;
                ModLogger.Msg("[ShowOffBypass] hook installed: " + AlertRewardsEventName + " suppress=" + this.skipShowOffAnimations);
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

        // Payload is a single List<(RewardData, int)> reference — never dereferenced here, so the
        // snapshot carries nothing and this stays a pure trace of "the Obtained popup was asked for".
        private void OnAlertRewardsEventHook(GameEventSnapshot e)
        {
            if (!MasterLogShowOffBypass)
            {
                return;
            }

            ModLogger.Msg("[ShowOffBypass] AlertRewardsEvent suppress=" + this.skipShowOffAnimations);
        }
    }
}
