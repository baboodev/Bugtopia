using System;
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

        private const string DailyClaimsAutoWorldReadyCallbackName = "DailyClaimsAutoClaim";

        // One send per tick at this spacing — the same "no burst the game never produces" rule the
        // manual sweeps follow.
        private const float DailyClaimsAutoDrainIntervalSeconds = 0.4f;

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
        private readonly List<int> dailyClaimsAutoTaskIds = new List<int>(16);
        private readonly List<int> dailyClaimsAutoPictorialTypes = new List<int>(8);
        private readonly List<int> dailyClaimsAutoSuitIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoAllSuitIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoCertIds = new List<int>(16);
        private readonly List<int> dailyClaimsAutoIssueIds = new List<int>(8);
        private readonly List<int> dailyClaimsAutoActivityIds = new List<int>(8);
        private bool dailyClaimsAutoPendingTownGuide;
        private bool dailyClaimsAutoPendingMail;

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

                this.dailyClaimsAutoHooksRegistered = redPoint || activityTasks || mail;
                this.DailyClaimsLog("auto-claim hooks registered: redPoint=" + redPoint
                    + " activityTasks=" + activityTasks + " mail=" + mail);

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
            this.dailyClaimsAutoCatchUpActivities = true;
            this.dailyClaimsAutoCatchUpTasks = true;
            this.dailyClaimsAutoCatchUpBattlePass = true;
            this.dailyClaimsAutoCatchUpWhalefall = true;
            this.dailyClaimsAutoPendingTownGuide = true;
            this.dailyClaimsAutoPendingMail = true;
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

            switch (redPointType)
            {
                case DailyClaimsRedPointTypeBattlePassTaskCanSubmit:
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoTaskIds, idParam);
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

                case DailyClaimsRedPointTypeTownGuides:
                case DailyClaimsRedPointTypeTownGuideNewNodeTask:
                case DailyClaimsRedPointTypeTownGuidesGrowth:
                    // The town-guide claim walks chapters itself, so the id is not needed.
                    this.dailyClaimsAutoPendingTownGuide = true;
                    break;

                default:
                    // Everything else (ordinary quests, cosmetics-unlocked markers, social pings) is
                    // either not a claim or not ours — see the file header.
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

        private void OnDailyClaimsAutoMailUpdatedEvent(GameEventSnapshot e)
        {
            if (!this.dailyClaimsAutoClaimEnabled)
            {
                return;
            }

            this.dailyClaimsAutoPendingMail = true;
        }

        private static void DailyClaimsAutoEnqueue(List<int> queue, int value)
        {
            if (value <= 0 || queue.Count >= DailyClaimsAutoMaxQueued || queue.Contains(value))
            {
                return;
            }

            queue.Add(value);
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
            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoTaskIds, out int taskId))
            {
                bool ok = this.TrySubmitDailyClaimsGameTask(taskId, out string status);
                this.DailyClaimsAutoReport(ok, "submit task " + taskId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoIssueIds, out int issueId))
            {
                bool ok = this.TryClaimDailyClaimsBpIssueReward(issueId, out string status);
                this.DailyClaimsAutoReport(ok, "bp issue " + issueId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoPictorialTypes, out int pictorialType))
            {
                bool ok = this.TryClaimDailyClaimsPictorialTypeReward(pictorialType, out string status);
                this.DailyClaimsAutoReport(ok, "collection type " + pictorialType, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoAllSuitIds, out int allSuitId))
            {
                bool ok = this.TryClaimDailyClaimsPediaAllSuitReward(allSuitId, out string status);
                this.DailyClaimsAutoReport(ok, "all-suit " + allSuitId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoCertIds, out int certId))
            {
                bool ok = this.TryClaimDailyClaimsCertificationReward(certId, out string status);
                this.DailyClaimsAutoReport(ok, "certification " + certId, status);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoSuitIds, out int suitId))
            {
                // The red point names the suit, not which tier is owed, so ask the service how many
                // pieces are held and claim every tier at or below it — the same gate the manual
                // sweep uses, just for one suit.
                this.DailyClaimsAutoClaimSuitTiers(suitId);
                return true;
            }

            if (DailyClaimsAutoTakeFirst(this.dailyClaimsAutoActivityIds, out int activityId))
            {
                this.DailyClaimsAutoClaimActivity(activityId);
                return true;
            }

            // --- flag-driven claims --------------------------------------------------------------
            if (this.dailyClaimsAutoPendingMail)
            {
                this.dailyClaimsAutoPendingMail = false;
                bool ok = this.TryClaimMailAll(out string status);
                this.DailyClaimsAutoReport(ok, "mail", status);
                return true;
            }

            if (this.dailyClaimsAutoPendingTownGuide)
            {
                this.dailyClaimsAutoPendingTownGuide = false;
                int sent = this.ClaimTownGuideRewards(out string detail);
                this.DailyClaimsAutoReport(sent > 0, "town guide (sent=" + sent + ")", detail);
                return true;
            }

            // --- world-ready catch-up --------------------------------------------------------------
            if (this.dailyClaimsAutoCatchUpBattlePass)
            {
                this.dailyClaimsAutoCatchUpBattlePass = false;
                this.TryClaimMiniBpAll(out string miniStatus);
                this.TryClaimBpLoop(out string loopStatus);
                this.DailyClaimsAutoReport(true, "catch-up battle pass", miniStatus + "; " + loopStatus);
                return true;
            }

            if (this.dailyClaimsAutoCatchUpActivities)
            {
                this.dailyClaimsAutoCatchUpActivities = false;
                int sent = this.ClaimSignInRewards(out string detail);
                this.DailyClaimsAutoReport(sent > 0, "catch-up sign-in (sent=" + sent + ")", detail);
                return true;
            }

            if (this.dailyClaimsAutoCatchUpTasks)
            {
                this.dailyClaimsAutoCatchUpTasks = false;
                this.DailyClaimsAutoQueueSubmittableTasks();
                return true;
            }

            if (this.dailyClaimsAutoCatchUpWhalefall)
            {
                this.dailyClaimsAutoCatchUpWhalefall = false;
                this.DailyClaimsAutoQueueWhalefallRequests();
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
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoTaskIds, buffer[i]);
                    queued++;
                }
            }

            if (this.DailyClaimsTryCollectSubmittableTaskIds(buffer, out string taskStatus))
            {
                for (int i = 0; i < buffer.Count; i++)
                {
                    if (this.DailyClaimsIsBattlePassTask(buffer[i], out _))
                    {
                        DailyClaimsAutoEnqueue(this.dailyClaimsAutoTaskIds, buffer[i]);
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
                    DailyClaimsAutoEnqueue(this.dailyClaimsAutoTaskIds, buffer[i]);
                    queued++;
                }
            }

            this.DailyClaimsAutoReport(queued > 0, "catch-up whalefall (queued=" + queued + ")", listStatus);
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
            this.DailyClaimsAutoReport(sent > 0, "activity " + activityId + " (nodes=" + sent + ")",
                nodeStatus + "; " + bpStatus);
        }

        private void DailyClaimsAutoClaimSuitTiers(int suitId)
        {
            List<DailyClaimsSuitRewardTier> tiers = new List<DailyClaimsSuitRewardTier>();
            if (!this.DailyClaimsTryCollectSuitRewardTiers(tiers, out string tableStatus))
            {
                this.DailyClaimsAutoReport(false, "suit " + suitId, tableStatus);
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

            int queued = this.dailyClaimsAutoTaskIds.Count
                + this.dailyClaimsAutoPictorialTypes.Count
                + this.dailyClaimsAutoSuitIds.Count
                + this.dailyClaimsAutoAllSuitIds.Count
                + this.dailyClaimsAutoCertIds.Count
                + this.dailyClaimsAutoIssueIds.Count
                + this.dailyClaimsAutoActivityIds.Count;

            return "Auto-claim on (hooks=" + (this.dailyClaimsAutoHooksRegistered ? "live" : "pending")
                + ", claimed=" + this.dailyClaimsAutoClaimedCount
                + ", queued=" + queued + "). " + this.dailyClaimsAutoLastStatus;
        }
    }
}
