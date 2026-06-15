using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool MasterLogSnowSculpture = false;
        private const float SnowSculptureTypeResolveRetrySeconds = 2f;
        private const int SnowSculptureRatingSuccessId = 2;
        private const int SnowSculptureMaxRound = 20;
        private const float SnowApiRetryBackoffSeconds = 0.5f;
        // Faster cadence while blindly filling the base + starting (PutSnowBall/StartSculpting
        // are cheap protocol sends; the server rejects premature/redundant ones harmlessly).
        private const float SnowApiStartBackoffSeconds = 0.3f;
        // Time to let the server finalize the sculpture (Idle + finishedStatidId) after
        // StopSculpting before attempting the GatherSnowSculpture take.
        private const float SnowApiFinalizeDelaySeconds = 2f;
        // Radius (metres) for finding a snow base without standing next to it.
        private const float SnowApiBaseScanRadius = 50f;
        private const int SnowApiBaseScanEntityCap = 4096;

        private const int SnowStorageWarehouse = 2;
        private const int SnowballStaticId = 5100;

        private const float SnowApiReportIntervalSeconds = 0.01f;

        private bool autoSnowEnabled = false;
        private int snowClickCount = 0;
        private uint snowApiTargetNetId = 0;
        private int snowApiRoundCount = 0;
        private int snowApiSuccessScore = 0;
        private bool snowApiStopSent = false;
        private float snowApiStopSentAt = 0f;
        private uint snowApiLastGatheredNetId = 0;
        private uint snowApiKnownBaseNetId = 0;
        private bool autoSnowWasEnabled = false;
        private float snowApiNextAttemptAt = 0f;
        private KeyCode autoSnowHotkey = KeyCode.None;
        private bool isListeningForAutoSnowHotkey = false;

        private string snowSculptureResolveStatus = "not resolved";
        private float snowSculptureNextResolveAt;
        private string snowSculptureLastActionStatus = string.Empty;
        private string snowLastLoggedStatus = string.Empty;
        private string snowMoveSnowballsStatus = string.Empty;

        private MethodInfo snowCachedReportScoreMethod;
        private MethodInfo snowCachedGatherSculptureMethod;
        private MethodInfo snowCachedStopSculptingMethod;
        private MethodInfo snowCachedStartSculptingMethod;
        private MethodInfo snowCachedPutSnowBallMethod;
        private Type snowCachedReportCommandType;
        private MethodInfo snowCachedSendCommandMethod;
        private object snowCachedReliableChannel;
        private MethodInfo snowCachedGetSnowSculptingRatingMethod;

        private IntPtr snowAuraMonoProtocolClass;
        private IntPtr snowAuraMonoReportScoreMethod;
        private IntPtr snowAuraMonoGatherSculptureMethod;
        private IntPtr snowAuraMonoStopSculptingMethod;
        private IntPtr snowAuraMonoStartSculptingMethod;
        private IntPtr snowAuraMonoPutSnowBallMethod;
        private IntPtr snowAuraMonoGetItemNetIdMethod;
        private IntPtr snowAuraMonoEntityUtilClass;
        private IntPtr snowAuraMonoGetEntityResIdMethod;
        private IntPtr snowAuraMonoGetSnowbaseMethod;
        private IntPtr snowAuraMonoTableDataClass;
        private IntPtr snowAuraMonoGetSnowSculptingRatingMethod;
        private IntPtr snowAuraMonoPanelClass;

        private static readonly string[] SnowAuraMonoPanelTypeNames =
        {
            "XDTGame.UI.Panel.SnowSculpturePanel",
            "SnowSculpturePanel"
        };

        private static readonly string[] SnowAuraMonoPanelImages =
        {
            "XDTGameUI",
            "XDTGameUI.dll"
        };

        private static readonly string[] SnowAuraMonoProtocolTypeNames =
        {
            "XDTDataAndProtocol.ProtocolService.Snow.SnowSculptureProtocolManager",
            "SnowSculptureProtocolManager"
        };

        private static readonly string[] SnowAuraMonoProtocolImages =
        {
            "XDTDataAndProtocol",
            "XDTDataAndProtocol.dll",
            "Client",
            "Client.dll",
            "EcsClient",
            "EcsClient.dll"
        };

        private bool SnowSculptureHasReportPath()
        {
            return this.snowCachedReportScoreMethod != null
                   || this.snowAuraMonoReportScoreMethod != IntPtr.Zero
                   || (this.snowCachedSendCommandMethod != null && this.snowCachedReportCommandType != null);
        }

        private bool SnowSculptureHasGatherPath()
        {
            return this.snowCachedGatherSculptureMethod != null
                   || this.snowAuraMonoGatherSculptureMethod != IntPtr.Zero;
        }

        private bool SnowSculptureIsResolved()
        {
            return this.SnowSculptureHasReportPath();
        }

        private void ProcessSnowSculptureOnUpdate()
        {
            if (this.autoSnowEnabled && !this.autoSnowWasEnabled)
            {
                this.ResetSnowApiProgress();
            }

            this.autoSnowWasEnabled = this.autoSnowEnabled;

            if (this.autoSnowEnabled)
            {
                this.RunAutoSnowSculptureApi();
            }
        }

        private void ResetSnowApiProgress()
        {
            this.snowApiTargetNetId = 0;
            this.snowApiRoundCount = 0;
            this.snowApiSuccessScore = 0;
            this.snowApiStopSent = false;
            this.snowApiStopSentAt = 0f;
            this.snowApiLastGatheredNetId = 0;
            this.snowApiKnownBaseNetId = 0;
            this.snowApiNextAttemptAt = 0f;
        }

        private void DisableAutoSnowSculpture(string reason)
        {
            if (!this.autoSnowEnabled)
            {
                return;
            }

            this.autoSnowEnabled = false;
            this.AddMenuNotification("Auto Snow Sculpture disabled: " + reason, new Color(1f, 0.55f, 0.55f));
        }

        // Resets per-sculpture progress while keeping the toggle on, so the next sculpture is
        // picked up automatically. Remembers the base just gathered to avoid re-engaging it.
        private void PrepareSnowApiForNextSculpture(uint justGatheredNetId)
        {
            this.snowApiTargetNetId = 0;
            this.snowApiRoundCount = 0;
            this.snowApiSuccessScore = 0;
            this.snowApiStopSent = false;
            this.snowApiStopSentAt = 0f;
            this.snowApiLastGatheredNetId = justGatheredNetId;
        }

        private void SnowSculptureLog(string message)
        {
            if (!MasterLogSnowSculpture)
            {
                return;
            }

            ModLogger.Msg("[SnowSculpture] " + message);
        }

        // Writes to helper.log regardless of MasterLogSnowSculpture, deduped so a stuck
        // per-tick state (backoff) is logged once rather than spamming the file.
        private void SnowSculptureLogStatus(string message)
        {
            if (string.IsNullOrEmpty(message) || message == this.snowLastLoggedStatus)
            {
                return;
            }

            this.snowLastLoggedStatus = message;
            ModLogger.Msg("[SnowSculpture] " + message);
        }

        private bool EnsureSnowSculptureTypesResolved(out string status)
        {
            status = this.snowSculptureResolveStatus;
            if (this.SnowSculptureIsResolved())
            {
                status = "cached";
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.snowSculptureNextResolveAt)
            {
                return false;
            }

            this.snowSculptureNextResolveAt = now + SnowSculptureTypeResolveRetrySeconds;

            try
            {
                Type protocolManagerType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.Snow.SnowSculptureProtocolManager",
                    "Il2CppXDTDataAndProtocol.ProtocolService.Snow.SnowSculptureProtocolManager",
                    "SnowSculptureProtocolManager");

                this.snowCachedReportCommandType = this.FindLoadedType(
                    "EcsClient.XDT.Scene.Shared.Modules.Snowman.SnowSculptingReportQteScoreNetworkCommand",
                    "XDT.Scene.Shared.Modules.Snowman.SnowSculptingReportQteScoreNetworkCommand",
                    "Il2CppEcsClient.XDT.Scene.Shared.Modules.Snowman.SnowSculptingReportQteScoreNetworkCommand",
                    "SnowSculptingReportQteScoreNetworkCommand");

                Type tableDataType = this.FindLoadedType(
                    "TableData",
                    "EcsClient.TableData",
                    "Il2CppEcsClient.TableData");
                if (tableDataType != null)
                {
                    this.snowCachedGetSnowSculptingRatingMethod = tableDataType.GetMethod(
                        "GetSnowSculptingRating",
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (protocolManagerType != null)
                {
                    this.snowCachedReportScoreMethod = protocolManagerType.GetMethod(
                        "ReportSculptingScore",
                        BindingFlags.Public | BindingFlags.Static);
                    this.snowCachedGatherSculptureMethod = protocolManagerType.GetMethod(
                        "GatherSnowSculpture",
                        BindingFlags.Public | BindingFlags.Static);
                    this.snowCachedStopSculptingMethod = protocolManagerType.GetMethod(
                        "StopSculpting",
                        BindingFlags.Public | BindingFlags.Static);
                    this.snowCachedStartSculptingMethod = protocolManagerType.GetMethod(
                        "StartSculpting",
                        BindingFlags.Public | BindingFlags.Static);
                    this.snowCachedPutSnowBallMethod = protocolManagerType.GetMethod(
                        "PutSnowBall",
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (this.snowCachedSendCommandMethod == null)
                {
                    Type webRequestType = this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                        "Il2CppXDTDataAndProtocol.ProtocolService.WebRequestUtility",
                        "WebRequestUtility");
                    Type channelType = this.FindLoadedType(
                        "XD.GameGerm.Network.ChannelType",
                        "Il2CppXD.GameGerm.Network.ChannelType",
                        "ChannelType");
                    if (webRequestType != null && channelType != null)
                    {
                        foreach (MethodInfo method in webRequestType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (method.Name != "SendCommand" || !method.IsGenericMethodDefinition)
                            {
                                continue;
                            }

                            if (method.GetParameters().Length != 3)
                            {
                                continue;
                            }

                            this.snowCachedSendCommandMethod = method;
                            this.snowCachedReliableChannel = Enum.Parse(channelType, "Reliable");
                            break;
                        }
                    }
                }

                string auraStatus;
                this.TryResolveSnowSculptureAuraMono(out auraStatus);

                bool ok = this.SnowSculptureIsResolved();
                status = ok
                    ? "resolved managedReport=" + (this.snowCachedReportScoreMethod != null)
                      + " auraReport=" + (this.snowAuraMonoReportScoreMethod != IntPtr.Zero)
                      + " gather=" + this.SnowSculptureHasGatherPath()
                    : "unavailable managedReport=" + (this.snowCachedReportScoreMethod != null)
                      + " sendCmd=" + (this.snowCachedSendCommandMethod != null)
                      + " cmdType=" + (this.snowCachedReportCommandType != null)
                      + " | aura: " + auraStatus;
                this.snowSculptureResolveStatus = status;
                this.SnowSculptureLog("resolve: " + status);
                return ok;
            }
            catch (Exception ex)
            {
                status = "error: " + ex.Message;
                this.snowSculptureResolveStatus = status;
                this.SnowSculptureLog("resolve error: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveSnowSculptureAuraMono(out string auraStatus)
        {
            auraStatus = "not attempted";
            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    auraStatus = "aura api not ready";
                    return false;
                }

                if (this.snowAuraMonoProtocolClass == IntPtr.Zero)
                {
                    for (int i = 0; i < SnowAuraMonoProtocolTypeNames.Length; i++)
                    {
                        this.snowAuraMonoProtocolClass = this.FindAuraMonoClassByFullName(SnowAuraMonoProtocolTypeNames[i]);
                        if (this.snowAuraMonoProtocolClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                if (this.snowAuraMonoProtocolClass == IntPtr.Zero)
                {
                    this.snowAuraMonoProtocolClass = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Snow",
                        "SnowSculptureProtocolManager",
                        SnowAuraMonoProtocolImages);
                }

                if (this.snowAuraMonoProtocolClass == IntPtr.Zero)
                {
                    this.snowAuraMonoProtocolClass = this.FindAuraMonoClassInImages(
                        string.Empty,
                        "SnowSculptureProtocolManager",
                        SnowAuraMonoProtocolImages);
                }

                if (this.snowAuraMonoProtocolClass != IntPtr.Zero)
                {
                    if (this.snowAuraMonoReportScoreMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoReportScoreMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoProtocolClass,
                            "ReportSculptingScore",
                            2);
                    }

                    if (this.snowAuraMonoGatherSculptureMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoGatherSculptureMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoProtocolClass,
                            "GatherSnowSculpture",
                            1);
                    }

                    if (this.snowAuraMonoStopSculptingMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoStopSculptingMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoProtocolClass,
                            "StopSculpting",
                            1);
                    }

                    if (this.snowAuraMonoStartSculptingMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoStartSculptingMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoProtocolClass,
                            "StartSculpting",
                            1);
                    }

                    if (this.snowAuraMonoPutSnowBallMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoPutSnowBallMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoProtocolClass,
                            "PutSnowBall",
                            2);
                    }
                }

                if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                {
                    // TableData lives in the GLOBAL namespace on the EcsClient image, so the
                    // full-name / "EcsClient.TableData" lookups all miss. Use the canonical
                    // resolver (auraMonoClassFromName with an empty namespace).
                    this.snowAuraMonoTableDataClass = this.FindAuraMonoTableDataClass();
                    if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                    {
                        this.snowAuraMonoTableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                    }

                    if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                    {
                        this.snowAuraMonoTableDataClass = this.FindAuraMonoClassByFullName("EcsClient.TableData");
                    }
                }

                if (this.snowAuraMonoTableDataClass != IntPtr.Zero && this.snowAuraMonoGetSnowSculptingRatingMethod == IntPtr.Zero)
                {
                    // Signature is GetSnowSculptingRating(int id, bool needException = false) -> 2 params.
                    this.snowAuraMonoGetSnowSculptingRatingMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.snowAuraMonoTableDataClass,
                        "GetSnowSculptingRating",
                        2);
                    if (this.snowAuraMonoGetSnowSculptingRatingMethod == IntPtr.Zero)
                    {
                        this.snowAuraMonoGetSnowSculptingRatingMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.snowAuraMonoTableDataClass,
                            "GetSnowSculptingRating",
                            1);
                    }
                }

                if (this.snowAuraMonoPanelClass == IntPtr.Zero)
                {
                    for (int i = 0; i < SnowAuraMonoPanelTypeNames.Length; i++)
                    {
                        this.snowAuraMonoPanelClass = this.FindAuraMonoClassByFullName(SnowAuraMonoPanelTypeNames[i]);
                        if (this.snowAuraMonoPanelClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                if (this.snowAuraMonoPanelClass == IntPtr.Zero)
                {
                    this.snowAuraMonoPanelClass = this.FindAuraMonoClassInImages(
                        "XDTGame.UI.Panel",
                        "SnowSculpturePanel",
                        SnowAuraMonoPanelImages);
                }

                auraStatus = "protocolClass=0x" + this.snowAuraMonoProtocolClass.ToInt64().ToString("X")
                             + " report=0x" + this.snowAuraMonoReportScoreMethod.ToInt64().ToString("X")
                             + " gather=0x" + this.snowAuraMonoGatherSculptureMethod.ToInt64().ToString("X")
                             + " table=0x" + this.snowAuraMonoTableDataClass.ToInt64().ToString("X")
                             + " panelClass=0x" + this.snowAuraMonoPanelClass.ToInt64().ToString("X");
                return this.snowAuraMonoReportScoreMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                auraStatus = "aura exception: " + ex.Message;
                return false;
            }
        }

        private void RunAutoSnowSculptureApi()
        {
            float unscaledTime = Time.unscaledTime;
            if (unscaledTime < this.snowApiNextAttemptAt)
            {
                return;
            }

            // Default cadence: SnowApiReportIntervalSeconds (10 ms); error paths use SnowApiRetryBackoffSeconds.
            this.snowApiNextAttemptAt = unscaledTime + SnowApiReportIntervalSeconds;

            if (!this.EnsureSnowSculptureTypesResolved(out string resolveStatus))
            {
                this.snowSculptureLastActionStatus = "resolving types...";
                this.SnowSculptureLogStatus("resolve: " + resolveStatus);
                this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                return;
            }

            // Resolve the active snow base once per sculpture (this path does the heavy
            // panel GetView / player-status AuraMono work — keep it out of the per-round loop).
            if (this.snowApiTargetNetId == 0)
            {
                if (!this.TryGetSnowSculptureTargetNetId(out uint targetNetId, out string targetStatus))
                {
                    // No active sculpture: the previous session is over, so forget the
                    // just-gathered guard, then start a new sculpture purely via protocol
                    // (fill snowball + StartSculpting; no interact -> no icall spam).
                    this.snowApiLastGatheredNetId = 0;
                    if (this.TryStartSnowSculptureViaApi(out string startStatus))
                    {
                        // Pure-protocol start does NOT open the panel / set the client TargetNetId,
                        // so optimistically treat the base we just filled+started as the active
                        // target and proceed to report rounds. A base needs exactly one snowball
                        // (Idle->Prepared->Started), and the reliable channel processes
                        // PutSnowBall -> StartSculpting -> ReportSculptingScore in order. If it did
                        // not actually start, the reports/stop/gather are rejected harmlessly and
                        // the cycle simply retries.
                        this.snowApiTargetNetId = this.snowApiKnownBaseNetId;
                        this.snowApiRoundCount = 0;
                        this.snowApiSuccessScore = 0;
                        this.snowApiStopSent = false;
                        this.snowApiStopSentAt = 0f;
                        this.snowSculptureLastActionStatus = "started (optimistic)";
                        this.SnowSculptureLogStatus("start: " + startStatus + " -> target=" + this.snowApiKnownBaseNetId);
                        // brief pause so the server processes fill+start before the first report
                        this.snowApiNextAttemptAt = unscaledTime + SnowApiStartBackoffSeconds;
                    }
                    else
                    {
                        this.snowSculptureLastActionStatus = "idle: " + startStatus;
                        this.SnowSculptureLogStatus("start unavailable: " + startStatus);
                        if (startStatus.StartsWith("no snowballs in bag", StringComparison.Ordinal))
                        {
                            this.DisableAutoSnowSculpture(startStatus);
                        }
                        else
                        {
                            this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                        }
                    }

                    return;
                }

                // Same base we just gathered (server may still report it briefly): wait for the
                // next sculpture instead of re-running rounds on a finished base.
                if (targetNetId == this.snowApiLastGatheredNetId)
                {
                    this.snowSculptureLastActionStatus = "waiting for next sculpture";
                    this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                    return;
                }

                this.snowApiLastGatheredNetId = 0;
                this.snowApiTargetNetId = targetNetId;
                this.snowApiKnownBaseNetId = targetNetId;
                this.snowApiRoundCount = 0;
                this.snowApiSuccessScore = 0;
                this.SnowSculptureLogStatus("target resolved netId=" + targetNetId + " (" + targetStatus + ")");
            }

            // Round 20 reached -> finalize: StopSculpting (server computes the sculpture from the
            // accumulated score), wait for finalization, then GatherSnowSculpture (the take).
            if (this.snowApiRoundCount >= SnowSculptureMaxRound)
            {
                if (!this.snowApiStopSent)
                {
                    this.TryStopSnowSculpting(this.snowApiTargetNetId, out string stopStatus);
                    this.snowApiStopSent = true;
                    this.snowApiStopSentAt = unscaledTime;
                    this.snowSculptureLastActionStatus = "finalizing...";
                    this.SnowSculptureLogStatus("stop: " + stopStatus);
                    this.snowApiNextAttemptAt = unscaledTime + SnowApiFinalizeDelaySeconds;
                    return;
                }

                if (unscaledTime - this.snowApiStopSentAt < SnowApiFinalizeDelaySeconds)
                {
                    this.snowSculptureLastActionStatus = "finalizing...";
                    this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                    return;
                }

                this.TryGatherSnowSculpture(this.snowApiTargetNetId, out string gatherStatus);
                uint gatheredNetId = this.snowApiTargetNetId;
                this.SnowSculptureLogStatus("gather: " + gatherStatus);
                this.PrepareSnowApiForNextSculpture(gatheredNetId);
                this.snowSculptureLastActionStatus = "complete (gathered); waiting for next";
                this.AddMenuNotification("Snow sculpture complete: " + gatherStatus, new Color(0.45f, 1f, 0.55f));
                this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                return;
            }

            // Resolve the perfect-round score once (rating table lookup is another heavy invoke).
            if (this.snowApiSuccessScore <= 0)
            {
                if (!this.TryGetSnowRatingSingleScore(SnowSculptureRatingSuccessId, out int score, out string ratingStatus) || score <= 0)
                {
                    this.snowSculptureLastActionStatus = "rating score unavailable";
                    this.SnowSculptureLogStatus("rating unavailable: " + ratingStatus);
                    this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
                    return;
                }

                this.snowApiSuccessScore = score;
                this.SnowSculptureLogStatus("rating resolved score=" + score + " (" + ratingStatus + ")");
            }

            // Pure-API QTE: cheap per-round ReportSculptingScore with cached target + score.
            if (this.TrySendSnowReportScore(this.snowApiTargetNetId, this.snowApiSuccessScore, out string scoreStatus))
            {
                this.snowApiRoundCount++;
                this.snowClickCount++;
                this.snowSculptureLastActionStatus =
                    "round " + this.snowApiRoundCount + "/" + SnowSculptureMaxRound;
                this.SnowSculptureLogStatus("round " + this.snowApiRoundCount + "/" + SnowSculptureMaxRound + " " + scoreStatus);
            }
            else
            {
                this.snowSculptureLastActionStatus = "report failed";
                this.SnowSculptureLogStatus("report failed: " + scoreStatus);
                this.snowApiNextAttemptAt = unscaledTime + SnowApiRetryBackoffSeconds;
            }
        }

        private bool TrySendSnowReportScore(uint baseNetId, int score, out string status)
        {
            status = string.Empty;

            if (this.TryInvokeAuraMonoSnowReportScore(baseNetId, score, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            try
            {
                if (this.snowCachedReportScoreMethod != null)
                {
                    this.snowCachedReportScoreMethod.Invoke(null, new object[] { baseNetId, score });
                    status = "ReportSculptingScore(" + baseNetId + "," + score + ")";
                    return true;
                }

                if (this.snowCachedSendCommandMethod != null && this.snowCachedReportCommandType != null)
                {
                    object cmd = Activator.CreateInstance(this.snowCachedReportCommandType);
                    if (cmd == null)
                    {
                        status = "command alloc failed";
                        return false;
                    }

                    this.TrySetFieldValue(this.snowCachedReportCommandType, ref cmd, "BaseNetId", baseNetId);
                    this.TrySetFieldValue(this.snowCachedReportCommandType, ref cmd, "QteScore", score);
                    MethodInfo closed = this.snowCachedSendCommandMethod.MakeGenericMethod(this.snowCachedReportCommandType);
                    object result = closed.Invoke(null, new object[] { cmd, true, this.snowCachedReliableChannel });
                    int code = result is int sendCode ? sendCode : -1;
                    if (code < 0)
                    {
                        status = "SendCommand failed (" + code + ")";
                        return false;
                    }

                    status = "SendCommand score=" + score;
                    return true;
                }
            }
            catch (Exception ex)
            {
                status = "managed report failed: " + ex.Message;
                return false;
            }

            status = "no report path";
            return false;
        }

        private bool TryGatherSnowSculpture(uint baseNetId, out string status)
        {
            status = string.Empty;
            if (!this.SnowSculptureHasGatherPath())
            {
                this.EnsureSnowSculptureTypesResolved(out _);
            }

            if (this.TryInvokeAuraMonoSnowGather(baseNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (this.snowCachedGatherSculptureMethod != null)
            {
                try
                {
                    this.snowCachedGatherSculptureMethod.Invoke(null, new object[] { baseNetId });
                    status = "GatherSnowSculpture(" + baseNetId + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed gather failed: " + ex.Message;
                    return false;
                }
            }

            status = string.IsNullOrEmpty(auraStatus) ? "no gather path" : auraStatus;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoSnowGather(uint baseNetId, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoGatherSculptureMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura gather method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            uint netId = baseNetId;
            args[0] = (IntPtr)(&netId);
            auraMonoRuntimeInvoke(this.snowAuraMonoGatherSculptureMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura GatherSnowSculpture exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura GatherSnowSculpture(" + baseNetId + ")";
            return true;
        }

        private bool TryStopSnowSculpting(uint baseNetId, out string status)
        {
            status = string.Empty;

            if (this.TryInvokeAuraMonoSnowStop(baseNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (this.snowCachedStopSculptingMethod != null)
            {
                try
                {
                    this.snowCachedStopSculptingMethod.Invoke(null, new object[] { baseNetId });
                    status = "StopSculpting(" + baseNetId + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed stop failed: " + ex.Message;
                    return false;
                }
            }

            status = string.IsNullOrEmpty(auraStatus) ? "no stop path" : auraStatus;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoSnowStop(uint baseNetId, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoStopSculptingMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura stop method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            uint netId = baseNetId;
            args[0] = (IntPtr)(&netId);
            auraMonoRuntimeInvoke(this.snowAuraMonoStopSculptingMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura StopSculpting exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura StopSculpting(" + baseNetId + ")";
            return true;
        }

        // Pure-protocol auto-start: resolve the snow base, fill a snowball, then StartSculpting.
        // No ExecuteHasTargetCommand (which is what spams mono icall warnings). The base state is
        // NOT read (that needs an unsafe struct-out component read); instead this fills + starts
        // blindly each tick and RunAutoSnowSculptureApi detects success via TargetNetId polling.
        // The server harmlessly rejects PutSnowBall on a full base and StartSculpting on an
        // unfilled one, so over-sending is safe.
        private bool TryStartSnowSculptureViaApi(out string status)
        {
            status = string.Empty;

            if (!this.TryResolveSnowBaseNetId(out uint baseNetId, out status) || baseNetId == 0)
            {
                return false;
            }

            this.snowApiKnownBaseNetId = baseNetId;

            if (!this.TryResolveBackpackSnowballNetId(out uint snowBallNetId) || snowBallNetId == 0)
            {
                status = "no snowballs in bag (base=" + baseNetId + ")";
                return false;
            }

            this.TryPutSnowBall(baseNetId, snowBallNetId, out string putStatus);
            this.TryStartSnowSculpting(baseNetId, out string startStatus);
            status = "fill+start base=" + baseNetId + " ball=" + snowBallNetId
                     + " [" + putStatus + "; " + startStatus + "]";
            return true;
        }

        private bool TryResolveSnowBaseNetId(out uint baseNetId, out string status)
        {
            baseNetId = 0;
            status = string.Empty;

            // Reuse the base we already sculpted this session (same physical base across cycles).
            if (this.snowApiKnownBaseNetId != 0)
            {
                baseNetId = this.snowApiKnownBaseNetId;
                status = "cached base";
                return true;
            }

            // Next, derive it from the interact target the player is standing at: read the
            // current/focus/selected level objects (AuraMono field reads -> no icall) and map the
            // level object id to its owner entity (the snow base) net id. Cheap when adjacent.
            List<ulong> levelObjects = new List<ulong>(4);
            if (this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(levelObjects, out _) && levelObjects.Count > 0)
            {
                this.ResolveAuraFarmRuntimeMethods();
                for (int i = 0; i < levelObjects.Count; i++)
                {
                    ulong levelObjectId = levelObjects[i];

                    // Managed resolver is dead on this build (game types not visible); the AuraMono
                    // EntityHelper.GetLevelObjectOwner path is the one that works here.
                    if ((this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectId, out uint ownerId)
                         || this.TryResolveOwnerIdFromLevelObjectId(levelObjectId, out ownerId))
                        && ownerId != 0)
                    {
                        baseNetId = ownerId;
                        status = "owner of levelObject " + levelObjectId;
                        return true;
                    }
                }
            }

            // Not adjacent: scan loaded entities within SnowApiBaseScanRadius for a snow base.
            return this.TryResolveSnowBaseNetIdByScan(out baseNetId, out status);
        }

        private bool TryResolveSnowBaseNetIdByScan(out uint baseNetId, out string status)
        {
            baseNetId = 0;
            status = string.Empty;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "scan: aura not ready";
                return false;
            }

            if (!this.TryGetAuraSnowSelfPlayerPosition(out Vector3 playerPos))
            {
                status = "scan: no player position";
                return false;
            }

            if (!this.EnsureSnowBaseScanMethods(out string resolveStatus))
            {
                status = "scan: " + resolveStatus;
                return false;
            }

            int previousCap = this.auraMonoEntityEnumerationCapOverride;
            this.auraMonoEntityEnumerationCapOverride = SnowApiBaseScanEntityCap;
            List<IntPtr> entities;
            string enumStatus;
            try
            {
                bool ok = this.TryEnumerateAuraMonoLoadedEntityObjects(out entities, out enumStatus);
                if (!ok || entities == null || entities.Count == 0)
                {
                    status = "scan: " + enumStatus;
                    return false;
                }
            }
            finally
            {
                this.auraMonoEntityEnumerationCapOverride = previousCap;
            }

            float bestDistance = SnowApiBaseScanRadius;
            int inRange = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                IntPtr entityObj = entities[i];
                if (entityObj == IntPtr.Zero
                    || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId)
                    || netId == 0U)
                {
                    continue;
                }

                if (!this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 pos))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerPos, pos);
                if (distance > SnowApiBaseScanRadius)
                {
                    continue;
                }

                inRange++;

                // Only the cheap distance filter runs for every entity; the heavier table
                // lookups run for the (few) entities already inside the radius.
                int staticId = this.TryGetSnowEntityResIdAura(netId);
                if (staticId <= 0 || !this.IsSnowBaseStaticIdAura(staticId))
                {
                    continue;
                }

                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    baseNetId = netId;
                }
            }

            if (baseNetId == 0)
            {
                status = "scan: no snow base within " + SnowApiBaseScanRadius + "m (inRange=" + inRange + "/" + entities.Count + ")";
                return false;
            }

            status = "scan base=" + baseNetId + " dist=" + bestDistance.ToString("F1") + "m";
            return true;
        }

        private bool EnsureSnowBaseScanMethods(out string status)
        {
            status = string.Empty;

            if (this.snowAuraMonoEntityUtilClass == IntPtr.Zero)
            {
                this.snowAuraMonoEntityUtilClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
                if (this.snowAuraMonoEntityUtilClass == IntPtr.Zero)
                {
                    this.snowAuraMonoEntityUtilClass = this.FindAuraMonoClassInImages(
                        "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                        "EntityUtil",
                        SnowAuraMonoProtocolImages);
                }
            }

            if (this.snowAuraMonoEntityUtilClass != IntPtr.Zero && this.snowAuraMonoGetEntityResIdMethod == IntPtr.Zero)
            {
                this.snowAuraMonoGetEntityResIdMethod = this.FindAuraMonoMethodOnHierarchy(this.snowAuraMonoEntityUtilClass, "GetEntityResId", 1);
            }

            // TableData class is resolved during the rating path; resolve GetSnowbase on it.
            if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
            {
                this.snowAuraMonoTableDataClass = this.FindAuraMonoTableDataClass();
                if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                {
                    this.snowAuraMonoTableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }
            }

            if (this.snowAuraMonoTableDataClass != IntPtr.Zero && this.snowAuraMonoGetSnowbaseMethod == IntPtr.Zero)
            {
                this.snowAuraMonoGetSnowbaseMethod = this.FindAuraMonoMethodOnHierarchy(this.snowAuraMonoTableDataClass, "GetSnowbase", 2);
                if (this.snowAuraMonoGetSnowbaseMethod == IntPtr.Zero)
                {
                    this.snowAuraMonoGetSnowbaseMethod = this.FindAuraMonoMethodOnHierarchy(this.snowAuraMonoTableDataClass, "GetSnowbase", 1);
                }
            }

            if (this.snowAuraMonoGetEntityResIdMethod == IntPtr.Zero)
            {
                status = "GetEntityResId missing";
                return false;
            }

            if (this.snowAuraMonoGetSnowbaseMethod == IntPtr.Zero)
            {
                status = "GetSnowbase missing";
                return false;
            }

            return true;
        }

        private unsafe int TryGetSnowEntityResIdAura(uint netId)
        {
            if (this.snowAuraMonoGetEntityResIdMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr boxed = auraMonoRuntimeInvoke(this.snowAuraMonoGetEntityResIdMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryUnboxMonoInt32(boxed, out int staticId))
            {
                return staticId;
            }

            ulong raw = this.TryReadMonoUnsignedIntegral(boxed);
            return raw <= int.MaxValue ? (int)raw : 0;
        }

        private unsafe bool IsSnowBaseStaticIdAura(int staticId)
        {
            if (this.snowAuraMonoGetSnowbaseMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            int id = staticId;
            byte needException = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&needException);
            IntPtr snowbaseObj = auraMonoRuntimeInvoke(this.snowAuraMonoGetSnowbaseMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            // Non-null TableSnowbase row => this staticId is a snow base.
            return exc == IntPtr.Zero && snowbaseObj != IntPtr.Zero;
        }

        private unsafe bool TryGetAuraSnowSelfPlayerPosition(out Vector3 position)
        {
            position = Vector3.zero;

            if (this.snowAuraMonoEntityUtilClass == IntPtr.Zero)
            {
                this.snowAuraMonoEntityUtilClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
            }

            if (this.snowAuraMonoEntityUtilClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getSelfPlayer = this.FindAuraMonoMethodOnHierarchy(this.snowAuraMonoEntityUtilClass, "GetSelfPlayer", 0);
            if (getSelfPlayer == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(getSelfPlayer, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetAuraMonoObjectPosition(playerObj, out position) && position != Vector3.zero)
            {
                return true;
            }

            // Player component may expose its world position via its entity.
            if (this.TryGetMonoObjectMember(playerObj, "entity", out IntPtr entityObj) && entityObj != IntPtr.Zero
                && this.TryGetAuraMonoObjectPosition(entityObj, out position) && position != Vector3.zero)
            {
                return true;
            }

            return false;
        }

        private unsafe bool TryResolveBackpackSnowballNetId(out uint snowBallNetId)
        {
            snowBallNetId = 0;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            // Re-resolve the BackPackSystem object every call (do not cache a managed-object
            // pointer across frames — the GC can move it). Only the method descriptor is cached.
            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                || backPackSystemObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.snowAuraMonoGetItemNetIdMethod == IntPtr.Zero)
            {
                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                this.snowAuraMonoGetItemNetIdMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetItemNetId", 1);
                if (this.snowAuraMonoGetItemNetIdMethod == IntPtr.Zero)
                {
                    return false;
                }
            }

            IntPtr exc = IntPtr.Zero;
            int staticId = SnowballStaticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&staticId);
            IntPtr boxed = auraMonoRuntimeInvoke(this.snowAuraMonoGetItemNetIdMethod, backPackSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryUnboxMonoUInt32(boxed, out snowBallNetId) && snowBallNetId != 0)
            {
                return true;
            }

            ulong raw = this.TryReadMonoUnsignedIntegral(boxed);
            snowBallNetId = raw <= uint.MaxValue ? (uint)raw : 0;
            return snowBallNetId != 0;
        }

        private bool TryStartSnowSculpting(uint baseNetId, out string status)
        {
            status = string.Empty;

            if (this.TryInvokeAuraMonoSnowStart(baseNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (this.snowCachedStartSculptingMethod != null)
            {
                try
                {
                    this.snowCachedStartSculptingMethod.Invoke(null, new object[] { baseNetId });
                    status = "StartSculpting(" + baseNetId + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed start failed: " + ex.Message;
                    return false;
                }
            }

            status = string.IsNullOrEmpty(auraStatus) ? "no start path" : auraStatus;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoSnowStart(uint baseNetId, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoStartSculptingMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura start method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            uint netId = baseNetId;
            args[0] = (IntPtr)(&netId);
            auraMonoRuntimeInvoke(this.snowAuraMonoStartSculptingMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura StartSculpting exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura StartSculpting(" + baseNetId + ")";
            return true;
        }

        private bool TryPutSnowBall(uint baseNetId, uint snowBallNetId, out string status)
        {
            status = string.Empty;

            if (this.TryInvokeAuraMonoSnowPutBall(baseNetId, snowBallNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (this.snowCachedPutSnowBallMethod != null)
            {
                try
                {
                    this.snowCachedPutSnowBallMethod.Invoke(null, new object[] { baseNetId, snowBallNetId });
                    status = "PutSnowBall(" + baseNetId + "," + snowBallNetId + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    status = "managed putball failed: " + ex.Message;
                    return false;
                }
            }

            status = string.IsNullOrEmpty(auraStatus) ? "no putball path" : auraStatus;
            return false;
        }

        private unsafe bool TryInvokeAuraMonoSnowPutBall(uint baseNetId, uint snowBallNetId, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoPutSnowBallMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura putball method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            uint baseId = baseNetId;
            uint ballId = snowBallNetId;
            args[0] = (IntPtr)(&baseId);
            args[1] = (IntPtr)(&ballId);
            auraMonoRuntimeInvoke(this.snowAuraMonoPutSnowBallMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura PutSnowBall exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura PutSnowBall(" + baseNetId + "," + snowBallNetId + ")";
            return true;
        }

        private unsafe bool TryCollectWarehouseSnowballStacks(out Dictionary<uint, int> map, out string status)
        {
            map = new Dictionary<uint, int>();
            status = string.Empty;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono unavailable";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                || backPackSystemObj == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
            IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
            bool needsStorageType = true;
            if (getAllItemMethod == IntPtr.Zero)
            {
                getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                needsStorageType = false;
            }

            if (getAllItemMethod == IntPtr.Zero)
            {
                status = "GetAllItem missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr itemListObj;
            int storageTypeValue = SnowStorageWarehouse;
            if (needsStorageType)
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&storageTypeValue);
                itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, (IntPtr)args, ref exc);
            }
            else
            {
                itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, IntPtr.Zero, ref exc);
            }

            if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
            {
                status = "warehouse read failed";
                return false;
            }

            List<IntPtr> warehouseItems = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, warehouseItems) || warehouseItems.Count == 0)
            {
                status = "warehouse empty";
                return true;
            }

            int snowStacks = 0;
            int snowQty = 0;
            for (int i = 0; i < warehouseItems.Count; i++)
            {
                IntPtr itemObj = warehouseItems[i];
                if (itemObj == IntPtr.Zero
                    || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                    || netId == 0U)
                {
                    continue;
                }

                if (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                {
                    continue;
                }

                if (!this.TryGetDirectBackpackItemCount(itemObj, out int count) || count <= 0)
                {
                    count = 1;
                }

                if (!this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId) || staticId != SnowballStaticId)
                {
                    continue;
                }

                map[netId] = count;
                snowStacks++;
                snowQty += count;
            }

            status = snowStacks > 0
                ? "warehouse snowballs=" + snowStacks + " qty=" + snowQty
                : "no snowballs in warehouse";
            return true;
        }

        private bool TryMoveSnowballsWarehouseToBackpack(out string status)
        {
            status = string.Empty;
            if (!this.TryCollectWarehouseSnowballStacks(out Dictionary<uint, int> map, out string collectStatus))
            {
                status = collectStatus;
                return false;
            }

            if (map.Count == 0)
            {
                status = collectStatus;
                return false;
            }

            List<uint> keys = new List<uint>(map.Keys);
            int sentStacks = 0;
            int sentQty = 0;
            for (int offset = 0; offset < keys.Count; offset += TransferBatchMaxCount)
            {
                Dictionary<uint, int> chunk = new Dictionary<uint, int>();
                int end = Math.Min(keys.Count, offset + TransferBatchMaxCount);
                for (int i = offset; i < end; i++)
                {
                    uint netId = keys[i];
                    chunk[netId] = map[netId];
                }

                if (!this.TrySendTransferBatch(chunk, SnowStorageWarehouse, out string error))
                {
                    status = string.IsNullOrEmpty(error)
                        ? "MoveBatchBackpackItems failed"
                        : error + (sentStacks > 0 ? " (after " + sentStacks + " stack(s))" : string.Empty);
                    return false;
                }

                sentStacks += chunk.Count;
                foreach (int qty in chunk.Values)
                {
                    sentQty += qty;
                }
            }

            status = "Moved " + sentStacks + " snowball stack(s), qty " + sentQty + " -> Bag";
            return true;
        }

        private bool TryGetSnowSculptureTargetNetId(out uint targetNetId, out string status)
        {
            targetNetId = 0;
            status = "no player";

            if (this.TryGetSnowSculptureTargetNetIdFromAuraPanel(out targetNetId, out string panelStatus))
            {
                status = panelStatus;
                return true;
            }

            if (this.TryGetSnowSculptureTargetNetIdAura(out targetNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            // Keep the (informative) AuraMono failure reason as the headline; the managed
            // path below is a dead last resort on Il2Cpp/AuraMono builds where game types
            // are not visible to FindLoadedType.
            if (!string.IsNullOrEmpty(auraStatus))
            {
                status = auraStatus;
            }

            try
            {
                Type entityUtilType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                    "EntityUtil");
                if (entityUtilType == null)
                {
                    if (string.IsNullOrEmpty(status) || status == "no player")
                    {
                        status = "EntityUtil null";
                    }

                    return false;
                }

                MethodInfo getSelf = entityUtilType.GetMethod(
                    "GetSelfPlayer",
                    BindingFlags.Public | BindingFlags.Static);
                if (getSelf == null)
                {
                    status = "GetSelfPlayer null";
                    return false;
                }

                object player = getSelf.Invoke(null, null);
                if (player == null)
                {
                    status = "self player null";
                    return false;
                }

                object snowStatus = this.TryReadMember(player, "Status", "SnowSculptureStatus");
                if (snowStatus == null)
                {
                    status = "SnowSculptureStatus null";
                    return false;
                }

                object targetObj = this.TryReadMember(snowStatus, "TargetNetId");
                if (targetObj == null)
                {
                    status = "TargetNetId null";
                    return false;
                }

                targetNetId = Convert.ToUInt32(targetObj);
                if (targetNetId == 0)
                {
                    status = "TargetNetId zero";
                    return false;
                }

                status = "ok";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetActiveSnowSculpturePanelAura(out IntPtr panelObj, out string status)
        {
            panelObj = IntPtr.Zero;
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "aura not ready";
                return false;
            }

            if (this.snowAuraMonoPanelClass == IntPtr.Zero)
            {
                this.TryResolveSnowSculptureAuraMono(out status);
            }

            if (this.snowAuraMonoPanelClass == IntPtr.Zero)
            {
                status = "aura panel class missing";
                return false;
            }

            if (!this.ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out IntPtr uiManagerClass))
            {
                status = "aura ui manager missing";
                return false;
            }

            // Build the System.Type from the already-resolved panel class pointer. This avoids
            // TryCreateAuraMonoSystemTypeObject's name-based fallback (mono Type.GetType), which
            // logs an icall.c warning every call when the class isn't resolvable by full name —
            // spamming the log at the interact-loop rate.
            IntPtr panelTypeObj = IntPtr.Zero;
            if (auraMonoClassGetType != null && auraMonoTypeGetObject != null && this.auraMonoRootDomain != IntPtr.Zero)
            {
                IntPtr monoType = auraMonoClassGetType(this.snowAuraMonoPanelClass);
                if (monoType != IntPtr.Zero)
                {
                    panelTypeObj = auraMonoTypeGetObject(this.auraMonoRootDomain, monoType);
                }
            }

            if (panelTypeObj == IntPtr.Zero
                && (!this.TryCreateAuraMonoSystemTypeObject("XDTGame.UI.Panel.SnowSculpturePanel", out panelTypeObj)
                    || panelTypeObj == IntPtr.Zero))
            {
                status = "aura panel Type missing";
                return false;
            }

            IntPtr getView = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "GetView", 1);
            if (getView == IntPtr.Zero)
            {
                status = "aura GetView missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = panelTypeObj;
            panelObj = auraMonoRuntimeInvoke(getView, uiManagerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || panelObj == IntPtr.Zero)
            {
                status = "aura panel not open";
                return false;
            }

            status = "aura GetView";
            return true;
        }

        private bool TryGetSnowSculptureTargetNetIdFromAuraPanel(out uint targetNetId, out string status)
        {
            targetNetId = 0;
            status = string.Empty;
            if (!this.TryGetActiveSnowSculpturePanelAura(out IntPtr panelObj, out status))
            {
                return false;
            }

            targetNetId = this.TryReadAuraMonoUIntField(panelObj, "_targetNetId", "targetNetId");
            if (targetNetId == 0)
            {
                status = "aura panel _targetNetId zero";
                return false;
            }

            status = "aura panel _targetNetId";
            return true;
        }

        private unsafe bool TryGetSnowSculptureTargetNetIdAura(out uint targetNetId, out string status)
        {
            targetNetId = 0;
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "aura not ready";
                return false;
            }

            IntPtr entityUtilClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
            if (entityUtilClass == IntPtr.Zero)
            {
                entityUtilClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                    "EntityUtil",
                    SnowAuraMonoProtocolImages);
            }

            if (entityUtilClass == IntPtr.Zero)
            {
                status = "aura EntityUtil missing";
                return false;
            }

            IntPtr getSelfPlayer = this.FindAuraMonoMethodOnHierarchy(entityUtilClass, "GetSelfPlayer", 0);
            if (getSelfPlayer == IntPtr.Zero)
            {
                status = "aura GetSelfPlayer missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(getSelfPlayer, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                status = "aura self player missing";
                return false;
            }

            IntPtr playerClass = auraMonoObjectGetClass(playerObj);
            IntPtr getStatus = this.FindAuraMonoMethodOnHierarchy(playerClass, "get_Status", 0);
            if (getStatus == IntPtr.Zero)
            {
                status = "aura get_Status missing";
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr statusObj = auraMonoRuntimeInvoke(getStatus, playerObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || statusObj == IntPtr.Zero)
            {
                status = "aura Status missing";
                return false;
            }

            IntPtr statusClass = auraMonoObjectGetClass(statusObj);
            IntPtr getSnowStatus = this.FindAuraMonoMethodOnHierarchy(statusClass, "get_SnowSculptureStatus", 0);
            if (getSnowStatus == IntPtr.Zero)
            {
                status = "aura get_SnowSculptureStatus missing";
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr snowStatusObj = auraMonoRuntimeInvoke(getSnowStatus, statusObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || snowStatusObj == IntPtr.Zero)
            {
                status = "aura SnowSculptureStatus missing";
                return false;
            }

            IntPtr snowStatusClass = auraMonoObjectGetClass(snowStatusObj);
            IntPtr getTarget = this.FindAuraMonoMethodOnHierarchy(snowStatusClass, "get_TargetNetId", 0);
            if (getTarget == IntPtr.Zero)
            {
                status = "aura get_TargetNetId missing";
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr boxedTarget = auraMonoRuntimeInvoke(getTarget, snowStatusObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxedTarget == IntPtr.Zero || !this.TryUnboxMonoUInt32(boxedTarget, out targetNetId))
            {
                status = "aura TargetNetId unbox failed";
                return false;
            }

            if (targetNetId == 0)
            {
                status = "aura TargetNetId zero";
                return false;
            }

            status = "aura player TargetNetId";
            return true;
        }

        private int TryReadAuraMonoIntFieldOnObject(IntPtr obj, params string[] fieldNames)
        {
            if (!this.TryReadAuraMonoObjectField(obj, out IntPtr boxed, fieldNames) || boxed == IntPtr.Zero)
            {
                return -1;
            }

            if (this.TryUnboxMonoInt32(boxed, out int value))
            {
                return value;
            }

            ulong fallback = this.TryReadMonoUnsignedIntegral(boxed);
            return fallback <= int.MaxValue ? (int)fallback : -1;
        }

        private bool TryGetSnowRatingSingleScore(int ratingId, out int score, out string status)
        {
            score = 0;
            if (this.TryGetSnowRatingSingleScoreAura(ratingId, out score, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            status = auraStatus;
            try
            {
                if (this.snowCachedGetSnowSculptingRatingMethod == null)
                {
                    return false;
                }

                // GetSnowSculptingRating(int id, bool needException = false)
                object rating = this.snowCachedGetSnowSculptingRatingMethod.GetParameters().Length >= 2
                    ? this.snowCachedGetSnowSculptingRatingMethod.Invoke(null, new object[] { ratingId, false })
                    : this.snowCachedGetSnowSculptingRatingMethod.Invoke(null, new object[] { ratingId });
                if (rating == null)
                {
                    status = "managed rating null";
                    return false;
                }

                PropertyInfo single = rating.GetType().GetProperty("singleScore");
                if (single == null)
                {
                    status = "managed singleScore prop missing";
                    return false;
                }

                score = Convert.ToInt32(single.GetValue(rating));
                status = "managed singleScore=" + score;
                return score > 0;
            }
            catch (Exception ex)
            {
                status = "managed rating ex: " + ex.Message + " | " + auraStatus;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoSnowReportScore(uint baseNetId, int score, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoReportScoreMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura report method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            uint netId = baseNetId;
            int qteScore = score;
            args[0] = (IntPtr)(&netId);
            args[1] = (IntPtr)(&qteScore);
            auraMonoRuntimeInvoke(this.snowAuraMonoReportScoreMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura ReportSculptingScore exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura ReportSculptingScore(" + baseNetId + "," + score + ")";
            return true;
        }

        private unsafe bool TryGetSnowRatingSingleScoreAura(int ratingId, out int score, out string status)
        {
            score = 0;
            status = string.Empty;
            if (this.snowAuraMonoGetSnowSculptingRatingMethod == IntPtr.Zero)
            {
                status = "aura rating method missing (table=0x"
                         + this.snowAuraMonoTableDataClass.ToInt64().ToString("X") + ")";
                return false;
            }

            if (auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                status = "aura invoke/unbox unbound";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int id = ratingId;
                byte needException = 0; // mono bool == 1 byte
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                IntPtr ratingObj = auraMonoRuntimeInvoke(
                    this.snowAuraMonoGetSnowSculptingRatingMethod,
                    IntPtr.Zero,
                    (IntPtr)args,
                    ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "aura GetSnowSculptingRating exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                if (ratingObj == IntPtr.Zero || auraMonoObjectGetClass == null)
                {
                    status = "aura rating obj null";
                    return false;
                }

                // singleScore is an int property backed by a byte field (_singleScore); read it
                // through the getter to avoid mis-sizing the 1-byte field as a 4-byte uint.
                IntPtr ratingClass = auraMonoObjectGetClass(ratingObj);
                if (ratingClass != IntPtr.Zero)
                {
                    IntPtr getter = this.FindAuraMonoMethodOnHierarchy(ratingClass, "get_singleScore", 0);
                    if (getter != IntPtr.Zero)
                    {
                        exc = IntPtr.Zero;
                        IntPtr boxedScore = auraMonoRuntimeInvoke(getter, ratingObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && boxedScore != IntPtr.Zero)
                        {
                            if (this.TryUnboxMonoInt32(boxedScore, out score) && score > 0)
                            {
                                status = "aura getter singleScore=" + score;
                                return true;
                            }

                            ulong raw = this.TryReadMonoUnsignedIntegral(boxedScore);
                            score = raw <= int.MaxValue ? (int)raw : 0;
                            if (score > 0)
                            {
                                status = "aura getter raw singleScore=" + score;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        status = "aura get_singleScore missing";
                    }
                }

                // Fallback: read the backing byte field directly (low byte only).
                uint fieldScore = this.TryReadAuraMonoUIntField(ratingObj, "_singleScore", "singleScore") & 0xFF;
                if (fieldScore > 0)
                {
                    score = (int)fieldScore;
                    status = "aura field _singleScore=" + score;
                    return true;
                }

                if (string.IsNullOrEmpty(status))
                {
                    status = "aura rating score=0";
                }

                return false;
            }
            catch (Exception ex)
            {
                status = "aura rating ex: " + ex.Message;
                return false;
            }
        }

        private object TryReadMember(object target, params string[] names)
        {
            if (target == null || names == null || names.Length == 0)
            {
                return null;
            }

            object current = target;
            for (int i = 0; i < names.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                Type type = current.GetType();
                PropertyInfo prop = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }

                FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }

                return null;
            }

            return current;
        }
    }
}
