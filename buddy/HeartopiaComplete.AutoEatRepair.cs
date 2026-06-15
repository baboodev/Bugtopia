﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private string GetAutoRepairOptionLabel(int index)
        {
            if (index < 0 || index >= this.autoRepairOptions.Length)
            {
                return string.Empty;
            }

            return this.L(this.autoRepairOptions[index]);
        }

        private string GetAutoEatFoodOptionLabel(int index)
        {
            if (index < 0 || index >= this.autoEatFoodOptions.Length)
            {
                return string.Empty;
            }

            // For custom food, show the saved custom food name
            if (index == this.autoEatFoodOptions.Length - 1 && !string.IsNullOrEmpty(this.autoEatCustomFoodName))
            {
                return "Custom: " + GetFoodDisplayName(this.autoEatCustomFoodName);
            }

            return this.L(this.autoEatFoodOptions[index]);
        }

        private void AutoEatRepairLog(string message)
        {
            if (AutoEatRepairLogsEnabled && !string.IsNullOrEmpty(message))
            {
                ModLogger.Msg(message);
            }
        }

        private void ReportAutoEatRepairSlowRuntime(string label, long startTimestamp)
        {
            if (!AutoEatRepairLogsEnabled)
            {
                return;
            }

            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
            double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs < AutoEatRepairSlowRuntimeWarnMs || Time.unscaledTime < this.nextAutoEatRepairSlowRuntimeLogAt)
            {
                return;
            }

            this.nextAutoEatRepairSlowRuntimeLogAt = Time.unscaledTime + AutoEatRepairSlowRuntimeLogCooldown;
            ModLogger.Msg($"[AutoEatRepairPerf] Slow {label}: {elapsedMs:F1}ms");
        }

        private void TryHandleLiveDurabilityAutoRepair()
        {
            if (!this.autoRepairOnToastEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - this.lastToolDurabilityPollAt < this.GetEffectiveToolDurabilityPollInterval())
            {
                return;
            }

            this.lastToolDurabilityPollAt = now;

            if (!this.IsAutoRepairWorldReady(out string readinessStatus))
            {
                bool statusChanged = !string.Equals(this.lastLoggedAutoRepairNetStatus, readinessStatus ?? string.Empty, StringComparison.Ordinal);
                if (now >= this.nextToolDurabilityLogAt || statusChanged)
                {
                    this.AutoEatRepairLog("[AutoRepair] Live durability waiting: " + readinessStatus);
                    this.lastLoggedAutoRepairNetStatus = readinessStatus ?? string.Empty;
                    this.nextToolDurabilityLogAt = now + ToolDurabilityLogInterval;
                }
                return;
            }

            if (!this.TryGetCurrentToolDurability(out int toolId, out int durability, out int maxDurability, out string status))
            {
                string primaryStatus = status;
                string handholdStatus = "expensive fallback throttled";
                string auraStatus = "expensive fallback throttled";
                bool handholdOk = false;
                bool canUseExpensiveFallback = now >= this.nextAutoRepairExpensiveDurabilityFallbackAt;
                if (canUseExpensiveFallback)
                {
                    this.nextAutoRepairExpensiveDurabilityFallbackAt = now + AutoRepairExpensiveFallbackRetrySeconds;
                    handholdOk = this.TryGetCurrentToolDurabilityViaHandhold(out toolId, out durability, out maxDurability, out handholdStatus);
                }

                if (!handholdOk)
                {
                    bool auraOk = canUseExpensiveFallback
                        && this.TryGetCurrentToolDurabilityViaAuraMono(out toolId, out durability, out maxDurability, out auraStatus);
                    if (!auraOk)
                    {
                        if (canUseExpensiveFallback)
                        {
                            this.nextAutoRepairExpensiveDurabilityFallbackAt = now + AutoRepairExpensiveFallbackMissBackoffSeconds;
                        }

                        string failureSummary = "Unknown";
                        List<string> failures = new List<string>();
                        if (!string.IsNullOrWhiteSpace(primaryStatus))
                        {
                            failures.Add("tool=" + primaryStatus);
                        }
                        if (!string.IsNullOrWhiteSpace(handholdStatus) && !string.Equals(handholdStatus, primaryStatus, StringComparison.Ordinal))
                        {
                            failures.Add("handhold=" + handholdStatus);
                        }
                        if (!string.IsNullOrWhiteSpace(auraStatus)
                            && !string.Equals(auraStatus, primaryStatus, StringComparison.Ordinal)
                            && !string.Equals(auraStatus, handholdStatus, StringComparison.Ordinal))
                        {
                            failures.Add("aura=" + auraStatus);
                        }

                        if (failures.Count > 0)
                        {
                            failureSummary = string.Join(" | ", failures.ToArray());
                        }

                        if (now >= this.nextToolDurabilityLogAt)
                        {
                            this.AutoEatRepairLog("[AutoRepair] Live durability read unavailable: " + failureSummary);
                            this.lastLoggedAutoRepairNetStatus = failureSummary;
                            this.nextToolDurabilityLogAt = now + ToolDurabilityUnavailableLogInterval;
                        }
                        return;
                    }
                }
            }

            string toolName = this.GetAutoRepairSupportedToolName(toolId);
            if (string.IsNullOrEmpty(toolName))
            {
                string idleStatus = toolId > 0
                    ? "Holding unsupported tool (toolId=" + toolId + ")"
                    : "No supported tool equipped";
                bool statusChanged = !string.Equals(this.lastLoggedAutoRepairNetStatus, idleStatus, StringComparison.Ordinal);
                if (now >= this.nextToolDurabilityLogAt || statusChanged)
                {
                    this.AutoEatRepairLog("[AutoRepair] Live durability idle: " + idleStatus);
                    this.lastLoggedAutoRepairNetStatus = idleStatus;
                    this.nextToolDurabilityLogAt = now + ToolDurabilityLogInterval;
                }
                return;
            }

            this.lastLoggedAutoRepairNetStatus = toolName + " Equipped";

            bool changed = toolId != this.lastObservedToolId
                || durability != this.lastObservedToolDurability
                || maxDurability != this.lastObservedToolMaxDurability;
            if (changed || now >= this.nextToolDurabilityLogAt)
            {
                float ratio = (maxDurability > 0) ? ((float)durability / (float)maxDurability) : 0f;
                this.AutoEatRepairLog($"[AutoRepair] Live durability tool={toolName} toolId={toolId} durability={durability}/{maxDurability} ratio={ratio:P1}");
                this.nextToolDurabilityLogAt = now + ToolDurabilityLogInterval;
            }

            this.lastObservedToolId = toolId;
            this.lastObservedToolDurability = durability;
            this.lastObservedToolMaxDurability = maxDurability;

            if (maxDurability <= 0 || now < this.nextLiveDurabilityTriggerAt)
            {
                return;
            }

            float liveDurabilityRatio = (float)durability / (float)maxDurability;
            bool latchedForCurrentTool = this.liveDurabilityLowLatched
                && toolId == this.liveDurabilityLatchedToolId
                && maxDurability == this.liveDurabilityLatchedToolMaxDurability;

            if (!latchedForCurrentTool)
            {
                this.liveDurabilityLowLatched = false;
                this.liveDurabilityLatchedToolId = toolId;
                this.liveDurabilityLatchedToolMaxDurability = maxDurability;
            }

            float repairTriggerRatio = Mathf.Clamp(this.autoRepairTriggerPercent, 1, 100) / 100f;
            float repairResetRatio = Mathf.Clamp01(repairTriggerRatio + 0.05f);

            if (liveDurabilityRatio > repairResetRatio)
            {
                this.liveDurabilityLowLatched = false;
                return;
            }

            if (this.liveDurabilityLowLatched || liveDurabilityRatio > repairTriggerRatio)
            {
                return;
            }

            this.nextLiveDurabilityTriggerAt = now + 1f;
            bool repairTriggered = this.TryHandleDurabilityAutoRepairTrigger($"live durability {toolName} toolId={toolId} ({durability}/{maxDurability}, ratio={liveDurabilityRatio:P1}, threshold={repairTriggerRatio:P0}, reset={repairResetRatio:P0})");
            if (repairTriggered)
            {
                this.liveDurabilityLowLatched = true;
                this.liveDurabilityLatchedToolId = toolId;
                this.liveDurabilityLatchedToolMaxDurability = maxDurability;
            }
            else
            {
                // If repair could not start because another automation/cooldown blocked it,
                // keep polling so a tool stuck at 0% can recover instead of staying latched forever.
                this.liveDurabilityLowLatched = false;
                this.nextLiveDurabilityTriggerAt = now + 0.5f;
            }
        }

        private bool IsAutoRepairWorldReady(out string status)
        {
            status = "world UI unavailable";
            float now = Time.unscaledTime;
            if (now < this.nextAutoRepairWorldReadyProbeAt)
            {
                status = this.cachedAutoRepairWorldReadyStatus;
                return this.cachedAutoRepairWorldReady;
            }

            this.nextAutoRepairWorldReadyProbeAt = now + 3f;
            try
            {
                GameObject loginPanel = GameObject.Find(LOGIN_PANEL_PATH);
                GameObject loginRoomPanel = GameObject.Find(LOGIN_ROOM_PANEL_PATH);
                if ((loginPanel != null && loginPanel.activeInHierarchy)
                    || (loginRoomPanel != null && loginRoomPanel.activeInHierarchy))
                {
                    status = "login UI active";
                    this.cachedAutoRepairWorldReady = false;
                    this.cachedAutoRepairWorldReadyStatus = status;
                    return false;
                }

                GameObject statusPanel = GameObject.Find(STATUS_PANEL_PATH) ?? GameObject.Find("StatusPanel(Clone)");
                if (statusPanel == null || !statusPanel.activeInHierarchy)
                {
                    status = "status panel unavailable";
                    this.cachedAutoRepairWorldReady = false;
                    this.cachedAutoRepairWorldReadyStatus = status;
                    return false;
                }

                status = "world UI ready";
                this.cachedAutoRepairWorldReady = true;
                this.cachedAutoRepairWorldReadyStatus = status;
                return true;
            }
            catch
            {
                status = "world UI probe failed";
                this.cachedAutoRepairWorldReady = false;
                this.cachedAutoRepairWorldReadyStatus = status;
                return false;
            }
        }

        private bool IsAutoRepairPlayerContextReady(out string status)
        {
            status = "player context unavailable";
            float now = Time.unscaledTime;
            if (now < this.nextAutoRepairPlayerContextProbeAt)
            {
                status = this.cachedAutoRepairPlayerContextStatus;
                return this.cachedAutoRepairPlayerContextReady;
            }

            try
            {
                GameObject player = this.GetPlayerObject();
                if (player != null)
                {
                    status = "player context ready";
                    this.cachedAutoRepairPlayerContextReady = true;
                    this.cachedAutoRepairPlayerContextStatus = status;
                    this.nextAutoRepairPlayerContextProbeAt = now + 1f;
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (this.TryGetManagedSelfPlayerObject(out object playerObj, out _) && playerObj != null)
                {
                    status = "player context ready";
                    this.cachedAutoRepairPlayerContextReady = true;
                    this.cachedAutoRepairPlayerContextStatus = status;
                    this.nextAutoRepairPlayerContextProbeAt = now + 1f;
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (this.TryGetManagedInteractSystemObject(out object interactSystemObj, out _)
                    && this.TryGetManagedInteractPlayerObject(interactSystemObj, out object interactPlayerObj, out _)
                    && interactPlayerObj != null)
                {
                    status = "player context ready";
                    this.cachedAutoRepairPlayerContextReady = true;
                    this.cachedAutoRepairPlayerContextStatus = status;
                    this.nextAutoRepairPlayerContextProbeAt = now + 1f;
                    return true;
                }
            }
            catch
            {
            }

            status = "player context unavailable";
            this.cachedAutoRepairPlayerContextReady = false;
            this.cachedAutoRepairPlayerContextStatus = status;
            this.nextAutoRepairPlayerContextProbeAt = now + 2f;
            return false;
        }

        private string GetAutoRepairSupportedToolName(int toolId)
        {
            switch (toolId)
            {
                case 1:
                    return "Axe";
                case 2:
                    return "Sprinkler";
                case 3:
                    return "Rod";
                case 4:
                    return "BirdScanner";
                case 5:
                    return "Net";
                default:
                    return string.Empty;
            }
        }

        private bool TryGetCurrentToolDurability(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = 0;
            durability = 0;
            maxDurability = 0;
            status = "Unknown";

            try
            {
                if (this.TryGetCurrentToolDurabilityViaAuraMonoToolSystem(out toolId, out durability, out maxDurability, out status))
                {
                    return true;
                }

                string auraToolSystemStatus = status;
                if (!string.IsNullOrEmpty(auraToolSystemStatus)
                    && (auraToolSystemStatus.IndexOf("resolve throttled", StringComparison.OrdinalIgnoreCase) >= 0
                        || auraToolSystemStatus.IndexOf("module unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                        || auraToolSystemStatus.IndexOf("API unavailable", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    status = auraToolSystemStatus;
                    return false;
                }

                if (this.TryGetCurrentToolDurabilityViaClientService(out toolId, out durability, out maxDurability, out status))
                {
                    return true;
                }

                string serviceStatus = status;
                if (!this.TryResolveToolDurabilityReflection(out string reflectionStatus))
                {
                    status = !string.IsNullOrWhiteSpace(serviceStatus)
                        ? "toolSystem=" + auraToolSystemStatus + " | service=" + serviceStatus + " | reflection=" + reflectionStatus
                        : reflectionStatus;
                    return false;
                }

                object toolSystemInstance = this.cachedToolSystemInstanceProperty?.GetValue(null, null)
                    ?? this.cachedToolDataModuleInstanceProperty?.GetValue(null, null);
                if (toolSystemInstance == null)
                {
                    status = "ToolSystem instance unavailable";
                    return false;
                }

                object currentTool = this.cachedToolSystemGetCurrentToolMethod?.Invoke(toolSystemInstance, null);
                if (currentTool == null)
                {
                    status = "Current tool unavailable";
                    return false;
                }

                Type currentToolType = currentTool.GetType();
                FieldInfo idField = this.cachedToolIdField;
                FieldInfo durabilityField = this.cachedToolDurabilityField;
                FieldInfo maxDurabilityField = this.cachedToolMaxDurabilityField;
                if (idField == null || idField.DeclaringType != currentToolType)
                {
                    idField = currentToolType.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (durabilityField == null || durabilityField.DeclaringType != currentToolType)
                {
                    durabilityField = currentToolType.GetField("durability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (maxDurabilityField == null || maxDurabilityField.DeclaringType != currentToolType)
                {
                    maxDurabilityField = currentToolType.GetField("maxDurability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (idField == null || durabilityField == null || maxDurabilityField == null)
                {
                    status = "Tool durability fields unavailable";
                    return false;
                }

                this.cachedToolIdField = idField;
                this.cachedToolDurabilityField = durabilityField;
                this.cachedToolMaxDurabilityField = maxDurabilityField;

                toolId = Convert.ToInt32(idField.GetValue(currentTool));
                durability = Convert.ToInt32(durabilityField.GetValue(currentTool));
                maxDurability = Convert.ToInt32(maxDurabilityField.GetValue(currentTool));
                status = "OK";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaClientService(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = 0;
            durability = 0;
            maxDurability = 0;
            status = "Tool service unavailable";

            try
            {
                if (this.cachedToolClientServiceTryGetMethod == null || this.cachedToolClientServiceType == null)
                {
                    float now = Time.unscaledTime;
                    if (now < this.nextToolClientServiceResolveAttemptAt)
                    {
                        status = "Tool service resolve throttled";
                        return false;
                    }
                    this.nextToolClientServiceResolveAttemptAt = now + 8f;

                    Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService")
                        ?? this.FindLoadedEcsServiceType();
                    Type toolServiceType = this.FindLoadedType(
                        "ClientSystem.Tool.IToolService",
                        "ClientSystem.Tool.ToolService",
                        "IToolService",
                        "ToolService")
                        ?? this.FindLoadedToolServiceType();
                    if (ecsServiceType == null || toolServiceType == null)
                    {
                        status = "Tool service types unavailable"
                            + $" (ecs={(ecsServiceType != null ? ecsServiceType.FullName : "null")}, tool={(toolServiceType != null ? toolServiceType.FullName : "null")})";
                        return false;
                    }

                    MethodInfo tryGetMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                    if (tryGetMethod == null)
                    {
                        status = "EcsService.TryGet unavailable";
                        return false;
                    }

                    this.cachedToolClientServiceType = toolServiceType;
                    this.cachedToolClientServiceTryGetMethod = tryGetMethod.MakeGenericMethod(toolServiceType);
                    this.nextToolClientServiceResolveAttemptAt = -999f;
                }

                object[] serviceArgs = new object[] { null, false };
                object serviceResult = this.cachedToolClientServiceTryGetMethod.Invoke(null, serviceArgs);
                if (!(serviceResult is bool) || !(bool)serviceResult || serviceArgs[0] == null)
                {
                    status = "IToolService unavailable";
                    return false;
                }

                object toolService = serviceArgs[0];
                Type toolServiceRuntimeType = toolService.GetType();
                if (this.cachedTryGetTakenToolMethod == null || this.cachedTryGetTakenToolMethod.DeclaringType != toolServiceRuntimeType)
                {
                    this.cachedTryGetTakenToolMethod = toolServiceRuntimeType.GetMethod("TryGetTakenTool", BindingFlags.Public | BindingFlags.Instance);
                    this.cachedTryGetToolComponentMethod = toolServiceRuntimeType.GetMethod("TryGetToolComponent", BindingFlags.Public | BindingFlags.Instance);
                    this.cachedGetToolDurabilityMethod = toolServiceRuntimeType.GetMethod("GetToolDurability", BindingFlags.Public | BindingFlags.Instance);
                    this.cachedGetToolDurabilityUpperLimitMethod = toolServiceRuntimeType.GetMethod("GetToolDurabilityUpperLimit", BindingFlags.Public | BindingFlags.Instance);
                }

                if (this.cachedTryGetTakenToolMethod == null)
                {
                    status = "Tool service methods unavailable";
                    return false;
                }

                object[] takenToolArgs = new object[] { null };
                object takenToolResult = this.cachedTryGetTakenToolMethod.Invoke(toolService, takenToolArgs);
                if (!(takenToolResult is bool) || !(bool)takenToolResult || takenToolArgs[0] == null)
                {
                    status = "Taken tool unavailable";
                    return false;
                }

                object takenTool = takenToolArgs[0];
                Type takenToolType = takenTool.GetType();
                if (this.cachedTakenToolItem1Field == null || this.cachedTakenToolItem1Field.DeclaringType != takenToolType)
                {
                    this.cachedTakenToolItem1Field = takenToolType.GetField("Item1");
                }
                if (this.cachedTakenToolItem1Field == null)
                {
                    status = "Taken tool tuple unreadable";
                    return false;
                }

                object toolTypeValue = this.cachedTakenToolItem1Field.GetValue(takenTool);
                toolId = Convert.ToInt32(toolTypeValue);
                if (toolId <= 0)
                {
                    status = "Taken tool id unavailable";
                    return false;
                }

                if (this.cachedGetToolDurabilityMethod != null && this.cachedGetToolDurabilityUpperLimitMethod != null)
                {
                    try
                    {
                        object directDurability = this.cachedGetToolDurabilityMethod.Invoke(toolService, new object[] { toolTypeValue });
                        object directMaxDurability = this.cachedGetToolDurabilityUpperLimitMethod.Invoke(toolService, new object[] { toolTypeValue });
                        durability = Convert.ToInt32(directDurability);
                        maxDurability = Convert.ToInt32(directMaxDurability);
                        if (maxDurability > 0)
                        {
                            status = "Tool service API OK";
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        status = "Tool service API exception: " + ex.Message;
                    }
                }

                if (this.cachedTryGetToolComponentMethod == null)
                {
                    status = "TryGetToolComponent unavailable";
                    return false;
                }

                ParameterInfo[] componentParameters = this.cachedTryGetToolComponentMethod.GetParameters();
                if (componentParameters.Length != 2 || !componentParameters[1].ParameterType.IsByRef)
                {
                    status = "TryGetToolComponent signature unavailable";
                    return false;
                }

                Type toolComponentType = componentParameters[1].ParameterType.GetElementType();
                object toolComponentBox = Activator.CreateInstance(toolComponentType);
                object[] componentArgs = new object[] { toolId, toolComponentBox };
                object componentResult = this.cachedTryGetToolComponentMethod.Invoke(toolService, componentArgs);
                if (!(componentResult is bool) || !(bool)componentResult || componentArgs[1] == null)
                {
                    status = "Tool component unavailable";
                    return false;
                }

                object toolComponent = componentArgs[1];
                Type componentType = toolComponent.GetType();
                if (this.cachedToolComponentDurabilityField == null || this.cachedToolComponentDurabilityField.DeclaringType != componentType)
                {
                    this.cachedToolComponentDurabilityField = componentType.GetField("Durability", BindingFlags.Public | BindingFlags.Instance)
                        ?? componentType.GetField("durability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    this.cachedToolComponentMaxDurabilityField = componentType.GetField("DurabilityLimit", BindingFlags.Public | BindingFlags.Instance)
                        ?? componentType.GetField("durabilityLimit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? componentType.GetField("maxDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    this.cachedToolComponentIdField = componentType.GetField("Id", BindingFlags.Public | BindingFlags.Instance)
                        ?? componentType.GetField("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (this.cachedToolComponentDurabilityField == null || this.cachedToolComponentMaxDurabilityField == null)
                {
                    status = "Tool component fields unavailable";
                    return false;
                }

                if (this.cachedToolComponentIdField != null)
                {
                    toolId = Convert.ToInt32(this.cachedToolComponentIdField.GetValue(toolComponent));
                }

                durability = Convert.ToInt32(this.cachedToolComponentDurabilityField.GetValue(toolComponent));
                maxDurability = Convert.ToInt32(this.cachedToolComponentMaxDurabilityField.GetValue(toolComponent));
                status = "Tool service OK";
                return maxDurability > 0;
            }
            catch (Exception ex)
            {
                status = "Tool service exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaHud(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = -1;
            durability = 0;
            maxDurability = 1000;
            status = "HUD durability unavailable";

            try
            {
                if (!this.TryResolveHudDurabilityComponent(out Component joyComponent, out status))
                {
                    return false;
                }

                if (this.TryGetHudDurabilityFromManagedWrapper(joyComponent, out float managedRatio, out bool managedVisible, out _))
                {
                    if (!managedVisible)
                    {
                        status = "HUD durability hidden";
                        return false;
                    }

                    managedRatio = Mathf.Clamp01(managedRatio);
                    durability = Mathf.Clamp(Mathf.RoundToInt(managedRatio * maxDurability), 0, maxDurability);
                    status = "HUD OK";
                    return true;
                }

                if (!this.TryGetHudDurabilityTarget(joyComponent, out Il2CppObject joyObject, out Il2CppType joyType))
                {
                    this.cachedHudDurabilityComponent = null;
                    status = "HUD joy Il2Cpp type unavailable";
                    return false;
                }

                Il2CppFieldInfo durabilityRatioField = joyType.GetField("_durabilityRatio", (Il2CppBindingFlags)62);
                if (durabilityRatioField == null)
                {
                    this.cachedHudDurabilityComponent = null;
                    status = "HUD durability ratio field unavailable";
                    return false;
                }

                object rawRatio = durabilityRatioField.GetValue(joyObject);
                float ratio = 1f;
                try
                {
                    ratio = Convert.ToSingle(rawRatio);
                }
                catch
                {
                    this.cachedHudDurabilityComponent = null;
                    status = "HUD durability ratio unreadable";
                    return false;
                }

                Il2CppFieldInfo durabilityNodeField = joyType.GetField("durabilityNode", (Il2CppBindingFlags)62);
                if (durabilityNodeField != null)
                {
                    GameObject durabilityNode = durabilityNodeField.GetValue(joyObject) as GameObject;
                    if (durabilityNode != null && !durabilityNode.activeInHierarchy)
                    {
                        status = "HUD durability hidden";
                        return false;
                    }
                }

                ratio = Mathf.Clamp01(ratio);
                durability = Mathf.Clamp(Mathf.RoundToInt(ratio * maxDurability), 0, maxDurability);
                status = "HUD OK";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaStatusText(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = -2;
            durability = 0;
            maxDurability = 1000;
            status = "HUD durability text unavailable";

            try
            {
                GameObject statusPanel = GameObject.Find(STATUS_PANEL_PATH) ?? GameObject.Find("StatusPanel(Clone)");
                if (statusPanel == null)
                {
                    status = "HUD status panel unavailable";
                    return false;
                }

                List<string> candidates = new List<string>();
                foreach (Text uiText in statusPanel.GetComponentsInChildren<Text>(true))
                {
                    if (uiText == null)
                    {
                        continue;
                    }

                    Transform textTransform = uiText.transform;
                    string path = this.GetTransformPath(textTransform);
                    string lowerPath = path?.ToLowerInvariant() ?? string.Empty;
                    string value = uiText.text;

                    bool likelyDurability = lowerPath.Contains("duarable")
                        || lowerPath.Contains("durable")
                        || (lowerPath.Contains("skill_bar") && lowerPath.Contains("txt"))
                        || (lowerPath.Contains("main_joy") && lowerPath.Contains("txt"));
                    if (!likelyDurability)
                    {
                        continue;
                    }

                    if (candidates.Count < 8)
                    {
                        candidates.Add($"{path}='{value}'");
                    }

                    if (!this.TryParseDisplayedDurabilityValue(value, out int parsed))
                    {
                        continue;
                    }

                    durability = Mathf.Clamp(parsed * 10, 0, maxDurability);
                    status = "HUD durability text OK";
                    return true;
                }

                status = (candidates.Count > 0)
                    ? "HUD durability text unreadable; candidates=" + string.Join(", ", candidates.ToArray())
                    : "HUD durability text unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "HUD durability text exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaHandhold(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = 0;
            durability = 0;
            maxDurability = 0;
            status = "Handhold durability unavailable";

            try
            {
                if (!this.TryGetCurrentHandholdObject(out object handholdObj, out string handholdSource) || handholdObj == null)
                {
                    status = "Handhold unavailable: " + handholdSource;
                    return false;
                }

                if (!this.TryFindDurabilityCarrierObject(handholdObj, out object carrierObj, out string carrierPath))
                {
                    string handholdTypeName = handholdObj.GetType().FullName ?? handholdObj.GetType().Name ?? "<unknown>";
                    status = "Handhold durability unavailable: " + handholdTypeName;
                    return false;
                }

                Type carrierType = carrierObj.GetType();
                if (!this.TryReadIntMember(carrierObj, carrierType, "durability", out durability)
                    || !this.TryReadIntMember(carrierObj, carrierType, "maxDurability", out maxDurability))
                {
                    status = "Handhold durability fields unreadable: " + carrierPath;
                    return false;
                }

                if (!this.TryReadIntMember(carrierObj, carrierType, "Id", out toolId)
                    && !this.TryReadIntMember(carrierObj, carrierType, "id", out toolId)
                    && !this.TryReadIntMember(carrierObj, carrierType, "toolId", out toolId))
                {
                    toolId = -3;
                }

                status = "Handhold OK: " + handholdSource + " -> " + carrierPath;
                return maxDurability > 0;
            }
            catch (Exception ex)
            {
                status = "Handhold durability exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaAuraMonoToolSystem(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = 0;
            durability = 0;
            maxDurability = 0;
            status = "AuraMono ToolSystem unavailable";

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                float now = Time.unscaledTime;
                this.cachedAuraMonoToolSystemObj.TryGet(out IntPtr toolSystemObj);
                IntPtr getCurrentToolMethod = this.cachedAuraMonoToolSystemGetCurrentToolMethod;
                if (toolSystemObj == IntPtr.Zero || getCurrentToolMethod == IntPtr.Zero)
                {
                    if (now < this.nextAuraMonoToolSystemResolveAttemptAt)
                    {
                        status = "AuraMono ToolSystem resolve throttled";
                        return false;
                    }
                    this.nextAuraMonoToolSystemResolveAttemptAt = now + 8f;

                    if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Tool.ToolSystem", out toolSystemObj) || toolSystemObj == IntPtr.Zero)
                    {
                        status = "AuraMono ToolSystem module unavailable";
                        return false;
                    }

                    IntPtr toolSystemClass = auraMonoObjectGetClass(toolSystemObj);
                    if (toolSystemClass == IntPtr.Zero)
                    {
                        status = "AuraMono ToolSystem class unavailable";
                        return false;
                    }

                    getCurrentToolMethod = this.FindAuraMonoMethodOnHierarchy(toolSystemClass, "GetCurrentTool", 0);
                    if (getCurrentToolMethod == IntPtr.Zero)
                    {
                        status = "AuraMono ToolSystem.GetCurrentTool unavailable";
                        return false;
                    }

                    this.cachedAuraMonoToolSystemObj.Set(toolSystemObj);
                    this.cachedAuraMonoToolSystemGetCurrentToolMethod = getCurrentToolMethod;
                    this.nextAuraMonoToolSystemResolveAttemptAt = -999f;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr toolObj = auraMonoRuntimeInvoke(getCurrentToolMethod, toolSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || toolObj == IntPtr.Zero)
                {
                    this.cachedAuraMonoToolSystemObj.Clear();
                    status = "AuraMono current tool unavailable";
                    return false;
                }

                bool hasDurability = this.TryGetMonoIntMember(toolObj, "durability", out durability)
                    || this.TryGetMonoIntMember(toolObj, "_durability", out durability)
                    || this.TryGetMonoIntMember(toolObj, "Durability", out durability);
                bool hasMaxDurability = this.TryGetMonoIntMember(toolObj, "maxDurability", out maxDurability)
                    || this.TryGetMonoIntMember(toolObj, "_maxDurability", out maxDurability)
                    || this.TryGetMonoIntMember(toolObj, "MaxDurability", out maxDurability);
                if (!hasDurability || !hasMaxDurability)
                {
                    status = "AuraMono ToolSystem fields unreadable: " + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(toolObj));
                    return false;
                }

                if (!this.TryGetMonoIntMember(toolObj, "Id", out toolId)
                    && !this.TryGetMonoIntMember(toolObj, "id", out toolId)
                    && !this.TryGetMonoIntMember(toolObj, "toolId", out toolId)
                    && !this.TryGetMonoIntMember(toolObj, "staticId", out toolId))
                {
                    toolId = -5;
                }

                status = "AuraMono ToolSystem OK: " + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(toolObj));
                return maxDurability > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono ToolSystem exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCurrentToolDurabilityViaAuraMono(out int toolId, out int durability, out int maxDurability, out string status)
        {
            toolId = 0;
            durability = 0;
            maxDurability = 0;
            status = "AuraMono durability unavailable";

            try
            {
                if (this.TryGetCurrentToolDurabilityViaAuraMonoToolSystem(out toolId, out durability, out maxDurability, out string toolSystemStatus))
                {
                    status = toolSystemStatus;
                    return true;
                }

                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero)
                {
                    status = "AuraMono InteractSystem unavailable";
                    return false;
                }

                if (this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                {
                    status = "AuraMono get_player unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
                {
                    status = "AuraMono player unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent") || equipObj == IntPtr.Zero)
                {
                    status = "AuraMono equipComponent unavailable";
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold") || handholdObj == IntPtr.Zero)
                {
                    status = "AuraMono handhold unavailable";
                    return false;
                }

                if (!this.TryGetMonoIntMember(handholdObj, "durability", out durability)
                    || !this.TryGetMonoIntMember(handholdObj, "maxDurability", out maxDurability))
                {
                    string handholdClassName = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass != null ? auraMonoObjectGetClass(handholdObj) : IntPtr.Zero);
                    status = !string.IsNullOrWhiteSpace(toolSystemStatus)
                        ? "toolSystem=" + toolSystemStatus + " | handhold=AuraMono durability fields unreadable: " + handholdClassName
                        : "AuraMono durability fields unreadable: " + handholdClassName;
                    return false;
                }

                if (!this.TryGetMonoIntMember(handholdObj, "Id", out toolId)
                    && !this.TryGetMonoIntMember(handholdObj, "id", out toolId)
                    && !this.TryGetMonoIntMember(handholdObj, "toolId", out toolId))
                {
                    toolId = -4;
                }

                status = "AuraMono handhold OK: " + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass != null ? auraMonoObjectGetClass(handholdObj) : IntPtr.Zero);
                return maxDurability > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono durability exception: " + ex.Message;
                return false;
            }
        }

        private bool TryFindDurabilityCarrierObject(object rootObj, out object carrierObj, out string carrierPath)
        {
            carrierObj = null;
            carrierPath = null;
            if (rootObj == null)
            {
                return false;
            }

            Queue<(object obj, string path, int depth)> queue = new Queue<(object obj, string path, int depth)>();
            HashSet<object> seen = new HashSet<object>();
            queue.Enqueue((rootObj, rootObj.GetType().FullName ?? rootObj.GetType().Name ?? "<root>", 0));

            string[] preferredMembers = new string[]
            {
                "tool", "_tool", "item", "_item", "data", "_data", "toolData", "_toolData",
                "itemData", "_itemData", "runtimeData", "_runtimeData", "config", "_config",
                "tableTool", "_tableTool", "toolInfo", "_toolInfo", "useItem", "_useItem",
                "entity", "_entity", "ComponentData", "_componentData"
            };

            while (queue.Count > 0)
            {
                (object obj, string path, int depth) current = queue.Dequeue();
                if (current.obj == null || !seen.Add(current.obj))
                {
                    continue;
                }

                Type type = current.obj.GetType();
                if (this.HasDurabilityMembers(type))
                {
                    carrierObj = current.obj;
                    carrierPath = current.path;
                    return true;
                }

                if (current.depth >= 3)
                {
                    continue;
                }

                foreach (string memberName in preferredMembers)
                {
                    if (!this.TryGetObjectMember(current.obj, memberName, out object nested) || nested == null)
                    {
                        continue;
                    }

                    Type nestedType = nested.GetType();
                    if (nestedType.IsPrimitive || nested is string || nestedType.IsEnum)
                    {
                        continue;
                    }

                    queue.Enqueue((nested, current.path + "." + memberName, current.depth + 1));
                }
            }

            return false;
        }

        private bool HasDurabilityMembers(Type type)
        {
            return type != null
                && this.FindFieldInHierarchy(type, "durability") != null
                && this.FindFieldInHierarchy(type, "maxDurability") != null;
        }

        private bool TryParseDisplayedDurabilityValue(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            List<char> digits = new List<char>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    digits.Add(c);
                }
                else if (digits.Count > 0)
                {
                    break;
                }
            }

            if (digits.Count == 0)
            {
                return false;
            }

            return int.TryParse(new string(digits.ToArray()), out value);
        }

        private bool TryResolveHudDurabilityComponent(out Component joyComponent, out string status)
        {
            joyComponent = null;
            status = "HUD joy component unavailable";

            if (this.cachedHudDurabilityComponent != null)
            {
                if (this.ComponentHasHudDurability(this.cachedHudDurabilityComponent))
                {
                    joyComponent = this.cachedHudDurabilityComponent;
                    return true;
                }

                this.cachedHudDurabilityComponent = null;
            }

            GameObject skillBar = GameObject.Find(STATUS_SKILL_BAR_PATH)
                ?? GameObject.Find("skill_bar@w@go");
            if (skillBar != null && this.TryFindHudDurabilityComponentOnGameObject(skillBar, out joyComponent, out status))
            {
                this.cachedHudDurabilityComponent = joyComponent;
                return true;
            }

            GameObject skillBarWidget = GameObject.Find(STATUS_SKILL_BAR_WIDGET_PATH)
                ?? GameObject.Find("skill_bar@go");
            if (skillBarWidget != null && this.TryFindHudDurabilityComponentOnGameObject(skillBarWidget, out joyComponent, out status))
            {
                this.cachedHudDurabilityComponent = joyComponent;
                return true;
            }

            GameObject mainJoy = GameObject.Find(STATUS_MAIN_JOY_PATH)
                ?? GameObject.Find("main_joy@go@w");
            if (mainJoy != null && this.TryFindHudDurabilityComponentOnGameObject(mainJoy, out joyComponent, out status))
            {
                this.cachedHudDurabilityComponent = joyComponent;
                return true;
            }

            float now = Time.time;
            if (now < this.nextHudDurabilitySceneScanAt)
            {
                if (skillBar == null && skillBarWidget == null && mainJoy == null)
                {
                    status = "HUD skill bar unavailable";
                }
                return false;
            }

            this.nextHudDurabilitySceneScanAt = now + 1f;
            int bestScore = int.MinValue;
            string bestPath = null;
            List<string> candidateNames = new List<string>();

            foreach (Component component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                GameObject gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!this.ComponentHasHudDurability(component))
                {
                    continue;
                }

                string path = this.GetTransformPath(gameObject.transform);
                string componentName = this.GetHudComponentDebugName(component);
                if (candidateNames.Count < 6)
                {
                    candidateNames.Add(componentName + "@" + path);
                }

                int score = 0;
                if (gameObject.activeInHierarchy)
                {
                    score += 100;
                }
                if (path.IndexOf("StatusPanel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 50;
                }
                if (path.IndexOf("skill_bar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 40;
                }
                if (path.IndexOf("main_joy", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 30;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestPath = path;
                joyComponent = component;
            }

            if (joyComponent == null)
            {
                if (skillBar == null && skillBarWidget == null && mainJoy == null)
                {
                    status = "HUD skill bar unavailable";
                }
                else if (candidateNames.Count > 0)
                {
                    status = "HUD joy component unavailable; candidates=" + string.Join(", ", candidateNames.ToArray());
                }
                return false;
            }

            this.cachedHudDurabilityComponent = joyComponent;
            status = "HUD component bound at " + bestPath;
            return true;
        }

        private bool TryFindHudDurabilityComponentOnGameObject(GameObject gameObject, out Component joyComponent, out string status)
        {
            joyComponent = null;
            status = "HUD joy component unavailable";

            List<string> componentNames = new List<string>();
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                string componentName = this.GetHudComponentDebugName(component);
                if (componentNames.Count < 8)
                {
                    componentNames.Add(componentName);
                }

                if (!this.ComponentHasHudDurability(component))
                {
                    continue;
                }

                joyComponent = component;
                status = "HUD OK";
                return true;
            }

            if (componentNames.Count > 0)
            {
                status = "HUD joy component unavailable; components=" + string.Join(", ", componentNames.ToArray());
            }
            return false;
        }

        private bool ComponentHasHudDurability(Component component)
        {
            if (component == null)
            {
                return false;
            }

            if (this.TryGetHudDurabilityFromManagedWrapper(component, out _, out _, out _))
            {
                return true;
            }

            return this.TryGetHudDurabilityComponentFields(component, out _, out _);
        }

        private bool TryGetHudDurabilityComponentFields(Component component, out Il2CppFieldInfo durabilityRatioField, out Il2CppFieldInfo durabilityNodeField)
        {
            durabilityRatioField = null;
            durabilityNodeField = null;
            if (component == null)
            {
                return false;
            }

            if (!this.TryGetHudDurabilityTarget(component, out Il2CppObject _, out Il2CppType ilType))
            {
                return false;
            }

            durabilityRatioField = ilType.GetField("_durabilityRatio", (Il2CppBindingFlags)62);
            durabilityNodeField = ilType.GetField("durabilityNode", (Il2CppBindingFlags)62);
            return durabilityRatioField != null && durabilityNodeField != null;
        }

        private bool TryGetHudDurabilityFromManagedWrapper(Component component, out float ratio, out bool visible, out string targetName)
        {
            ratio = 1f;
            visible = true;
            targetName = null;
            if (component == null)
            {
                return false;
            }

            try
            {
                Type wrapperType = component.GetType();
                PropertyInfo implProperty = wrapperType.GetProperty("Impl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object implObject = implProperty?.GetValue(component, null);
                if (implObject == null)
                {
                    FieldInfo implInternalField = this.FindFieldInHierarchy(wrapperType, "ImplInternal");
                    implObject = implInternalField?.GetValue(component);
                }

                if (implObject == null)
                {
                    return false;
                }

                Type implType = implObject.GetType();
                if (this.TryGetHudDurabilityFromManagedObject(implObject, implType, out ratio, out visible, out targetName))
                {
                    return true;
                }

                object nodesObject = this.TryGetManagedMemberValue(implObject, "nodes");
                if (nodesObject != null)
                {
                    Type nodesType = nodesObject.GetType();
                    object mainJoyWidgetObject = this.TryGetManagedMemberValue(nodesObject, "main_joy_widget");
                    if (mainJoyWidgetObject != null)
                    {
                        Type mainJoyWidgetType = mainJoyWidgetObject.GetType();
                        if (this.TryGetHudDurabilityFromManagedObject(mainJoyWidgetObject, mainJoyWidgetType, out ratio, out visible, out string mainJoyTargetName))
                        {
                            targetName = (implType.FullName ?? implType.Name) + ".nodes.main_joy_widget->" + mainJoyTargetName;
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetHudDurabilityFromManagedObject(object targetObject, Type targetType, out float ratio, out bool visible, out string targetName)
        {
            ratio = 1f;
            visible = true;
            targetName = null;
            if (targetObject == null || targetType == null)
            {
                return false;
            }

            try
            {
                FieldInfo ratioField = this.FindFieldInHierarchy(targetType, "_durabilityRatio");
                FieldInfo nodeField = this.FindFieldInHierarchy(targetType, "durabilityNode");
                if (ratioField == null || nodeField == null)
                {
                    return false;
                }

                ratio = Convert.ToSingle(ratioField.GetValue(targetObject));
                GameObject durabilityNode = nodeField.GetValue(targetObject) as GameObject;
                visible = durabilityNode == null || durabilityNode.activeInHierarchy;
                targetName = targetType.FullName ?? targetType.Name;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetHudDurabilityTarget(Component component, out Il2CppObject targetObject, out Il2CppType targetType)
        {
            targetObject = null;
            targetType = null;
            if (component == null)
            {
                return false;
            }

            try
            {
                Il2CppObject componentObject = component.TryCast<Il2CppObject>();
                Il2CppType componentType = componentObject?.GetIl2CppType();
                if (componentObject == null || componentType == null)
                {
                    return false;
                }

                Il2CppFieldInfo ratioField = componentType.GetField("_durabilityRatio", (Il2CppBindingFlags)62);
                Il2CppFieldInfo nodeField = componentType.GetField("durabilityNode", (Il2CppBindingFlags)62);
                if (ratioField != null && nodeField != null)
                {
                    targetObject = componentObject;
                    targetType = componentType;
                    return true;
                }

                string typeName = componentType.FullName?.ToString() ?? componentType.Name?.ToString() ?? string.Empty;
                if (!typeName.Contains("DynamicMonoBehaviour"))
                {
                    return false;
                }

                Il2CppPropertyInfo implProperty = componentType.GetProperty("Impl");
                Il2CppObject implObject = implProperty != null ? (implProperty.GetValue(componentObject) as Il2CppObject) : null;
                Il2CppType implType = implObject?.GetIl2CppType();
                if (implObject == null || implType == null)
                {
                    return false;
                }

                ratioField = implType.GetField("_durabilityRatio", (Il2CppBindingFlags)62);
                nodeField = implType.GetField("durabilityNode", (Il2CppBindingFlags)62);
                if (ratioField == null || nodeField == null)
                {
                    return false;
                }

                targetObject = implObject;
                targetType = implType;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryResolveToolDurabilityReflection(out string status)
        {
            status = "OK";
            if (this.toolDurabilityReflectionResolved
                && (this.cachedToolSystemInstanceProperty != null || this.cachedToolDataModuleInstanceProperty != null)
                && this.cachedToolSystemGetCurrentToolMethod != null)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.nextToolReflectionResolveAttemptAt)
            {
                status = "ToolSystem reflection resolve throttled";
                return false;
            }
            this.nextToolReflectionResolveAttemptAt = now + 10f;

            try
            {
                List<string> candidateToolSystems = new List<string>();

                if (this.cachedToolSystemType == null)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            Type type = assembly.GetType("XDTGameSystem.GameplaySystem.Tool.ToolSystem", false);
                            if (type != null)
                            {
                                this.cachedToolSystemType = type;
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                if (this.cachedToolSystemType == null)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types = null;
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

                        foreach (Type type in types)
                        {
                            if (type == null)
                            {
                                continue;
                            }

                            MethodInfo getCurrentToolMethod = null;
                            try
                            {
                                getCurrentToolMethod = type.GetMethod("GetCurrentTool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            }
                            catch
                            {
                            }

                            if (getCurrentToolMethod == null)
                            {
                                continue;
                            }

                            Type toolReturnType = getCurrentToolMethod.ReturnType;
                            if (toolReturnType == null)
                            {
                                continue;
                            }

                            FieldInfo returnDurabilityField = toolReturnType.GetField("durability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            FieldInfo returnMaxDurabilityField = toolReturnType.GetField("maxDurability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            FieldInfo returnIdField = toolReturnType.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (returnDurabilityField == null || returnMaxDurabilityField == null || returnIdField == null)
                            {
                                continue;
                            }

                            string candidateName = type.FullName ?? type.Name;
                            if (candidateToolSystems.Count < 8)
                            {
                                candidateToolSystems.Add(candidateName);
                            }

                            bool preferred = string.Equals(type.Name, "ToolSystem", StringComparison.Ordinal)
                                || candidateName.IndexOf("ToolSystem", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!preferred && this.cachedToolSystemType != null)
                            {
                                continue;
                            }

                            this.cachedToolSystemType = type;
                            this.cachedToolSystemGetCurrentToolMethod = getCurrentToolMethod;
                            this.cachedToolIdField = returnIdField;
                            this.cachedToolDurabilityField = returnDurabilityField;
                            this.cachedToolMaxDurabilityField = returnMaxDurabilityField;

                            if (preferred)
                            {
                                break;
                            }
                        }

                        if (this.cachedToolSystemType != null)
                        {
                            break;
                        }
                    }
                }

                if (this.cachedToolSystemType == null)
                {
                    status = (candidateToolSystems.Count > 0)
                        ? "ToolSystem type unavailable; GetCurrentTool candidates=" + string.Join(", ", candidateToolSystems.ToArray())
                        : "ToolSystem type unavailable; no GetCurrentTool candidates";
                    return false;
                }

                if (!this.toolDurabilityDiscoveryLogged)
                {
                    this.AutoEatRepairLog("[AutoRepair] Bound live durability resolver to " + (this.cachedToolSystemType.FullName ?? this.cachedToolSystemType.Name));
                    if (candidateToolSystems.Count > 0)
                    {
                        this.AutoEatRepairLog("[AutoRepair] Live durability candidates: " + string.Join(", ", candidateToolSystems.ToArray()));
                    }
                    this.toolDurabilityDiscoveryLogged = true;
                }

                if (this.cachedDataModuleOpenGenericType == null)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types = null;
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

                        foreach (Type type in types)
                        {
                            if (type == null || !type.IsGenericTypeDefinition || type.Name != "DataModule`1")
                            {
                                continue;
                            }

                            PropertyInfo instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceProperty != null)
                            {
                                this.cachedDataModuleOpenGenericType = type;
                                break;
                            }
                        }

                        if (this.cachedDataModuleOpenGenericType != null)
                        {
                            break;
                        }
                    }
                }

                if (this.cachedDataModuleOpenGenericType == null)
                {
                    status = "DataModule<T> type unavailable";
                    return false;
                }

                if (this.cachedToolDataModuleType == null)
                {
                    this.cachedToolDataModuleType = this.cachedDataModuleOpenGenericType.MakeGenericType(this.cachedToolSystemType);
                }

                if (this.cachedToolSystemInstanceProperty == null)
                {
                    this.cachedToolSystemInstanceProperty = this.cachedToolSystemType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                }

                if (this.cachedToolDataModuleInstanceProperty == null)
                {
                    this.cachedToolDataModuleInstanceProperty = this.cachedToolDataModuleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }

                if (this.cachedToolSystemGetCurrentToolMethod == null)
                {
                    this.cachedToolSystemGetCurrentToolMethod = this.cachedToolSystemType.GetMethod("GetCurrentTool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }

                if ((this.cachedToolSystemInstanceProperty == null && this.cachedToolDataModuleInstanceProperty == null) || this.cachedToolSystemGetCurrentToolMethod == null)
                {
                    status = "ToolSystem reflection members unavailable";
                    return false;
                }

                this.toolDurabilityReflectionResolved = true;
                this.nextToolReflectionResolveAttemptAt = -999f;
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        public float GetResourceAutoRepairPauseSeconds()
        {
            return this.resourceAutoRepairPauseSeconds;
        }

        public void SetResourceAutoRepairPauseSeconds(float seconds)
        {
            this.resourceAutoRepairPauseSeconds = seconds;
        }

        public bool IsResourceRepairPaused()
        {
            return Time.time < this.resourceRepairPauseUntil;
        }

        public void StartRepairPublic()
        {
            try { this.StartRepair(); } catch { }
        }

        public void StartAutoEatPublic()
        {
            try { this.StartAutoEat(); } catch { }
        }

        private bool IsAutoRepairInProgress()
        {
            return this.isRepairing || this.autoRepairWaiting || this.repairStep != 0;
        }

        private bool IsAutoEatActiveOrQueued()
        {
            return this.isAutoEating || this.pendingAutoEatRequest;
        }

        private bool IsAutoRepairActiveOrQueued()
        {
            return this.IsAutoRepairInProgress() || this.pendingAutoRepairRequest;
        }

        private float GetEffectiveAutoEatTriggerCheckInterval()
        {
            return this.AreHeavyFarmAutomationsActive() ? FarmActiveAutoEatTriggerCheckInterval : AutoEatTriggerCheckInterval;
        }

        private float GetEffectiveAutoRepairTriggerCheckInterval()
        {
            return this.AreHeavyFarmAutomationsActive() ? FarmActiveAutoRepairTriggerCheckInterval : AutoRepairTriggerCheckInterval;
        }

        private float GetEffectiveToolDurabilityPollInterval()
        {
            return this.AreHeavyFarmAutomationsActive() ? FarmActiveToolDurabilityPollInterval : ToolDurabilityPollInterval;
        }

        private bool TryHandleDurabilityAutoRepairTrigger(string source)
        {
            float now = Time.time;
            float cooldownUntil = Math.Max(this.nextAutoRepairToastAllowedAt, this.resourceRepairPauseUntil);
            if (now < cooldownUntil)
            {
                this.AutoEatRepairLog($"[AutoRepair] Durability trigger ignored from {source}; repair toast cooldown active ({cooldownUntil - now:F1}s left).");
                return false;
            }

            if (this.IsAutoRepairInProgress())
            {
                this.nextAutoRepairToastAllowedAt = now + Mathf.Max(2f, this.resourceAutoRepairPauseSeconds);
                this.AutoEatRepairLog("[AutoRepair] Durability trigger ignored from " + source + " because repair is already running.");
                return false;
            }

            if (this.pendingAutoRepairRequest)
            {
                this.nextAutoRepairToastAllowedAt = now + Mathf.Max(2f, this.resourceAutoRepairPauseSeconds);
                this.AutoEatRepairLog("[AutoRepair] Durability trigger ignored from " + source + " because repair is already queued.");
                return false;
            }

            if (this.isAutoEating)
            {
                if (!this.pendingAutoRepairRequest)
                {
                    this.pendingAutoRepairRequest = true;
                    this.nextAutoRepairToastAllowedAt = now + Mathf.Max(2f, this.resourceAutoRepairPauseSeconds);
                    this.AutoEatRepairLog("[AutoRepair] Durability trigger queued from " + source + " because bag automation is busy.");
                    return true;
                }

                this.AutoEatRepairLog("[AutoRepair] Durability trigger ignored from " + source + " because repair is already queued.");
                return false;
            }

            this.AutoEatRepairLog("[AutoRepair] Durability toast requested StartRepair (" + source + ")");
            this.lastStartWasAutoRepair = true;
            this.StartRepair();
            if (!this.isRepairing)
            {
                this.AutoEatRepairLog("[AutoRepair] Durability trigger from " + source + " did not start because StartRepair rejected it.");
                return false;
            }

            this.nextAutoRepairToastAllowedAt = now + Mathf.Max(2f, this.resourceAutoRepairPauseSeconds);
            this.resourceRepairPauseUntil = now + this.resourceAutoRepairPauseSeconds;
            this.AddMenuNotification(this.L("Auto Repair started"), new Color(0.45f, 1f, 0.55f));
            return true;
        }

        private bool TryDirectUseRepairKit()
        {
            try
            {
                string repairKey = (this.autoRepairType >= 0 && this.autoRepairType < this.autoRepairKeys.Length) ? this.autoRepairKeys[this.autoRepairType] : this.autoRepairKeys[0];
                this.AutoEatRepairLog("[AutoRepair] Direct repair requested. key=" + repairKey + " option=" + this.autoRepairOptions[Mathf.Clamp(this.autoRepairType, 0, this.autoRepairOptions.Length - 1)]);
                if (this.TryUseCachedRepairKit(repairKey))
                {
                    return true;
                }

                if (!this.TryFindDirectBackpackItem(repairKey, false, out uint netId) || netId == 0U)
                {
                    this.AutoEatRepairLog("[AutoRepair] Direct backpack item not found for " + repairKey);
                    this.ShowMissingRepairItemNotification();
                    return false;
                }

                this.CacheRepairKitMatch(repairKey);
                this.AutoEatRepairLog("[AutoRepair] Direct repair matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + "; sending BagModule ToolRestorer function.");
                return this.TryExecuteDirectBackpackItemFunc(113, netId);
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[AutoRepair] Direct repair exception: " + ex.Message);
                return false;
            }
        }

        private bool TryUseCachedRepairKit(string repairKey)
        {
            if (string.IsNullOrEmpty(repairKey)
                || this.cachedRepairKitNetId == 0U
                || !string.Equals(this.cachedRepairKitKey, repairKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!this.TryGetDirectBackpackItemCountByNetId(this.cachedRepairKitNetId, out int currentCount, false))
            {
                this.ClearCachedRepairKit();
                return false;
            }

            this.lastDirectBackpackMatchedNetId = this.cachedRepairKitNetId;
            this.lastDirectBackpackMatchedStaticId = this.cachedRepairKitStaticId;
            this.lastDirectBackpackMatchedEntityType = 0;
            this.lastDirectBackpackMatchedCount = currentCount;
            this.cachedRepairKitCount = currentCount;

            this.AutoEatRepairLog("[AutoRepair] Cached repair kit matched netId=" + this.cachedRepairKitNetId + " count=" + currentCount + "; sending BagModule ToolRestorer function.");
            if (this.TryExecuteDirectBackpackItemFunc(113, this.cachedRepairKitNetId))
            {
                return true;
            }

            this.ClearCachedRepairKit();
            return false;
        }

        private void CacheRepairKitMatch(string repairKey)
        {
            this.cachedRepairKitKey = repairKey ?? "";
            this.cachedRepairKitNetId = this.lastDirectBackpackMatchedNetId;
            this.cachedRepairKitStaticId = this.lastDirectBackpackMatchedStaticId;
            this.cachedRepairKitCount = this.lastDirectBackpackMatchedCount;
        }

        private void ClearCachedRepairKit()
        {
            this.cachedRepairKitKey = "";
            this.cachedRepairKitNetId = 0U;
            this.cachedRepairKitStaticId = 0;
            this.cachedRepairKitCount = 0;
        }

        private bool TryUseCachedFood(string foodKey, bool anyFood)
        {
            if (this.cachedFoodNetId == 0U
                || this.cachedFoodAnyFood != anyFood
                || !string.Equals(this.cachedFoodKey, foodKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!this.TryGetDirectBackpackItemCountByNetId(this.cachedFoodNetId, out int currentCount, false) || currentCount <= 0)
            {
                this.ClearCachedFood();
                return false;
            }

            this.lastDirectBackpackMatchedNetId = this.cachedFoodNetId;
            this.lastDirectBackpackMatchedStaticId = this.cachedFoodStaticId;
            this.lastDirectBackpackMatchedEntityType = this.cachedFoodEntityType;
            this.lastDirectBackpackMatchedCount = currentCount;
            this.cachedFoodCount = currentCount;

            this.AutoEatRepairLog("[Auto Eat] Cached food matched netId=" + this.cachedFoodNetId + " count=" + currentCount + "; sending BagModule Eat function.");
            if (this.TryExecuteDirectBackpackItemFunc(112, this.cachedFoodNetId))
            {
                return true;
            }

            this.ClearCachedFood();
            return false;
        }

        private void CacheFoodMatch(string foodKey, bool anyFood)
        {
            this.cachedFoodKey = foodKey ?? "";
            this.cachedFoodAnyFood = anyFood;
            this.cachedFoodNetId = this.lastDirectBackpackMatchedNetId;
            this.cachedFoodStaticId = this.lastDirectBackpackMatchedStaticId;
            this.cachedFoodEntityType = this.lastDirectBackpackMatchedEntityType;
            this.cachedFoodCount = this.lastDirectBackpackMatchedCount;
        }

        private void ClearCachedFood()
        {
            this.cachedFoodKey = "";
            this.cachedFoodAnyFood = false;
            this.cachedFoodNetId = 0U;
            this.cachedFoodStaticId = 0;
            this.cachedFoodEntityType = 0;
            this.cachedFoodCount = 0;
        }

        private bool VerifyLastRepairUseSucceeded()
        {
            try
            {
                uint previousNetId = this.lastRepairUseNetId;
                int previousCount = this.lastRepairUseCountBefore;

                if (previousNetId == 0U)
                {
                    this.AutoEatRepairLog("[AutoRepair] Repair verification has no previous netId; accepting use.");
                    return true;
                }

                if (!this.TryGetDirectBackpackItemCountByNetId(previousNetId, out int currentCount, true))
                {
                    if (this.cachedRepairKitNetId == previousNetId)
                    {
                        this.ClearCachedRepairKit();
                    }

                    this.AutoEatRepairLog("[AutoRepair] Repair verification success: previous repair kit netId disappeared.");
                    return true;
                }

                if (previousCount > 0 && currentCount > 0)
                {
                    bool consumed = currentCount < previousCount;
                    if (consumed && this.cachedRepairKitNetId == previousNetId)
                    {
                        this.cachedRepairKitCount = currentCount;
                    }

                    this.AutoEatRepairLog("[AutoRepair] Repair verification count check: netId=" + previousNetId + " before=" + previousCount + " after=" + currentCount + " consumed=" + consumed);
                    return consumed;
                }

                if (previousCount <= 0 || currentCount <= 0)
                {
                    this.AutoEatRepairLog("[AutoRepair] Repair verification count unavailable; accepting use to avoid a false retry. before=" + previousCount + " after=" + currentCount);
                    return true;
                }

                this.AutoEatRepairLog("[AutoRepair] Repair verification failed: item still appears unchanged. netId=" + previousNetId + " before=" + previousCount + " after=" + currentCount);
                return false;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[AutoRepair] Repair verification exception; accepting use. " + ex.Message);
                return true;
            }
        }

        private void TryUseBaitFromBagWithNotification()
        {
            if (Time.unscaledTime < this.nextUseBaitAllowedAt)
            {
                return;
            }

            if (this.TryUseBaitFromBag())
            {
                this.AddMenuNotification(this.L("Bait used"), new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.AddMenuNotification(this.L("No bait found in bag"), new Color(1f, 0.65f, 0.45f));
            }
        }

        private bool TryUseBaitFromBag()
        {
            try
            {
                if (Time.unscaledTime < this.nextUseBaitAllowedAt)
                {
                    return false;
                }

                if (!this.TryFindDirectBackpackItemByStaticId(BaitStaticId, out uint netId) || netId == 0U)
                {
                    this.AutoEatRepairLog("[UseBait] Backpack bait not found for staticId=" + BaitStaticId);
                    return false;
                }

                this.AutoEatRepairLog("[UseBait] Matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + "; sending ChumBait function.");
                if (!this.TryExecuteDirectBackpackItemFunc(BackpackFuncChumBait, netId))
                {
                    this.AutoEatRepairLog("[UseBait] ExecuteBackpackItemFunc failed for netId=" + netId);
                    return false;
                }

                this.nextUseBaitAllowedAt = Time.unscaledTime + UseBaitCooldownSeconds;
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[UseBait] Exception: " + ex.Message);
                return false;
            }
        }

        private bool TryDirectUseFood()
        {
            try
            {
                string foodKey = this.GetAutoEatFoodKey();
                bool anyFood = this.autoEatFoodType == this.autoEatFoodOptions.Length - 2;
                this.AutoEatRepairLog("[Auto Eat] Direct food requested. key=" + foodKey + " anyFood=" + anyFood + " option=" + this.GetAutoEatFoodOptionLabel(this.autoEatFoodType) + " energy=" + this.GetCurrentEnergyDisplay());
                if (this.TryUseCachedFood(foodKey, anyFood))
                {
                    return true;
                }

                if (!this.TryFindDirectBackpackItem(foodKey, anyFood, out uint netId) || netId == 0U)
                {
                    this.AutoEatRepairLog("[Auto Eat] Direct backpack food not found for " + this.GetAutoEatFoodOptionLabel(this.autoEatFoodType));
                    this.ClearCachedFood();
                    this.ShowMissingFoodNotification();
                    return false;
                }

                this.CacheFoodMatch(foodKey, anyFood);
                this.AutoEatRepairLog("[Auto Eat] Direct food matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + " entityType=" + this.lastDirectBackpackMatchedEntityType + "; sending BagModule Eat function.");
                return this.TryExecuteDirectBackpackItemFunc(112, netId);
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[Auto Eat] Direct food exception: " + ex.Message);
                return false;
            }
        }

        private void ShowMissingRepairItemNotification()
        {
            if (Time.unscaledTime < this.nextMissingRepairItemNotificationAt)
            {
                return;
            }

            this.nextMissingRepairItemNotificationAt = Time.unscaledTime + 6f;
            string repairName = this.GetAutoRepairOptionLabel(Mathf.Clamp(this.autoRepairType, 0, this.autoRepairOptions.Length - 1));
            this.AddMenuNotification("Auto Repair stopped - no " + repairName + " found", new Color(1f, 0.65f, 0.45f));
        }

        private void ShowMissingFoodNotification()
        {
            if (Time.unscaledTime < this.nextMissingFoodNotificationAt)
            {
                return;
            }

            this.nextMissingFoodNotificationAt = Time.unscaledTime + 6f;
            this.AddMenuNotification("Auto Eat stopped - no " + this.GetAutoEatFoodOptionLabel(this.autoEatFoodType) + " found", new Color(1f, 0.65f, 0.45f));
        }

        private string GetAutoEatFoodKey()
        {
            if (this.autoEatFoodType == this.autoEatFoodOptions.Length - 1 && !string.IsNullOrWhiteSpace(this.autoEatCustomFoodName))
            {
                return this.NormalizeAutoEatFoodLookupKey(this.autoEatCustomFoodName);
            }

            if (this.autoEatFoodType >= 0 && this.autoEatFoodType < this.autoEatFoodKeys.Length)
            {
                return this.autoEatFoodKeys[this.autoEatFoodType];
            }

            return AUTO_EAT_FOOD_KEY;
        }

        private string NormalizeAutoEatFoodLookupKey(string key)
        {
            string text = (key ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (text.StartsWith("ui_item_normal_", StringComparison.Ordinal))
            {
                text = text.Substring("ui_item_normal_".Length);
            }
            else if (text.StartsWith("ui_item_", StringComparison.Ordinal))
            {
                text = text.Substring("ui_item_".Length);
            }

            return text;
        }

        private void CheckForBagFoodClick()
        {
            if (!IsBagOpen() || !this.customFoodPickMode) return;

            // Check if there's a selected item - look for the selection indicator
            GameObject selectedIndicator = GameObject.Find(SELECTED_ITEM_PATH);
            if (selectedIndicator != null && selectedIndicator.activeInHierarchy)
            {
                // A food item was clicked - find the food closest to selection indicator
                string clickedFood = this.GetSelectedFoodFromSelectionIndicator();
                if (!string.IsNullOrEmpty(clickedFood))
                {
                    this.lastClickedBagFood = clickedFood;
                    this.AddMenuNotification(this.LF("Detected food click: {0}", this.GetFoodDisplayName(clickedFood)), new Color(0.45f, 1f, 0.55f));
                    // Close the bag to complete the selection
                    this.CloseInventory();
                }
            }
        }

        private string GetSelectedFoodFromSelectionIndicator()
        {
            // Get the selection indicator GameObject
            GameObject selectedIndicator = GameObject.Find(SELECTED_ITEM_PATH);
            if (selectedIndicator == null || !selectedIndicator.activeInHierarchy) return "";

            // Get the position of the selection indicator
            Vector3 indicatorPos = selectedIndicator.transform.position;

            // Find the Image component closest to the selection indicator position
            Image closestFoodImage = null;
            float closestDistance = float.MaxValue;

            foreach (Image img in GetBagPanelImages())
            {
                if (img != null && img.sprite != null && img.gameObject.activeInHierarchy)
                {
                    string spriteName = img.sprite.name.ToLowerInvariant();
                    // Check if it's a food item sprite
                    if (spriteName.StartsWith("ui_item_normal_p_"))
                    {
                        string itemName = spriteName.Substring("ui_item_normal_p_".Length);
                        // Check if it's a food item by keywords
                        string[] foodKeywords = new[] { "food", "bread", "jam", "mushroom", "salad", "soup", "stew", "pie", "cake", "fish", "meat", "fruit", "vegetable", "berry", "apple", "cheese", "egg", "milk", "honey", "candy", "snack", "meal", "dish", "blue", "rasp", "mix", "bake" };
                        if (foodKeywords.Any(k => itemName.Contains(k)))
                        {
                            // Calculate distance to selection indicator
                            float distance = Vector3.Distance(img.transform.position, indicatorPos);
                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                closestFoodImage = img;
                            }
                        }
                    }
                }
            }

            if (closestFoodImage != null && closestDistance < 100f) // Within reasonable UI distance
            {
                return closestFoodImage.sprite.name.ToLowerInvariant();
            }

            return "";
        }

        private string[] ScanBagForFoodItems()
        {
            var foodList = new System.Collections.Generic.List<string>();
            HashSet<string> seenItems = new HashSet<string>();
            this.scannedBagFoodDisplayNames.Clear();

            // Known food keywords to filter items
            string[] foodKeywords = new[] { "food", "bread", "jam", "mushroom", "salad", "soup", "stew", "pie", "cake", "fish", "meat", "fruit", "vegetable", "berry", "apple", "cheese", "egg", "milk", "honey", "candy", "snack", "meal", "dish" };

            foreach (Image img in GetBagPanelImages())
            {
                if (img != null && img.sprite != null && img.gameObject.activeInHierarchy)
                {
                    string spriteName = img.sprite.name.ToLowerInvariant();
                    bool isFood = false;
                    string itemName = "";

                    // Check if it's an item sprite (ui_item_normal_p_*)
                    if (spriteName.StartsWith("ui_item_normal_p_"))
                    {
                        itemName = spriteName.Replace("ui_item_normal_p_", "");
                        // Check if it contains any food keyword
                        foreach (string keyword in foodKeywords)
                        {
                            if (itemName.Contains(keyword))
                            {
                                isFood = true;
                                break;
                            }
                        }
                        // Also check if it contains "food_", "gather_", or "fruit_" patterns
                        if (!isFood && (itemName.Contains("food_") || itemName.Contains("gather_") || itemName.Contains("fruit_")))
                            isFood = true;
                    }
                    // Also include gather_ and fruit_ items that don't have ui_item_normal_p_ prefix
                    if (!isFood && (spriteName.Contains("gather_") || spriteName.Contains("fruit_")))
                    {
                        isFood = true;
                    }

                    if (isFood && !seenItems.Contains(spriteName))
                    {
                        seenItems.Add(spriteName);
                        foodList.Add(spriteName);
                        this.CacheScannedBagFoodDisplayName(spriteName);
                        if (!this.scannedBagFoodTextures.ContainsKey(spriteName) && this.TryLoadCachedItemIcon(spriteName, out Texture2D cachedFoodTexture))
                        {
                            this.scannedBagFoodTextures[spriteName] = cachedFoodTexture;
                            continue;
                        }
                        // Copy the sprite texture for UI display (copy to survive bag scrolling)
                        // Use RenderTexture approach since game textures are non-readable
                        if (img.sprite.texture != null)
                        {
                            Texture2D original = img.sprite.texture;
                            try
                            {
                                // Create a temporary RenderTexture to copy the non-readable texture
                                RenderTexture rt = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
                                Graphics.Blit(original, rt);
                                
                                // Create new readable texture and read from RenderTexture
                                Texture2D copy = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
                                RenderTexture previousRT = RenderTexture.active;
                                RenderTexture.active = rt;
                                copy.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
                                copy.Apply();
                                RenderTexture.active = previousRT;
                                RenderTexture.ReleaseTemporary(rt);
                                
                                this.scannedBagFoodTextures[spriteName] = copy;
                                this.SaveCachedItemIcon(spriteName, copy);
                            }
                            catch (Exception texEx)
                            {
                                ModLogger.Msg($"[BagScan] Failed to copy texture for {spriteName}: {texEx.Message}");
                            }
                        }
                    }
                }
            }

            return foodList.ToArray();
        }

        private void CacheScannedBagFoodDisplayName(string spriteName)
        {
            string normalizedSprite = this.NormalizeAutoSellMatchKey(spriteName);
            if (string.IsNullOrWhiteSpace(normalizedSprite) || this.scannedBagFoodDisplayNames.ContainsKey(normalizedSprite))
            {
                return;
            }

            string resolvedName = this.TryResolveScannedBagFoodDisplayName(spriteName, normalizedSprite, out string displayName)
                ? displayName
                : this.GetFoodDisplayName(spriteName);

            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                this.scannedBagFoodDisplayNames[normalizedSprite] = resolvedName;
            }
        }

        private bool TryResolveScannedBagFoodDisplayName(string spriteName, string normalizedSprite, out string displayName)
        {
            displayName = string.Empty;

            if (this.autoSellBagItems != null)
            {
                for (int i = 0; i < this.autoSellBagItems.Count; i++)
                {
                    AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    if (!this.DoesBagItemEntryMatchSprite(entry, normalizedSprite))
                    {
                        continue;
                    }

                    if (this.TryGetResolvedFoodNameFromStaticId(entry.StaticId, out displayName))
                    {
                        return true;
                    }

                    string entryName = this.CleanResolvedBagFoodName(entry.DisplayName);
                    if (!string.IsNullOrWhiteSpace(entryName))
                    {
                        displayName = entryName;
                        return true;
                    }
                }
            }

            if (this.TryRefreshDirectBackpackRuntimeSnapshot(false))
            {
                for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
                {
                    DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                    if (item == null || !this.DoesRuntimeBackpackItemMatchSprite(item, normalizedSprite))
                    {
                        continue;
                    }

                    if (this.TryGetResolvedFoodNameFromStaticId(item.StaticId, out displayName))
                    {
                        return true;
                    }

                    string descriptorName = this.CleanResolvedBagFoodName(item.Descriptor);
                    if (!string.IsNullOrWhiteSpace(descriptorName))
                    {
                        displayName = descriptorName;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetResolvedFoodNameFromStaticId(int staticId, out string displayName)
        {
            displayName = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            try
            {
                Type backpackItemType = this.FindLoadedType(
                    "BackpackItem",
                    "XDTGameSystem.UISystem.BackPack.BackpackItem",
                    "UISystem.BackPack.BackpackItem");
                MethodInfo getBackpackNameMethod = backpackItemType?.GetMethod(
                    "GetBackPackName",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(int), typeof(uint) },
                    null);
                if (getBackpackNameMethod != null)
                {
                    object rawName = getBackpackNameMethod.Invoke(null, new object[] { staticId, 0, 0U });
                    string cleanedName = this.CleanResolvedBagFoodName(rawName?.ToString());
                    if (!string.IsNullOrWhiteSpace(cleanedName))
                    {
                        displayName = cleanedName;
                        return true;
                    }
                }

                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType != null)
                {
                    MethodInfo getEntityMethod = tableDataType.GetMethod("GetEntity", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                    if (getEntityMethod != null)
                    {
                        object entityObj = getEntityMethod.Invoke(null, new object[] { staticId, false });
                        if (entityObj != null && this.TryGetResolvedFoodNameFromEntityObject(entityObj, out displayName))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return this.TryGetResolvedFoodNameFromStaticIdAuraMono(staticId, out displayName);
        }

        private unsafe bool TryGetResolvedFoodNameFromStaticIdAuraMono(int staticId, out string displayName)
        {
            displayName = string.Empty;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr backpackClass = this.FindAuraMonoClassByFullName("XDTGameSystem.UISystem.BackPack.BackpackItem");
                if (backpackClass == IntPtr.Zero)
                {
                    backpackClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTGameSystem.UISystem.BackPack",
                        "BackpackItem");
                }

                if (backpackClass != IntPtr.Zero)
                {
                    IntPtr getBackpackNameMethod = this.FindAuraMonoMethodOnHierarchy(backpackClass, "GetBackPackName", 3);
                    if (getBackpackNameMethod != IntPtr.Zero)
                    {
                        int starRate = 0;
                        uint netId = 0U;
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[3];
                        args[0] = (IntPtr)(&staticId);
                        args[1] = (IntPtr)(&starRate);
                        args[2] = (IntPtr)(&netId);
                        IntPtr nameObj = auraMonoRuntimeInvoke(getBackpackNameMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero && nameObj != IntPtr.Zero && this.TryReadMonoString(nameObj, out string rawName))
                        {
                            displayName = this.CleanResolvedBagFoodName(rawName);
                            if (!string.IsNullOrWhiteSpace(displayName))
                            {
                                return true;
                            }
                        }
                    }
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                if (getEntityMethod != IntPtr.Zero)
                {
                    bool needException = false;
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&staticId);
                    args[1] = (IntPtr)(&needException);
                    IntPtr entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && entityObj != IntPtr.Zero && this.TryGetMonoStringMember(entityObj, "name", out string entityName))
                    {
                        displayName = this.CleanResolvedBagFoodName(entityName);
                        if (!string.IsNullOrWhiteSpace(displayName))
                        {
                            return true;
                        }
                    }
                }

                if (this.TryResolveNetCookRecipeNameFromTableDataMono(tableDataClass, staticId, out displayName))
                {
                    displayName = this.CleanResolvedBagFoodName(displayName);
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                displayName = string.Empty;
                return false;
            }
        }

        private bool TryGetResolvedFoodNameFromEntityObject(object entityObj, out string displayName)
        {
            displayName = string.Empty;
            if (entityObj == null)
            {
                return false;
            }

            foreach (string memberName in new[] { "name", "_name", "Name", "displayName", "_displayName", "DisplayName" })
            {
                if (this.TryGetObjectMember(entityObj, memberName, out object rawName) && rawName != null)
                {
                    string cleanedName = this.CleanResolvedBagFoodName(rawName.ToString());
                    if (!string.IsNullOrWhiteSpace(cleanedName))
                    {
                        displayName = cleanedName;
                        return true;
                    }
                }
            }

            return false;
        }

        private string CleanResolvedBagFoodName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            name = name.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            if (int.TryParse(name, out _) || this.IsNumericTokenSequence(name))
            {
                return string.Empty;
            }

            string lowered = name.ToLowerInvariant();
            if (lowered.StartsWith("ui_item_normal_") || lowered.StartsWith("ui_item_special_") || lowered.StartsWith("p_"))
            {
                return string.Empty;
            }

            if (lowered.Contains("templateid") || lowered.Contains("icon"))
            {
                return string.Empty;
            }

            return name;
        }

        private string GetFoodDisplayName(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return "Unknown Food";

            string normalizedSprite = this.NormalizeAutoSellMatchKey(spriteName);
            if (!string.IsNullOrWhiteSpace(normalizedSprite)
                && this.scannedBagFoodDisplayNames.TryGetValue(normalizedSprite, out string cachedName)
                && !string.IsNullOrWhiteSpace(cachedName))
            {
                return cachedName;
            }

            // Extract item name from sprite name
            string itemName = spriteName
                .Replace("ui_item_normal_p_", "")
                .Replace("gather_", "")
                .Replace("fruit_", "")
                .Replace("_", " ");

            // Capitalize first letter of each word
            var words = itemName.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        private string GetCurrentFoodKey()
        {
            // Custom food is the last option - use the saved custom food name
            if (this.autoEatFoodType == this.autoEatFoodOptions.Length - 1)
            {
                return this.autoEatCustomFoodName?.ToLowerInvariant() ?? "";
            }
            return this.autoEatFoodKeys[this.autoEatFoodType];
        }

        private string GetCurrentRepairKey()
        {
            return this.autoRepairKeys[this.autoRepairType];
        }

        private bool IsDurabilityToastMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            string s = message.Trim();
            return this.ToastContainsLocalizedPhrase(s, "Tool durability depleted") ||
                   this.ToastContainsLocalizedPhrase(s, "Scanner Durability low");
        }

        private bool CheckForDurabilityNotification()
        {
            try
            {
                // Scan all toast children like HeartopiaBuddy4 does (more reliable than single path)
                GameObject toastPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Tip/TipPanel(Clone)/ToastPanel(Clone)/toasts@t");
                if (toastPanel != null)
                {
                    int childCount = toastPanel.transform.childCount;
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = toastPanel.transform.GetChild(i);
                        if (child != null && child.gameObject.activeInHierarchy)
                        {
                            Transform textTransform = child.Find("AniRoot@ani/root_visible@go/root_visible/value@txt");
                            if (textTransform != null && textTransform.gameObject.activeInHierarchy)
                            {
                                var text = textTransform.GetComponent<Text>();
                                if (text != null && this.IsDurabilityToastMessage(text.text))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private float GetCurrentEnergy()
        {
            try
            {
                string energyStr = this.TryGetCurrentEnergyText();
                if (!string.IsNullOrEmpty(energyStr) && energyStr.Contains("/"))
                {
                    string[] parts = energyStr.Split('/');
                    if (parts.Length >= 2)
                    {
                        string currentDigits = new string(parts[0].Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                        string maxDigits = new string(parts[1].Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                        if (float.TryParse(currentDigits, out float current) && float.TryParse(maxDigits, out float max) && max > 0f)
                        {
                            float ratio = current / max;
                            this.lastKnownEnergyRatio = ratio;
                            this.lastKnownEnergyDisplay = energyStr.Trim();
                            return ratio;
                        }
                    }
                }
            }
            catch
            {
            }
            return this.lastKnownEnergyRatio;
        }

        private string GetCurrentEnergyDisplay()
        {
            try
            {
                string energyText = this.TryGetCurrentEnergyText();
                if (!string.IsNullOrEmpty(energyText))
                {
                    this.lastKnownEnergyDisplay = energyText.Trim();
                    return this.lastKnownEnergyDisplay;
                }
            }
            catch
            {
            }

            return this.lastKnownEnergyDisplay;
        }

        private string GetRepairStatusDisplay()
        {
            if (this.IsAutoRepairInProgress())
            {
                return this.L("In Progress");
            }

            if (this.pendingAutoRepairRequest)
            {
                return this.L("Queued");
            }

            return this.L("Ready");
        }

        private string GetAutoEatStatusDisplay()
        {
            if (this.isAutoEating)
            {
                return this.L("In Progress");
            }

            if (this.pendingAutoEatRequest)
            {
                return this.L("Queued");
            }

            return this.L("Ready");
        }

        private void RefreshFoodRepairUiStatusSnapshot(bool force = false)
        {
            float now = Time.unscaledTime;
            if (!force && now < this.nextFoodRepairUiStatusRefreshAt)
            {
                return;
            }

            this.nextFoodRepairUiStatusRefreshAt = now + 1f;
            try
            {
                string energyDisplay = this.GetCurrentEnergyDisplay();
                if (!string.IsNullOrWhiteSpace(energyDisplay))
                {
                    this.cachedFoodRepairEnergyStatusDisplay = energyDisplay;
                }
            }
            catch
            {
            }

            this.cachedToolDurabilityStatusDisplay = this.FormatCachedToolDurabilityStatusDisplay();
        }

        private string GetCurrentToolDurabilityStatusDisplay()
        {
            return this.FormatCachedToolDurabilityStatusDisplay();
        }

        private string FormatCachedToolDurabilityStatusDisplay()
        {
            try
            {
                if (this.lastObservedToolId > 0 && this.lastObservedToolMaxDurability > 0)
                {
                    string toolName = this.GetAutoRepairSupportedToolName(this.lastObservedToolId);
                    if (string.IsNullOrEmpty(toolName))
                    {
                        toolName = "Tool " + this.lastObservedToolId;
                    }

                    float ratio = (float)this.lastObservedToolDurability / (float)this.lastObservedToolMaxDurability;
                    return string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} {1}/{2} ({3:P1})",
                        toolName,
                        this.lastObservedToolDurability,
                        this.lastObservedToolMaxDurability,
                        ratio);
                }
            }
            catch
            {
            }

            return this.L("Unavailable");
        }

        private void DrawFoodRepairStatusRow(Rect labelRect, Rect valueRect, string label, string value, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            GUI.Label(labelRect, label, labelStyle);
            GUI.Label(valueRect, value, valueStyle);
        }

        private bool TryCacheEnergyTextObject()
        {
            if (this.cachedEnergyTextObj != null && this.cachedEnergyTextObj.activeInHierarchy)
            {
                return true;
            }

            if (Time.unscaledTime < this.nextEnergyTextPathScanAt)
            {
                return false;
            }

            this.nextEnergyTextPathScanAt = Time.unscaledTime + 2f;
            this.cachedEnergyTextObj = null;
            this.cachedEnergyTextComponent = null;
            this.cachedEnergyTextProperty = null;

            string[] energyPaths =
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/top_left_layout@go/energy_bar@go@w/root/energy_progress@go/energy_more@slider/energy_progress@txt",
                "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/top_left_layout@go/energy_bar@go@w/root/energy_progress@go/energy_progress@txt",
                "GameApp/startup_root(Clone)/XDRUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/top_left_layout@go/energy_bar@go@w/root/energy_progress@go/energy_more@slider/energy_progress@txt",
                "GameApp/startup_root(Clone)/XDRUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/top_left_layout@go/energy_bar@go@w/root/energy_progress@go/energy_progress@txt"
            };

            for (int i = 0; i < energyPaths.Length; i++)
            {
                GameObject energyText = GameObject.Find(energyPaths[i]);
                if (energyText != null && energyText.activeInHierarchy)
                {
                    this.cachedEnergyTextObj = energyText;
                    return true;
                }
            }

            return false;
        }

        private bool TryParseEnergyText(string energyText, out int current, out int max)
        {
            current = -1;
            max = -1;
            if (string.IsNullOrWhiteSpace(energyText) || !energyText.Contains("/"))
            {
                return false;
            }

            string[] parts = energyText.Split('/');
            if (parts.Length < 2)
            {
                return false;
            }

            string currentDigits = new string(parts[0].Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            string maxDigits = new string(parts[1].Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (!float.TryParse(currentDigits, out float currentFloat) || !float.TryParse(maxDigits, out float maxFloat) || maxFloat <= 0f)
            {
                return false;
            }

            current = Mathf.RoundToInt(currentFloat);
            max = Mathf.RoundToInt(maxFloat);
            this.lastKnownEnergyDisplay = current + "/" + max;
            this.lastKnownEnergyRatio = currentFloat / maxFloat;
            return true;
        }

        private string TryReadCachedEnergyTextValue()
        {
            if (this.cachedEnergyTextObj == null)
            {
                return null;
            }

            try
            {
                if (this.cachedEnergyTextComponent == null)
                {
                    Text text = this.cachedEnergyTextObj.GetComponent<Text>();
                    if (text != null)
                    {
                        this.cachedEnergyTextComponent = text;
                    }
                    else
                    {
                        foreach (Component comp in this.cachedEnergyTextObj.GetComponents<Component>())
                        {
                            if (comp == null)
                            {
                                continue;
                            }

                            Il2CppType ilType = comp.GetIl2CppType();
                            if (ilType != null && ilType.Name == "XDText")
                            {
                                this.cachedEnergyTextComponent = comp;
                                this.cachedEnergyTextProperty = ilType.GetProperty("text");
                                break;
                            }
                        }
                    }
                }

                if (this.cachedEnergyTextComponent is Text unityText && !string.IsNullOrEmpty(unityText.text))
                {
                    return unityText.text;
                }

                if (this.cachedEnergyTextComponent != null)
                {
                    if (this.cachedEnergyTextProperty == null)
                    {
                        Il2CppType ilType = this.cachedEnergyTextComponent.GetIl2CppType();
                        if (ilType != null)
                        {
                            this.cachedEnergyTextProperty = ilType.GetProperty("text");
                        }
                    }

                    if (this.cachedEnergyTextProperty != null)
                    {
                        Il2CppObject value = this.cachedEnergyTextProperty.GetValue(this.cachedEnergyTextComponent);
                        string text = value != null ? value.ToString() : null;
                        if (!string.IsNullOrEmpty(text))
                        {
                            return text;
                        }
                    }
                }
            }
            catch
            {
                this.cachedEnergyTextComponent = null;
                this.cachedEnergyTextProperty = null;
            }

            return null;
        }

        private string TryGetCurrentEnergyText()
        {
            try
            {
                if (!this.TryCacheEnergyTextObject())
                {
                    return null;
                }

                string textValue = this.TryReadCachedEnergyTextValue();
                if (!string.IsNullOrEmpty(textValue) && textValue.Contains("/"))
                {
                    if (this.TryParseEnergyText(textValue.Trim(), out _, out _))
                    {
                        return this.lastKnownEnergyDisplay;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool IsEnergyLow()
        {
            float energyPercent = GetCurrentEnergy();
            return energyPercent <= (Mathf.Clamp(this.autoEatTriggerPercent, 1, 100) / 100f);
        }

        private bool IsEnergyAtOrBelowAutoEatTrigger()
        {
            int threshold = Mathf.Clamp(this.autoEatTriggerPercent, 1, 100);
            if (!this.TryReadEnergy(out int current, out int max) || max <= 0)
            {
                return false;
            }

            float percent = ((float)current / (float)max) * 100f;
            return percent <= threshold;
        }

        private bool IsEnergyFull()
        {
            float energyPercent = GetCurrentEnergy();
            return energyPercent >= 1.0f; // Consider full at 100%
        }

        private bool TryReadEnergy(out int current, out int max)
        {
            float now = Time.unscaledTime;
            if (now < this.nextEnergyValueRefreshAt && this.cachedEnergyMax > 0)
            {
                current = this.cachedEnergyCurrent;
                max = this.cachedEnergyMax;
                return true;
            }

            current = -1;
            max = -1;
            if (!this.TryCacheEnergyTextObject())
            {
                if (this.cachedEnergyMax > 0)
                {
                    current = this.cachedEnergyCurrent;
                    max = this.cachedEnergyMax;
                    return true;
                }
                return false;
            }

            string textValue = this.TryReadCachedEnergyTextValue();
            if (this.TryParseEnergyText(textValue, out current, out max))
            {
                this.cachedEnergyCurrent = current;
                this.cachedEnergyMax = max;
                this.nextEnergyValueRefreshAt = now + EnergyReadCacheInterval;
                return true;
            }

            if (this.cachedEnergyMax > 0)
            {
                current = this.cachedEnergyCurrent;
                max = this.cachedEnergyMax;
                return true;
            }

            return false;
        }

    }
}
