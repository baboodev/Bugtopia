namespace HeartopiaMod
{
    // Quiet Congratulation Popups — swallows the family of full-screen "well done" panels the game
    // throws up after a collection milestone: the certification card ("Orchid Murex - Master
    // Seashell Collector", Tap any area to close), the collector rank-up card, the achievement
    // card, the hobby level-up card, the pictorial "new entry" tip and the battle-pass collect tip.
    //
    // WHY THIS IS SAFE, per channel: every event below has exactly ONE listener in the whole game —
    // UIEventBridge — and each of those handlers does nothing but `SomePanel.Open(...)`. The
    // rewards are already granted server-side before the dispatch (MailSyncSystem raises them off a
    // RewardNotice, i.e. a notification of something that already happened), so swallowing the
    // dispatch removes the panel and nothing else. Every panel on the receiving end is
    // display-only: none of them send a protocol, mark anything read, or write PlayerPrefs.
    // Verified against the decompiled UIEventBridge/panels on 2026-09-04.
    //
    // Same mechanism as Skip Show Off's AlertRewardsEvent (see ShowOffBypassFeature): a
    // suppress-forward hook on the EventCenter dispatch detour. No .text patches, nothing the
    // anti-cheat surface can see.
    //
    // HOOKS ARE REGISTERED LAZILY — only on the first time the toggle goes on. Slots are never
    // released and the pool is shared with every other feature (see MaxEventHookSlots), so six
    // permanently-burnt slots for a popup filter nobody enabled is not a trade worth making.
    public partial class HeartopiaComplete
    {
        // Справочник -> сертификация коллекционера. MailSyncSystem (RewardNotice, reason
        // WorldSystem.CollectCertification) -> UIEventBridge.OnPictorialProgressReward ->
        // PictorialProgressRewardPanel.Open. Payload is a single inline RewardData struct whose
        // fields are strings and ints mixed — Mono is free to reorder it, so nothing is read.
        private const string PictorialProgressRewardEventName = "XDTDataAndProtocol.Events.PictorialProgressRewardEvent";
        private const int PictorialProgressRewardEventPayloadBytes = 0;

        // Ранг коллекционера. PictorialSyncSystem -> UIEventBridge.OnCertificationLevelUp ->
        // PictorialCollectLevelUpTipPanel.Open. Three ints, safe to read for the trace.
        private const string CertificationLevelUpEventName = "XDTDataAndProtocol.Events.CertificationLevelUpEvent";
        private const int CertificationLevelUpEventPayloadBytes = 12;

        // Достижения. AchievementModule -> UIEventBridge.OnAchievementUnlocked ->
        // AchievementTipPanel.Open. Payload is one List<int> reference.
        private const string AchievementUnlockedNotifyEventName = "XDTGameSystem.UI.AchievementUnlockedNotifyEvent";
        private const int AchievementUnlockedNotifyEventPayloadBytes = 0;

        // Уровень хобби. HobbyModule -> UIEventBridge.OnHobbyUnlocked -> HobbyUnlockPanel.Open.
        // HobbyId is an int-backed enum, then the level.
        private const string HobbyUnlockedEventName = "XDTGameSystem.UI.HobbyUnlockedEvent";
        private const int HobbyUnlockedEventPayloadBytes = 8;

        // "Новая запись в справочнике". DefaultModule -> UIEventBridge.OnPictorialTipShowRequested
        // -> PictorialTipPanel.Open. List + tuple + Dictionary, all references.
        private const string PictorialTipShowRequestedEventName = "XDTGame.UI.PictorialTipShowRequestedEvent";
        private const int PictorialTipShowRequestedEventPayloadBytes = 0;

        // BattlePass collect tip -> BattlePassCollectTipPanel.Open. Two ints.
        private const string BattlePassCollectTipEventName = "XDTGameSystem.UI.BattlePassCollectTipEvent";
        private const int BattlePassCollectTipEventPayloadBytes = 8;

        internal static bool MasterLogQuietPopups = false;

        private bool quietCongratsPopups;
        private bool quietPopupsHooksRegistered;
        private bool quietPopupsHookInstallLogged;

        private void ProcessQuietPopupsOnUpdate()
        {
            bool on = this.quietCongratsPopups;
            if (!on && !this.quietPopupsHooksRegistered)
            {
                // Never enabled this session: do not burn six shared hook slots on it.
                return;
            }

            this.EnsureQuietPopupsEventHooks();

            this.SetGameEventHookSuppressForward(PictorialProgressRewardEventName, on);
            this.SetGameEventHookSuppressForward(CertificationLevelUpEventName, on);
            this.SetGameEventHookSuppressForward(AchievementUnlockedNotifyEventName, on);
            this.SetGameEventHookSuppressForward(HobbyUnlockedEventName, on);
            this.SetGameEventHookSuppressForward(PictorialTipShowRequestedEventName, on);
            this.SetGameEventHookSuppressForward(BattlePassCollectTipEventName, on);

            this.LogQuietPopupsHookInstallState();
        }

        private void EnsureQuietPopupsEventHooks()
        {
            if (this.quietPopupsHooksRegistered)
            {
                return;
            }

            this.quietPopupsHooksRegistered = true;

            bool certOk = this.RegisterGameEventHook(
                PictorialProgressRewardEventName, PictorialProgressRewardEventPayloadBytes, this.OnPictorialProgressRewardEventHook);
            bool levelOk = this.RegisterGameEventHook(
                CertificationLevelUpEventName, CertificationLevelUpEventPayloadBytes, this.OnCertificationLevelUpEventHook);
            bool achieveOk = this.RegisterGameEventHook(
                AchievementUnlockedNotifyEventName, AchievementUnlockedNotifyEventPayloadBytes, this.OnAchievementUnlockedEventHook);
            bool hobbyOk = this.RegisterGameEventHook(
                HobbyUnlockedEventName, HobbyUnlockedEventPayloadBytes, this.OnHobbyUnlockedEventHook);
            bool pictorialTipOk = this.RegisterGameEventHook(
                PictorialTipShowRequestedEventName, PictorialTipShowRequestedEventPayloadBytes, this.OnPictorialTipShowRequestedEventHook);
            bool bpOk = this.RegisterGameEventHook(
                BattlePassCollectTipEventName, BattlePassCollectTipEventPayloadBytes, this.OnBattlePassCollectTipEventHook);

            // Tier 1: a refused hook means that popup keeps showing with the toggle on, which the
            // user would otherwise read as "the feature is broken". Say which one, always.
            if (!certOk || !levelOk || !achieveOk || !hobbyOk || !pictorialTipOk || !bpOk)
            {
                ModLogger.Warning("[QuietPopups] some hooks were refused — those popups will still show:"
                    + " certification=" + certOk
                    + " collectorLevel=" + levelOk
                    + " achievement=" + achieveOk
                    + " hobby=" + hobbyOk
                    + " pictorialTip=" + pictorialTipOk
                    + " battlePass=" + bpOk);
            }
            else if (MasterLogQuietPopups)
            {
                ModLogger.Msg("[QuietPopups] all six hooks registered");
            }
        }

        private void LogQuietPopupsHookInstallState()
        {
            if (!MasterLogQuietPopups || this.quietPopupsHookInstallLogged)
            {
                return;
            }

            if (!this.IsGameEventHookInstalled(PictorialProgressRewardEventName))
            {
                return;
            }

            this.quietPopupsHookInstallLogged = true;
            ModLogger.Msg("[QuietPopups] hooks installed, suppress=" + this.quietCongratsPopups);
        }

        // The payload is one inline RewardData — never dereferenced (see the const's comment), so
        // this stays a pure trace of "the certification card was asked for".
        private void OnPictorialProgressRewardEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] PictorialProgressRewardEvent suppress=" + this.quietCongratsPopups);
        }

        private void OnCertificationLevelUpEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] CertificationLevelUpEvent staticId=" + e.ReadInt32(0)
                + " " + e.ReadInt32(4) + "->" + e.ReadInt32(8)
                + " suppress=" + this.quietCongratsPopups);
        }

        private void OnAchievementUnlockedEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] AchievementUnlockedNotifyEvent suppress=" + this.quietCongratsPopups);
        }

        private void OnHobbyUnlockedEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] HobbyUnlockedEvent hobbyId=" + e.ReadInt32(0)
                + " level=" + e.ReadInt32(4)
                + " suppress=" + this.quietCongratsPopups);
        }

        private void OnPictorialTipShowRequestedEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] PictorialTipShowRequestedEvent suppress=" + this.quietCongratsPopups);
        }

        private void OnBattlePassCollectTipEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] BattlePassCollectTipEvent activityId=" + e.ReadInt32(0)
                + " taskId=" + e.ReadInt32(4)
                + " suppress=" + this.quietCongratsPopups);
        }
    }
}
