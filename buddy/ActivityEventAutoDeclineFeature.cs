using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Activity-Event Auto-Decline — the twin of PartyAutoDeclineFeature for the OTHER, much larger
    // multiplayer subsystem. Two separate toggles again, for the same reason.
    //
    // WHY THIS EXISTS SEPARATELY. The game has two parallel "join other players" systems that look
    // identical in the UI and share nothing in code:
    //   * Party      — table `Party`, exactly FIVE rows: Free Party, Sea Fishing Party, Tea Party,
    //                  Obstacle Party, Hide and Seek Party. Handled by PartyAutoDeclineFeature.
    //   * ActivityEvent — table `ActivityEvent`, ~606 rows. This is where the scheduled world events
    //                  live: every 鱼潮 shoal event (Neritic Shoal Event = ids 225/226, groupId 45),
    //                  toy-fish events, ice-crystal fish, and so on.
    // A field report of "a party invite rang and nothing was declined / the daily party auto-joined
    // me and nothing left it" was ActivityEvent traffic the whole time — the party hooks were live
    // and correct, that channel simply had no traffic. Do not merge the two halves.
    //
    // 1) AUTO-DECLINE INVITES (activityAutoDeclineInvites).
    //    Same phone-call plumbing as parties: ActivityEventProtocolManager.BeInvitedToEvent ->
    //    `ActivityInvitedEvent` -> the activity module builds an `EventCallData` -> 30 s ring.
    //    Suppressing the dispatch means no call is ever built.
    //
    //    DIFFERENCE FROM PARTIES, and a deliberate limitation: activities DO have a real reject
    //    protocol — `SendRejectActivityEventCommand(eventNetId, inviterShortId, ActivityOpType)`
    //    with Accept=1 / Later=2 / Reject=3 / Overtime=4, which is what PhoneModule.DropCall sends
    //    for an EventCallData. This feature does NOT send it, because the command needs the
    //    inviter's SHORT id and the event only carries net ids; converting requires
    //    PlayerProtocolManager.TryGetPlayerShortId(uint, out long), and passing an out-param slot
    //    for a value type through mono_runtime_invoke is the documented stack-corruption trap
    //    (TYPE_RESOLUTION.md; it is what crashed the AutoSell scan). Suppression alone therefore
    //    leaves the invite to expire server-side exactly as an unanswered call would — the inviter
    //    sees a timeout instead of an immediate decline. Wiring the real reject is a follow-up that
    //    needs a pointer-safe shortId lookup first.
    //
    // 2) AUTO-LEAVE (activityAutoLeaveEvents).
    //    Scheduled events add you server-side when you are standing in their area, the same way
    //    parties do, so again the only possible answer is to leave afterwards:
    //    `ActivityEventProtocolManager.SendQuitActivityEventCommand()` (static, zero args).
    //
    //    Trigger is `SelfActivityChangedEvent`, which ActivityEventProtocolManager dispatches from
    //    UpdateActivityEvent (netId == current activity) and UpdateParticipantInfo (self is in the
    //    participant list) — i.e. it fires for the server-side area add. Its own payload is
    //    `ActivityEventInfo?`, a nullable struct that is unreadable by offset, so it is registered
    //    at 0 bytes and used purely as an edge; the actual state comes from
    //    `ActivityEventSystem.IsSelfInActivity()` (zero-arg bool, DataModule singleton).
    //    Note IsSelfInActivity, not IsSelfInActivityOrParty — the latter would also fire for a
    //    party the other half is (or is deliberately not) handling.
    //
    //    DELIBERATE-JOIN DISCRIMINATION, same discipline as the party half:
    //      * `OnRequestJoinActivitySuccess` (uint activityNetId @0) — dispatched only by
    //        RequestJoinActivityResult on success, which only runs because WE sent
    //        ApplyForActivityEventCommand. A server-side area add never reaches it.
    //      * an invite we deliberately let through (auto-decline off) arms
    //        `activityInviteAwaitingChoice`, so accepting a ringing call counts as deliberate too.
    //        Edge-triggered and cleared with the membership, never a timer.
    //      * PartySystem.IsSelfVisitor() — being a visitor in someone else's town still requires a
    //        click, so it means the same thing here as it does for parties. Resolved through the
    //        party half's cached pointers (same partial class), fail-closed if unavailable.
    //
    //    Attempt budget is this half's own, identical shape to the party one.
    public partial class HeartopiaComplete
    {
        // uint activityNetId @0, uint inviterNetId @4.
        private const string ActivityInvitedEventName = "XDTDataAndProtocol.Events.ActivityInvitedEvent";
        private const int ActivityInvitedEventPayloadBytes = 8;

        // Payload is OtherTownActivityInviteInfo — ref-heavy, nothing readable by offset.
        private const string ActivityOtherRoomInvitedEventName = "XDTDataAndProtocol.Events.OtherRoomActivityInvitedEvent";
        private const int ActivityOtherRoomInvitedEventPayloadBytes = 0;

        // `ActivityEventInfo? endEventInfo` — nullable struct, unreadable. Edge only.
        private const string ActivitySelfChangedEventName = "XDTDataAndProtocol.Events.SelfActivityChangedEvent";
        private const int ActivitySelfChangedEventPayloadBytes = 0;

        // uint activityNetId @0.
        private const string ActivityJoinSuccessEventName = "XDTDataAndProtocol.Events.OnRequestJoinActivitySuccess";
        private const int ActivityJoinSuccessEventPayloadBytes = 4;

        private const string ActivityEventSystemTypeName = "XDTGameSystem.GameplaySystem.ActivityEvent.ActivityEventSystem";
        private const string ActivityEventSystemNamespace = "XDTGameSystem.GameplaySystem.ActivityEvent";
        private const string ActivityEventSystemClassName = "ActivityEventSystem";

        private const string ActivityProtocolManagerTypeName = "XDTDataAndProtocol.ProtocolService.ActivityEvent.ActivityEventProtocolManager";
        private const string ActivityProtocolManagerNamespace = "XDTDataAndProtocol.ProtocolService.ActivityEvent";
        private const string ActivityProtocolManagerClassName = "ActivityEventProtocolManager";

        private const float ActivityAutoLeaveMinIntervalSec = 3f;
        private const int ActivityAutoLeaveMaxAttemptsPerWindow = 3;
        private const float ActivityAutoLeaveWindowSec = 60f;

        internal static bool MasterLogActivityAutoDecline = false;

        private bool activityAutoDeclineInvites;
        private bool activityAutoLeaveEvents;

        private bool activityAutoDeclineRegistered;
        private int activityDeclinedInviteCount;
        private int activityAutoLeftCount;

        private bool activityJoinWasDeliberate;
        private bool activityInviteAwaitingChoice;
        private bool activityLastInActivity;

        private bool activityAutoLeavePending;
        private float activityAutoLeaveNextAttemptAt;
        private int activityAutoLeaveAttemptsInWindow;
        private float activityAutoLeaveWindowStartedAt;
        private bool activityAutoLeaveGaveUp;

        // Class/method pointers only (CI lint E3). The DataModule instance is level-scoped and
        // re-resolved on every use.
        private IntPtr activityEventSystemClass = IntPtr.Zero;
        private IntPtr activityIsSelfInActivityMethod = IntPtr.Zero;
        private IntPtr activityQuitMethod = IntPtr.Zero;

        private string activityAutoDeclineLastLoggedStatus;

        // First-sighting latches, mirroring the party half: the branch logs only speak when the
        // feature acts, so without these a quiet run is indistinguishable from a dead channel —
        // which is exactly the ambiguity that made the party half look broken when it was merely
        // pointed at the wrong subsystem. Ungated, once per event type per session.
        private bool activitySawInviteEvent;
        private bool activitySawOtherRoomInviteEvent;
        private bool activitySawSelfChangedEvent;
        private bool activitySawJoinSuccessEvent;

        private void ProcessActivityAutoDeclineOnUpdate()
        {
            this.EnsureActivityAutoDeclineRegistrations();

            bool decline = this.activityAutoDeclineInvites;
            this.SetGameEventHookSuppressForward(ActivityInvitedEventName, decline);
            this.SetGameEventHookSuppressForward(ActivityOtherRoomInvitedEventName, decline);

            if (!this.activityAutoLeavePending)
            {
                return;
            }

            if (!this.activityAutoLeaveEvents)
            {
                this.activityAutoLeavePending = false;
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.activityAutoLeaveNextAttemptAt)
            {
                return;
            }

            if (now - this.activityAutoLeaveWindowStartedAt > ActivityAutoLeaveWindowSec)
            {
                this.activityAutoLeaveWindowStartedAt = now;
                this.activityAutoLeaveAttemptsInWindow = 0;
                this.activityAutoLeaveGaveUp = false;
            }

            if (this.activityAutoLeaveGaveUp)
            {
                this.activityAutoLeavePending = false;
                return;
            }

            if (this.activityAutoLeaveAttemptsInWindow >= ActivityAutoLeaveMaxAttemptsPerWindow)
            {
                this.activityAutoLeaveGaveUp = true;
                this.activityAutoLeavePending = false;
                this.ActivityAutoDeclineSetStatus("Event auto-leave gave up after "
                    + ActivityAutoLeaveMaxAttemptsPerWindow + " attempts.");
                ModLogger.Msg("[ActivityAutoDecline] " + ActivityAutoLeaveMaxAttemptsPerWindow
                    + " quit attempts inside " + ActivityAutoLeaveWindowSec
                    + "s did not stick — the server keeps re-adding you (event restarted with a new"
                    + " netId, a fresh invite, or the quit was refused). Walk out of the event area;"
                    + " the budget rearms after a quiet window.");
                this.AddMenuNotification(
                    this.L("Event auto-leave failed — leave the event area on foot"),
                    new Color(1f, 0.55f, 0.45f));
                return;
            }

            if (!this.IsWorldReady)
            {
                return;
            }

            if (this.TryActivityAutoLeaveOnce())
            {
                this.activityAutoLeavePending = false;
            }
        }

        private void EnsureActivityAutoDeclineRegistrations()
        {
            if (this.activityAutoDeclineRegistered)
            {
                return;
            }

            this.activityAutoDeclineRegistered = true;

            bool inviteOk = this.RegisterGameEventHook(
                ActivityInvitedEventName, ActivityInvitedEventPayloadBytes, this.OnActivityInvitedEventHook);
            bool otherRoomOk = this.RegisterGameEventHook(
                ActivityOtherRoomInvitedEventName, ActivityOtherRoomInvitedEventPayloadBytes,
                this.OnActivityOtherRoomInvitedEventHook);
            bool selfChangedOk = this.RegisterGameEventHook(
                ActivitySelfChangedEventName, ActivitySelfChangedEventPayloadBytes,
                this.OnActivitySelfChangedEventHook);
            bool joinOk = this.RegisterGameEventHook(
                ActivityJoinSuccessEventName, ActivityJoinSuccessEventPayloadBytes,
                this.OnActivityJoinSuccessEventHook);

            if (!inviteOk || !otherRoomOk || !selfChangedOk || !joinOk)
            {
                ModLogger.Msg("[ActivityAutoDecline] hook registration incomplete — invite=" + inviteOk
                    + " otherRoom=" + otherRoomOk + " selfChanged=" + selfChangedOk + " join=" + joinOk);
            }
            else if (MasterLogActivityAutoDecline)
            {
                ModLogger.Msg("[ActivityAutoDecline] 4 hooks registered");
            }

            this.RegisterWorldReadyCallback("ActivityAutoDecline", this.OnActivityAutoDeclineWorldReady);
        }

        private bool OnActivityAutoDeclineWorldReady()
        {
            this.activityJoinWasDeliberate = false;
            this.activityInviteAwaitingChoice = false;
            this.activityLastInActivity = false;
            this.activityAutoLeavePending = false;
            this.activityAutoLeaveNextAttemptAt = 0f;
            this.activityAutoLeaveAttemptsInWindow = 0;
            this.activityAutoLeaveWindowStartedAt = Time.unscaledTime;
            this.activityAutoLeaveGaveUp = false;
            return true;
        }

        private void ActivityAutoDeclineNoteFirstSighting(ref bool latch, string what, string detail)
        {
            if (latch)
            {
                return;
            }

            latch = true;
            ModLogger.Msg("[ActivityAutoDecline] first " + what + " received this session" + detail
                + " — the hook channel is live.");
        }

        private void OnActivityInvitedEventHook(GameEventSnapshot e)
        {
            uint activityNetId = e.ReadUInt32(0);
            uint inviterNetId = e.ReadUInt32(4);

            this.ActivityAutoDeclineNoteFirstSighting(ref this.activitySawInviteEvent,
                "ActivityInvitedEvent", " (activity=" + activityNetId + " inviter=" + inviterNetId + ")");

            if (!this.activityAutoDeclineInvites)
            {
                // Let through on purpose -> whatever the player does with the ringing call is their
                // decision, so a join that follows is deliberate.
                this.activityInviteAwaitingChoice = true;
                if (MasterLogActivityAutoDecline)
                {
                    ModLogger.Msg("[ActivityAutoDecline] invite passed through activity=" + activityNetId
                        + " inviter=" + inviterNetId);
                }

                return;
            }

            this.activityDeclinedInviteCount++;
            ModLogger.Msg("[ActivityAutoDecline] invite declined activity=" + activityNetId
                + " inviter=" + inviterNetId + " (total " + this.activityDeclinedInviteCount + ")");
            this.AddMenuNotification(this.L("Event invite auto-declined"), new Color(0.45f, 0.88f, 1f));
        }

        private void OnActivityOtherRoomInvitedEventHook(GameEventSnapshot e)
        {
            this.ActivityAutoDeclineNoteFirstSighting(ref this.activitySawOtherRoomInviteEvent,
                "OtherRoomActivityInvitedEvent", string.Empty);

            if (!this.activityAutoDeclineInvites)
            {
                this.activityInviteAwaitingChoice = true;
                if (MasterLogActivityAutoDecline)
                {
                    ModLogger.Msg("[ActivityAutoDecline] cross-town invite passed through");
                }

                return;
            }

            this.activityDeclinedInviteCount++;
            ModLogger.Msg("[ActivityAutoDecline] cross-town invite declined (total "
                + this.activityDeclinedInviteCount + ")");
            this.AddMenuNotification(this.L("Event invite auto-declined"), new Color(0.45f, 0.88f, 1f));
        }

        private void OnActivityJoinSuccessEventHook(GameEventSnapshot e)
        {
            uint activityNetId = e.ReadUInt32(0);

            this.ActivityAutoDeclineNoteFirstSighting(ref this.activitySawJoinSuccessEvent,
                "OnRequestJoinActivitySuccess", " (activity=" + activityNetId + ")");

            this.activityJoinWasDeliberate = true;
            this.activityAutoLeavePending = false;
            this.ActivityAutoDeclineSetStatus("Joined event " + activityNetId
                + " on purpose — auto-leave off for it.");
        }

        private void OnActivitySelfChangedEventHook(GameEventSnapshot e)
        {
            this.ActivityAutoDeclineNoteFirstSighting(ref this.activitySawSelfChangedEvent,
                "SelfActivityChangedEvent", string.Empty);

            if (!this.activityAutoLeaveEvents)
            {
                if (MasterLogActivityAutoDecline)
                {
                    ModLogger.Msg("[ActivityAutoDecline] activity membership changed (auto-leave off)");
                }

                return;
            }

            // The payload is unreadable, so unlike the party half the in/out state is not on the
            // event — the tick resolves it from IsSelfInActivity(). Queue an evaluation and let
            // TryActivityAutoLeaveOnce decide; it settles cleanly when we are not in one.
            this.activityAutoLeavePending = true;
        }

        // Returns true when the request is settled, false when another attempt is warranted.
        private bool TryActivityAutoLeaveOnce()
        {
            this.activityAutoLeaveNextAttemptAt = Time.unscaledTime + ActivityAutoLeaveMinIntervalSec;
            this.activityAutoLeaveAttemptsInWindow++;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    this.ActivityAutoDeclineSetStatus("Mono API not ready.");
                    return false;
                }

                if (!AuraMonoGameDataLive)
                {
                    this.ActivityAutoDeclineSetStatus("Game Mono side not live yet.");
                    return false;
                }

                if (!AuraMonoPinningAvailable)
                {
                    this.ActivityAutoDeclineSetStatus("AuraMono pinning unavailable — event auto-leave disabled.");
                    return true;
                }

                if (!this.TryResolveActivityAutoDeclineMethods())
                {
                    return false;
                }

                IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.activityEventSystemClass);
                if (instance == IntPtr.Zero)
                {
                    this.ActivityAutoDeclineSetStatus("DataModule<ActivityEventSystem>.Instance null.");
                    return false;
                }

                bool inActivity;
                uint instancePin = AuraMonoPinNew(instance);
                try
                {
                    if (!this.TryInvokePartySystemBool(instance, this.activityIsSelfInActivityMethod, out inActivity))
                    {
                        this.ActivityAutoDeclineSetStatus("IsSelfInActivity() invoke failed.");
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(instancePin);
                }

                if (!inActivity)
                {
                    // Clear the per-membership latches on the TRUE->FALSE edge only. A deliberate
                    // join sets activityJoinWasDeliberate from OnRequestJoinActivitySuccess, and the
                    // SelfActivityChangedEvent that follows can be observed before
                    // IActivityEventService has published the new membership — reading a bare
                    // "false" there and clearing would throw the mark away, and the next event
                    // (now genuinely in the activity) would look server-pushed and quit the event
                    // the player just joined. Same trap the party half hit.
                    if (this.activityLastInActivity)
                    {
                        this.activityJoinWasDeliberate = false;
                        this.activityInviteAwaitingChoice = false;
                        this.activityLastInActivity = false;
                    }

                    this.ActivityAutoDeclineSetStatus("Not in an event — nothing to leave.");
                    return true;
                }

                this.activityLastInActivity = true;

                if (this.activityJoinWasDeliberate || this.activityInviteAwaitingChoice)
                {
                    this.ActivityAutoDeclineSetStatus("Joined the event on purpose — auto-leave skipped.");
                    return true;
                }

                if (!this.TryIsSelfVisitorForAutoDecline(out bool isVisitor))
                {
                    // Fail closed exactly as the party half does: without the visitor answer a
                    // deliberate cross-town join cannot be told from a server push.
                    this.ActivityAutoDeclineSetStatus("IsSelfVisitor() unavailable — not leaving.");
                    return false;
                }

                if (isVisitor)
                {
                    this.activityJoinWasDeliberate = true;
                    this.ActivityAutoDeclineSetStatus("Visiting another town — deliberate join, auto-leave skipped.");
                    return true;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.activityQuitMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.ActivityAutoDeclineSetStatus("SendQuitActivityEventCommand() raised a mono exception.");
                    return false;
                }

                this.activityAutoLeftCount++;
                this.ActivityAutoDeclineSetStatus("Left an auto-joined event (total "
                    + this.activityAutoLeftCount + ").");
                this.AddMenuNotification(this.L("Left auto-joined event"), new Color(0.45f, 1f, 0.55f));
                return true;
            }
            catch (Exception ex)
            {
                this.ActivityAutoDeclineSetStatus("Event auto-leave error: " + ex.Message);
                return false;
            }
        }

        // PartySystem.IsSelfVisitor() is town-scoped, not party-scoped, so it answers the same
        // question for activities. Shares the party half's cached pointers (same partial class);
        // both resolvers are null-guarded, so whichever half runs first fills them.
        private bool TryIsSelfVisitorForAutoDecline(out bool isVisitor)
        {
            isVisitor = false;

            if (this.partySystemClass == IntPtr.Zero)
            {
                this.partySystemClass = this.FindAuraMonoClassByFullName(PartySystemTypeName);
                if (this.partySystemClass == IntPtr.Zero)
                {
                    this.partySystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        PartySystemNamespace, PartySystemClassName);
                }
            }

            if (this.partySystemClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.partySystemIsSelfVisitorMethod == IntPtr.Zero)
            {
                this.partySystemIsSelfVisitorMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.partySystemClass, "IsSelfVisitor", 0);
            }

            if (this.partySystemIsSelfVisitorMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr partyInstance = this.TryGetAuraMonoDataModuleInstance(this.partySystemClass);
            if (partyInstance == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(partyInstance);
            try
            {
                return this.TryInvokePartySystemBool(partyInstance, this.partySystemIsSelfVisitorMethod, out isVisitor);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private bool TryResolveActivityAutoDeclineMethods()
        {
            if (this.activityEventSystemClass == IntPtr.Zero)
            {
                this.activityEventSystemClass = this.FindAuraMonoClassByFullName(ActivityEventSystemTypeName);
                if (this.activityEventSystemClass == IntPtr.Zero)
                {
                    this.activityEventSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        ActivityEventSystemNamespace, ActivityEventSystemClassName);
                }
            }

            if (this.activityEventSystemClass == IntPtr.Zero)
            {
                this.ActivityAutoDeclineSetStatus("ActivityEventSystem class not found.");
                return false;
            }

            if (this.activityIsSelfInActivityMethod == IntPtr.Zero)
            {
                this.activityIsSelfInActivityMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.activityEventSystemClass, "IsSelfInActivity", 0);
            }

            if (this.activityIsSelfInActivityMethod == IntPtr.Zero)
            {
                this.ActivityAutoDeclineSetStatus("ActivityEventSystem.IsSelfInActivity(0) not found.");
                return false;
            }

            if (this.activityQuitMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName(ActivityProtocolManagerTypeName);
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        ActivityProtocolManagerNamespace, ActivityProtocolManagerClassName);
                }

                if (protocolClass == IntPtr.Zero)
                {
                    this.ActivityAutoDeclineSetStatus("ActivityEventProtocolManager class not found.");
                    return false;
                }

                // Static, zero args; builds QuitActivityEventCommand and hands it to
                // WebRequestUtility.SendCommand<T> inside normally-JIT'd game code.
                this.activityQuitMethod =
                    this.FindAuraMonoMethodOnHierarchy(protocolClass, "SendQuitActivityEventCommand", 0);
                if (this.activityQuitMethod == IntPtr.Zero)
                {
                    this.ActivityAutoDeclineSetStatus("SendQuitActivityEventCommand(0) not found.");
                    return false;
                }
            }

            return true;
        }

        internal string GetActivityAutoDeclineCounters()
        {
            return this.LF("Party {0}/{1} · Events {2}/{3}  (declined/left)",
                this.partyDeclinedInviteCount, this.partyAutoLeftCount,
                this.activityDeclinedInviteCount, this.activityAutoLeftCount);
        }

        // Writes into the shared status line the party half owns, so the Privacy tab needs only one
        // row for both; the log prefix stays distinct so the two are still separable there.
        private void ActivityAutoDeclineSetStatus(string status)
        {
            this.partyAutoDeclineStatus = status;
            if (string.Equals(status, this.activityAutoDeclineLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.activityAutoDeclineLastLoggedStatus = status;
            ModLogger.Msg("[ActivityAutoDecline] " + status);
        }
    }
}
