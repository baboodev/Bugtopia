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
    //
    // TWO INDEPENDENT TOGGLES with two independent latches: the congratulation family above, and
    // the Battle Pass reward modal (AlertBPPayRewardEvent), which is kept separate because it
    // doubles as a paid-pass upsell rather than being a pure "well done" card.
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

        // Наградная модалка боевого пропуска — SEPARATE TOGGLE, not part of the congratulation
        // family above: this one also carries the "buy the paid pass" upsell (payRewardBg /
        // toBuy@btn / payRewards@list next to the granted rewards), so hiding it is a different
        // decision from hiding a certification card.
        //
        // MailSyncSystem (WorldSystem.ShowBattlePassPayReward) -> ShowBpPayRewardEvent ->
        // DefaultModule.ShowBpPayRewardEventHandler (pure field copy) -> AlertBPPayRewardEvent ->
        // UIEventBridge.OnAlertBPPayReward -> AlertBPPayRewardPanel.Open. Hooked at the LOWER,
        // terminal channel for the same reason AlertRewardsEvent is preferred over
        // AlertRewardEvent: the upper one could grow a second listener that runs real logic.
        // Payload is a List<(RewardData,int)> reference plus an int whose offset depends on how
        // Mono lays the struct out — nothing is read.
        private const string AlertBPPayRewardEventName = "XDTGameSystem.UI.AlertBPPayRewardEvent";
        private const int AlertBPPayRewardEventPayloadBytes = 0;

        // The card a pet photo session ends on ("Tap to continue"), one photo at a time. SEPARATE
        // TOGGLE again: the six above are cards nobody asked for, this one is the OUTPUT of something
        // the player deliberately did, so hiding it is its own decision.
        //
        // PhotoModule._OpenPetPhotoResultPanel -> PetPhotoResultOpenRequestedEvent ->
        // UIEventBridge.OnPetPhotoResultOpenRequested -> UIManager.OpenView<PetPhotoResultPanel>.
        // UIEventBridge is the event's ONLY listener, and the panel itself sends nothing — the
        // photos are saved by PhotoModule.TakePetPhoto, which runs in the same method AFTER the
        // dispatch, so swallowing it costs no photo and no moment. Payload is all reference fields
        // (List<Texture2D>, the moment list); nothing is read.
        private const string PetPhotoResultOpenRequestedEventName =
            "XDTGameSystem.UI.PetPhotoResultOpenRequestedEvent";
        private const int PetPhotoResultOpenRequestedEventPayloadBytes = 0;

        // Карточки животных. Both are display-only, and each has exactly ONE listener in the whole
        // game (UIEventBridge) whose handler is a bare Panel.Open:
        //   AnimalModule.OnWildAnimalFavoriteLvChanged -> WildAnimalLevelUpEvent{AnimalGroup group}
        //     -> UIEventBridge.OnWildAnimalLevelUp -> AnimalLevelUpPanel.Open — the "Bond Level Up"
        //     card that stays up until it is tapped (the one the gift auto-claim keeps raising);
        //   AnimalModule.OnWildAnimalVisitEventChanged -> WildAnimalVisitNotifyEvent{group, state}
        //     -> UIEventBridge.OnWildAnimalVisit -> AnimalArrivedTipPanel.Open — "an animal came to
        //     visit". The bond level and the visit are server state that is already updated when the
        //     dispatch happens, so swallowing it removes the card and nothing else.
        // SEPARATE TOGGLE (own latch, own slots), like the two below.
        private const string WildAnimalLevelUpEventName = "XDTGameSystem.UI.WildAnimalLevelUpEvent";
        private const int WildAnimalLevelUpEventPayloadBytes = 4;     // AnimalGroup, int-backed enum
        private const string WildAnimalVisitNotifyEventName = "XDTGameSystem.UI.WildAnimalVisitNotifyEvent";
        private const int WildAnimalVisitNotifyEventPayloadBytes = 0; // group + bool: layout is Mono's to pick

        internal static bool MasterLogQuietPopups = false;

        private bool quietCongratsPopups;
        private bool quietPopupsHooksRegistered;
        private bool quietPopupsHookInstallLogged;

        private bool quietBpPayRewardPopup;
        private bool quietBpPayHookRegistered;

        private bool quietPetPhotoResultPopup;
        private bool quietPetPhotoHookRegistered;

        private bool quietAnimalCardPopups;
        private bool quietAnimalCardHooksRegistered;

        private void ProcessQuietPopupsOnUpdate()
        {
            this.ProcessQuietBpPayRewardOnUpdate();
            this.ProcessQuietPetPhotoResultOnUpdate();
            this.ProcessQuietAnimalCardsOnUpdate();

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

        // Own latch, own slot: the two toggles are independent, so enabling one must not register
        // the other's hooks.
        private void ProcessQuietBpPayRewardOnUpdate()
        {
            bool on = this.quietBpPayRewardPopup;
            if (!on && !this.quietBpPayHookRegistered)
            {
                return;
            }

            if (!this.quietBpPayHookRegistered)
            {
                this.quietBpPayHookRegistered = true;
                if (!this.RegisterGameEventHook(
                        AlertBPPayRewardEventName, AlertBPPayRewardEventPayloadBytes, this.OnAlertBPPayRewardEventHook))
                {
                    ModLogger.Warning("[QuietPopups] AlertBPPayRewardEvent hook refused"
                        + " — the Battle Pass reward popup will still show.");
                }
                else if (MasterLogQuietPopups)
                {
                    ModLogger.Msg("[QuietPopups] AlertBPPayRewardEvent hook registered");
                }
            }

            this.SetGameEventHookSuppressForward(AlertBPPayRewardEventName, on);
        }

        // Own latch, own slot — same reasoning as the Battle Pass toggle above.
        private void ProcessQuietPetPhotoResultOnUpdate()
        {
            bool on = this.quietPetPhotoResultPopup;
            if (!on && !this.quietPetPhotoHookRegistered)
            {
                return;
            }

            if (!this.quietPetPhotoHookRegistered)
            {
                this.quietPetPhotoHookRegistered = true;
                if (!this.RegisterGameEventHook(
                        PetPhotoResultOpenRequestedEventName,
                        PetPhotoResultOpenRequestedEventPayloadBytes,
                        this.OnPetPhotoResultOpenRequestedEventHook))
                {
                    ModLogger.Warning("[QuietPopups] PetPhotoResultOpenRequestedEvent hook refused"
                        + " — the pet photo card will still show.");
                }
                else if (MasterLogQuietPopups)
                {
                    ModLogger.Msg("[QuietPopups] PetPhotoResultOpenRequestedEvent hook registered");
                }
            }

            this.SetGameEventHookSuppressForward(PetPhotoResultOpenRequestedEventName, on);
        }

        // Own latch, own two slots — same reasoning as the two toggles above.
        private void ProcessQuietAnimalCardsOnUpdate()
        {
            bool on = this.quietAnimalCardPopups;
            if (!on && !this.quietAnimalCardHooksRegistered)
            {
                return;
            }

            if (!this.quietAnimalCardHooksRegistered)
            {
                this.quietAnimalCardHooksRegistered = true;
                bool levelOk = this.RegisterGameEventHook(
                    WildAnimalLevelUpEventName, WildAnimalLevelUpEventPayloadBytes, this.OnWildAnimalLevelUpEventHook);
                bool visitOk = this.RegisterGameEventHook(
                    WildAnimalVisitNotifyEventName, WildAnimalVisitNotifyEventPayloadBytes, this.OnWildAnimalVisitNotifyEventHook);
                if (!levelOk || !visitOk)
                {
                    ModLogger.Warning("[QuietPopups] animal card hooks refused — those cards will still show:"
                        + " bondLevelUp=" + levelOk + " animalVisit=" + visitOk);
                }
                else if (MasterLogQuietPopups)
                {
                    ModLogger.Msg("[QuietPopups] animal card hooks registered");
                }
            }

            this.SetGameEventHookSuppressForward(WildAnimalLevelUpEventName, on);
            this.SetGameEventHookSuppressForward(WildAnimalVisitNotifyEventName, on);
        }

        private void OnWildAnimalLevelUpEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] WildAnimalLevelUpEvent group=" + e.ReadInt32(0)
                + " suppress=" + this.quietAnimalCardPopups);
        }

        private void OnWildAnimalVisitNotifyEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] WildAnimalVisitNotifyEvent suppress=" + this.quietAnimalCardPopups);
        }

        private void OnPetPhotoResultOpenRequestedEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] PetPhotoResultOpenRequestedEvent suppress="
                + this.quietPetPhotoResultPopup);
        }

        private void OnAlertBPPayRewardEventHook(GameEventSnapshot e)
        {
            if (!MasterLogQuietPopups)
            {
                return;
            }

            ModLogger.Msg("[QuietPopups] AlertBPPayRewardEvent suppress=" + this.quietBpPayRewardPopup);
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
