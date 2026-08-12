using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Party Auto-Decline — two independent switches over the game's party plumbing. They solve two
    // different problems and are deliberately NOT one toggle: suppressing invites is side-effect
    // free, auto-leaving talks to the server.
    //
    // 1) AUTO-DECLINE INVITES (partyAutoDeclineInvites).
    //    An invite is not a dialog, it is a PHONE CALL. The chain is
    //        server InvitePlayerToPartyEvent / OtherTownInviteToPartyEvent
    //          -> PartyClientService.InvitePlayerToParty
    //          -> PartyProtocolManager.BeInvitedToParty
    //          -> EventCenter.DispatchEvent(PartyInvitedEvent)          (global)
    //          -> PartyModule.NoticeInvited -> PhoneModule.AddCall(PartyCallData)
    //          -> 30 s ringing overlay, then a missed-call entry + red point.
    //
    //    LEVER: suppress the dispatch. PartyModule.NoticeInvited is the only thing standing between
    //    the event and the phone, so a swallowed dispatch means no call is ever built — no ring, no
    //    overlay, no missed-call red point. Same mechanism ShowOffBypassFeature and
    //    TutorialBlockFeature already use in production.
    //
    //    WHY NOT "answer and hang up": PhoneModule.DropCall would be the polite route, but
    //    PhoneModule is a ViewModule with no static Instance, so reaching it goes through
    //    Managers.GetModule(Type) — a documented hard-crash path (memory:
    //    auramono-viewmodule-resolve-typecrash) — and it would still flash a frame of UI.
    //
    //    NOTHING IS SENT TO THE SERVER, and nothing needs to be: PhoneModule.DropCall only emits a
    //    reject for EventCallData (activities) and MultiBuildCallData (joint building); the
    //    PartyCallData branch is empty. The game has no "reject party invite" command at all, so
    //    from the server's point of view a suppressed invite is indistinguishable from a missed
    //    call — exactly what happens today when the 30 s timer runs out.
    //
    //    KNOWN TRADE-OFF: PartyModule.NoticeInvited is also what feeds _canJoinPartyIds, which
    //    gates PartyModule.CanJoinToPrivateParty. While this toggle is on, a PRIVATE party you were
    //    invited to cannot be joined by hand — the invite never reaches the client. Open parties and
    //    the party festival are unaffected.
    //
    // 2) AUTO-LEAVE (partyAutoLeaveParties).
    //    Walking into a party's home area joins you to it. That is 100% SERVER-SIDE: the client's
    //    PartyClientService.CheckPlayerPartyArea (a 500 ms tick from PartyClientSystem.Run) only
    //    raises the LOCAL PlayerEnterPartyTriggerAreaEvent / PlayerEnterPartyExtendAreaEvent — it
    //    sends no join. The membership itself arrives as JoinPartyTipsEvent and as an
    //    ActivityParticipantComponent on the player entity. There is no client lever to refuse it,
    //    so the only possible design is to leave after the fact.
    //
    //    LEVER: PartyProtocolManager.LeaveParty() — the exact static the in-game Exit button calls
    //    (PartyMembersPanel.ExitParty; everything else there is the confirm dialog and CloseSelf).
    //    Because it is the same call, it inherits the same server-side effect: a party you left
    //    deliberately does not re-add you when you walk back into its area.
    //
    //    TELLING A DELIBERATE JOIN FROM A SERVER-PUSHED ONE is the whole difficulty — without it the
    //    feature would undo the player's own joins. Two independent signals, both event-cheap:
    //      * ApplyPartyGameResultEvent{errorCode == Success} — dispatched by
    //        PartyProtocolManager.ApplyPartyGameResult, which only ever runs in response to the
    //        server's ApplyPartyTipsEvent, which the server only sends because WE sent an
    //        ApplyPartyGameNetworkCommand. An area auto-add produces JoinPartyTipsEvent instead and
    //        never lands here. It is dispatched immediately BEFORE UpdateSelfPartyInfo(), so the
    //        mark is always set before the membership event it belongs to (the ring buffer drains
    //        in dispatch order on the same frame).
    //      * PartySystem.IsSelfVisitor() — you can only be a visitor in someone else's town by
    //        having deliberately joined a cross-town party (SetOtherTownPartyData is called from
    //        the two party-info panels only, i.e. by a click). This covers the party-festival paths
    //        that reach a party without going through ApplyPartyGame.
    //
    //    PING-PONG GUARD: the server-side "left deliberately" memory should make a single leave
    //    enough, but it is an inherited assumption, not something the client can verify. Edge cases
    //    stay possible (the party is recreated with a fresh netId, you are re-invited and re-added,
    //    the server restarts). A leave/join loop at the area tick's 500 ms would spam
    //    LeavePartyTipsEvent toasts and is a textbook automation signature, so attempts are capped:
    //    min PartyAutoLeaveMinIntervalSec between them, at most PartyAutoLeaveMaxAttemptsPerWindow
    //    inside PartyAutoLeaveWindowSec, then the feature gives up and says so. In normal operation
    //    exactly one attempt is ever spent.
    //
    //    PartyErrorCode.NotAllowLeaveParty (21) — the server can refuse a leave outright (hide &
    //    seek in progress). That is not distinguishable here without reading the response, so it
    //    simply burns the attempt budget and lands in the give-up state, which is the right
    //    outcome: nothing the mod can do about it.
    //
    // Everything is event-driven. The per-frame method only latches registrations, mirrors the
    // suppression flags and services the attempt timer — no scans, no polls, no Ensure* retry loop.
    public partial class HeartopiaComplete
    {
        // uint partyNetId @0, uint inviterNetId @4.
        private const string PartyInvitedEventName = "XDTDataAndProtocol.Events.PartyInvitedEvent";
        private const int PartyInvitedEventPayloadBytes = 8;

        // Payload is OtherTownPartyInviteInfo — strings, Guids, List<int>, Dictionary — so nothing
        // in it is readable by offset. Registered with 0 bytes purely to own a suppression slot.
        private const string PartyOtherRoomInvitedEventName = "XDTDataAndProtocol.Events.OtherRoomPartyInvitedEvent";
        private const int PartyOtherRoomInvitedEventPayloadBytes = 0;

        // bool inParty @0 — dispatched by PartyModule.RefreshPartyPanelVisibility on every
        // SelfPartyChangedEvent. Chosen over SelfPartyChangedEvent itself, whose fields are nullable
        // structs and unreadable by offset.
        private const string PartyMembershipChangedEventName = "XDTGameSystem.UI.PartyMembershipChangedEvent";
        private const int PartyMembershipChangedEventPayloadBytes = 1;

        // PartyErrorCode errorCode (int) @0, uint partyNetId @4.
        private const string PartyApplyResultEventName = "XDTDataAndProtocol.Events.ApplyPartyGameResultEvent";
        private const int PartyApplyResultEventPayloadBytes = 8;

        // PartyErrorCode.Success.
        private const int PartyErrorCodeSuccess = 0;

        private const string PartySystemTypeName = "XDTGameSystem.GameplaySystem.Party.PartySystem";
        private const string PartySystemNamespace = "XDTGameSystem.GameplaySystem.Party";
        private const string PartySystemClassName = "PartySystem";

        private const string PartyProtocolManagerTypeName = "XDTDataAndProtocol.ProtocolService.Party.PartyProtocolManager";
        private const string PartyProtocolManagerNamespace = "XDTDataAndProtocol.ProtocolService.Party";
        private const string PartyProtocolManagerClassName = "PartyProtocolManager";

        private const float PartyAutoLeaveMinIntervalSec = 3f;
        private const int PartyAutoLeaveMaxAttemptsPerWindow = 3;
        private const float PartyAutoLeaveWindowSec = 60f;

        internal static bool MasterLogPartyAutoDecline = false;

        private bool partyAutoDeclineInvites;
        private bool partyAutoLeaveParties;

        private bool partyAutoDeclineRegistered;
        private int partyDeclinedInviteCount;
        private int partyAutoLeftCount;

        // Latched for the lifetime of one membership: set by our own apply-success (or by the
        // visitor check), cleared the moment the party ends. No netId bookkeeping needed — you can
        // only be in one party at a time, so the flag and the membership rise and fall together.
        private bool partyJoinWasDeliberate;

        // Last membership state seen, so the mark above is cleared on the true->false EDGE only.
        private bool partyLastInParty;

        private bool partyAutoLeavePending;
        private float partyAutoLeaveNextAttemptAt;
        private int partyAutoLeaveAttemptsInWindow;
        private float partyAutoLeaveWindowStartedAt;
        private bool partyAutoLeaveGaveUp;

        // Class/method pointers only — stable for the image lifetime, never a MonoObject* (CI lint
        // E3). The PartySystem instance is level-scoped and re-resolved on every use.
        private IntPtr partySystemClass = IntPtr.Zero;
        private IntPtr partySystemIsSelfInPartyMethod = IntPtr.Zero;
        private IntPtr partySystemIsSelfVisitorMethod = IntPtr.Zero;
        private IntPtr partyLeavePartyMethod = IntPtr.Zero;

        private string partyAutoDeclineStatus = "Idle.";
        private string partyAutoDeclineLastLoggedStatus;

        private void ProcessPartyAutoDeclineOnUpdate()
        {
            this.EnsurePartyAutoDeclineRegistrations();

            // Cheap and idempotent — the engine only writes the slot array when the value differs
            // from what the entry already carries.
            bool decline = this.partyAutoDeclineInvites;
            this.SetGameEventHookSuppressForward(PartyInvitedEventName, decline);
            this.SetGameEventHookSuppressForward(PartyOtherRoomInvitedEventName, decline);

            if (!this.partyAutoLeavePending)
            {
                return;
            }

            if (!this.partyAutoLeaveParties)
            {
                this.partyAutoLeavePending = false;
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.partyAutoLeaveNextAttemptAt)
            {
                return;
            }

            // A quiet window rearms the budget, so a legitimate second auto-join hours later is not
            // punished by an earlier burst.
            if (now - this.partyAutoLeaveWindowStartedAt > PartyAutoLeaveWindowSec)
            {
                this.partyAutoLeaveWindowStartedAt = now;
                this.partyAutoLeaveAttemptsInWindow = 0;
                this.partyAutoLeaveGaveUp = false;
            }

            if (this.partyAutoLeaveGaveUp)
            {
                this.partyAutoLeavePending = false;
                return;
            }

            if (this.partyAutoLeaveAttemptsInWindow >= PartyAutoLeaveMaxAttemptsPerWindow)
            {
                this.partyAutoLeaveGaveUp = true;
                this.partyAutoLeavePending = false;
                // Short line for the single-line status label; the reason goes to the log in full.
                this.PartyAutoDeclineSetStatus("Gave up after " + PartyAutoLeaveMaxAttemptsPerWindow + " attempts.");
                ModLogger.Msg("[PartyAutoDecline] " + PartyAutoLeaveMaxAttemptsPerWindow
                    + " leave attempts inside " + PartyAutoLeaveWindowSec
                    + "s did not stick — the server keeps re-adding you (party recreated with a new"
                    + " netId, a fresh invite, or the leave was refused outright, e.g."
                    + " PartyErrorCode.NotAllowLeaveParty during hide & seek). Walk out of the party"
                    + " area; the budget rearms after a quiet window.");
                this.AddMenuNotification(
                    this.L("Party auto-leave failed — leave the party area on foot"),
                    new Color(1f, 0.55f, 0.45f));
                return;
            }

            // Resolving game types before a world exists fails at best and AVs at worst
            // (AGENTS.md world-ready rule). No retry timer here — the pending flag plus the
            // interval above already is the retry, bounded by the attempt budget.
            if (!this.IsWorldReady)
            {
                return;
            }

            if (this.TryPartyAutoLeaveOnce())
            {
                this.partyAutoLeavePending = false;
            }
        }

        private void EnsurePartyAutoDeclineRegistrations()
        {
            if (this.partyAutoDeclineRegistered)
            {
                return;
            }

            this.partyAutoDeclineRegistered = true;

            // Four slots. Registration is metadata only — the detours are spliced by the
            // world-ready gate, so this is safe to run at startup.
            bool inviteOk = this.RegisterGameEventHook(
                PartyInvitedEventName, PartyInvitedEventPayloadBytes, this.OnPartyInvitedEventHook);
            bool otherRoomOk = this.RegisterGameEventHook(
                PartyOtherRoomInvitedEventName, PartyOtherRoomInvitedEventPayloadBytes,
                this.OnPartyOtherRoomInvitedEventHook);
            bool membershipOk = this.RegisterGameEventHook(
                PartyMembershipChangedEventName, PartyMembershipChangedEventPayloadBytes,
                this.OnPartyMembershipChangedEventHook);
            bool applyOk = this.RegisterGameEventHook(
                PartyApplyResultEventName, PartyApplyResultEventPayloadBytes,
                this.OnPartyApplyResultEventHook);

            // Not gated on MasterLog: a refused registration (slot pool exhausted) silently turns
            // the feature into a no-op, which the user would otherwise diagnose by guesswork.
            if (!inviteOk || !otherRoomOk || !membershipOk || !applyOk)
            {
                ModLogger.Msg("[PartyAutoDecline] hook registration incomplete — invite=" + inviteOk
                    + " otherRoom=" + otherRoomOk + " membership=" + membershipOk + " apply=" + applyOk);
            }
            else if (MasterLogPartyAutoDecline)
            {
                ModLogger.Msg("[PartyAutoDecline] 4 hooks registered");
            }

            this.RegisterWorldReadyCallback("PartyAutoDecline", this.OnPartyAutoDeclineWorldReady);
        }

        // A new world means a new PartySystem and no membership carried over the loading screen.
        // The deliberate mark is cleared too: for a cross-town join the ApplyPartyGame that sets it
        // runs from PartySystem.OnAfterLevelLoaded, i.e. AFTER this callback, so nothing is lost.
        private bool OnPartyAutoDeclineWorldReady()
        {
            this.partyJoinWasDeliberate = false;
            this.partyLastInParty = false;
            this.partyAutoLeavePending = false;
            this.partyAutoLeaveNextAttemptAt = 0f;
            this.partyAutoLeaveAttemptsInWindow = 0;
            this.partyAutoLeaveWindowStartedAt = Time.unscaledTime;
            this.partyAutoLeaveGaveUp = false;
            return true;
        }

        private void OnPartyInvitedEventHook(GameEventSnapshot e)
        {
            uint partyNetId = e.ReadUInt32(0);
            uint inviterNetId = e.ReadUInt32(4);

            if (!this.partyAutoDeclineInvites)
            {
                if (MasterLogPartyAutoDecline)
                {
                    ModLogger.Msg("[PartyAutoDecline] invite passed through party=" + partyNetId
                        + " inviter=" + inviterNetId);
                }

                return;
            }

            this.partyDeclinedInviteCount++;
            ModLogger.Msg("[PartyAutoDecline] invite declined party=" + partyNetId
                + " inviter=" + inviterNetId + " (total " + this.partyDeclinedInviteCount + ")");
            this.AddMenuNotification(this.L("Party invite auto-declined"), new Color(0.45f, 0.88f, 1f));
        }

        private void OnPartyOtherRoomInvitedEventHook(GameEventSnapshot e)
        {
            if (!this.partyAutoDeclineInvites)
            {
                if (MasterLogPartyAutoDecline)
                {
                    ModLogger.Msg("[PartyAutoDecline] cross-town invite passed through");
                }

                return;
            }

            this.partyDeclinedInviteCount++;
            ModLogger.Msg("[PartyAutoDecline] cross-town invite declined (total "
                + this.partyDeclinedInviteCount + ")");
            this.AddMenuNotification(this.L("Party invite auto-declined"), new Color(0.45f, 0.88f, 1f));
        }

        // Fires only for a join WE asked for (see the header note). Marks the membership that is
        // about to be announced as deliberate so auto-leave keeps its hands off it.
        private void OnPartyApplyResultEventHook(GameEventSnapshot e)
        {
            int errorCode = e.ReadInt32(0);
            uint partyNetId = e.ReadUInt32(4);

            if (errorCode != PartyErrorCodeSuccess)
            {
                if (MasterLogPartyAutoDecline)
                {
                    ModLogger.Msg("[PartyAutoDecline] apply rejected code=" + errorCode
                        + " party=" + partyNetId);
                }

                return;
            }

            this.partyJoinWasDeliberate = true;
            this.partyAutoLeavePending = false;
            this.PartyAutoDeclineSetStatus("Joined party " + partyNetId + " on purpose — auto-leave off for it.");
        }

        private void OnPartyMembershipChangedEventHook(GameEventSnapshot e)
        {
            bool inParty = e.ReadBool(0);

            if (!inParty)
            {
                // Clear the deliberate mark on the TRUE->FALSE edge only, never on a bare "false"
                // report. ApplyPartyGameResult dispatches its event and then calls
                // UpdateSelfPartyInfo(), and PartyModule re-reads IsSelfInParty() at that moment —
                // if the participant component has not landed yet, that first membership event says
                // false. A level-triggered clear would throw away the mark we set microseconds
                // earlier and then auto-leave the join the player just made by hand.
                if (this.partyLastInParty)
                {
                    this.partyJoinWasDeliberate = false;
                }

                this.partyLastInParty = false;
                this.partyAutoLeavePending = false;
                return;
            }

            this.partyLastInParty = true;

            if (this.partyJoinWasDeliberate)
            {
                return;
            }

            if (!this.partyAutoLeaveParties)
            {
                if (MasterLogPartyAutoDecline)
                {
                    ModLogger.Msg("[PartyAutoDecline] server-side party join seen (auto-leave off)");
                }

                return;
            }

            ModLogger.Msg("[PartyAutoDecline] server-side party join detected — leaving");
            this.partyAutoLeavePending = true;
        }

        // Returns true when the request is settled (left, or established that we must not leave) and
        // false when it is worth another attempt from the budget.
        private bool TryPartyAutoLeaveOnce()
        {
            this.partyAutoLeaveNextAttemptAt = Time.unscaledTime + PartyAutoLeaveMinIntervalSec;
            this.partyAutoLeaveAttemptsInWindow++;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    this.PartyAutoDeclineSetStatus("Mono API not ready.");
                    return false;
                }

                // Raw reads against game statics AV before login.
                if (!AuraMonoGameDataLive)
                {
                    this.PartyAutoDeclineSetStatus("Game Mono side not live yet.");
                    return false;
                }

                // The instance is held across two invokes below; without pinning the moving sgen GC
                // can relocate it between them.
                if (!AuraMonoPinningAvailable)
                {
                    this.PartyAutoDeclineSetStatus("AuraMono pinning unavailable — auto-leave disabled.");
                    return true;
                }

                if (!this.TryResolvePartyAutoDeclineMethods())
                {
                    return false;
                }

                // Level-scoped singleton — resolved fresh, never cached across frames.
                IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.partySystemClass);
                if (instance == IntPtr.Zero)
                {
                    this.PartyAutoDeclineSetStatus("DataModule<PartySystem>.Instance null.");
                    return false;
                }

                bool inParty;
                bool isVisitor;
                uint instancePin = AuraMonoPinNew(instance);
                try
                {
                    if (!this.TryInvokePartySystemBool(instance, this.partySystemIsSelfInPartyMethod, out inParty))
                    {
                        this.PartyAutoDeclineSetStatus("IsSelfInParty() invoke failed.");
                        return false;
                    }

                    if (!inParty)
                    {
                        // The membership evaporated while the attempt was queued.
                        this.PartyAutoDeclineSetStatus("Not in a party any more — nothing to leave.");
                        return true;
                    }

                    if (!this.TryInvokePartySystemBool(instance, this.partySystemIsSelfVisitorMethod, out isVisitor))
                    {
                        // Fail closed: without the visitor answer we cannot tell a deliberate
                        // cross-town join from a server push, and leaving the wrong one drags the
                        // player out of the town they are visiting.
                        this.PartyAutoDeclineSetStatus("IsSelfVisitor() invoke failed — not leaving.");
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(instancePin);
                }

                if (isVisitor)
                {
                    // Visitor status is only reachable by deliberately joining a cross-town party,
                    // so this membership is the player's own doing.
                    this.partyJoinWasDeliberate = true;
                    this.PartyAutoDeclineSetStatus("Visiting another town — deliberate join, auto-leave skipped.");
                    return true;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.partyLeavePartyMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.PartyAutoDeclineSetStatus("LeaveParty() raised a mono exception.");
                    return false;
                }

                this.partyAutoLeftCount++;
                this.PartyAutoDeclineSetStatus("Left an auto-joined party (total " + this.partyAutoLeftCount + ").");
                this.AddMenuNotification(this.L("Left auto-joined party"), new Color(0.45f, 1f, 0.55f));
                return true;
            }
            catch (Exception ex)
            {
                this.PartyAutoDeclineSetStatus("Auto-leave error: " + ex.Message);
                return false;
            }
        }

        private bool TryResolvePartyAutoDeclineMethods()
        {
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
                this.PartyAutoDeclineSetStatus("PartySystem class not found.");
                return false;
            }

            // Param count 0 is load-bearing on both: IsSelfInParty is overloaded with
            // IsSelfInParty(int areaId), and a first-match resolve would grab the wrong one.
            if (this.partySystemIsSelfInPartyMethod == IntPtr.Zero)
            {
                this.partySystemIsSelfInPartyMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.partySystemClass, "IsSelfInParty", 0);
            }

            if (this.partySystemIsSelfVisitorMethod == IntPtr.Zero)
            {
                this.partySystemIsSelfVisitorMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.partySystemClass, "IsSelfVisitor", 0);
            }

            if (this.partySystemIsSelfInPartyMethod == IntPtr.Zero
                || this.partySystemIsSelfVisitorMethod == IntPtr.Zero)
            {
                this.PartyAutoDeclineSetStatus("PartySystem.IsSelfInParty(0)/IsSelfVisitor(0) not found.");
                return false;
            }

            if (this.partyLeavePartyMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName(PartyProtocolManagerTypeName);
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        PartyProtocolManagerNamespace, PartyProtocolManagerClassName);
                }

                if (protocolClass == IntPtr.Zero)
                {
                    this.PartyAutoDeclineSetStatus("PartyProtocolManager class not found.");
                    return false;
                }

                // Static, zero args. It builds LeavePartyCommand and hands it to
                // WebRequestUtility.SendCommand<T> inside normally-JIT'd game code — we never
                // inflate a generic ourselves.
                this.partyLeavePartyMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "LeaveParty", 0);
                if (this.partyLeavePartyMethod == IntPtr.Zero)
                {
                    this.PartyAutoDeclineSetStatus("PartyProtocolManager.LeaveParty(0) not found.");
                    return false;
                }
            }

            return true;
        }

        // Zero-arg instance predicate; mono hands the bool back boxed.
        private bool TryInvokePartySystemBool(IntPtr instance, IntPtr method, out bool value)
        {
            value = false;
            if (instance == IntPtr.Zero || method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instance, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        internal string GetPartyAutoDeclineStatus()
        {
            return this.partyAutoDeclineStatus;
        }

        private void PartyAutoDeclineSetStatus(string status)
        {
            this.partyAutoDeclineStatus = status;
            if (string.Equals(status, this.partyAutoDeclineLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.partyAutoDeclineLastLoggedStatus = status;
            ModLogger.Msg("[PartyAutoDecline] " + status);
        }
    }
}
