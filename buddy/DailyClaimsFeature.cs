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
        private const bool DailyClaimsLogsEnabled = MasterLogDailyClaims;
        private const float DailyClaimsActionDelaySeconds = 0.65f;

        private object dailyClaimsCoroutine = null;
        private string dailyClaimsLastStatus = string.Empty;

        private Type dailyClaimsEcsServiceType = null;
        private MethodInfo dailyClaimsEcsTryGetGeneric = null;
        private readonly Dictionary<string, MethodInfo> dailyClaimsEcsTryGetByService = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        private Type dailyClaimsOperationActivityProtocolType = null;
        private MethodInfo dailyClaimsReceiveRewardMethod = null;
        private Type dailyClaimsMailProtocolType = null;
        private MethodInfo dailyClaimsMailRequestAllMethod = null;
        private Type dailyClaimsBattlePassProtocolType = null;
        private MethodInfo dailyClaimsBattlePassGetAllMethod = null;
        private MethodInfo dailyClaimsBattlePassGetLoopMethod = null;
        private Type dailyClaimsTownGuidesProtocolType = null;
        private MethodInfo dailyClaimsTownGuideGetNodeMethod = null;
        private MethodInfo dailyClaimsTownGuideGetChapterMethod = null;

        private DailyClaimsServiceBinding dailyClaimsActivityServiceBinding = default;
        private DailyClaimsServiceBinding dailyClaimsTownGuideServiceBinding = default;

        private readonly List<int> dailyClaimsActivityIdBuffer = new List<int>(64);
        private readonly List<string> dailyClaimsNodeStateBuffer = new List<string>(32);
        private readonly List<DailyClaimsTownGuideChapterSnapshot> dailyClaimsTownGuideChapterBuffer = new List<DailyClaimsTownGuideChapterSnapshot>(32);
        private readonly List<IntPtr> dailyClaimsAuraMonoItemBuffer = new List<IntPtr>(64);
        private bool dailyClaimsResolveProbeLogged = false;

        private IntPtr dailyClaimsAuraEcsServiceClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraEcsTryGetOpenMethod = IntPtr.Zero;
        private readonly Dictionary<IntPtr, IntPtr> dailyClaimsAuraInflatedTryGetByServiceClass = new Dictionary<IntPtr, IntPtr>();

        private IntPtr dailyClaimsGuidesChapterInfoListClass = IntPtr.Zero;
        private IntPtr dailyClaimsAuraBattlePassSystemClass = IntPtr.Zero;

        private const int DailyClaimsBattlePassSlotCanGet = 1;

        private struct DailyClaimsServiceBinding
        {
            public object Managed;
            public IntPtr AuraMono;
            public string Source;

            public bool IsValid => this.Managed != null || this.AuraMono != IntPtr.Zero;
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

        private float DrawDailyClaimsControls(float startY)
        {
            const float left = 40f;
            const float width = 520f;
            const float btnW = 248f;
            const float btnH = 28f;
            const float rowH = 34f;

            float y = startY + 8f;
            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = textColor;

            Rect sectionRect = new Rect(left, y, width, 372f);
            GUI.Box(sectionRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(sectionRect, 1f);
            GUI.Label(new Rect(sectionRect.x + 16f, sectionRect.y + 10f, 240f, 20f), this.L("DAILY CLAIMS"), labelStyle);

            float btnY = sectionRect.y + 36f;
            bool busy = this.dailyQuestSubmitCoroutine != null
                || this.birdPhotoSubmitCoroutine != null
                || this.dailyClaimsCoroutine != null
                || this.wildAnimalGiftCoroutine != null;
            GUI.enabled = !busy;

            if (this.DrawDailyClaimsButton(sectionRect.x + 16f, btnY, width - 32f, btnH, this.L("Log All State")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsLogAllStateRoutine());
            }

            btnY += rowH;
            if (this.DrawDailyClaimsButton(sectionRect.x + 16f, btnY, btnW, btnH, this.L("Claim Sign-In Rewards")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimSignInRoutine());
            }

            if (this.DrawDailyClaimsButton(sectionRect.x + 16f + btnW + 8f, btnY, btnW, btnH, this.L("Claim Mail All")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimMailRoutine());
            }

            btnY += rowH;
            if (this.DrawDailyClaimsButton(sectionRect.x + 16f, btnY, btnW, btnH, this.L("Claim Mini BP All")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimMiniBpAllRoutine());
            }

            if (this.DrawDailyClaimsButton(sectionRect.x + 16f + btnW + 8f, btnY, btnW, btnH, this.L("Claim BP Loop")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimBpLoopRoutine());
            }

            btnY += rowH;
            if (this.DrawDailyClaimsButton(sectionRect.x + 16f, btnY, btnW, btnH, this.L("Claim Town Guide")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimTownGuideRoutine());
            }

            if (this.DrawDailyClaimsButton(sectionRect.x + 16f + btnW + 8f, btnY, btnW, btnH, this.L("Claim Wild Gifts")))
            {
                this.StartWildAnimalClaimAllGifts(silent: false);
                this.dailyClaimsLastStatus = "Wild gift claim started.";
            }

            btnY += rowH;
            if (this.DrawDailyClaimsButton(sectionRect.x + 16f, btnY, width - 32f, btnH + 4f, this.L("Claim All Daily")))
            {
                this.StartDailyClaimsAction(this.DailyClaimsClaimAllRoutine());
            }

            GUI.enabled = true;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.82f);
            y = sectionRect.yMax + 6f;
            GUI.Label(new Rect(left, y, width, 40f), this.dailyClaimsLastStatus ?? string.Empty, statusStyle);
            return y + 44f;
        }

        private bool DrawDailyClaimsButton(float x, float y, float w, float h, string label)
        {
            return GUI.Button(new Rect(x, y, w, h), label, this.themePrimaryButtonStyle ?? GUI.skin.button);
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

            this.DailyClaimsLog("=== Daily Claims state end ===");
            this.dailyClaimsLastStatus = "State: signIn wait=" + waitClaimNodes
                + " town ch=" + chapterRewards
                + " mail=" + (mailRewardable ? mailRewardableCount : 0)
                + " miniBp=" + (miniBpFreeCanGet + miniBpPaidCanGet)
                + " bpLoop=" + bpLoopCycles;
            yield break;
        }

        private IEnumerator DailyClaimsClaimSignInRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming sign-in rewards...";
            int claimed = this.ClaimSignInRewards(out string detail);
            this.dailyClaimsLastStatus = "Sign-in claim done: sent=" + claimed;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(detail);
            yield return new WaitForSecondsRealtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimMailRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming mail attachments...";
            bool ok = this.TryClaimMailAll(out string status);
            this.dailyClaimsLastStatus = ok ? "Mail claim sent." : ("Mail claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return new WaitForSecondsRealtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimMiniBpAllRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming mini battle pass rewards...";
            bool ok = this.TryClaimMiniBpAll(out string status);
            this.dailyClaimsLastStatus = ok ? "Mini BP claim sent." : ("Mini BP claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return new WaitForSecondsRealtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimBpLoopRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming BP loop rewards...";
            bool ok = this.TryClaimBpLoop(out string status);
            this.dailyClaimsLastStatus = ok ? "BP loop claim sent." : ("BP loop claim failed: " + status);
            this.DailyClaimsLog(this.dailyClaimsLastStatus + " detail=" + status);
            yield return new WaitForSecondsRealtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimTownGuideRoutine()
        {
            this.dailyClaimsLastStatus = "Claiming town guide rewards...";
            int claimed = this.ClaimTownGuideRewards(out string detail);
            this.dailyClaimsLastStatus = "Town guide claim done: sent=" + claimed;
            this.DailyClaimsLog(this.dailyClaimsLastStatus);
            this.DailyClaimsLog(detail);
            yield return new WaitForSecondsRealtime(DailyClaimsActionDelaySeconds);
        }

        private IEnumerator DailyClaimsClaimAllRoutine()
        {
            this.DailyClaimsLog("=== Claim All Daily start ===");

            yield return this.DailyClaimsClaimSignInRoutine();
            yield return this.DailyClaimsClaimMailRoutine();
            yield return this.DailyClaimsClaimMiniBpAllRoutine();
            yield return this.DailyClaimsClaimBpLoopRoutine();
            yield return this.DailyClaimsClaimTownGuideRoutine();

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

            if (lines.Count == 0)
            {
                lines.Add("no WaitClaim nodes found across " + activityIds.Count + " activities (source=" + binding.Source + ")");
            }

            detail = string.Join("\n", lines.ToArray());
            return sent;
        }

        private bool TryClaimMailAll(out string status)
        {
            Type commandType = this.ResolveDailyClaimsManagedType(
                "TakeAllAttachmentCommand",
                "XDT.Scene.Shared.Modules.Mail.TakeAllAttachmentCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Mail.TakeAllAttachmentCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, _ => true, out status))
            {
                status = "SendCommand TakeAllAttachmentCommand ok";
                return true;
            }

            return this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsMailProtocolType,
                ref this.dailyClaimsMailRequestAllMethod,
                "XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager",
                "MailProtocolManager",
                "RequestAllRewards",
                null,
                out status);
        }

        private bool TryClaimMiniBpAll(out string status)
        {
            Type commandType = this.ResolveDailyClaimsManagedType(
                "BattlePassGetRewardNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.BattlePass.BattlePassGetRewardNetworkCommand",
                "XDT.Scene.Shared.Modules.BattlePass.BattlePassGetRewardNetworkCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, cmd =>
            {
                this.TrySetObjectMember(cmd, "flag", 0);
                this.TrySetObjectMember(cmd, "rewardId", 0);
                return true;
            }, out status))
            {
                status = "SendCommand BattlePassGetRewardNetworkCommand flag=0 ok";
                return true;
            }

            return this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsBattlePassProtocolType,
                ref this.dailyClaimsBattlePassGetAllMethod,
                "XDTDataAndProtocol.ProtocolService.BattlePass.BattlePassProtocolManager",
                "BattlePassProtocolManager",
                "GetAllRewards",
                null,
                out status);
        }

        private bool TryClaimBpLoop(out string status)
        {
            Type commandType = this.ResolveDailyClaimsManagedType(
                "BattlePassGetCycleRewardNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.BattlePass.BattlePassGetCycleRewardNetworkCommand",
                "XDT.Scene.Shared.Modules.BattlePass.BattlePassGetCycleRewardNetworkCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, _ => true, out status))
            {
                status = "SendCommand BattlePassGetCycleRewardNetworkCommand ok";
                return true;
            }

            return this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsBattlePassProtocolType,
                ref this.dailyClaimsBattlePassGetLoopMethod,
                "XDTDataAndProtocol.ProtocolService.BattlePass.BattlePassProtocolManager",
                "BattlePassProtocolManager",
                "GetLoopRewards",
                null,
                out status);
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

            try
            {
                Type mailProtocolType = this.ResolveDailyClaimsManagedType(
                    "MailProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager");
                if (mailProtocolType != null)
                {
                    MethodInfo isAnyMethod = mailProtocolType.GetMethod(
                        "IsAnyRewardable",
                        BindingFlags.Public | BindingFlags.Static);
                    if (isAnyMethod != null && isAnyMethod.Invoke(null, null) is bool managedAny)
                    {
                        anyRewardable = managedAny;
                        source = "MailProtocolManager";
                    }
                }
            }
            catch
            {
            }

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

            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            items.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(mailsObj, items) || items.Count == 0)
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
            items.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, items))
            {
                status = methodName + " list empty";
                return 0;
            }

            int canGetCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                IntPtr slotObj = items[i];
                if (slotObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetMonoInt32Member(slotObj, "state", out int state) && state == DailyClaimsBattlePassSlotCanGet)
                {
                    canGetCount++;
                }
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

            IntPtr tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
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

                if (this.TryGetMonoInt32Member(periodObj, "CycleRewardNeedPoint", out int cycleNeed) && cycleNeed > 0)
                {
                    return cycleNeed;
                }

                this.TryGetMonoInt32Member(periodObj, "cycleRewardNeedPoint", out cycleNeed);
                return cycleNeed;
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
            Type commandType = this.ResolveDailyClaimsManagedType(
                "ClaimActivityNodeRewardWithSelectNetworkCommand",
                "XDT.Scene.Shared.Modules.OperationActivityCenter.ClaimActivityNodeRewardWithSelectNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.OperationActivityCenter.ClaimActivityNodeRewardWithSelectNetworkCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, cmd =>
            {
                this.TrySetObjectMember(cmd, "ActivityId", activityId);
                this.TrySetObjectMember(cmd, "NodeIndex", nodeIndex);
                this.TrySetObjectMember(cmd, "SelectIndex", 0);
                return true;
            }, out status))
            {
                status = "SendCommand ClaimActivityNodeReward ok";
                return true;
            }

            object[] args = { activityId, nodeIndex, 0 };
            if (this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsOperationActivityProtocolType,
                ref this.dailyClaimsReceiveRewardMethod,
                "XDTDataAndProtocol.ProtocolService.OperationActivity.OperationActivityProtocolMananger",
                "OperationActivityProtocolMananger",
                "ReceiveReward",
                args,
                out status))
            {
                status = "ReceiveReward ok";
                return true;
            }

            return false;
        }

        private bool TryClaimTownGuideNodeReward(int nodeId, out string status)
        {
            Type commandType = this.ResolveDailyClaimsManagedType(
                "GetNodeRewardCommand",
                "XDT.Scene.Shared.Modules.TownGuides.GetNodeRewardCommand",
                "EcsClient.XDT.Scene.Shared.Modules.TownGuides.GetNodeRewardCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, cmd =>
            {
                this.TrySetObjectMember(cmd, "NodeId", nodeId);
                return true;
            }, out status))
            {
                status = "SendCommand GetNodeRewardCommand ok";
                return true;
            }

            object[] args = { nodeId };
            return this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsTownGuidesProtocolType,
                ref this.dailyClaimsTownGuideGetNodeMethod,
                "XDTDataAndProtocol.ProtocolService.TownGuides.TownGuidesProtocolManager",
                "TownGuidesProtocolManager",
                "GetNodeReward",
                args,
                out status);
        }

        private bool TryClaimTownGuideChapterReward(int chapterId, out string status)
        {
            Type commandType = this.ResolveDailyClaimsManagedType(
                "GetChapterRewardCommand",
                "XDT.Scene.Shared.Modules.TownGuides.GetChapterRewardCommand",
                "EcsClient.XDT.Scene.Shared.Modules.TownGuides.GetChapterRewardCommand");
            if (commandType != null && this.TryHomelandFarmSendCommand(commandType, cmd =>
            {
                this.TrySetObjectMember(cmd, "ChapterId", chapterId);
                return true;
            }, out status))
            {
                status = "SendCommand GetChapterRewardCommand ok";
                return true;
            }

            object[] args = { chapterId };
            return this.TryInvokeDailyClaimsProtocol(
                ref this.dailyClaimsTownGuidesProtocolType,
                ref this.dailyClaimsTownGuideGetChapterMethod,
                "XDTDataAndProtocol.ProtocolService.TownGuides.TownGuidesProtocolManager",
                "TownGuidesProtocolManager",
                "GetChapterReward",
                args,
                out status);
        }

        private bool TryInvokeDailyClaimsProtocol(
            ref Type protocolType,
            ref MethodInfo method,
            string fullTypeName,
            string shortTypeName,
            string methodName,
            object[] args,
            out string status)
        {
            status = methodName + " unavailable";
            this.EnsureDailyClaimsReflectionReady();
            try
            {
                if (protocolType == null)
                {
                    protocolType = this.ResolveDailyClaimsManagedType(shortTypeName, fullTypeName)
                        ?? this.FindLoadedType(fullTypeName, shortTypeName);
                }

                if (protocolType != null)
                {
                    if (method == null)
                    {
                        method = protocolType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));
                    }

                    if (method != null)
                    {
                        method.Invoke(null, args);
                        status = shortTypeName + "." + methodName + " invoked (managed)";
                        this.DailyClaimsLog(status + " args=" + this.FormatDailyClaimsArgs(args));
                        return true;
                    }
                }

                if (this.TryInvokeDailyClaimsProtocolAuraMono(fullTypeName, shortTypeName, methodName, args, out status))
                {
                    return true;
                }

                status = shortTypeName + "." + methodName + " missing (managed+AuraMono)";
                return false;
            }
            catch (Exception ex)
            {
                status = shortTypeName + "." + methodName + " exception: " + (ex.InnerException ?? ex).Message;
                this.DailyClaimsLog(status);
                if (this.TryInvokeDailyClaimsProtocolAuraMono(fullTypeName, shortTypeName, methodName, args, out string auraStatus))
                {
                    status = auraStatus;
                    return true;
                }

                return false;
            }
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
            if (this.dailyClaimsActivityServiceBinding.IsValid)
            {
                binding = this.dailyClaimsActivityServiceBinding;
                status = binding.Source;
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
            if (this.dailyClaimsTownGuideServiceBinding.IsValid)
            {
                binding = this.dailyClaimsTownGuideServiceBinding;
                status = binding.Source;
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
                    Managed = null,
                    AuraMono = auraService,
                    Source = auraEcsStatus
                };
                this.CacheDailyClaimsServiceBinding(ecsTypeCandidates, managerHints, binding);
                status = binding.Source;
                return true;
            }

            if (this.TryDailyClaimsResolveServiceViaEcs(ecsTypeCandidates, managerHints, out object managedService, out string ecsStatus))
            {
                binding = new DailyClaimsServiceBinding
                {
                    Managed = managedService,
                    AuraMono = IntPtr.Zero,
                    Source = "EcsService.TryGet: " + managedService.GetType().FullName
                };
                this.CacheDailyClaimsServiceBinding(ecsTypeCandidates, managerHints, binding);
                status = binding.Source;
                return true;
            }

            status = "auraEcs=" + auraEcsStatus + "; managedEcs=" + ecsStatus;
            this.DailyClaimsLogResolveProbeOnce(ecsTypeCandidates, managerHints, status);
            return false;
        }

        private void CacheDailyClaimsServiceBinding(string[] ecsTypeCandidates, string[] managerHints, DailyClaimsServiceBinding binding)
        {
            for (int i = 0; i < managerHints.Length; i++)
            {
                if (managerHints[i].IndexOf("OperationActivity", StringComparison.OrdinalIgnoreCase) >= 0
                    || managerHints[i].IndexOf("ActivityCenter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsActivityServiceBinding = binding;
                    return;
                }

                if (managerHints[i].IndexOf("TownGuide", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsTownGuideServiceBinding = binding;
                    return;
                }
            }

            for (int i = 0; i < ecsTypeCandidates.Length; i++)
            {
                if (ecsTypeCandidates[i].IndexOf("OperationActivity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsActivityServiceBinding = binding;
                    return;
                }

                if (ecsTypeCandidates[i].IndexOf("TownGuides", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.dailyClaimsTownGuideServiceBinding = binding;
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

        private bool TryDailyClaimsResolveServiceViaEcs(
            string[] serviceTypeCandidates,
            string[] managerHints,
            out object service,
            out string status)
        {
            service = null;
            status = "EcsService unavailable";
            if (!this.EnsureDailyClaimsEcsTryGet())
            {
                return false;
            }

            List<Type> serviceTypes = this.DailyClaimsCollectServiceTypes(serviceTypeCandidates, managerHints);
            for (int t = 0; t < serviceTypes.Count; t++)
            {
                Type serviceType = serviceTypes[t];
                if (serviceType == null)
                {
                    continue;
                }

                string cacheKey = serviceType.FullName ?? serviceType.Name;
                if (!this.dailyClaimsEcsTryGetByService.TryGetValue(cacheKey, out MethodInfo tryGetMethod))
                {
                    tryGetMethod = this.dailyClaimsEcsTryGetGeneric.MakeGenericMethod(serviceType);
                    this.dailyClaimsEcsTryGetByService[cacheKey] = tryGetMethod;
                }

                for (int logError = 0; logError < 2; logError++)
                {
                    object[] args = new object[] { null, logError == 0 };
                    object result = tryGetMethod.Invoke(null, args);
                    if (result is bool ok && ok && args[0] != null)
                    {
                        service = args[0];
                        status = service.GetType().FullName;
                        return true;
                    }
                }
            }

            status = "EcsService.TryGet miss";
            return false;
        }

        private List<Type> DailyClaimsCollectServiceTypes(string[] serviceTypeCandidates, string[] managerHints)
        {
            List<Type> serviceTypes = new List<Type>(serviceTypeCandidates.Length + 2);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            void AddType(Type type)
            {
                if (type == null)
                {
                    return;
                }

                string key = type.FullName ?? type.Name;
                if (seen.Add(key))
                {
                    serviceTypes.Add(type);
                }
            }

            for (int i = 0; i < serviceTypeCandidates.Length; i++)
            {
                string candidate = serviceTypeCandidates[i];
                AddType(this.ResolveDailyClaimsManagedType(candidate, candidate));
                AddType(this.FindLoadedType(candidate, candidate));
                AddType(this.FindLoadedTypeBySuffix(candidate));
            }

            if (this.DailyClaimsLooksLikeActivityHints(managerHints))
            {
                AddType(this.FindDailyClaimsServiceTypeByShape("GetAliveActivityIds", "GetActivityNodeStateById"));
            }
            else if (this.DailyClaimsLooksLikeTownGuideHints(managerHints))
            {
                AddType(this.FindDailyClaimsServiceTypeByShape("GetAllChapterInfo", "GetChapterInfo"));
            }

            return serviceTypes;
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

        private Type FindDailyClaimsServiceTypeByShape(params string[] requiredInstanceMethods)
        {
            if (requiredInstanceMethods == null || requiredInstanceMethods.Length == 0)
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal)
                    || assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
                    || assemblyName.StartsWith("Unity", StringComparison.Ordinal)
                    || assemblyName.StartsWith("Harmony", StringComparison.Ordinal)
                    || assemblyName.StartsWith("BepInEx", StringComparison.Ordinal))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null || !type.IsClass || type.IsAbstract)
                    {
                        continue;
                    }

                    bool matches = true;
                    for (int m = 0; m < requiredInstanceMethods.Length; m++)
                    {
                        if (type.GetMethod(requiredInstanceMethods[m], BindingFlags.Public | BindingFlags.Instance) == null)
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private void DailyClaimsLogResolveProbeOnce(string[] ecsTypeCandidates, string[] managerHints, string failureStatus)
        {
            if (this.dailyClaimsResolveProbeLogged)
            {
                return;
            }

            this.dailyClaimsResolveProbeLogged = true;
            Type ecsType = this.dailyClaimsEcsServiceType
                ?? this.FindLoadedEcsServiceType();
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
                + " interopLoaded=" + this.homelandFarmInteropAssembliesLoaded
                + " managedEcsType=" + (ecsType != null ? ecsType.FullName : "null")
                + " auraEcsClass=" + auraEcsClass
                + " auraTryGet=" + auraTryGet
                + " hints=[" + string.Join(",", managerHints ?? Array.Empty<string>()) + "]"
                + " candidates=[" + string.Join(",", ecsTypeCandidates ?? Array.Empty<string>()) + "]"
                + " auraApi=" + this.EnsureAuraMonoApiReady());
        }

        private bool TryDailyClaimsResolveServiceFromManagers(string[] typeHints, out object service, out string status)
        {
            service = null;
            status = "Managers._serviceDic unavailable";
            try
            {
                Type managersType = this.FindLoadedType("XDTGame.Framework.Managers", "Managers");
                if (managersType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = managersType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object managers = instanceProperty?.GetValue(null, null);
                object serviceDic = managers != null
                    ? this.TryGetManagedMemberValue(managers, "_serviceDic") ?? this.TryGetManagedMemberValue(managers, "serviceDic")
                    : null;
                if (!(serviceDic is IEnumerable enumerable))
                {
                    return false;
                }

                foreach (object entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    object serviceObj = this.TryGetManagedMemberValue(entry, "Value") ?? entry;
                    if (serviceObj == null)
                    {
                        continue;
                    }

                    string fullName = serviceObj.GetType().FullName ?? string.Empty;
                    for (int i = 0; i < typeHints.Length; i++)
                    {
                        if (fullName.IndexOf(typeHints[i], StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (!this.DailyClaimsManagedServiceMatchesShape(serviceObj, typeHints))
                        {
                            continue;
                        }

                        service = serviceObj;
                        status = "Managers._serviceDic: " + fullName;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                status = "Managers._serviceDic exception: " + ex.Message;
            }

            return false;
        }

        private bool TryDailyClaimsResolveServiceFromManagersAuraMono(string[] typeHints, out IntPtr serviceObj, out string status)
        {
            serviceObj = IntPtr.Zero;
            status = "Managers AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }

            if (managersClass == IntPtr.Zero)
            {
                status = "Managers class missing";
                return false;
            }

            if ((!this.TryGetAuraMonoStaticObjectField(managersClass, "_serviceDic", out IntPtr serviceDicObj) || serviceDicObj == IntPtr.Zero)
                && (!this.TryGetAuraMonoStaticObjectField(managersClass, "serviceDic", out serviceDicObj) || serviceDicObj == IntPtr.Zero))
            {
                status = "Managers._serviceDic field missing";
                return false;
            }

            List<IntPtr> entries = this.dailyClaimsAuraMonoItemBuffer;
            entries.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(serviceDicObj, entries) || entries.Count == 0)
            {
                status = "Managers._serviceDic empty";
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entryObj = entries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                if ((!this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr candidate) || candidate == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "value", out candidate) || candidate == IntPtr.Zero))
                {
                    candidate = entryObj;
                }

                IntPtr candidateClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(candidate) : IntPtr.Zero;
                string className = candidateClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(candidateClass) : string.Empty;
                for (int h = 0; h < typeHints.Length; h++)
                {
                    if (className.IndexOf(typeHints[h], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        serviceObj = candidate;
                        status = "Managers._serviceDic AuraMono: " + className;
                        return true;
                    }
                }
            }

            status = "Managers._serviceDic had no matching service";
            return false;
        }

        private bool EnsureDailyClaimsEcsTryGet()
        {
            if (this.dailyClaimsEcsTryGetGeneric != null && this.dailyClaimsEcsServiceType != null)
            {
                return true;
            }

            this.EnsureDailyClaimsReflectionReady();
            this.dailyClaimsEcsServiceType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.EcsService",
                    "Il2CppXDTDataAndProtocol.ProtocolService.EcsService",
                    "EcsService")
                ?? this.FindLoadedTypeBySuffix("ProtocolService.EcsService", "EcsService")
                ?? this.FindLoadedEcsServiceType()
                ?? this.ResolveDailyClaimsManagedType(
                    "EcsService",
                    "XDTDataAndProtocol.ProtocolService.EcsService",
                    "Il2CppXDTDataAndProtocol.ProtocolService.EcsService");
            if (this.dailyClaimsEcsServiceType == null)
            {
                return false;
            }

            this.dailyClaimsEcsTryGetGeneric = this.dailyClaimsEcsServiceType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            return this.dailyClaimsEcsTryGetGeneric != null;
        }

        private bool DailyClaimsTryGetAliveActivityIds(DailyClaimsServiceBinding binding, List<int> ids, out string status)
        {
            ids.Clear();
            status = "GetAliveActivityIds unavailable";
            if (!binding.IsValid)
            {
                return false;
            }

            if (binding.Managed != null)
            {
                try
                {
                    MethodInfo method = binding.Managed.GetType().GetMethod("GetAliveActivityIds", BindingFlags.Public | BindingFlags.Instance);
                    if (method == null)
                    {
                        status = "managed GetAliveActivityIds missing";
                        return false;
                    }

                    object result = method.Invoke(binding.Managed, null);
                    if (result is IEnumerable enumerable)
                    {
                        foreach (object item in enumerable)
                        {
                            if (item != null)
                            {
                                ids.Add(Convert.ToInt32(item));
                            }
                        }
                    }

                    status = "managed ok count=" + ids.Count;
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed GetAliveActivityIds exception: " + ex.Message;
                    return false;
                }
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

            if (binding.Managed != null)
            {
                try
                {
                    MethodInfo stateMethodInfo = binding.Managed.GetType().GetMethod("GetActivityNodeStateById", BindingFlags.Public | BindingFlags.Instance);
                    if (stateMethodInfo == null)
                    {
                        status = "managed GetActivityNodeStateById missing";
                        return false;
                    }

                    if (stateMethodInfo.Invoke(binding.Managed, new object[] { activityId }) is Array nodeStates)
                    {
                        for (int i = 0; i < nodeStates.Length; i++)
                        {
                            stateNames.Add(nodeStates.GetValue(i)?.ToString() ?? "?");
                        }
                    }

                    status = "managed ok count=" + stateNames.Count;
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed GetActivityNodeStateById exception: " + ex.Message;
                    return false;
                }
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

            if (binding.Managed != null)
            {
                try
                {
                    Type chapterInfoType = this.ResolveDailyClaimsManagedType(
                        "GuidesChapterInfo",
                        "XDT.Scene.Shared.Modules.TownGuides.GuidesChapterInfo",
                        "EcsClient.XDT.Scene.Shared.Modules.TownGuides.GuidesChapterInfo");
                    if (chapterInfoType == null)
                    {
                        status = "GuidesChapterInfo type missing";
                        return false;
                    }

                    Type listType = typeof(List<>).MakeGenericType(chapterInfoType);
                    object list = Activator.CreateInstance(listType);
                    MethodInfo method = binding.Managed.GetType().GetMethod("GetAllChapterInfo", BindingFlags.Public | BindingFlags.Instance);
                    if (method == null || list == null)
                    {
                        status = "managed GetAllChapterInfo missing";
                        return false;
                    }

                    method.Invoke(binding.Managed, new[] { list });
                    if (list is IEnumerable enumerable)
                    {
                        foreach (object chapterObj in enumerable)
                        {
                            if (chapterObj == null)
                            {
                                continue;
                            }

                            chapters.Add(this.DailyClaimsParseTownGuideChapter(chapterObj));
                        }
                    }

                    status = "managed ok count=" + chapters.Count;
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed GetAllChapterInfo exception: " + ex.Message;
                    return false;
                }
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
                unsafe
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&chapterId);
                    IntPtr chapterObj = auraMonoRuntimeInvoke(getChapterInfoMethod, binding.AuraMono, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || chapterObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    DailyClaimsTownGuideChapterSnapshot chapter = this.DailyClaimsParseTownGuideChapterAuraMono(chapterObj);
                    if (chapter.ChapterId > 0)
                    {
                        chapters.Add(chapter);
                    }
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

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = listObj;
            auraMonoRuntimeInvoke(getAllMethod, binding.AuraMono, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "AuraMono GetAllChapterInfo invoke failed";
                return false;
            }

            List<IntPtr> items = this.dailyClaimsAuraMonoItemBuffer;
            items.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items) || items.Count == 0)
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
                entries.Clear();
                if (this.TryEnumerateAuraMonoCollectionItems(chaptersTableObj, entries))
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
                List<IntPtr> nodeItems = this.dailyClaimsAuraMonoItemBuffer;
                nodeItems.Clear();
                if (this.TryEnumerateAuraMonoCollectionItems(nodesObj, nodeItems))
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

        private DailyClaimsTownGuideChapterSnapshot DailyClaimsParseTownGuideChapter(object chapterObj)
        {
            DailyClaimsTownGuideChapterSnapshot chapter = new DailyClaimsTownGuideChapterSnapshot
            {
                ChapterId = this.TryGetDailyClaimsIntMember(chapterObj, "ChapterId"),
                ChapterState = this.TryGetDailyClaimsEnumName(chapterObj, "State"),
                Nodes = new List<DailyClaimsTownGuideNodeSnapshot>(8)
            };

            if (this.TryGetObjectMember(chapterObj, "AllNodes", out object nodesObj) && nodesObj is IEnumerable nodes)
            {
                foreach (object node in nodes)
                {
                    if (node == null)
                    {
                        continue;
                    }

                    chapter.Nodes.Add(new DailyClaimsTownGuideNodeSnapshot
                    {
                        NodeId = this.TryGetDailyClaimsIntMember(node, "NodeId"),
                        State = this.TryGetDailyClaimsEnumName(node, "State")
                    });
                }
            }

            return chapter;
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
            items.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items))
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

        private Type ResolveDailyClaimsManagedType(string shortName, params string[] fullNames)
        {
            this.EnsureDailyClaimsReflectionReady();
            return this.ResolveHomelandFarmManagedType(shortName, fullNames);
        }

        private bool DailyClaimsManagedServiceMatchesShape(object serviceObj, string[] typeHints)
        {
            if (serviceObj == null)
            {
                return false;
            }

            Type serviceType = serviceObj.GetType();
            if (this.DailyClaimsLooksLikeActivityHints(typeHints))
            {
                return serviceType.GetMethod("GetAliveActivityIds", BindingFlags.Public | BindingFlags.Instance) != null
                    && serviceType.GetMethod("GetActivityNodeStateById", BindingFlags.Public | BindingFlags.Instance) != null;
            }

            if (this.DailyClaimsLooksLikeTownGuideHints(typeHints))
            {
                return serviceType.GetMethod("GetAllChapterInfo", BindingFlags.Public | BindingFlags.Instance) != null
                    && serviceType.GetMethod("GetChapterInfo", BindingFlags.Public | BindingFlags.Instance) != null;
            }

            return true;
        }

        private int TryGetDailyClaimsIntMember(object instance, string memberName)
        {
            if (instance == null)
            {
                return 0;
            }

            if (this.TryGetManagedInt32Member(instance, memberName, out int value))
            {
                return value;
            }

            if (this.TryGetObjectMember(instance, memberName, out object raw) && raw != null)
            {
                try
                {
                    return Convert.ToInt32(raw);
                }
                catch
                {
                }
            }

            return 0;
        }

        private string TryGetDailyClaimsEnumName(object instance, string memberName)
        {
            if (instance == null || !this.TryGetObjectMember(instance, memberName, out object raw) || raw == null)
            {
                return "?";
            }

            return raw.ToString();
        }

        private string FormatDailyClaimsArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", args.Select(a => a == null ? "null" : a.ToString()).ToArray()) + "]";
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
