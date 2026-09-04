using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Activity Reward Auto-Claim — takes the rewards of a player-hosted ActivityEvent the moment it
    // ends, so the "Claim Rewards" button on PartyMembersPanel never has to be pressed.
    //
    // WHAT THE BUTTON ACTUALLY DOES. `PartyMembersPanel.ClaimReward` does NOT claim — it only opens
    // `EventRewardPanel`. The claim lives in that panel and is two independent static sends:
    //     ActivityEventProtocolManager.SendGetActivityEventRewardCommand(Personal, info.NetId)
    //     ActivityEventProtocolManager.SendGetActivityEventRewardCommand(Team,     info.NetId)
    // We call the MANAGER, never build `GetActivityRewardCommand` ourselves: its `RewardType` is an
    // enum field and TrySetObjectMember cannot set enums without Enum.ToObject — the lesson the
    // BattlePass issue rewards already paid for.
    //
    // WHERE netId AND staticId COME FROM. Every obvious accessor on ActivityEventSystem is on a
    // banned shape: `TryGetCurActivityInfo(out ActivityEventInfo)` is an out-param on a 128-byte
    // value type (the documented stack smash), and `GetActivityInfoByStaticId` returns
    // `ActivityEventInfo?` — a Nullable<T> instantiation, which is the generic-invoke crash path.
    // `GetAllActivityEventInfo()` returns a plain `List<ActivityEventInfo>` and IS safe, but it is
    // useless at end time: ActivityEventClientSystem raises the end with `needWithDestroy: true`,
    // so the entity may already carry NetworkEntityDestroyedTag by then and the list — which
    // defaults to needWithDestroy=false — filters it out. The event payload is the only source that
    // still holds an ended activity's ids, so that is what the trigger reads.
    //
    // LAYOUT. `ActivityEventInfo` was measured on the live build and keeps declaration order, size
    // 128 - NetId(uint)@0, StaticId(int)@4, StatusType(int enum)@24; the string/int[]/List fields
    // were NOT hoisted to the front. What was NOT measured is how `Nullable<ActivityEventInfo>`
    // wraps it, and the first live run showed that mattering - see the trigger's own note. Both
    // candidate layouts are read and the game's own reward gate arbitrates.
    //
    // TRIGGER COSTS ZERO HOOK SLOTS. `SelfActivityChangedEvent` is already hooked by the auto-
    // decline half at payloadBytes 0; RegisterGameEventHookInternal appends our handler to the same
    // entry and grows PayloadBytes live, so that half keeps working untouched and we get 64 bytes.
    // The event is the right one: ActivityEventModule.NoticeSelfActivity - the listener that opens
    // the end panel - gates that panel on `evt.endEventInfo.HasValue` from this very dispatch, so
    // whatever reaches the panel reaches us, one handler earlier.
    //
    // FAILURES ARE SILENT. `ActivityRewardRefreshEvent` is dispatched ONLY on
    // ActivityErrorCode.Success; a rejection goes to ShowErrorToast and never reaches us. So a
    // claim is judged by the ABSENCE of an ack inside a window, not by an error.
    //
    // SECOND, SEPARATE TOGGLE: hide the end panel. `ActivityEventEndingNoticeRequestedEvent` has
    // exactly one listener (UIEventBridge.OnActivityEventEndingNoticeRequested) whose body only
    // opens PartySoonPanel(Finish) and, when the event's `isOpenResult` is set, wires the callback
    // that opens PartyMembersPanel. Suppressing the dispatch removes both and nothing else.
    // Deliberately NOT tied to auto-claim: that panel is also the only manual way to claim, so
    // hiding it is the player's call, not a side effect of automation.
    public partial class HeartopiaComplete
    {
        // `ActivityEventRewardType rewardType` @0. Success-only, and it carries no netId.
        private const string ActivityRewardRefreshEventName = "XDTDataAndProtocol.Events.ActivityRewardRefreshEvent";
        private const int ActivityRewardRefreshEventPayloadBytes = 4;

        // Re-registration size for the auto-decline half's SelfActivityChangedEvent. EventPayloadCap
        // is 64 and every field we need sits well inside that.
        private const int ActivityRewardSelfChangedPayloadBytes = 64;

        // Bare offsets inside SelfActivityChangedEvent under each candidate Nullable layout — see
        // the note on the trigger. Both are read; the reward gate decides which was right.
        private const int ActivityRewardNetIdOffsetCorefx = 8;   // hasValue first, then the struct
        private const int ActivityRewardStaticIdOffsetCorefx = 12;
        private const int ActivityRewardNetIdOffsetMono = 0;     // struct first, has_value trailing
        private const int ActivityRewardStaticIdOffsetMono = 4;

        // A handful is enough to settle the layout; after that the log stays quiet.
        private const int ActivityRewardMaxPayloadDumps = 12;

        // Single listener, body opens PartySoonPanel(Finish) + optionally PartyMembersPanel.
        // Payload starts with a string reference - nothing is read.
        private const string ActivityEndNoticeEventName = "XDTGameSystem.UI.ActivityEventEndingNoticeRequestedEvent";
        private const int ActivityEndNoticeEventPayloadBytes = 0;

        // ActivityEventRewardType { Null=0, Personal=1, Team=2, All=3 }.
        private const int ActivityRewardTypePersonal = 1;
        private const int ActivityRewardTypeTeam = 2;

        // ActivityStatusType { Null, Preparing, Playing, Ending, CanOrganize }.
        private const int ActivityRewardStatusEnding = 3;

        private const float ActivityRewardPollIntervalSec = 2f;
        private const float ActivityRewardJobLifetimeSec = 120f;
        private const float ActivityRewardAckWaitSec = 10f;
        private const int ActivityRewardMaxSendsPerType = 2;

        internal static bool MasterLogActivityRewardClaim = false;

        private bool activityRewardAutoClaim;
        private bool activityRewardHooksRegistered;
        private bool activityRewardSweepPending;
        private bool activityRewardLastToggleState;
        private bool activityRewardDisabledByPinning;
        private bool activityHideEndPanel;
        private int activityRewardClaimedCount;
        private int activityRewardDumpsLogged;
        private float activityRewardNextPollAt;
        private string activityRewardStatus = "Idle.";
        private string activityRewardLastLoggedStatus;

        // Class/method pointers only — the DataModule instance is level-scoped and re-resolved on
        // every use, exactly as the auto-decline half does it.
        private IntPtr activityRewardGetPersonalGoalIdMethod = IntPtr.Zero;
        private IntPtr activityRewardCanGetRewardMethod = IntPtr.Zero;
        private IntPtr activityRewardCheckClaimedMethod = IntPtr.Zero;
        private IntPtr activityRewardCheckPlayerInActivityMethod = IntPtr.Zero;
        private IntPtr activityRewardGetAllInfoMethod = IntPtr.Zero;
        private IntPtr activityRewardSendMethod = IntPtr.Zero;

        private readonly List<ActivityRewardJob> activityRewardJobs = new List<ActivityRewardJob>();
        private readonly Dictionary<int, int> activityRewardPersonalGoalIds = new Dictionary<int, int>();

        // One ended activity we are still trying to drain. At most one is normally live; the list
        // exists because two ends can overlap if the player leaves one event as another finishes.
        private sealed class ActivityRewardJob
        {
            public uint NetId;
            public int StaticId;
            public float ExpiresAt;
            public bool PersonalDone;
            public bool TeamDone;
            public int PersonalSends;
            public int TeamSends;
            public float PersonalSentAt;
            public float TeamSentAt;
        }

        private void ProcessActivityRewardAutoClaimOnUpdate()
        {
            bool on = this.activityRewardAutoClaim;
            if (!on && !this.activityHideEndPanel && !this.activityRewardHooksRegistered)
            {
                // Neither half ever enabled this session: do not burn shared hook slots on them.
                return;
            }

            this.EnsureActivityRewardHooks();
            this.SetGameEventHookSuppressForward(ActivityEndNoticeEventName, this.activityHideEndPanel);

            if (on != this.activityRewardLastToggleState)
            {
                this.activityRewardLastToggleState = on;
                FeatureLog.Toggle("ActivityReward", on);
                if (on)
                {
                    // Events give transitions, never state — a toggle flipped on mid-session has to
                    // look at what is already ending.
                    this.activityRewardSweepPending = true;
                    this.activityRewardDisabledByPinning = false;
                }
                else
                {
                    this.activityRewardJobs.Clear();
                    this.activityRewardSweepPending = false;
                    this.ActivityRewardSetStatus("Off.");
                }
            }

            if (!on || this.activityRewardDisabledByPinning)
            {
                return;
            }

            if (Time.unscaledTime < this.activityRewardNextPollAt)
            {
                return;
            }

            this.activityRewardNextPollAt = Time.unscaledTime + ActivityRewardPollIntervalSec;

            if (this.activityRewardSweepPending)
            {
                // Resolving game types before a world exists fails at best and AVs at worst, so the
                // sweep waits for the gate rather than spinning its own retry timer.
                if (!this.IsWorldReady)
                {
                    return;
                }

                if (this.TryActivityRewardSweep())
                {
                    this.activityRewardSweepPending = false;
                }
            }

            if (this.activityRewardJobs.Count > 0)
            {
                this.PumpActivityRewardJobs();
            }
        }

        private void EnsureActivityRewardHooks()
        {
            if (this.activityRewardHooksRegistered)
            {
                return;
            }

            this.activityRewardHooksRegistered = true;

            // Second handler on an event the auto-decline half already owns: no new slot, and its
            // payload grows from 0 to 64 for both of us. That half ignores the payload entirely.
            bool endOk = this.RegisterGameEventHook(
                ActivitySelfChangedEventName,
                ActivityRewardSelfChangedPayloadBytes,
                this.OnActivityRewardEndEventHook);

            bool ackOk = this.RegisterGameEventHook(
                ActivityRewardRefreshEventName,
                ActivityRewardRefreshEventPayloadBytes,
                this.OnActivityRewardRefreshEventHook);

            // Registered so the suppress flag has a slot to live in; the payload is never read.
            bool hideOk = this.RegisterGameEventHook(
                ActivityEndNoticeEventName,
                ActivityEndNoticeEventPayloadBytes,
                this.OnActivityEndNoticeEventHook);

            // A refused hook degrades this feature silently — with the toggle on, rewards would
            // simply never be claimed and it would read as "the feature is broken". Name it.
            if (!endOk || !ackOk || !hideOk)
            {
                ModLogger.Warning("[ActivityReward] hook registration incomplete — endTrigger=" + endOk
                    + " ack=" + ackOk + " hidePanel=" + hideOk
                    + "; rewards will not be claimed automatically.");
            }

            // Runs once per world load: jobs from the previous world are meaningless, and a relog
            // during an event's Ending phase is exactly what the sweep exists for.
            this.RegisterWorldReadyCallback("ActivityRewardAutoClaim", this.OnActivityRewardWorldReady);
        }

        private bool OnActivityRewardWorldReady()
        {
            this.activityRewardJobs.Clear();
            this.activityRewardPersonalGoalIds.Clear();
            this.activityRewardSweepPending = this.activityRewardAutoClaim;
            return true;
        }

        // Trigger.
        //
        // WHY THIS NO LONGER TRUSTS A SINGLE LAYOUT. The first live run produced no "activity
        // ended" line at all even though the game's own end panel opened — and that panel rides the
        // exact same field (ActivityEventModule.NoticeSelfActivity gates OnStopActivity on
        // `evt.endEventInfo.HasValue`). So either no end happened while the toggle was on, or the
        // Nullable layout is not what was assumed. The offsets came from describing the OPEN
        // generic `System.Nullable`1` and inferring the closed instantiation, which is exactly the
        // step that can be wrong: Mono's own nullable layout puts `value` FIRST and `has_value`
        // after it, which for a 128-byte T would push has_value past the 64-byte snapshot entirely.
        //
        // Rather than guess again, both candidate layouts are read and BOTH are offered as jobs:
        //   A. corefx order — hasValue@0, so NetId@8, StaticId@12
        //   B. mono order   — value@0, so NetId@0, StaticId@4 (has_value out of reach, ignored)
        // A wrong pair cannot do damage: nothing is ever sent until CanGetReward — the very gate the
        // game puts on its own claim button — says yes for that netId, and it never will for a
        // misread one. The job simply expires. Meanwhile the first bytes are dumped so the next real
        // end settles the layout from evidence instead of inference.
        private void OnActivityRewardEndEventHook(GameEventSnapshot e)
        {
            if (!this.activityRewardAutoClaim)
            {
                return;
            }

            uint netIdA = e.ReadUInt32(ActivityRewardNetIdOffsetCorefx);
            int staticIdA = e.ReadInt32(ActivityRewardStaticIdOffsetCorefx);
            uint netIdB = e.ReadUInt32(ActivityRewardNetIdOffsetMono);
            int staticIdB = e.ReadInt32(ActivityRewardStaticIdOffsetMono);

            if (this.activityRewardDumpsLogged < ActivityRewardMaxPayloadDumps)
            {
                this.activityRewardDumpsLogged++;
                FeatureLog.Life("ActivityReward", "SelfActivityChangedEvent len=" + e.Length
                    + " corefx(netId=" + netIdA + " staticId=" + staticIdA + ")"
                    + " mono(netId=" + netIdB + " staticId=" + staticIdB + ")"
                    + " bytes=" + DescribeActivityRewardPayload(e));
            }

            this.TryEnqueueActivityRewardCandidate(netIdA, staticIdA, "end/corefx");
            this.TryEnqueueActivityRewardCandidate(netIdB, staticIdB, "end/mono");
        }

        private void OnActivityEndNoticeEventHook(GameEventSnapshot e)
        {
            if (!MasterLogActivityRewardClaim)
            {
                return;
            }

            ModLogger.Msg("[ActivityReward] ActivityEventEndingNoticeRequestedEvent suppress="
                + this.activityHideEndPanel);
        }

        // A candidate only becomes a job if it is even shaped like an activity; CanGetReward does
        // the real arbitration later.
        private void TryEnqueueActivityRewardCandidate(uint netId, int staticId, string reason)
        {
            if (netId == 0u || staticId <= 0 || staticId > 9999999)
            {
                return;
            }

            this.EnqueueActivityRewardJob(netId, staticId, reason);
        }

        private static string DescribeActivityRewardPayload(GameEventSnapshot e)
        {
            int len = Math.Min(e.Length, 48);
            System.Text.StringBuilder sb = new System.Text.StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0 && (i % 4) == 0)
                {
                    sb.Append(' ');
                }

                sb.Append(e.ReadByte(i).ToString("X2"));
            }

            return sb.ToString();
        }

        // Server ack. Success-only and it carries no netId, so it settles the oldest job still
        // waiting on that reward type — jobs are deduped by netId and normally there is exactly one.
        private void OnActivityRewardRefreshEventHook(GameEventSnapshot e)
        {
            int rewardType = e.ReadInt32(0);

            for (int i = 0; i < this.activityRewardJobs.Count; i++)
            {
                ActivityRewardJob job = this.activityRewardJobs[i];
                if (rewardType == ActivityRewardTypePersonal && job.PersonalSends > 0 && !job.PersonalDone)
                {
                    job.PersonalDone = true;
                }
                else if (rewardType == ActivityRewardTypeTeam && job.TeamSends > 0 && !job.TeamDone)
                {
                    job.TeamDone = true;
                }
                else
                {
                    continue;
                }

                this.activityRewardClaimedCount++;
                FeatureLog.Life("ActivityReward", "reward claimed: netId=" + job.NetId
                    + " type=" + rewardType + " (total " + this.activityRewardClaimedCount + ")");
                this.ActivityRewardSetStatus("Claimed " + this.activityRewardClaimedCount + " event reward(s).");
                this.AddMenuNotification(this.L("Event reward claimed"), new Color(0.45f, 1f, 0.55f));
                return;
            }

            if (MasterLogActivityRewardClaim)
            {
                ModLogger.Msg("[ActivityReward] ack type=" + rewardType + " matched no live job");
            }
        }

        private void EnqueueActivityRewardJob(uint netId, int staticId, string reason)
        {
            for (int i = 0; i < this.activityRewardJobs.Count; i++)
            {
                if (this.activityRewardJobs[i].NetId == netId)
                {
                    // Both end paths can fire for one end; the second is not a new job.
                    return;
                }
            }

            this.activityRewardJobs.Add(new ActivityRewardJob
            {
                NetId = netId,
                StaticId = staticId,
                ExpiresAt = Time.unscaledTime + ActivityRewardJobLifetimeSec,
            });

            this.activityRewardNextPollAt = 0f;
            this.ActivityRewardSetStatus("Claiming rewards for event " + staticId + " (" + reason + ").");
        }

        private void PumpActivityRewardJobs()
        {
            if (!this.EnsureActivityRewardMonoReady())
            {
                return;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.activityEventSystemClass);
            if (instance == IntPtr.Zero)
            {
                this.ActivityRewardSetStatus("DataModule<ActivityEventSystem>.Instance null.");
                return;
            }

            // One send per tick: the two claims of an event go out 2 s apart, which is the pacing
            // discipline the daily-claim sweeps established — no in-game action produces a burst.
            bool sentThisTick = false;
            uint instancePin = AuraMonoPinNew(instance);
            try
            {
                for (int i = this.activityRewardJobs.Count - 1; i >= 0; i--)
                {
                    ActivityRewardJob job = this.activityRewardJobs[i];

                    if (job.PersonalDone && job.TeamDone)
                    {
                        this.activityRewardJobs.RemoveAt(i);
                        continue;
                    }

                    if (Time.unscaledTime >= job.ExpiresAt)
                    {
                        this.ReportActivityRewardJobExpiry(job);
                        this.activityRewardJobs.RemoveAt(i);
                        continue;
                    }

                    if (!sentThisTick && !job.PersonalDone)
                    {
                        sentThisTick = this.TryActivityRewardType(instance, job, ActivityRewardTypePersonal);
                    }

                    if (!sentThisTick && !job.TeamDone)
                    {
                        sentThisTick = this.TryActivityRewardType(instance, job, ActivityRewardTypeTeam);
                    }
                }
            }
            catch (Exception ex)
            {
                this.ActivityRewardSetStatus("Claim error: " + ex.Message);
                FeatureLog.Fail("ActivityReward", "claim pump threw: " + ex);
            }
            finally
            {
                AuraMonoPinFree(instancePin);
            }
        }

        // Returns true when a command was actually sent this tick.
        private bool TryActivityRewardType(IntPtr instance, ActivityRewardJob job, int rewardType)
        {
            bool personal = rewardType == ActivityRewardTypePersonal;
            int sends = personal ? job.PersonalSends : job.TeamSends;
            float sentAt = personal ? job.PersonalSentAt : job.TeamSentAt;

            if (sends >= ActivityRewardMaxSendsPerType)
            {
                return false;
            }

            // One retry, and only after the ack window has passed with nothing arriving.
            if (sends > 0 && Time.unscaledTime - sentAt < ActivityRewardAckWaitSec)
            {
                return false;
            }

            // Personal is gated on its own goal id; Team is gated on the activity's own static id.
            int idArg = job.StaticId;
            if (personal)
            {
                if (!this.TryGetActivityRewardPersonalGoalId(instance, job.StaticId, out idArg))
                {
                    return false;
                }

                if (idArg == 0)
                {
                    // FirstOrDefault found no TableGameTask row — this event has no personal goal,
                    // so there is nothing to claim and never will be.
                    job.PersonalDone = true;
                    FeatureLog.Once("ActivityReward", "nopersonal:" + job.StaticId,
                        "event " + job.StaticId + " has no personal goal — skipping that half.");
                    return false;
                }
            }

            // Local dictionary the game's own panel writes — free, and it stops a second send after
            // the player claimed by hand while we were waiting.
            if (this.TryActivityRewardBool2(instance, this.activityRewardCheckClaimedMethod,
                    job.NetId, rewardType, out bool alreadyClaimed) && alreadyClaimed)
            {
                if (personal)
                {
                    job.PersonalDone = true;
                }
                else
                {
                    job.TeamDone = true;
                }

                return false;
            }

            // The same gate EventRewardPanel puts on its claim button. Backed by a server-pushed
            // component, so it can turn true AFTER the end event — that is what the poll is for.
            if (!this.TryActivityRewardCanGet(instance, idArg, job.NetId, rewardType, out bool canGet))
            {
                this.ActivityRewardSetStatus("CanGetReward() invoke failed.");
                return false;
            }

            if (!canGet)
            {
                return false;
            }

            if (!this.TryActivityRewardSend(rewardType, job.NetId))
            {
                this.ActivityRewardSetStatus("SendGetActivityEventRewardCommand() failed.");
                return false;
            }

            if (personal)
            {
                job.PersonalSends++;
                job.PersonalSentAt = Time.unscaledTime;
            }
            else
            {
                job.TeamSends++;
                job.TeamSentAt = Time.unscaledTime;
            }

            FeatureLog.Life("ActivityReward", "claim sent: netId=" + job.NetId
                + " staticId=" + job.StaticId
                + " type=" + rewardType
                + " attempt=" + (personal ? job.PersonalSends : job.TeamSends));
            return true;
        }

        // A goal that was never met is the normal outcome of a lost team target — that is not a
        // failure and must not be logged as one. A send that never got its ack is.
        private void ReportActivityRewardJobExpiry(ActivityRewardJob job)
        {
            bool personalStuck = !job.PersonalDone && job.PersonalSends > 0;
            bool teamStuck = !job.TeamDone && job.TeamSends > 0;

            if (personalStuck || teamStuck)
            {
                FeatureLog.Fail("ActivityReward", "no server ack for netId=" + job.NetId
                    + " staticId=" + job.StaticId
                    + " personalSends=" + job.PersonalSends + " teamSends=" + job.TeamSends
                    + " — the claim was rejected (rejections are silent) or never arrived.");
                this.ActivityRewardSetStatus("Event " + job.StaticId + ": claim not confirmed.");
                return;
            }

            FeatureLog.Once("ActivityReward", "unmet:" + job.StaticId,
                "event " + job.StaticId + " ended with nothing claimable — goal not met.");
            this.ActivityRewardSetStatus("Event " + job.StaticId + ": nothing to claim.");
        }

        // Initial-state sweep. Unlike the trigger this CAN use the list, because an activity still
        // sitting in Ending has not been destroyed yet.
        private bool TryActivityRewardSweep()
        {
            if (!this.EnsureActivityRewardMonoReady())
            {
                return false;
            }

            if (this.activityRewardGetAllInfoMethod == IntPtr.Zero
                || this.activityRewardCheckPlayerInActivityMethod == IntPtr.Zero)
            {
                // Sweep-only methods; their absence must not disable the event-driven path.
                return true;
            }

            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0u)
            {
                return false;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.activityEventSystemClass);
            if (instance == IntPtr.Zero)
            {
                return false;
            }

            uint instancePin = AuraMonoPinNew(instance);
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr listObj = auraMonoRuntimeInvoke(
                    this.activityRewardGetAllInfoMethod, instance, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    return false;
                }

                uint listPin = AuraMonoPinNew(listObj);
                try
                {
                    // An empty collection enumerates as `false` too, so get_Count is what tells a
                    // quiet world apart from a technical failure.
                    IntPtr listClass = auraMonoObjectGetClass(listObj);
                    IntPtr getCount = listClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0)
                        : IntPtr.Zero;
                    int count = this.GetAuraMonoIntCount(listObj, getCount);
                    if (count == 0)
                    {
                        return true;
                    }

                    List<IntPtr> items = new List<IntPtr>();
                    List<uint> pins = new List<uint>();
                    bool enumerated = this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins);
                    try
                    {
                        if (!enumerated)
                        {
                            return false;
                        }

                        this.SweepActivityRewardItems(instance, items, selfNetId);
                    }
                    finally
                    {
                        FreeAuraMonoPins(pins);
                    }
                }
                finally
                {
                    AuraMonoPinFree(listPin);
                }

                return true;
            }
            catch (Exception ex)
            {
                FeatureLog.Fail("ActivityReward", "sweep threw: " + ex);
                return true; // do not spin on a broken sweep; the event path still works
            }
            finally
            {
                AuraMonoPinFree(instancePin);
            }
        }

        private void SweepActivityRewardItems(IntPtr instance, List<IntPtr> items, uint selfNetId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                IntPtr item = items[i];
                if (item == IntPtr.Zero
                    || !this.TryGetMonoUInt32Member(item, "NetId", out uint netId) || netId == 0u
                    || !this.TryGetMonoInt32Member(item, "StaticId", out int staticId) || staticId == 0
                    || !this.TryGetMonoInt32Member(item, "StatusType", out int status)
                    || status != ActivityRewardStatusEnding)
                {
                    continue;
                }

                if (!this.TryActivityRewardBool2Uint(instance, this.activityRewardCheckPlayerInActivityMethod,
                        netId, selfNetId, out bool inIt) || !inIt)
                {
                    continue;
                }

                this.EnqueueActivityRewardJob(netId, staticId, "sweep");
            }
        }

        private bool EnsureActivityRewardMonoReady()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                this.ActivityRewardSetStatus("Mono API not ready.");
                return false;
            }

            if (!AuraMonoGameDataLive)
            {
                this.ActivityRewardSetStatus("Game Mono side not live yet.");
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                // Fail closed: without pinning every object pointer we hold can move under sgen.
                this.activityRewardDisabledByPinning = true;
                this.activityRewardJobs.Clear();
                this.ActivityRewardSetStatus("AuraMono pinning unavailable — auto-claim disabled.");
                FeatureLog.Fail("ActivityReward", "pinning unavailable; auto-claim disabled for this session.");
                return false;
            }

            return this.TryResolveActivityRewardMethods();
        }

        private bool TryResolveActivityRewardMethods()
        {
            // Shares the auto-decline half's class pointer (same partial class); whichever half runs
            // first fills it.
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
                this.ActivityRewardSetStatus("ActivityEventSystem class not found.");
                return false;
            }

            if (this.activityRewardGetPersonalGoalIdMethod == IntPtr.Zero)
            {
                this.activityRewardGetPersonalGoalIdMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.activityEventSystemClass, "GetPersonalGoalId", 1);
            }

            if (this.activityRewardCanGetRewardMethod == IntPtr.Zero)
            {
                this.activityRewardCanGetRewardMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.activityEventSystemClass, "CanGetReward", 3);
            }

            if (this.activityRewardCheckClaimedMethod == IntPtr.Zero)
            {
                this.activityRewardCheckClaimedMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.activityEventSystemClass, "CheckRewardClaimed", 2);
            }

            // Sweep-only; missing ones are tolerated, the event path does not need them.
            if (this.activityRewardCheckPlayerInActivityMethod == IntPtr.Zero)
            {
                this.activityRewardCheckPlayerInActivityMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.activityEventSystemClass, "CheckPlayerInActivity", 2);
            }

            if (this.activityRewardGetAllInfoMethod == IntPtr.Zero)
            {
                this.activityRewardGetAllInfoMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.activityEventSystemClass, "GetAllActivityEventInfo", 0);
            }

            if (this.activityRewardSendMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName(ActivityProtocolManagerTypeName);
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        ActivityProtocolManagerNamespace, ActivityProtocolManagerClassName);
                }

                if (protocolClass != IntPtr.Zero)
                {
                    // Static, (ActivityEventRewardType, uint). Builds GetActivityRewardCommand and
                    // hands it to WebRequestUtility.SendCommand<T> inside normally-JIT'd game code,
                    // which is the whole reason we never touch the command object ourselves.
                    this.activityRewardSendMethod = this.FindAuraMonoMethodOnHierarchy(
                        protocolClass, "SendGetActivityEventRewardCommand", 2);
                }
            }

            if (this.activityRewardCanGetRewardMethod == IntPtr.Zero
                || this.activityRewardCheckClaimedMethod == IntPtr.Zero
                || this.activityRewardGetPersonalGoalIdMethod == IntPtr.Zero
                || this.activityRewardSendMethod == IntPtr.Zero)
            {
                this.ActivityRewardSetStatus("Activity reward methods not found.");
                return false;
            }

            return true;
        }

        private bool TryGetActivityRewardPersonalGoalId(IntPtr instance, int staticId, out int goalId)
        {
            if (this.activityRewardPersonalGoalIds.TryGetValue(staticId, out goalId))
            {
                return true;
            }

            // A LINQ scan over TableGameTasks — worth caching, and the answer never changes.
            if (!this.TryActivityRewardInt1(instance, this.activityRewardGetPersonalGoalIdMethod, staticId, out goalId))
            {
                return false;
            }

            this.activityRewardPersonalGoalIds[staticId] = goalId;
            return true;
        }

        // ---- Invokes. Every argument is a value type handed over as a pointer to a local, which is
        // the only shape mono_runtime_invoke takes safely here; no out-params, no generics, and the
        // enum crosses as the plain int it is. ----

        private unsafe bool TryActivityRewardInt1(IntPtr instance, IntPtr method, int arg, out int value)
        {
            value = 0;
            if (instance == IntPtr.Zero || method == IntPtr.Zero)
            {
                return false;
            }

            int argValue = arg;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&argValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoInt32(boxed, out value);
        }

        // CheckRewardClaimed(uint activityEventId, ActivityEventRewardType rewardType)
        private unsafe bool TryActivityRewardBool2(IntPtr instance, IntPtr method, uint netId, int rewardType, out bool value)
        {
            value = false;
            if (instance == IntPtr.Zero || method == IntPtr.Zero)
            {
                return false;
            }

            uint netIdValue = netId;
            int typeValue = rewardType;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&netIdValue);
            args[1] = (IntPtr)(&typeValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        // CheckPlayerInActivity(uint eventNetId, uint playerNetId)
        private unsafe bool TryActivityRewardBool2Uint(IntPtr instance, IntPtr method, uint first, uint second, out bool value)
        {
            value = false;
            if (instance == IntPtr.Zero || method == IntPtr.Zero)
            {
                return false;
            }

            uint firstValue = first;
            uint secondValue = second;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&firstValue);
            args[1] = (IntPtr)(&secondValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        // CanGetReward(int activityStaticId, uint activityNetId, ActivityEventRewardType rewardType)
        private unsafe bool TryActivityRewardCanGet(IntPtr instance, int idArg, uint netId, int rewardType, out bool value)
        {
            value = false;
            if (instance == IntPtr.Zero || this.activityRewardCanGetRewardMethod == IntPtr.Zero)
            {
                return false;
            }

            int idValue = idArg;
            uint netIdValue = netId;
            int typeValue = rewardType;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&idValue);
            args[1] = (IntPtr)(&netIdValue);
            args[2] = (IntPtr)(&typeValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(
                this.activityRewardCanGetRewardMethod, instance, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        // SendGetActivityEventRewardCommand(ActivityEventRewardType rewardType, uint netId) — static.
        private unsafe bool TryActivityRewardSend(int rewardType, uint netId)
        {
            if (this.activityRewardSendMethod == IntPtr.Zero)
            {
                return false;
            }

            int typeValue = rewardType;
            uint netIdValue = netId;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&typeValue);
            args[1] = (IntPtr)(&netIdValue);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.activityRewardSendMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        internal string GetActivityRewardClaimStatus()
        {
            return this.activityRewardStatus;
        }

        private void ActivityRewardSetStatus(string status)
        {
            this.activityRewardStatus = status;
            if (string.Equals(status, this.activityRewardLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.activityRewardLastLoggedStatus = status;
            ModLogger.Msg("[ActivityReward] " + status);
        }
    }
}
