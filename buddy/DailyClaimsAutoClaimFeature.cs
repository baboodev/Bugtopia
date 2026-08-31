using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // AUTO-CLAIM — event-driven Daily Claims.
    //
    // The game already maintains a single authoritative "there is something to collect" channel:
    // XDTDataAndProtocol.ProtocolService.RedPoint.RedPointEvent, dispatched GLOBALLY from
    // EcsSystem/ClientSystem.RedPoint/ClientRedPointSystem.cs. Its payload is fully scalar (32 B:
    // RedPointType@0, IdParam@4, IsAdd@8, NetId@12, ItemGuid@16), and for almost every reward kind
    // IdParam is EXACTLY the argument the matching claim command needs — taskId, series triggerId,
    // EPictorialNewType, suitId, certification itemId, activityId. So one hook replaces
    // re-deriving each subsystem's "is it claimable" logic.
    //
    // Two channels RedPointEvent does NOT carry, hooked separately:
    //   - operation-activity missions (Sanrio-style event dailies): TaskSystem pokes RedPointManager
    //     directly for those, but it also dispatches RefreshActivityTasks{taskId} — that is the
    //     trigger. (These tasks live in _operationActivityTasks, never in _tasks.)
    //   - mail: no RedPointType at all; MailUpdatedEvent's first field is a bool, and the rest is a
    //     List<Guid> we must not touch, so it is hooked as a 1-byte "mail changed" flag.
    //
    // THE TIMING CAVEAT that shapes the whole design: the server-side red points arrive as a burst
    // during the initial ECS sync, i.e. before the world-ready gate installs the detour. Events
    // therefore deliver TRANSITIONS, never the starting state — so world-ready also queues a
    // catch-up sweep of the cheap claims. The heavy table-driven collection sweeps (480 suits /
    // 1.7k certifications / 73 cat moments) are deliberately NOT part of the catch-up: they cost
    // thousands of gate reads, and once running, their red points arrive as events like everything
    // else. Anything already pending from before login stays a job for the manual button.
    //
    // Deliberately NOT auto-claimed:
    //   - RedPointType.Task (200), ordinary quests — Quest Assistant owns those, and submitting a
    //     story quest behind the player's back is not a "claim".
    //   - the SeaCycle exploration upgrade — it SPENDS a ticket and exp; the Whalefall button does
    //     it on an explicit press, auto-claim never spends.
    //
    // Pacing: one queued item per DailyClaimsAutoDrainIntervalSeconds, never in bursts, and never
    // while a manual Daily Claims coroutine is running. IsAdd=false is ignored outright — a cleared
    // red point is usually the echo of our own claim, and acting on it would loop.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Persisted (HeartopiaComplete.Config.cs). Off by default: auto-claim sends server commands
        // with no user action, so it is strictly opt-in.
        internal bool dailyClaimsAutoClaimEnabled = false;

        // Sazabi.Scene.XDT.Scene.Server.RedPoint.RedPointType — the server enum carried in the event.
        private const int DailyClaimsRedPointTypeBattlePassTaskCanSubmit = 502;
        private const int DailyClaimsRedPointTypeSeriesReward = 7000;
        private const int DailyClaimsRedPointTypeTownGuides = 7100;
        private const int DailyClaimsRedPointTypeTownGuideNewNodeTask = 7101;
        private const int DailyClaimsRedPointTypeTownGuidesGrowth = 7200;
        // PetGrownGift on the client (RedPointEnum 40012), PetGrowthGift on the server — same
        // number, and RedPointUtility maps one to the other. PetManualPanel registers it as
        // (PetGrownGift, (int)petNetId), so IdParam here is the PET's netId, not a reward id.
        private const int DailyClaimsRedPointTypePetGrowthGift = 40012;
        private const int DailyClaimsRedPointTypePictorialTypeReward = 9000;
        private const int DailyClaimsRedPointTypePictorialSuitReward = 9001;
        private const int DailyClaimsRedPointTypePictorialAllSuitReward = 9002;
        private const int DailyClaimsRedPointTypeActivityForOperation = 20779;
        private const int DailyClaimsRedPointTypeCollectCertification = 100004;

        // SeriesRewardTriggerComponent.TableTriggerId = (int)table * 100000 + period, and
        // ESeriesRewardTriggerTable.TableBpPeriod == 1 — so a BP-issue trigger id is 100000+issueId.
        // Anything outside the BpPeriodMin..BpPeriodMax window is a pay-related series reward and is
        // left alone.
        private const int DailyClaimsSeriesRewardBpPeriodMin = 100000;
        private const int DailyClaimsSeriesRewardBpPeriodMax = 200000;

        private const string DailyClaimsRedPointEventName =
            "XDTDataAndProtocol.ProtocolService.RedPoint.RedPointEvent";
        private const int DailyClaimsRedPointEventBytes = 32;
        private const string DailyClaimsRefreshActivityTasksEventName =
            "XDTDataAndProtocol.Events.RefreshActivityTasks";
        private const int DailyClaimsRefreshActivityTasksEventBytes = 4;
        private const string DailyClaimsMailUpdatedEventName =
            "XDTDataAndProtocol.Events.MailUpdatedEvent";
        private const int DailyClaimsMailUpdatedEventBytes = 1;

        // DreamSystem dispatches this immediately after it recomputes the Dream dots
        // (RefreshRewardRedPoint -> RefreshDreamEvent), which is the only signal that says a dream
        // reward may just have become claimable. Note the namespace: ScriptsRefactory, not XDT.
        private const string DailyClaimsRefreshDreamEventName =
            "ScriptsRefactory.DataAndProtocol.Events.RefreshDreamEvent";
        private const int DailyClaimsRefreshDreamEventBytes = 4;   // int dreamId

        // Taking a dream reward makes the server push the dream data back, which dispatches
        // RefreshDreamEvent again. The pass is idempotent (a target with nothing left simply stops
        // being lit), so the interval is only there to keep the _nodeDic walk rare.
        private const float DailyClaimsAutoDreamMinIntervalSeconds = 3f;
        private const int DailyClaimsAutoDreamPerPass = 8;

        // Sticker theme bonuses have no server RedPointType either. This is what the server sync of
        // the theme node states dispatches (OperationActivityCenterSyncSystem), i.e. exactly when a
        // tier can flip to WaitClaim. Empty struct, so one byte.
        private const string DailyClaimsRefreshStickerRewardEventName =
            "XDTDataAndProtocol.Events.RefreshStickerRewardEvent";
        private const int DailyClaimsRefreshStickerRewardEventBytes = 1;
        private const float DailyClaimsAutoStickerMinIntervalSeconds = 3f;
        private const int DailyClaimsAutoStickerPerPass = 8;

        // BattlePassSystem dispatches this when the BP component syncs — the moment a level-up makes
        // new track rewards claimable. It is also what sets the BattlePassLoopReward dot, so both
        // halves of the panel's own CanOneClaimAward test are settled by the time this arrives.
        private const string DailyClaimsBattlePassUpdatedEventName =
            "XDTDataAndProtocol.Events.BattlePassUpdatedEvent";
        private const int DailyClaimsBattlePassUpdatedEventBytes = 4;   // int level
        private const float DailyClaimsAutoBattlePassMinIntervalSeconds = 5f;

        // Whalefall daily requests are plain game tasks with no red point of either kind, so the only
        // signal that one became submittable is the task event itself. It is also the noisiest event
        // in the game — Quest Assistant measured 600-700 dispatches in a single frame — so the
        // handler does nothing but flip a bool and the interval below does the coalescing.
        private const string DailyClaimsTaskUpdatedEventName = "XDTDataAndProtocol.Events.TaskUpdated";
        private const int DailyClaimsTaskUpdatedEventBytes = 8;   // uint taskNetId@0, int taskStaticId@4
        private const float DailyClaimsAutoWhalefallMinIntervalSeconds = 5f;

        private const string DailyClaimsAutoWorldReadyCallbackName = "DailyClaimsAutoClaim";

        // One send per tick at this spacing — the same "no burst the game never produces" rule the
        // manual sweeps follow.
        private const float DailyClaimsAutoDrainIntervalSeconds = 0.4f;

        // Mail is a CLOSED LOOP if left alone: RequestAllRewards makes the server push
        // MailUpdatedEvent, whose handler re-arms pendingMail, which sends RequestAllRewards again.
        // Live trace 2026-08-11: three RequestAllRewards inside 1.5s on every world entry, each
        // answered with MailErrorCode:RewardIsEmpty. Red points already filter their own echo via
        // IsAdd == false; MailUpdatedEvent carries no such flag, so it is filtered by time instead.
        // EchoWindow swallows the server's answer to our own send; MinInterval is the backstop that
        // caps the loop at one send per window even if an echo slips past it.
        private const float DailyClaimsAutoMailEchoWindowSeconds = 5f;
        private const float DailyClaimsAutoMailMinIntervalSeconds = 60f;

        // Per-queue cap. A pathological red-point storm must not grow these without bound; dropping
        // the overflow is safe because the manual button remains a full sweep.
        private const int DailyClaimsAutoMaxQueued = 256;

        private bool dailyClaimsAutoHooksRegistered;
        private bool dailyClaimsAutoWorldReadyRegistered;
        private float dailyClaimsAutoNextDrainAt;
        private int dailyClaimsAutoClaimedCount;
        private string dailyClaimsAutoLastStatus = string.Empty;

        // Queues, all scalar. Lists (not sets) with an explicit Contains guard: they stay tiny, and
        // this avoids allocating a HashSet enumerator on the hot drain path.
        // Tasks carry their red-point identity with them: a battle-pass challenge has a server-side
        // point (type 502), an operation-activity mission has only a CLIENT node
        // (RedPointEnum.ActivityTaskReward — TaskSystem pokes RedPointManager directly for those),
        // and a Whalefall request has neither. Without this the drain could not tell which dot to
        // clear after a submit.
        private struct DailyClaimsAutoTaskJob
        {
            public int TaskId;
            public int ServerRedPointType;   // 0 = no server-side point
            public int ClientRedPointEnum;   // 0 = derive from the server type
        }

        private readonly List<DailyClaimsAutoTaskJob> dailyClaimsAutoTaskJobs = new List<DailyClaimsAutoTaskJob>(16);
        private readonly List<int> dailyClaimsAutoPictorialTypes = new List<int>(8);
        private readonly List<int> dailyClaimsAutoSuitIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoAllSuitIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoCertIds = new List<int>(16);
        private readonly List<int> dailyClaimsAutoIssueIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoActivityIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoPetNetIds = new List<int>(4);
        private bool dailyClaimsAutoPendingTownGuide;

        // Auto mark-read. Auto-claim only ever CLAIMED, so a "seen" marker — a learned pet pose, a
        // new recipe, a wardrobe item — fell into the switch default, was logged as unhandled and
        // stayed lit forever. Those carry no reward at all; the game clears them when the player
        // LOOKS at the thing, and RedPointManager.ReadRedPoint is that same act.
        private bool dailyClaimsAutoPendingMarkRead;
        private float dailyClaimsAutoMarkReadNextAllowedAt;
        private bool dailyClaimsAutoPendingMail;
        private bool dailyClaimsAutoPendingDream;
        private float dailyClaimsAutoDreamNextAllowedAt;
        private bool dailyClaimsAutoPendingSticker;
        private float dailyClaimsAutoStickerNextAllowedAt;
        private bool dailyClaimsAutoPendingBattlePass;
        private float dailyClaimsAutoBattlePassNextAllowedAt;
        private bool dailyClaimsAutoPendingWhalefall;
        private float dailyClaimsAutoWhalefallNextAllowedAt;
        private float dailyClaimsAutoMailEchoUntil;
        private float dailyClaimsAutoMailNextAllowedAt;

        // The catch-up is armed ONCE per world epoch. Before this, every world-ready re-armed the
        // whole thing: the 2026-08-18 log shows mail, GetAllRewards, GetLoopRewards and the same
        // sign-in node re-sent three times inside three minutes, which is exactly the command burst
        // the per-item pacing exists to avoid.
        private int dailyClaimsAutoCatchUpEpoch = -1;

        // (activityId * 100 + nodeIndex) pairs already attempted this session. The server silently
        // rejects a claim it does not accept, and ClaimSignInRewards counts a DISPATCHED command as
        // success — so without this, a node the server refuses (300044/0 in that same log) is
        // re-sent on every single world change, forever.
        private readonly List<int> dailyClaimsAutoAttemptedActivityNodes = new List<int>(32);

        // World-ready catch-up steps, drained one per tick like everything else.
        private bool dailyClaimsAutoCatchUpActivities;
        private bool dailyClaimsAutoCatchUpTasks;
        private bool dailyClaimsAutoCatchUpBattlePass;
        private bool dailyClaimsAutoCatchUpWhalefall;

        // ----------------------------------------------------------------------------------------
        // Registration — HARD RULE: hook installs go on the world-ready gate, never a fresh poll in
        // OnUpdate. Registration itself is unconditional (the detour must exist before the first
        // transition); the toggle is checked in the handlers, so flipping it costs nothing.
        // ----------------------------------------------------------------------------------------

        private void EnsureDailyClaimsAutoClaimRegistered()
        {
            if (this.dailyClaimsAutoWorldReadyRegistered)
            {
                return;
            }

            this.dailyClaimsAutoWorldReadyRegistered = true;
            this.RegisterWorldReadyCallback(
                DailyClaimsAutoWorldReadyCallbackName,
                this.OnDailyClaimsAutoClaimWorldReady);
        }

        // The gate contract is Func<bool>: false = "not done, call me again next tick". Registration
        // only records handler metadata (the detour installs lazily once EventCenter and the event's
        // image are loaded), so a successful register is final.
        private bool OnDailyClaimsAutoClaimWorldReady()
        {
            if (!this.dailyClaimsAutoHooksRegistered)
            {
                bool redPoint = this.RegisterGameEventHook(
                    DailyClaimsRedPointEventName,
                    DailyClaimsRedPointEventBytes,
                    this.OnDailyClaimsAutoRedPointEvent);
                bool activityTasks = this.RegisterGameEventHook(
                    DailyClaimsRefreshActivityTasksEventName,
                    DailyClaimsRefreshActivityTasksEventBytes,
                    this.OnDailyClaimsAutoRefreshActivityTasksEvent);
                bool mail = this.RegisterGameEventHook(
                    DailyClaimsMailUpdatedEventName,
                    DailyClaimsMailUpdatedEventBytes,
                    this.OnDailyClaimsAutoMailUpdatedEvent);
                bool dream = this.RegisterGameEventHook(
                    DailyClaimsRefreshDreamEventName,
                    DailyClaimsRefreshDreamEventBytes,
                    this.OnDailyClaimsAutoRefreshDreamEvent);
                bool sticker = this.RegisterGameEventHook(
                    DailyClaimsRefreshStickerRewardEventName,
                    DailyClaimsRefreshStickerRewardEventBytes,
                    this.OnDailyClaimsAutoRefreshStickerRewardEvent);
                bool battlePass = this.RegisterGameEventHook(
                    DailyClaimsBattlePassUpdatedEventName,
                    DailyClaimsBattlePassUpdatedEventBytes,
                    this.OnDailyClaimsAutoBattlePassUpdatedEvent);
                bool taskUpdated = this.RegisterGameEventHook(
                    DailyClaimsTaskUpdatedEventName,
                    DailyClaimsTaskUpdatedEventBytes,
                    this.OnDailyClaimsAutoTaskUpdatedEvent);

                this.dailyClaimsAutoHooksRegistered =
                    redPoint || activityTasks || mail || dream || sticker || battlePass || taskUpdated;
                this.DailyClaimsLog("auto-claim hooks registered: redPoint=" + redPoint
                    + " activityTasks=" + activityTasks + " mail=" + mail + " dream=" + dream
                    + " sticker=" + sticker + " battlePass=" + battlePass
                    + " taskUpdated=" + taskUpdated);

                if (!this.dailyClaimsAutoHooksRegistered)
                {
                    // Nothing took — retry on the next gate tick rather than silently running
                    // catch-up-only forever.
                    return false;
                }
            }

            // The pre-login red-point burst is already gone by now (see the file header), so queue a
            // catch-up of the cheap claims. Queued rather than run inline: this runs on the gate,
            // and the drain owns all pacing.
            //
            // Once per world epoch: the gate can fire repeatedly for the same world, and re-running
            // the sweep just re-sends commands the server already answered.
            int epoch = AuraMonoWorldEpoch;
            if (epoch == this.dailyClaimsAutoCatchUpEpoch)
            {
                return true;
            }

            this.dailyClaimsAutoCatchUpEpoch = epoch;
            this.dailyClaimsAutoCatchUpActivities = true;
            this.dailyClaimsAutoCatchUpTasks = true;
            this.dailyClaimsAutoCatchUpBattlePass = true;
            this.dailyClaimsAutoCatchUpWhalefall = true;
            this.dailyClaimsAutoPendingTownGuide = true;
            this.dailyClaimsAutoPendingMail = true;

            // Dream dots are computed from data that syncs during login, so by the time the hook
            // exists RefreshDreamEvent has usually already fired. Without this the whole family
            // would wait for the next dream change to be claimed at all.
            this.dailyClaimsAutoPendingDream = true;

            // Same reasoning for the sticker tiers: the theme node states sync during login, so the
            // refresh event has usually already fired by the time the hook exists.
            this.dailyClaimsAutoPendingSticker = true;

            // The markers that matter most arrive in the pre-world burst, which the event detour
            // structurally cannot see (installing it that early is what aborted the process three
            // times). One pass at world-ready is what clears that backlog.
            this.dailyClaimsAutoPendingMarkRead = true;
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Event handlers — run on the main thread in the hook engine's drain, so they may allocate.
        // They only ENQUEUE; nothing is sent from here (a red-point burst would otherwise become a
        // command burst in one frame).
        // ----------------------------------------------------------------------------------------

        private void OnDailyClaimsAutoRedPointEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // IsAdd == false means the point was CLEARED — usually the echo of a claim we just made.
            if (!e.ReadBool(8))
            {
                return;
            }

            int redPointType = e.ReadInt32(0);
            int idParam = e.ReadInt32(4);

            // Not every kind puts its subject in IdParam. ClientRedPointSystem.ToRedPointType
            // carries entity-scoped kinds (Pet, PetGrowthGift, PetLearnedMotion, PartyCanJoin,
            // NewFriend) in NetId with IdParam left at 0 — reading only IdParam there yields 0 and
            // silently claims nothing.
            uint netIdParam = e.ReadUInt32(12);

            // Traced unconditionally: this is the only way to see WHICH red-point kinds the game
            // actually raises on this account, including the ones the switch below drops on the
            // floor — the surfaces that keep a dot after auto-claim are exactly the kinds that never
            // appear here, or appear under a type the switch has no case for.
            this.DailyClaimsLog("redpoint event type=" + redPointType + " id=" + idParam
                + " netId=" + netIdParam + " add=true");

            switch (redPointType)
            {
                case DailyClaimsRedPointTypeBattlePassTaskCanSubmit:
                    this.DailyClaimsAutoEnqueueTask(idParam, DailyClaimsRedPointTypeBattlePassTaskCanSubmit, 0);
                    break;

                case DailyClaimsRedPointTypeSeriesReward:
                    // triggerId → issueId, but only inside the BP-period window; the other series
                    // rewards are first-pay / pay-accumulate and are not ours to claim.
                    if (idParam > DailyClaimsSeriesRewardBpPeriodMin && idParam < DailyClaimsSeriesRewardBpPeriodMax)
                    {
                        DailyClaimsAutoEnqueue(this.dailyClaimsAutoIssueIds, idParam - DailyClaimsSeriesRewardBpPeriodMin);
                    }
                    break;

                case DailyClaimsRedPointTypePictorialTypeReward:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoPictorialTypes, idParam);
                    break;

                case DailyClaimsRedPointTypePictorialSuitReward:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoSuitIds, idParam);
                    break;

                case DailyClaimsRedPointTypePictorialAllSuitReward:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoAllSuitIds, idParam);
                    break;

                case DailyClaimsRedPointTypeCollectCertification:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoCertIds, idParam);
                    break;

                case DailyClaimsRedPointTypeActivityForOperation:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoActivityIds, idParam);
                    break;

                case DailyClaimsRedPointTypePetGrowthGift:
                    // PetGrowthRedPointNetworkSchema -> (PetGrowthGift, idParam: 0, netId: the pet).
                    // The pet is in NetId; IdParam is always 0 for this kind.
                    if (netIdParam != 0u)
                    {
                        DailyClaimsAutoEnqueue(this.dailyClaimsAutoPetNetIds, (int)netIdParam);
                    }
                    else
                    {
                        this.DailyClaimsLog("pet growth red point arrived with no netId (id=" + idParam
                            + ") — nothing to claim against.");
                    }
                    break;

                case DailyClaimsRedPointTypeTownGuides:
                case DailyClaimsRedPointTypeTownGuideNewNodeTask:
                case DailyClaimsRedPointTypeTownGuidesGrowth:
                    // The town-guide claim walks chapters itself, so the id is not needed.
                    this.dailyClaimsAutoPendingTownGuide = true;
                    break;

                default:
                    // Everything else (ordinary quests, cosmetics-unlocked markers, social pings) is
                    // either not a claim or not ours — see the file header. Not a reward, so there is
                    // nothing to claim: queue a mark-read pass instead of leaving the dot lit. Still
                    // logged, because a kind that SHOULD have been claimable has to stay visible in
                    // the trace rather than disappearing quietly into the mark-read bucket.
                    this.DailyClaimsLog("redpoint event type=" + redPointType
                        + " no claim mapped — queued for mark-read");
                    this.dailyClaimsAutoPendingMarkRead = true;
                    break;
            }
        }

        private void OnDailyClaimsAutoRefreshActivityTasksEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // Fires on any state change of an operation-activity task, not just "became claimable",
            // so the drain re-checks the state before submitting.
            this.dailyClaimsAutoCatchUpTasks = true;
        }

        private void OnDailyClaimsAutoTaskUpdatedEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // Deliberately NOT logged and deliberately not filtered by task id: this fires hundreds
            // of times per frame during a login sync, and reading the SeaCycle list to decide
            // whether the id is one of ours would pay that cost per dispatch. The drain checks the
            // seven ids once per interval instead.
            this.dailyClaimsAutoPendingWhalefall = true;
        }

        private void OnDailyClaimsAutoRefreshStickerRewardEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // Carries nothing — the pass works off the lit theme dots.
            this.dailyClaimsAutoPendingSticker = true;
        }

        private void OnDailyClaimsAutoBattlePassUpdatedEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            this.DailyClaimsLog("battle pass updated event level=" + e.ReadInt32(0));
            this.dailyClaimsAutoPendingBattlePass = true;
        }

        private void OnDailyClaimsAutoRefreshDreamEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // dreamId is read only for the trace: the pass works off the lit nodes, because one
            // dream's data landing can light a target under any of them.
            this.DailyClaimsLog("refresh dream event dreamId=" + e.ReadInt32(0));
            this.dailyClaimsAutoPendingDream = true;
        }

        private void OnDailyClaimsAutoMailUpdatedEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // Our own RequestAllRewards makes the server push this event straight back. Re-arming on
            // it is what produced the send loop, so anything inside the echo window is dropped;
            // genuinely new mail arriving later still re-arms normally.
            if (Time.realtimeSinceStartup < this.dailyClaimsAutoMailEchoUntil)
            {
                return;
            }

            this.dailyClaimsAutoPendingMail = true;
        }

        // ==========================================================================================
        // Clearing the red point after a claim
        //
        // Claiming a reward and clearing its dot are SEPARATE operations in this game: the widgets
        // clear when the player OPENS the panel (ReadRedPoint / UpdateRedPointData), not when the
        // reward lands. Auto-claim never opens a panel, so without this the dots pile up — seen live
        // on 2026-08-18, a suit-reward tier still marked after its reward had been taken.
        //
        // Suits are worse than the rest: ClientRedPointSystem.ToRedPointType returns Unknow once a
        // suit has no claimable tier left, and HandleRedPoint does not dispatch for Unknow — so the
        // "it is gone now" event never arrives at all and the client node keeps a stale Active=true.
        //
        // Two layers, both needed:
        //   server  RedPointProtocolManager.DeleteRedPoint(type, idParam) — authoritative, survives a
        //           relog. Called on the PROTOCOL manager rather than RedPointManager.ReadRedPoint
        //           because the latter no-ops once the node is not Active, which makes call order
        //           fragile for no benefit.
        //   client  RedPointManager.UpdateRedPointData(enum, nodeId, false) — instant, so the dot
        //           goes this frame instead of after the round trip.
        //
        // nodeId is NOT always idParam: suit rewards are keyed by TablePediaSuitReward.id (the tier
        // row) while the event carries the suitId.
        // ==========================================================================================

        // One live entry of RedPointManager._nodeDic.
        private struct DailyClaimsRedPointNode
        {
            public int EnumValue;
            public int Id;
        }

        // Walks RedPointManager._nodeDic — the client's own map of every red-point node it has ever
        // created — and returns the ones currently lit. This is the ONLY way to see the state that
        // arrived in the pre-world burst: the event detour cannot exist that early (installing it
        // there is what aborted the process three times, see the eventhook-preworld-inflate-abort
        // note), so transitions are all the hook can ever deliver. Reading the map instead sidesteps
        // the whole timing problem.
        //
        // The dictionary key is a ValueTuple<RedPointEnum,int>, so each entry's `key` boxes to a
        // struct whose Item1/Item2 are the enum and the id.
        private bool DailyClaimsCollectActiveRedPoints(List<DailyClaimsRedPointNode> nodes, out string status)
        {
            nodes.Clear();
            status = "RedPointManager unavailable";

            IntPtr manager = this.DailyClaimsResolveRedPointManager();
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(manager, "_nodeDic", out IntPtr dictObj) || dictObj == IntPtr.Zero)
            {
                status = "_nodeDic unreadable";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            int total = 0;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(dictObj, entries, pins))
                {
                    status = "_nodeDic empty";
                    return true;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    total++;

                    if (!this.TryGetMonoObjectMember(entry, "value", out IntPtr node) || node == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint nodePin = AuraMonoPinNew(node);
                    bool active;
                    try
                    {
                        active = this.TryGetMonoBoolMember(node, "Active", out bool a) && a;
                    }
                    finally
                    {
                        AuraMonoPinFree(nodePin);
                    }

                    if (!active)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(entry, "key", out IntPtr key) || key == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint keyPin = AuraMonoPinNew(key);
                    try
                    {
                        if (this.TryGetMonoIntMember(key, "Item1", out int enumValue)
                            && this.TryGetMonoIntMember(key, "Item2", out int id)
                            && enumValue > 0)
                        {
                            nodes.Add(new DailyClaimsRedPointNode { EnumValue = enumValue, Id = id });
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(keyPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = "_nodeDic nodes=" + total + " active=" + nodes.Count;
            return true;
        }

        // RedPointUtility.ToRedPointType(this RedPointEnum) — the reverse of DailyClaimsToRedPointEnum,
        // needed because the sweep starts from a CLIENT enum while every claim is keyed by the
        // server type.
        private unsafe int DailyClaimsToRedPointType(int redPointEnum)
        {
            if (redPointEnum <= 0
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            // RedPointUtility's map is compiled into the build, so the answer never changes within a
            // session — cache it and stop paying an invoke per node.
            if (this.dailyClaimsRedPointTypeByEnum.TryGetValue(redPointEnum, out int cached))
            {
                return cached;
            }

            if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraRedPointUtilityClass = this.FindAuraMonoClassByFullName(
                    "XDTGameSystem.GameplaySystem.RedPoint.RedPointUtility");
                if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraRedPointUtilityClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.RedPoint", "RedPointUtility");
                }
            }

            if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                this.dailyClaimsAuraRedPointUtilityClass, "ToRedPointType", 1);
            if (method == IntPtr.Zero)
            {
                return 0;
            }

            int enumValue = redPointEnum;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&enumValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            // RedPointType.Unknow is -1; treat anything non-positive as "no server type".
            int resolved = this.TryUnboxMonoInt32(boxed, out int typeValue) && typeValue > 0 ? typeValue : 0;
            this.dailyClaimsRedPointTypeByEnum[redPointEnum] = resolved;
            return resolved;
        }

        // TablePediaSuitRewards for the current sweep, walked at most once.
        private List<DailyClaimsSuitRewardTier> DailyClaimsSweepSuitTiers()
        {
            if (!this.dailyClaimsSweepSuitTiersLoaded)
            {
                this.dailyClaimsSweepSuitTiersLoaded = true;
                this.DailyClaimsTryCollectSuitRewardTiers(this.dailyClaimsSweepSuitTiers, out _);
            }

            return this.dailyClaimsSweepSuitTiers;
        }

        // Claim whatever a single red point stands for. Returns false when the kind has no claim
        // mapped — the sweep then only reports it, which is how an unhandled kind that keeps a dot
        // alive becomes visible instead of invisible.
        // Client enums whose nodes carry a REWARD but have no server RedPointType, so the switch on
        // serverType below can never reach them. Found by the 2026-08-18 sweep: after the
        // "unread marker" kinds were read, these were what still held the HUD dots lit.
        //
        // The Dream family is the clearest example: DreamTypeReward/DreamTaskReward carry real
        // rewards, RedPointUtility maps only DreamNew and DreamOnline to a server type, so nothing
        // under Dream can ever arrive as a RedPointEvent. DreamSystem is the authority on all of
        // them (RefreshRewardRedPoint / OnTaskUpdated / InitDreamTaskRedPoint / OnLoopTaskUpdate).
        private const int DailyClaimsRedPointEnumDreamReward = 1900;     // aggregate over a dreamType
        private const int DailyClaimsRedPointEnumDreamUpgrade = 1901;    // SPENDS — never auto
        private const int DailyClaimsRedPointEnumDreamTypeReward = 1902;
        private const int DailyClaimsRedPointEnumDreamTaskReward = 1903;
        private const int DailyClaimsRedPointEnumPartyFestivalTaskCanSubmit = 803;
        private const int DailyClaimsRedPointEnumPartyOfficialTaskCanSubmit = 804;
        private const int DailyClaimsRedPointEnumActivityFreeReward = 20788;

        // StickerActivityThemeReward: the node id is the sticker THEME, and the claim also needs the
        // activity that owns it plus the tier ordinal. RedPointUtility gives it no server type, so
        // like the Dream kinds it can only be reached from a node walk.
        private const int DailyClaimsRedPointEnumStickerActivityThemeReward = 20786;

        // Nodes walked between frame hand-backs when nothing is being sent. Only bounds frame time —
        // it is not command pacing, because no command is going out on those iterations.
        private const int DailyClaimsSweepFrameChunk = 64;

        // Read commands are "mark as seen", and the game itself emits them in bulk — PictorialTabNode
        // .Read() folds every child into ONE command, ReadAllRedPointByType sends one per type. So
        // mark-read paces per CHUNK rather than per node; at 0.05 s each, per-node spacing cost 16 s
        // for the 319 nodes of the first live run.
        private const int DailyClaimsMarkReadChunk = 8;

        // Auto mark-read pacing. The pass walks _nodeDic, so it is the same cost as the manual
        // button — rare and capped rather than per-event.
        private const float DailyClaimsAutoMarkReadMinIntervalSeconds = 12f;
        private const int DailyClaimsAutoMarkReadPerPass = 40;

        // client enum -> server RedPointType, resolved through RedPointUtility once per distinct
        // enum. The map is baked into the build, so this is a pure cache; it removed one AuraMono
        // invoke per node from both sweeps.
        private readonly Dictionary<int, int> dailyClaimsRedPointTypeByEnum = new Dictionary<int, int>();

        // TablePediaSuitRewards, collected once per sweep. Both the suit CLAIM and the suit CLEAR
        // need these rows, and each was re-walking all 922 of them (pinned) for every suit node.
        private readonly List<DailyClaimsSuitRewardTier> dailyClaimsSweepSuitTiers = new List<DailyClaimsSuitRewardTier>();
        private bool dailyClaimsSweepSuitTiersLoaded;

        // sticker themeId -> activityId, built once per sweep from TableStickerThemes.
        private readonly Dictionary<int, int> dailyClaimsStickerActivityByTheme = new Dictionary<int, int>();

        // targetId -> dreamType, built once per sweep from TableDreamTaskTypes.
        private readonly Dictionary<int, int> dailyClaimsDreamTypeByTarget = new Dictionary<int, int>();

        // dreamTaskId -> gameTaskId, built once per sweep from TableDreamTasks. A DreamTaskReward
        // node names the DREAM task; the command that takes it names the GAME task behind it.
        private readonly Dictionary<int, int> dailyClaimsDreamGameTaskByTask = new Dictionary<int, int>();

        // The New Life Log's "Day N" tabs, and every other operation activity's daily tab.
        //
        // NewPlayerJournalWidget clears one with exactly two calls — a client poke and a server
        // command — and neither is reachable from the generic paths: ActivityDailyTab/ActivityNewDay
        // have no server RedPointType (so the base Read() is a no-op) and no Read() override:
        //     UpdateRedPointData(RedPointEnum.ActivityNewDay, day, active: false);
        //     OperationActivityProtocolMananger.ClearDailyTabRedPoint(1003, day);
        // The node id IS the day number, and ActivityDailyTabNode.GetParentData resolves the owning
        // activity the same way: id <= 14 belongs to activity 1003, anything larger is a
        // TableActivityMission row whose activityId is on the row.
        private const int DailyClaimsRedPointEnumActivityDailyTab = 20775;
        private const int DailyClaimsRedPointEnumActivityNewDay = 20777;
        private const int DailyClaimsNewLifeLogDayIdMax = 14;

        // TableActivityMission.id -> activityId, built lazily for day ids above the 1003 range.
        private readonly Dictionary<int, int> dailyClaimsActivityByMissionId = new Dictionary<int, int>();
        private bool dailyClaimsActivityMissionMapBuilt;

        private int DailyClaimsResolveDailyTabActivityId(int dayId)
        {
            if (dayId <= DailyClaimsNewLifeLogDayIdMax)
            {
                return DailyClaimsNewLifeLogActivityId;
            }

            if (!this.dailyClaimsActivityMissionMapBuilt)
            {
                this.dailyClaimsActivityMissionMapBuilt = true;
                this.DailyClaimsForEachTableRow("TableActivityMissions", row =>
                {
                    if (this.TryGetMonoIntMember(row, "id", out int rowId) && rowId > 0
                        && this.TryGetMonoIntMember(row, "activityId", out int activityId) && activityId > 0)
                    {
                        this.dailyClaimsActivityByMissionId[rowId] = activityId;
                    }
                }, out _);
            }

            return this.dailyClaimsActivityByMissionId.TryGetValue(dayId, out int owner) ? owner : 0;
        }

        // OperationActivityProtocolMananger.ClearDailyTabRedPoint(int, int, ActivityRedPointType) —
        // 3 scalar args, the third an int-backed enum (None = 0, which is the default the widget uses).
        private bool TryDailyClaimsClearActivityDailyTab(int dayId, out string status)
        {
            int activityId = this.DailyClaimsResolveDailyTabActivityId(dayId);
            if (activityId <= 0)
            {
                status = "owning activity for day id " + dayId + " unknown";
                return false;
            }

            object[] args = { activityId, dayId, 0 };
            bool ok = this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.OperationActivity.OperationActivityProtocolMananger",
                "OperationActivityProtocolMananger",
                "ClearDailyTabRedPoint",
                args,
                out status);
            if (ok)
            {
                // The widget pokes the client node too, and it uses ActivityNewDay for it.
                this.TryDailyClaimsClearRedPointLocally(DailyClaimsRedPointEnumActivityNewDay, dayId);
                status = "activity " + activityId + " day " + dayId + ": " + status;
            }

            return ok;
        }

        // Task ids already submitted during the current sweep. TaskSystem.UpdatePartyTaskBranchRedPoints
        // writes the SAME taskId onto both PartyFestivalTaskCanSubmit and PartyOfficialTaskCanSubmit,
        // so a party task shows up as two lit nodes and was being submitted twice (seen live
        // 2026-08-18: task 1600007 under enum 803 and again under 804). The second send is harmless
        // — the server rejects it — but it is still a command the game would never issue.
        private readonly List<int> dailyClaimsSweepSubmittedTaskIds = new List<int>(16);

        // dreamTaskId -> gameTaskId, from TableDreamTasks. Walked at most once per sweep; the map
        // is baked into the build, so the only reason it is not permanent is that a game update
        // could renumber it under a running session.
        private bool DailyClaimsTryGetDreamGameTaskId(int dreamTaskId, out int gameTaskId)
        {
            gameTaskId = 0;
            if (dreamTaskId <= 0)
            {
                return false;
            }

            if (this.dailyClaimsDreamGameTaskByTask.Count == 0)
            {
                this.DailyClaimsForEachTableRow("TableDreamTasks", row =>
                {
                    // id and gameTaskId are both plain public int fields. The narrow-field-behind-a
                    // -property trap on this row is dreamType / dreamTaskType / taskType — none of
                    // which the claim needs, because the red point already encodes the gate.
                    if (this.TryGetMonoIntMember(row, "id", out int rowId) && rowId > 0
                        && this.TryGetMonoIntMember(row, "gameTaskId", out int rowGameTaskId)
                        && rowGameTaskId > 0)
                    {
                        this.dailyClaimsDreamGameTaskByTask[rowId] = rowGameTaskId;
                    }
                }, out _);
            }

            return this.dailyClaimsDreamGameTaskByTask.TryGetValue(dreamTaskId, out gameTaskId)
                && gameTaskId > 0;
        }

        // Every lit sticker-theme dot. Same shape as the Dream pass and for the same reason:
        // StickerActivityThemeReward has no server RedPointType, so it cannot arrive as a
        // RedPointEvent. The per-tier WaitClaim gate lives in the claim itself, so a pass that finds
        // nothing owed sends nothing and the refresh this triggers cannot loop.
        private int DailyClaimsAutoClaimStickerNodes(out string status)
        {
            List<DailyClaimsRedPointNode> nodes = new List<DailyClaimsRedPointNode>();
            if (!this.DailyClaimsCollectActiveRedPoints(nodes, out string collectStatus))
            {
                status = "collect failed: " + collectStatus;
                return 0;
            }

            this.dailyClaimsStickerActivityByTheme.Clear();

            int sent = 0;
            int lit = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].EnumValue != DailyClaimsRedPointEnumStickerActivityThemeReward)
                {
                    continue;
                }

                lit++;
                if (sent >= DailyClaimsAutoStickerPerPass)
                {
                    this.dailyClaimsAutoPendingSticker = true;
                    break;
                }

                int serverType = this.DailyClaimsToRedPointType(nodes[i].EnumValue);
                if (this.DailyClaimsTryClaimForRedPoint(
                        nodes[i].EnumValue, serverType, nodes[i].Id, out string what, out string claimStatus))
                {
                    sent++;
                    this.DailyClaimsLog("auto sticker: claimed " + what + " (" + claimStatus + ")");
                }
                else
                {
                    this.DailyClaimsLog("auto sticker: LEFT " + what + " (" + claimStatus + ")");
                }
            }

            status = "lit=" + lit + " claimed=" + sent;
            return sent;
        }

        // The mini-BP "Claim All" button is BattlePassSystem.GetAllRewards(), a one-line passthrough
        // to the same BattlePassGetRewardNetworkCommand{flag=0,rewardId=0} this already sends. What
        // was missing is the gate and the trigger: the catch-up fired both commands blind, once per
        // world epoch, so a level-up mid-session was never picked up at all.
        //
        // Gated exactly like the panel's CanOneClaimAward: a track reward needs a slot in state
        // CanGet, and the cycle reward has its own dot, which BattlePassSystem sets in the very
        // handler that dispatches the event this pass runs on. Without the gate, claiming makes the
        // component sync, which dispatches the event again — a two-command echo with no end.
        private bool DailyClaimsAutoClaimBattlePass(out string status)
        {
            if (!this.TryDailyClaimsGetAuraMonoBattlePassSystem(out IntPtr battlePassSystem, out string systemStatus)
                || battlePassSystem == IntPtr.Zero)
            {
                status = "BattlePassSystem unavailable: " + systemStatus;
                return false;
            }

            int freeCanGet = this.DailyClaimsCountAuraMonoBattlePassSlotsCanGet(
                battlePassSystem, "GetFreeBattlePassSlots", out string freeStatus);
            int paidCanGet = this.DailyClaimsCountAuraMonoBattlePassSlotsCanGet(
                battlePassSystem, "GetPayBattlePassSlots", out string paidStatus);

            // Second half of CanOneClaimAward: the cycle reward, which is earned exp over the
            // period's CycleRewardNeedPoint rather than a slot state.
            this.LogBpLoopRewardState(out bool loopClaimable, out int pendingCycles);

            int sent = 0;
            string detail = "free=" + freeCanGet + " paid=" + paidCanGet;
            if (freeCanGet + paidCanGet > 0)
            {
                if (this.TryClaimMiniBpAll(out string allStatus))
                {
                    sent++;
                }
                else
                {
                    detail += "; rewards FAILED " + allStatus;
                }
            }
            else
            {
                detail += " (" + freeStatus + "; " + paidStatus + ")";
            }

            if (loopClaimable)
            {
                if (this.TryClaimBpLoop(out string loopStatus))
                {
                    sent++;
                    detail += "; loop x" + pendingCycles;
                }
                else
                {
                    detail += "; loop FAILED " + loopStatus;
                }
            }

            status = detail + "; sent=" + sent;
            return sent > 0;
        }

        // One pass over every lit Dream node. This exists as its own pass instead of a case in the
        // event switch because NO Dream enum has a server RedPointType — RedPointUtility maps only
        // DreamNew and DreamOnline — so a dream reward can never arrive as a RedPointEvent, and the
        // switch that handles every other kind structurally cannot see these.
        //
        // Only the two claimable kinds are touched. DreamReward is an aggregate that goes out with
        // its children, and DreamUpgrade spends.
        private int DailyClaimsAutoClaimDreamNodes(out string status)
        {
            List<DailyClaimsRedPointNode> nodes = new List<DailyClaimsRedPointNode>();
            if (!this.DailyClaimsCollectActiveRedPoints(nodes, out string collectStatus))
            {
                status = "collect failed: " + collectStatus;
                return 0;
            }

            this.dailyClaimsDreamTypeByTarget.Clear();
            this.dailyClaimsDreamGameTaskByTask.Clear();
            this.dailyClaimsSweepSubmittedTaskIds.Clear();

            int sent = 0;
            int left = 0;
            int seen = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                int enumValue = nodes[i].EnumValue;
                if (enumValue != DailyClaimsRedPointEnumDreamTypeReward
                    && enumValue != DailyClaimsRedPointEnumDreamTaskReward)
                {
                    continue;
                }

                seen++;
                if (sent >= DailyClaimsAutoDreamPerPass)
                {
                    // Re-arm rather than push the rest through in one frame: the drain's contract is
                    // one action per tick, and the interval above keeps the next pass cheap.
                    this.dailyClaimsAutoPendingDream = true;
                    break;
                }

                int serverType = this.DailyClaimsToRedPointType(enumValue);
                if (this.DailyClaimsTryClaimForRedPoint(
                        enumValue, serverType, nodes[i].Id, out string what, out string claimStatus))
                {
                    sent++;
                    this.DailyClaimsLog("auto dream: claimed " + what + " (" + claimStatus + ")");
                }
                else
                {
                    left++;
                    this.DailyClaimsLog("auto dream: LEFT " + what + " (" + claimStatus + ")");
                }
            }

            status = "lit=" + seen + " claimed=" + sent + (left > 0 ? (" left=" + left) : string.Empty);
            return sent;
        }

        private bool DailyClaimsTryClaimForRedPoint(int clientEnum, int serverType, int id, out string what, out string status)
        {
            // Client-enum kinds first — these have no server type at all.
            switch (clientEnum)
            {
                case DailyClaimsRedPointEnumStickerActivityThemeReward:
                {
                    // Node id is the theme (StickerActivityThemeRewardNode resolves its parent
                    // through TableData.GetStickerTheme(base.Id)); the command needs that theme's
                    // activity as well.
                    what = "sticker theme " + id;
                    if (this.dailyClaimsStickerActivityByTheme.Count == 0)
                    {
                        List<DailyClaimsActivityTheme> themes = new List<DailyClaimsActivityTheme>();
                        this.DailyClaimsTryCollectActivityThemes("TableStickerThemes", themes, out _);
                        for (int i = 0; i < themes.Count; i++)
                        {
                            this.dailyClaimsStickerActivityByTheme[themes[i].ThemeId] = themes[i].ActivityId;
                        }
                    }

                    if (!this.dailyClaimsStickerActivityByTheme.TryGetValue(id, out int stickerActivityId)
                        || stickerActivityId <= 0)
                    {
                        status = "activityId for sticker theme " + id + " unknown";
                        return false;
                    }

                    what = "sticker theme " + id + " (activity " + stickerActivityId + ")";
                    return this.TryClaimDailyClaimsStickerThemeTiers(stickerActivityId, id, out status);
                }

                case DailyClaimsRedPointEnumDreamTaskReward:
                {
                    // Node id is the TableDreamTask key, and DreamSystem lights it exactly when the
                    // game task behind it is CanSubmit (or, for taskType 1, has a repeat reward
                    // waiting). DreamTaskTreePanel takes it with ClientSubmitTask either way — the
                    // same SubmitGameTaskNetworkCommand the BattlePass tasks already use — so this
                    // is a reward pickup on finished work, not a story quest being submitted behind
                    // the player's back (the exclusion in this file's header).
                    what = "dream task " + id;
                    if (!this.DailyClaimsTryGetDreamGameTaskId(id, out int dreamGameTaskId))
                    {
                        status = "no gameTaskId for dream task " + id;
                        return false;
                    }

                    what = "dream task " + id + " (game task " + dreamGameTaskId + ")";
                    if (this.dailyClaimsSweepSubmittedTaskIds.Contains(dreamGameTaskId))
                    {
                        status = "already submitted earlier in this sweep";
                        return false;
                    }

                    this.dailyClaimsSweepSubmittedTaskIds.Add(dreamGameTaskId);
                    return this.TrySubmitDailyClaimsGameTask(dreamGameTaskId, out status);
                }

                case DailyClaimsRedPointEnumDreamReward:
                    // Pure aggregate. RefreshRewardRedPoint never sets this one and DreamRewardNode
                    // has no flag of its own, so it is lit only while a DreamTypeReward or
                    // DreamTaskReward beneath it is lit, and it goes out with them. Reported rather
                    // than claimed — and kept off the mark-read pass, because it is the roll-up of a
                    // reward that really is waiting.
                    what = "dream " + id;
                    status = "aggregate - clears with its children";
                    return false;

                case DailyClaimsRedPointEnumDreamUpgrade:
                    // UpgradeDreamLevelCommand SPENDS. Same rule that keeps the SeaCycle exploration
                    // upgrade behind an explicit press: auto-claim never spends. (RefreshUpgradeRed
                    // Point is empty in this build, so it is not lit either way.)
                    what = "dream upgrade " + id;
                    status = "upgrade spends resources - never auto-claimed";
                    return false;

                case DailyClaimsRedPointEnumDreamTypeReward:
                    // Node id IS the dream TARGET (TableDreamTaskType key); the command also needs
                    // its parent dreamType. One claim drains every earned tier of that target, which
                    // is why the DreamTaskReward leaves below it clear as a side effect.
                    what = "dream target " + id;
                    if (this.dailyClaimsDreamTypeByTarget.Count == 0)
                    {
                        List<DailyClaimsDreamTarget> targets = new List<DailyClaimsDreamTarget>();
                        this.DailyClaimsTryCollectDreamTargets(targets, out _);
                        for (int i = 0; i < targets.Count; i++)
                        {
                            this.dailyClaimsDreamTypeByTarget[targets[i].TargetId] = targets[i].DreamType;
                        }
                    }

                    if (!this.dailyClaimsDreamTypeByTarget.TryGetValue(id, out int dreamType) || dreamType <= 0)
                    {
                        status = "dreamType for target " + id + " unknown";
                        return false;
                    }

                    return this.TryClaimDailyClaimsDreamTargetReward(dreamType, id, out status);

                case DailyClaimsRedPointEnumPartyFestivalTaskCanSubmit:
                case DailyClaimsRedPointEnumPartyOfficialTaskCanSubmit:
                    // TaskSystem.UpdatePartyTaskBranchRedPoints puts the TASK ID on both enums, and
                    // the party panel submits with the plain ClientSubmitTask — id 0 is the tab
                    // aggregate, not a task.
                    if (id <= 0)
                    {
                        what = "party task tab";
                        status = "aggregate node, nothing to submit";
                        return false;
                    }

                    what = "party task " + id;
                    if (this.dailyClaimsSweepSubmittedTaskIds.Contains(id))
                    {
                        status = "already submitted earlier in this sweep (the other party enum)";
                        return false;
                    }

                    this.dailyClaimsSweepSubmittedTaskIds.Add(id);
                    return this.TrySubmitDailyClaimsGameTask(id, out status);

                case DailyClaimsRedPointEnumActivityFreeReward:
                    // Node id is the activityId; the monopoly-style panels claim this exact dot with
                    // ReceiveActivityBPReward, which is the same command the activity sweep sends.
                    what = "activity free reward " + id;
                    return this.TryClaimDailyClaimsActivityBpReward(id, out status);
            }

            what = "type " + serverType + " id " + id;
            switch (serverType)
            {
                case DailyClaimsRedPointTypeBattlePassTaskCanSubmit:
                    what = "task " + id;
                    if (this.dailyClaimsSweepSubmittedTaskIds.Contains(id))
                    {
                        status = "already submitted earlier in this sweep";
                        return false;
                    }

                    this.dailyClaimsSweepSubmittedTaskIds.Add(id);
                    return this.TrySubmitDailyClaimsGameTask(id, out status);

                case DailyClaimsRedPointTypeSeriesReward:
                    if (id > DailyClaimsSeriesRewardBpPeriodMin && id < DailyClaimsSeriesRewardBpPeriodMax)
                    {
                        int issueId = id - DailyClaimsSeriesRewardBpPeriodMin;
                        what = "bp issue " + issueId;
                        return this.TryClaimDailyClaimsBpIssueReward(issueId, out status);
                    }

                    status = "series reward outside the BP-period window";
                    return false;

                case DailyClaimsRedPointTypePictorialTypeReward:
                    what = "collection type " + id;
                    return this.TryClaimDailyClaimsPictorialTypeReward(id, out status);

                case DailyClaimsRedPointTypePictorialAllSuitReward:
                    what = "all-suit " + id;
                    return this.TryClaimDailyClaimsPediaAllSuitReward(id, out status);

                case DailyClaimsRedPointTypeCollectCertification:
                    what = "certification " + id;
                    return this.TryClaimDailyClaimsCertificationReward(id, out status);

                case DailyClaimsRedPointTypePictorialSuitReward:
                    what = "suit " + id;
                    this.DailyClaimsAutoClaimSuitTiers(id);
                    status = "suit tiers swept";
                    return true;

                case DailyClaimsRedPointTypeActivityForOperation:
                    what = "activity " + id;
                    this.DailyClaimsAutoClaimActivity(id);
                    status = "activity swept";
                    return true;

                case DailyClaimsRedPointTypePetGrowthGift:
                    what = "pet growth " + id;
                    return this.TryClaimDailyClaimsPetGrowthRewards(id, out status);

                case DailyClaimsRedPointTypeTownGuides:
                case DailyClaimsRedPointTypeTownGuideNewNodeTask:
                case DailyClaimsRedPointTypeTownGuidesGrowth:
                    what = "town guide";
                    int sent = this.ClaimTownGuideRewards(out status);
                    return sent > 0;

                default:
                    // GachaGiftExist (1703) deliberately lands here. It does NOT mean "a gift is
                    // waiting": GachaSystem.RenderGiftFreeRedPointOfPool lights it when the pool's
                    // gift SHOP has any stock in it, and lights the separate GachaGiftFree (1700)
                    // only when something in there costs zero. Taking a GachaGiftExist entry would
                    // SPEND currency, which no sweep here is allowed to do — the same rule that
                    // keeps the SeaCycle exploration upgrade off the auto path. On 2026-08-18 seven
                    // of these were lit and GachaGiftFree was not lit at all, i.e. nothing free was
                    // actually pending.
                    status = "no claim mapped for this kind";
                    return false;
            }
        }

        // Manual sweep over every red point the client currently has lit. This is the answer to the
        // pre-world burst that the event hook structurally cannot see, and it doubles as the
        // diagnostic for "which dot is this, actually" — anything without a mapped claim is reported
        // with its enum and id rather than silently cleared.
        //
        // A dot is only cleared when its reward was actually claimed. Clearing an unclaimed one
        // would hide a real pending reward behind a tidy UI, which is worse than the dot.
        internal IEnumerator DailyClaimsClaimRedPointsRoutine()
        {
            this.dailyClaimsLastStatus = "Sweeping red points...";
            float sweepStartedAt = Time.realtimeSinceStartup;

            List<DailyClaimsRedPointNode> nodes = new List<DailyClaimsRedPointNode>();
            if (!this.DailyClaimsCollectActiveRedPoints(nodes, out string collectStatus))
            {
                this.dailyClaimsLastStatus = "Red point sweep failed: " + collectStatus;
                // TIER 1 — a failure is never gated (see FeatureLog.cs / the errors-to-log rule).
                FeatureLog.Fail("DailyClaims", this.dailyClaimsLastStatus);
                yield break;
            }

            // Rebuilt lazily per sweep — the dream table is static, but a stale map across a game
            // update would silently claim the wrong target.
            this.dailyClaimsDreamTypeByTarget.Clear();
            this.dailyClaimsDreamGameTaskByTask.Clear();
            this.dailyClaimsStickerActivityByTheme.Clear();
            this.dailyClaimsSweepSubmittedTaskIds.Clear();
            this.dailyClaimsSweepSuitTiers.Clear();
            this.dailyClaimsSweepSuitTiersLoaded = false;

            List<string> lines = new List<string> { "--- red point sweep (" + collectStatus + ") ---" };
            int claimed = 0;
            int unmapped = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                int enumValue = nodes[i].EnumValue;
                int id = nodes[i].Id;
                int serverType = this.DailyClaimsToRedPointType(enumValue);

                if (this.DailyClaimsTryClaimForRedPoint(enumValue, serverType, id, out string what, out string status))
                {
                    claimed++;
                    // The node key names the client enum exactly, so pass it rather than deriving it
                    // back from the server type (several enums share one type).
                    this.DailyClaimsAutoClearRedPoint(serverType, id, enumValue);
                    lines.Add("claimed " + what + " [enum=" + enumValue + " type=" + serverType + "] " + status);

                    // Pace on COMMANDS, not on nodes. The spacing exists so a sweep never fires a
                    // burst of reward commands — a node that sent nothing needs none of it. On the
                    // first live run 314 of 319 nodes fell through to LEFT and still waited 150 ms
                    // each, which is where 48 s of the sweep went.
                    yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
                    continue;
                }

                unmapped++;
                lines.Add("LEFT enum=" + enumValue + " id=" + id + " type=" + serverType + " (" + status + ")");

                // Still hand a frame back periodically: the walk itself is cheap, but a few hundred
                // table/AuraMono reads in one frame is a visible hitch.
                if ((i + 1) % DailyClaimsSweepFrameChunk == 0)
                {
                    yield return null;
                }
            }

            if (nodes.Count == 0)
            {
                lines.Add("nothing lit");
            }

            this.dailyClaimsLastStatus = "Red points: claimed=" + claimed + " left=" + unmapped
                + " of " + nodes.Count
                + " in " + (Time.realtimeSinceStartup - sweepStartedAt).ToString("0.0") + "s";
            // TIER 1 — the end-of-sweep result. This is the ONE line that says the auto-claim ran
            // and what it got; before the split it was gated by MasterLogDailyClaims (ships OFF),
            // so a sweep could only be inferred from FpsWatch phase timings (dc.signin, dc.mail…).
            // The per-node breakdown below stays Tier 2.
            FeatureLog.Life("DailyClaims", this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // ==========================================================================================
        // Mark All Read — the OTHER half of "the dots are still there"
        //
        // The 2026-08-18 sweep found 319 lit nodes and NOTHING claimable among them: 104
        // PictorialData, 29 CraftRecipe, 23 AvatarDefaultItem, 22 BuildItemUnlock… These are
        // "you have not looked at this yet" markers, not unclaimed rewards. They carry no reward at
        // all, and the game clears them when the player VIEWS the thing.
        //
        // The first cut of this button split them by whether RedPointUtility maps the client enum to
        // a server RedPointType and hand-rolled DeleteRedPoint for the mapped half — which cleared
        // 153 of 319 and left the rest, because:
        //   mapped   → DeleteRedPoint(type, id) is what the BASE Read() sends.
        //   unmapped → the map is not the whole story; the node subclass is. Each
        //              subsystem knows its own. RedPointManager.ReadRedPoint dispatches into that
        //              per-node Read() override, so this needs no per-subsystem code of its own —
        //              see the comment on TryDailyClaimsReadRedPoint below.
        //
        // This is destructive in one specific sense: there is no reward to confirm it worked, and no
        // way to un-read. It is a separate button from every claim for that reason.

        // RedPointManager.ReadRedPoint(RedPointEnum, int) — 2 scalar args, instance.
        //
        // THE right entry point, and the reason the first cut of this button only cleared half of
        // what it walked: RedPointTreeNode.Read() is VIRTUAL and overridden per node kind, so the
        // game already knows how to clear each subsystem —
        //   CraftRecipeNode        → CraftSystem.ReadRecipes(id)
        //   AvatarDefualtItemNode  → DeleteRedPoint(RedPointType.AvatarBag, id)  (type HARDCODED,
        //                            which is why RedPointUtility's enum→type map has no entry and
        //                            a hand-rolled DeleteRedPoint could never work for it)
        //   PictorialTabNode       → batches its PictorialData children in one read
        //   everything else        → the base Read(), i.e. DeleteRedPoint by the mapped type
        // Calling ReadRedPoint dispatches to whichever of those applies instead of reimplementing a
        // fraction of them.
        //
        // Read() no-ops unless the node is Active, so this must run BEFORE any local clear — which
        // is also why nothing is cleared locally here: the game's own path updates the node when the
        // clear actually lands, so the UI keeps telling the truth.
        private unsafe bool TryDailyClaimsReadRedPoint(int redPointEnum, int id)
        {
            if (redPointEnum <= 0 || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr manager = this.DailyClaimsResolveRedPointManager();
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(manager), "ReadRedPoint", 2);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            int enumValue = redPointEnum;
            int idValue = id;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&enumValue);
            args[1] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, manager, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // One chunk of the mark-read walk. SYNCHRONOUS on purpose: RedPointManager is resolved inside
        // each helper call and never survives a frame boundary (CI lint W1), and keeping the whole
        // chunk in one frame is what lets the spacing move from per-node to per-chunk.
        private void DailyClaimsMarkReadChunkStep(
            List<DailyClaimsRedPointNode> nodes,
            int start,
            int count,
            Dictionary<int, int> perEnum,
            List<string> lines,
            ref int read,
            ref int failed)
        {
            int end = Math.Min(start + count, nodes.Count);
            for (int i = start; i < end; i++)
            {
                int enumValue = nodes[i].EnumValue;
                int id = nodes[i].Id;
                bool ok;

                // Daily tabs are the one kind Read() cannot serve: no server type to delete by and
                // no Read() override, so the game clears them through their own activity command.
                if (enumValue == DailyClaimsRedPointEnumActivityDailyTab
                    || enumValue == DailyClaimsRedPointEnumActivityNewDay)
                {
                    ok = this.TryDailyClaimsClearActivityDailyTab(id, out string tabStatus);
                    if (!ok)
                    {
                        lines.Add("daily tab id=" + id + " NOT cleared: " + tabStatus);
                    }
                }
                else
                {
                    // Straight into the game's own polymorphic Read(). No special casing — the node
                    // subclass knows which subsystem command it needs.
                    ok = this.TryDailyClaimsReadRedPoint(enumValue, id);
                }

                if (ok)
                {
                    read++;
                    perEnum.TryGetValue(enumValue, out int n);
                    perEnum[enumValue] = n + 1;
                }
                else
                {
                    failed++;
                }
            }
        }

        // The button: walk every lit node, mark read what CAN be marked, report the rest.
        internal IEnumerator DailyClaimsMarkRedPointsReadRoutine()
        {
            this.dailyClaimsLastStatus = "Marking red points read...";
            float markStartedAt = Time.realtimeSinceStartup;

            List<DailyClaimsRedPointNode> nodes = new List<DailyClaimsRedPointNode>();
            if (!this.DailyClaimsCollectActiveRedPoints(nodes, out string collectStatus))
            {
                this.dailyClaimsLastStatus = "Mark-read failed: " + collectStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<string> lines = new List<string> { "--- mark red points read (" + collectStatus + ") ---" };
            Dictionary<int, int> perEnum = new Dictionary<int, int>();
            int read = 0;
            int failed = 0;

            // A chunk at a time, with the spacing between chunks rather than between nodes.
            for (int start = 0; start < nodes.Count; start += DailyClaimsMarkReadChunk)
            {
                this.DailyClaimsMarkReadChunkStep(nodes, start, DailyClaimsMarkReadChunk,
                    perEnum, lines, ref read, ref failed);
                yield return ModWait.Realtime(DailyClaimsBulkCommandSpacingSeconds);
            }

            // Per-enum tally: a kind that stays lit after this is the one to investigate, and the
            // count says whether its Read() ran at all.
            List<string> byEnum = new List<string>(perEnum.Count);
            foreach (KeyValuePair<int, int> kv in perEnum)
            {
                byEnum.Add("enum" + kv.Key + "=" + kv.Value);
            }

            lines.Add("read=" + read + " failed=" + failed + " [" + string.Join(", ", byEnum.ToArray()) + "]");

            this.dailyClaimsLastStatus = "Mark read: " + read + " of " + nodes.Count
                + (failed > 0 ? (" (" + failed + " failed)") : string.Empty)
                + " in " + (Time.realtimeSinceStartup - markStartedAt).ToString("0.0") + "s";
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private const int DailyClaimsRedPointEnumActivityTaskReward = 20776;
        private const int DailyClaimsRedPointEnumPictorialSuitReward = 9001;

        private IntPtr dailyClaimsAuraRedPointProtocolClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraRedPointUtilityClass = IntPtr.Zero;

        // RedPointProtocolManager.DeleteRedPoint(RedPointType, int, uint) — 3 scalar args, static.
        private unsafe bool TryDailyClaimsDeleteRedPointOnServer(int redPointType, int idParam)
        {
            if (redPointType <= 0
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.dailyClaimsAuraRedPointProtocolClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraRedPointProtocolClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.RedPoint.RedPointProtocolManager");
                if (this.dailyClaimsAuraRedPointProtocolClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraRedPointProtocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.RedPoint", "RedPointProtocolManager");
                }
            }

            if (this.dailyClaimsAuraRedPointProtocolClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                this.dailyClaimsAuraRedPointProtocolClass, "DeleteRedPoint", 3);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            int typeValue = redPointType;
            int idValue = idParam;
            uint netId = 0u;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&typeValue);
            args[1] = (IntPtr)(&idValue);
            args[2] = (IntPtr)(&netId);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // RedPointUtility.ToRedPointEnum(this RedPointType) — static extension, one scalar arg.
        // Returns RedPointEnum.None (0) for server types the client does not map.
        private unsafe int DailyClaimsToRedPointEnum(int redPointType)
        {
            if (redPointType <= 0
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraRedPointUtilityClass = this.FindAuraMonoClassByFullName(
                    "XDTGameSystem.GameplaySystem.RedPoint.RedPointUtility");
                if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraRedPointUtilityClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.RedPoint", "RedPointUtility");
                }
            }

            if (this.dailyClaimsAuraRedPointUtilityClass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                this.dailyClaimsAuraRedPointUtilityClass, "ToRedPointEnum", 1);
            if (method == IntPtr.Zero)
            {
                return 0;
            }

            int typeValue = redPointType;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&typeValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            return this.TryUnboxMonoInt32(boxed, out int enumValue) ? enumValue : 0;
        }

        // RedPointManager.UpdateRedPointData(RedPointEnum, int, bool) — 3 scalar args, instance.
        private unsafe bool TryDailyClaimsClearRedPointLocally(int redPointEnum, int nodeId)
        {
            if (redPointEnum <= 0 || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr manager = this.DailyClaimsResolveRedPointManager();
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(manager), "UpdateRedPointData", 3);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            int enumValue = redPointEnum;
            int idValue = nodeId;
            bool active = false;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&enumValue);
            args[1] = (IntPtr)(&idValue);
            args[2] = (IntPtr)(&active);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, manager, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // The whole clear for one claimed reward. serverRedPointType == 0 means the kind has no
        // server-side red point (activity missions), so only the client node is cleared, under the
        // enum the caller names.
        private void DailyClaimsAutoClearRedPoint(int serverRedPointType, int idParam, int clientEnumOverride)
        {
            // Every step is logged with its result: both AuraMono calls return a bool that used to be
            // discarded, which made a silently failing clear indistinguishable from a dot the game
            // keeps for its own reasons. Diagnosing "the dot is still there" needs this.
            string serverPart = "server=n/a";
            if (serverRedPointType > 0)
            {
                bool deleted = this.TryDailyClaimsDeleteRedPointOnServer(serverRedPointType, idParam);
                serverPart = "server=" + (deleted ? "ok" : "FAILED") + "(type=" + serverRedPointType + ")";
            }

            int clientEnum = clientEnumOverride > 0
                ? clientEnumOverride
                : this.DailyClaimsToRedPointEnum(serverRedPointType);
            if (clientEnum <= 0)
            {
                this.DailyClaimsLog("redpoint clear id=" + idParam + " " + serverPart
                    + " client=skipped(no enum" + (clientEnumOverride > 0 ? "" : " from type " + serverRedPointType) + ")");
                return;
            }

            // Suits: one node per TablePediaSuitReward row of the suit, so clearing by suitId would
            // miss every one of them.
            if (clientEnum == DailyClaimsRedPointEnumPictorialSuitReward)
            {
                int cleared = 0;
                int rows = 0;
                List<DailyClaimsSuitRewardTier> tiers = this.DailyClaimsSweepSuitTiers();
                {
                    for (int i = 0; i < tiers.Count; i++)
                    {
                        if (tiers[i].SuitId == idParam && tiers[i].RowId > 0)
                        {
                            rows++;
                            if (this.TryDailyClaimsClearRedPointLocally(clientEnum, tiers[i].RowId))
                            {
                                cleared++;
                            }
                        }
                    }
                }

                this.DailyClaimsLog("redpoint clear suit=" + idParam + " " + serverPart
                    + " client=enum" + clientEnum + " tierRows=" + rows + " cleared=" + cleared);
                return;
            }

            bool localOk = this.TryDailyClaimsClearRedPointLocally(clientEnum, idParam);
            this.DailyClaimsLog("redpoint clear id=" + idParam + " " + serverPart
                + " client=enum" + clientEnum + " " + (localOk ? "ok" : "FAILED"));
        }

        private static void DailyClaimsAutoEnqueue(List<int> queue, int value)
        {
            if (value <= 0 || queue.Count >= DailyClaimsAutoMaxQueued || queue.Contains(value))
            {
                return;
            }

            queue.Add(value);
        }

        private void DailyClaimsAutoEnqueueTask(int taskId, int serverRedPointType, int clientRedPointEnum)
        {
            if (taskId <= 0 || this.dailyClaimsAutoTaskJobs.Count >= DailyClaimsAutoMaxQueued)
            {
                return;
            }

            for (int i = 0; i < this.dailyClaimsAutoTaskJobs.Count; i++)
            {
                if (this.dailyClaimsAutoTaskJobs[i].TaskId == taskId)
                {
                    return;
                }
            }

            this.dailyClaimsAutoTaskJobs.Add(new DailyClaimsAutoTaskJob
            {
                TaskId = taskId,
                ServerRedPointType = serverRedPointType,
                ClientRedPointEnum = clientRedPointEnum
            });
        }

        private static bool DailyClaimsAutoTakeFirst(List<int> queue, out int value)
        {
            if (queue.Count == 0)
            {
                value = 0;
                return false;
            }

            value = queue[0];
            queue.RemoveAt(0);
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Drain — exactly one action per tick, and never while a manual sweep holds the feature.
        // ----------------------------------------------------------------------------------------

        private void ProcessDailyClaimsAutoClaimOnUpdate()
        {
            this.EnsureDailyClaimsAutoClaimRegistered();

            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            // A manual Claim All is a long paced coroutine; racing it would double-send.
            if (this.dailyClaimsCoroutine != null)
            {
                return;
            }

            if (Time.realtimeSinceStartup < this.dailyClaimsAutoNextDrainAt)
            {
                return;
            }

            if (!this.TryDrainDailyClaimsAutoStep())
            {
                return;
            }

            this.dailyClaimsAutoNextDrainAt = Time.realtimeSinceStartup + DailyClaimsAutoDrainIntervalSeconds;
        }

        // Returns true when a step actually did something (so the pacing clock is only armed for
        // real work, and an empty queue costs one compare per frame).
        private bool TryDrainDailyClaimsAutoStep()
        {
            // --- targeted, id-carrying claims first: these came straight from a red point ---------
            if (this.dailyClaimsAutoTaskJobs.Count > 0)
            {
                Breadcrumbs.Phase("dc.task");
                DailyClaimsAutoTaskJob job = this.dailyClaimsAutoTaskJobs[0];
                this.dailyClaimsAutoTaskJobs.RemoveAt(0);
                bool ok = this.TrySubmitDailyClaimsGameTask(job.TaskId, out string status);
                if (ok)
                {
                    this.DailyClaimsAutoClearRedPoint(job.ServerRedPointType, job.TaskId, job.ClientRedPointEnum);
                }

                this.DailyClaimsAutoReport(ok, "submit task " + job.TaskId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoIssueIds, out int issueId))
            {
                Breadcrumbs.Phase("dc.bpissue");
                bool ok = this.TryClaimDailyClaimsBpIssueReward(issueId, out string status);
                if (ok)
                {
                    // The point is keyed by the series TRIGGER id, not the issue id we claim with.
                    this.DailyClaimsAutoClearRedPoint(
                        DailyClaimsRedPointTypeSeriesReward, issueId + DailyClaimsSeriesRewardBpPeriodMin, 0);
                }

                this.DailyClaimsAutoReport(ok, "bp issue " + issueId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoPictorialTypes, out int pictorialType))
            {
                Breadcrumbs.Phase("dc.pictorial");
                bool ok = this.TryClaimDailyClaimsPictorialTypeReward(pictorialType, out string status);
                if (ok)
                {
                    this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypePictorialTypeReward, pictorialType, 0);
                }

                this.DailyClaimsAutoReport(ok, "collection type " + pictorialType, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoAllSuitIds, out int allSuitId))
            {
                Breadcrumbs.Phase("dc.allsuit");
                bool ok = this.TryClaimDailyClaimsPediaAllSuitReward(allSuitId, out string status);
                if (ok)
                {
                    this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypePictorialAllSuitReward, allSuitId, 0);
                }

                this.DailyClaimsAutoReport(ok, "all-suit " + allSuitId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoCertIds, out int certId))
            {
                Breadcrumbs.Phase("dc.cert");
                bool ok = this.TryClaimDailyClaimsCertificationReward(certId, out string status);
                if (ok)
                {
                    this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypeCollectCertification, certId, 0);
                }

                this.DailyClaimsAutoReport(ok, "certification " + certId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoSuitIds, out int suitId))
            {
                // The red point names the suit, not which tier is owed, so ask the service how many
                // pieces are held and claim every tier at or below it — the same gate the manual
                // sweep uses, just for one suit.
                Breadcrumbs.Phase("dc.suit");
                this.DailyClaimsAutoClaimSuitTiers(suitId);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoActivityIds, out int activityId))
            {
                Breadcrumbs.Phase("dc.activity");
                this.DailyClaimsAutoClaimActivity(activityId);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoPetNetIds, out int petNetId))
            {
                // One pet, every growth level it has reached and not yet taken. The gift red point
                // is server-owned, so clearing it here is only honest because the claim gate below
                // is the same one the game's own tip panel uses to enable the button.
                Breadcrumbs.Phase("dc.petgrowth");
                bool ok = this.TryClaimDailyClaimsPetGrowthRewards(petNetId, out string status);
                if (ok)
                {
                    this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypePetGrowthGift, petNetId, 0);
                }

                this.DailyClaimsAutoReport(ok, "pet growth " + petNetId, status);
                return true;
            }

            // --- flag-driven claims --------------------------------------------------------------
            // The pending flag is kept (not consumed) while the min-interval backstop holds it off,
            // so a claim that is merely too early is DELAYED rather than dropped, and the drain
            // falls through to the next step instead of stalling on it.
            if (this.dailyClaimsAutoPendingWhalefall
                && Time.realtimeSinceStartup >= this.dailyClaimsAutoWhalefallNextAllowedAt)
            {
                this.dailyClaimsAutoPendingWhalefall = false;
                this.dailyClaimsAutoWhalefallNextAllowedAt =
                    Time.realtimeSinceStartup + DailyClaimsAutoWhalefallMinIntervalSeconds;
                Breadcrumbs.Phase("dc.whalefall");

                // Reports through the queue helper. A request that is merely in progress is not
                // queued, so a pass with nothing finished sends nothing and the submit's own
                // TaskUpdated cannot loop.
                this.DailyClaimsAutoQueueWhalefallRequests();
                return true;
            }

            if (this.dailyClaimsAutoPendingSticker
                && Time.realtimeSinceStartup >= this.dailyClaimsAutoStickerNextAllowedAt)
            {
                this.dailyClaimsAutoPendingSticker = false;
                this.dailyClaimsAutoStickerNextAllowedAt =
                    Time.realtimeSinceStartup + DailyClaimsAutoStickerMinIntervalSeconds;
                Breadcrumbs.Phase("dc.sticker");
                int stickerSent = this.DailyClaimsAutoClaimStickerNodes(out string stickerStatus);
                this.DailyClaimsAutoReport(stickerSent > 0, "sticker", stickerStatus);
                return true;
            }

            if (this.dailyClaimsAutoPendingBattlePass
                && Time.realtimeSinceStartup >= this.dailyClaimsAutoBattlePassNextAllowedAt)
            {
                this.dailyClaimsAutoPendingBattlePass = false;
                this.dailyClaimsAutoBattlePassNextAllowedAt =
                    Time.realtimeSinceStartup + DailyClaimsAutoBattlePassMinIntervalSeconds;
                Breadcrumbs.Phase("dc.bp");
                bool bpSent = this.DailyClaimsAutoClaimBattlePass(out string bpStatus);
                this.DailyClaimsAutoReport(bpSent, "battle pass", bpStatus);
                return true;
            }

            if (this.dailyClaimsAutoPendingDream && Time.realtimeSinceStartup >= this.dailyClaimsAutoDreamNextAllowedAt)
            {
                this.dailyClaimsAutoPendingDream = false;
                this.dailyClaimsAutoDreamNextAllowedAt =
                    Time.realtimeSinceStartup + DailyClaimsAutoDreamMinIntervalSeconds;
                Breadcrumbs.Phase("dc.dream");
                int dreamSent = this.DailyClaimsAutoClaimDreamNodes(out string dreamStatus);
                this.DailyClaimsAutoReport(dreamSent > 0, "dream", dreamStatus);
                return true;
            }

            if (this.dailyClaimsAutoPendingMail && Time.realtimeSinceStartup >= this.dailyClaimsAutoMailNextAllowedAt)
            {
                this.dailyClaimsAutoPendingMail = false;
                this.dailyClaimsAutoMailEchoUntil = Time.realtimeSinceStartup + DailyClaimsAutoMailEchoWindowSeconds;
                this.dailyClaimsAutoMailNextAllowedAt = Time.realtimeSinceStartup + DailyClaimsAutoMailMinIntervalSeconds;
                Breadcrumbs.Phase("dc.mail");
                bool ok = this.TryClaimMailAll(out string status);
                this.DailyClaimsAutoReport(ok, "mail", status);
                return true;
            }

            if (this.dailyClaimsAutoPendingTownGuide)
            {
                this.dailyClaimsAutoPendingTownGuide = false;
                Breadcrumbs.Phase("dc.townguide");
                int sent = this.ClaimTownGuideRewards(out string detail);
                this.DailyClaimsAutoReport(sent > 0, "town guide (sent=" + sent + ")", detail);
                return true;
            }

            // --- world-ready catch-up --------------------------------------------------------------
            if (this.dailyClaimsAutoCatchUpBattlePass)
            {
                this.dailyClaimsAutoCatchUpBattlePass = false;
                Breadcrumbs.Phase("dc.battlepass");
                bool bpCaughtUp = this.DailyClaimsAutoClaimBattlePass(out string bpCatchUpStatus);
                this.DailyClaimsAutoReport(bpCaughtUp, "catch-up battle pass", bpCatchUpStatus);
                return true;
            }

            if (this.dailyClaimsAutoCatchUpActivities)
            {
                this.dailyClaimsAutoCatchUpActivities = false;
                Breadcrumbs.Phase("dc.signin");
                int sent = this.DailyClaimsAutoClaimSignInOnce(out string detail);
                this.DailyClaimsAutoReport(sent > 0, "catch-up sign-in (sent=" + sent + ")", detail);
                return true;
            }

            if (this.dailyClaimsAutoCatchUpTasks)
            {
                this.dailyClaimsAutoCatchUpTasks = false;
                Breadcrumbs.Phase("dc.catchuptasks");
                this.DailyClaimsAutoQueueSubmittableTasks();
                return true;
            }

            if (this.dailyClaimsAutoCatchUpWhalefall)
            {
                this.dailyClaimsAutoCatchUpWhalefall = false;
                Breadcrumbs.Phase("dc.whalefall");
                this.DailyClaimsAutoQueueWhalefallRequests();
                return true;
            }

            // --- mark-read, LAST ------------------------------------------------------------------
            // Deliberately below every claim: a claimable dot must be claimed, never merely hidden.
            if (this.dailyClaimsAutoPendingMarkRead
                && Time.realtimeSinceStartup >= this.dailyClaimsAutoMarkReadNextAllowedAt)
            {
                this.dailyClaimsAutoPendingMarkRead = false;
                this.dailyClaimsAutoMarkReadNextAllowedAt =
                    Time.realtimeSinceStartup + DailyClaimsAutoMarkReadMinIntervalSeconds;
                Breadcrumbs.Phase("dc.markread");
                int markRead = this.DailyClaimsAutoMarkSeenMarkersRead(out string markStatus, out bool more);
                if (more)
                {
                    // Hit the per-pass cap — keep going on the next interval instead of walking a
                    // few hundred nodes in one frame.
                    this.dailyClaimsAutoPendingMarkRead = true;
                }

                this.DailyClaimsAutoReport(markRead > 0, "mark read (" + markRead + ")", markStatus);
                return true;
            }

            return false;
        }

        // One mark-read pass over the lit nodes. SYNCHRONOUS: RedPointManager is resolved inside each
        // helper and no raw pointer survives a frame boundary (CI lint W1), so the pass is capped
        // instead of chunked across yields.
        //
        // The node id is never derived from the event, and that is the whole point. A learned pose is
        // stored under a SYNTHETIC index: RedPointManager.OnUpdateRedpoint routes CatInteraction
        // through UpdateRedPointDataGenericKey(enum, (long)netId * 1000 + idParam), which allocates a
        // sequential _genericIndex and keeps the real key in _int2Key — wiped by ClearData() on every
        // level load. Only _nodeDic knows the id ReadRedPoint wants, so the pass reads it from there.
        private int DailyClaimsAutoMarkSeenMarkersRead(out string status, out bool more)
        {
            more = false;
            List<DailyClaimsRedPointNode> nodes = new List<DailyClaimsRedPointNode>();
            if (!this.DailyClaimsCollectActiveRedPoints(nodes, out status))
            {
                return 0;
            }

            int read = 0;
            int skipped = 0;
            int failed = 0;
            int i = 0;
            for (; i < nodes.Count; i++)
            {
                if (read >= DailyClaimsAutoMarkReadPerPass)
                {
                    more = true;
                    break;
                }

                int enumValue = nodes[i].EnumValue;
                int id = nodes[i].Id;
                if (this.DailyClaimsRedPointHasClaimMapping(enumValue, this.DailyClaimsToRedPointType(enumValue)))
                {
                    skipped++;
                    continue;
                }

                bool ok;
                if (enumValue == DailyClaimsRedPointEnumActivityDailyTab
                    || enumValue == DailyClaimsRedPointEnumActivityNewDay)
                {
                    // No server type to delete by and no Read() override — the game clears these
                    // through their own activity command, same as the manual button does.
                    ok = this.TryDailyClaimsClearActivityDailyTab(id, out _);
                }
                else
                {
                    ok = this.TryDailyClaimsReadRedPoint(enumValue, id);
                }

                if (ok)
                {
                    read++;
                }
                else
                {
                    failed++;
                }
            }

            status = "read=" + read + " skipped=" + skipped + " failed=" + failed
                + " of " + nodes.Count + (more ? " (more next pass)" : string.Empty);
            return read;
        }

        // Does the CLAIM path know this kind? Anything it does must be left alone by the mark-read
        // pass: reading the dot would not lose the reward server-side, but it would lose the only
        // signal that one is waiting, which is exactly the trade the separate manual button exists
        // to keep explicit.
        //
        // KEEP IN SYNC with DailyClaimsTryClaimForRedPoint — every kind it dispatches on belongs
        // here. A kind missing from this list is a reward the auto pass would quietly hide.
        private bool DailyClaimsRedPointHasClaimMapping(int clientEnum, int serverType)
        {
            switch (clientEnum)
            {
                case DailyClaimsRedPointEnumStickerActivityThemeReward:
                case DailyClaimsRedPointEnumDreamTypeReward:
                case DailyClaimsRedPointEnumDreamTaskReward:
                // Neither of these is claimable, but neither may be hidden: DreamReward is the
                // roll-up of a child reward that IS waiting, and DreamUpgrade is a spend the player
                // has to decide on.
                case DailyClaimsRedPointEnumDreamReward:
                case DailyClaimsRedPointEnumDreamUpgrade:
                case DailyClaimsRedPointEnumPartyFestivalTaskCanSubmit:
                case DailyClaimsRedPointEnumPartyOfficialTaskCanSubmit:
                case DailyClaimsRedPointEnumActivityFreeReward:
                    return true;

                // Not dispatched by the claim switch, but the task queue submits these on its own
                // schedule — reading the dot first would hide a mission that is still pending.
                case DailyClaimsRedPointEnumActivityTaskReward:
                    return true;
            }

            switch (serverType)
            {
                case DailyClaimsRedPointTypeBattlePassTaskCanSubmit:
                case DailyClaimsRedPointTypeSeriesReward:
                case DailyClaimsRedPointTypePictorialTypeReward:
                case DailyClaimsRedPointTypePictorialAllSuitReward:
                case DailyClaimsRedPointTypeCollectCertification:
                case DailyClaimsRedPointTypePictorialSuitReward:
                case DailyClaimsRedPointTypeActivityForOperation:
                case DailyClaimsRedPointTypePetGrowthGift:
                case DailyClaimsRedPointTypeTownGuides:
                case DailyClaimsRedPointTypeTownGuideNewNodeTask:
                case DailyClaimsRedPointTypeTownGuidesGrowth:
                    return true;
            }

            return false;
        }

        // Both task collections in one pass: _tasks (battle-pass challenges) and
        // _operationActivityTasks (event dailies). Only ids are queued — the sends are paced by the
        // drain like everything else. The SeaCycle exploration upgrade is NOT queued: auto-claim
        // never spends a ticket.
        private void DailyClaimsAutoQueueSubmittableTasks()
        {
            int queued = 0;

            List<int> buffer = this.dailyClaimsTaskIdBuffer;
            if (this.DailyClaimsTryCollectSubmittableActivityMissionIds(buffer, out string missionStatus))
            {
                for (int i = 0; i < buffer.Count; i++)
                {
                    // Activity missions have no SERVER red point — TaskSystem sets the client node
                    // (RedPointEnum.ActivityTaskReward) directly — so only that one gets cleared.
                    this.DailyClaimsAutoEnqueueTask(buffer[i], 0, DailyClaimsRedPointEnumActivityTaskReward);
                    queued++;
                }
            }

            if (this.DailyClaimsTryCollectSubmittableTaskIds(buffer, out string taskStatus))
            {
                for (int i = 0; i < buffer.Count; i++)
                {
                    if (this.DailyClaimsIsBattlePassTask(buffer[i], out _))
                    {
                        this.DailyClaimsAutoEnqueueTask(
                            buffer[i], DailyClaimsRedPointTypeBattlePassTaskCanSubmit, 0);
                        queued++;
                    }
                }
            }

            this.DailyClaimsAutoReport(queued > 0, "catch-up tasks (queued=" + queued + ")",
                missionStatus + "; " + taskStatus);
        }

        private void DailyClaimsAutoQueueWhalefallRequests()
        {
            List<int> buffer = this.dailyClaimsSeaCycleTaskIdBuffer;
            if (!this.DailyClaimsTryGetSeaCycleDailyTaskIds(buffer, out string listStatus))
            {
                this.DailyClaimsAutoReport(false, "catch-up whalefall", listStatus);
                return;
            }

            int queued = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (this.TryGetGameTaskStateAura(buffer[i], out int state, out _)
                    && state == DailyClaimsGameTaskStateCanSubmit)
                {
                    // Whalefall requests carry no red point of either kind.
                    this.DailyClaimsAutoEnqueueTask(buffer[i], 0, 0);
                    queued++;
                }
            }

            this.DailyClaimsAutoReport(queued > 0, "catch-up whalefall (queued=" + queued + ")", listStatus);
        }

        // Sign-in sweep with a session-wide attempt memo. ClaimSignInRewards treats a dispatched
        // command as success (there is no ACK to wait on), so a node the server keeps refusing looks
        // claimed every time and comes back on the next world change. Remembering what was already
        // tried turns that from an endless retry into one attempt.
        private int DailyClaimsAutoClaimSignInOnce(out string detail)
        {
            detail = string.Empty;
            if (!this.TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                detail = "IOperationActivityCenterService unavailable: " + serviceStatus;
                return 0;
            }

            List<int> activityIds = this.dailyClaimsActivityIdBuffer;
            activityIds.Clear();
            if (!this.DailyClaimsTryGetAliveActivityIds(binding, activityIds, out string listStatus))
            {
                detail = "GetAliveActivityIds failed: " + listStatus;
                return 0;
            }

            List<int> alive = new List<int>(activityIds);
            List<string> lines = new List<string>();
            List<string> nodeParts = this.dailyClaimsNodeStateBuffer;
            int sent = 0;
            int skippedRepeat = 0;

            for (int i = 0; i < alive.Count; i++)
            {
                int activityId = alive[i];
                nodeParts.Clear();
                if (!this.DailyClaimsTryGetActivityNodeStateNames(binding, activityId, nodeParts, out string nodeStatus))
                {
                    lines.Add("activityId=" + activityId + " state read failed: " + nodeStatus);
                    continue;
                }

                for (int n = 0; n < nodeParts.Count; n++)
                {
                    if (!string.Equals(nodeParts[n], "WaitClaim", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int memo = activityId * 100 + n;
                    if (this.dailyClaimsAutoAttemptedActivityNodes.Contains(memo))
                    {
                        skippedRepeat++;
                        continue;
                    }

                    this.dailyClaimsAutoAttemptedActivityNodes.Add(memo);
                    if (this.TryReceiveActivityReward(activityId, n, out string claimStatus))
                    {
                        sent++;
                        lines.Add("sent activityId=" + activityId + " nodeIndex=" + n + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED activityId=" + activityId + " nodeIndex=" + n + " (" + claimStatus + ")");
                    }
                }
            }

            lines.Add("(sent counts DISPATCHED commands, not server acceptance; repeats skipped="
                + skippedRepeat + ")");
            detail = string.Join("\n", lines.ToArray());
            return sent;
        }

        // One activity: its WaitClaim reward nodes, then its activity-BP track.
        private void DailyClaimsAutoClaimActivity(int activityId)
        {
            if (!this.TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                this.DailyClaimsAutoReport(false, "activity " + activityId, serviceStatus);
                return;
            }

            int sent = 0;
            List<string> nodeParts = this.dailyClaimsNodeStateBuffer;
            nodeParts.Clear();
            if (this.DailyClaimsTryGetActivityNodeStateNames(binding, activityId, nodeParts, out string nodeStatus))
            {
                for (int n = 0; n < nodeParts.Count; n++)
                {
                    if (!string.Equals(nodeParts[n], "WaitClaim", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (this.TryReceiveActivityReward(activityId, n, out _))
                    {
                        sent++;
                    }
                }
            }

            this.TryClaimDailyClaimsActivityBpReward(activityId, out string bpStatus);
            this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypeActivityForOperation, activityId, 0);
            this.DailyClaimsAutoReport(sent > 0, "activity " + activityId + " (nodes=" + sent + ")",
                nodeStatus + "; " + bpStatus);
        }

        private void DailyClaimsAutoClaimSuitTiers(int suitId)
        {
            List<DailyClaimsSuitRewardTier> tiers = this.DailyClaimsSweepSuitTiers();
            if (tiers.Count == 0)
            {
                this.DailyClaimsAutoReport(false, "suit " + suitId, "TablePediaSuitRewards unavailable");
                return;
            }

            List<int> owned = new List<int>(1);
            List<int> ownedCounts = new List<int>(1);
            List<int> single = new List<int>(1) { suitId };
            if (!this.DailyClaimsFilterOwnedSuitChunk(single, 0, 1, owned, ownedCounts, out string gateStatus)
                || owned.Count == 0)
            {
                this.DailyClaimsAutoReport(false, "suit " + suitId, gateStatus);
                return;
            }

            int hasNum = ownedCounts[0];
            int sent = 0;
            for (int t = 0; t < tiers.Count; t++)
            {
                if (tiers[t].SuitId != suitId || tiers[t].Quantity > hasNum)
                {
                    continue;
                }

                if (this.TryClaimDailyClaimsPictorialSuitReward(suitId, tiers[t].Quantity, out _))
                {
                    sent++;
                }
            }

            if (sent > 0)
            {
                // Suits are the case where the game itself never sends the "gone" event once the
                // last tier is taken, so this clear is the only thing that removes the dot.
                this.DailyClaimsAutoClearRedPoint(DailyClaimsRedPointTypePictorialSuitReward, suitId, 0);
            }

            this.DailyClaimsAutoReport(sent > 0, "suit " + suitId + " (tiers=" + sent + ")", gateStatus);
        }

        private void DailyClaimsAutoReport(bool claimed, string what, string detail)
        {
            if (claimed)
            {
                this.dailyClaimsAutoClaimedCount++;
            }

            this.dailyClaimsAutoLastStatus = "Auto: " + what + (claimed ? " ok" : " skipped");
            this.DailyClaimsLog("auto-claim " + what + " -> " + (claimed ? "ok" : "skipped")
                + " | " + (detail ?? string.Empty));
        }

        // Status line for the UI: what auto-claim is doing and how much is waiting.
        internal string DailyClaimsAutoClaimStatusText()
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return "Auto-claim off.";
            }

            int queued = this.dailyClaimsAutoTaskJobs.Count
                + this.dailyClaimsAutoPictorialTypes.Count
                + this.dailyClaimsAutoSuitIds.Count
                + this.dailyClaimsAutoAllSuitIds.Count
                + this.dailyClaimsAutoCertIds.Count
                + this.dailyClaimsAutoIssueIds.Count
                + this.dailyClaimsAutoActivityIds.Count
                + this.dailyClaimsAutoPetNetIds.Count;

            return "Auto-claim on (hooks=" + (this.dailyClaimsAutoHooksRegistered ? "live" : "pending")
                + ", claimed=" + this.dailyClaimsAutoClaimedCount
                + ", queued=" + queued
                + (this.dailyClaimsAutoPendingMarkRead ? ", mark-read pending" : string.Empty)
                + "). " + this.dailyClaimsAutoLastStatus;
        }
    }
}
