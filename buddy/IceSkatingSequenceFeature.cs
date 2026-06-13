using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // SkateProtocolManager + *NetworkCommand live in embedded Mono only (see DrawUploadFeature.cs).
    // Primary path: AuraMono static invoke on SkateProtocolManager; SendCommand is fallback when interop loads.
    public partial class HeartopiaComplete
    {
        private const bool IceSkatingSequenceLogsEnabled = true;
        private const int IceSkatingChallengeSequencePerfectCount = 5;
        private const int IceSkatingChallengeEndScore = 1500;
        private const int IceSkatingPerfectDrillPerfectsPerRun = 10;
        private const int IceSkatingSequenceMaxRunCount = 999;
        private const int IceSkatingSequenceActionReportInterval = 5;
        private const float IceSkatingSequenceCountdownSeconds = 0.1f;
        private const float IceSkatingSequenceStepDelaySeconds = 0.1f;
        private const float IceSkatingSequencePhaseDelaySeconds = 0.1f;
        private const int IceSkatingSequenceDefaultBranchNormalId = 26;
        private const int IceSkatingSequenceUiLogLineLimit = 36;
        private const string IceSkatingSequenceProtocolFullName = "XDTDataAndProtocol.ProtocolService.Skate.SkateProtocolManager";
        private const string IceSkatingSequenceCommandNamespace = "Sazabi.Scene.Share.ClinetShared.Modules.Skate";

        private static readonly string[] IceSkatingSequenceAuraProtocolImages =
        {
            "XDTDataAndProtocol",
            "XDTDataAndProtocol.dll",
            "Client",
            "Client.dll",
            "EcsClient",
            "EcsClient.dll"
        };

        private object iceSkatingSequenceCoroutine;
        private string iceSkatingSequenceLastStatus = "Idle.";
        private readonly List<string> iceSkatingSequenceUiLog = new List<string>(IceSkatingSequenceUiLogLineLimit);
        private int iceSkatingSequenceBranchNormalId = IceSkatingSequenceDefaultBranchNormalId;
        private int iceSkatingSequenceRunCount = 1;
        private int iceSkatingPerfectDrillRunCount = 1;
        private string iceSkatingSequenceNormalsOverride = string.Empty;

        private bool iceSkatingSequenceResolverReady;
        private Type iceSkatingSequenceProtocolManagerType;
        private MethodInfo iceSkatingSequenceSendStartMethod;
        private MethodInfo iceSkatingSequenceSendExitMethod;
        private MethodInfo iceSkatingSequenceSendChallengeStartMethod;
        private MethodInfo iceSkatingSequenceSendChallengeEndMethod;
        private MethodInfo iceSkatingSequenceSendDoActionMethod;
        private MethodInfo iceSkatingSequenceSendPerfectMethod;
        private MethodInfo iceSkatingSequenceSendUltimateMethod;
        private IntPtr iceSkatingSequenceAuraProtocolClass;
        private IntPtr iceSkatingAuraSendStartMethod;
        private IntPtr iceSkatingAuraSendExitMethod;
        private IntPtr iceSkatingAuraSendChallengeStartMethod;
        private IntPtr iceSkatingAuraSendChallengeEndMethod;
        private IntPtr iceSkatingAuraSendDoActionMethod;
        private IntPtr iceSkatingAuraSendPerfectMethod;
        private IntPtr iceSkatingAuraSendUltimateMethod;
        private IntPtr iceSkatingAuraDictSetItemMethod;
        private IntPtr iceSkatingAuraDictAddMethod;
        private IntPtr iceSkatingAuraIntIntDictionaryClass;
        private IntPtr iceSkatingAuraEcsServiceClass;
        private IntPtr iceSkatingAuraEcsTryGetOpenMethod;
        private IntPtr iceSkatingAuraSkateServiceTryGetClass;
        private readonly Dictionary<IntPtr, IntPtr> iceSkatingAuraInflatedTryGetByServiceClass = new Dictionary<IntPtr, IntPtr>();

        private static readonly string[] IceSkatingAuraIntIntDictionaryTypeNames =
        {
            "System.Collections.Generic.Dictionary`2[System.Int32,System.Int32]",
            "System.Collections.Generic.Dictionary`2[[System.Int32, mscorlib],[System.Int32, mscorlib]]"
        };

        private Type iceSkatingSequenceCmdStartType;
        private Type iceSkatingSequenceCmdEndType;
        private Type iceSkatingSequenceCmdChallengeStartType;
        private Type iceSkatingSequenceCmdChallengeEndType;
        private Type iceSkatingSequenceCmdDoActionType;
        private Type iceSkatingSequenceCmdPerfectType;
        private Type iceSkatingSequenceCmdUltimateType;

        private Type iceSkatingSequenceSkateServiceType;
        private MethodInfo iceSkatingSequenceEcsTryGetGeneric;
        private MethodInfo iceSkatingSequenceGetChallengeIdMethod;

        private Type iceSkatingSequenceTableDataType;
        private MethodInfo iceSkatingSequenceGetSkateActionMethod;
        private MethodInfo iceSkatingSequenceGetSkateActionTypeMethod;
        private PropertyInfo iceSkatingSequenceSkateActionParentProperty;
        private PropertyInfo iceSkatingSequenceSkateActionActionTypeProperty;
        private PropertyInfo iceSkatingSequenceSkateActionTypeUltimateProperty;

        private float DrawIceSkatingExtrasTab(float startY)
        {
            const float left = 40f;
            float y = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, 400f, 24f), "Perfect Ice Skating", headerStyle);
            y += 30f;

            bool busy = this.iceSkatingSequenceCoroutine != null;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(left, y, 280f, 32f), "Challenge (5 perfect, 1500)", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartIceSkatingNetworkSequence();
            }

            this.iceSkatingSequenceRunCount = this.DrawIceSkatingRunCountField(
                new Rect(left + 290f, y + 4f, 100f, 22f),
                "Runs",
                this.iceSkatingSequenceRunCount);

            y += 36f;
            if (GUI.Button(new Rect(left, y, 280f, 32f), "Perfect Drill", GUI.skin.button))
            {
                this.StartIceSkatingPerfectDrillSequence();
            }

            this.iceSkatingPerfectDrillRunCount = this.DrawIceSkatingRunCountField(
                new Rect(left + 290f, y + 4f, 100f, 22f),
                "Runs",
                this.iceSkatingPerfectDrillRunCount);

            GUI.enabled = true;
            return y + 40f;
        }

        private int DrawIceSkatingRunCountField(Rect rect, string label, int value)
        {
            GUI.Label(new Rect(rect.x, rect.y, 36f, rect.height), label);
            string text = GUI.TextField(new Rect(rect.x + 40f, rect.y, rect.width - 40f, rect.height), value.ToString());
            if (int.TryParse(text, out int parsed))
            {
                return Mathf.Clamp(parsed, 1, IceSkatingSequenceMaxRunCount);
            }

            return Mathf.Clamp(value, 1, IceSkatingSequenceMaxRunCount);
        }

        private int IceSkatingSequenceClampRunCount(int runCount)
        {
            return Mathf.Clamp(runCount, 1, IceSkatingSequenceMaxRunCount);
        }

        private string BuildIceSkatingSequenceUiLogText()
        {
            if (this.iceSkatingSequenceUiLog.Count == 0)
            {
                return "Logs appear here and in loader log ([IceSkatingSeq]).";
            }

            StringBuilder sb = new StringBuilder(this.iceSkatingSequenceUiLog.Count * 48);
            for (int i = 0; i < this.iceSkatingSequenceUiLog.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(this.iceSkatingSequenceUiLog[i]);
            }

            return sb.ToString();
        }

        private void StartIceSkatingNetworkSequence()
        {
            if (this.iceSkatingSequenceCoroutine != null)
            {
                this.AddMenuNotification("Ice skating sequence already running", new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.iceSkatingSequenceUiLog.Clear();
            this.iceSkatingSequenceLastStatus = "Running...";
            this.IceSkatingSequenceLog("=== challenge sequence start runs=" + this.IceSkatingSequenceClampRunCount(this.iceSkatingSequenceRunCount) + " ===", force: true);
            this.iceSkatingSequenceCoroutine = ModCoroutines.Start(this.IceSkatingNetworkSequenceRoutine());
        }

        private void StartIceSkatingPerfectDrillSequence()
        {
            if (this.iceSkatingSequenceCoroutine != null)
            {
                this.AddMenuNotification("Ice skating sequence already running", new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.iceSkatingSequenceUiLog.Clear();
            this.iceSkatingSequenceLastStatus = "Running perfect drill...";
            int runCount = this.IceSkatingSequenceClampRunCount(this.iceSkatingPerfectDrillRunCount);
            this.IceSkatingSequenceLog(
                "=== perfect drill start runs=" + runCount
                + " perfectsPerRun=" + IceSkatingPerfectDrillPerfectsPerRun + " ===",
                force: true);
            this.iceSkatingSequenceCoroutine = ModCoroutines.Start(this.IceSkatingPerfectDrillRoutine());
        }

        private IEnumerator IceSkatingPerfectDrillRoutine()
        {
            yield return null;

            this.EnsureAuraMonoApiReady();
            this.AttachAuraMonoThread();

            if (!this.EnsureIceSkatingSequenceSendResolver(out string resolverStatus))
            {
                this.IceSkatingSequenceFail("resolver failed: " + resolverStatus);
                yield break;
            }

            int runCount = this.IceSkatingSequenceClampRunCount(this.iceSkatingPerfectDrillRunCount);
            if (!this.TryResolveIceSkatingNormalActionIds(
                    IceSkatingPerfectDrillPerfectsPerRun,
                    out int[] actionIds,
                    out string actionStatus))
            {
                this.IceSkatingSequenceFail("action resolve failed: " + actionStatus);
                yield break;
            }

            this.IceSkatingSequenceLog(
                "drill perfectsPerRun=" + IceSkatingPerfectDrillPerfectsPerRun
                + " actions=[" + string.Join(",", actionIds) + "] (" + actionStatus + ")");

            for (int run = 0; run < runCount; run++)
            {
                this.IceSkatingSequenceLog("=== drill run " + (run + 1) + "/" + runCount + " ===");

                if (!this.TrySendIceSkatingStart(out string startStatus))
                {
                    this.IceSkatingSequenceFail("SkateStart failed run " + (run + 1) + ": " + startStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog("SkateStartNetworkCommand sent ok (" + startStatus + ")");
                yield return new WaitForSecondsRealtime(IceSkatingSequencePhaseDelaySeconds);

                Dictionary<int, int> pendingBatch = new Dictionary<int, int>();
                int[] actionCountSinceFlush = { 0 };
                IEnumerator perfects = this.IceSkatingExecutePerfectCycles(
                    1,
                    IceSkatingPerfectDrillPerfectsPerRun,
                    actionIds,
                    pendingBatch,
                    actionCountSinceFlush);
                while (perfects.MoveNext())
                {
                    yield return perfects.Current;
                }

                if (pendingBatch.Count > 0)
                {
                    if (!this.TryFlushIceSkatingDoActionBatch(pendingBatch, out string finalFlushStatus))
                    {
                        this.IceSkatingSequenceFail("DoAction flush failed run " + (run + 1) + ": " + finalFlushStatus);
                        yield break;
                    }

                    this.IceSkatingSequenceLog("DoAction flush (" + finalFlushStatus + ")");
                }

                if (!this.TrySendIceSkatingEnd(out string exitStatus))
                {
                    this.IceSkatingSequenceFail("SkateEnd failed run " + (run + 1) + ": " + exitStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog("SkateEndNetworkCommand sent ok (" + exitStatus + ")");

                if (run + 1 < runCount)
                {
                    yield return new WaitForSecondsRealtime(IceSkatingSequencePhaseDelaySeconds);
                }
            }

            this.IceSkatingSequenceLog("=== perfect drill complete runs=" + runCount + " ===", force: true);
            this.iceSkatingSequenceLastStatus = "Perfect drill complete (" + runCount + " run(s)).";
            this.AddMenuNotification("Ice skating perfect drill x" + runCount, new Color(0.55f, 1f, 0.65f));
            this.iceSkatingSequenceCoroutine = null;
        }

        private IEnumerator IceSkatingExecutePerfectCycles(
            int cycleCount,
            int perfectsPerCycle,
            int[] actionIds,
            Dictionary<int, int> pendingBatch,
            int[] actionCountSinceFlushHolder)
        {
            if (actionIds == null || actionIds.Length == 0)
            {
                this.IceSkatingSequenceFail("action ids missing");
                yield break;
            }

            if (actionCountSinceFlushHolder == null || actionCountSinceFlushHolder.Length == 0)
            {
                actionCountSinceFlushHolder = new int[1];
            }

            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                this.IceSkatingSequenceLog("--- cycle " + (cycle + 1) + "/" + cycleCount + " ---");

                for (int perfect = 1; perfect <= perfectsPerCycle; perfect++)
                {
                    int actionId = actionIds[Math.Min(perfect - 1, actionIds.Length - 1)];
                    this.IceSkatingSequenceAccumulateAction(pendingBatch, actionId);
                    actionCountSinceFlushHolder[0]++;

                    if (!this.TrySendIceSkatingPerfect(perfect, out string perfectStatus))
                    {
                        this.IceSkatingSequenceFail(
                            "SkateReportPerfect failed at cycle " + (cycle + 1) + " perfect=" + perfect + ": " + perfectStatus);
                        yield break;
                    }

                    this.IceSkatingSequenceLog(
                        "perfect report Perfect=" + perfect + " actionId=" + actionId + " (" + perfectStatus + ")");

                    if (actionCountSinceFlushHolder[0] >= IceSkatingSequenceActionReportInterval)
                    {
                        if (!this.TryFlushIceSkatingDoActionBatch(pendingBatch, out string flushStatus))
                        {
                            this.IceSkatingSequenceFail("DoAction flush failed: " + flushStatus);
                            yield break;
                        }

                        this.IceSkatingSequenceLog("DoAction flush interval=" + IceSkatingSequenceActionReportInterval + " (" + flushStatus + ")");
                        actionCountSinceFlushHolder[0] = 0;
                    }

                    yield return new WaitForSecondsRealtime(IceSkatingSequenceStepDelaySeconds);
                }
            }
        }

        private IEnumerator IceSkatingNetworkSequenceRoutine()
        {
            yield return null;

            this.EnsureAuraMonoApiReady();
            this.AttachAuraMonoThread();

            if (!this.EnsureIceSkatingSequenceSendResolver(out string resolverStatus))
            {
                this.IceSkatingSequenceFail("resolver failed: " + resolverStatus);
                yield break;
            }

            int runCount = this.IceSkatingSequenceClampRunCount(this.iceSkatingSequenceRunCount);
            if (!this.TryResolveIceSkatingNormalActionIds(
                    IceSkatingChallengeSequencePerfectCount,
                    out int[] actionIds,
                    out string actionStatus))
            {
                this.IceSkatingSequenceFail("action resolve failed: " + actionStatus);
                yield break;
            }

            this.IceSkatingSequenceLog(
                "challenge actions=[" + string.Join(",", actionIds) + "] endScore=" + IceSkatingChallengeEndScore
                + " (" + actionStatus + ")");

            for (int run = 0; run < runCount; run++)
            {
                this.IceSkatingSequenceLog("=== challenge run " + (run + 1) + "/" + runCount + " ===");

                if (!this.TrySendIceSkatingStart(out string startStatus))
                {
                    this.IceSkatingSequenceFail("SkateStart failed run " + (run + 1) + ": " + startStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog("SkateStartNetworkCommand sent ok (" + startStatus + ")");
                yield return new WaitForSecondsRealtime(IceSkatingSequencePhaseDelaySeconds);

                if (!this.TrySendIceSkatingChallengeStart(isHelp: false, out string challengeStartStatus))
                {
                    this.IceSkatingSequenceFail("SkateChallengeStart failed run " + (run + 1) + ": " + challengeStartStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog("SkateChallengeStartNetworkCommand IsHelp=false ok (" + challengeStartStatus + ")");
                this.IceSkatingSequenceLog("waiting countdown " + IceSkatingSequenceCountdownSeconds.ToString("0.0") + "s");
                yield return new WaitForSecondsRealtime(IceSkatingSequenceCountdownSeconds);

                Dictionary<int, int> pendingBatch = new Dictionary<int, int>();
                int[] actionCountSinceFlush = { 0 };
                IEnumerator perfects = this.IceSkatingExecutePerfectCycles(
                    1,
                    IceSkatingChallengeSequencePerfectCount,
                    actionIds,
                    pendingBatch,
                    actionCountSinceFlush);
                while (perfects.MoveNext())
                {
                    yield return perfects.Current;
                }

                if (pendingBatch.Count > 0)
                {
                    if (!this.TryFlushIceSkatingDoActionBatch(pendingBatch, out string flushStatus))
                    {
                        this.IceSkatingSequenceFail("DoAction flush failed run " + (run + 1) + ": " + flushStatus);
                        yield break;
                    }

                    this.IceSkatingSequenceLog("DoAction flush (" + flushStatus + ")");
                }

                if (!this.TrySendIceSkatingChallengeEnd(IceSkatingChallengeEndScore, out string endStatus))
                {
                    this.IceSkatingSequenceFail("SkateChallengeEnd failed run " + (run + 1) + ": " + endStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog(
                    "SkateChallengeEndNetworkCommand Score=" + IceSkatingChallengeEndScore + " (" + endStatus + ")");
                yield return new WaitForSecondsRealtime(IceSkatingSequencePhaseDelaySeconds);

                if (!this.TrySendIceSkatingEnd(out string exitStatus))
                {
                    this.IceSkatingSequenceFail("SkateEnd failed run " + (run + 1) + ": " + exitStatus);
                    yield break;
                }

                this.IceSkatingSequenceLog("SkateEndNetworkCommand sent ok (" + exitStatus + ")");

                if (run + 1 < runCount)
                {
                    yield return new WaitForSecondsRealtime(IceSkatingSequencePhaseDelaySeconds);
                }
            }

            this.IceSkatingSequenceLog("=== challenge sequence complete runs=" + runCount + " ===", force: true);
            this.iceSkatingSequenceLastStatus = "Challenge sequence complete (" + runCount + " run(s)).";
            this.AddMenuNotification("Ice skating challenge sent x" + runCount, new Color(0.55f, 1f, 0.65f));
            this.iceSkatingSequenceCoroutine = null;
        }

        private void IceSkatingSequenceFail(string message)
        {
            this.IceSkatingSequenceLog("FAIL " + message, force: true);
            this.iceSkatingSequenceLastStatus = message;
            this.AddMenuNotification("Ice skating: " + message, new Color(1f, 0.55f, 0.45f));
            this.iceSkatingSequenceCoroutine = null;
        }

        private void IceSkatingSequenceAccumulateAction(Dictionary<int, int> batch, int actionId)
        {
            if (batch.ContainsKey(actionId))
            {
                batch[actionId]++;
            }
            else
            {
                batch[actionId] = 1;
            }
        }

        private string IceSkatingSequenceFormatBatch(Dictionary<int, int> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return "{}";
            }

            StringBuilder sb = new StringBuilder(batch.Count * 12);
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<int, int> pair in batch)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append(pair.Key).Append(':').Append(pair.Value);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private void IceSkatingSequenceLog(string message, bool force = false)
        {
            if (!force && !IceSkatingSequenceLogsEnabled)
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            this.iceSkatingSequenceUiLog.Add(line);
            while (this.iceSkatingSequenceUiLog.Count > IceSkatingSequenceUiLogLineLimit)
            {
                this.iceSkatingSequenceUiLog.RemoveAt(0);
            }

            ModLogger.Msg("[IceSkatingSeq] " + message);
        }

        private bool EnsureIceSkatingSequenceSendResolver(out string status)
        {
            if (this.iceSkatingSequenceResolverReady)
            {
                status = "cached";
                return true;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();

            this.iceSkatingSequenceProtocolManagerType = this.ResolveIceSkatingManagedType(
                "SkateProtocolManager",
                IceSkatingSequenceProtocolFullName,
                "Il2CppXDTDataAndProtocol.ProtocolService.Skate.SkateProtocolManager",
                "Il2Cpp.XDTDataAndProtocol.ProtocolService.Skate.SkateProtocolManager");
            if (this.iceSkatingSequenceProtocolManagerType != null)
            {
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                this.iceSkatingSequenceSendStartMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendStartSkateCommand", flags);
                this.iceSkatingSequenceSendExitMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendExitSkateCommand", flags);
                this.iceSkatingSequenceSendChallengeStartMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendStartChallengeCommand", flags);
                this.iceSkatingSequenceSendChallengeEndMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendEndChallengeCommand", flags);
                this.iceSkatingSequenceSendDoActionMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendReportDoActionCommand", flags);
                this.iceSkatingSequenceSendPerfectMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendReportPerfectCommand", flags);
                this.iceSkatingSequenceSendUltimateMethod = this.iceSkatingSequenceProtocolManagerType.GetMethod("SendReportUseUltimateCommand", flags);
            }

            this.iceSkatingSequenceCmdStartType = this.ResolveIceSkatingCommandType("SkateStartNetworkCommand");
            this.iceSkatingSequenceCmdEndType = this.ResolveIceSkatingCommandType("SkateEndNetworkCommand");
            this.iceSkatingSequenceCmdChallengeStartType = this.ResolveIceSkatingCommandType("SkateChallengeStartNetworkCommand");
            this.iceSkatingSequenceCmdChallengeEndType = this.ResolveIceSkatingCommandType("SkateChallengeEndNetworkCommand");
            this.iceSkatingSequenceCmdDoActionType = this.ResolveIceSkatingCommandType("SkateReportDoActionNetworkCommand");
            this.iceSkatingSequenceCmdPerfectType = this.ResolveIceSkatingCommandType("SkateReportPerfectNetworkCommand");
            this.iceSkatingSequenceCmdUltimateType = this.ResolveIceSkatingCommandType("SkateReportUseUltimateNetworkCommand");

            this.TryEnsureIceSkatingAuraProtocolResolver();
            this.EnsureHomelandFarmSendCommandResolver();
            this.EnsureIceSkatingSequenceTableResolver();
            this.EnsureIceSkatingSequenceSkateServiceResolver();

            bool hasAuraProto = this.IceSkatingSequenceHasAuraProtocolPath();
            bool hasManagedProto = this.IceSkatingSequenceHasManagedProtocolPath();
            bool hasSendCommand = this.IceSkatingSequenceHasSendCommandPath();
            if (!hasAuraProto && !hasManagedProto && !hasSendCommand)
            {
                status = "no path: auraProto=" + hasAuraProto
                    + " proto=" + (this.iceSkatingSequenceProtocolManagerType != null ? this.iceSkatingSequenceProtocolManagerType.FullName : "null")
                    + " cmdStart=" + (this.iceSkatingSequenceCmdStartType != null ? this.iceSkatingSequenceCmdStartType.FullName : "null")
                    + " sendCmd=" + (this.homelandFarmSendCommandMethodDef != null);
                return false;
            }

            this.iceSkatingSequenceResolverReady = true;
            status = "auraProto=" + hasAuraProto
                + " managedProto=" + hasManagedProto
                + " sendCommand=" + hasSendCommand
                + " protoType=" + (this.iceSkatingSequenceProtocolManagerType?.FullName ?? "null");
            return true;
        }

        private bool IceSkatingSequenceHasAuraProtocolPath()
        {
            return this.iceSkatingSequenceAuraProtocolClass != IntPtr.Zero
                && this.iceSkatingAuraSendStartMethod != IntPtr.Zero
                && this.iceSkatingAuraSendExitMethod != IntPtr.Zero
                && this.iceSkatingAuraSendChallengeStartMethod != IntPtr.Zero
                && this.iceSkatingAuraSendChallengeEndMethod != IntPtr.Zero
                && this.iceSkatingAuraSendDoActionMethod != IntPtr.Zero
                && this.iceSkatingAuraSendPerfectMethod != IntPtr.Zero
                && this.iceSkatingAuraSendUltimateMethod != IntPtr.Zero;
        }

        private Type ResolveIceSkatingManagedType(string shortName, params string[] fullNames)
        {
            return this.ResolveHomelandFarmManagedType(shortName, fullNames);
        }

        private Type ResolveIceSkatingCommandType(string shortName)
        {
            string ns = IceSkatingSequenceCommandNamespace;
            return this.ResolveHomelandFarmManagedType(
                shortName,
                ns + "." + shortName,
                "EcsClient." + ns + "." + shortName,
                "Il2CppEcsClient." + ns + "." + shortName,
                "Il2Cpp." + ns + "." + shortName,
                "Il2CppEcsClient." + ns + "." + shortName);
        }

        private bool IceSkatingSequenceHasManagedProtocolPath()
        {
            return this.iceSkatingSequenceSendStartMethod != null
                && this.iceSkatingSequenceSendExitMethod != null
                && this.iceSkatingSequenceSendChallengeStartMethod != null
                && this.iceSkatingSequenceSendChallengeEndMethod != null
                && this.iceSkatingSequenceSendDoActionMethod != null
                && this.iceSkatingSequenceSendPerfectMethod != null
                && this.iceSkatingSequenceSendUltimateMethod != null;
        }

        private bool IceSkatingSequenceHasSendCommandPath()
        {
            return this.homelandFarmSendCommandMethodDef != null
                && this.iceSkatingSequenceCmdStartType != null
                && this.iceSkatingSequenceCmdEndType != null
                && this.iceSkatingSequenceCmdChallengeStartType != null
                && this.iceSkatingSequenceCmdChallengeEndType != null
                && this.iceSkatingSequenceCmdDoActionType != null
                && this.iceSkatingSequenceCmdPerfectType != null
                && this.iceSkatingSequenceCmdUltimateType != null;
        }

        private void TryEnsureIceSkatingAuraProtocolResolver()
        {
            if (this.IceSkatingSequenceHasAuraProtocolPath())
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            if (this.iceSkatingSequenceAuraProtocolClass == IntPtr.Zero)
            {
                this.iceSkatingSequenceAuraProtocolClass = this.FindAuraMonoClassByFullName(IceSkatingSequenceProtocolFullName);
                if (this.iceSkatingSequenceAuraProtocolClass == IntPtr.Zero)
                {
                    this.iceSkatingSequenceAuraProtocolClass = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Skate",
                        "SkateProtocolManager",
                        IceSkatingSequenceAuraProtocolImages);
                }

                if (this.iceSkatingSequenceAuraProtocolClass == IntPtr.Zero)
                {
                    this.iceSkatingSequenceAuraProtocolClass = this.FindAuraMonoClassInImages(
                        string.Empty,
                        "SkateProtocolManager",
                        IceSkatingSequenceAuraProtocolImages);
                }
            }

            if (this.iceSkatingSequenceAuraProtocolClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr protoClass = this.iceSkatingSequenceAuraProtocolClass;
            if (this.iceSkatingAuraSendStartMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendStartMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendStartSkateCommand", 0);
            }

            if (this.iceSkatingAuraSendExitMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendExitMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendExitSkateCommand", 0);
            }

            if (this.iceSkatingAuraSendChallengeStartMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendChallengeStartMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendStartChallengeCommand", 1);
            }

            if (this.iceSkatingAuraSendChallengeEndMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendChallengeEndMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendEndChallengeCommand", 1);
            }

            if (this.iceSkatingAuraSendDoActionMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendDoActionMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendReportDoActionCommand", 1);
            }

            if (this.iceSkatingAuraSendPerfectMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendPerfectMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendReportPerfectCommand", 1);
            }

            if (this.iceSkatingAuraSendUltimateMethod == IntPtr.Zero)
            {
                this.iceSkatingAuraSendUltimateMethod = this.FindAuraMonoMethodOnHierarchy(protoClass, "SendReportUseUltimateCommand", 0);
            }
        }

        private void EnsureIceSkatingSequenceTableResolver()
        {
            if (this.iceSkatingSequenceGetSkateActionMethod != null)
            {
                return;
            }

            this.iceSkatingSequenceTableDataType = this.ResolveIceSkatingManagedType(
                "TableData",
                "XDTGame.Core.TableData",
                "EcsClient.TableData",
                "Il2CppEcsClient.TableData",
                "Il2CppXDTGame.Core.TableData");
            if (this.iceSkatingSequenceTableDataType == null)
            {
                return;
            }

            this.iceSkatingSequenceGetSkateActionMethod = this.iceSkatingSequenceTableDataType.GetMethod(
                "GetSkateAction",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null)
                ?? this.iceSkatingSequenceTableDataType.GetMethod(
                    "GetSkateAction",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null);

            this.iceSkatingSequenceGetSkateActionTypeMethod = this.iceSkatingSequenceTableDataType.GetMethod(
                "GetSkateActionType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);

            Type skateActionType = this.ResolveIceSkatingManagedType(
                "TableSkateAction",
                "EcsClient.TableSkateAction",
                "Il2CppEcsClient.TableSkateAction");
            if (skateActionType != null)
            {
                this.iceSkatingSequenceSkateActionParentProperty = skateActionType.GetProperty("parent");
                this.iceSkatingSequenceSkateActionActionTypeProperty = skateActionType.GetProperty("actionType");
            }

            Type skateActionTypeRow = this.ResolveIceSkatingManagedType(
                "TableSkateActionType",
                "EcsClient.TableSkateActionType",
                "Il2CppEcsClient.TableSkateActionType");
            if (skateActionTypeRow != null)
            {
                this.iceSkatingSequenceSkateActionTypeUltimateProperty = skateActionTypeRow.GetProperty("ultimateActionId");
            }
        }

        private void EnsureIceSkatingSequenceSkateServiceResolver()
        {
            if (this.iceSkatingSequenceGetChallengeIdMethod != null)
            {
                return;
            }

            this.iceSkatingSequenceSkateServiceType = this.ResolveIceSkatingManagedType(
                "ISkateService",
                "XDTDataAndProtocol.ProtocolService.Skate.ISkateService",
                "ClientSystem.Skate.ISkateService",
                "Il2CppXDTDataAndProtocol.ProtocolService.Skate.ISkateService");
            if (this.iceSkatingSequenceSkateServiceType == null)
            {
                return;
            }

            this.iceSkatingSequenceGetChallengeIdMethod = this.iceSkatingSequenceSkateServiceType.GetMethod("GetChallengeId");
            Type ecsServiceType = this.ResolveIceSkatingManagedType(
                "EcsService",
                "XDTDataAndProtocol.ProtocolService.EcsService",
                "XD.GameGerm.Ecs.EcsService",
                "Il2CppXDTDataAndProtocol.ProtocolService.EcsService");
            if (ecsServiceType == null)
            {
                return;
            }

            MethodInfo tryGet = ecsServiceType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            this.iceSkatingSequenceEcsTryGetGeneric = tryGet;
        }

        private bool TryReadIceSkatingHighestRecord(out int highestRecord, out string status)
        {
            highestRecord = 0;
            if (this.TryReadIceSkatingHighestRecordAuraMono(out highestRecord, out int remain, out int nextChallenge, out string auraStatus))
            {
                status = auraStatus + " remain=" + remain + " nextChallenge=" + nextChallenge;
                return true;
            }

            status = auraStatus;
            this.EnsureIceSkatingSequenceSkateServiceResolver();
            if (this.iceSkatingSequenceSkateServiceType == null
                || this.iceSkatingSequenceGetChallengeIdMethod == null
                || this.iceSkatingSequenceEcsTryGetGeneric == null)
            {
                return false;
            }

            try
            {
                MethodInfo tryGet = this.iceSkatingSequenceEcsTryGetGeneric.MakeGenericMethod(this.iceSkatingSequenceSkateServiceType);
                object[] args = new object[] { null, false };
                if (!(tryGet.Invoke(null, args) is bool ok) || !ok || args[0] == null)
                {
                    status = status + "; managed EcsService.TryGet<ISkateService> miss";
                    return false;
                }

                object service = args[0];
                object[] invokeArgs = new object[] { 0, 0 };
                object returnValue = this.iceSkatingSequenceGetChallengeIdMethod.Invoke(service, invokeArgs);
                highestRecord = invokeArgs[1] is int high ? high : Convert.ToInt32(invokeArgs[1]);
                int remainManaged = invokeArgs[0] is int rem ? rem : Convert.ToInt32(invokeArgs[0]);
                int nextChallengeManaged = returnValue is int id ? id : Convert.ToInt32(returnValue);
                status = "managed ISkateService remain=" + remainManaged + " nextChallenge=" + nextChallengeManaged;
                return true;
            }
            catch (Exception ex)
            {
                status = status + "; managed " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryReadIceSkatingHighestRecordAuraMono(
            out int highestRecord,
            out int remainChallengeCount,
            out int nextChallengeId,
            out string status)
        {
            highestRecord = 0;
            remainChallengeCount = 0;
            nextChallengeId = 0;
            status = "AuraMono ISkateService unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.EnsureIceSkatingAuraSkateServiceResolved(out status))
            {
                return false;
            }

            if (!this.TryIceSkatingAuraMonoEcsTryGet(this.iceSkatingAuraSkateServiceTryGetClass, false, out IntPtr serviceObj, out string tryGetStatus)
                || serviceObj == IntPtr.Zero)
            {
                status = tryGetStatus;
                return false;
            }

            IntPtr runtimeServiceClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(serviceObj) : IntPtr.Zero;
            IntPtr getChallengeIdMethod = runtimeServiceClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(runtimeServiceClass, "GetChallengeId", 2)
                : IntPtr.Zero;
            if (getChallengeIdMethod == IntPtr.Zero)
            {
                status = "GetChallengeId missing on " + this.GetAuraMonoClassDisplayName(runtimeServiceClass);
                return false;
            }

            int remain = 0;
            int highest = 0;
            IntPtr exc = IntPtr.Zero;
            IntPtr* invokeArgs = stackalloc IntPtr[2];
            invokeArgs[0] = (IntPtr)(&remain);
            invokeArgs[1] = (IntPtr)(&highest);
            IntPtr returnValue = auraMonoRuntimeInvoke(getChallengeIdMethod, serviceObj, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "GetChallengeId AuraMono exception";
                return false;
            }

            if (returnValue != IntPtr.Zero && auraMonoObjectUnbox != null)
            {
                nextChallengeId = *(int*)auraMonoObjectUnbox(returnValue);
            }

            highestRecord = highest;
            remainChallengeCount = remain;
            status = "AuraMono " + this.GetAuraMonoClassDisplayName(runtimeServiceClass);
            return true;
        }

        private bool EnsureIceSkatingAuraSkateServiceResolved(out string status)
        {
            status = "ISkateService class missing";
            if (this.iceSkatingAuraSkateServiceTryGetClass != IntPtr.Zero)
            {
                status = "cached";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono API unavailable";
                return false;
            }

            string[] tryGetCandidates =
            {
                "XDTDataAndProtocol.ProtocolService.Skate.ISkateService",
                "ClientSystem.Skate.SkateClientService"
            };
            for (int i = 0; i < tryGetCandidates.Length; i++)
            {
                IntPtr serviceClass = this.FindAuraMonoClassByFullName(tryGetCandidates[i]);
                if (serviceClass == IntPtr.Zero)
                {
                    int lastDot = tryGetCandidates[i].LastIndexOf('.');
                    string ns = lastDot > 0 ? tryGetCandidates[i].Substring(0, lastDot) : string.Empty;
                    string shortName = lastDot > 0 ? tryGetCandidates[i].Substring(lastDot + 1) : tryGetCandidates[i];
                    serviceClass = this.FindAuraMonoClassAcrossLoadedAssemblies(ns, shortName);
                }

                if (serviceClass == IntPtr.Zero)
                {
                    continue;
                }

                this.iceSkatingAuraSkateServiceTryGetClass = serviceClass;
                status = tryGetCandidates[i];
                return true;
            }

            return false;
        }

        private bool EnsureIceSkatingAuraEcsTryGetOpenMethod()
        {
            if (this.iceSkatingAuraEcsTryGetOpenMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady())
            {
                return false;
            }

            if (this.iceSkatingAuraEcsServiceClass == IntPtr.Zero)
            {
                this.iceSkatingAuraEcsServiceClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.EcsService");
                if (this.iceSkatingAuraEcsServiceClass == IntPtr.Zero)
                {
                    this.iceSkatingAuraEcsServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService",
                        "EcsService");
                }
            }

            if (this.iceSkatingAuraEcsServiceClass == IntPtr.Zero)
            {
                return false;
            }

            this.iceSkatingAuraEcsTryGetOpenMethod = this.FindAuraMonoMethodOnHierarchy(
                this.iceSkatingAuraEcsServiceClass,
                "TryGet",
                2);
            return this.iceSkatingAuraEcsTryGetOpenMethod != IntPtr.Zero;
        }

        private unsafe bool TryIceSkatingInflateAuraMonoEcsTryGetMethod(IntPtr serviceClass, out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (serviceClass == IntPtr.Zero
                || !this.EnsureIceSkatingAuraEcsTryGetOpenMethod()
                || auraMonoClassInflateGenericMethod == null
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null)
            {
                return false;
            }

            if (this.iceSkatingAuraInflatedTryGetByServiceClass.TryGetValue(serviceClass, out inflatedMethod)
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

            inflatedMethod = auraMonoClassInflateGenericMethod(this.iceSkatingAuraEcsTryGetOpenMethod, ref context);
            if (inflatedMethod == IntPtr.Zero || !AuraMonoMethodParamCountIs(inflatedMethod, 2))
            {
                return false;
            }

            this.iceSkatingAuraInflatedTryGetByServiceClass[serviceClass] = inflatedMethod;
            return true;
        }

        private unsafe bool TryIceSkatingAuraMonoEcsTryGet(
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

            if (!this.TryIceSkatingInflateAuraMonoEcsTryGetMethod(serviceClass, out IntPtr inflatedMethod))
            {
                status = "AuraMono EcsService.TryGet inflate failed";
                return false;
            }

            IntPtr* serviceSlot = stackalloc IntPtr[1];
            serviceSlot[0] = IntPtr.Zero;
            bool logErrorValue = logError;
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
                status = "AuraMono EcsService.TryGet miss";
                return false;
            }

            status = "AuraMono EcsService.TryGet ok";
            return true;
        }

        private bool TryResolveIceSkatingNormalActionIds(int actionCount, out int[] normalIds, out string status)
        {
            normalIds = null;
            status = "invalid action count";
            if (actionCount <= 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(this.iceSkatingSequenceNormalsOverride))
            {
                string[] parts = this.iceSkatingSequenceNormalsOverride.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    normalIds = new int[actionCount];
                    for (int i = 0; i < actionCount; i++)
                    {
                        string part = parts[Math.Min(i, parts.Length - 1)].Trim();
                        if (!int.TryParse(part, out normalIds[i]) || normalIds[i] <= 0)
                        {
                            status = "invalid override id at index " + i;
                            normalIds = null;
                            return false;
                        }
                    }

                    status = "override csv padded to " + actionCount;
                    return true;
                }
            }

            int branchId = this.iceSkatingSequenceBranchNormalId > 0
                ? this.iceSkatingSequenceBranchNormalId
                : IceSkatingSequenceDefaultBranchNormalId;

            if (this.TryBuildIceSkatingParentPath(branchId, out List<int> path, out string pathStatus))
            {
                normalIds = this.IceSkatingSequencePickActionIdsFromPath(path, actionCount);
                status = "path " + pathStatus + " picked=[" + string.Join(",", normalIds) + "]";
                return true;
            }

            normalIds = new int[actionCount];
            for (int i = 0; i < actionCount; i++)
            {
                normalIds[i] = Math.Max(1, branchId - (actionCount - 1 - i));
            }

            status = "fallback linear to branch " + branchId;
            return true;
        }

        private int[] IceSkatingSequencePickActionIdsFromPath(List<int> path, int actionCount)
        {
            path = path ?? new List<int>(0);
            actionCount = Math.Max(1, actionCount);
            if (path.Count == 0)
            {
                int branch = this.iceSkatingSequenceBranchNormalId > 0
                    ? this.iceSkatingSequenceBranchNormalId
                    : IceSkatingSequenceDefaultBranchNormalId;
                int[] filled = new int[actionCount];
                for (int i = 0; i < actionCount; i++)
                {
                    filled[i] = branch;
                }

                return filled;
            }

            int[] result = new int[actionCount];
            if (path.Count >= actionCount)
            {
                int start = path.Count - actionCount;
                for (int i = 0; i < actionCount; i++)
                {
                    result[i] = path[start + i];
                }

                return result;
            }

            for (int i = 0; i < actionCount; i++)
            {
                result[i] = path[Math.Min(i, path.Count - 1)];
            }

            return result;
        }

        private bool TryBuildIceSkatingParentPath(int leafId, out List<int> path, out string status)
        {
            path = new List<int>(8);
            status = "TableData unavailable";
            if (this.iceSkatingSequenceGetSkateActionMethod == null || this.iceSkatingSequenceSkateActionParentProperty == null)
            {
                return false;
            }

            try
            {
                int current = leafId;
                int guard = 0;
                while (current > 0 && guard < 32)
                {
                    guard++;
                    path.Add(current);
                    object row = this.iceSkatingSequenceGetSkateActionMethod.GetParameters().Length == 1
                        ? this.iceSkatingSequenceGetSkateActionMethod.Invoke(null, new object[] { current })
                        : this.iceSkatingSequenceGetSkateActionMethod.Invoke(null, new object[] { current, false });
                    if (row == null)
                    {
                        break;
                    }

                    object parentObj = this.iceSkatingSequenceSkateActionParentProperty.GetValue(row, null);
                    current = parentObj is int parent ? parent : Convert.ToInt32(parentObj);
                }

                path.Reverse();
                status = "parent-walk len=" + path.Count + " leaf=" + leafId;
                return path.Count > 0;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryResolveIceSkatingUltimateForBranch(int branchNormalId, out int ultimateId, out string status)
        {
            ultimateId = 0;
            status = "TableData unavailable";
            if (this.iceSkatingSequenceGetSkateActionMethod == null
                || this.iceSkatingSequenceGetSkateActionTypeMethod == null
                || this.iceSkatingSequenceSkateActionActionTypeProperty == null
                || this.iceSkatingSequenceSkateActionTypeUltimateProperty == null)
            {
                return false;
            }

            try
            {
                object row = this.iceSkatingSequenceGetSkateActionMethod.GetParameters().Length == 1
                    ? this.iceSkatingSequenceGetSkateActionMethod.Invoke(null, new object[] { branchNormalId })
                    : this.iceSkatingSequenceGetSkateActionMethod.Invoke(null, new object[] { branchNormalId, false });
                if (row == null)
                {
                    status = "GetSkateAction(" + branchNormalId + ") null";
                    return false;
                }

                object actionTypeObj = this.iceSkatingSequenceSkateActionActionTypeProperty.GetValue(row, null);
                int actionType = actionTypeObj is int type ? type : Convert.ToInt32(actionTypeObj);
                object typeRow = this.iceSkatingSequenceGetSkateActionTypeMethod.Invoke(null, new object[] { actionType });
                if (typeRow == null)
                {
                    status = "GetSkateActionType(" + actionType + ") null";
                    return false;
                }

                object ultimateObj = this.iceSkatingSequenceSkateActionTypeUltimateProperty.GetValue(typeRow, null);
                ultimateId = ultimateObj is int ult ? ult : Convert.ToInt32(ultimateObj);
                status = "branch " + branchNormalId + " actionType=" + actionType + " ultimate=" + ultimateId;
                return ultimateId > 0;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TrySendIceSkatingStart(out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolZeroArg(this.iceSkatingAuraSendStartMethod, "SendStartSkateCommand", out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdStartType, _ => true, "SkateStart", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendStartSkateCommand", null, out status);
        }

        private bool TrySendIceSkatingEnd(out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolZeroArg(this.iceSkatingAuraSendExitMethod, "SendExitSkateCommand", out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdEndType, _ => true, "SkateEnd", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendExitSkateCommand", null, out status);
        }

        private bool TrySendIceSkatingChallengeStart(bool isHelp, out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolBoolArg(this.iceSkatingAuraSendChallengeStartMethod, "SendStartChallengeCommand", isHelp, out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdChallengeStartType, cmd =>
            {
                this.TrySetObjectMember(cmd, "IsHelp", isHelp);
                return true;
            }, "SkateChallengeStart", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendStartChallengeCommand", new object[] { isHelp }, out status);
        }

        private bool TrySendIceSkatingChallengeEnd(int score, out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolIntArg(this.iceSkatingAuraSendChallengeEndMethod, "SendEndChallengeCommand", score, out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdChallengeEndType, cmd =>
            {
                this.TrySetObjectMember(cmd, "Score", score);
                return true;
            }, "SkateChallengeEnd", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendEndChallengeCommand", new object[] { score }, out status);
        }

        private bool TrySendIceSkatingPerfect(int perfect, out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolIntArg(this.iceSkatingAuraSendPerfectMethod, "SendReportPerfectCommand", perfect, out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdPerfectType, cmd =>
            {
                this.TrySetObjectMember(cmd, "Perfect", perfect);
                return true;
            }, "SkateReportPerfect", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendReportPerfectCommand", new object[] { perfect }, out status);
        }

        private bool TrySendIceSkatingUseUltimate(out string status)
        {
            if (this.TryInvokeIceSkatingAuraProtocolZeroArg(this.iceSkatingAuraSendUltimateMethod, "SendReportUseUltimateCommand", out status))
            {
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdUltimateType, cmd =>
            {
                this.TrySetObjectMember(cmd, "UseUltimate", 1);
                return true;
            }, "SkateReportUseUltimate", out status))
            {
                return true;
            }

            return this.TryInvokeIceSkatingManagedProtocol("SendReportUseUltimateCommand", null, out status);
        }

        private bool TryFlushIceSkatingDoActionBatch(Dictionary<int, int> batch, out string status)
        {
            status = "empty batch";
            if (batch == null || batch.Count == 0)
            {
                return true;
            }

            Dictionary<int, int> payload = new Dictionary<int, int>(batch);
            string payloadText = this.IceSkatingSequenceFormatBatch(payload);

            if (this.TryInvokeIceSkatingAuraProtocolDoAction(payload, out string auraStatus))
            {
                status = "Actions=" + payloadText + " " + auraStatus;
                batch.Clear();
                return true;
            }

            if (this.TryIceSkatingSendCommand(this.iceSkatingSequenceCmdDoActionType, cmd =>
            {
                this.TrySetObjectMember(cmd, "Actions", payload);
                return true;
            }, "SkateReportDoAction", out status))
            {
                status = "Actions=" + payloadText + " " + status;
                batch.Clear();
                return true;
            }

            if (this.TryInvokeIceSkatingManagedProtocol("SendReportDoActionCommand", new object[] { payload }, out string protoStatus))
            {
                status = "Actions=" + payloadText + " " + protoStatus;
                batch.Clear();
                return true;
            }

            status = "Actions=" + payloadText + " aura=" + auraStatus + "; " + status;
            return false;
        }

        private unsafe bool TryInvokeIceSkatingAuraProtocolDoAction(Dictionary<int, int> actions, out string status)
        {
            status = "SendReportDoActionCommand AuraMono unavailable";
            if (actions == null || actions.Count == 0)
            {
                status = "empty actions";
                return true;
            }

            if (this.iceSkatingAuraSendDoActionMethod == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryCreateIceSkatingAuraIntIntDictionary(actions, out IntPtr dictObj, out string dictStatus))
            {
                status = dictStatus;
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = dictObj;
            auraMonoRuntimeInvoke(this.iceSkatingAuraSendDoActionMethod, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "SendReportDoActionCommand AuraMono exception";
                return false;
            }

            status = "SendReportDoActionCommand AuraMono ok";
            return true;
        }

        private unsafe bool TryCreateIceSkatingAuraIntIntDictionary(
            Dictionary<int, int> actions,
            out IntPtr dictObj,
            out string status)
        {
            dictObj = IntPtr.Zero;
            status = "Dictionary<int,int> unavailable";
            if (actions == null || actions.Count == 0)
            {
                status = "empty actions";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoStringNew == null
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < IceSkatingAuraIntIntDictionaryTypeNames.Length && dictObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, IceSkatingAuraIntIntDictionaryTypeNames[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* typeArgs = stackalloc IntPtr[1];
                typeArgs[0] = typeNameObj;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                exc = IntPtr.Zero;
                IntPtr* createArgs = stackalloc IntPtr[1];
                createArgs[0] = typeObj;
                dictObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    dictObj = IntPtr.Zero;
                }
            }

            if (dictObj == IntPtr.Zero)
            {
                status = "Dictionary<int,int> construction failed";
                return false;
            }

            IntPtr dictClass = this.iceSkatingAuraIntIntDictionaryClass;
            if (dictClass == IntPtr.Zero)
            {
                dictClass = auraMonoObjectGetClass(dictObj);
                this.iceSkatingAuraIntIntDictionaryClass = dictClass;
            }

            IntPtr writeMethod = this.iceSkatingAuraDictSetItemMethod;
            if (writeMethod == IntPtr.Zero && dictClass != IntPtr.Zero)
            {
                writeMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "set_Item", 2);
                if (writeMethod == IntPtr.Zero)
                {
                    writeMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "Add", 2);
                    if (writeMethod != IntPtr.Zero)
                    {
                        this.iceSkatingAuraDictAddMethod = writeMethod;
                    }
                }
                else
                {
                    this.iceSkatingAuraDictSetItemMethod = writeMethod;
                }
            }

            if (writeMethod == IntPtr.Zero)
            {
                status = "Dictionary write method missing";
                return false;
            }

            foreach (KeyValuePair<int, int> pair in actions)
            {
                int key = pair.Key;
                int value = pair.Value;
                IntPtr excSet = IntPtr.Zero;
                IntPtr* setArgs = stackalloc IntPtr[2];
                setArgs[0] = (IntPtr)(&key);
                setArgs[1] = (IntPtr)(&value);
                auraMonoRuntimeInvoke(writeMethod, dictObj, (IntPtr)setArgs, ref excSet);
                if (excSet != IntPtr.Zero)
                {
                    status = "Dictionary write threw key=" + key;
                    dictObj = IntPtr.Zero;
                    return false;
                }
            }

            status = "Dictionary<int,int> ok count=" + actions.Count;
            return true;
        }

        private unsafe bool TryInvokeIceSkatingAuraProtocolZeroArg(IntPtr method, string methodName, out string status)
        {
            status = methodName + " AuraMono unavailable";
            if (method == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = methodName + " AuraMono exception";
                return false;
            }

            status = methodName + " AuraMono ok";
            return true;
        }

        private unsafe bool TryInvokeIceSkatingAuraProtocolBoolArg(IntPtr method, string methodName, bool value, out string status)
        {
            status = methodName + " AuraMono unavailable";
            if (method == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            bool arg = value;
            IntPtr exc = IntPtr.Zero;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = methodName + " AuraMono exception";
                return false;
            }

            status = methodName + " AuraMono ok IsHelp=" + value;
            return true;
        }

        private unsafe bool TryInvokeIceSkatingAuraProtocolIntArg(IntPtr method, string methodName, int value, out string status)
        {
            status = methodName + " AuraMono unavailable";
            if (method == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int arg = value;
            IntPtr exc = IntPtr.Zero;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = methodName + " AuraMono exception";
                return false;
            }

            status = methodName + " AuraMono ok value=" + value;
            return true;
        }

        private bool TryIceSkatingSendCommand(Type commandType, Func<object, bool> populate, string label, out string status)
        {
            status = label + " SendCommand unavailable.";
            if (commandType == null || populate == null)
            {
                status = label + " command type missing.";
                return false;
            }

            if (!this.TryHomelandFarmSendCommand(commandType, populate, out string sendStatus))
            {
                status = label + " " + sendStatus;
                return false;
            }

            status = label + " " + sendStatus + " type=" + commandType.FullName;
            return true;
        }

        private bool TryInvokeIceSkatingManagedProtocol(string methodName, object[] args, out string status)
        {
            status = methodName + " managed unavailable";
            MethodInfo method = this.IceSkatingSequenceGetProtocolMethod(methodName);
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(null, args ?? Array.Empty<object>());
                status = methodName + " managed (" + this.iceSkatingSequenceProtocolManagerType?.FullName + ")";
                return true;
            }
            catch (Exception ex)
            {
                status = methodName + " managed exception: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private MethodInfo IceSkatingSequenceGetProtocolMethod(string methodName)
        {
            switch (methodName)
            {
                case "SendStartSkateCommand": return this.iceSkatingSequenceSendStartMethod;
                case "SendExitSkateCommand": return this.iceSkatingSequenceSendExitMethod;
                case "SendStartChallengeCommand": return this.iceSkatingSequenceSendChallengeStartMethod;
                case "SendEndChallengeCommand": return this.iceSkatingSequenceSendChallengeEndMethod;
                case "SendReportDoActionCommand": return this.iceSkatingSequenceSendDoActionMethod;
                case "SendReportPerfectCommand": return this.iceSkatingSequenceSendPerfectMethod;
                case "SendReportUseUltimateCommand": return this.iceSkatingSequenceSendUltimateMethod;
                default: return null;
            }
        }
    }
}
