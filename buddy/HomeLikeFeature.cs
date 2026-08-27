using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto-like own home — sends the one daily "very nice" reaction to the player's OWN mailbox,
    // the same thing a click on the like bar over the mailbox does, without having to walk to it.
    //
    // WHICH SYSTEM ACTUALLY CARRIES A HOME LIKE. There are two of them in the dumps and only the
    // second one is on the wire:
    //   * XDT.Scene.Shared.Modules.HouseLike (HouseLikeNetworkCommand / HouseLikeCancelNetworkCommand)
    //     is server-side; the Mono client contains no sender for either command.
    //   * XDT.Scene.Shared.Modules.EmojiFeedBack is the live channel. The vanilla click path is
    //     HomeLikeTrackCellModel.OnLikeDisplayClick (the widget the mailbox track bar grows while
    //     InteractId.HomeLike = 911 is active) -> EmojiReactionPanelLogic.TrySubmitQuickFeedback ->
    //     TrySubmitFeedbackChange -> EmojiFeedBackProtocolManager.SendFeedBack(ownerNetId, [999], [])
    //     -> WebRequestUtility.SendCommand(EmojiFeedBackCommand).
    //
    // ⚠️ MEASURED 2026-08-27, THE HARD-WON PART: HouseLikeProtocolManager.GetSelfHouseLikeData()
    // does NOT answer for this path. Its SelfHouseLikeData.IsGaveLike (= the server's
    // HouseLikeHistoryStatComponent.IsGaveSelf) and its Today counter both stayed put across a send
    // that the invoke reported as clean — read live: `today 0, total 4`, so the component is synced
    // and populated, it simply does not track an EmojiFeedBack like. The first version of this
    // feature gated on it and concluded, wrongly, that nothing had landed. The state this feature
    // reads is therefore the SAME pair the game's own widget renders `alreadyLiked` from:
    //   EmojiReactionPanelLogic.GetHomeTodayFeedbackGuid(ownerNetId)  (private static, 1 arg)
    //   EmojiReactionPanelLogic.IsEmojiFeedbackLiked(Guid)            (public static, 1 arg)
    // mono_runtime_invoke does not enforce accessibility, so the private one is reachable; both were
    // confirmed present with these arities against the RUNNING build via the MCP bridge, not the
    // dumps. GetSelfHouseLikeData is left alone — knowing what it is NOT is the point of this note.
    //
    // THE GUID IS ALSO THE LOCATION GATE, and that is why the feature needs no level check of its
    // own. GetHomeTodayFeedbackGuid resolves our short id, then asks EmojiFeedbackClientService for
    // the entity carrying EmojiFeedBackHomeTodayTag; an empty Guid means our home's today-feedback
    // entity is not streamed to this client here, which is exactly the state in which the mailbox
    // widget could not show a like bar either. Nothing is sent then. (netIds are room-scoped, so a
    // command naming our player netId in the wrong room is at best a no-op — the likeliest reading
    // of the first live attempt, which went out 12 s after login.)
    //
    // TARGET is the home OWNER'S PLAYER netId, not a mailbox or a feedback entity: the widget passes
    // BuildComponent.OwnerId of the mailbox, and the server resolves the owner's Home/HomeToday/
    // HomeBp feedback entities itself. For our own home that is simply our own player netId.
    //
    // WHY EXPRESSION 999 IS HARDCODED: the reaction picker is skipped whenever the target type has
    // exactly ONE selectable expression (EmojiReactionPanelLogic.ShouldQuickSubmit).
    // Expression.emojiFeedBackTypes in the design tables gives EmojiFeedBackType.Home (= 4) exactly
    // one row — id 999 — so a home like is always a single silent send, never a panel. Sending 999
    // is not a shortcut around the UI; it is the only thing the UI could ever have sent.
    //
    // ACTION: EmojiFeedBackProtocolManager.SendFeedBack(uint, List<int>, List<int>) — the game's own
    // NON-generic protocol wrapper, which builds the command struct and calls the generic
    // SendCommand<T> inside normally-JIT'd game code (project rule: never invoke a runtime-inflated
    // generic ourselves). The shared TryAuraSendCommand helper cannot be used here at all: it maps
    // int/uint/float/bool/string fields only, and EmojiFeedBackCommand carries two List<int>s. The
    // two lists are built the same proven way AutoLearn builds its List<uint>.
    //
    // TRIGGER is EmojiFeedBackRecordUpdateEvent (XDTDataAndProtocol.Events), dispatched by
    // AudioRecordSyncSystem on every Added/Updated/Removed of EmojiFeedBackRecordComponent — i.e.
    // whenever OUR OWN like records change. It is the same signal HomeLikeTrackCellModel refreshes
    // its like display from, so confirmation is immediate rather than timed, and the daily rotation
    // of the today-entity is noticed inside a running session. (HomeLikeUpdatedEvent, the obvious
    // candidate by name, is driven by the HouseLike stat component and was measured NOT to fire for
    // this path.) A slow timer re-check backs it up, so a refused hook only makes the feature
    // slower, never wrong.
    public partial class HeartopiaComplete
    {
        private const string HomeLikeRecordUpdatedEventName = "XDTDataAndProtocol.Events.EmojiFeedBackRecordUpdateEvent";

        // The event struct is empty (StructLayout Size = 1) — the dispatch itself is the signal.
        private const int HomeLikeRecordUpdatedEventPayloadBytes = 0;

        private const string HomeLikePanelLogicTypeName = "XDTGame.UI.Panel.EmojiReactionPanelLogic";
        private const string HomeLikePanelLogicNamespace = "XDTGame.UI.Panel";
        private const string HomeLikePanelLogicClassName = "EmojiReactionPanelLogic";
        private const string HomeLikeGuidMethodName = "GetHomeTodayFeedbackGuid";
        private const string HomeLikeIsLikedMethodName = "IsEmojiFeedbackLiked";

        private const string HomeLikeProtocolTypeName = "XDTDataAndProtocol.ProtocolService.EmojiFeedBack.EmojiFeedBackProtocolManager";
        private const string HomeLikeProtocolNamespace = "XDTDataAndProtocol.ProtocolService.EmojiFeedBack";
        private const string HomeLikeProtocolClassName = "EmojiFeedBackProtocolManager";
        private const string HomeLikeProtocolMethodName = "SendFeedBack";

        // Expression.id 999 ("very nice") — the ONLY row whose emojiFeedBackTypes contains
        // EmojiFeedBackType.Home (4). Verified in the design tables, not guessed.
        private const int HomeLikeExpressionId = 999;

        // Slack for the feedback entities to stream in after a world load.
        private const float HomeLikeWorldSettleSeconds = 12f;

        // Flipping the toggle on mid-session must not wait for the next world load.
        private const float HomeLikeToggleSettleSeconds = 2f;

        // Cadence while the home-today feedback entity is not reachable from here. This is the
        // common state — it is only reachable in the homeland — so it must stay cheap and quiet.
        private const float HomeLikeWaitRecheckSeconds = 30f;

        // How long the server gets to push the record back before the send is judged.
        private const float HomeLikeVerifySeconds = 8f;

        // A record-update event means our own records have ALREADY changed — this is settle time for
        // the read, not a wait for the answer.
        private const float HomeLikeEventSettleSeconds = 1f;

        // Idle cadence once the like is in. It exists to notice the daily rotation if the session
        // outlives it and no event arrives.
        private const float HomeLikeIdleRecheckSeconds = 900f;

        private const float HomeLikeRetrySeconds = 30f;

        // Sends allowed between two confirmations. Two, not one: the first may go out the moment the
        // homeland streams in and race the record sync. After that the feature stops and says why
        // instead of re-sending forever.
        private const int HomeLikeMaxSendsPerReset = 2;

        internal static bool MasterLogHomeLike = false;

        private bool autoLikeOwnHome;
        private bool homeLikeRegistered;
        private bool homeLikeHookInstallLogged;
        private bool homeLikeLastToggleState;
        private bool homeLikeSentThisWorld;
        private bool homeLikeUnverifiedLogged;
        private bool homeLikeHaveLastGuid;
        private bool homeLikeWaitLogged;
        private Guid homeLikeLastGuid = Guid.Empty;
        private int homeLikeSendsSinceReset;
        private int homeLikeSentTotal;
        private float homeLikeNextCheckAt;
        private float homeLikeVerifyAt;
        private IntPtr homeLikePanelLogicClass = IntPtr.Zero;
        private IntPtr homeLikeGuidMethod = IntPtr.Zero;
        private IntPtr homeLikeIsLikedMethod = IntPtr.Zero;
        private IntPtr homeLikeProtocolMethod = IntPtr.Zero;
        private IntPtr homeLikeIntListClass = IntPtr.Zero;
        private IntPtr homeLikeIntListAddMethod = IntPtr.Zero;
        private string homeLikeStatus = "Idle.";
        private string homeLikeLastLoggedStatus;
        private FeatureBreakerState homeLikeBreaker;

        private void ProcessHomeLikeOnUpdate()
        {
            bool on = this.autoLikeOwnHome;
            if (on != this.homeLikeLastToggleState)
            {
                this.homeLikeLastToggleState = on;
                if (on)
                {
                    // A fresh opt-in gets a fresh budget: the user asking for it again is the one
                    // signal that beats "we already tried and it did not stick".
                    this.homeLikeSendsSinceReset = 0;
                    this.homeLikeSentThisWorld = false;
                    this.homeLikeUnverifiedLogged = false;
                    this.homeLikeWaitLogged = false;
                    this.homeLikeNextCheckAt = Time.unscaledTime + HomeLikeToggleSettleSeconds;
                }
                else
                {
                    this.HomeLikeSetStatus("Off.");
                }
            }

            if (!on)
            {
                return;
            }

            // Registration is deliberately LAZY — the event-hook pool is a shared, never-released
            // 96-slot budget, so a feature nobody switched on must not hold one. Registering late is
            // safe: the world-ready pump picks up a callback added mid-epoch, and the toggle branch
            // above has already armed the first check on its own.
            this.EnsureHomeLikeRegistrations();

            // Resolving game types before a world exists fails at best and AVs at worst
            // (AGENTS.md world-ready rule).
            if (!this.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool hooked = this.IsGameEventHookInstalled(HomeLikeRecordUpdatedEventName);
            if (hooked && !this.homeLikeHookInstallLogged)
            {
                this.homeLikeHookInstallLogged = true;
                ModLogger.Msg("[HomeLike] hook installed: " + HomeLikeRecordUpdatedEventName);
            }

            if (now < this.homeLikeNextCheckAt)
            {
                return;
            }

            if (!this.homeLikeBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                this.HomeLikeTick(now);
                this.homeLikeBreaker.Success();
            }
            catch (Exception ex)
            {
                this.homeLikeBreaker.Failure("HomeLike", ex, now);
                this.homeLikeNextCheckAt = now + HomeLikeRetrySeconds;
                this.HomeLikeSetStatus("Tick error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void EnsureHomeLikeRegistrations()
        {
            if (this.homeLikeRegistered)
            {
                return;
            }

            this.homeLikeRegistered = true;
            bool ok = this.RegisterGameEventHook(
                HomeLikeRecordUpdatedEventName, HomeLikeRecordUpdatedEventPayloadBytes, this.OnHomeLikeRecordUpdatedEventHook);
            if (!ok)
            {
                // Not fatal: the timer re-check still confirms, just later.
                ModLogger.Msg("[HomeLike] EmojiFeedBackRecordUpdateEvent hook registration REFUSED — confirmation falls back to the timer.");
            }
            else if (MasterLogHomeLike)
            {
                ModLogger.Msg("[HomeLike] registered hook " + HomeLikeRecordUpdatedEventName);
            }

            this.RegisterWorldReadyCallback("AutoLikeOwnHome", this.OnHomeLikeWorldReady);
        }

        // New world => one more chance to send, but NOT a fresh budget: hopping between town and
        // home must not turn into one command per transition if the like never registers.
        private bool OnHomeLikeWorldReady()
        {
            this.homeLikeSentThisWorld = false;
            this.homeLikeWaitLogged = false;
            this.homeLikeVerifyAt = 0f;
            if (this.autoLikeOwnHome)
            {
                this.homeLikeNextCheckAt = Time.unscaledTime + HomeLikeWorldSettleSeconds;
            }

            return true;
        }

        private void OnHomeLikeRecordUpdatedEventHook(GameEventSnapshot e)
        {
            if (!this.autoLikeOwnHome)
            {
                return;
            }

            // Our own feedback records have already changed by the time this fires, so pulling the
            // verification verdict forward is correct rather than premature — this IS the answer the
            // verify window was waiting for.
            float at = Time.unscaledTime + HomeLikeEventSettleSeconds;
            if (at < this.homeLikeNextCheckAt)
            {
                this.homeLikeNextCheckAt = at;
            }

            if (this.homeLikeVerifyAt > at)
            {
                this.homeLikeVerifyAt = at;
            }

            if (MasterLogHomeLike)
            {
                ModLogger.Msg("[HomeLike] EmojiFeedBackRecordUpdateEvent — re-reading own like state");
            }
        }

        private void HomeLikeTick(float now)
        {
            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
            {
                this.homeLikeNextCheckAt = now + HomeLikeRetrySeconds;
                this.HomeLikeSetStatus("Self player netId unavailable.");
                return;
            }

            if (!this.TryReadHomeTodayFeedbackGuid(selfNetId, out Guid guid, out string readStatus))
            {
                this.homeLikeNextCheckAt = now + HomeLikeRetrySeconds;
                this.HomeLikeSetStatus(readStatus);
                return;
            }

            if (guid == Guid.Empty)
            {
                // Not an error: our home's today-feedback entity simply is not streamed here. This
                // is the normal state everywhere except the homeland, so it is logged once per
                // world rather than every re-check.
                this.homeLikeNextCheckAt = now + HomeLikeWaitRecheckSeconds;
                if (!this.homeLikeWaitLogged)
                {
                    this.homeLikeWaitLogged = true;
                    this.HomeLikeSetStatus("Own home like data is not loaded here — waiting for the homeland.");
                }

                return;
            }

            // The today-entity rotates on the daily reset, so a NEW guid is the reset: re-open the
            // budget. This is also why the guid is remembered rather than just read.
            if (this.homeLikeHaveLastGuid && guid != this.homeLikeLastGuid)
            {
                this.homeLikeSentThisWorld = false;
                this.homeLikeSendsSinceReset = 0;
                this.homeLikeUnverifiedLogged = false;
                if (MasterLogHomeLike)
                {
                    ModLogger.Msg("[HomeLike] today-feedback guid rotated — daily reset, re-arming");
                }
            }

            this.homeLikeHaveLastGuid = true;
            this.homeLikeLastGuid = guid;

            if (!this.TryReadHomeLikeAlreadyLiked(guid, out bool liked, out string likedStatus))
            {
                this.homeLikeNextCheckAt = now + HomeLikeRetrySeconds;
                this.HomeLikeSetStatus(likedStatus);
                return;
            }

            if (liked)
            {
                this.homeLikeSendsSinceReset = 0;
                this.homeLikeUnverifiedLogged = false;
                this.homeLikeNextCheckAt = now + HomeLikeIdleRecheckSeconds;
                this.HomeLikeSetStatus("Own home liked today.");
                return;
            }

            if (this.homeLikeSentThisWorld || this.homeLikeSendsSinceReset >= HomeLikeMaxSendsPerReset)
            {
                this.HomeLikeJudgeUnconfirmedSend(now);
                return;
            }

            if (!this.TrySendHomeLike(selfNetId, out string sendStatus))
            {
                this.homeLikeNextCheckAt = now + HomeLikeRetrySeconds;
                this.HomeLikeSetStatus(sendStatus);
                return;
            }

            this.homeLikeSentThisWorld = true;
            this.homeLikeSendsSinceReset++;
            this.homeLikeSentTotal++;
            // Each send gets its own verdict — otherwise a second attempt would be judged silently
            // against the first one's already-logged outcome.
            this.homeLikeUnverifiedLogged = false;
            this.homeLikeVerifyAt = now + HomeLikeVerifySeconds;
            this.homeLikeNextCheckAt = this.homeLikeVerifyAt;
            this.HomeLikeSetStatus("Like sent for own home; waiting for the record.");
        }

        // Reached only with the like still absent from our own records after a send — the same bool
        // the mailbox widget draws, so if it is false the server did not take the like.
        private void HomeLikeJudgeUnconfirmedSend(float now)
        {
            if (now < this.homeLikeVerifyAt)
            {
                this.homeLikeNextCheckAt = this.homeLikeVerifyAt;
                return;
            }

            this.homeLikeNextCheckAt = now + HomeLikeIdleRecheckSeconds;
            if (this.homeLikeUnverifiedLogged)
            {
                return;
            }

            this.homeLikeUnverifiedLogged = true;
            this.HomeLikeSetStatus("Like sent but it is still not in our own records — the server did not take it.");
        }

        // EmojiReactionPanelLogic.GetHomeTodayFeedbackGuid(uint ownerNetId) — PRIVATE static
        // (mono_runtime_invoke does not check accessibility) returning a Guid, i.e. a boxed copy.
        // Guid.Empty is a legitimate answer, not a failure: it means the entity is not here.
        private unsafe bool TryReadHomeTodayFeedbackGuid(uint ownerNetId, out Guid guid, out string status)
        {
            guid = Guid.Empty;
            status = string.Empty;

            if (!this.TryResolveHomeLikePanelLogic(out status))
            {
                return false;
            }

            uint netId = ownerNetId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            if (!TryAuraInvoke(this.homeLikeGuidMethod, IntPtr.Zero, (IntPtr)args, out IntPtr boxed, out string invokeError))
            {
                status = "GetHomeTodayFeedbackGuid failed: " + invokeError;
                return false;
            }

            if (boxed == IntPtr.Zero)
            {
                status = "GetHomeTodayFeedbackGuid returned nothing.";
                return false;
            }

            // TryUnboxMonoGuid reports false for Guid.Empty, which here is a VALUE, not an error —
            // the caller distinguishes them, so an empty read is still a successful read.
            uint pin = AuraMonoPinNew(boxed);
            try
            {
                this.TryUnboxMonoGuid(boxed, out guid);
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // EmojiReactionPanelLogic.IsEmojiFeedbackLiked(Guid) — public static, one value-type
        // argument passed as a pointer to the raw 16 bytes (the plain value-type convention;
        // Nullable<T> is the only exception to it, and this is not one).
        private unsafe bool TryReadHomeLikeAlreadyLiked(Guid feedbackGuid, out bool liked, out string status)
        {
            liked = false;
            status = string.Empty;

            if (!this.TryResolveHomeLikePanelLogic(out status))
            {
                return false;
            }

            Guid value = feedbackGuid;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            if (!TryAuraInvoke(this.homeLikeIsLikedMethod, IntPtr.Zero, (IntPtr)args, out IntPtr boxed, out string invokeError))
            {
                status = "IsEmojiFeedbackLiked failed: " + invokeError;
                return false;
            }

            if (boxed == IntPtr.Zero)
            {
                status = "IsEmojiFeedbackLiked returned nothing.";
                return false;
            }

            uint pin = AuraMonoPinNew(boxed);
            try
            {
                if (!this.TryUnboxMonoBoolean(boxed, out liked))
                {
                    status = "IsEmojiFeedbackLiked result unreadable.";
                    return false;
                }

                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private bool TryResolveHomeLikePanelLogic(out string status)
        {
            status = string.Empty;
            if (this.homeLikeGuidMethod != IntPtr.Zero && this.homeLikeIsLikedMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "Mono API not ready.";
                return false;
            }

            if (this.homeLikePanelLogicClass == IntPtr.Zero)
            {
                this.homeLikePanelLogicClass = this.FindAuraMonoClassByFullName(HomeLikePanelLogicTypeName);
                if (this.homeLikePanelLogicClass == IntPtr.Zero)
                {
                    this.homeLikePanelLogicClass =
                        this.FindAuraMonoClassAcrossLoadedAssemblies(HomeLikePanelLogicNamespace, HomeLikePanelLogicClassName);
                }
            }

            if (this.homeLikePanelLogicClass == IntPtr.Zero)
            {
                status = "EmojiReactionPanelLogic not found.";
                return false;
            }

            if (this.homeLikeGuidMethod == IntPtr.Zero)
            {
                this.homeLikeGuidMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.homeLikePanelLogicClass, HomeLikeGuidMethodName, 1);
            }

            if (this.homeLikeIsLikedMethod == IntPtr.Zero)
            {
                this.homeLikeIsLikedMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.homeLikePanelLogicClass, HomeLikeIsLikedMethodName, 1);
            }

            if (this.homeLikeGuidMethod == IntPtr.Zero || this.homeLikeIsLikedMethod == IntPtr.Zero)
            {
                status = "EmojiReactionPanelLogic.GetHomeTodayFeedbackGuid / IsEmojiFeedbackLiked (1 arg) not found.";
                return false;
            }

            return true;
        }

        // EmojiFeedBackProtocolManager.SendFeedBack(uint targetNetId, List<int> expressionIds,
        // List<int> cancelIds) — static, one value argument and two reference arguments.
        //
        // ⚠️ THIS SENDS A REAL COMMAND TO A LIVE SERVER.
        //
        // cancelIds is an EMPTY list, never null: the command's serializer writes it unconditionally
        // and a null there is the game's business, not ours to gamble on.
        private unsafe bool TrySendHomeLike(uint targetNetId, out string status)
        {
            status = string.Empty;
            if (!this.TryResolveHomeLikeProtocol(out status))
            {
                return false;
            }

            // Building the SECOND list allocates on the mono heap, which can move the first one, so
            // the expression list is pinned from the moment it exists until the invoke returns.
            if (!this.TryCreateHomeLikeIntList(HomeLikeExpressionId, out IntPtr expressionList, out uint expressionPin, out status))
            {
                return false;
            }

            try
            {
                if (!this.TryCreateHomeLikeIntList(0, out IntPtr cancelList, out uint cancelPin, out status))
                {
                    return false;
                }

                try
                {
                    uint netId = targetNetId;
                    IntPtr* args = stackalloc IntPtr[3];
                    args[0] = (IntPtr)(&netId);
                    args[1] = expressionList;
                    args[2] = cancelList;
                    if (!TryAuraInvoke(this.homeLikeProtocolMethod, IntPtr.Zero, (IntPtr)args, out _, out string invokeError))
                    {
                        status = "SendFeedBack failed: " + invokeError;
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(cancelPin);
                }
            }
            finally
            {
                AuraMonoPinFree(expressionPin);
            }

            ModLogger.Msg("[HomeLike] SendFeedBack(netId=" + targetNetId + ", expression=" + HomeLikeExpressionId
                + ") sent; " + (this.homeLikeSentTotal + 1) + " this session");
            return true;
        }

        private bool TryResolveHomeLikeProtocol(out string status)
        {
            status = string.Empty;
            if (this.homeLikeProtocolMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "Mono API not ready.";
                return false;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName(HomeLikeProtocolTypeName);
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(HomeLikeProtocolNamespace, HomeLikeProtocolClassName);
            }

            if (protocolClass == IntPtr.Zero)
            {
                status = "EmojiFeedBackProtocolManager not found.";
                return false;
            }

            this.homeLikeProtocolMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, HomeLikeProtocolMethodName, 3);
            if (this.homeLikeProtocolMethod == IntPtr.Zero)
            {
                status = "EmojiFeedBackProtocolManager.SendFeedBack(3 args) not found.";
                return false;
            }

            return true;
        }

        // Type.GetType(name) + Activator.CreateInstance(type) + List<int>.Add — the shape AutoLearn
        // and PetFeed use for List<uint>. `value` of 0 builds the EMPTY list (0 is not a valid
        // expression id, so it cannot collide with a real one).
        //
        // The pin is taken here and handed to the caller rather than taken after the return: every
        // step below allocates, and the object must not be left unrooted between two of them.
        private unsafe bool TryCreateHomeLikeIntList(int value, out IntPtr listObj, out uint listPin, out string status)
        {
            listObj = IntPtr.Zero;
            listPin = 0U;
            status = string.Empty;

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoStringNew == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                status = "List<int> prerequisites unavailable.";
                return false;
            }

            string[] typeCandidates = new[]
            {
                "System.Collections.Generic.List`1[System.Int32]",
                "System.Collections.Generic.List`1[[System.Int32, mscorlib]]",
                "System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < typeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, typeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                if (!TryAuraInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, out IntPtr typeObj, out _)
                    || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                if (!TryAuraInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, out listObj, out _))
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "List<int> create failed.";
                return false;
            }

            listPin = AuraMonoPinNew(listObj);
            if (value == 0)
            {
                return true;
            }

            if (this.homeLikeIntListClass == IntPtr.Zero)
            {
                this.homeLikeIntListClass = auraMonoObjectGetClass(listObj);
            }

            if (this.homeLikeIntListAddMethod == IntPtr.Zero && this.homeLikeIntListClass != IntPtr.Zero)
            {
                this.homeLikeIntListAddMethod = this.FindAuraMonoMethodOnHierarchy(this.homeLikeIntListClass, "Add", 1);
            }

            if (this.homeLikeIntListAddMethod == IntPtr.Zero)
            {
                AuraMonoPinFree(listPin);
                listPin = 0U;
                listObj = IntPtr.Zero;
                status = "List<int>.Add unavailable.";
                return false;
            }

            int item = value;
            IntPtr* addArgs = stackalloc IntPtr[1];
            addArgs[0] = (IntPtr)(&item);
            if (!TryAuraInvoke(this.homeLikeIntListAddMethod, listObj, (IntPtr)addArgs, out _, out string addError))
            {
                AuraMonoPinFree(listPin);
                listPin = 0U;
                listObj = IntPtr.Zero;
                status = "List<int>.Add failed: " + addError;
                return false;
            }

            return true;
        }

        // Failures always reach the log, not just the status string — a silent no-op is the only
        // other thing this feature could look like; deduped so a stuck state cannot spam.
        private void HomeLikeSetStatus(string status)
        {
            this.homeLikeStatus = status;
            if (string.Equals(status, this.homeLikeLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.homeLikeLastLoggedStatus = status;
            ModLogger.Msg("[HomeLike] " + status);
        }
    }
}
