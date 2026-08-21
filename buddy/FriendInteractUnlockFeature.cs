using System;

namespace HeartopiaMod
{
    // ============================================================================================
    // FRIEND INTERACT UNLOCK — list the two-person interactions their date window is hiding.
    //
    // NOT the same lever as Emote Unlock. Single actions and expressions come out of an owned list
    // the mod can fabricate (ExpressionActionClientService.ExpressionActionComponent). Two-person
    // interactions never consult that list in this build: FriendInteractPanel walks
    // TableData.TableFriendInteracts and decides everything from fields on each table row.
    //
    //   InitInteractData — is the row listed at all?
    //     gainCondition = Default(0)   -> always
    //                     Activity(1)  -> only inside appearDate's window          <- the one gate
    //                                                                                 this feature
    //                                                                                 opens
    //                     Reward(2)    -> only if owned  (ZERO rows in this build, which is why the
    //                                    owned-list route is a dead end here)
    //     unlockType    = 0            -> goes into the friendship-level bucket
    //                     otherwise    -> needs ActivityFriendShipPoint[activityEnum] >= friendshipLv,
    //                                     and a MISSING dictionary entry drops the row entirely
    //
    //   RenderItems — is the row clickable?
    //     available = (bucketLevel <= _friendComponent.FriendShipLevel)             <- NOT touched
    //
    // These are plain FIELDS, so there is nothing to detour — but a field can be written. An
    // event-locked row gets gainCondition = 0 (list it) and unlockType = 0 (route it through the
    // level bucket, the only path that lists anything at all). Nothing else is written.
    //
    // ⚠️ friendshipLv IS LEFT ALONE. Flattening it to 0 made every interaction in the game
    // clickable and the server answered exactly as it should — FriendShipLvNotEnough (601), thirty
    // times over, silently, because the client has no toast text for that code. Levels are real
    // progression enforced server-side; faking them locally buys a longer list and nothing else.
    //
    // ⚠️ isDisable IS LEFT ALONE for the same reason in the other direction: a disabled row is one
    // the developers switched off.
    //
    // WHAT THIS BUYS, measured live: three event-locked poses (45, 46, 47) were listed by this and
    // the server accepted every one — it does not re-check the date window. Item transfers and
    // seasonal activities are excluded by the tables below, on request.
    //
    // ⚠️ CLIENT-SIDE ONLY. The click still goes out as the game's own SendFriendDuoAction, and a
    // two-person animation is server state (PlayerSocialData, written on BOTH players). There is no
    // local half to fake — unlike a single action, a duo pose needs the partner driven too.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // The table is keyed by id within the CURRENT level's dictionary; 54 distinct ids ship today
        // (378 decoded rows = 54 ids x 7 level variants). The probe walks a little past that so a
        // patch that adds rows is picked up without a code change.
        private const int FriendInteractUnlockMaxId = 120;

        // The server's verdict for a friend duo action. errorCode is the FIRST field of both structs,
        // so four bytes is the whole payload we need.
        //
        // WHY THIS EXISTS: the click is the game's own call and the mod is not on that path, so a
        // refusal is completely silent — the client has no toast text for these codes either
        // ("FriendShipLvNotEnough not defined in ErrorCode表" in the game's Player.log). Without this
        // there is no way to say which interaction the server accepted and which it turned down.
        private const string FriendDuoResponseEventName =
            "ScriptsRefactory.DataAndProtocol.Events.ResponseFriendDuoActionWithParamNetworkEvent";
        private const string FriendDuoResponseForSenderEventName =
            "ScriptsRefactory.DataAndProtocol.Events.ResponseFriendDuoActionForSenderWithParamNetworkEvent";
        private const int FriendDuoResponsePayloadBytes = 4;

        // Rows this feature refuses to unlock even when their date window has them hidden.
        //
        // They cannot be told apart by any field — 16, 32, 33 and 50 look exactly like a plain
        // animation in the table — because the routing lives in the PANEL code. So each id is traced
        // to the panel that sends it:
        //
        //   15  ChooseGiftPanel                    gift chooser
        //   16  gift (赠礼, icon 516 — the icon 15 and 44 also use)
        //   17  BattlePassFriendSellPanel          assist sell, carries OuterItemNetPair
        //   18  BattlePassFriendShopPanel          assist buy, carries OuterItemNetPair
        //   19  ChooseStickerPanel                 stickers, carries selectedStickers
        //   32  ActivityMlpTreePanel               activity flow
        //   33  ChooseBadgePanel                   badge gift
        //   38  万圣节讨糖 — seasonal ACTIVITY (interactionId 3034), needs the event running server-side
        //   44  gift, continuous state (stateType = 2, isContinue = 1)
        //   50  ChooseInteractGiftPanel.Open(50, actId), the only row in TableFriendInteractGift
        //
        // ⚠️ An id list goes stale on a game update. Re-check it by grepping the UI panels for
        // `new FriendDoubleSocialEvent` after a code update.
        //
        // Deliberately NOT a field-shape test: isEntityCreated / needReplace correlate with "the
        // click does nothing" (43 碰拳, 22 双人自拍 carry them), but those are still ANIMATIONS, and
        // hiding them would go beyond what was asked.
        private static readonly int[] FriendInteractExcludedIds = { 15, 16, 17, 18, 19, 32, 33, 38, 44, 50 };

        private bool friendInteractUnlockHooked;

        private bool friendInteractUnlockEnabled;
        private int friendInteractUnlockWorldEpoch = -1;
        private bool friendInteractUnlockTried;
        private string friendInteractUnlockStatus = "Idle.";

        public bool FriendInteractUnlockEnabled
        {
            get { return this.friendInteractUnlockEnabled; }
            set { this.friendInteractUnlockEnabled = value; }
        }

        public string FriendInteractUnlockStatus
        {
            get { return this.friendInteractUnlockStatus; }
        }

        private void ProcessFriendInteractUnlockOnUpdate()
        {
            // TableFriendInteracts is LEVEL-KEYED (LevelTableFriendInteracts[CurLevelId]), so a world
            // change hands the panel a different dictionary of different row objects. The previous
            // world's patch means nothing here.
            if (this.friendInteractUnlockWorldEpoch != this.WorldReadyEpoch)
            {
                this.friendInteractUnlockWorldEpoch = this.WorldReadyEpoch;
                this.friendInteractUnlockTried = false;
            }

            if (!this.friendInteractUnlockEnabled || this.friendInteractUnlockTried || !this.IsWorldReady)
            {
                return;
            }

            this.friendInteractUnlockTried = true;
            try
            {
                this.EnsureFriendDuoVerdictHooks();
                this.ApplyFriendInteractUnlock();
            }
            catch (Exception ex)
            {
                this.friendInteractUnlockStatus = "Failed: " + ex.Message;
                ModLogger.Msg("[FriendInteractUnlock] " + this.friendInteractUnlockStatus);
            }
        }

        // Registered once per session — hooks are never torn down in this codebase, and the two event
        // types cost two of the engine's 48 slots.
        private void EnsureFriendDuoVerdictHooks()
        {
            if (this.friendInteractUnlockHooked)
            {
                return;
            }

            this.friendInteractUnlockHooked = true;
            bool a = this.RegisterGameEventHook(FriendDuoResponseEventName,
                FriendDuoResponsePayloadBytes, this.OnFriendDuoVerdict);
            bool b = this.RegisterGameEventHook(FriendDuoResponseForSenderEventName,
                FriendDuoResponsePayloadBytes, this.OnFriendDuoVerdict);
            ModLogger.Msg("[FriendInteractUnlock] verdict hooks: response=" + a + " forSender=" + b);
        }

        // Always logs, both outcomes. A refusal names the code so "it did nothing" becomes a fact
        // with a reason; code 0 is the server accepting, which is the only proof an interaction the
        // player was not entitled to actually went through.
        private void OnFriendDuoVerdict(GameEventSnapshot e)
        {
            int code = e.ReadInt32(0);
            ModLogger.Msg("[FriendInteractUnlock] server verdict: " + FriendDuoErrorCodeName(code)
                          + " (" + code + ")");
        }

        // Only the codes this path actually produces; anything else is reported as its number.
        private static string FriendDuoErrorCodeName(int code)
        {
            switch (code)
            {
                case 0: return "Success";
                case 600: return "FriendShipIsNotFriend";
                case 601: return "FriendShipLvNotEnough";
                default: return "unknown";
            }
        }

        // Open the event gate on every event-locked row of the CURRENT level's table.
        //
        // Rows are fetched one at a time through the game's own TableData.GetFriendInteract(id, bool)
        // rather than by enumerating the dictionary: enumerating a Dictionary<int, T> from AuraMono
        // means generic invokes over a value type, which is a documented way to fault this runtime.
        // The getter is a plain static, and the row it returns is written to immediately — nothing
        // allocates between the fetch and the writes, so the pointer cannot move underneath them.
        private unsafe void ApplyFriendInteractUnlock()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoFieldSetValue == null
                || auraMonoClassGetFieldFromName == null)
            {
                this.friendInteractUnlockStatus = "AuraMono unavailable.";
                return;
            }

            // TableData sits in the GLOBAL namespace — same resolve the emote unlock needs.
            IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            IntPtr getter = tableData == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(tableData, "GetFriendInteract", 2);
            if (getter == IntPtr.Zero)
            {
                this.friendInteractUnlockStatus = "TableData.GetFriendInteract(int,bool) unresolved.";
                ModLogger.Msg("[FriendInteractUnlock] " + this.friendInteractUnlockStatus
                              + " (TableData=" + (tableData != IntPtr.Zero) + ")");
                return;
            }

            IntPtr rowClass = this.FindAuraMonoClassInAllLoadedImages("TableFriendInteract", string.Empty);
            IntPtr fGain = rowClass == IntPtr.Zero ? IntPtr.Zero : auraMonoClassGetFieldFromName(rowClass, "gainCondition");
            IntPtr fUnlock = rowClass == IntPtr.Zero ? IntPtr.Zero : auraMonoClassGetFieldFromName(rowClass, "unlockType");
            if (fGain == IntPtr.Zero || fUnlock == IntPtr.Zero)
            {
                // Fail CLOSED: a partial patch is worse than none — it would leave rows listed but
                // still unavailable, which reads as "the feature is broken" rather than "off".
                this.friendInteractUnlockStatus = "TableFriendInteract fields unresolved.";
                ModLogger.Msg("[FriendInteractUnlock] " + this.friendInteractUnlockStatus
                              + " (gainCondition=" + (fGain != IntPtr.Zero)
                              + ", unlockType=" + (fUnlock != IntPtr.Zero) + ")");
                return;
            }

            // Argument slots allocated ONCE — a stackalloc inside the loop is a real stack-overflow
            // risk (CA2014), not a style nit.
            IntPtr* probeArgs = stackalloc IntPtr[2];
            int probeId = 0;
            byte needException = 0;
            int zero = 0;
            int patched = 0;
            int skippedExcluded = 0;

            for (int id = 1; id <= FriendInteractUnlockMaxId; id++)
            {
                probeId = id;
                probeArgs[0] = (IntPtr)(&probeId);
                probeArgs[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr row = auraMonoRuntimeInvoke(getter, IntPtr.Zero, (IntPtr)probeArgs, ref exc);
                if (exc != IntPtr.Zero || row == IntPtr.Zero)
                {
                    continue; // no such interaction on this build / level
                }

                // Item transfers and seasonal activities stay locked, on request.
                if (Array.IndexOf(FriendInteractExcludedIds, id) >= 0)
                {
                    skippedExcluded++;
                    continue;
                }

                // Only EVENT-locked rows are touched. Everything else the game already lists on its
                // own terms, and re-routing a row that needs nothing is a change with no upside.
                this.TryGetMonoInt32Member(row, "gainCondition", out int gain);
                if (gain == 0)
                {
                    continue;
                }

                // One line per row, so a row in the panel can be turned back into an id. The level is
                // logged because it still applies — the row is listed now, but the game will only let
                // it be clicked once the friendship actually reaches it.
                this.TryGetMonoInt32Member(row, "motionDisplayIcon", out int icon);
                this.TryGetMonoInt32Member(row, "sortPriority", out int sort);
                this.TryGetMonoInt32Member(row, "friendshipLv", out int lvWas);
                this.TryGetMonoInt32Member(row, "isDisable", out int disabledWas);
                ModLogger.Msg("[FriendInteractUnlock]   id=" + id + " icon=" + icon + " sort=" + sort
                              + " wasLv=" + lvWas + " wasGain=" + gain
                              + (disabledWas != 0 ? " wasDISABLED" : string.Empty));

                auraMonoFieldSetValue(row, fGain, (IntPtr)(&zero));    // Default — listed outside its date window
                auraMonoFieldSetValue(row, fUnlock, (IntPtr)(&zero));  // level bucket, not activity points
                // friendshipLv and isDisable are NOT written: the level is the game's progression and
                // is enforced server-side anyway, and a disabled row is one the developers switched
                // off. Both stay exactly as shipped.
                patched++;
            }

            this.friendInteractUnlockStatus = patched > 0
                ? "Listed " + patched + " event-locked interactions (" + skippedExcluded
                    + " item/seasonal left locked); friendship levels untouched."
                : "Nothing event-locked to list.";
            ModLogger.Msg("[FriendInteractUnlock] " + this.friendInteractUnlockStatus);
        }
    }
}
