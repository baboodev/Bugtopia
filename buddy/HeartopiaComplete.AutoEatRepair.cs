using HarmonyLib;
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
                // The managed handhold walk that used to run here resolved the InteractSystem and
                // self player through FindLoadedType and never succeeded, so AuraMono below was
                // always the real answer.
                bool handholdOk = false;
                bool canUseExpensiveFallback = now >= this.nextAutoRepairExpensiveDurabilityFallbackAt;
                if (canUseExpensiveFallback)
                {
                    this.nextAutoRepairExpensiveDurabilityFallbackAt = now + AutoRepairExpensiveFallbackRetrySeconds;
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
            this.lastObservedToolDurabilityAt = now;

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
                // Re-arm: the latch normally clears when durability recovers past the reset
                // ratio. If a triggered repair never restored durability (kit ran out mid-way,
                // the kit was never consumed, or the player left the restore aura before it
                // finished), durability stays pinned at/below the threshold and the latch would
                // otherwise disable auto-repair forever. Once the repair machinery is fully idle
                // (state machine AND restore aura) and the re-arm delay elapsed, drop the latch
                // so the next poll can trigger a fresh repair.
                if (this.liveDurabilityLowLatched
                    && liveDurabilityRatio <= repairTriggerRatio
                    && now >= this.liveDurabilityLatchRearmAt
                    && !this.IsAutoRepairActiveOrQueued()
                    && !this.IsRepairAuraActive())
                {
                    this.liveDurabilityLowLatched = false;
                    this.AutoEatRepairLog($"[AutoRepair] Durability still low ({durability}/{maxDurability}) after a triggered repair went idle; latch re-armed.");
                }
                return;
            }

            this.nextLiveDurabilityTriggerAt = now + 1f;
            bool repairTriggered = this.TryHandleDurabilityAutoRepairTrigger($"live durability {toolName} toolId={toolId} ({durability}/{maxDurability}, ratio={liveDurabilityRatio:P1}, threshold={repairTriggerRatio:P0}, reset={repairResetRatio:P0})");
            if (repairTriggered)
            {
                this.liveDurabilityLowLatched = true;
                this.liveDurabilityLatchedToolId = toolId;
                this.liveDurabilityLatchedToolMaxDurability = maxDurability;
                this.liveDurabilityLatchRearmAt = now + LiveDurabilityLatchRearmSeconds;
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
                case 7:
                    return "SeaCleaner"; // ToolType.SeaCleaner — Aura Farm contamination dwell holds it
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

                // Pin the freshly-invoked tool object across the field reads. It is not cached/pinned
                // otherwise, so bdwgc can move/collect it between the invoke and these reads — a window
                // wide enough under a debugger to fault (the Auto Bird Farm enable crash, via
                // CapturePreviousTool -> TryGetCurrentToolInfo). Pin immediately (no managed alloc in
                // between), free in finally.
                uint toolPin = AuraMonoPinNew(toolObj);
                try
                {
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
                finally
                {
                    AuraMonoPinFree(toolPin);
                }
            }
            catch (Exception ex)
            {
                status = "AuraMono ToolSystem exception: " + ex.Message;
                return false;
            }
        }

        // Durability of an ARBITRARY tool — without equipping it.
        //
        // `ToolSystem` keeps `_toolsData` for every tool in `TableToolTypes` (populated in InitData and
        // kept live by the durability sync event), and exposes `public Tool GetTool(int toolId)` —
        // non-generic, one int arg, reference-type return, i.e. a safe `mono_runtime_invoke` target.
        // The shipped auto-repair only ever reads `GetCurrentTool()`, so it is blind to the two tools
        // that are not in hand; the combined-farm coordinator needs all three (rod 3 / scanner 4 /
        // net 5) to decide whether a repair pause is even necessary.
        //
        // Safety envelope is copied from TryGetCurrentToolDurabilityViaAuraMonoToolSystem: shared
        // module/method cache with the same throttle, and the returned `Tool` is PINNED across the
        // field reads (bdwgc can move it between the invoke and the reads). Fails closed when pinning
        // is unavailable — an unpinned walk here is exactly the stale-pointer AV class.
        public unsafe bool TryGetToolDurabilityById(int toolId, out int durability, out int maxDurability, out string status)
        {
            durability = 0;
            maxDurability = 0;
            status = "AuraMono ToolSystem unavailable";

            if (toolId <= 0)
            {
                status = "Tool id missing";
                return false;
            }

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (!AuraMonoPinningAvailable)
                {
                    status = "AuraMono pinning unavailable";
                    return false;
                }

                float now = Time.unscaledTime;
                // The module object is shared with the GetCurrentTool reader, so resolve it only when
                // the shared cache is actually empty — and consume the shared resolve throttle only
                // then. Resolving OUR method off an already-cached object costs nothing and must not
                // burn the other path's retry budget.
                this.cachedAuraMonoToolSystemObj.TryGet(out IntPtr toolSystemObj);
                if (toolSystemObj == IntPtr.Zero)
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

                    this.cachedAuraMonoToolSystemObj.Set(toolSystemObj);
                    this.nextAuraMonoToolSystemResolveAttemptAt = -999f;
                }

                IntPtr getToolMethod = this.cachedAuraMonoToolSystemGetToolMethod;
                if (getToolMethod == IntPtr.Zero)
                {
                    IntPtr toolSystemClass = auraMonoObjectGetClass(toolSystemObj);
                    if (toolSystemClass == IntPtr.Zero)
                    {
                        status = "AuraMono ToolSystem class unavailable";
                        return false;
                    }

                    getToolMethod = this.FindAuraMonoMethodOnHierarchy(toolSystemClass, "GetTool", 1);
                    if (getToolMethod == IntPtr.Zero)
                    {
                        status = "AuraMono ToolSystem.GetTool unavailable";
                        return false;
                    }

                    this.cachedAuraMonoToolSystemGetToolMethod = getToolMethod;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr toolObj;
                int toolIdArg = toolId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&toolIdArg);
                toolObj = auraMonoRuntimeInvoke(getToolMethod, toolSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    // A raised exception means the cached module pointer is suspect — drop it so the
                    // next call re-resolves (same reaction as the GetCurrentTool path).
                    this.cachedAuraMonoToolSystemObj.Clear();
                    this.cachedAuraMonoToolSystemGetToolMethod = IntPtr.Zero;
                    status = "AuraMono GetTool raised";
                    return false;
                }

                if (toolObj == IntPtr.Zero)
                {
                    // GetTool returns null for a toolId that is not in _toolsData. That is a valid
                    // answer about the ARGUMENT, not evidence of a stale cache — keep the cache.
                    status = "Tool " + toolId + " not in ToolSystem._toolsData";
                    return false;
                }

                uint toolPin = AuraMonoPinNew(toolObj);
                if (toolPin == 0)
                {
                    status = "AuraMono tool pin failed";
                    return false;
                }

                try
                {
                    bool hasDurability = this.TryGetMonoIntMember(toolObj, "durability", out durability)
                        || this.TryGetMonoIntMember(toolObj, "_durability", out durability)
                        || this.TryGetMonoIntMember(toolObj, "Durability", out durability);
                    bool hasMaxDurability = this.TryGetMonoIntMember(toolObj, "maxDurability", out maxDurability)
                        || this.TryGetMonoIntMember(toolObj, "_maxDurability", out maxDurability)
                        || this.TryGetMonoIntMember(toolObj, "MaxDurability", out maxDurability);
                    if (!hasDurability || !hasMaxDurability)
                    {
                        status = "AuraMono Tool fields unreadable: " + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(toolObj));
                        return false;
                    }

                    // maxDurability stays 0 for a tool the player has not unlocked (InitData only
                    // fills it from IToolService when the tool component exists) — report that as a
                    // miss so callers never divide by zero or read it as "0 % durability".
                    if (maxDurability <= 0)
                    {
                        status = "Tool " + toolId + " locked or max durability unknown";
                        return false;
                    }

                    status = "OK";
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(toolPin);
                }
            }
            catch (Exception ex)
            {
                status = "AuraMono GetTool exception: " + ex.Message;
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

        // --- Repair-aura window (event-driven) ---
        // A repair kit is not an instant use: the game places a ToolRestorer ENTITY whose sphere
        // aura repairs the tool over time while the player stands inside it
        // (ToolRestorerComponent, SphereShape radius = TableBuffConfig.range). The mod's own
        // repair state machine finishes at "item consumed", long before the aura is done — so
        // "repair busy" must also cover the aura phase or a route teleport yanks the player out
        // of the circle mid-repair.
        //
        // Signals (see docs/GAME_EVENTS_LIST.md + ilspy-dumps):
        //  * ScriptsRefactory.DataAndProtocol.Events.ToolRestorerEvent {itemNetId@0, staticId@4}
        //    — dispatched by ToolRestorerProtocolManager.CanPutRestorerResult when the server
        //    approves the kit throw (fires for BOTH the mod's auto path and manual use). Opens
        //    the window.
        //  * ScriptsRefactory.DataAndProtocol.Events.ToolRestoreDestroyEvent {ownerNetId@0}
        //    — dispatched from ToolRestorerComponent.OnSpawned when the restorer entity actually
        //    lands (ownerNetId = thrower). Refreshes the window at the true aura start.
        // PRECISE state (no timers) — mirrors the game's own UI: SkillWidget highlights the tool
        // button by re-querying ToolSystem.HasToolRestoreBuff() on every UpdateBuffUiEvent
        // {buffId@0} (dispatched for the SELF player only, on every buff add/UPDATE/remove).
        // Tool-restore buff ids are hardcoded in that method as 701..706
        // ((uint)(buffId - 701) <= 5u). HasToolRestoreBuff is a plain no-arg instance method on
        // the ToolSystem module (whose instance the durability reader already resolves/caches),
        // so it is safe to mono_runtime_invoke — no generic inflation involved.
        //   ON edge:  buff-add event (id 701-706) → query returns true.
        //   OFF edge: buff-remove event → query returns false (true→false closes the window).
        // The throw→land gap and any query failure are bridged by the fallback window below
        // (durability early close + hard timeout), so timers only bound the failure modes.
        private const float RepairAuraWindowSeconds = 30f;
        private const float RepairAuraFullDurabilityRatio = 0.99f;
        private const int ToolRestoreBuffIdMin = 701;
        private const int ToolRestoreBuffIdMax = 706;
        private const float RepairBuffReverifySeconds = 5f;
        // How long after a triggered repair the low-durability latch may re-arm if durability
        // never recovered (see the re-arm block in TryHandleLiveDurabilityAutoRepair).
        private const float LiveDurabilityLatchRearmSeconds = 30f;
        private bool repairAuraHooksRegistered;
        private float repairAuraWindowStartedAt = -999f;
        private float repairAuraWindowUntil = -999f;
        private float lastObservedToolDurabilityAt = -999f;
        private bool repairBuffStateKnown;
        private bool repairBuffActive;
        private float repairBuffStateAt = -999f;
        private IntPtr cachedAuraMonoToolSystemHasRestoreBuffMethod = IntPtr.Zero;
        private float nextRepairBuffResolveAttemptAt = -999f;

        public void EnsureRepairAuraEventHooks()
        {
            if (this.repairAuraHooksRegistered)
            {
                return;
            }

            this.repairAuraHooksRegistered = true;
            this.RegisterGameEventHook("ScriptsRefactory.DataAndProtocol.Events.ToolRestorerEvent", 8, e =>
            {
                float now = Time.unscaledTime;
                this.repairAuraWindowStartedAt = now;
                this.repairAuraWindowUntil = now + RepairAuraWindowSeconds;
                this.AutoEatRepairLog("[AutoRepair] ToolRestorerEvent: repair-aura window opened (kit staticId=" + e.ReadInt32(4) + ")");
            });
            this.RegisterGameEventHook("ScriptsRefactory.DataAndProtocol.Events.ToolRestoreDestroyEvent", 4, e =>
            {
                // Fires when a restorer entity spawns; ownerNetId tells whose. Only OUR restorer
                // refreshes the window (a neighbour repairing next to us is not our repair).
                uint ownerNetId = e.ReadUInt32(0);
                if (ownerNetId != 0
                    && this.TryGetSelfPlayerNetId(out uint selfNetId)
                    && selfNetId != 0
                    && ownerNetId == selfNetId)
                {
                    float now = Time.unscaledTime;
                    if (this.repairAuraWindowStartedAt < 0f)
                    {
                        this.repairAuraWindowStartedAt = now;
                    }
                    this.repairAuraWindowUntil = now + RepairAuraWindowSeconds;
                    this.AutoEatRepairLog("[AutoRepair] ToolRestoreDestroyEvent: own restorer landed; repair-aura window refreshed.");
                }
            });
            // The precise channel: every self-buff change dispatches UpdateBuffUiEvent; only the
            // tool-restore ids matter. The handler runs on the main thread (event drain), so the
            // AuraMono query is allowed here.
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.UpdateBuffUiEvent", 4, e =>
            {
                int buffId = e.ReadInt32(0);
                if (buffId < ToolRestoreBuffIdMin || buffId > ToolRestoreBuffIdMax)
                {
                    return;
                }

                if (this.TryQueryToolRestoreBuffActive(out bool active))
                {
                    bool wasActive = this.repairBuffStateKnown && this.repairBuffActive;
                    this.repairBuffStateKnown = true;
                    this.repairBuffActive = active;
                    this.repairBuffStateAt = Time.unscaledTime;
                    if (active)
                    {
                        this.AutoEatRepairLog("[AutoRepair] Tool-restore buff ACTIVE (buffId=" + buffId + ").");
                    }
                    else if (wasActive)
                    {
                        // true→false edge = repair finished; the fallback window is obsolete too.
                        this.repairAuraWindowUntil = -999f;
                        this.AutoEatRepairLog("[AutoRepair] Tool-restore buff ended (buffId=" + buffId + "); repair no longer active.");
                    }
                }
                else
                {
                    // Query failed — drop to the fallback window until the next buff event.
                    this.repairBuffStateKnown = false;
                }
            });
        }

        // --- Event-driven Auto Eat / Auto Repair triggers ---
        // Replaces the poll-only detection (energy = UI-text parse of the energy panel;
        // durability = timed AuraMono read):
        //  * XDTDataAndProtocol.Events.PlayerStaminaUpdatedEvent {CurrentValue@0, BaseMaxValue@4,
        //    BoostedMaxValue@8} — dispatched by PropertySyncSystem on every self energy change
        //    (the same event the game's EnergyModule renders the panel from). Carries the value,
        //    so it feeds the energy cache directly and requests an immediate trigger check.
        //  * XDTDataAndProtocol.Events.Player.HandHoldUpdatedEvent {} — dispatched by ToolSystem
        //    on every ToolComponent update (durability!), skin change and handhold change. Empty
        //    payload, so it only marks tool data dirty; the existing AuraMono durability read
        //    then runs once instead of on a 0.5-1s timer.
        // The old polls remain as safety nets: energy falls back to the UI parse until the first
        // stamina event, durability keeps a stretched 45s poll, and the toast scan re-engages if
        // the event channel goes quiet (game-build drift protection).
        public void EnsureAutoEatRepairEventHooks()
        {
            if (this.autoEatRepairEventHooksRegistered)
            {
                return;
            }

            this.autoEatRepairEventHooksRegistered = true;
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.PlayerStaminaUpdatedEvent", 12, e =>
            {
                int current = e.ReadInt32(0);
                int baseMax = e.ReadInt32(4);
                int boostedMax = e.ReadInt32(8);
                int max = boostedMax > 0 ? boostedMax : baseMax;
                if (max <= 0 || current < 0)
                {
                    return;
                }

                float now = Time.unscaledTime;
                this.cachedEnergyCurrent = current;
                this.cachedEnergyMax = max;
                this.lastKnownEnergyDisplay = current + "/" + max;
                this.nextEnergyValueRefreshAt = now + EventDrivenEnergyCacheSeconds;
                this.staminaEventSeenAt = now;
                this.autoEatCheckRequestedByEvent = true;
            });
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.Player.HandHoldUpdatedEvent", 0, e =>
            {
                float now = Time.unscaledTime;
                this.handholdEventSeenAt = now;
                // Bursts (durability + skin + handhold updates land together) coalesce into one
                // durability check per debounce window.
                if (now >= this.nextEventDurabilityCheckAllowedAt)
                {
                    this.nextEventDurabilityCheckAllowedAt = now + 0.25f;
                    this.durabilityCheckRequestedByEvent = true;
                }
            });
        }

        // --- Silent eat (no animation) ---
        // The game's own eat flow (BackpackEatCommand.OnExecuteAsync) does TWO independent things:
        //   base.player.Cast(PlayerParameterEat)            ← client-only eating ANIMATION clip
        //   CharacterProtocolManager.EatFood(foodNetId)     ← the actual server consume + stamina
        // Only the second one reaches the server, and it goes out at cast START, not when the clip
        // ends — the server cannot tell whether the animation played. So calling EatFood directly
        // eats the food with NO animation, no pose lock, no ~2.3s clip. PlayerEatAction carries no
        // protocol, stat or buff code, so skipping the Cast costs visuals only; stamina and the
        // hunger UI come back from the server (PlayerStaminaUpdatedEvent, hooked above).
        //
        // EatFood is itself a one-liner: SendCommand(new EatFoodNetworkCommand{FoodNetId}) on the
        // defaults needAuthed=true, ChannelType.Reliable. That bare command IS accepted by the
        // server — measured 2026-08-15, two sends through the MCP bridge, backpack 12→11→10 with a
        // RefreshBackPackEvent each time. (An earlier version of this comment claimed a
        // fertilizer-style silent rejection; that was wrong.) Building the command here would be
        // identical on the wire, so the EatFood invoke stays: it is the smaller AuraMono operation
        // — one 1-arg static invoke, versus inflating SendCommand<T>, allocating the struct and
        // setting its field.
        //
        // What IS dropped silently is a stale FoodNetId (netIds are reassigned every game start;
        // the send still reports ok). That is what SendEatForAutoEat's health check catches — on no
        // stamina change, and on any resolve/invoke failure, it falls back to the BagModule Eat
        // function (112, the animated path) for the rest of the session.
        private const float SilentEatVerifySeconds = 2.5f;
        private IntPtr cachedCharacterEatFoodMethod = IntPtr.Zero;
        private bool silentEatPathBroken;
        private float lastSilentEatSentAt = -999f;
        private int lastSilentEatEnergyBefore = -1;

        private unsafe bool TryEatFoodDirectNoAnimation(uint foodNetId, out string status)
        {
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.cachedCharacterEatFoodMethod == IntPtr.Zero)
            {
                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Character", "CharacterProtocolManager");
                }

                if (cls == IntPtr.Zero)
                {
                    status = "CharacterProtocolManager class unresolved";
                    return false;
                }

                this.cachedCharacterEatFoodMethod = this.FindAuraMonoMethodOnHierarchy(cls, "EatFood", 1);
                if (this.cachedCharacterEatFoodMethod == IntPtr.Zero)
                {
                    status = "EatFood method missing";
                    return false;
                }

                this.AutoEatRepairLog("[Auto Eat] CharacterProtocolManager.EatFood resolved OK");
            }

            uint localNetId = foodNetId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&localNetId);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.cachedCharacterEatFoodMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "EatFood exc=0x" + exc.ToInt64().ToString("X");
                this.cachedCharacterEatFoodMethod = IntPtr.Zero;
                return false;
            }

            status = "ok";
            return true;
        }

        // Single funnel for Auto Eat's use-item send: silent EatFood when enabled and healthy,
        // BagModule Eat (112, animated) otherwise.
        private bool SendEatForAutoEat(uint netId)
        {
            if (this.autoEatNoAnimationEnabled && !this.silentEatPathBroken)
            {
                float now = Time.unscaledTime;
                // Health check on the previous silent send: if no stamina event arrived since and
                // the cached energy never rose, the send did not take effect. The command itself is
                // accepted by the server (see above), so the usual cause is a stale FoodNetId, which
                // is dropped silently. Empirical guard — stop burning attempts on the silent path
                // for this session.
                if (this.lastSilentEatSentAt > 0f
                    && now - this.lastSilentEatSentAt >= SilentEatVerifySeconds
                    && this.staminaEventSeenAt < this.lastSilentEatSentAt
                    && this.lastSilentEatEnergyBefore >= 0
                    && this.cachedEnergyCurrent <= this.lastSilentEatEnergyBefore)
                {
                    this.silentEatPathBroken = true;
                    this.AutoEatRepairLog("[Auto Eat] Silent EatFood produced no stamina change; falling back to the animated BagModule path for this session.");
                }

                if (!this.silentEatPathBroken)
                {
                    int energyBefore = this.cachedEnergyCurrent;
                    if (this.TryEatFoodDirectNoAnimation(netId, out string silentStatus))
                    {
                        this.lastSilentEatSentAt = now;
                        this.lastSilentEatEnergyBefore = energyBefore;
                        this.AutoEatRepairLog("[Auto Eat] Silent EatFood sent (no animation) netId=" + netId);
                        return true;
                    }

                    this.AutoEatRepairLog("[Auto Eat] Silent EatFood unavailable (" + silentStatus + "); using BagModule Eat function.");
                }
            }

            bool eatSent = this.TryExecuteDirectBackpackItemFunc(112, netId);
            if (!eatSent)
            {
                this.RejectAutoEatFoodCandidate(netId, this.lastDirectBackpackMatchedStaticId, this.lastDirectBackpackMatchedEntityType, "BagModule Eat (112) failed");
            }

            return eatSent;
        }

        // True once HandHoldUpdatedEvent has fired on this build AND the live durability read
        // recently produced a value — then the toast UI scan adds nothing and is skipped.
        private bool IsDurabilityEventChannelHealthy()
        {
            return this.handholdEventSeenAt > 0f
                && this.lastObservedToolDurabilityAt > 0f
                && Time.unscaledTime - this.lastObservedToolDurabilityAt < ToastScanDurabilityFreshSeconds;
        }

        // ToolSystem.HasToolRestoreBuff(): no-arg bool instance method on the ToolSystem module
        // (shares the durability reader's cached instance). Returns false on any resolve/invoke
        // problem — callers then rely on the fallback window.
        private bool TryQueryToolRestoreBuffActive(out bool active)
        {
            active = false;
            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
                {
                    return false;
                }

                float now = Time.unscaledTime;
                this.cachedAuraMonoToolSystemObj.TryGet(out IntPtr toolSystemObj);
                IntPtr method = this.cachedAuraMonoToolSystemHasRestoreBuffMethod;
                if (toolSystemObj == IntPtr.Zero || method == IntPtr.Zero)
                {
                    if (now < this.nextRepairBuffResolveAttemptAt)
                    {
                        return false;
                    }
                    this.nextRepairBuffResolveAttemptAt = now + 3f;

                    if (toolSystemObj == IntPtr.Zero)
                    {
                        if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Tool.ToolSystem", out toolSystemObj) || toolSystemObj == IntPtr.Zero)
                        {
                            return false;
                        }
                        this.cachedAuraMonoToolSystemObj.Set(toolSystemObj);
                    }

                    IntPtr toolSystemClass = auraMonoObjectGetClass(toolSystemObj);
                    if (toolSystemClass == IntPtr.Zero)
                    {
                        return false;
                    }

                    method = this.FindAuraMonoMethodOnHierarchy(toolSystemClass, "HasToolRestoreBuff", 0);
                    if (method == IntPtr.Zero)
                    {
                        return false;
                    }

                    this.cachedAuraMonoToolSystemHasRestoreBuffMethod = method;
                    this.nextRepairBuffResolveAttemptAt = -999f;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(method, toolSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    // Instance may be stale after a world change — drop it so the next call re-resolves.
                    this.cachedAuraMonoToolSystemObj.Clear();
                    this.cachedAuraMonoToolSystemHasRestoreBuffMethod = IntPtr.Zero;
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                active = Marshal.ReadByte(raw) != 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsRepairAuraActive()
        {
            float now = Time.unscaledTime;

            // Primary: exact buff state, updated by the UpdateBuffUiEvent hook (same source the
            // game's tool-button highlight uses). While the buff reads active, "repairing" holds
            // with no timer involved; a throttled re-verify self-heals a missed remove event
            // (world change can eat it, which would otherwise pin the state active forever).
            if (this.repairBuffStateKnown && this.repairBuffActive)
            {
                if (now - this.repairBuffStateAt > RepairBuffReverifySeconds)
                {
                    if (this.TryQueryToolRestoreBuffActive(out bool stillActive))
                    {
                        this.repairBuffActive = stillActive;
                        this.repairBuffStateAt = now;
                        if (!stillActive)
                        {
                            this.repairAuraWindowUntil = -999f;
                            this.AutoEatRepairLog("[AutoRepair] Tool-restore buff re-verify: no longer active.");
                        }
                    }
                    else
                    {
                        this.repairBuffStateKnown = false;
                    }
                }

                if (this.repairBuffStateKnown && this.repairBuffActive)
                {
                    return true;
                }
            }

            // Fallback window: bridges the kit-throw → restorer-landing gap and any buff-query
            // failure. Bounded by the hard timeout; cut early when durability reads full.
            if (now >= this.repairAuraWindowUntil)
            {
                return false;
            }

            // Early close: the equipped tool reads (near) full durability on a poll taken after
            // the window opened — the aura has done its job, no need to sit out the timeout.
            if (this.lastObservedToolDurabilityAt > this.repairAuraWindowStartedAt + 1f
                && this.lastObservedToolMaxDurability > 0
                && (float)this.lastObservedToolDurability / this.lastObservedToolMaxDurability >= RepairAuraFullDurabilityRatio)
            {
                this.repairAuraWindowUntil = -999f;
                this.AutoEatRepairLog("[AutoRepair] Repair-aura window closed early: durability restored ("
                    + this.lastObservedToolDurability + "/" + this.lastObservedToolMaxDurability + ").");
                return false;
            }

            return true;
        }

        // --- Public accessors for FishingRouteFeature (static class; the fields are private) ---
        public bool IsAutoRepairBusy()
        {
            return this.IsAutoRepairActiveOrQueued() || this.IsRepairAuraActive();
        }

        // Kit-USE execution phase only (USE/VERIFY steps or a queued start): the window in
        // which the game must see an idle player or the ToolRestorer use is silently ignored.
        // Deliberately EXCLUDES the WAIT step between multi-kit uses (18s aura settle) and the
        // restore aura itself — both repair passively, so activities (fishing casts) may resume;
        // only position changes must wait for the full window (IsAutoRepairBusy covers that).
        public bool IsAutoRepairUsePhase()
        {
            return (this.isRepairing && !this.autoRepairWaiting) || this.pendingAutoRepairRequest;
        }

        public bool GetAutoEatEnergyPanelEnabled()
        {
            return this.autoEatAutoTriggerEnabled;
        }

        public void SetAutoEatEnergyPanelEnabled(bool value)
        {
            this.autoEatAutoTriggerEnabled = value;
        }

        public bool GetAutoRepairOnDurabilityEnabled()
        {
            return this.autoRepairOnToastEnabled;
        }

        // The durability ratio at/below which auto-repair fires (1-100 %, default 10). The combined
        // farm's repair cycle uses the SAME number to decide which stowed tools are worth a pass, so
        // the two can never disagree about what "low" means.
        public int GetAutoRepairTriggerPercent()
        {
            return Mathf.Clamp(this.autoRepairTriggerPercent, 1, 100);
        }

        public void SetAutoRepairOnDurabilityEnabled(bool value)
        {
            this.autoRepairOnToastEnabled = value;
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
            // Once the HandHoldUpdatedEvent channel is proven on this build, durability checks
            // are event-driven and the timed poll is only a safety net.
            if (this.handholdEventSeenAt > 0f)
            {
                return EventDrivenDurabilitySafetyPollSeconds;
            }

            return this.AreHeavyFarmAutomationsActive() ? FarmActiveToolDurabilityPollInterval : ToolDurabilityPollInterval;
        }

        // External request for an immediate tool-durability check (e.g. AutoFishingFarm on each
        // cast-cycle end, BirdNetFarm on each catch tick). Same channel the HandHoldUpdatedEvent uses:
        // the OnUpdate poll picks up the flag next frame and runs TryHandleLiveDurabilityAutoRepair once,
        // bypassing the timed poll — so fast animation-skipped farming repairs the tool before it snaps.
        // Throttled to ≤1/s: bird multi-catch can confirm captures every frame, and each honoured request
        // forces a fresh AuraMono durability read — 1/s is ample for gradual wear. No-op unless
        // auto-repair-on-toast is enabled.
        private float nextRequestedDurabilityCheckAt = -999f;
        public void RequestDurabilityCheck()
        {
            if (!this.autoRepairOnToastEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.nextRequestedDurabilityCheckAt)
            {
                return;
            }

            this.nextRequestedDurabilityCheckAt = now + 1f;
            this.durabilityCheckRequestedByEvent = true;
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

        // Direct repair-kit throw: send PutRecoverToolCommand immediately via the game's own static
        // wrapper ToolRestorerProtocolManager.NotifyThrowToolRestorer(uint, Vector3, Quaternion, uint).
        // Skips the CanPut server round-trip, the Free-state interaction gate and the ~1.5-2s throw
        // animation + flight — the command is on the wire instantly; the server spawns the device and
        // applies the repair buff exactly as with the normal path.
        private unsafe bool TryThrowToolRestorerDirectMono(uint itemNetId, out string status)
        {
            status = "direct restorer throw unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "direct restorer Mono runtime unavailable";
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.ItemDisplay.ToolRestorerProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    status = "ToolRestorerProtocolManager class unavailable";
                    return false;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "NotifyThrowToolRestorer", 4);
                if (method == IntPtr.Zero)
                {
                    status = "NotifyThrowToolRestorer(4) unavailable";
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "player position unavailable";
                    return false;
                }

                Vector3 forward = Vector3.forward;
                Quaternion rotation = Quaternion.identity;
                GameObject playerRoot = this.FindPlayerRoot();
                if (playerRoot != null)
                {
                    forward = playerRoot.transform.forward;
                    forward.y = 0f;
                    forward = forward.sqrMagnitude < 0.0004f ? Vector3.forward : forward.normalized;
                    rotation = playerRoot.transform.rotation;
                }

                string targetHow;
                Vector3 targetPos = this.ComputeToolRestorerThrowTarget(playerPos, forward, out targetHow);
                this.lastToolRestorerThrowPlacement = targetHow;
                // Unconditional, a few lines per session (not a per-frame trace): where the kit went
                // is invisible in-world once it lands, and AutoEatRepairLog is compiled out in ship
                // builds — this is the only record of what the placement resolved to.
                ModLogger.Msg("[AutoRepair] throw target -> " + targetHow + " @ " + FormatToolRestorerVec(targetPos)
                    + " player=" + FormatToolRestorerVec(playerPos) + " fwd=" + FormatToolRestorerVec(forward));

                uint netIdArg = itemNetId;
                Vector3 posArg = targetPos;
                Quaternion rotArg = rotation;
                uint parentArg = 0u; // ground placement; ship/platform parenting not handled here

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[4];
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&posArg);
                args[2] = (IntPtr)(&rotArg);
                args[3] = (IntPtr)(&parentArg);
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "NotifyThrowToolRestorer exception";
                    this.AutoEatRepairLog("[AutoRepair] Direct restorer Mono exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                // Open the repair-aura fallback window ourselves: the legacy path got this from
                // ToolRestorerEvent (the CanPut reply we skip). Keeps IsRepairAuraActive() /
                // IsAutoRepairBusy() continuous across the send → device-landing gap; the precise
                // buff channel takes over once the device lands.
                float windowNow = Time.unscaledTime;
                this.repairAuraWindowStartedAt = windowNow;
                this.repairAuraWindowUntil = windowNow + RepairAuraWindowSeconds;

                status = "PutRecoverToolCommand sent (direct) target=" + targetPos + " [" + targetHow + "]";
                return true;
            }
            catch (Exception ex)
            {
                status = "direct restorer failed: " + ex.Message;
                return false;
            }
        }

        // Throw-path switch. OFF (the default since 2026-08-06) = the game throw: BagModule func
        // 113 -> CanPut -> BackpackToolRestorer (10104) -> the throw clip, with the game resolving
        // the landing spot itself (real ground raycast + parentNetId) and RepairThrowAnimationTrim
        // cutting nearly all of the animation. "Instant Direct Throw" ON = the direct Mono send,
        // which skips the CanPut round-trip, the PlayerState.Free gate and the clip entirely — the
        // choice when a repair has to land mid-fishing — but has to place the kit geometrically.
        // The bag function stays the fallback if that direct send fails.
        private bool TryUseRepairKitByNetId(uint netId)
        {
            if (!this.autoRepairNoAnimationEnabled)
            {
                this.AutoEatRepairLog("[AutoRepair] Animated throw selected; using the game's BagModule func 113 path.");
                // Opens the trim feature's fast poll window (no-op while its toggle is off), so the
                // common case costs no polling at all — see RepairThrowAnimationTrimFeature.
                this.NotifyRepairThrowAnimationStarted();
                return this.TryExecuteDirectBackpackItemFunc(113, netId);
            }

            if (this.TryThrowToolRestorerDirectMono(netId, out string directStatus))
            {
                this.AutoEatRepairLog("[AutoRepair] Direct restorer throw: " + directStatus);
                return true;
            }

            this.AutoEatRepairLog("[AutoRepair] Direct restorer throw failed (" + directStatus + "); falling back to BagModule func 113.");
            return this.TryExecuteDirectBackpackItemFunc(113, netId);
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
                this.AutoEatRepairLog("[AutoRepair] Direct repair matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + "; throwing restorer directly.");
                return this.TryUseRepairKitByNetId(netId);
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

            this.AutoEatRepairLog("[AutoRepair] Cached repair kit matched netId=" + this.cachedRepairKitNetId + " count=" + currentCount + "; throwing restorer directly.");
            if (this.TryUseRepairKitByNetId(this.cachedRepairKitNetId))
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

            if (!this.IsAutoEatFoodCandidate(this.cachedFoodNetId, this.cachedFoodStaticId, this.cachedFoodEntityType, out string cachedRejectReason))
            {
                this.AutoEatRepairLog("[Auto Eat] Cached food dropped: netId=" + this.cachedFoodNetId + " staticId=" + this.cachedFoodStaticId + " (" + cachedRejectReason + ")");
                this.ClearCachedFood();
                return false;
            }

            this.lastDirectBackpackMatchedNetId = this.cachedFoodNetId;
            this.lastDirectBackpackMatchedStaticId = this.cachedFoodStaticId;
            this.lastDirectBackpackMatchedEntityType = this.cachedFoodEntityType;
            this.lastDirectBackpackMatchedCount = currentCount;
            this.cachedFoodCount = currentCount;

            this.AutoEatRepairLog("[Auto Eat] Cached food matched netId=" + this.cachedFoodNetId + " count=" + currentCount + "; sending eat.");
            if (this.SendEatForAutoEat(this.cachedFoodNetId))
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

        private void TryUseAttractorFromBagWithNotification()
        {
            if (Time.unscaledTime < this.nextUseAttractorAllowedAt)
            {
                return;
            }

            if (this.TryUseAttractorFromBag())
            {
                this.AddMenuNotification(this.L("Attractor used"), new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.AddMenuNotification(this.L("No attractor found in bag"), new Color(1f, 0.65f, 0.45f));
            }
        }

        // Routes the auto-fishing "Auto Bait" feature to the chosen item (Bait vs Attractor),
        // reusing the same backpack item-function path as the manual Use Bait / Use Attractor hotkeys.
        public bool TryThrowFishBaitForAuto(bool useBait, out string kind)
        {
            kind = useBait ? "bait" : "attractor";
            return useBait ? this.TryUseBaitFromBag() : this.TryUseAttractorFromBag();
        }

        // Direct bait/attractor throw without the ~1.5-2s spread animation, same pattern as the direct
        // repair-kit throw: AuraMono-invoke the game's own network sender at the end of its throw chain.
        // Bait  = FishingProtocolManager.CmdScatterBait(uint baitNetId, Vector3 position) -> SpawnFishByBaitCommand.
        // Lure  = FishingProtocolManager.CmdUseFishTrapDevice(uint deviceNetId, Vector3 pos, Quaternion rot) -> SpawnFishRefreshDeviceCommand.
        // Both are pure WebRequestUtility.SendCommand (no local anim). Position computed ahead of the
        // player (the game's own CanThrowAutoBait has an out-Vector3 that's unsafe via mono invoke).
        // 3m matches the game's standard: FishGear searches 3–5m (FishGearMinLength=3), toolRestorerLength=3.
        private const float BaitThrowDistance = 4f;         // bait / attractor forward throw distance
        private const float ToolRestorerThrowDistance = 3f; // repair-kit DEFAULT forward throw distance
        // Bound for every repair-kit throw offset axis, applied as +/- this value: the restorer's
        // aura radius, TableBuffConfig.range for the tool-restore buffs 701-706 (5.0 in every row,
        // and ToolRestorerComponent feeds exactly that field into its sphere Radius). Past it the
        // player standing where they threw is outside the aura and the kit repairs nothing, so it is
        // the only honest bound for a user-set offset.
        private const float ToolRestorerThrowMaxOffset = 5f;

        // ----------------------------------------------------------------------------------------
        // Repair-kit placement
        //
        // The restorer device is spawned at EXACTLY the position carried by PutRecoverToolCommand —
        // no gravity, no settling (ToolRestorerProtocolManager.CmdAddToolRestorer writes
        // TransformComponentData.position verbatim). The game does not send a raw aim point either:
        // BackpackToolRestorer builds aim = pos + forward*toolRestorerLength(3) + up*toolRestorerHeight(2),
        // hands it to PlayerSphereChecker.TryFindThrowPoint (line-of-sight test -> downward ground
        // raycast within toolRestorerHeightLimit(3)) and sinks the hit by toolRestoreSinkHeight(0.3).
        //
        // WE CANNOT REPLAY THAT GROUND SNAP. Measured live 2026-08-06: every UnityEngine.Physics
        // cast from the mod returns nothing — every distance, straight down under a standing player,
        // 40m reach, layer mask ~(1<<2). The reason is in the game's own binding layer: its
        // XDRaycastHit carries `int xdCollider` resolved through XDCollider.GetColliderFromIndex,
        // i.e. XDT.Physics is a SELF-CONTAINED physics engine with its own collider registry, not
        // Unity PhysX. There are no Unity colliders in the scene to hit, so Physics.Raycast /
        // Linecast / RaycastAll are permanently blind here (this also means the ESP ground-ring
        // casts have always silently fallen back to their anchor position). Reaching the real
        // colliders would mean AuraMono-invoking MonoGame.ScriptFramework.PhysicsExtension — its
        // out-XDRaycastHit overloads are struct-outs (stack corruption through raw
        // mono_runtime_invoke) so it would have to go through the bool-only
        // Raycast(Vector3,Vector3,float,int) overload plus a bisection on maxDistance, with
        // overload disambiguation by parameter type. Not built.
        //
        // So placement is purely geometric, and the two modes differ in what they trade away:
        //   at feet  = playerPos: the player is standing on the ground, so this is exactly ground
        //              level by construction, and a zero offset leaves the whole 5m aura as margin.
        //   offset   = playerPos + right*X + up*Y + forward*Z, at the PLAYER'S height plus Y. The
        //              three components are user-set (Features -> Food & Repair), each bounded to
        //              +/-ToolRestorerThrowMaxOffset; the default 0/0/3 is the fixed 3m-ahead throw
        //              this used to hard-code. The axes are the PLAYER'S local ones (Unity's own
        //              X = right, Y = up, Z = forward), not world axes, so a tuned spot stays put
        //              relative to the character however they turn. Matches the game whenever the
        //              ground under the resolved point is level with the player, which is the
        //              normal case; over a ledge, a slope, water or mid-jump it will hang in the
        //              air. That is the cost of the mode, and the reason the at-feet toggle exists.
        //              The bound is PER AXIS, not on the magnitude: 5/5/5 still reaches 8.7m, well
        //              outside the aura, so it is a sanity rail rather than a guarantee.
        // Both apply the game's 0.3m sink so the device sits in the ground rather than on top of it.
        private const float ToolRestorerSinkHeight = 0.3f;  // LevelScriptableConfig.toolRestoreSinkHeight

        // Last resolved placement, surfaced in the Repair Status row and the throw log line.
        private string lastToolRestorerThrowPlacement = "";

        private static string FormatToolRestorerVec(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
        }

        // Renders the resolved offset for the Repair Status row and the throw log line: only the
        // axes actually in play, so the common single-axis default still reads "z 3m".
        private static string FormatToolRestorerThrowPlacement(float offsetX, float offsetY, float offsetZ)
        {
            string text = "";
            if (offsetX != 0f)
            {
                text = "x " + offsetX.ToString("0.#") + "m";
            }
            if (offsetY != 0f)
            {
                text += (text.Length > 0 ? " + " : "") + "y " + offsetY.ToString("0.#") + "m";
            }
            if (offsetZ != 0f)
            {
                text += (text.Length > 0 ? " + " : "") + "z " + offsetZ.ToString("0.#") + "m";
            }
            return text.Length > 0 ? text : "at player";
        }

        private Vector3 ComputeToolRestorerThrowTarget(Vector3 playerPos, Vector3 forward, out string how)
        {
            Vector3 sink = new Vector3(0f, ToolRestorerSinkHeight, 0f);
            if (this.autoRepairThrowAtFeetEnabled)
            {
                how = "at feet";
                return playerPos - sink;
            }

            // Clamped here as well as in the UI and on config load: this is the only place the
            // numbers reach the wire, and a hand-edited config must not be able to fling a kit
            // across the map.
            float offsetX = Mathf.Clamp(this.autoRepairThrowOffsetX, -ToolRestorerThrowMaxOffset, ToolRestorerThrowMaxOffset);
            float offsetY = Mathf.Clamp(this.autoRepairThrowOffsetY, -ToolRestorerThrowMaxOffset, ToolRestorerThrowMaxOffset);
            float offsetZ = Mathf.Clamp(this.autoRepairThrowOffsetZ, -ToolRestorerThrowMaxOffset, ToolRestorerThrowMaxOffset);

            // Player-frame right, derived from the SAME flattened forward the caller resolved rather
            // than from transform.right, so the two axes cannot disagree about the facing (and X
            // stays horizontal even if the root is ever pitched).
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            how = FormatToolRestorerThrowPlacement(offsetX, offsetY, offsetZ);
            return playerPos
                + right * offsetX
                + new Vector3(0f, offsetY, 0f)
                + forward * offsetZ
                - sink;
        }

        private bool TryComputeBaitThrowTarget(out Vector3 targetPos)
        {
            targetPos = Vector3.zero;
            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos) || playerPos == Vector3.zero)
            {
                return false;
            }

            Vector3 forward = Vector3.forward;
            GameObject pr = this.FindPlayerRoot();
            if (pr != null)
            {
                forward = pr.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude < 0.0004f ? Vector3.forward : forward.normalized;
            }

            targetPos = playerPos + forward * BaitThrowDistance;
            return true;
        }

        private unsafe bool TryScatterBaitDirectMono(uint itemNetId, out string status)
        {
            status = "direct bait unavailable";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "direct bait Mono runtime unavailable";
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (cls == IntPtr.Zero) { status = "FishingProtocolManager class unavailable"; return false; }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "CmdScatterBait", 2);
                if (method == IntPtr.Zero) { status = "CmdScatterBait(2) unavailable"; return false; }

                if (!this.TryComputeBaitThrowTarget(out Vector3 targetPos)) { status = "bait target unavailable"; return false; }

                uint netIdArg = itemNetId;
                Vector3 posArg = targetPos;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&posArg);
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "CmdScatterBait exception";
                    this.AutoEatRepairLog("[UseBait] Direct scatter Mono exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "SpawnFishByBaitCommand sent (direct) target=" + targetPos;
                return true;
            }
            catch (Exception ex)
            {
                status = "direct bait failed: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryUseAttractorDirectMono(uint itemNetId, out string status)
        {
            status = "direct attractor unavailable";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "direct attractor Mono runtime unavailable";
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (cls == IntPtr.Zero) { status = "FishingProtocolManager class unavailable"; return false; }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "CmdUseFishTrapDevice", 3);
                if (method == IntPtr.Zero) { status = "CmdUseFishTrapDevice(3) unavailable"; return false; }

                if (!this.TryComputeBaitThrowTarget(out Vector3 targetPos)) { status = "attractor target unavailable"; return false; }

                Quaternion rotation = Quaternion.identity;
                GameObject pr = this.FindPlayerRoot();
                if (pr != null) { rotation = pr.transform.rotation; }

                uint netIdArg = itemNetId;
                Vector3 posArg = targetPos;
                Quaternion rotArg = rotation;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&posArg);
                args[2] = (IntPtr)(&rotArg);
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "CmdUseFishTrapDevice exception";
                    this.AutoEatRepairLog("[UseAttractor] Direct trap Mono exception ptr=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "SpawnFishRefreshDeviceCommand sent (direct) target=" + targetPos;
                return true;
            }
            catch (Exception ex)
            {
                status = "direct attractor failed: " + ex.Message;
                return false;
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

                // Skip Bait Animation ON: send SpawnFishByBaitCommand directly; func path is the fallback.
                bool skipAnim = AutoFishingFarm.GetSkipBaitAnimEnabled();
                bool sent = false;
                if (skipAnim)
                {
                    if (this.TryScatterBaitDirectMono(netId, out string directStatus))
                    {
                        this.AutoEatRepairLog("[UseBait] Direct scatter netId=" + netId + ": " + directStatus);
                        sent = true;
                    }
                    else
                    {
                        this.AutoEatRepairLog("[UseBait] Direct scatter failed (" + directStatus + "); falling back to ChumBait function.");
                    }
                }

                if (!sent)
                {
                    this.AutoEatRepairLog("[UseBait] Matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + "; sending ChumBait function.");
                    if (!this.TryExecuteDirectBackpackItemFunc(BackpackFuncChumBait, netId))
                    {
                        this.AutoEatRepairLog("[UseBait] ExecuteBackpackItemFunc failed for netId=" + netId);
                        return false;
                    }
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

        private bool TryUseAttractorFromBag()
        {
            try
            {
                if (Time.unscaledTime < this.nextUseAttractorAllowedAt)
                {
                    return false;
                }

                if (!this.TryFindDirectBackpackItemByStaticId(AttractorStaticId, out uint netId) || netId == 0U)
                {
                    this.AutoEatRepairLog("[UseAttractor] Backpack attractor not found for staticId=" + AttractorStaticId);
                    return false;
                }

                // Skip Bait Animation ON: send SpawnFishRefreshDeviceCommand directly; func path is the fallback.
                bool skipAnim = AutoFishingFarm.GetSkipBaitAnimEnabled();
                bool sent = false;
                if (skipAnim)
                {
                    if (this.TryUseAttractorDirectMono(netId, out string directStatus))
                    {
                        this.AutoEatRepairLog("[UseAttractor] Direct trap netId=" + netId + ": " + directStatus);
                        sent = true;
                    }
                    else
                    {
                        this.AutoEatRepairLog("[UseAttractor] Direct trap failed (" + directStatus + "); falling back to FishingLureBall function.");
                    }
                }

                if (!sent)
                {
                    this.AutoEatRepairLog("[UseAttractor] Matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + "; sending FishingLureBall function.");
                    if (!this.TryExecuteDirectBackpackItemFunc(BackpackFuncFishingLureBall, netId))
                    {
                        this.AutoEatRepairLog("[UseAttractor] ExecuteBackpackItemFunc failed for netId=" + netId);
                        return false;
                    }
                }

                this.nextUseAttractorAllowedAt = Time.unscaledTime + UseAttractorCooldownSeconds;
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[UseAttractor] Exception: " + ex.Message);
                return false;
            }
        }

        // ── Auto Eat candidate validation ───────────────────────────────────────────────────────
        //
        // Item lookups match on the descriptor, which is a prefab/icon name — so a DECORATION that
        // merely looks like food matches too: `p_food_oroll_award` (staticId 301782, entityType 50)
        // contains "p_food", and `p_food_bakemushroom_award` even matches the "Bake Mushroom"
        // preset key. Feeding one to BagModule.ExecuteBackpackItemFunc(Eat) is fatal to the call:
        //
        //     int[] eatAction = TableData.GetEatable(item.staticId).eatAction;   // GetEatable → null
        //
        // The NRE surfaces as a Mono exception on our invoke, the protocol fallback is stubbed out,
        // and Auto Eat gives up — then re-picks the same decoration 5 s later, forever, while
        // energy drains to zero. (Seen in the field: energy 1/100 with food in the bag.)
        //
        // Guard = the item's real entity type. Every one of the 477 rows in the game's Eatable
        // table sits in exactly one of these three EntityType rows, so this is not a heuristic:
        //   25 fruit          40101-40999
        //   45 food           45101-45999
        //   97 normalmushroom 48000-48999
        private const int EatableEntityTypeFruit = 25;
        private const int EatableEntityTypeFood = 45;
        private const int EatableEntityTypeMushroom = 97;

        // Items that made the game throw on Eat. Keyed by both ids because a decoration stacks:
        // rejecting only the netId would just pick the next copy of the same thing.
        private readonly HashSet<uint> autoEatRejectedFoodNetIds = new HashSet<uint>();
        private readonly HashSet<int> autoEatRejectedFoodStaticIds = new HashSet<int>();

        private static bool IsEatableEntityType(int entityType)
        {
            return entityType == EatableEntityTypeFood
                || entityType == EatableEntityTypeFruit
                || entityType == EatableEntityTypeMushroom;
        }

        // Fallback for when the entity type could not be read: the same three types as id ranges.
        private static bool IsEatableStaticIdRange(int staticId)
        {
            return (staticId >= 40101 && staticId <= 40999)
                || (staticId >= 45101 && staticId <= 45999)
                || (staticId >= 48000 && staticId <= 48999);
        }

        private bool IsAutoEatFoodCandidate(uint netId, int staticId, int entityType, out string rejectReason)
        {
            if (netId != 0U && this.autoEatRejectedFoodNetIds.Contains(netId))
            {
                rejectReason = "netId rejected earlier this session";
                return false;
            }

            if (staticId > 0 && this.autoEatRejectedFoodStaticIds.Contains(staticId))
            {
                rejectReason = "staticId rejected earlier this session";
                return false;
            }

            if (entityType > 0)
            {
                if (IsEatableEntityType(entityType))
                {
                    rejectReason = null;
                    return true;
                }

                rejectReason = "entityType " + entityType + " is not an eatable type";
                return false;
            }

            if (staticId > 0)
            {
                if (IsEatableStaticIdRange(staticId))
                {
                    rejectReason = null;
                    return true;
                }

                rejectReason = "staticId " + staticId + " is outside every eatable id range";
                return false;
            }

            // Neither id readable — no worse than before this guard existed; let it through and let
            // RejectAutoEatFoodCandidate deal with the failure if it turns out to be inedible.
            rejectReason = null;
            return true;
        }

        // Called when a send for this candidate failed. Blacklists it so the next attempt picks a
        // DIFFERENT item instead of retrying the same one until energy hits zero.
        private void RejectAutoEatFoodCandidate(uint netId, int staticId, int entityType, string reason)
        {
            // A provably edible item that failed hit a transient fault (stale module pointer,
            // fishing mode, server refusal) — blacklisting real food over that would be worse than
            // the bug this guard exists for.
            if (IsEatableEntityType(entityType) || (entityType <= 0 && staticId > 0 && IsEatableStaticIdRange(staticId)))
            {
                FeatureLog.Fail("AutoEat", "Eat failed for edible item netId=" + netId + " staticId=" + staticId
                    + " entityType=" + entityType + " (" + reason + "); keeping it as a candidate.");
                this.ClearCachedFood();
                return;
            }

            bool added = false;
            if (netId != 0U)
            {
                added |= this.autoEatRejectedFoodNetIds.Add(netId);
            }

            if (staticId > 0)
            {
                added |= this.autoEatRejectedFoodStaticIds.Add(staticId);
            }

            if (added)
            {
                FeatureLog.Fail("AutoEat", "Item is not food - excluded from Auto Eat for this session. netId=" + netId
                    + " staticId=" + staticId + " entityType=" + entityType + " (" + reason + ")");
            }

            this.ClearCachedFood();
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

                if (!this.TryFindDirectBackpackItem(foodKey, anyFood, out uint netId, true) || netId == 0U)
                {
                    FeatureLog.Fail("AutoEat", "No usable food found in the backpack for " + this.GetAutoEatFoodOptionLabel(this.autoEatFoodType) + ".");
                    this.ClearCachedFood();
                    this.ShowMissingFoodNotification();
                    return false;
                }

                this.CacheFoodMatch(foodKey, anyFood);
                this.AutoEatRepairLog("[Auto Eat] Direct food matched netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + " entityType=" + this.lastDirectBackpackMatchedEntityType + "; sending eat.");
                return this.SendEatForAutoEat(netId);
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
                        if (!this.IsEdibleBagFoodSprite(spriteName))
                        {
                            FeatureLog.Once("AutoEat", "picker-skip:" + spriteName, "Custom Food picker skipped " + spriteName + " - it is not an edible item.");
                            continue;
                        }

                        foodList.Add(spriteName);
                        this.CacheScannedBagFoodDisplayName(spriteName);
                        if (!this.scannedBagFoodTextures.ContainsKey(spriteName)
                            && this.autoSellBagItemTextures.TryGetValue(spriteName, out Texture2D directFoodTexture)
                            && directFoodTexture != null)
                        {
                            this.scannedBagFoodTextures[spriteName] = directFoodTexture;
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

        // The Custom Food picker offers whatever the open bag shows whose SPRITE NAME reads like
        // food — and a decoration's sprite reads exactly like food:
        // `ui_item_normal_p_food_bakemushroom_award` (staticId 302311, entityType 50 decoration)
        // even matches the "Bake Mushroom" keyword. Picking one is a dead end now that Auto Eat
        // validates its candidates — the eat lookup skips it and reports "no usable food" — so the
        // list resolves each sprite back to a real bag item and applies the same test.
        //
        // Both sources carry StaticId + EntityType; a sprite that resolves to neither is left in
        // the list, exactly as before this filter existed.
        private bool IsEdibleBagFoodSprite(string spriteName)
        {
            string normalizedSprite = this.NormalizeAutoSellMatchKey(spriteName);
            if (string.IsNullOrWhiteSpace(normalizedSprite))
            {
                return true;
            }

            try
            {
                if (this.TryRefreshDirectBackpackRuntimeSnapshot(false))
                {
                    for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
                    {
                        DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                        if (item == null || !this.DoesRuntimeBackpackItemMatchSprite(item, normalizedSprite))
                        {
                            continue;
                        }

                        return this.IsAutoEatFoodCandidate(0U, item.StaticId, item.EntityType, out _);
                    }
                }

                if (this.autoSellBagItems != null)
                {
                    for (int i = 0; i < this.autoSellBagItems.Count; i++)
                    {
                        AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                        if (entry == null || !this.DoesBagItemEntryMatchSprite(entry, normalizedSprite))
                        {
                            continue;
                        }

                        return this.IsAutoEatFoodCandidate(0U, entry.StaticId, entry.EntityType, out _);
                    }
                }
            }
            catch
            {
            }

            return true;
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
            // Event-fed cache is authoritative while stamina events are fresh — skip the UI read.
            if (Time.unscaledTime - this.staminaEventSeenAt < EventDrivenEnergyCacheSeconds)
            {
                return this.lastKnownEnergyDisplay;
            }

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

            // Where the last direct throw actually put the kit — the tiers degrade silently, so this
            // is the only way to tell "ground 3m" from "at feet" without a debug build.
            if (!string.IsNullOrEmpty(this.lastToolRestorerThrowPlacement))
            {
                return this.L("Ready") + " · " + this.lastToolRestorerThrowPlacement;
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
