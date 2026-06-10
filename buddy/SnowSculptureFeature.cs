using System;
using System.Collections;
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
        private const int SnowSculpturePanelStateRound = 1;
        private const int SnowSculptureRatingSuccessId = 2;
        private const int SnowSculptureRatingFailId = 1;

        private const int SnowInteractPutBall = 14;
        private const int SnowInteractStartSculpt = 15;
        private const int SnowInteractGatherSculpt = 16;

        private const int SnowStorageWarehouse = 2;
        private const int SnowballStaticId = 5100;

        private static readonly int[] SnowInteractCommandPriority =
        {
            SnowInteractStartSculpt,
            SnowInteractPutBall,
            SnowInteractGatherSculpt
        };

        private bool autoSnowEnabled = false;
        private float snowClickInterval = 0.02f;
        private float lastSnowClickTime = 0f;
        private int snowClickCount = 0;
        private KeyCode autoSnowHotkey = KeyCode.None;
        private bool isListeningForAutoSnowHotkey = false;

        private bool autoSculptureIconRapidEnabled = false;
        private float sculptIconClickInterval = 0.05f;
        private float lastSculptIconClickTime = 0f;

        private string snowSculptureResolveStatus = "not resolved";
        private float snowSculptureNextResolveAt;
        private string snowSculptureLastActionStatus = string.Empty;
        private string snowMoveSnowballsStatus = string.Empty;

        private MethodInfo snowCachedReportScoreMethod;
        private Type snowCachedReportCommandType;
        private MethodInfo snowCachedSendCommandMethod;
        private object snowCachedReliableChannel;
        private Type snowCachedPanelType;
        private MethodInfo snowCachedOnPressDownMethod;
        private FieldInfo snowCachedLightButtonsField;
        private FieldInfo snowCachedStateField;
        private MethodInfo snowCachedGetSnowSculptingRatingMethod;

        private IntPtr snowAuraMonoProtocolClass;
        private IntPtr snowAuraMonoReportScoreMethod;
        private IntPtr snowAuraMonoTableDataClass;
        private IntPtr snowAuraMonoGetSnowSculptingRatingMethod;
        private IntPtr snowAuraMonoPanelClass;
        private IntPtr snowAuraMonoExecuteHasTargetMethod;
        private IntPtr snowAuraMonoConfirmExecuteMethod;

        private Type snowCachedInteractSystemType;
        private MethodInfo snowCachedConfirmExecuteMethod;
        private MethodInfo snowCachedPlayerInteractionExecuteMethod;

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

        private bool SnowSculptureHasInteractPath()
        {
            return this.snowCachedPlayerInteractionExecuteMethod != null
                   || this.snowAuraMonoExecuteHasTargetMethod != IntPtr.Zero;
        }

        private bool SnowSculptureIsResolved()
        {
            return this.SnowSculptureHasReportPath()
                   || this.snowCachedOnPressDownMethod != null;
        }

        private void ProcessSnowSculptureOnUpdate()
        {
            if (this.autoSnowEnabled)
            {
                this.RunAutoSnowSculptureApi();
            }

            if (this.autoSculptureIconRapidEnabled)
            {
                this.RunSculptIconRapidApi();
            }
        }

        private void SnowSculptureLog(string message)
        {
            if (!MasterLogSnowSculpture)
            {
                return;
            }

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

                this.snowCachedPanelType = this.FindLoadedType(
                    "XDTGame.UI.Panel.SnowSculpturePanel",
                    "Il2CppXDTGame.UI.Panel.SnowSculpturePanel",
                    "SnowSculpturePanel");

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
                }

                if (this.snowCachedPanelType != null)
                {
                    this.snowCachedOnPressDownMethod = this.snowCachedPanelType.GetMethod(
                        "OnPressDown",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    this.snowCachedLightButtonsField = this.snowCachedPanelType.GetField(
                        "_lightButtons",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    this.snowCachedStateField = this.snowCachedPanelType.GetField(
                        "_state",
                        BindingFlags.NonPublic | BindingFlags.Instance);
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

                this.snowCachedInteractSystemType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.InteractSystem.InteractSystem",
                    "InteractSystem");
                if (this.snowCachedInteractSystemType != null)
                {
                    this.snowCachedConfirmExecuteMethod = this.snowCachedInteractSystemType.GetMethod(
                        "ConfirmExecuteHasTargetCommand",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(ulong), typeof(int) },
                        null);
                }

                Type playerInteractionType = this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.PlayerInteraction",
                    "PlayerInteraction");
                if (playerInteractionType != null)
                {
                    this.snowCachedPlayerInteractionExecuteMethod = playerInteractionType.GetMethod(
                        "ExecuteHasTargetCommand",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(ulong), typeof(int) },
                        null);
                }

                string auraStatus;
                this.TryResolveSnowSculptureAuraMono(out auraStatus);

                bool ok = this.SnowSculptureIsResolved();
                status = ok
                    ? "resolved managedReport=" + (this.snowCachedReportScoreMethod != null)
                      + " auraReport=" + (this.snowAuraMonoReportScoreMethod != IntPtr.Zero)
                      + " panel=" + (this.snowCachedOnPressDownMethod != null)
                    : "unavailable managedReport=" + (this.snowCachedReportScoreMethod != null)
                      + " managedPanel=" + (this.snowCachedPanelType != null)
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

                }

                if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                {
                    this.snowAuraMonoTableDataClass = this.FindAuraMonoClassByFullName("EcsClient.TableData");
                    if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                    {
                        this.snowAuraMonoTableDataClass = this.FindAuraMonoClassByFullName("TableData");
                    }

                    if (this.snowAuraMonoTableDataClass == IntPtr.Zero)
                    {
                        this.snowAuraMonoTableDataClass = this.FindAuraMonoClassInImages(
                            "EcsClient",
                            "TableData",
                            SnowAuraMonoProtocolImages);
                    }
                }

                if (this.snowAuraMonoTableDataClass != IntPtr.Zero && this.snowAuraMonoGetSnowSculptingRatingMethod == IntPtr.Zero)
                {
                    this.snowAuraMonoGetSnowSculptingRatingMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.snowAuraMonoTableDataClass,
                        "GetSnowSculptingRating",
                        1);
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

                if (this.snowAuraMonoExecuteHasTargetMethod == IntPtr.Zero)
                {
                    IntPtr playerInteractionClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.Gameplay.PlayerInteraction");
                    if (playerInteractionClass == IntPtr.Zero)
                    {
                        playerInteractionClass = this.FindAuraMonoClassInImages(
                            "XDTLevelAndEntity.Gameplay",
                            "PlayerInteraction",
                            SnowAuraMonoProtocolImages);
                    }

                    if (playerInteractionClass != IntPtr.Zero)
                    {
                        this.snowAuraMonoExecuteHasTargetMethod = this.FindAuraMonoMethodOnHierarchy(
                            playerInteractionClass,
                            "ExecuteHasTargetCommand",
                            2);
                    }
                }

                if (this.snowAuraMonoConfirmExecuteMethod == IntPtr.Zero)
                {
                    IntPtr interactClass = this.auraMonoInteractSystemClassPtr;
                    if (interactClass == IntPtr.Zero)
                    {
                        interactClass = this.FindAuraMonoClassByFullName(
                            "XDTLevelAndEntity.BaseSystem.InteractSystem.InteractSystem");
                    }

                    if (interactClass != IntPtr.Zero)
                    {
                        this.snowAuraMonoConfirmExecuteMethod = this.FindAuraMonoMethodOnHierarchy(
                            interactClass,
                            "ConfirmExecuteHasTargetCommand",
                            2);
                    }
                }

                auraStatus = "protocolClass=0x" + this.snowAuraMonoProtocolClass.ToInt64().ToString("X")
                             + " report=0x" + this.snowAuraMonoReportScoreMethod.ToInt64().ToString("X")
                             + " table=0x" + this.snowAuraMonoTableDataClass.ToInt64().ToString("X")
                             + " panelClass=0x" + this.snowAuraMonoPanelClass.ToInt64().ToString("X")
                             + " interact=0x" + this.snowAuraMonoExecuteHasTargetMethod.ToInt64().ToString("X");
                return this.snowAuraMonoReportScoreMethod != IntPtr.Zero
                       || this.snowAuraMonoExecuteHasTargetMethod != IntPtr.Zero;
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
            if (unscaledTime - this.lastSnowClickTime < this.snowClickInterval)
            {
                return;
            }

            this.lastSnowClickTime = unscaledTime;

            if (this.TryAdvanceSnowQteViaPanel(out string panelStatus))
            {
                this.snowClickCount++;
                this.snowSculptureLastActionStatus = panelStatus;
                this.SnowSculptureLog("panel: " + panelStatus);
                return;
            }

            if (this.TryReportSnowRoundScore(true, out string scoreStatus))
            {
                this.snowClickCount++;
                this.snowSculptureLastActionStatus = scoreStatus;
                this.SnowSculptureLog("score: " + scoreStatus);
            }
            else
            {
                this.snowSculptureLastActionStatus = scoreStatus;
            }
        }

        private void RunSculptIconRapidApi()
        {
            float unscaled = Time.unscaledTime;
            if (unscaled - this.lastSculptIconClickTime < this.sculptIconClickInterval)
            {
                return;
            }

            this.lastSculptIconClickTime = unscaled;

            if (this.TryExecuteSnowSculptureInteractViaApi(out string status))
            {
                this.snowSculptureLastActionStatus = status;
                this.SnowSculptureLog("interact: " + status);
            }
        }

        private bool TryAdvanceSnowQteViaPanel(out string status)
        {
            status = string.Empty;
            if (!this.EnsureSnowSculptureTypesResolved(out status))
            {
                return false;
            }

            if (this.TryAdvanceSnowQteViaAuraPanel(out status))
            {
                return true;
            }

            if (!this.TryGetActiveSnowSculpturePanel(out object panel, out status))
            {
                return false;
            }

            if (this.snowCachedStateField != null)
            {
                object state = this.snowCachedStateField.GetValue(panel);
                if (state != null && Convert.ToInt32(state) != SnowSculpturePanelStateRound)
                {
                    status = "panel not in Round state";
                    return false;
                }
            }

            if (this.snowCachedLightButtonsField == null || this.snowCachedOnPressDownMethod == null)
            {
                status = "panel reflection missing";
                return false;
            }

            object lightListObj = this.snowCachedLightButtonsField.GetValue(panel);
            IEnumerable lightIds = lightListObj as IEnumerable;
            if (lightIds == null)
            {
                status = "no light buttons";
                return false;
            }

            int pressed = 0;
            foreach (object idObj in lightIds)
            {
                int id = Convert.ToInt32(idObj);
                this.snowCachedOnPressDownMethod.Invoke(panel, new object[] { id });
                pressed++;
            }

            if (pressed == 0)
            {
                status = "empty light list";
                return false;
            }

            status = "OnPressDown x" + pressed;
            return true;
        }

        private bool TryReportSnowRoundScore(bool success, out string status)
        {
            status = string.Empty;
            if (!this.EnsureSnowSculptureTypesResolved(out status))
            {
                return false;
            }

            if (!this.TryGetSnowSculptureTargetNetId(out uint baseNetId, out status))
            {
                return false;
            }

            int ratingId = success ? SnowSculptureRatingSuccessId : SnowSculptureRatingFailId;
            if (!this.TryGetSnowRatingSingleScore(ratingId, out int score))
            {
                status = "rating score unavailable";
                return false;
            }

            if (this.TryInvokeAuraMonoSnowReportScore(baseNetId, score, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

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

            status = "no report path";
            return false;
        }

        private bool TryExecuteSnowSculptureInteractViaApi(out string status)
        {
            status = string.Empty;
            this.EnsureSnowSculptureTypesResolved(out _);
            if (!this.SnowSculptureHasInteractPath())
            {
                status = "ExecuteHasTarget unavailable";
                return false;
            }

            if (this.TryGetActiveSnowSculpturePanel(out _, out _)
                || this.TryGetActiveSnowSculpturePanelAura(out _, out _))
            {
                status = "panel open";
                return false;
            }

            List<ulong> targets = new List<ulong>(4);
            if (!this.TryCollectSnowInteractTargets(targets, out status) || targets.Count == 0)
            {
                status = string.IsNullOrEmpty(status) ? "no interact targets" : status;
                return false;
            }

            for (int t = 0; t < targets.Count; t++)
            {
                ulong levelObjectId = targets[t];
                for (int c = 0; c < SnowInteractCommandPriority.Length; c++)
                {
                    int commandId = SnowInteractCommandPriority[c];
                    if (!this.TryConfirmSnowInteractCommand(levelObjectId, commandId, out int confirmCode)
                        || confirmCode != 0)
                    {
                        continue;
                    }

                    if (this.TryInvokeSnowInteractCommand(levelObjectId, commandId, out status))
                    {
                        status = "ExecuteHasTarget(" + levelObjectId + "," + commandId + ")";
                        return true;
                    }
                }
            }

            status = "no valid snow interact";
            return false;
        }

        private bool TryCollectSnowInteractTargets(List<ulong> targets, out string status)
        {
            status = string.Empty;
            if (targets == null)
            {
                status = "target list null";
                return false;
            }

            targets.Clear();
            if (this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(targets, out status) && targets.Count > 0)
            {
                return true;
            }

            return this.TryGetCurrentInteractTargetLevelObjectsViaStaticHelper(targets, out status) && targets.Count > 0;
        }

        private bool TryConfirmSnowInteractCommand(ulong levelObjectId, int commandId, out int confirmCode)
        {
            confirmCode = 0;
            if (this.TryConfirmSnowInteractCommandManaged(levelObjectId, commandId, out confirmCode))
            {
                return true;
            }

            return this.TryConfirmSnowInteractCommandAura(levelObjectId, commandId, out confirmCode);
        }

        private bool TryConfirmSnowInteractCommandManaged(ulong levelObjectId, int commandId, out int confirmCode)
        {
            confirmCode = 0;
            if (this.snowCachedConfirmExecuteMethod == null || this.snowCachedInteractSystemType == null)
            {
                return false;
            }

            try
            {
                PropertyInfo instanceProperty = this.snowCachedInteractSystemType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static);
                object interactSystem = instanceProperty?.GetValue(null);
                if (interactSystem == null)
                {
                    return false;
                }

                object result = this.snowCachedConfirmExecuteMethod.Invoke(
                    interactSystem,
                    new object[] { levelObjectId, commandId });
                return this.TryReadInteractConfirmSuccess(result, out confirmCode);
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryConfirmSnowInteractCommandAura(ulong levelObjectId, int commandId, out int confirmCode)
        {
            confirmCode = 0;
            if (this.snowAuraMonoConfirmExecuteMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                ulong targetId = levelObjectId;
                int cmdId = commandId;
                args[0] = (IntPtr)(&targetId);
                args[1] = (IntPtr)(&cmdId);
                IntPtr resultObj = auraMonoRuntimeInvoke(
                    this.snowAuraMonoConfirmExecuteMethod,
                    interactObj,
                    (IntPtr)args,
                    ref exc);
                if (exc != IntPtr.Zero || resultObj == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryReadAuraInteractConfirmSuccess(resultObj, out confirmCode);
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadInteractConfirmSuccess(object result, out int confirmCode)
        {
            confirmCode = 0;
            if (result == null)
            {
                return false;
            }

            object inner = this.TryReadMember(result, "Result") ?? result;
            object errorObj = this.TryReadMember(inner, "errorCode");
            if (errorObj == null)
            {
                errorObj = this.TryReadMember(result, "errorCode");
            }

            int errorCode = errorObj != null ? Convert.ToInt32(errorObj) : -1;
            if (errorCode != 0)
            {
                return false;
            }

            object confirmObj = this.TryReadMember(inner, "confirmCode")
                                ?? this.TryReadMember(result, "confirmCode");
            confirmCode = confirmObj != null ? Convert.ToInt32(confirmObj) : 0;
            return true;
        }

        private bool TryReadAuraInteractConfirmSuccess(IntPtr resultObj, out int confirmCode)
        {
            confirmCode = 0;
            if (resultObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryReadAuraMonoObjectField(resultObj, out IntPtr innerPtr, "Result") && innerPtr != IntPtr.Zero)
            {
                int errorCode = this.TryReadAuraMonoIntFieldOnObject(innerPtr, "errorCode");
                if (errorCode != 0)
                {
                    return false;
                }

                confirmCode = this.TryReadAuraMonoIntFieldOnObject(innerPtr, "confirmCode");
                return true;
            }

            int topError = this.TryReadAuraMonoIntFieldOnObject(resultObj, "errorCode");
            if (topError != 0)
            {
                return false;
            }

            confirmCode = this.TryReadAuraMonoIntFieldOnObject(resultObj, "confirmCode");
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

        private bool TryInvokeSnowInteractCommand(ulong levelObjectId, int commandId, out string status)
        {
            status = string.Empty;
            if (this.snowCachedPlayerInteractionExecuteMethod != null)
            {
                try
                {
                    this.snowCachedPlayerInteractionExecuteMethod.Invoke(null, new object[] { levelObjectId, commandId });
                    status = "managed ExecuteHasTarget";
                    return true;
                }
                catch (Exception ex)
                {
                    status = ex.Message;
                }
            }

            return this.TryInvokeSnowInteractCommandAura(levelObjectId, commandId, out status);
        }

        private unsafe bool TryInvokeSnowInteractCommandAura(ulong levelObjectId, int commandId, out string status)
        {
            status = string.Empty;
            if (this.snowAuraMonoExecuteHasTargetMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura ExecuteHasTarget missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            ulong targetId = levelObjectId;
            int cmdId = commandId;
            args[0] = (IntPtr)(&targetId);
            args[1] = (IntPtr)(&cmdId);
            auraMonoRuntimeInvoke(this.snowAuraMonoExecuteHasTargetMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura ExecuteHasTarget exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura ExecuteHasTarget";
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

            try
            {
                Type entityUtilType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                    "EntityUtil");
                if (entityUtilType == null)
                {
                    status = "EntityUtil null";
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

        private bool TryGetActiveSnowSculpturePanel(out object panelInstance, out string status)
        {
            panelInstance = null;
            status = "panel type unresolved";
            if (this.snowCachedPanelType == null && !this.EnsureSnowSculptureTypesResolved(out status))
            {
                return false;
            }

            Type uiManagerType = this.FindLoadedType("XDTGame.Framework.UI.UIManager", "UIManager");
            if (uiManagerType != null)
            {
                PropertyInfo inst = uiManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object uiManager = inst?.GetValue(null);
                if (uiManager != null)
                {
                    MethodInfo getView = uiManagerType.GetMethod("GetView", BindingFlags.Public | BindingFlags.Instance);
                    if (getView != null && getView.IsGenericMethodDefinition)
                    {
                        MethodInfo closed = getView.MakeGenericMethod(this.snowCachedPanelType);
                        panelInstance = closed.Invoke(uiManager, null);
                        if (panelInstance != null)
                        {
                            status = "GetView";
                            return true;
                        }
                    }
                }
            }

            status = "panel not open";
            return false;
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

            if (!this.TryCreateAuraMonoSystemTypeObject("XDTGame.UI.Panel.SnowSculpturePanel", out IntPtr panelTypeObj)
                || panelTypeObj == IntPtr.Zero)
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

        private unsafe bool TryAdvanceSnowQteViaAuraPanel(out string status)
        {
            status = string.Empty;
            if (!this.TryGetActiveSnowSculpturePanelAura(out IntPtr panelObj, out status))
            {
                return false;
            }

            int state = this.TryReadAuraMonoIntFieldOnObject(panelObj, "_state");
            if (state != SnowSculpturePanelStateRound)
            {
                status = "aura panel state=" + state + " (need Round)";
                return false;
            }

            if (!this.TryReadAuraMonoObjectField(panelObj, out IntPtr lightListObj, "_lightButtons")
                || lightListObj == IntPtr.Zero)
            {
                status = "aura _lightButtons missing";
                return false;
            }

            List<int> lightIds = new List<int>(8);
            if (!this.TryReadAuraMonoListIntItems(lightListObj, lightIds))
            {
                status = "aura empty light list";
                return false;
            }

            IntPtr panelClass = auraMonoObjectGetClass(panelObj);
            IntPtr onPressDown = this.FindAuraMonoMethodOnHierarchy(panelClass, "OnPressDown", 1);
            if (onPressDown == IntPtr.Zero)
            {
                status = "aura OnPressDown missing";
                return false;
            }

            int pressed = 0;
            for (int i = 0; i < lightIds.Count; i++)
            {
                int id = lightIds[i];
                IntPtr exc = IntPtr.Zero;
                IntPtr* pressArgs = stackalloc IntPtr[1];
                pressArgs[0] = (IntPtr)(&id);
                auraMonoRuntimeInvoke(onPressDown, panelObj, (IntPtr)pressArgs, ref exc);
                if (exc == IntPtr.Zero)
                {
                    pressed++;
                }
            }

            if (pressed == 0)
            {
                status = "aura OnPressDown failed";
                return false;
            }

            status = "aura OnPressDown x" + pressed;
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

        private unsafe bool TryReadAuraMonoListIntItems(IntPtr listObj, List<int> output)
        {
            output.Clear();
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            IntPtr getCount = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0);
            IntPtr getItem = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Item", 1);
            if (getCount == IntPtr.Zero || getItem == IntPtr.Zero)
            {
                return false;
            }

            int count = this.GetAuraMonoIntCount(listObj, getCount);
            for (int i = 0; i < count && i < 64; i++)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&i);
                IntPtr boxed = auraMonoRuntimeInvoke(getItem, listObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryUnboxMonoInt32(boxed, out int value))
                {
                    output.Add(value);
                }
            }

            return output.Count > 0;
        }

        private bool TryGetSnowRatingSingleScore(int ratingId, out int score)
        {
            score = 0;
            if (this.TryGetSnowRatingSingleScoreAura(ratingId, out score))
            {
                return true;
            }

            try
            {
                if (this.snowCachedGetSnowSculptingRatingMethod == null)
                {
                    return false;
                }

                object rating = this.snowCachedGetSnowSculptingRatingMethod.Invoke(null, new object[] { ratingId });
                if (rating == null)
                {
                    return false;
                }

                PropertyInfo single = rating.GetType().GetProperty("singleScore");
                if (single == null)
                {
                    return false;
                }

                score = Convert.ToInt32(single.GetValue(rating));
                return score > 0;
            }
            catch
            {
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

        private unsafe bool TryGetSnowRatingSingleScoreAura(int ratingId, out int score)
        {
            score = 0;
            if (this.snowAuraMonoGetSnowSculptingRatingMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                int id = ratingId;
                args[0] = (IntPtr)(&id);
                IntPtr ratingObj = auraMonoRuntimeInvoke(
                    this.snowAuraMonoGetSnowSculptingRatingMethod,
                    IntPtr.Zero,
                    (IntPtr)args,
                    ref exc);
                if (exc != IntPtr.Zero || ratingObj == IntPtr.Zero || auraMonoObjectGetClass == null)
                {
                    return false;
                }

                uint fieldScore = this.TryReadAuraMonoUIntField(ratingObj, "_singleScore", "singleScore");
                if (fieldScore > 0)
                {
                    score = (int)fieldScore;
                    return true;
                }

                IntPtr ratingClass = auraMonoObjectGetClass(ratingObj);
                if (ratingClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getter = this.FindAuraMonoMethodOnHierarchy(ratingClass, "get_singleScore", 0);
                if (getter == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr boxedScore = auraMonoRuntimeInvoke(getter, ratingObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxedScore == IntPtr.Zero)
                {
                    return false;
                }

                if (this.TryUnboxMonoInt32(boxedScore, out score) && score > 0)
                {
                    return true;
                }

                ulong raw = this.TryReadMonoUnsignedIntegral(boxedScore);
                score = raw <= int.MaxValue ? (int)raw : 0;
                return score > 0;
            }
            catch
            {
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
