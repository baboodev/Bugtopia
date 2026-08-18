using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private static bool DailyClaimsLogsEnabled => MasterLogDailyClaims;
        private const float DailyClaimsActionDelaySeconds = 0.65f;

        // Spacing between the individual sends of a multi-command sweep. The festival-reward and
        // collection sweeps fan out to 7 and 17 commands respectively; firing that many server RPCs
        // inside one frame is a burst no in-game action produces, so they are paced instead.
        private const float DailyClaimsCommandSpacingSeconds = 0.15f;

        private object dailyClaimsCoroutine = null;
        private string dailyClaimsLastStatus = string.Empty;

        // 2026-08-10 purge: every managed-reflection path in this feature is gone (project rule —
        // EcsClient/XDT* types live only in embedded Mono and are ABSENT from the interop, so a
        // managed branch is dead code that only misleads debugging). The cached Type/MethodInfo
        // fields the old dual-path senders carried went with them; protocol calls now go straight
        // through TryInvokeDailyClaimsProtocolAuraMono, and commands through
        // TryDailyClaimsSendCommandAura.
        // Service objects are cached through AuraMonoObjectCache, NOT as raw IntPtrs. Two reasons,
        // both of them live crashes:
        //   * SGen is a MOVING collector and a bare MonoObject* is not a root it can see, so an
        //     unpinned cached pointer goes stale on the next collection (AGENTS.md §11).
        //   * A level transition tears down and rebuilds the ECS client services. The old service
        //     object becomes garbage under the scene-load allocation storm, and the next invoke on
        //     the cached pointer is a native AV with no WER dump — this is exactly the 2026-08-11
        //     18:09 death entering the underwater level: the town-guide catch-up step ran ~2s after
        //     world-ready on a binding resolved before the transition. AuraMonoObjectCache pins the
        //     object AND stamps the world epoch, so a world change forces a clean re-resolve.
        private AuraMonoObjectCache dailyClaimsActivityServiceCache;
        private AuraMonoObjectCache dailyClaimsTownGuideServiceCache;
        private AuraMonoObjectCache dailyClaimsSeaCycleServiceCache;
        private AuraMonoObjectCache dailyClaimsPictorialServiceCache;
        private string dailyClaimsActivityServiceSource = string.Empty;
        private string dailyClaimsTownGuideServiceSource = string.Empty;
        private string dailyClaimsSeaCycleServiceSource = string.Empty;
        private string dailyClaimsPictorialServiceSource = string.Empty;

        private readonly List<int> dailyClaimsActivityIdBuffer = new List<int>(64);
        private readonly List<string> dailyClaimsNodeStateBuffer = new List<string>(32);
        private readonly List<DailyClaimsTownGuideChapterSnapshot> dailyClaimsTownGuideChapterBuffer = new List<DailyClaimsTownGuideChapterSnapshot>(32);
        private readonly List<IntPtr> dailyClaimsAuraMonoItemBuffer = new List<IntPtr>(64);
        private readonly List<uint> dailyClaimsAuraMonoPinBuffer = new List<uint>(64);
        // The chapter parser runs INSIDE the loop that walks dailyClaimsAuraMonoItemBuffer, so it
        // must never touch that list: it used to take the same instance, Clear() it and refill it
        // with the chapter's nodes, which left the caller indexing node pointers as chapters (and
        // re-bounded its loop to the node count). Own buffers, one nesting level.
        private readonly List<IntPtr> dailyClaimsAuraMonoNodeBuffer = new List<IntPtr>(32);
        private readonly List<uint> dailyClaimsAuraMonoNodePinBuffer = new List<uint>(32);
        private bool dailyClaimsResolveProbeLogged = false;

        private IntPtr dailyClaimsAuraEcsServiceClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraEcsTryGetOpenMethod = IntPtr.Zero;
        private readonly Dictionary<IntPtr, IntPtr> dailyClaimsAuraInflatedTryGetByServiceClass = new Dictionary<IntPtr, IntPtr>();

        private IntPtr dailyClaimsGuidesChapterInfoListClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraBattlePassSystemClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraPictorialSystemClass = IntPtr.Zero;

        private const int DailyClaimsBattlePassSlotCanGet = 1;

        // ---- Festival (mini-BP) + Collection claims -------------------------------------------
        // "Festival" in the UI is the MINI battle pass: TableBattlePassPeriod._bpType == 2 (ids
        // 201-207), rendered by MiniBattlePassPanel. Its two content tabs and the seasonal BP's
        // (_bpType == 1, ids 5-8) are the SAME code path, only the tab captions differ
        // (tab1Name=每周挑战 Weekly Challenge, tab2Name=庆典活动/潮流活动 Festival/Fashion Activity) —
        // so one sweep covers both tabs of whichever pass is live.

        // GameTaskState.CanSubmit (XDT.Scene.Shared.Modules.GameplayLayer.GameTask.GameTaskState).
        private const int DailyClaimsGameTaskStateCanSubmit = 4;

        // ClientRedPointSystem.IsBattlePassTaskCanSubmitRange: a task belongs to the BP/festival
        // activity tabs iff TableGameTask.autoSubmit == 2 AND type is 3 or 11. That is the game's
        // own definition of "this CanSubmit task is a battle-pass challenge", reused verbatim so the
        // sweep can never touch a normal story/daily quest (those go through Quest Assistant).
        private const int DailyClaimsBpTaskAutoSubmit = 2;
        private const int DailyClaimsBpTaskTypeA = 3;
        private const int DailyClaimsBpTaskTypeB = 11;

        // GameTaskType.OperationActivityMission (10) — event mission tasks, e.g. the Sanrio sticker
        // themes' daily quests and the New Developer Life Log's. TaskSystem routes these into its
        // SEPARATE _operationActivityTasks dictionary (and returns early), so they are reached via
        // GetOperationActivityTasks() rather than GetAllTasks(), and membership in that collection
        // already means type 10 — no TableGameTask classification needed on this path.

        // TableBpIssue ids are periodId*100 + week, weeks 1..6 for every shipped period (season BPs
        // 2/4/6/7/8 -> x01..x06, season 5 -> 501..507, mini BPs 201-207 -> 2xx01..2xx06). Sweeping
        // 1..7 covers the longest observed period; the server drops triggers for issues that do not
        // exist or have nothing pending, so over-reach costs one rejected command.
        private const int DailyClaimsBpIssuesPerPeriod = 7;

        // New Life Log (新生活日志) is OperationActivity 1003 — NewPlayerJournalWidget claims it with
        // ReceiveReward(1003, nodeIndex), the exact call ClaimSignInRewards already makes for every
        // id GetAliveActivityIds returns. This constant only drives the explicit fallback for the
        // case where 1003 is missing from that list.
        private const int DailyClaimsNewLifeLogActivityId = 1003;

        // EPictorialNewType (XDT.Scene.Shared.Modules.Pictorial): 1..16 are the visible collection
        // categories, 1000 = CurrentBpTotal. GetPictorialRewardCommand{id} claims EVERY pending
        // point milestone of one category server-side, so one command per category is enough.
        private static readonly int[] DailyClaimsPictorialTypes =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 1000
        };

        private readonly List<int> dailyClaimsTaskIdBuffer = new List<int>(64);
        private readonly List<int> dailyClaimsSeaCycleTaskIdBuffer = new List<int>(16);

        // ---- 2026-08-10 round 2: home / dream / event / collection-extension sweeps -------------
        // Spacing for the BULK sweeps (hundreds of candidates, only a handful of them sends). The
        // 0.15s pace is right for a dozen commands, not for 70+; 0.05s is still only 20/s.
        private const float DailyClaimsBulkCommandSpacingSeconds = 0.05f;

        // How many table candidates get their red-point/service gate read per frame. The gate reads
        // are cheap 1-2 arg invokes, but 1700+ of them in one frame is a visible hitch.
        private const int DailyClaimsGateChunkSize = 200;

        // RedPointEnum.CollectCertification — the game's own "this certification is claimable" bit,
        // pushed by the server (RedPointType.CollectCertification = 100004 → this enum). Used as the
        // gate because the certification table has 3.5k rows and no cheaper filter exists.
        private const int DailyClaimsRedPointCollectCertification = 9004;

        private IntPtr dailyClaimsAuraRedPointManagerClass = IntPtr.Zero;

        // One TablePediaSuitReward row: which suit, which quantity tier, and the ROW id — the last
        // one matters because the client keys suit red-point nodes by row id, not by suitId.
        private struct DailyClaimsSuitRewardTier
        {
            public int RowId;
            public int SuitId;
            public int Quantity;
        }

        // A (dreamType, targetId) pair from TableDreamTaskType — the two args of
        // DrawDreamTargetRewardCommand.
        private struct DailyClaimsDreamTarget
        {
            public int DreamType;
            public int TargetId;
        }

        // A sticker/badge theme and the operation activity it belongs to; the activity id is what
        // gates the sweep down to whatever event is actually running.
        private struct DailyClaimsActivityTheme
        {
            public int ActivityId;
            public int ThemeId;
        }

        private struct DailyClaimsServiceBinding
        {
            public IntPtr AuraMono;
            public string Source;

            public bool IsValid => this.AuraMono != IntPtr.Zero;
        }

        private struct DailyClaimsTownGuideChapterSnapshot
        {
            public int ChapterId;
            public string ChapterState;
            public List<DailyClaimsTownGuideNodeSnapshot> Nodes;
        }

        private struct DailyClaimsTownGuideNodeSnapshot
        {
            public int NodeId;
            public string State;
        }


        private void StartDailyClaimsAction(IEnumerator routine)
        {
            if (this.dailyClaimsCoroutine != null)
            {
                this.dailyClaimsLastStatus = "Daily claims busy.";
                return;
            }

            this.dailyClaimsCoroutine = ModCoroutines.Start(this.DailyClaimsActionWrapper(routine));
        }

        private IEnumerator DailyClaimsActionWrapper(IEnumerator routine)
        {
            try
            {
                yield return routine;
            }
            finally
            {
                this.dailyClaimsCoroutine = null;
            }
        }

        private IEnumerator DailyClaimsLogAllStateRoutine()
        {
            this.dailyClaimsLastStatus = "Logging daily claims state...";
            this.DailyClaimsLog("=== Daily Claims state ===");

            string signInDetail = this.LogSignInRewardState(out int activityCount, out int waitClaimNodes);
            this.DailyClaimsLog("Sign-in: activities=" + activityCount + " waitClaimNodes=" + waitClaimNodes);
            this.DailyClaimsLog(signInDetail);

            string townGuideDetail = this.LogTownGuideRewardState(out int nodeRewards, out int chapterRewards);
            this.DailyClaimsLog("Town guide: nodeReward=" + nodeRewards + " chapterReward=" + chapterRewards);
            this.DailyClaimsLog(townGuideDetail);

            string mailDetail = this.LogMailRewardState(out bool mailRewardable, out int mailRewardableCount);
            this.DailyClaimsLog("Mail: rewardable=" + mailRewardable + " count=" + mailRewardableCount);
            this.DailyClaimsLog(mailDetail);

            string miniBpDetail = this.LogMiniBpRewardState(out int miniBpFreeCanGet, out int miniBpPaidCanGet);
            this.DailyClaimsLog("Mini BP: freeCanGet=" + miniBpFreeCanGet + " paidCanGet=" + miniBpPaidCanGet);
            this.DailyClaimsLog(miniBpDetail);

            string bpLoopDetail = this.LogBpLoopRewardState(out bool bpLoopClaimable, out int bpLoopCycles);
            this.DailyClaimsLog("BP Loop: claimable=" + bpLoopClaimable + " cycles=" + bpLoopCycles);
            this.DailyClaimsLog(bpLoopDetail);

            string festivalDetail = this.LogFestivalRewardState(out int festivalTasks, out int festivalPeriodId);
            this.DailyClaimsLog("Festival: submittable challenges=" + festivalTasks + " periodId=" + festivalPeriodId);
            this.DailyClaimsLog(festivalDetail);

            string whalefallDetail = this.LogWhalefallRequestState(out int whalefallReady, out int whalefallTotal);
            this.DailyClaimsLog("Whalefall: submittable=" + whalefallReady + "/" + whalefallTotal);
            this.DailyClaimsLog(whalefallDetail);

            string lifeLogDetail = this.LogNewLifeLogState(out long lifeLogProgress, out int lifeLogWaitClaim);
            this.DailyClaimsLog("New Life Log: progress=" + lifeLogProgress + " waitClaim=" + lifeLogWaitClaim);
            this.DailyClaimsLog(lifeLogDetail);

            string missionDetail = this.LogActivityMissionState(out int missionReady);
            this.DailyClaimsLog("Activity missions: submittable=" + missionReady);
            this.DailyClaimsLog(missionDetail);

            this.DailyClaimsLog("=== Daily Claims state end ===");
            this.dailyClaimsLastStatus = "State: signIn wait=" + waitClaimNodes
                + " town ch=" + chapterRewards
                + " mail=" + (mailRewardable ? mailRewardableCount : 0)
                + " miniBp=" + (miniBpFreeCanGet + miniBpPaidCanGet)
                + " bpLoop=" + bpLoopCycles
                + " fest=" + festivalTasks
                + " whale=" + whalefallReady;
            yield break;
        }

        // Whalefall read-side: exact on both counts — the service hands out today's assigned task
        // ids and each state comes from the same TaskProtocolManager.GetTaskState the panel uses.
        private string LogWhalefallRequestState(out int submittable, out int assigned)
        {
            submittable = 0;
            assigned = 0;

            List<int> taskIds = this.dailyClaimsSeaCycleTaskIdBuffer;
            if (!this.DailyClaimsTryGetSeaCycleDailyTaskIds(taskIds, out string listStatus))
            {
                return "--- Whalefall daily requests unavailable: " + listStatus + " ---";
            }

            assigned = taskIds.Count;
            List<string> parts = new List<string>(taskIds.Count);
            for (int i = 0; i < taskIds.Count; i++)
            {
                int taskId = taskIds[i];
                if (!this.TryGetGameTaskStateAura(taskId, out int state, out _))
                {
                    parts.Add(taskId + ":?");
                    continue;
                }

                if (state == DailyClaimsGameTaskStateCanSubmit)
                {
                    submittable++;
                }

                parts.Add(taskId + ":" + state);
            }

            string upgradeDetail;
            bool upgradeReady = this.DailyClaimsCanUpgradeSeaCycle(out upgradeDetail);

            return "--- Whalefall daily requests (" + listStatus + "): assigned=" + assigned
                + " submittable=" + submittable
                + " [" + string.Join(", ", parts.ToArray()) + "] (state 4 = CanSubmit)"
                + " | exploration upgrade " + (upgradeReady ? "AVAILABLE" : "no") + ": " + upgradeDetail + " ---";
        }

        // Operation-activity mission read-side (Sanrio-style event daily quests): exact, since it
        // uses the same held-task scan and the same TableGameTask classification the claim does.
        private string LogActivityMissionState(out int submittable)
        {
            submittable = 0;

            List<int> taskIds = this.dailyClaimsTaskIdBuffer;
            if (!this.DailyClaimsTryCollectSubmittableActivityMissionIds(taskIds, out string scanStatus))
            {
                return "--- activity missions unavailable: " + scanStatus + " ---";
            }

            submittable = taskIds.Count;
            List<string> matched = new List<string>(taskIds.Count);
            for (int i = 0; i < taskIds.Count; i++)
            {
                matched.Add(taskIds[i].ToString());
            }

            return "--- activity missions: " + scanStatus + ", CanSubmit=" + submittable
                + " [" + string.Join(", ", matched.ToArray()) + "] ---";
        }

        // IOperationActivityCenterService.GetActivityProgress(int) returns a **long**. Unboxing it as
        // Int32 would silently keep only the low 32 bits ([[auramono-boxed-int64-truncation]]), so
        // the payload is read as a full 8 bytes.
        private unsafe bool DailyClaimsTryGetActivityProgress(
            DailyClaimsServiceBinding binding,
            int activityId,
            out long progress,
            out string status)
        {
            progress = 0L;
            status = "GetActivityProgress unavailable";
            if (!binding.IsValid
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(binding.AuraMono),
                "GetActivityProgress",
                1);
            if (method == IntPtr.Zero)
            {
                status = "GetActivityProgress method missing";
                return false;
            }

            int id = activityId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, binding.AuraMono, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                status = "GetActivityProgress invoke failed";
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                status = "GetActivityProgress unbox failed";
                return false;
            }

            progress = Marshal.ReadInt64(raw);
            status = "ok";
            return true;
        }

        // New Developer Life Log (新发展家生活日志) = OperationActivity 1003. Its 14 reward nodes line up
        // 1:1 with TableBeginnerMissionBonuss, each gated on a task count (6/12/18…84). The node
        // states alone cannot tell "not earned yet" from "server has not flipped it", so this reports
        // the live progress next to the thresholds.
        private string LogNewLifeLogState(out long progress, out int waitClaimNodes)
        {
            progress = 0L;
            waitClaimNodes = 0;

            if (!this.TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                return "--- New Life Log unavailable: " + serviceStatus + " ---";
            }

            string progressStatus = "not read";
            this.DailyClaimsTryGetActivityProgress(binding, DailyClaimsNewLifeLogActivityId, out progress, out progressStatus);

            List<string> nodeParts = this.dailyClaimsNodeStateBuffer;
            nodeParts.Clear();
            if (!this.DailyClaimsTryGetActivityNodeStateNames(
                binding, DailyClaimsNewLifeLogActivityId, nodeParts, out string nodeStatus))
            {
                return "--- New Life Log (activityId=" + DailyClaimsNewLifeLogActivityId
                    + ") progress=" + progress + " (" + progressStatus + ") node states unreadable: " + nodeStatus + " ---";
            }

            List<string> display = new List<string>(nodeParts.Count);
            for (int i = 0; i < nodeParts.Count; i++)
            {
                display.Add(i + ":" + nodeParts[i]);
                if (string.Equals(nodeParts[i], "WaitClaim", StringComparison.Ordinal))
                {
                    waitClaimNodes++;
                }
            }

            List<int> thresholds = new List<int>();
            string bonusStatus = "TableBeginnerMissionBonuss not read";
            this.DailyClaimsForEachTableRow("TableBeginnerMissionBonuss", row =>
            {
                if (this.DailyClaimsTryGetMonoPropertyInt(row, "get_requiredTaskNum", out int need) && need > 0)
                {
                    thresholds.Add(need);
                }
            }, out bonusStatus);
            thresholds.Sort();

            int nextNeed = 0;
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i] > progress)
                {
                    nextNeed = thresholds[i];
                    break;
                }
            }

            return "--- New Life Log activityId=" + DailyClaimsNewLifeLogActivityId
                + " progress=" + progress + " (" + progressStatus + ")"
                + " nextThreshold=" + (nextNeed > 0 ? nextNeed.ToString() : "none (all reached)")
                + " thresholds=[" + string.Join(",", thresholds.ConvertAll(t => t.ToString()).ToArray()) + "] (" + bonusStatus + ")"
                + " waitClaim=" + waitClaimNodes
                + " nodes=[" + string.Join(", ", display.ToArray()) + "] ---";
        }

        // Festival read-side. The challenge count is exact (the same classification the claim uses);
        // the reward track is reported as the issue span the claim would sweep — the per-issue
        // current/rewarded point counts live behind ISeriesRewardCenter and are not read here, so
        // this line says what WOULD be swept, not how much is pending. Collection is not listed for
        // the same reason: without TablePediaPointReward "current > last claimed" cannot be turned
        // into a milestone count, and a misleading number is worse than none.
        private string LogFestivalRewardState(out int submittableChallenges, out int periodId)
        {
            submittableChallenges = 0;
            periodId = 0;

            List<string> lines = new List<string>();
            List<int> taskIds = this.dailyClaimsTaskIdBuffer;
            if (this.DailyClaimsTryCollectSubmittableTaskIds(taskIds, out string collectStatus))
            {
                List<string> matched = new List<string>();
                for (int i = 0; i < taskIds.Count; i++)
                {
                    if (this.DailyClaimsIsBattlePassTask(taskIds[i], out _))
                    {
                        submittableChallenges++;
                        matched.Add(taskIds[i].ToString());
                    }
                }

                lines.Add("--- festival challenges: " + collectStatus
                    + ", CanSubmit=" + taskIds.Count
                    + ", battle-pass=" + submittableChallenges
                    + " [" + string.Join(", ", matched.ToArray()) + "] ---");
            }
            else
            {
                lines.Add("--- festival challenges unavailable: " + collectStatus + " ---");
            }

            if (this.DailyClaimsTryGetCurrentBpPeriodId(out periodId, out string periodStatus))
            {
                lines.Add("festival rewards would sweep issueIds "
                    + (periodId * 100 + 1) + ".." + (periodId * 100 + DailyClaimsBpIssuesPerPeriod)
                    + " (periodId=" + periodId + ", " + periodStatus + ")");
            }
            else
            {
                lines.Add("festival rewards period unavailable: " + periodStatus);
            }

            return string.Join("\n", lines.ToArray());
        }

        private IEnumerator DailyClaimsClaimSignInRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming sign-in rewards...";
            int claimed = this.ClaimSignInRewards(out string detail);
            this.dailyClaimsLastStatus = "Sign-in claim done: sent=" + claimed;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(detail);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimMailRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming mail attachments...";
            bool ok = this.TryClaimMailAll(out string status);
            this.dailyClaimsLastStatus = ok ? "Mail claim sent." : ("Mail claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimMiniBpAllRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming mini battle pass rewards...";
            bool ok = this.TryClaimMiniBpAll(out string status);
            this.dailyClaimsLastStatus = ok ? "Mini BP claim sent." : ("Mini BP claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimBpLoopRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming BP loop rewards...";
            bool ok = this.TryClaimBpLoop(out string status);
            this.dailyClaimsLastStatus = ok ? "BP loop claim sent." : ("BP loop claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimTownGuideRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming town guide rewards...";
            int claimed = this.ClaimTownGuideRewards(out string detail);
            this.dailyClaimsLastStatus = "Town guide claim done: sent=" + claimed;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(detail);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // The three festival/collection sweeps are paced coroutines rather than one synchronous
        // burst. Everything that survives a `yield` here is a scalar (ints and strings) — no raw
        // MonoObject* is ever held across a frame boundary.
        private IEnumerator DailyClaimsClaimFestivalChallengesRoutine()
        {
            this.dailyClaimsLastStatus = "Submitting festival challenges...";

            List<int> taskIds = this.dailyClaimsTaskIdBuffer;
            if (!this.DailyClaimsTryCollectSubmittableTaskIds(taskIds, out string collectStatus))
            {
                this.dailyClaimsLastStatus = "Festival challenges failed: " + collectStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            // Copy out of the shared buffer: the sweep yields, and another daily-claims pass could
            // otherwise reuse it mid-flight.
            List<int> pending = new List<int>(taskIds);
            List<string> lines = new List<string>
            {
                "--- festival challenges: " + pending.Count + " CanSubmit task(s) (" + collectStatus + ") ---"
            };

            int submitted = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                int taskId = pending[i];
                if (!this.DailyClaimsIsBattlePassTask(taskId, out string kindStatus))
                {
                    lines.Add("skip taskId=" + taskId + " (" + kindStatus + ")");
                    continue;
                }

                if (this.TrySubmitDailyClaimsGameTask(taskId, out string submitStatus))
                {
                    submitted++;
                    lines.Add("submitted taskId=" + taskId + " (" + submitStatus + ")");
                }
                else
                {
                    lines.Add("FAILED taskId=" + taskId + " (" + submitStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            if (pending.Count == 0)
            {
                lines.Add("no CanSubmit tasks held right now");
            }

            this.dailyClaimsLastStatus = "Festival challenges done: submitted=" + submitted;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimFestivalRewardsRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming festival rewards...";

            if (!this.DailyClaimsTryGetCurrentBpPeriodId(out int periodId, out string periodStatus))
            {
                this.dailyClaimsLastStatus = "Festival rewards failed: " + periodStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<string> lines = new List<string>
            {
                "--- festival rewards periodId=" + periodId + " (" + periodStatus + ") ---"
            };

            int sent = 0;
            for (int week = 1; week <= DailyClaimsBpIssuesPerPeriod; week++)
            {
                int issueId = periodId * 100 + week;
                if (this.TryClaimDailyClaimsBpIssueReward(issueId, out string claimStatus))
                {
                    sent++;
                    lines.Add("issueId=" + issueId + " (" + claimStatus + ")");
                }
                else
                {
                    lines.Add("FAILED issueId=" + issueId + " (" + claimStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            this.dailyClaimsLastStatus = "Festival rewards done: sent=" + sent;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimCollectionRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming collection rewards...";

            List<string> lines = new List<string> { "--- collection (pictorial) point rewards ---" };
            int sent = 0;

            for (int i = 0; i < DailyClaimsPictorialTypes.Length; i++)
            {
                int pictorialType = DailyClaimsPictorialTypes[i];
                if (this.TryClaimDailyClaimsPictorialTypeReward(pictorialType, out string claimStatus))
                {
                    sent++;
                    lines.Add("type=" + pictorialType + " (" + claimStatus + ")");
                }
                else
                {
                    lines.Add("FAILED type=" + pictorialType + " (" + claimStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            // ---- suit rewards + all-suit rewards -------------------------------------------------
            // TablePediaSuitRewards ships 922 tiers over 480 suits, so this cannot be blind-swept:
            // every suit is first asked how many of its items the player owns, and only tiers at or
            // below that count are claimed.
            List<DailyClaimsSuitRewardTier> tiers = new List<DailyClaimsSuitRewardTier>();
            if (this.DailyClaimsTryCollectSuitRewardTiers(tiers, out string suitTableStatus))
            {
                List<int> suitIds = new List<int>();
                for (int i = 0; i < tiers.Count; i++)
                {
                    if (!suitIds.Contains(tiers[i].SuitId))
                    {
                        suitIds.Add(tiers[i].SuitId);
                    }
                }

                // The gate reads are cheap individually, but 480 of them in one frame is a hitch, so
                // they run a chunk at a time. Each chunk is a SYNCHRONOUS helper that resolves the
                // service, uses it and drops it — no mono pointer ever lives in this coroutine's
                // state machine across a yield (CI lint W1).
                List<int> ownedSuitIds = new List<int>();
                List<int> ownedSuitCounts = new List<int>();
                string suitServiceStatus = "IPictorialService unavailable";
                for (int start = 0; start < suitIds.Count; start += DailyClaimsGateChunkSize)
                {
                    if (!this.DailyClaimsFilterOwnedSuitChunk(
                        suitIds, start, DailyClaimsGateChunkSize, ownedSuitIds, ownedSuitCounts, out suitServiceStatus))
                    {
                        break;
                    }

                    yield return null;
                }

                lines.Add("--- suits: " + suitTableStatus + ", suits=" + suitIds.Count
                    + ", owned=" + ownedSuitIds.Count + " (" + suitServiceStatus + ") ---");

                for (int i = 0; i < ownedSuitIds.Count; i++)
                {
                    int suitId = ownedSuitIds[i];
                    int hasNum = ownedSuitCounts[i];

                    for (int t = 0; t < tiers.Count; t++)
                    {
                        if (tiers[t].SuitId != suitId || tiers[t].Quantity > hasNum)
                        {
                            continue;
                        }

                        if (this.TryClaimDailyClaimsPictorialSuitReward(suitId, tiers[t].Quantity, out string tierStatus))
                        {
                            sent++;
                            lines.Add("suit=" + suitId + " tier=" + tiers[t].Quantity + " (" + tierStatus + ")");
                        }
                        else
                        {
                            lines.Add("FAILED suit=" + suitId + " tier=" + tiers[t].Quantity + " (" + tierStatus + ")");
                        }

                        yield return ModWait.Realtime(DailyClaimsBulkCommandSpacingSeconds);
                    }

                    if (!this.DailyClaimsIsAllSuitRewardClaimed(suitId))
                    {
                        if (this.TryClaimDailyClaimsPediaAllSuitReward(suitId, out string allStatus))
                        {
                            sent++;
                            lines.Add("suit=" + suitId + " all-suit (" + allStatus + ")");
                        }
                        else
                        {
                            lines.Add("FAILED suit=" + suitId + " all-suit (" + allStatus + ")");
                        }

                        yield return ModWait.Realtime(DailyClaimsBulkCommandSpacingSeconds);
                    }
                }
            }
            else
            {
                lines.Add("--- suits unavailable: " + suitTableStatus + " ---");
            }

            // ---- collection certifications --------------------------------------------------------
            // 1.7k candidate ids, so the gate is the game's own red point (RedPointEnum
            // .CollectCertification): the node only exists for ids the server flagged claimable.
            List<int> certIds = new List<int>();
            if (this.DailyClaimsTryCollectCertificationIds(certIds, out string certTableStatus))
            {
                // Same chunking shape as the suit gate: the RedPointManager instance is resolved,
                // used and dropped INSIDE the synchronous helper, so it never crosses a yield.
                List<int> claimableCerts = new List<int>();
                bool certGateOk = certIds.Count == 0;
                for (int start = 0; start < certIds.Count; start += DailyClaimsGateChunkSize)
                {
                    if (!this.DailyClaimsFilterClaimableCertificationChunk(
                        certIds, start, DailyClaimsGateChunkSize, claimableCerts))
                    {
                        certGateOk = false;
                        break;
                    }

                    certGateOk = true;
                    yield return null;
                }

                lines.Add("--- certifications: " + certTableStatus + ", open=" + certIds.Count
                    + ", flagged=" + claimableCerts.Count
                    + (certGateOk ? string.Empty : " (RedPointManager unavailable — gate incomplete)") + " ---");

                for (int i = 0; i < claimableCerts.Count; i++)
                {
                    if (this.TryClaimDailyClaimsCertificationReward(claimableCerts[i], out string certStatus))
                    {
                        sent++;
                        lines.Add("certification staticId=" + claimableCerts[i] + " (" + certStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED certification staticId=" + claimableCerts[i] + " (" + certStatus + ")");
                    }

                    yield return ModWait.Realtime(DailyClaimsBulkCommandSpacingSeconds);
                }
            }
            else
            {
                lines.Add("--- certifications unavailable: " + certTableStatus + " ---");
            }

            // ---- cat moments ----------------------------------------------------------------------
            // Only 73 rows and no client-side "claimable" read, so this one is a blind sweep.
            List<int> catMomentIds = new List<int>();
            if (this.DailyClaimsTryCollectSimpleTableIds("TableCatmoments", catMomentIds, out string catStatus))
            {
                lines.Add("--- cat moments: " + catStatus + " ---");
                for (int i = 0; i < catMomentIds.Count; i++)
                {
                    if (this.TryClaimDailyClaimsCatMomentReward(catMomentIds[i], out string momentStatus))
                    {
                        sent++;
                    }
                    else
                    {
                        lines.Add("FAILED cat moment staticId=" + catMomentIds[i] + " (" + momentStatus + ")");
                    }

                    yield return ModWait.Realtime(DailyClaimsBulkCommandSpacingSeconds);
                }
            }
            else
            {
                lines.Add("--- cat moments unavailable: " + catStatus + " ---");
            }

            this.dailyClaimsLastStatus = "Collection claim done: sent=" + sent;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // Whalefall Canyon daily requests (鲸落峡谷每日委托) — the SeaCycle daily task set. Same submit
        // path as the festival challenges, but the id list is authoritative (ISeaCycleService hands
        // out exactly today's assigned tasks), so no TableGameTask classification is needed.
        private IEnumerator DailyClaimsClaimWhalefallRequestsRoutine()
        {
            this.dailyClaimsLastStatus = "Submitting Whalefall requests...";

            List<int> taskIds = this.dailyClaimsSeaCycleTaskIdBuffer;
            if (!this.DailyClaimsTryGetSeaCycleDailyTaskIds(taskIds, out string listStatus))
            {
                this.dailyClaimsLastStatus = "Whalefall requests failed: " + listStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<int> pending = new List<int>(taskIds);
            List<string> lines = new List<string>
            {
                "--- Whalefall daily requests: " + pending.Count + " assigned (" + listStatus + ") ---"
            };

            int submitted = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                int taskId = pending[i];
                if (!this.TryGetGameTaskStateAura(taskId, out int state, out string stateStatus))
                {
                    lines.Add("skip taskId=" + taskId + " (state unreadable: " + stateStatus + ")");
                    continue;
                }

                if (state != DailyClaimsGameTaskStateCanSubmit)
                {
                    lines.Add("skip taskId=" + taskId + " (state=" + state + ", not CanSubmit)");
                    continue;
                }

                if (this.TrySubmitDailyClaimsGameTask(taskId, out string submitStatus))
                {
                    submitted++;
                    lines.Add("submitted taskId=" + taskId + " (" + submitStatus + ")");
                }
                else
                {
                    lines.Add("FAILED taskId=" + taskId + " (" + submitStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            if (pending.Count == 0)
            {
                lines.Add("no daily requests assigned (Whalefall Canyon not unlocked, or the season is over)");
            }

            // Submitting the requests is what earns the exploration exp and the upgrade ticket, so
            // the level-up check belongs AFTER them — and only makes sense once the server has
            // credited the rewards, hence the settle wait. Chained because one upgrade can leave
            // enough exp for the next.
            if (submitted > 0)
            {
                yield return ModWait.Realtime(DailyClaimsSeaCycleSettleSeconds);
            }

            int upgrades = 0;
            for (int attempt = 0; attempt < DailyClaimsMaxSeaCycleUpgrades; attempt++)
            {
                if (!this.DailyClaimsCanUpgradeSeaCycle(out string gateDetail))
                {
                    lines.Add("exploration upgrade: " + gateDetail);
                    break;
                }

                if (!this.TryUpgradeDailyClaimsSeaCycle(out string upgradeStatus))
                {
                    lines.Add("FAILED exploration upgrade (" + gateDetail + "): " + upgradeStatus);
                    break;
                }

                upgrades++;
                lines.Add("exploration upgrade sent (" + gateDetail + "): " + upgradeStatus);
                yield return ModWait.Realtime(DailyClaimsSeaCycleSettleSeconds);
            }

            this.dailyClaimsLastStatus = "Whalefall done: submitted=" + submitted + " upgrades=" + upgrades;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // Home evaluation reward (家园评价奖励) — one empty command, the server works out what is owed.
        private IEnumerator DailyClaimsClaimHomeRewardsRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming home rewards...";
            bool ok = this.TryClaimDailyClaimsHomeEvaluationReward(out string status);
            this.dailyClaimsLastStatus = ok ? "Home reward claim sent." : ("Home reward claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // Dream target rewards — one command per (dreamType, target); the server hands over every
        // tier of that target whose exp threshold is met and not yet flagged in TargetRewardFlag.
        private IEnumerator DailyClaimsClaimDreamRewardsRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming dream rewards...";

            List<DailyClaimsDreamTarget> targets = new List<DailyClaimsDreamTarget>();
            if (!this.DailyClaimsTryCollectDreamTargets(targets, out string tableStatus))
            {
                this.dailyClaimsLastStatus = "Dream rewards failed: " + tableStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<string> lines = new List<string> { "--- dream targets (" + tableStatus + ") ---" };
            int sent = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                DailyClaimsDreamTarget target = targets[i];
                if (this.TryClaimDailyClaimsDreamTargetReward(target.DreamType, target.TargetId, out string claimStatus))
                {
                    sent++;
                    lines.Add("dreamType=" + target.DreamType + " targetId=" + target.TargetId + " (" + claimStatus + ")");
                }
                else
                {
                    lines.Add("FAILED dreamType=" + target.DreamType + " targetId=" + target.TargetId
                        + " (" + claimStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            if (targets.Count == 0)
            {
                lines.Add("no dream targets in TableDreamTaskTypes");
            }

            this.dailyClaimsLastStatus = "Dream claim done: sent=" + sent;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        // Event extras that hang off operation activities: sticker theme bonuses, badge theme +
        // final rewards, and the activity's own BP track. All three are gated on the activity being
        // ALIVE — the theme tables list every event ever shipped, and sweeping all of them would be
        // dozens of rejected commands instead of a handful of real ones.
        private IEnumerator DailyClaimsClaimEventRewardsRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming event rewards...";

            if (!this.TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                this.dailyClaimsLastStatus = "Event rewards failed: " + serviceStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<int> aliveBuffer = this.dailyClaimsActivityIdBuffer;
            if (!this.DailyClaimsTryGetAliveActivityIds(binding, aliveBuffer, out string aliveStatus))
            {
                this.dailyClaimsLastStatus = "Event rewards failed: " + aliveStatus;
                this.DailyClaimsLog(this.dailyClaimsLastStatus);
                yield break;
            }

            List<int> alive = new List<int>(aliveBuffer);
            List<string> lines = new List<string>
            {
                "--- event rewards: " + alive.Count + " alive activity(ies) (" + aliveStatus + ") ---"
            };
            int sent = 0;

            // 1. The activity's own BP track (ClaimAllActivityBPReward drains it in one call).
            for (int i = 0; i < alive.Count; i++)
            {
                int activityId = alive[i];
                if (this.TryClaimDailyClaimsActivityBpReward(activityId, out string bpStatus))
                {
                    sent++;
                    lines.Add("activity BP activityId=" + activityId + " (" + bpStatus + ")");
                }
                else
                {
                    lines.Add("FAILED activity BP activityId=" + activityId + " (" + bpStatus + ")");
                }

                yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
            }

            // 2. Operation-activity mission tasks (GameTaskType 10) — the per-event daily quests,
            // e.g. the Sanrio sticker themes'. Swept from the tasks the player actually holds, so the
            // cost is bounded by the CanSubmit set rather than by the ~350 shipped mission rows.
            List<int> heldTaskIds = this.dailyClaimsTaskIdBuffer;
            if (this.DailyClaimsTryCollectSubmittableActivityMissionIds(heldTaskIds, out string missionScanStatus))
            {
                List<int> missionPending = new List<int>(heldTaskIds);
                int missionSent = 0;
                for (int i = 0; i < missionPending.Count; i++)
                {
                    int taskId = missionPending[i];
                    if (this.TrySubmitDailyClaimsGameTask(taskId, out string submitStatus))
                    {
                        sent++;
                        missionSent++;
                        lines.Add("activity mission taskId=" + taskId + " (" + submitStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED activity mission taskId=" + taskId + " (" + submitStatus + ")");
                    }

                    yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
                }

                lines.Add("activity missions: " + missionScanStatus
                    + ", CanSubmit=" + missionPending.Count + ", submitted=" + missionSent);
            }
            else
            {
                lines.Add("activity mission scan failed: " + missionScanStatus);
            }

            // 3. Sticker theme bonuses. StickerThemeBonus ships 1-2 tiers per theme, so both node
            // indices are tried for every live theme.
            List<DailyClaimsActivityTheme> stickerThemes = new List<DailyClaimsActivityTheme>();
            if (this.DailyClaimsTryCollectActivityThemes("TableStickerThemes", stickerThemes, out string stickerStatus))
            {
                int stickerSent = 0;
                for (int i = 0; i < stickerThemes.Count; i++)
                {
                    DailyClaimsActivityTheme theme = stickerThemes[i];
                    if (!alive.Contains(theme.ActivityId))
                    {
                        continue;
                    }

                    for (int nodeIndex = 0; nodeIndex < 2; nodeIndex++)
                    {
                        if (this.TryClaimDailyClaimsStickerThemeReward(
                            theme.ActivityId, theme.ThemeId, nodeIndex, out string claimStatus))
                        {
                            sent++;
                            stickerSent++;
                            lines.Add("sticker themeId=" + theme.ThemeId + " node=" + nodeIndex
                                + " (" + claimStatus + ")");
                        }
                        else
                        {
                            lines.Add("FAILED sticker themeId=" + theme.ThemeId + " node=" + nodeIndex
                                + " (" + claimStatus + ")");
                        }

                        yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
                    }
                }

                lines.Add("sticker: " + stickerStatus + ", live-theme sends=" + stickerSent);
            }
            else
            {
                lines.Add("sticker themes unavailable: " + stickerStatus);
            }

            // 4. Badge collect: per-theme progress rewards + the activity's final reward.
            List<DailyClaimsActivityTheme> badgeThemes = new List<DailyClaimsActivityTheme>();
            if (this.DailyClaimsTryCollectActivityThemes("TableBadgeThemes", badgeThemes, out string badgeStatus))
            {
                for (int i = 0; i < badgeThemes.Count; i++)
                {
                    DailyClaimsActivityTheme theme = badgeThemes[i];
                    if (!alive.Contains(theme.ActivityId))
                    {
                        continue;
                    }

                    if (this.TryClaimDailyClaimsBadgeThemeReward(theme.ThemeId, out string claimStatus))
                    {
                        sent++;
                        lines.Add("badge themeId=" + theme.ThemeId + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED badge themeId=" + theme.ThemeId + " (" + claimStatus + ")");
                    }

                    yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
                }
            }
            else
            {
                lines.Add("badge themes unavailable: " + badgeStatus);
            }

            List<int> badgeFinalActivities = new List<int>();
            if (this.DailyClaimsTryCollectBadgeFinalActivityIds(badgeFinalActivities, out string badgeFinalStatus))
            {
                for (int i = 0; i < badgeFinalActivities.Count; i++)
                {
                    int activityId = badgeFinalActivities[i];
                    if (!alive.Contains(activityId))
                    {
                        continue;
                    }

                    if (this.TryClaimDailyClaimsBadgeFinalReward(activityId, out string claimStatus))
                    {
                        sent++;
                        lines.Add("badge final activityId=" + activityId + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED badge final activityId=" + activityId + " (" + claimStatus + ")");
                    }

                    yield return ModWait.Realtime(DailyClaimsCommandSpacingSeconds);
                }
            }
            else
            {
                lines.Add("badge final rewards unavailable: " + badgeFinalStatus);
            }

            this.dailyClaimsLastStatus = "Event claim done: sent=" + sent;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(string.Join("\n", lines.ToArray()));
            yield return ModWait.Realtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimAllRoutine()
        {
            this.DailyClaimsLog("=== Claim All Daily start ===");

            yield return this.DailyClaimsClaimSignInRoutine();
            yield return this.DailyClaimsClaimMailRoutine();
            yield return this.DailyClaimsClaimMiniBpAllRoutine();
            yield return this.DailyClaimsClaimBpLoopRoutine();
            yield return this.DailyClaimsClaimTownGuideRoutine();
            yield return this.DailyClaimsClaimFestivalChallengesRoutine();
            yield return this.DailyClaimsClaimFestivalRewardsRoutine();
            yield return this.DailyClaimsClaimCollectionRoutine();
            yield return this.DailyClaimsClaimWhalefallRequestsRoutine();
            yield return this.DailyClaimsClaimHomeRewardsRoutine();
            yield return this.DailyClaimsClaimDreamRewardsRoutine();
            yield return this.DailyClaimsClaimEventRewardsRoutine();

            this.DailyClaimsLog("Starting wild gift claim (Claim All).");
            this.StartWildAnimalClaimAllGifts(silent: true);
            float waitStart = Time.realtimeSinceStartup;
            while (this.wildAnimalGiftCoroutine != null && Time.realtimeSinceStartup - waitStart < 120f)
            {
                yield return null;
            }

            this.dailyClaimsLastStatus = this.wildAnimalGiftCoroutine != null
                ? "Claim All done (wild gifts still running)."
                : "Claim All Daily finished.";
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " wildStatus=" + (this.wildAnimalGiftLastStatus ?? string.Empty));
            this.DailyClaimsLog("=== Claim All Daily end ===");
        }

        private string LogSignInRewardState(out int activityCount, out int waitClaimNodes)
        {
            activityCount = 0;
            waitClaimNodes = 0;
            if (!this.TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                return "IOperationActivityCenterService unavailable: " + serviceStatus;
            }

            this.DailyClaimsLog("Activity service ready via " + binding.Source);
            List<int> activityIds = this.dailyClaimsActivityIdBuffer;
            activityIds.Clear();
            if (!this.DailyClaimsTryGetAliveActivityIds(binding, activityIds, out string listStatus))
            {
                return "GetAliveActivityIds failed: " + listStatus;
            }

            activityCount = activityIds.Count;
            List<string> lines = new List<string>
            {
                "--- sign-in / activity nodes activityCount=" + activityCount + " source=" + binding.Source + " ---"
            };

            List<string> nodeParts = this.dailyClaimsNodeStateBuffer;
            for (int i = 0; i < activityIds.Count; i++)
            {
                int activityId = activityIds[i];
                nodeParts.Clear();
                if (!this.DailyClaimsTryGetActivityNodeStateNames(binding, activityId, nodeParts, out string nodeStatus))
                {
                    lines.Add("activityId=" + activityId + " nodes=error(" + nodeStatus + ")");
                    continue;
                }

                if (nodeParts.Count == 0)
                {
                    lines.Add("activityId=" + activityId + " nodes=0");
                    continue;
                }

                List<string> displayParts = new List<string>(nodeParts.Count);
                for (int n = 0; n < nodeParts.Count; n++)
                {
                    string stateName = nodeParts[n];
                    displayParts.Add(n + ":" + stateName);
                    if (string.Equals(stateName, "WaitClaim", StringComparison.Ordinal))
                    {
                        waitClaimNodes++;
                    }
                }

                lines.Add("activityId=" + activityId + " [" + string.Join(", ", displayParts.ToArray()) + "]");
            }

            return string.Join("\n", lines.ToArray());
        }

        private int ClaimSignInRewards(out string detail)
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

            List<string> lines = new List<string>();
            List<string> nodeParts = this.dailyClaimsNodeStateBuffer;
            int sent = 0;

            for (int i = 0; i < activityIds.Count; i++)
            {
                int activityId = activityIds[i];
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

                    if (this.TryReceiveActivityReward(activityId, n, out string claimStatus))
                    {
                        sent++;
                        lines.Add("claimed activityId=" + activityId + " nodeIndex=" + n + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED activityId=" + activityId + " nodeIndex=" + n + " (" + claimStatus + ")");
                    }
                }
            }

            // New Life Log (新生活日志) is OperationActivity 1003, and NewPlayerJournalWidget claims it
            // with the very call above — so the sweep already covers it whenever the server lists
            // 1003 as alive. GetAliveActivityIds mirrors the ECS filter rather than the panel, so if
            // 1003 is missing probe it directly: GetActivityNodeStateById returns an empty array for
            // an activity that genuinely is not running, making this read-then-claim, never a blind
            // send.
            if (!activityIds.Contains(DailyClaimsNewLifeLogActivityId))
            {
                nodeParts.Clear();
                if (this.DailyClaimsTryGetActivityNodeStateNames(
                    binding,
                    DailyClaimsNewLifeLogActivityId,
                    nodeParts,
                    out string lifeLogStatus))
                {
                    int lifeLogSent = 0;
                    for (int n = 0; n < nodeParts.Count; n++)
                    {
                        if (!string.Equals(nodeParts[n], "WaitClaim", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (this.TryReceiveActivityReward(DailyClaimsNewLifeLogActivityId, n, out string claimStatus))
                        {
                            sent++;
                            lifeLogSent++;
                            lines.Add("claimed New Life Log nodeIndex=" + n + " (" + claimStatus + ")");
                        }
                        else
                        {
                            lines.Add("FAILED New Life Log nodeIndex=" + n + " (" + claimStatus + ")");
                        }
                    }

                    if (lifeLogSent == 0)
                    {
                        lines.Add("New Life Log (activityId=" + DailyClaimsNewLifeLogActivityId
                            + ") not in alive list: nodes=" + nodeParts.Count + " none waiting");
                    }
                }
                else
                {
                    lines.Add("New Life Log (activityId=" + DailyClaimsNewLifeLogActivityId
                        + ") state read failed: " + lifeLogStatus);
                }
            }

            if (lines.Count == 0)
            {
                lines.Add("no WaitClaim nodes found across " + activityIds.Count + " activities (source=" + binding.Source + ")");
            }

            detail = string.Join("\n", lines.ToArray());
            return sent;
        }

        private bool TryClaimMailAll(out string status)
        {
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager",
                "MailProtocolManager",
                "RequestAllRewards",
                null,
                out status);
        }

        private bool TryClaimMiniBpAll(out string status)
        {
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.BattlePass.BattlePassProtocolManager",
                "BattlePassProtocolManager",
                "GetAllRewards",
                null,
                out status);
        }

        private bool TryClaimBpLoop(out string status)
        {
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.BattlePass.BattlePassProtocolManager",
                "BattlePassProtocolManager",
                "GetLoopRewards",
                null,
                out status);
        }

        // ==========================================================================================
        // Festival challenges (mini-BP tab1 每周挑战 + tab2 庆典活动/潮流活动)
        //
        // Both tabs render TableBpActivity.taskGroup entries and "claim" means SUBMIT the task:
        // BattlePassStarterTaskWidget -> TaskSystem.SubmitTask(netId) -> TaskProtocolManager
        // .ClientSubmitTask(staticId) -> SubmitGameTaskNetworkCommand{GameTaskId}. Rather than walk
        // period -> issues -> activities -> taskGroup through four tables, this sweeps the tasks the
        // player actually holds (TaskSystem.GetAllTasks, the same source Quest Assistant uses) and
        // keeps the ones the game itself classifies as battle-pass tasks.
        // ==========================================================================================

        // TaskSystem keeps game tasks in TWO dictionaries and hands them out through two getters:
        //   GetAllTasks()              -> _tasks
        //   GetOperationActivityTasks()-> _operationActivityTasks
        // OnCreate/OnUpdateTaskItem route every `TableGameTask.type == 10` task into the SECOND one
        // and `return` — so an activity-mission task is NEVER in _tasks. Scanning only GetAllTasks
        // (as the first cut did) therefore reports zero event missions no matter what is pending,
        // which is exactly why the Sanrio daily quests stayed unclaimed on 2026-08-10.
        private bool DailyClaimsTryCollectSubmittableTaskIds(List<int> taskIds, out string status)
        {
            return this.DailyClaimsTryCollectSubmittableTaskIdsFrom("GetAllTasks", taskIds, out status);
        }

        // Everything in _operationActivityTasks is type 10 by construction, so the caller needs no
        // TableGameTask classification for these — membership IS the classification.
        private bool DailyClaimsTryCollectSubmittableActivityMissionIds(List<int> taskIds, out string status)
        {
            return this.DailyClaimsTryCollectSubmittableTaskIdsFrom("GetOperationActivityTasks", taskIds, out status);
        }

        // Pass 1 of the sweep: scalarize (taskId) for every CanSubmit task while the mono objects are
        // pinned, so pass 2 (which invokes TableData.GetGameTask and sends commands — both allocate
        // mono-side) never holds a raw MonoObject*.
        private bool DailyClaimsTryCollectSubmittableTaskIdsFrom(string getterName, List<int> taskIds, out string status)
        {
            taskIds.Clear();
            status = "TaskSystem unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            // Reuses Quest Assistant's resolver (same partial class): it caches the class/method and
            // deliberately re-resolves the DataModule instance every call (CI lint E3).
            if (!this.QuestAssistantEnsureTaskSystem(out IntPtr taskSystemObj, out string taskSysStatus)
                || taskSystemObj == IntPtr.Zero)
            {
                status = "TaskSystem: " + taskSysStatus;
                return false;
            }

            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(taskSystemObj), getterName, 0);
            if (getter == IntPtr.Zero)
            {
                status = getterName + " method missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr listObj = auraMonoRuntimeInvoke(getter, taskSystemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                status = getterName + " invoke failed";
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                // A false here also means "empty" on this build (the enumerator cannot tell them
                // apart) — an empty task list is a legitimate "nothing to submit", not a failure.
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                {
                    status = getterName + " returned no items";
                    return true;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr boxedTask = items[i];
                    if (boxedTask == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(boxedTask, "taskItemComponent", out IntPtr component)
                        || component == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Member reads box, i.e. allocate — pin the component for the read pair.
                    uint componentPin = AuraMonoPinNew(component);
                    try
                    {
                        if (!this.TryGetMonoIntMember(component, "TaskState", out int state)
                            || state != DailyClaimsGameTaskStateCanSubmit)
                        {
                            continue;
                        }

                        if (this.TryGetMonoIntMember(component, "TaskId", out int taskId) && taskId > 0)
                        {
                            taskIds.Add(taskId);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(componentPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = getterName + " scanned " + items.Count + " task(s)";
            return true;
        }

        // Reads the two TableGameTask columns that classify a CanSubmit task: `autoSubmit` and
        // `type` (GameTaskType). Both callers below key off them, so the row is fetched once and
        // pinned for the read pair (member reads box, i.e. allocate).
        private bool DailyClaimsTryGetGameTaskKind(int taskId, out int autoSubmit, out int taskType, out string status)
        {
            autoSubmit = -1;
            taskType = -1;
            if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr row, out string rowStatus) || row == IntPtr.Zero)
            {
                status = "GetGameTask: " + rowStatus;
                return false;
            }

            uint rowPin = AuraMonoPinNew(row);
            try
            {
                if (!this.TryGetMonoIntMember(row, "autoSubmit", out autoSubmit))
                {
                    status = "autoSubmit unreadable";
                    return false;
                }

                if (!this.TryGetMonoIntMember(row, "type", out taskType))
                {
                    status = "type unreadable";
                    return false;
                }

                status = "autoSubmit=" + autoSubmit + " type=" + taskType;
                return true;
            }
            finally
            {
                AuraMonoPinFree(rowPin);
            }
        }

        // ClientRedPointSystem.IsBattlePassTaskCanSubmitRange, verbatim: autoSubmit == 2 and type in
        // {3, 11} (GameTaskType.BattlePass / MiniBp). Anything else belongs to another sweep — the
        // operation-activity missions below, or Quest Assistant for ordinary quests.
        private bool DailyClaimsIsBattlePassTask(int taskId, out string status)
        {
            if (!this.DailyClaimsTryGetGameTaskKind(taskId, out int autoSubmit, out int taskType, out status))
            {
                return false;
            }

            bool isBpTask = autoSubmit == DailyClaimsBpTaskAutoSubmit
                && (taskType == DailyClaimsBpTaskTypeA || taskType == DailyClaimsBpTaskTypeB);
            status = status + (isBpTask ? " bp" : " not a bp task");
            return isBpTask;
        }

        private bool TrySubmitDailyClaimsGameTask(int taskId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.GameplayLayer.GameTask.SubmitGameTaskNetworkCommand",
                new[] { "GameTaskId" },
                new[] { taskId },
                out status);
        }

        // ==========================================================================================
        // Festival rewards (the per-issue point milestones beside the challenge list)
        //
        // BattlePassActivityRewardWidget claims with BattlePassSystem.GetBpActivityReward(issueId) ->
        // BattlePassProtocolManager.GetBpActivityReward -> SeriesRewardTriggerNetworkCommand with
        // TriggerId = TableBpPeriod(1) * 100000 + issueId. ONE call per issue drains every milestone
        // that issue has earned (the server compares current vs rewarded point counts), so the sweep
        // is "one command per week of the live period".
        //
        // Going through the protocol manager rather than building the command keeps us off the
        // ESeriesRewardTriggerId enum field (TrySetObjectMember would have to Enum.ToObject it).
        // ==========================================================================================

        private bool DailyClaimsTryGetCurrentBpPeriodId(out int periodId, out string status)
        {
            periodId = 0;
            if (!this.TryDailyClaimsGetAuraMonoBattlePassSystem(out IntPtr battlePassSystem, out status))
            {
                return false;
            }

            if (!this.DailyClaimsTryAuraMonoInvokeIntInstance(battlePassSystem, "GetCurrentPeriodId", out periodId)
                || periodId <= 0)
            {
                status = "GetCurrentPeriodId failed";
                return false;
            }

            status = "GetCurrentPeriodId ok";
            return true;
        }

        private bool TryClaimDailyClaimsBpIssueReward(int issueId, out string status)
        {
            object[] args = { issueId };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.BattlePass.BattlePassProtocolManager",
                "BattlePassProtocolManager",
                "GetBpActivityReward",
                args,
                out status);
        }

        // ==========================================================================================
        // Collection rewards (图鉴积分奖励 — the Pictorial point track)
        //
        // PictorialLevelRewardWidget claims with PictorialSystem.GetPictorialRewards(type) ->
        // GetPictorialRewardCommand{id = (int)EPictorialNewType}; the server hands over every
        // milestone of that category whose point threshold is met and not yet paid, so one command
        // per category drains the whole track.
        // ==========================================================================================

        // ==========================================================================================
        // Generic AuraMono command sender
        //
        // Most claims reach the server through a protocol-manager or DataModule wrapper, which is a
        // plain static/instance invoke. Certifications and dream targets have no wrapper at all —
        // their UI panels dispatch the command inline — so those need the command struct built and
        // sent directly, which is what this does.
        //
        // The 2026-08-10 in-world run confirmed why there is no managed path here: the EcsClient
        // network-command structs are absent from the interop, so every claim that landed went
        // through AuraMono and every managed attempt was a silent no-op.
        //
        // The resolve → inflate → allocate → poke → invoke sequence this used to carry itself is now
        // HeartopiaComplete.TryAuraSendCommand (HeartopiaComplete.AuraSendCommand.cs); what is left
        // here is the array-shaped call convention its eight claim sites are written against.
        // ==========================================================================================

        // Every claim command in this feature takes int fields only — the shared sender picks the
        // write width from each value's runtime type, so boxing as int keeps the exact 4-byte writes
        // the hand-rolled loop did.
        private bool TryDailyClaimsSendCommandAura(
            string commandFullName,
            string[] fieldNames,
            int[] fieldValues,
            out string status)
        {
            int fieldCount = fieldNames != null ? fieldNames.Length : 0;
            if (fieldCount != (fieldValues != null ? fieldValues.Length : 0))
            {
                status = commandFullName + " field name/value count mismatch";
                return false;
            }

            Dictionary<string, object> fields = new Dictionary<string, object>(fieldCount, StringComparer.Ordinal);
            for (int i = 0; i < fieldCount; i++)
            {
                fields[fieldNames[i]] = fieldValues[i];
            }

            if (!this.TryAuraSendCommand(commandFullName, fields, AuraChannelReliable, true, out status))
            {
                return false;
            }

            status = "AuraMono SendCommand " + commandFullName + " ok";
            return true;
        }

        // ==========================================================================================
        // Shared plumbing for the table-driven sweeps
        // ==========================================================================================

        // Walks a `TableData` static Dictionary<int, TRow> and hands each row to `onRow` while BOTH
        // the entry and the row are pinned. The callback may only read scalars into managed state —
        // it must never send a command or invoke anything that allocates a mono object it then keeps,
        // because the whole point is to scalarize before the caller starts sending.
        private bool DailyClaimsForEachTableRow(string staticFieldName, Action<IntPtr> onRow, out string status)
        {
            status = staticFieldName + " unavailable";
            if (onRow == null || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            // TableData sits in the GLOBAL namespace of the EcsClient image, so resolving it by a
            // "EcsClient.TableData" full name (or with "EcsClient" as the namespace) misses — which
            // is exactly how the first cut of this walker reported "TableData class missing" for
            // every table on 2026-08-10. Reuse the image-based resolver Daily Quest Submit already
            // proved out instead of re-deriving the lookup.
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero)
            {
                status = "TableData class missing: " + classStatus;
                return false;
            }

            // Static reads fail closed until the Mono side is proven up (AuraMonoStaticFieldReadsAllowed).
            if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldName, out IntPtr dictObj)
                || dictObj == IntPtr.Zero)
            {
                status = staticFieldName + " static field unreadable";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            int rows = 0;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(dictObj, entries, pins))
                {
                    status = staticFieldName + " empty";
                    return true;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Dictionary enumeration yields boxed KeyValuePair<int, TRow>; `value` is the row
                    // (a class). Fall back to the entry itself for collections that hand out rows
                    // directly.
                    if (!this.TryGetMonoObjectMember(entry, "value", out IntPtr row) || row == IntPtr.Zero)
                    {
                        row = entry;
                    }

                    uint rowPin = AuraMonoPinNew(row);
                    try
                    {
                        onRow(row);
                        rows++;
                    }
                    finally
                    {
                        AuraMonoPinFree(rowPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = staticFieldName + " rows=" + rows;
            return true;
        }

        // Several table columns are private byte fields behind a public int property (quantity,
        // dreamType). Reading the FIELD would unbox one byte as four; the property getter returns a
        // real Int32, so it is the only correct path.
        private bool DailyClaimsTryGetMonoPropertyInt(IntPtr obj, string getterName, out int value)
        {
            value = 0;
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(obj), getterName, 0);
            if (getter == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, obj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoInt32(boxed, out value);
        }

        // DataModule<RedPointManager>.Instance — re-resolved by the caller per chunk, never cached
        // across a yield (CI lint E3).
        private IntPtr DailyClaimsResolveRedPointManager()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return IntPtr.Zero;
            }

            if (this.dailyClaimsAuraRedPointManagerClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraRedPointManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTGameSystem.GameplaySystem.RedPoint.RedPointManager");
                if (this.dailyClaimsAuraRedPointManagerClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraRedPointManagerClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.RedPoint",
                        "RedPointManager");
                }
            }

            if (this.dailyClaimsAuraRedPointManagerClass == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return this.TryGetAuraMonoDataModuleInstance(this.dailyClaimsAuraRedPointManagerClass);
        }

        // RedPointManager.GetRedPointState(RedPointEnum, int). Both args cross as plain 4-byte ints.
        // Returns false for ids the server never flagged (the node simply does not exist), which is
        // exactly the gate this needs.
        private unsafe bool DailyClaimsTryGetRedPointState(IntPtr redPointManager, int redPointEnum, int id, out bool active)
        {
            active = false;
            if (redPointManager == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(redPointManager),
                "GetRedPointState",
                2);
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
            IntPtr boxed = auraMonoRuntimeInvoke(method, redPointManager, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out active);
        }

        // IPictorialService reads used by the suit sweep — both single-int-arg, boxed scalar return.
        private unsafe bool DailyClaimsTryInvokePictorialServiceInt(
            IntPtr service,
            string methodName,
            int arg,
            out int value)
        {
            value = 0;
            if (service == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(service), methodName, 1);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            int argValue = arg;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&argValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, service, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryUnboxMonoInt32(boxed, out value))
            {
                return true;
            }

            if (this.TryUnboxMonoBoolean(boxed, out bool flag))
            {
                value = flag ? 1 : 0;
                return true;
            }

            return false;
        }

        // One chunk of the suit-ownership gate. SYNCHRONOUS on purpose: the service pointer is
        // resolved, used and dropped inside this call, so the caller's coroutine never carries a
        // mono pointer across a frame boundary (CI lint W1 / [[auramono-raw-pointers-across-yields]]).
        // Returns false when the gate cannot run at all, which stops the sweep instead of silently
        // reporting "owned=0".
        private bool DailyClaimsFilterOwnedSuitChunk(
            List<int> suitIds,
            int start,
            int count,
            List<int> ownedSuitIds,
            List<int> ownedSuitCounts,
            out string status)
        {
            if (!this.TryEnsureDailyClaimsPictorialService(out DailyClaimsServiceBinding binding, out status))
            {
                return false;
            }

            // The suit reads are AuraMono-only (project preference for EcsClient/XDT* services); a
            // managed-only binding cannot answer them, and pretending otherwise would report every
            // suit as unowned.
            if (binding.AuraMono == IntPtr.Zero)
            {
                status = status + " (managed-only binding — suit gate unavailable)";
                return false;
            }

            int end = Math.Min(start + count, suitIds.Count);
            for (int i = start; i < end; i++)
            {
                if (this.DailyClaimsTryInvokePictorialServiceInt(
                    binding.AuraMono, "GetPictorialSuitHasNum", suitIds[i], out int hasNum)
                    && hasNum > 0)
                {
                    ownedSuitIds.Add(suitIds[i]);
                    ownedSuitCounts.Add(hasNum);
                }
            }

            return true;
        }

        private bool DailyClaimsIsAllSuitRewardClaimed(int suitId)
        {
            if (!this.TryEnsureDailyClaimsPictorialService(out DailyClaimsServiceBinding binding, out _)
                || binding.AuraMono == IntPtr.Zero)
            {
                return false;
            }

            return this.DailyClaimsTryInvokePictorialServiceInt(
                binding.AuraMono, "GetPediaAllSuitRewardClaimed", suitId, out int claimedFlag)
                && claimedFlag != 0;
        }

        // One chunk of the certification red-point gate — same synchronous-scope rule as above.
        private bool DailyClaimsFilterClaimableCertificationChunk(
            List<int> certIds,
            int start,
            int count,
            List<int> claimable)
        {
            IntPtr redPointManager = this.DailyClaimsResolveRedPointManager();
            if (redPointManager == IntPtr.Zero)
            {
                return false;
            }

            int end = Math.Min(start + count, certIds.Count);
            for (int i = start; i < end; i++)
            {
                if (this.DailyClaimsTryGetRedPointState(
                    redPointManager, DailyClaimsRedPointCollectCertification, certIds[i], out bool active)
                    && active)
                {
                    claimable.Add(certIds[i]);
                }
            }

            return true;
        }

        private bool TryEnsureDailyClaimsPictorialService(out DailyClaimsServiceBinding binding, out string status)
        {
            if (TryTakeCachedDailyClaimsService(ref this.dailyClaimsPictorialServiceCache, this.dailyClaimsPictorialServiceSource, out binding, out status))
            {
                return true;
            }

            return this.TryResolveDailyClaimsService(
                new[]
                {
                    "XDTDataAndProtocol.ProtocolService.Pictorial.IPictorialService",
                    "EcsSystem.ClientSystem.Pictorial.PictorialClientService",
                    "ClientSystem.Pictorial.PictorialClientService"
                },
                new[] { "Pictorial", "IPictorialService" },
                out binding,
                out status);
        }

        // DataModule<PictorialSystem>.Instance.GetPictorialRewards(type) — the parameter is an enum,
        // which crosses mono_runtime_invoke as a plain 4-byte int. Confirmed working in-world.
        private bool TryClaimDailyClaimsPictorialTypeReward(int pictorialType, out string status)
        {
            return this.TryDailyClaimsInvokePictorialSystemGetRewards(pictorialType, out status);
        }

        private unsafe bool TryDailyClaimsInvokePictorialSystemGetRewards(int pictorialType, out string status)
        {
            status = "PictorialSystem unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.dailyClaimsAuraPictorialSystemClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraPictorialSystemClass = this.FindAuraMonoClassByFullName(
                    "XDTGameSystem.GameplaySystem.Pictorial.PictorialSystem");
                if (this.dailyClaimsAuraPictorialSystemClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraPictorialSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.Pictorial",
                        "PictorialSystem");
                }
            }

            if (this.dailyClaimsAuraPictorialSystemClass == IntPtr.Zero)
            {
                status = "AuraMono PictorialSystem class missing";
                return false;
            }

            IntPtr pictorialSystem = this.TryGetAuraMonoDataModuleInstance(this.dailyClaimsAuraPictorialSystemClass);
            if (pictorialSystem == IntPtr.Zero)
            {
                status = "AuraMono DataModule<PictorialSystem>.Instance missing";
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(pictorialSystem),
                "GetPictorialRewards",
                1);
            if (method == IntPtr.Zero)
            {
                status = "GetPictorialRewards AuraMono method missing";
                return false;
            }

            int typeValue = pictorialType;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&typeValue);
            auraMonoRuntimeInvoke(method, pictorialSystem, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "GetPictorialRewards AuraMono invoke failed";
                return false;
            }

            status = "AuraMono PictorialSystem.GetPictorialRewards ok";
            return true;
        }

        // ==========================================================================================
        // Command senders for the round-2 sweeps. Every one of these commands carries only int
        // fields, so TrySetObjectMember can populate them directly — no enum conversion anywhere.
        // ==========================================================================================

        // GetHomeEvaluationRewardCommand is an EMPTY struct (Size=1): the server derives everything.
        private bool TryClaimDailyClaimsHomeEvaluationReward(out string status)
        {
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.Homeland.HomelandProtocolManager",
                "HomelandProtocolManager",
                "GetHomeEvaluationReward",
                null,
                out status);
        }

        // No protocol/system wrapper exists — DreamDetailPanel dispatches this inline.
        private bool TryClaimDailyClaimsDreamTargetReward(int dreamType, int targetId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.Dream.DrawDreamTargetRewardCommand",
                new[] { "DreamType", "TargetId" },
                new[] { dreamType, targetId },
                out status);
        }

        private bool TryClaimDailyClaimsStickerThemeReward(int activityId, int themeId, int nodeIndex, out string status)
        {
            object[] args = { activityId, themeId, nodeIndex };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.OperationActivity.OperationActivityProtocolMananger",
                "OperationActivityProtocolMananger",
                "ReceiveStickerActivityThemeReward",
                args,
                out status);
        }

        private bool TryClaimDailyClaimsBadgeThemeReward(int themeId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.OperationActivityCenter.BadgeGetThemeProcessRewardNetworkCommand",
                new[] { "ThemeId" },
                new[] { themeId },
                out status);
        }

        private bool TryClaimDailyClaimsBadgeFinalReward(int activityId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.OperationActivityCenter.BadgeGetFinalRewardNetworkCommand",
                new[] { "ActId" },
                new[] { activityId },
                out status);
        }

        // Confirmed working in-world 2026-08-10 (15 alive activities swept).
        private bool TryClaimDailyClaimsActivityBpReward(int activityId, out string status)
        {
            object[] args = { activityId };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.OperationActivity.OperationActivityProtocolMananger",
                "OperationActivityProtocolMananger",
                "ReceiveActivityBPReward",
                args,
                out status);
        }

        private bool TryClaimDailyClaimsPictorialSuitReward(int suitId, int quantity, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.Pictorial.GetPictorialSuitRewardCommand",
                new[] { "SuitId", "SuitNumReward" },
                new[] { suitId, quantity },
                out status);
        }

        private bool TryClaimDailyClaimsPediaAllSuitReward(int suitId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.Pictorial.GetPediaAllSuitRewardCommand",
                new[] { "SuitId" },
                new[] { suitId },
                out status);
        }

        // No wrapper — PictorialCollectProgressWidget dispatches this inline.
        private bool TryClaimDailyClaimsCertificationReward(int staticId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.Pictorial.GetCertificationRewardNetworkCommand",
                new[] { "StaticId" },
                new[] { staticId },
                out status);
        }

        private bool TryClaimDailyClaimsCatMomentReward(int staticId, out string status)
        {
            return this.TryDailyClaimsSendCommandAura(
                "XDT.Scene.Shared.Modules.Pictorial.GetPictorialCatRewardCommand",
                new[] { "id" },
                new[] { staticId },
                out status);
        }

        // ==========================================================================================
        // Table collectors — every one scalarizes into managed lists BEFORE anything is sent.
        // ==========================================================================================

        private bool DailyClaimsTryCollectDreamTargets(List<DailyClaimsDreamTarget> targets, out string status)
        {
            targets.Clear();
            return this.DailyClaimsForEachTableRow("TableDreamTaskTypes", row =>
            {
                if (!this.TryGetMonoIntMember(row, "id", out int targetId) || targetId <= 0)
                {
                    return;
                }

                // dreamType is a public int PROPERTY over a private byte — the field read would be
                // wrong by three bytes. Fall back to the id's own hundreds digit, which is how the
                // shipped rows are numbered (101→1 … 602→6).
                if (!this.DailyClaimsTryGetMonoPropertyInt(row, "get_dreamType", out int dreamType) || dreamType <= 0)
                {
                    dreamType = targetId / 100;
                }

                if (dreamType > 0)
                {
                    targets.Add(new DailyClaimsDreamTarget { DreamType = dreamType, TargetId = targetId });
                }
            }, out status);
        }

        // Shared by TableStickerThemes and TableBadgeThemes — both rows are {int id; int activityId;}.
        private bool DailyClaimsTryCollectActivityThemes(
            string staticFieldName,
            List<DailyClaimsActivityTheme> themes,
            out string status)
        {
            themes.Clear();
            return this.DailyClaimsForEachTableRow(staticFieldName, row =>
            {
                if (this.TryGetMonoIntMember(row, "id", out int themeId) && themeId > 0
                    && this.TryGetMonoIntMember(row, "activityId", out int activityId) && activityId > 0)
                {
                    themes.Add(new DailyClaimsActivityTheme { ActivityId = activityId, ThemeId = themeId });
                }
            }, out status);
        }

        private bool DailyClaimsTryCollectSuitRewardTiers(List<DailyClaimsSuitRewardTier> tiers, out string status)
        {
            tiers.Clear();
            return this.DailyClaimsForEachTableRow("TablePediaSuitRewards", row =>
            {
                if (!this.TryGetMonoIntMember(row, "suitId", out int suitId) || suitId <= 0)
                {
                    return;
                }

                // quantity: public int property over a private byte (see get_dreamType above).
                if (!this.DailyClaimsTryGetMonoPropertyInt(row, "get_quantity", out int quantity) || quantity <= 0)
                {
                    return;
                }

                this.TryGetMonoIntMember(row, "id", out int rowId);
                tiers.Add(new DailyClaimsSuitRewardTier { RowId = rowId, SuitId = suitId, Quantity = quantity });
            }, out status);
        }

        private bool DailyClaimsTryCollectCertificationIds(List<int> ids, out string status)
        {
            ids.Clear();
            return this.DailyClaimsForEachTableRow("TableCollectCertifications", row =>
            {
                if (this.TryGetMonoIntMember(row, "id", out int id) && id > 0
                    && this.TryGetMonoBoolMember(row, "openCertification", out bool open) && open)
                {
                    ids.Add(id);
                }
            }, out status);
        }

        private bool DailyClaimsTryCollectSimpleTableIds(string staticFieldName, List<int> ids, out string status)
        {
            ids.Clear();
            return this.DailyClaimsForEachTableRow(staticFieldName, row =>
            {
                if (this.TryGetMonoIntMember(row, "id", out int id) && id > 0)
                {
                    ids.Add(id);
                }
            }, out status);
        }

        private bool DailyClaimsTryCollectBadgeFinalActivityIds(List<int> activityIds, out string status)
        {
            activityIds.Clear();
            List<int> collected = new List<int>();
            bool ok = this.DailyClaimsForEachTableRow("TableBadgeFinalRewards", row =>
            {
                if (this.TryGetMonoIntMember(row, "activityId", out int activityId) && activityId > 0)
                {
                    collected.Add(activityId);
                }
            }, out status);

            for (int i = 0; i < collected.Count; i++)
            {
                if (!activityIds.Contains(collected[i]))
                {
                    activityIds.Add(collected[i]);
                }
            }

            return ok;
        }

        private string LogTownGuideRewardState(out int nodeRewardCount, out int chapterRewardCount)
        {
            nodeRewardCount = 0;
            chapterRewardCount = 0;
            if (!this.TryEnsureDailyClaimsTownGuideService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                return "ITownGuidesService unavailable: " + serviceStatus;
            }

            this.DailyClaimsLog("Town guide service ready via " + binding.Source);
            List<DailyClaimsTownGuideChapterSnapshot> chapters = this.dailyClaimsTownGuideChapterBuffer;
            chapters.Clear();
            if (!this.DailyClaimsTryGetTownGuideChapters(binding, chapters, out string listStatus))
            {
                return "GetAllChapterInfo failed: " + listStatus;
            }

            List<string> lines = new List<string>
            {
                "--- town guide chapters=" + chapters.Count + " source=" + binding.Source + " ---"
            };

            for (int i = 0; i < chapters.Count; i++)
            {
                DailyClaimsTownGuideChapterSnapshot chapter = chapters[i];
                if (chapter.ChapterId <= 0)
                {
                    continue;
                }

                if (string.Equals(chapter.ChapterState, "Reward", StringComparison.Ordinal))
                {
                    chapterRewardCount++;
                }

                List<string> nodeParts = new List<string>();
                if (chapter.Nodes != null)
                {
                    for (int n = 0; n < chapter.Nodes.Count; n++)
                    {
                        DailyClaimsTownGuideNodeSnapshot node = chapter.Nodes[n];
                        nodeParts.Add(node.NodeId + ":" + node.State);
                        if (string.Equals(node.State, "Reward", StringComparison.Ordinal))
                        {
                            nodeRewardCount++;
                        }
                    }
                }

                lines.Add("chapterId=" + chapter.ChapterId + " state=" + chapter.ChapterState + " nodes=[" + string.Join(", ", nodeParts.ToArray()) + "]");
            }

            return string.Join("\n", lines.ToArray());
        }

        private string LogMailRewardState(out bool anyRewardable, out int rewardableCount)
        {
            anyRewardable = false;
            rewardableCount = 0;
            string source = "unavailable";

            if (this.TryDailyClaimsTryGetMailServiceAuraMono(out IntPtr mailService, out string mailStatus))
            {
                source = mailStatus;
                if (this.DailyClaimsTryAuraMonoInvokeBoolInstance(mailService, "IsAnyRewardable", out bool auraAny))
                {
                    anyRewardable = auraAny;
                }

                rewardableCount = this.DailyClaimsCountAuraMonoRewardableMails(mailService);
            }
            else if (rewardableCount == 0 && anyRewardable)
            {
                rewardableCount = -1;
            }

            return "--- mail source=" + source
                + " anyRewardable=" + anyRewardable
                + " rewardableCount=" + (rewardableCount < 0 ? "?" : rewardableCount.ToString()) + " ---";
        }

        private string LogMiniBpRewardState(out int freeCanGet, out int paidCanGet)
        {
            freeCanGet = 0;
            paidCanGet = 0;
            if (!this.TryDailyClaimsGetAuraMonoBattlePassSystem(out IntPtr battlePassSystem, out string status))
            {
                return "--- mini BP unavailable: " + status + " ---";
            }

            freeCanGet = this.DailyClaimsCountAuraMonoBattlePassSlotsCanGet(
                battlePassSystem,
                "GetFreeBattlePassSlots",
                out string freeStatus);
            paidCanGet = this.DailyClaimsCountAuraMonoBattlePassSlotsCanGet(
                battlePassSystem,
                "GetPayBattlePassSlots",
                out string paidStatus);

            return "--- mini BP slots freeCanGet=" + freeCanGet + " (" + freeStatus + ")"
                + " paidCanGet=" + paidCanGet + " (" + paidStatus + ") ---";
        }

        private string LogBpLoopRewardState(out bool claimable, out int pendingCycles)
        {
            claimable = false;
            pendingCycles = 0;
            if (!this.TryDailyClaimsGetAuraMonoBattlePassSystem(out IntPtr battlePassSystem, out string status))
            {
                return "--- BP loop unavailable: " + status + " ---";
            }

            if (!this.DailyClaimsTryAuraMonoInvokeObjectInstance(
                battlePassSystem,
                "GetBattlePassData",
                0,
                out IntPtr battlePassDataObj,
                out string dataStatus)
                || battlePassDataObj == IntPtr.Zero)
            {
                return "--- BP loop GetBattlePassData failed: " + dataStatus + " ---";
            }

            this.TryGetMonoInt32Member(battlePassDataObj, "curExp", out int curExp);
            this.TryGetMonoInt32Member(battlePassDataObj, "level", out int level);
            this.TryGetMonoInt32Member(battlePassDataObj, "curPeriodId", out int periodId);
            this.TryGetMonoInt32Member(battlePassDataObj, "cycleRewardNum", out int claimedCycleNum);

            int maxLevel = 0;
            if (this.DailyClaimsTryAuraMonoInvokeIntInstance(battlePassSystem, "GetBpMaxLevel", out int auraMaxLevel))
            {
                maxLevel = auraMaxLevel;
            }

            int cycleNeed = this.DailyClaimsTryGetBattlePassCycleNeedPointAuraMono(periodId);
            if (cycleNeed > 0)
            {
                pendingCycles = curExp / cycleNeed;
                claimable = pendingCycles > 0;
            }

            bool redPointClaimable = maxLevel > 0
                && level >= maxLevel
                && cycleNeed > 0
                && curExp >= cycleNeed;

            return "--- BP loop periodId=" + periodId
                + " level=" + level + "/" + maxLevel
                + " curExp=" + curExp
                + " cycleNeed=" + cycleNeed
                + " pendingCycles=" + pendingCycles
                + " redPoint=" + redPointClaimable
                + " claimedCycleNum=" + claimedCycleNum + " ---";
        }

        private bool TryDailyClaimsTryGetMailServiceAuraMono(out IntPtr service, out string status)
        {
            service = IntPtr.Zero;
            status = "IMailClientService unavailable";
            this.EnsureDailyClaimsReflectionReady();
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            string[] serviceClassNames =
            {
                "XDTDataAndProtocol.ProtocolService.Mail.IMailClientService",
                "ClientSystem.Mail.MailServiceClient"
            };

            for (int i = 0; i < serviceClassNames.Length; i++)
            {
                IntPtr serviceClass = this.FindAuraMonoClassByFullName(serviceClassNames[i]);
                if (serviceClass == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryDailyClaimsAuraMonoEcsTryGet(serviceClass, false, out IntPtr serviceObj, out string tryGetStatus)
                    && serviceObj != IntPtr.Zero)
                {
                    service = serviceObj;
                    status = "AuraMono EcsService.TryGet: " + this.GetAuraMonoClassDisplayName(serviceClass);
                    return true;
                }

                status = tryGetStatus;
            }

            return false;
        }

        private int DailyClaimsCountAuraMonoRewardableMails(IntPtr mailService)
        {
            if (mailService == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            if (!this.DailyClaimsTryAuraMonoInvokeObjectInstance(mailService, "GetMails", 0, out IntPtr mailsObj, out _)
                || mailsObj == IntPtr.Zero)
            {
                return 0;
            }

            // Pinned walk: IsMailRewardable boxes its bool return on every item, so an unpinned
            // items[] would be walking relocated mail objects by the time SGen fires (AGENTS.md §11).
            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            List<uint> pins = this.dailyClaimsAuraMonoPinBuffer;
            items.Clear();
            pins.Clear();
            uint mailsPin = AuraMonoPinNew(mailsObj);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(mailsObj, items, pins) || items.Count == 0)
                {
                    return 0;
                }

                IntPtr serviceClass = auraMonoObjectGetClass(mailService);
                IntPtr isMailRewardableMethod = this.FindAuraMonoMethodOnHierarchy(serviceClass, "IsMailRewardable", 1);
                if (isMailRewardableMethod == IntPtr.Zero)
                {
                    return 0;
                }

                int rewardableCount = 0;
                unsafe
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr mailObj = items[i];
                        if (mailObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = mailObj;
                        IntPtr boxedResult = auraMonoRuntimeInvoke(isMailRewardableMethod, mailService, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero
                            && boxedResult != IntPtr.Zero
                            && this.TryUnboxMonoBoolean(boxedResult, out bool rewardable)
                            && rewardable)
                        {
                            rewardableCount++;
                        }
                    }
                }

                return rewardableCount;
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(mailsPin);
            }
        }

        private bool TryDailyClaimsGetAuraMonoBattlePassSystem(out IntPtr battlePassSystem, out string status)
        {
            battlePassSystem = IntPtr.Zero;
            status = "BattlePassSystem unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.dailyClaimsAuraBattlePassSystemClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraBattlePassSystemClass = this.FindAuraMonoClassByFullName(
                    "XDTGameSystem.GameplaySystem.BattlePass.BattlePassSystem");
                if (this.dailyClaimsAuraBattlePassSystemClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraBattlePassSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.GameplaySystem.BattlePass",
                        "BattlePassSystem");
                }
            }

            if (this.dailyClaimsAuraBattlePassSystemClass == IntPtr.Zero)
            {
                status = "AuraMono BattlePassSystem class missing";
                return false;
            }

            battlePassSystem = this.TryGetAuraMonoDataModuleInstance(this.dailyClaimsAuraBattlePassSystemClass);
            if (battlePassSystem == IntPtr.Zero)
            {
                status = "AuraMono DataModule<BattlePassSystem>.Instance missing";
                return false;
            }

            status = "AuraMono BattlePassSystem";
            return true;
        }

        private int DailyClaimsCountAuraMonoBattlePassSlotsCanGet(
            IntPtr battlePassSystem,
            string methodName,
            out string status)
        {
            status = methodName + " unavailable";
            if (battlePassSystem == IntPtr.Zero)
            {
                return 0;
            }

            if (!this.DailyClaimsTryAuraMonoInvokeObjectInstance(
                battlePassSystem,
                methodName,
                0,
                out IntPtr slotsObj,
                out string invokeStatus)
                || slotsObj == IntPtr.Zero)
            {
                status = invokeStatus;
                return 0;
            }

            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            List<uint> pins = this.dailyClaimsAuraMonoPinBuffer;
            items.Clear();
            pins.Clear();
            uint slotsPin = AuraMonoPinNew(slotsObj);
            int canGetCount = 0;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, items, pins))
                {
                    status = methodName + " list empty";
                    return 0;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr slotObj = items[i];
                    if (slotObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    // TryGetMonoInt32Member boxes -> allocates -> SGen may relocate the slots; the
                    // pins above are what keep slotObj valid for the whole loop.
                    if (this.TryGetMonoInt32Member(slotObj, "state", out int state) && state == DailyClaimsBattlePassSlotCanGet)
                    {
                        canGetCount++;
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(slotsPin);
            }

            status = methodName + " ok slots=" + items.Count;
            return canGetCount;
        }

        private int DailyClaimsTryGetBattlePassCycleNeedPointAuraMono(int periodId)
        {
            if (periodId <= 0
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            // Same global-namespace trap as DailyClaimsForEachTableRow — use the image-based resolver.
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out _);
            if (tableDataClass == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr getPeriodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetBattlePassPeriod", 1);
            if (getPeriodMethod == IntPtr.Zero)
            {
                getPeriodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetBattlePassPeriod", 2);
            }

            if (getPeriodMethod == IntPtr.Zero)
            {
                return 0;
            }

            unsafe
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr periodObj;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&periodId);
                periodObj = auraMonoRuntimeInvoke(getPeriodMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if ((exc != IntPtr.Zero || periodObj == IntPtr.Zero) && getPeriodMethod != IntPtr.Zero)
                {
                    byte needException = 0;
                    IntPtr* argsWithFlag = stackalloc IntPtr[2];
                    argsWithFlag[0] = (IntPtr)(&periodId);
                    argsWithFlag[1] = (IntPtr)(&needException);
                    exc = IntPtr.Zero;
                    periodObj = auraMonoRuntimeInvoke(getPeriodMethod, IntPtr.Zero, (IntPtr)argsWithFlag, ref exc);
                }

                if (exc != IntPtr.Zero || periodObj == IntPtr.Zero)
                {
                    return 0;
                }

                // TableBattlePassPeriod stores this as `private byte _CycleRewardNeedPoint` behind a
                // `public int CycleRewardNeedPoint` property. Reading it as a FIELD under either
                // capitalisation finds nothing (the field carries a leading underscore) and reading
                // the underscored field would unbox one byte as four — so the property getter is the
                // only correct path. Before 2026-08-10 this silently returned 0, which pinned the BP
                // Loop status line at "pendingCycles=0 claimable=False" regardless of real progress;
                // the claim button itself was unaffected, it sends the command either way.
                if (this.DailyClaimsTryGetMonoPropertyInt(periodObj, "get_CycleRewardNeedPoint", out int cycleNeed)
                    && cycleNeed > 0)
                {
                    return cycleNeed;
                }

                return 0;
            }
        }

        private bool DailyClaimsTryAuraMonoInvokeBoolInstance(IntPtr instance, string methodName, out bool value)
        {
            value = false;
            if (!this.DailyClaimsTryAuraMonoInvokeObjectInstance(instance, methodName, 0, out IntPtr boxedResult, out _)
                || boxedResult == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxedResult, out value);
        }

        private bool DailyClaimsTryAuraMonoInvokeIntInstance(IntPtr instance, string methodName, out int value)
        {
            value = 0;
            if (!this.DailyClaimsTryAuraMonoInvokeObjectInstance(instance, methodName, 0, out IntPtr boxedResult, out _)
                || boxedResult == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoInt32(boxedResult, out value);
        }

        private unsafe bool DailyClaimsTryAuraMonoInvokeObjectInstance(
            IntPtr instance,
            string methodName,
            int argCount,
            out IntPtr resultObj,
            out string status)
        {
            resultObj = IntPtr.Zero;
            status = methodName + " unavailable";
            if (instance == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr instanceClass = auraMonoObjectGetClass(instance);
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(instanceClass, methodName, argCount);
            if (method == IntPtr.Zero)
            {
                status = methodName + " AuraMono method missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            resultObj = auraMonoRuntimeInvoke(method, instance, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = methodName + " AuraMono invoke failed";
                resultObj = IntPtr.Zero;
                return false;
            }

            status = methodName + " ok";
            return true;
        }

        private int ClaimTownGuideRewards(out string detail)
        {
            detail = string.Empty;
            if (!this.TryEnsureDailyClaimsTownGuideService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                detail = "ITownGuidesService unavailable: " + serviceStatus;
                return 0;
            }

            List<DailyClaimsTownGuideChapterSnapshot> chapters = this.dailyClaimsTownGuideChapterBuffer;
            chapters.Clear();
            if (!this.DailyClaimsTryGetTownGuideChapters(binding, chapters, out string listStatus))
            {
                detail = "GetAllChapterInfo failed: " + listStatus;
                return 0;
            }

            List<string> lines = new List<string>();
            int sent = 0;

            for (int i = 0; i < chapters.Count; i++)
            {
                DailyClaimsTownGuideChapterSnapshot chapter = chapters[i];
                if (chapter.ChapterId <= 0)
                {
                    continue;
                }

                if (string.Equals(chapter.ChapterState, "Reward", StringComparison.Ordinal))
                {
                    if (this.TryClaimTownGuideChapterReward(chapter.ChapterId, out string claimStatus))
                    {
                        sent++;
                        lines.Add("chapter reward chapterId=" + chapter.ChapterId + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED chapter reward chapterId=" + chapter.ChapterId + " (" + claimStatus + ")");
                    }
                }

                if (chapter.Nodes == null)
                {
                    continue;
                }

                for (int n = 0; n < chapter.Nodes.Count; n++)
                {
                    DailyClaimsTownGuideNodeSnapshot node = chapter.Nodes[n];
                    if (!string.Equals(node.State, "Reward", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (this.TryClaimTownGuideNodeReward(node.NodeId, out string claimStatus))
                    {
                        sent++;
                        lines.Add("node reward nodeId=" + node.NodeId + " (" + claimStatus + ")");
                    }
                    else
                    {
                        lines.Add("FAILED node reward nodeId=" + node.NodeId + " (" + claimStatus + ")");
                    }
                }
            }

            if (lines.Count == 0)
            {
                lines.Add("no town guide Reward states found across " + chapters.Count + " chapters (source=" + binding.Source + ")");
            }

            detail = string.Join("\n", lines.ToArray());
            return sent;
        }

        private bool TryReceiveActivityReward(int activityId, int nodeIndex, out string status)
        {
            object[] args = { activityId, nodeIndex, 0 };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.OperationActivity.OperationActivityProtocolMananger",
                "OperationActivityProtocolMananger",
                "ReceiveReward",
                args,
                out status);
        }

        private bool TryClaimTownGuideNodeReward(int nodeId, out string status)
        {
            object[] args = { nodeId };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.TownGuides.TownGuidesProtocolManager",
                "TownGuidesProtocolManager",
                "GetNodeReward",
                args,
                out status);
        }

        private bool TryClaimTownGuideChapterReward(int chapterId, out string status)
        {
            object[] args = { chapterId };
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.TownGuides.TownGuidesProtocolManager",
                "TownGuidesProtocolManager",
                "GetChapterReward",
                args,
                out status);
        }

        private unsafe bool TryInvokeDailyClaimsProtocolAuraMono(
            string fullTypeName,
            string shortTypeName,
            string methodName,
            object[] args,
            out string status)
        {
            status = shortTypeName + "." + methodName + " AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName(fullTypeName);
            if (protocolClass == IntPtr.Zero)
            {
                int lastDot = fullTypeName.LastIndexOf('.');
                string namespaceName = lastDot > 0 ? fullTypeName.Substring(0, lastDot) : string.Empty;
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(namespaceName, shortTypeName);
            }

            if (protocolClass == IntPtr.Zero)
            {
                status = shortTypeName + " AuraMono class missing";
                return false;
            }

            int paramCount = args?.Length ?? 0;
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(protocolClass, methodName, paramCount);
            if (method == IntPtr.Zero)
            {
                status = shortTypeName + "." + methodName + " AuraMono method missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            if (paramCount == 0)
            {
                auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            }
            else
            {
                int* argValues = stackalloc int[paramCount];
                for (int i = 0; i < paramCount; i++)
                {
                    argValues[i] = Convert.ToInt32(args[i]);
                }

                IntPtr* invokeArgs = stackalloc IntPtr[paramCount];
                for (int i = 0; i < paramCount; i++)
                {
                    invokeArgs[i] = (IntPtr)(argValues + i);
                }

                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                status = shortTypeName + "." + methodName + " AuraMono invoke failed";
                return false;
            }

            status = shortTypeName + "." + methodName + " invoked (AuraMono)";
            this.DailyClaimsLog(status + " args=" + this.FormatDailyClaimsArgs(args));
            return true;
        }

        private void EnsureDailyClaimsReflectionReady()
        {
            // Interop load clears reflection miss caches on success (HomelandFarmFeature).
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureAuraMonoApiReady();
        }

        private bool TryEnsureDailyClaimsActivityService(out DailyClaimsServiceBinding binding, out string status)
        {
            if (TryTakeCachedDailyClaimsService(ref this.dailyClaimsActivityServiceCache, this.dailyClaimsActivityServiceSource, out binding, out status))
            {
                return true;
            }

            return this.TryResolveDailyClaimsService(
                new[]
                {
                    "XDTDataAndProtocol.ProtocolService.OperationActivity.IOperationActivityCenterService",
                    "ClientSystem.OperationActivityCenter.OperationActivityCenterClientService"
                },
                new[] { "OperationActivityCenter", "IOperationActivityCenterService" },
                out binding,
                out status);
        }

        private bool TryEnsureDailyClaimsTownGuideService(out DailyClaimsServiceBinding binding, out string status)
        {
            if (TryTakeCachedDailyClaimsService(ref this.dailyClaimsTownGuideServiceCache, this.dailyClaimsTownGuideServiceSource, out binding, out status))
            {
                return true;
            }

            return this.TryResolveDailyClaimsService(
                new[]
                {
                    "XDTDataAndProtocol.ProtocolService.TownGuides.ITownGuidesService",
                    "ClientSystem.TownGuides.TownGuidesClientService"
                },
                new[] { "TownGuides", "ITownGuidesService" },
                out binding,
                out status);
        }

        private bool TryEnsureDailyClaimsSeaCycleService(out DailyClaimsServiceBinding binding, out string status)
        {
            if (TryTakeCachedDailyClaimsService(ref this.dailyClaimsSeaCycleServiceCache, this.dailyClaimsSeaCycleServiceSource, out binding, out status))
            {
                return true;
            }

            return this.TryResolveDailyClaimsService(
                new[]
                {
                    "XDTDataAndProtocol.ProtocolService.SeaCycle.ISeaCycleService",
                    "EcsSystem.ClientSystem.SeaCycle.SeaCycleClientService"
                },
                new[] { "SeaCycle", "ISeaCycleService" },
                out binding,
                out status);
        }

        // ==========================================================================================
        // Whalefall Canyon exploration level (SeaCycle)
        //
        // Submitting the daily requests credits exploration exp and, at milestones, an upgrade
        // ticket — so the level-up only becomes available AFTER the claims land. SeaCycleSystem
        // .CanUpgrade is mirrored exactly: TryGetLevelInfo → TableSeaCycleLevel(level) → exp >=
        // needExp AND ticket >= needTicket, with needExp <= 0 meaning "max level, nothing further".
        // ==========================================================================================

        // Number of consecutive level-ups one sweep will attempt. Each upgrade consumes exp and a
        // ticket, so the chain is naturally short; the cap only stops a runaway if the server state
        // never changes.
        private const int DailyClaimsMaxSeaCycleUpgrades = 5;

        // Seconds to let the server credit exp / apply an upgrade before the state is re-read.
        private const float DailyClaimsSeaCycleSettleSeconds = 1.2f;

        // ISeaCycleService.TryGetLevelInfo(out int level, out int exp, out int upgradeTicket) — three
        // out slots, each exactly 4 bytes, which is the shape mono_runtime_invoke expects for an
        // `out int`. (The out-slot hazard is value types WIDER than a pointer; an int fits exactly.)
        private unsafe bool DailyClaimsTryGetSeaCycleLevelInfo(
            out int level,
            out int exp,
            out int upgradeTicket,
            out string status)
        {
            level = 0;
            exp = 0;
            upgradeTicket = 0;

            if (!this.TryEnsureDailyClaimsSeaCycleService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                status = "ISeaCycleService unavailable: " + serviceStatus;
                return false;
            }

            if (binding.AuraMono == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                status = "ISeaCycleService has no AuraMono handle";
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(
                auraMonoObjectGetClass(binding.AuraMono), "TryGetLevelInfo", 3);
            if (method == IntPtr.Zero)
            {
                status = "TryGetLevelInfo method missing";
                return false;
            }

            int levelSlot = 0;
            int expSlot = 0;
            int ticketSlot = 0;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&levelSlot);
            args[1] = (IntPtr)(&expSlot);
            args[2] = (IntPtr)(&ticketSlot);

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, binding.AuraMono, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "TryGetLevelInfo invoke failed";
                return false;
            }

            if (boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool ok) && !ok)
            {
                status = "TryGetLevelInfo returned false (no SeaCycle data)";
                return false;
            }

            level = levelSlot;
            exp = expSlot;
            upgradeTicket = ticketSlot;
            status = "ok";
            return true;
        }

        // TableSeaCycleLevel.needExp / needTicket are int PROPERTIES over a private short / sbyte —
        // field reads would be wrong by several bytes, so both go through their getters.
        private unsafe bool DailyClaimsTryGetSeaCycleLevelNeeds(
            int level,
            out int needExp,
            out int needTicket,
            out string status)
        {
            needExp = 0;
            needTicket = 0;
            status = "GetSeaCycleLevel unavailable";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero)
            {
                status = "TableData class missing: " + classStatus;
                return false;
            }

            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetSeaCycleLevel", 2);
            bool twoArg = getMethod != IntPtr.Zero;
            if (getMethod == IntPtr.Zero)
            {
                getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetSeaCycleLevel", 1);
            }

            if (getMethod == IntPtr.Zero)
            {
                status = "GetSeaCycleLevel method missing";
                return false;
            }

            int id = level;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (twoArg)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                status = "GetSeaCycleLevel(" + level + ") returned null (max level?)";
                return false;
            }

            uint rowPin = AuraMonoPinNew(row);
            try
            {
                if (!this.DailyClaimsTryGetMonoPropertyInt(row, "get_needExp", out needExp))
                {
                    status = "needExp unreadable";
                    return false;
                }

                this.DailyClaimsTryGetMonoPropertyInt(row, "get_needTicket", out needTicket);
                status = "ok";
                return true;
            }
            finally
            {
                AuraMonoPinFree(rowPin);
            }
        }

        // SeaCycleSystem.CanUpgrade, mirrored.
        private bool DailyClaimsCanUpgradeSeaCycle(out string detail)
        {
            if (!this.DailyClaimsTryGetSeaCycleLevelInfo(out int level, out int exp, out int ticket, out string infoStatus))
            {
                detail = "level info: " + infoStatus;
                return false;
            }

            if (!this.DailyClaimsTryGetSeaCycleLevelNeeds(level, out int needExp, out int needTicket, out string needStatus))
            {
                detail = "lv=" + level + " exp=" + exp + " ticket=" + ticket + "; " + needStatus;
                return false;
            }

            detail = "lv=" + level + " exp=" + exp + "/" + needExp + " ticket=" + ticket + "/" + needTicket;
            if (needExp <= 0)
            {
                detail += " (max level)";
                return false;
            }

            if (exp < needExp)
            {
                detail += " (exp short)";
                return false;
            }

            if (ticket < needTicket)
            {
                detail += " (ticket short)";
                return false;
            }

            detail += " (READY)";
            return true;
        }

        private bool TryUpgradeDailyClaimsSeaCycle(out string status)
        {
            return this.TryInvokeDailyClaimsProtocolAuraMono(
                "XDTDataAndProtocol.ProtocolService.SeaCycle.SeaCycleProtocolManager",
                "SeaCycleProtocolManager",
                "RequestUpgrade",
                null,
                out status);
        }

        // ISeaCycleService.TryGetDailyTasks(out IReadOnlyList<SeaCycleDailyTaskInfo>) — the out is a
        // REFERENCE type, which is the only shape an AuraMono out-slot may carry (a struct out wider
        // than a pointer would smash the stack). Elements are boxed SeaCycleDailyTaskInfo structs;
        // only their TaskId is read, and it is scalarized here so the submit loop can yield.
        private unsafe bool DailyClaimsTryGetSeaCycleDailyTaskIds(List<int> taskIds, out string status)
        {
            taskIds.Clear();
            if (!this.TryEnsureDailyClaimsSeaCycleService(out DailyClaimsServiceBinding binding, out string serviceStatus))
            {
                status = "ISeaCycleService unavailable: " + serviceStatus;
                return false;
            }

            if (binding.AuraMono == IntPtr.Zero)
            {
                status = "ISeaCycleService resolved without an AuraMono handle";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono unavailable";
                return false;
            }

            IntPtr serviceClass = auraMonoObjectGetClass(binding.AuraMono);
            IntPtr method2 = this.FindAuraMonoMethodOnHierarchy(serviceClass, "TryGetDailyTasks", 1);
            if (method2 == IntPtr.Zero)
            {
                status = "AuraMono TryGetDailyTasks missing";
                return false;
            }

            IntPtr* taskListSlot = stackalloc IntPtr[1];
            taskListSlot[0] = IntPtr.Zero;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = (IntPtr)taskListSlot;

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method2, binding.AuraMono, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "AuraMono TryGetDailyTasks invoke failed";
                return false;
            }

            IntPtr listObj = taskListSlot[0];
            if (listObj == IntPtr.Zero)
            {
                status = "AuraMono TryGetDailyTasks returned null";
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                // false also means "empty" on this build — the service hands back a shared empty
                // list when the component is absent, which is a legitimate "nothing assigned".
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                {
                    status = "AuraMono ok count=0 (no tasks assigned)";
                    return true;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryGetMonoIntMember(items[i], "TaskId", out int taskId) && taskId > 0)
                    {
                        taskIds.Add(taskId);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = "AuraMono ok count=" + taskIds.Count;
            return true;
        }

        private bool TryResolveDailyClaimsService(
            string[] ecsTypeCandidates,
            string[] managerHints,
            out DailyClaimsServiceBinding binding,
            out string status)
        {
            binding = default;
            status = "service unavailable";
            this.EnsureDailyClaimsReflectionReady();

            // Decompiled client: services are injected via EcsInjectSystem and resolved with
            // EcsService.TryGet<T> — not Managers._serviceDic.
            if (this.TryDailyClaimsResolveServiceViaAuraMonoEcs(ecsTypeCandidates, managerHints, out IntPtr auraService, out string auraEcsStatus))
            {
                binding = new DailyClaimsServiceBinding
                {
                    AuraMono = auraService,
                    Source = auraEcsStatus
                };
                this.CacheDailyClaimsServiceBinding(ecsTypeCandidates, managerHints, binding);
                status = binding.Source;
                return true;
            }

            status = "auraEcs=" + auraEcsStatus;
            this.DailyClaimsLogResolveProbeOnce(ecsTypeCandidates, managerHints, status);
            return false;
        }

        // Reads one cached service back out. Returns false when the entry is empty, when pinning was
        // unavailable at Set time (the cache deliberately stores nothing rather than an unpinned raw
        // pointer), or when the world epoch moved on — all three mean "re-resolve", never "reuse".
        // The pin the cache holds also keeps the object put for the whole synchronous call the
        // returned binding is used in, which is what makes the member reads downstream safe.
        private static bool TryTakeCachedDailyClaimsService(
            ref AuraMonoObjectCache cache,
            string source,
            out DailyClaimsServiceBinding binding,
            out string status)
        {
            if (cache.TryGet(out IntPtr serviceObj) && serviceObj != IntPtr.Zero)
            {
                binding = new DailyClaimsServiceBinding
                {
                    AuraMono = serviceObj,
                    Source = source
                };
                status = source;
                return true;
            }

            binding = default;
            status = "service unavailable";
            return false;
        }

        private void CacheDailyClaimsServiceBinding(string[] ecsTypeCandidates, string[] managerHints, DailyClaimsServiceBinding binding)
        {
            for (int i = 0; i < managerHints.Length; i++)
            {
                if (managerHints[i].IndexOf("OperationActivity", StringComparison.OrdinalIgnoreCase) >= 0
                    || managerHints[i].IndexOf("ActivityCenter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsActivityServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsActivityServiceSource = binding.Source;
                    return;
                }

                if (managerHints[i].IndexOf("TownGuide", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsTownGuideServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsTownGuideServiceSource = binding.Source;
                    return;
                }

                if (managerHints[i].IndexOf("SeaCycle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsSeaCycleServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsSeaCycleServiceSource = binding.Source;
                    return;
                }

                if (managerHints[i].IndexOf("Pictorial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsPictorialServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsPictorialServiceSource = binding.Source;
                    return;
                }
            }

            for (int i = 0; i < ecsTypeCandidates.Length; i++)
            {
                if (ecsTypeCandidates[i].IndexOf("OperationActivity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsActivityServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsActivityServiceSource = binding.Source;
                    return;
                }

                if (ecsTypeCandidates[i].IndexOf("TownGuides", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsTownGuideServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsTownGuideServiceSource = binding.Source;
                    return;
                }

                if (ecsTypeCandidates[i].IndexOf("SeaCycle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsSeaCycleServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsSeaCycleServiceSource = binding.Source;
                    return;
                }

                if (ecsTypeCandidates[i].IndexOf("Pictorial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsPictorialServiceCache.Set(binding.AuraMono);
                    this.dailyClaimsPictorialServiceSource = binding.Source;
                    return;
                }
            }
        }

        private bool TryDailyClaimsResolveServiceViaAuraMonoEcs(
            string[] serviceTypeCandidates,
            string[] managerHints,
            out IntPtr service,
            out string status)
        {
            service = IntPtr.Zero;
            status = "AuraMono EcsService.TryGet unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono API unavailable";
                return false;
            }

            List<IntPtr> serviceClasses = this.DailyClaimsCollectAuraMonoServiceClasses(serviceTypeCandidates, managerHints);
            for (int i = 0; i < serviceClasses.Count; i++)
            {
                IntPtr serviceClass = serviceClasses[i];
                if (serviceClass == IntPtr.Zero)
                {
                    continue;
                }

                for (int logError = 0; logError < 2; logError++)
                {
                    if (this.TryDailyClaimsAuraMonoEcsTryGet(serviceClass, logError == 0, out IntPtr serviceObj, out string tryGetStatus)
                        && serviceObj != IntPtr.Zero)
                    {
                        service = serviceObj;
                        status = "AuraMono EcsService.TryGet: " + this.GetAuraMonoClassDisplayName(
                            auraMonoObjectGetClass != null ? auraMonoObjectGetClass(serviceObj) : IntPtr.Zero);
                        return true;
                    }

                    if (i == 0 && logError == 0)
                    {
                        status = tryGetStatus;
                    }
                }
            }

            if (serviceClasses.Count == 0)
            {
                status = "AuraMono service type classes missing";
            }

            return false;
        }

        private List<IntPtr> DailyClaimsCollectAuraMonoServiceClasses(string[] serviceTypeCandidates, string[] managerHints)
        {
            List<IntPtr> serviceClasses = new List<IntPtr>(serviceTypeCandidates.Length + 2);
            HashSet<IntPtr> seen = new HashSet<IntPtr>();

            void AddClass(IntPtr classPtr)
            {
                if (classPtr != IntPtr.Zero && seen.Add(classPtr))
                {
                    serviceClasses.Add(classPtr);
                }
            }

            for (int i = 0; i < serviceTypeCandidates.Length; i++)
            {
                AddClass(this.FindAuraMonoClassByFullName(serviceTypeCandidates[i]));
            }

            if (this.DailyClaimsLooksLikeActivityHints(managerHints))
            {
                AddClass(this.FindAuraMonoClassByFullName(
                    "ClientSystem.OperationActivityCenter.OperationActivityCenterClientService"));
                AddClass(this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.OperationActivity.IOperationActivityCenterService"));
            }
            else if (this.DailyClaimsLooksLikeTownGuideHints(managerHints))
            {
                AddClass(this.FindAuraMonoClassByFullName("ClientSystem.TownGuides.TownGuidesClientService"));
                AddClass(this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.TownGuides.ITownGuidesService"));
            }

            return serviceClasses;
        }

        private bool EnsureDailyClaimsAuraMonoEcsTryGetOpenMethod()
        {
            if (this.dailyClaimsAuraEcsTryGetOpenMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady())
            {
                return false;
            }

            if (this.dailyClaimsAuraEcsServiceClass == IntPtr.Zero)
            {
                this.dailyClaimsAuraEcsServiceClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.EcsService");
                if (this.dailyClaimsAuraEcsServiceClass == IntPtr.Zero)
                {
                    this.dailyClaimsAuraEcsServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService",
                        "EcsService");
                }
            }

            if (this.dailyClaimsAuraEcsServiceClass == IntPtr.Zero)
            {
                return false;
            }

            this.dailyClaimsAuraEcsTryGetOpenMethod = this.FindAuraMonoMethodOnHierarchy(
                this.dailyClaimsAuraEcsServiceClass,
                "TryGet",
                2);
            return this.dailyClaimsAuraEcsTryGetOpenMethod != IntPtr.Zero;
        }

        private unsafe bool TryDailyClaimsInflateAuraMonoEcsTryGetMethod(IntPtr serviceClass, out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (serviceClass == IntPtr.Zero
                || !this.EnsureDailyClaimsAuraMonoEcsTryGetOpenMethod()
                || auraMonoClassInflateGenericMethod == null
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null)
            {
                return false;
            }

            if (this.dailyClaimsAuraInflatedTryGetByServiceClass.TryGetValue(serviceClass, out inflatedMethod)
                && inflatedMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr serviceType = auraMonoClassGetType(serviceClass);
            if (serviceType == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = serviceType;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return false;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            inflatedMethod = auraMonoClassInflateGenericMethod(this.dailyClaimsAuraEcsTryGetOpenMethod, ref context);
            if (inflatedMethod == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoCompileMethod != null)
            {
                try
                {
                    auraMonoCompileMethod(inflatedMethod);
                }
                catch
                {
                }
            }

            // Inflated TryGet<T> must still take exactly 2 parameters (resolved via
            // FindAuraMonoMethodOnHierarchy(..., "TryGet", 2) above); a mismatched method_inst
            // would AV the process on invoke instead of throwing.
            if (!AuraMonoMethodParamCountIs(inflatedMethod, 2))
            {
                return false;
            }

            this.dailyClaimsAuraInflatedTryGetByServiceClass[serviceClass] = inflatedMethod;
            return true;
        }

        private unsafe bool TryDailyClaimsAuraMonoEcsTryGet(
            IntPtr serviceClass,
            bool logError,
            out IntPtr serviceObj,
            out string status)
        {
            serviceObj = IntPtr.Zero;
            status = "AuraMono EcsService.TryGet unavailable";
            if (serviceClass == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryDailyClaimsInflateAuraMonoEcsTryGetMethod(serviceClass, out IntPtr inflatedMethod))
            {
                status = "AuraMono EcsService.TryGet inflate failed";
                return false;
            }

            IntPtr* serviceSlot = stackalloc IntPtr[1];
            serviceSlot[0] = IntPtr.Zero;
            int logErrorValue = logError ? 1 : 0;
            IntPtr* invokeArgs = stackalloc IntPtr[2];
            invokeArgs[0] = (IntPtr)serviceSlot;
            invokeArgs[1] = (IntPtr)(&logErrorValue);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(inflatedMethod, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "AuraMono EcsService.TryGet invoke exception";
                return false;
            }

            serviceObj = serviceSlot[0];
            if (serviceObj == IntPtr.Zero)
            {
                status = "AuraMono EcsService.TryGet miss for " + this.GetAuraMonoClassDisplayName(serviceClass);
                return false;
            }

            status = "AuraMono EcsService.TryGet ok";
            return true;
        }

        private bool DailyClaimsLooksLikeActivityHints(string[] hints)
        {
            for (int i = 0; i < hints.Length; i++)
            {
                if (hints[i].IndexOf("OperationActivity", StringComparison.OrdinalIgnoreCase) >= 0
                    || hints[i].IndexOf("ActivityCenter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool DailyClaimsLooksLikeTownGuideHints(string[] hints)
        {
            for (int i = 0; i < hints.Length; i++)
            {
                if (hints[i].IndexOf("TownGuide", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string FormatDailyClaimsArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", args.Select(a => a == null ? "null" : a.ToString()).ToArray()) + "]";
        }

        private void DailyClaimsLogResolveProbeOnce(string[] ecsTypeCandidates, string[] managerHints, string failureStatus)
        {
            if (this.dailyClaimsResolveProbeLogged)
            {
                return;
            }

            this.dailyClaimsResolveProbeLogged = true;
            if (this.dailyClaimsAuraEcsServiceClass == IntPtr.Zero)
            {
                this.EnsureDailyClaimsAuraMonoEcsTryGetOpenMethod();
            }

            string auraEcsClass = this.dailyClaimsAuraEcsServiceClass != IntPtr.Zero
                ? this.GetAuraMonoClassDisplayName(this.dailyClaimsAuraEcsServiceClass)
                : "null";
            string auraTryGet = this.dailyClaimsAuraEcsTryGetOpenMethod != IntPtr.Zero ? "ok" : "missing";

            this.DailyClaimsLog(
                "resolve probe failure=" + failureStatus
                + " auraEcsClass=" + auraEcsClass
                + " auraTryGet=" + auraTryGet
                + " hints=[" + string.Join(",", managerHints ?? Array.Empty<string>()) + "]"
                + " candidates=[" + string.Join(",", ecsTypeCandidates ?? Array.Empty<string>()) + "]"
                + " auraApi=" + this.EnsureAuraMonoApiReady());
        }

        private bool DailyClaimsTryGetAliveActivityIds(DailyClaimsServiceBinding binding, List<int> ids, out string status)
        {
            ids.Clear();
            status = "GetAliveActivityIds unavailable";
            if (!binding.IsValid)
            {
                return false;
            }

            return this.DailyClaimsTryAuraMonoInvokeIntList(binding.AuraMono, "GetAliveActivityIds", 0, null, ids, out status);
        }

        private bool DailyClaimsTryGetActivityNodeStateNames(
            DailyClaimsServiceBinding binding,
            int activityId,
            List<string> stateNames,
            out string status)
        {
            stateNames.Clear();
            status = "GetActivityNodeStateById unavailable";
            if (!binding.IsValid)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr serviceClass = auraMonoObjectGetClass(binding.AuraMono);
            IntPtr stateMethodPtr = this.FindAuraMonoMethodOnHierarchy(serviceClass, "GetActivityNodeStateById", 1);
            if (stateMethodPtr == IntPtr.Zero)
            {
                status = "AuraMono GetActivityNodeStateById missing";
                return false;
            }

            unsafe
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&activityId);
                IntPtr arrayObj = auraMonoRuntimeInvoke(stateMethodPtr, binding.AuraMono, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || arrayObj == IntPtr.Zero)
                {
                    status = "AuraMono GetActivityNodeStateById invoke failed";
                    return false;
                }

                if (!this.DailyClaimsTryReadAuraMonoEnumIntArray(arrayObj, stateNames))
                {
                    status = "AuraMono node state array unreadable";
                    return false;
                }
            }

            status = "AuraMono ok count=" + stateNames.Count;
            return true;
        }

        private bool DailyClaimsTryGetTownGuideChapters(
            DailyClaimsServiceBinding binding,
            List<DailyClaimsTownGuideChapterSnapshot> chapters,
            out string status)
        {
            chapters.Clear();
            status = "GetAllChapterInfo unavailable";
            if (!binding.IsValid)
            {
                return false;
            }

            if (this.DailyClaimsTryGetTownGuideChaptersAuraMonoGetAll(binding, chapters, out status))
            {
                return true;
            }

            List<int> chapterIds = this.dailyClaimsActivityIdBuffer;
            chapterIds.Clear();
            if (!this.DailyClaimsTryGetTownGuideChapterIdsAuraMono(chapterIds, out string chapterIdStatus))
            {
                return false;
            }

            IntPtr serviceClass = auraMonoObjectGetClass(binding.AuraMono);
            IntPtr getChapterInfoMethod = this.FindAuraMonoMethodOnHierarchy(serviceClass, "GetChapterInfo", 1);
            if (getChapterInfoMethod == IntPtr.Zero)
            {
                status = "AuraMono GetChapterInfo missing";
                return false;
            }

            for (int i = 0; i < chapterIds.Count; i++)
            {
                int chapterId = chapterIds[i];
                IntPtr chapterObj;
                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&chapterId);
                    chapterObj = auraMonoRuntimeInvoke(getChapterInfoMethod, binding.AuraMono, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || chapterObj == IntPtr.Zero)
                    {
                        continue;
                    }
                }

                // Pin across the parse: it boxes on every member read, and each box is a mono-side
                // allocation that can trigger an SGen collection which relocates this very object.
                uint chapterPin = AuraMonoPinNew(chapterObj);
                try
                {
                    DailyClaimsTownGuideChapterSnapshot chapter = this.DailyClaimsParseTownGuideChapterAuraMono(chapterObj);
                    if (chapter.ChapterId > 0)
                    {
                        chapters.Add(chapter);
                    }
                }
                finally
                {
                    AuraMonoPinFree(chapterPin);
                }
            }

            status = "AuraMono GetChapterInfo fallback count=" + chapters.Count + " (" + chapterIdStatus + ")";
            return chapters.Count > 0;
        }

        private unsafe bool DailyClaimsTryGetTownGuideChaptersAuraMonoGetAll(
            DailyClaimsServiceBinding binding,
            List<DailyClaimsTownGuideChapterSnapshot> chapters,
            out string status)
        {
            chapters.Clear();
            status = "AuraMono GetAllChapterInfo unavailable";
            if (!binding.IsValid
                || binding.AuraMono == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.DailyClaimsTryCreateAuraMonoGuidesChapterInfoList(out IntPtr listObj, out string listStatus))
            {
                status = listStatus;
                return false;
            }

            IntPtr serviceClass = auraMonoObjectGetClass(binding.AuraMono);
            IntPtr getAllMethod = this.FindAuraMonoMethodOnHierarchy(serviceClass, "GetAllChapterInfo", 1);
            if (getAllMethod == IntPtr.Zero)
            {
                status = "AuraMono GetAllChapterInfo missing";
                return false;
            }

            // listObj is a fresh managed List<GuidesChapterInfo> we just allocated; GetAllChapterInfo
            // fills it (growing its backing array = more allocation), so it has to be pinned across
            // the invoke and the walk or SGen can move it out from under both.
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = listObj;

            uint listPin = AuraMonoPinNew(listObj);
            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            List<uint> pins = this.dailyClaimsAuraMonoPinBuffer;
            items.Clear();
            pins.Clear();
            try
            {
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(getAllMethod, binding.AuraMono, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono GetAllChapterInfo invoke failed";
                    return false;
                }

                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins) || items.Count == 0)
                {
                    status = "AuraMono GetAllChapterInfo returned empty list";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    DailyClaimsTownGuideChapterSnapshot chapter = this.DailyClaimsParseTownGuideChapterAuraMono(items[i]);
                    if (chapter.ChapterId > 0)
                    {
                        chapters.Add(chapter);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(listPin);
            }

            status = "AuraMono GetAllChapterInfo ok count=" + chapters.Count;
            return chapters.Count > 0;
        }

        private unsafe bool DailyClaimsTryCreateAuraMonoGuidesChapterInfoList(out IntPtr listObj, out string status)
        {
            listObj = IntPtr.Zero;
            status = "AuraMono List<GuidesChapterInfo> unavailable";
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            if (this.dailyClaimsGuidesChapterInfoListClass != IntPtr.Zero && auraMonoObjectNew != null)
            {
                listObj = auraMonoObjectNew(this.auraMonoRootDomain, this.dailyClaimsGuidesChapterInfoListClass);
                if (listObj != IntPtr.Zero && auraMonoRuntimeObjectInit != null)
                {
                    auraMonoRuntimeObjectInit(listObj);
                    status = "ok";
                    return true;
                }

                listObj = IntPtr.Zero;
            }

            string[] listTypeCandidates = new[]
            {
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.TownGuides.GuidesChapterInfo, EcsClient]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.TownGuides.GuidesChapterInfo, Client]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < listTypeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, listTypeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "AuraMono List<GuidesChapterInfo> create failed";
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(listObj) : IntPtr.Zero;
            if (listClass != IntPtr.Zero)
            {
                this.dailyClaimsGuidesChapterInfoListClass = listClass;
            }

            status = "ok";
            return true;
        }

        private bool DailyClaimsTryGetTownGuideChapterIdsAuraMono(List<int> chapterIds, out string status)
        {
            chapterIds.Clear();
            status = "chapter ids unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr tableDataClass = this.FindAuraMonoClassByFullName("EcsClient.TableData");
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
            }

            if (tableDataClass != IntPtr.Zero
                && this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableGuidesChapterss", out IntPtr chaptersTableObj)
                && chaptersTableObj != IntPtr.Zero)
            {
                List<IntPtr> entries = this.dailyClaimsAuraMonoItemBuffer;
                List<uint> entryPins = this.dailyClaimsAuraMonoPinBuffer;
                entries.Clear();
                entryPins.Clear();
                uint tablePin = AuraMonoPinNew(chaptersTableObj);
                try
                {
                    if (this.TryEnumerateAuraMonoCollectionItems(chaptersTableObj, entries, entryPins))
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            IntPtr entryObj = entries[i];
                            if (entryObj == IntPtr.Zero)
                            {
                                continue;
                            }

                            if (this.TryGetMonoInt32Member(entryObj, "id", out int chapterId) && chapterId > 0)
                            {
                                chapterIds.Add(chapterId);
                                continue;
                            }

                            if (this.TryGetMonoInt32Member(entryObj, "Key", out chapterId) && chapterId > 0)
                            {
                                chapterIds.Add(chapterId);
                                continue;
                            }

                            if (this.TryGetMonoInt32Member(entryObj, "m_value", out chapterId) && chapterId > 0)
                            {
                                chapterIds.Add(chapterId);
                            }
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(entryPins);
                    AuraMonoPinFree(tablePin);
                }
            }

            if (chapterIds.Count > 0)
            {
                status = "TableGuidesChapterss count=" + chapterIds.Count;
                return true;
            }

            for (int chapterId = 1; chapterId <= 128; chapterId++)
            {
                chapterIds.Add(chapterId);
            }

            status = "fallback chapterId range 1..128";
            return true;
        }

        private DailyClaimsTownGuideChapterSnapshot DailyClaimsParseTownGuideChapterAuraMono(IntPtr chapterObj)
        {
            DailyClaimsTownGuideChapterSnapshot chapter = new DailyClaimsTownGuideChapterSnapshot
            {
                ChapterId = 0,
                ChapterState = "?",
                Nodes = new List<DailyClaimsTownGuideNodeSnapshot>(8)
            };

            if (chapterObj == IntPtr.Zero)
            {
                return chapter;
            }

            if (!this.TryGetMonoInt32Member(chapterObj, "ChapterId", out int chapterId))
            {
                this.TryGetMonoInt32Member(chapterObj, "chapterId", out chapterId);
            }

            chapter.ChapterId = chapterId;
            chapter.ChapterState = this.DailyClaimsTryGetAuraMonoEnumName(chapterObj, "State");

            if (this.TryGetMonoObjectMember(chapterObj, "AllNodes", out IntPtr nodesObj) && nodesObj != IntPtr.Zero)
            {
                // Deliberately NOT dailyClaimsAuraMonoItemBuffer: the caller is mid-walk over that
                // list (see the buffer declarations). Nodes get their own buffer + pin list.
                List<IntPtr> nodeItems = this.dailyClaimsAuraMonoNodeBuffer;
                List<uint> nodePins = this.dailyClaimsAuraMonoNodePinBuffer;
                nodeItems.Clear();
                nodePins.Clear();
                uint nodesPin = AuraMonoPinNew(nodesObj);
                try
                {
                    if (this.TryEnumerateAuraMonoCollectionItems(nodesObj, nodeItems, nodePins))
                    {
                        for (int i = 0; i < nodeItems.Count; i++)
                        {
                            IntPtr nodeObj = nodeItems[i];
                            if (nodeObj == IntPtr.Zero)
                            {
                                continue;
                            }

                            int nodeId = 0;
                            if (!this.TryGetMonoInt32Member(nodeObj, "NodeId", out nodeId))
                            {
                                this.TryGetMonoInt32Member(nodeObj, "nodeId", out nodeId);
                            }

                            chapter.Nodes.Add(new DailyClaimsTownGuideNodeSnapshot
                            {
                                NodeId = nodeId,
                                State = this.DailyClaimsTryGetAuraMonoEnumName(nodeObj, "State")
                            });
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(nodePins);
                    AuraMonoPinFree(nodesPin);
                }
            }

            return chapter;
        }

        private string DailyClaimsTryGetAuraMonoEnumName(IntPtr obj, string memberName)
        {
            if (obj == IntPtr.Zero)
            {
                return "?";
            }

            if (this.TryGetMonoInt32Member(obj, memberName, out int enumValue))
            {
                return this.DailyClaimsGuidesStateName(enumValue);
            }

            if (this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) && boxed != IntPtr.Zero)
            {
                IntPtr boxedClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(boxed) : IntPtr.Zero;
                string className = boxedClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(boxedClass) : string.Empty;
                if (!string.IsNullOrEmpty(className))
                {
                    int dot = className.LastIndexOf('.');
                    return dot >= 0 ? className.Substring(dot + 1) : className;
                }
            }

            return "?";
        }

        private string DailyClaimsGuidesStateName(int enumValue)
        {
            switch (enumValue)
            {
                case 0: return "Lock";
                case 1: return "Unlock";
                case 2: return "Reward";
                case 3: return "Finished";
                default: return "Unknown(" + enumValue + ")";
            }
        }

        private unsafe bool DailyClaimsTryAuraMonoInvokeIntList(
            IntPtr serviceObj,
            string methodName,
            int argCount,
            int? singleArg,
            List<int> output,
            out string status)
        {
            output.Clear();
            status = methodName + " AuraMono unavailable";
            if (serviceObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr serviceClass = auraMonoObjectGetClass(serviceObj);
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(serviceClass, methodName, argCount);
            if (method == IntPtr.Zero)
            {
                status = methodName + " AuraMono method missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr listObj;
            if (argCount == 0)
            {
                listObj = auraMonoRuntimeInvoke(method, serviceObj, IntPtr.Zero, ref exc);
            }
            else
            {
                int argValue = singleArg ?? 0;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&argValue);
                listObj = auraMonoRuntimeInvoke(method, serviceObj, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                status = methodName + " AuraMono invoke failed";
                return false;
            }

            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            List<uint> pins = this.dailyClaimsAuraMonoPinBuffer;
            items.Clear();
            pins.Clear();
            uint listPin = AuraMonoPinNew(listObj);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                {
                    status = methodName + " AuraMono list empty";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (this.TryUnboxAuraUInt32(items[i], out uint value))
                    {
                        output.Add((int)value);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(listPin);
            }

            status = methodName + " AuraMono ok count=" + output.Count;
            return true;
        }

        private bool DailyClaimsTryReadAuraMonoEnumIntArray(IntPtr arrayObj, List<string> stateNames)
        {
            stateNames.Clear();
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null || !this.IsAuraMonoArrayObject(arrayObj))
            {
                return false;
            }

            try
            {
                int arrayCount = (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), 256UL);
                if (arrayCount == 0)
                {
                    // GetActivityNodeStateById returns Array.Empty when OperationActivityNodeStateComponent is absent.
                    return true;
                }

                IntPtr arrayBase = auraMonoArrayAddrWithSize(arrayObj, 4, UIntPtr.Zero);
                if (arrayBase == IntPtr.Zero)
                {
                    return false;
                }

                for (int i = 0; i < arrayCount; i++)
                {
                    int enumValue = Marshal.ReadInt32(arrayBase, i * 4);
                    stateNames.Add(this.DailyClaimsActivityNodeStateName(enumValue));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private string DailyClaimsActivityNodeStateName(int enumValue)
        {
            switch (enumValue)
            {
                case 0: return "Lock";
                case 1: return "Unlock";
                case 2: return "WaitClaim";
                case 3: return "Finished";
                default: return "Unknown(" + enumValue + ")";
            }
        }


        private void DailyClaimsLog(string message)
        {
            if (!DailyClaimsLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[DailyClaims] " + message);
        }
    }
}
