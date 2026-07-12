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
        private float CalculateAutoFarmTabHeight()
        {
            if (this.autoFarmSubTab == 1)
            {
                return this.CalculateNewSubTabHeight();
            }
            if (this.autoFarmSubTab == 2)
            {
                return 980f; // Insect farm tab height estimate
            }
            if (this.autoFarmSubTab == 3)
            {
                return 980f; // Bird farm tab height estimate
            }

            // Main tab content - estimate based on typical layout
            return 820f; // Conservative estimate for main foraging tab
        }

        private string GetForagingModeLabel()
        {
            return this.auraFarmEnabled ? "Aura Farm" : "No mode";
        }

        private string GetForagingStatusDisplayText(bool compact)
        {
            if (!this.autoFarmActive)
            {
                return "Idle";
            }

            string status = this.autoFarmStatus;
            if (string.IsNullOrWhiteSpace(status)
                || status == "READY"
                || status == "Idle"
                || status == "NO_TOGGLES"
                || status == "NO_TOGGLES_ERROR"
                || status == "RADAR_OFF_ERROR"
                || status == "MODE_REQUIRED_ERROR")
            {
                return compact ? "Running" : "Running (" + this.GetForagingModeLabel() + ")";
            }

            if (!compact)
            {
                return status;
            }

            if (status.StartsWith("Collecting", StringComparison.OrdinalIgnoreCase))
                return "Collecting";
            if (status.StartsWith("Cleaning", StringComparison.OrdinalIgnoreCase))
                return "Cleaning";
            if (status.StartsWith("Paused", StringComparison.OrdinalIgnoreCase))
                return "Paused";
            if (status.StartsWith("Sea cleaner depleted", StringComparison.OrdinalIgnoreCase))
                return "Repair Needed";
            if (status.StartsWith("Scanning", StringComparison.OrdinalIgnoreCase))
                return "Scanning";
            if (status.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
                return "Loading";
            if (status.StartsWith("Moving", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Going", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Teleporting", StringComparison.OrdinalIgnoreCase))
                return "Moving";
            if (status.StartsWith("Farming", StringComparison.OrdinalIgnoreCase))
                return "Farming";
            if (status.StartsWith("Adjusting camera", StringComparison.OrdinalIgnoreCase))
                return "Camera Fix";
            if (status.StartsWith("Rechecking", StringComparison.OrdinalIgnoreCase))
                return "Rechecking";
            if (status.StartsWith("No nodes found", StringComparison.OrdinalIgnoreCase))
                return "Cycling";
            if (status.StartsWith("Node found", StringComparison.OrdinalIgnoreCase))
                return "Node Found";

            return status;
        }

        private void SetAutoFarmSubTab(int subTab)
        {
            if (this.autoFarmSubTab != subTab)
            {
                this.autoFarmSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
            }
        }

        // Token: 0x06000008 RID: 8 RVA: 0x00002AA0 File Offset: 0x00000CA0
        private float DrawAutoFarmTab(int startY)
        {
            if (this.autoFarmSubTab == 1)
            {
                return this.DrawNewSubTab(startY);
            }
            if (this.autoFarmSubTab == 2)
            {
                return InsectNetFarm.DrawSection(this, startY);
            }
            if (this.autoFarmSubTab == 3)
            {
                return BirdNetFarm.DrawSection(this, startY);
            }

            float left = 20f;
            float panelWidth = 580f;
            int num = startY;
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color panelFill = new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
            Color panelLine = new Color(accent.r, accent.g, accent.b, 0.24f);
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 12 };
            sectionStyle.normal.textColor = accent;
            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            bodyStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };

            bool hasForagingMode = this.auraFarmEnabled;
            bool hasRadarLoot = this.AnyRadarLootToggleEnabled();
            string actionText = this.autoFarmActive ? "STOP FORAGING" : "START FORAGING";
            Rect statusPanel = new Rect(left, (float)num, panelWidth, 112f);
            this.DrawExentriSectionPanel(statusPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(statusPanel.x + 14f, statusPanel.y + 12f, statusPanel.width - 28f, 18f), this.L("FORAGING"), sectionStyle);
            if (this.DrawPrimaryActionButton(new Rect(statusPanel.x + 14f, statusPanel.y + 48f, 190f, 32f), actionText))
            {
                this.ToggleAutoFarm();
            }

            string statusText;
            Color statusColor;
            if (!hasRadarLoot)
            {
                statusText = "Select radar loot first";
                statusColor = new Color(1f, 0.32f, 0.32f);
                if (this.autoFarmActive)
                {
                    this.autoFarmActive = false;
                    this.SetGameSpeed(1f);
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                }
            }
            else if (!hasForagingMode)
            {
                statusText = "Enable Aura Farm";
                statusColor = new Color(1f, 0.7f, 0.45f);
                if (this.autoFarmActive)
                {
                    this.autoFarmActive = false;
                    this.SetGameSpeed(1f);
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                }
            }
            else if (this.autoFarmStatus == "NO_TOGGLES_ERROR")
            {
                statusText = "Select radar loot first";
                statusColor = new Color(1f, 0.32f, 0.32f);
            }
            else if (this.autoFarmStatus == "RADAR_OFF_ERROR")
            {
                statusText = "Radar is off";
                statusColor = new Color(1f, 0.32f, 0.32f);
            }
            else if (this.autoFarmStatus == "MODE_REQUIRED_ERROR")
            {
                statusText = "Enable Aura Farm";
                statusColor = new Color(1f, 0.7f, 0.45f);
            }
            else if (!this.autoFarmActive && (this.autoFarmStatus == "READY" || this.autoFarmStatus == "Idle" || this.autoFarmStatus == "NO_TOGGLES"))
            {
                statusText = "Ready";
                statusColor = new Color(0.45f, 1f, 0.55f);
            }
            else
            {
                statusText = this.autoFarmStatus ?? "Idle";
                statusColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            }

            Rect statusBox = new Rect(statusPanel.x + 224f, statusPanel.y + 36f, statusPanel.width - 238f, 60f);
            this.DrawRoundedPanel(statusBox, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, new Color(accent.r, accent.g, accent.b, 0.35f));
            GUI.Label(new Rect(statusBox.x + 12f, statusBox.y + 7f, 92f, 18f), this.L("STATUS"), sectionStyle);
            string modeText = this.auraFarmEnabled ? "Aura Farm" : "No mode";
            GUIStyle modeStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
            modeStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.9f);
            GUI.Label(new Rect(statusBox.x + 108f, statusBox.y + 7f, statusBox.width - 120f, 18f), modeText, modeStyle);
            statusStyle.normal.textColor = statusColor;
            GUI.Label(new Rect(statusBox.x + 12f, statusBox.y + 29f, statusBox.width - 24f, 24f), statusText, statusStyle);
            if (this.cameraStuckDisplayTimer > 0f)
            {
                GUIStyle warnStyle = new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold };
                warnStyle.normal.textColor = new Color(1f, 0.45f, 0.45f);
                GUI.Label(new Rect(statusBox.x + 12f, statusBox.y + 42f, statusBox.width - 24f, 16f), "Camera stuck fix running", warnStyle);
            }
            num += (int)statusPanel.height + 14;

            float settingsHeight = 184f
                + (this.auraFarmEnabled ? 103f : 0f)
                + (this.autoFarmAutoStopEnabled ? 44f : 0f);
            Rect settingsPanel = new Rect(left, (float)num, panelWidth, settingsHeight);
            this.DrawExentriSectionPanel(settingsPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(settingsPanel.x + 14f, settingsPanel.y + 12f, settingsPanel.width - 28f, 18f), this.L("SETTINGS"), sectionStyle);

            float rowY = settingsPanel.y + 42f;
            GUI.Label(new Rect(settingsPanel.x + 14f, rowY, 150f, 20f), this.LF("Area Load Delay: {0}s", (int)this.areaLoadDelay), bodyStyle);
            float prevAreaLoad = this.areaLoadDelay;
            this.areaLoadDelay = Mathf.Round(this.DrawAccentSlider(new Rect(settingsPanel.x + 172f, rowY + 1f, settingsPanel.width - 200f, 20f), this.areaLoadDelay, 1f, 10f));
            if (this.areaLoadDelay != prevAreaLoad) { try { this.SaveKeybinds(false); } catch { } }
            rowY += 34f;

            bool newAuraFarmEnabled = this.DrawSwitchToggle(new Rect(settingsPanel.x + 14f, rowY, 250f, 25f), this.auraFarmEnabled, "Aura Farm");
            if (newAuraFarmEnabled != this.auraFarmEnabled)
            {
                this.SetAuraFarmEnabled(newAuraFarmEnabled);
            }

            GUIStyle auraResolverStyle = new GUIStyle(bodyStyle);
            string auraResolverText;
            if (!this.auraFarmEnabled)
            {
                auraResolverStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                auraResolverText = this.L("Resolver: STANDBY");
            }
            else if (this.auraFarmMethodsReady)
            {
                auraResolverStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
                auraResolverText = this.L("Resolver: READY");
            }
            else
            {
                auraResolverStyle.normal.textColor = new Color(1f, 0.7f, 0.45f);
                auraResolverText = this.L("Resolver: RESOLVING / NOT READY");
            }
            GUI.Label(new Rect(settingsPanel.x + 304f, rowY + 2f, settingsPanel.width - 324f, 20f), auraResolverText, auraResolverStyle);
            rowY += 34f;

            if (this.auraFarmEnabled)
            {
                // "Auto Pickup Drops" (loot auto-collect) UI HIDDEN + feature DISABLED 2026-07-09: it triggers
                // server ErrorCode 3 "Error in uploaded data" after the map-resource rework (stale/wrong loot
                // netId). Runtime call is commented out in AuraFarm.cs (UpdateAuraFarmLootCollect). Restore this
                // block + that call once the netId regression is fixed (see scratchpad LOOT_ERROR3_DIAGNOSIS).

                GUI.Label(new Rect(settingsPanel.x + 14f, rowY, 170f, 20f), this.LF("Collect Wait Max: {0}s", (int)this.auraCollectWaitTimeout), bodyStyle);
                float prevAuraCollectWait = this.auraCollectWaitTimeout;
                this.auraCollectWaitTimeout = Mathf.Round(this.DrawAccentSlider(
                    new Rect(settingsPanel.x + 192f, rowY + 1f, settingsPanel.width - 220f, 20f),
                    this.auraCollectWaitTimeout,
                    4f,
                    30f));
                if (this.auraCollectWaitTimeout != prevAuraCollectWait)
                {
                    try { this.SaveKeybinds(false); } catch { }
                }

                rowY += 34f;

                GUI.Label(new Rect(settingsPanel.x + 14f, rowY, 180f, 20f), this.LF("Teleport Delay: {0}s", (int)this.foragingTeleportDelaySeconds), bodyStyle);
                float prevTpDelay = this.foragingTeleportDelaySeconds;
                this.foragingTeleportDelaySeconds = Mathf.Round(this.DrawAccentSlider(
                    new Rect(settingsPanel.x + 192f, rowY + 1f, settingsPanel.width - 220f, 20f),
                    this.foragingTeleportDelaySeconds,
                    0f,
                    10f));
                if (this.foragingTeleportDelaySeconds != prevTpDelay)
                {
                    try { this.SaveKeybinds(false); } catch { }
                }

                rowY += 34f;
            }

            this.autoFarmAutoStopEnabled = this.DrawSwitchToggle(new Rect(settingsPanel.x + 14f, rowY, 250f, 25f), this.autoFarmAutoStopEnabled, "Auto Stop Timer");
            if (this.autoFarmAutoStopEnabled)
            {
                GUIStyle timerSmall = new GUIStyle(bodyStyle);
                rowY += 34f;
                GUI.Label(new Rect(settingsPanel.x + 14f, rowY, 110f, 20f), "Timer", timerSmall);
                this.autoFarmAutoStopHoursInput = GUI.TextField(new Rect(settingsPanel.x + 126f, rowY, 46f, 22f), this.autoFarmAutoStopHoursInput, 2);
                GUI.Label(new Rect(settingsPanel.x + 176f, rowY, 10f, 20f), ":", timerSmall);
                this.autoFarmAutoStopMinutesInput = GUI.TextField(new Rect(settingsPanel.x + 190f, rowY, 46f, 22f), this.autoFarmAutoStopMinutesInput, 2);
                GUI.Label(new Rect(settingsPanel.x + 240f, rowY, 10f, 20f), ":", timerSmall);
                this.autoFarmAutoStopSecondsInput = GUI.TextField(new Rect(settingsPanel.x + 254f, rowY, 46f, 22f), this.autoFarmAutoStopSecondsInput, 2);

                int parsed;
                if (int.TryParse(this.autoFarmAutoStopHoursInput, out parsed))
                {
                    this.autoFarmAutoStopHours = Mathf.Clamp(parsed, 0, 23);
                    this.autoFarmAutoStopHoursInput = this.autoFarmAutoStopHours.ToString();
                }
                if (int.TryParse(this.autoFarmAutoStopMinutesInput, out parsed))
                {
                    this.autoFarmAutoStopMinutes = Mathf.Clamp(parsed, 0, 59);
                    this.autoFarmAutoStopMinutesInput = this.autoFarmAutoStopMinutes.ToString();
                }
                if (int.TryParse(this.autoFarmAutoStopSecondsInput, out parsed))
                {
                    this.autoFarmAutoStopSeconds = Mathf.Clamp(parsed, 0, 59);
                    this.autoFarmAutoStopSecondsInput = this.autoFarmAutoStopSeconds.ToString();
                }

                int autoStopSeconds = this.GetAutoFarmAutoStopSeconds();
                string timerText = autoStopSeconds <= 0 ? "Set at least 1 second" : "Stops after: " + this.FormatDurationHms(autoStopSeconds);
                if (this.autoFarmActive && this.autoFarmAutoStopAt > 0f)
                {
                    int remaining = Mathf.Max(0, Mathf.CeilToInt(this.autoFarmAutoStopAt - Time.unscaledTime));
                    timerText = "Remaining: " + this.FormatDurationHms(remaining);
                }
                GUI.Label(new Rect(settingsPanel.x + 318f, rowY + 1f, settingsPanel.width - 338f, 20f), timerText, timerSmall);
            }
            num += (int)settingsPanel.height + 14;

            Rect priorityPanel = new Rect(left, (float)num, panelWidth, 318f);
            this.DrawExentriSectionPanel(priorityPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(priorityPanel.x + 14f, priorityPanel.y + 12f, priorityPanel.width - 28f, 18f), this.L("LOOT PRIORITIES"), sectionStyle);

            float col1X = priorityPanel.x + 18f;
            float col2X = priorityPanel.x + 214f;
            float col3X = priorityPanel.x + 408f;
            float colW = 156f;
            float lootY = priorityPanel.y + 42f;
            GUI.Label(new Rect(col1X, lootY, colW, 20f), this.L("Mushrooms"), bodyStyle);
            GUI.Label(new Rect(col2X, lootY, colW, 20f), this.L("Events"), bodyStyle);
            GUI.Label(new Rect(col3X, lootY, colW, 20f), this.L("Other"), bodyStyle);
            lootY += 26f;

            float line = lootY;
            this.priorityOysterMushroom = this.DrawWrappedSwitchToggle(new Rect(col1X, line, colW, 22f), this.priorityOysterMushroom, "Oyster", 20f); line += 26f;
            this.priorityButtonMushroom = this.DrawWrappedSwitchToggle(new Rect(col1X, line, colW, 22f), this.priorityButtonMushroom, "Button", 20f); line += 26f;
            this.priorityPennyBun = this.DrawWrappedSwitchToggle(new Rect(col1X, line, colW, 22f), this.priorityPennyBun, "Penny Bun", 20f); line += 26f;
            this.priorityShiitake = this.DrawWrappedSwitchToggle(new Rect(col1X, line, colW, 22f), this.priorityShiitake, "Shiitake", 20f); line += 26f;
            this.priorityTruffle = this.DrawWrappedSwitchToggle(new Rect(col1X, line, colW, 22f), this.priorityTruffle, "Truffle", 20f);

            line = lootY;
            this.priorityFiddlehead = this.DrawWrappedSwitchToggle(new Rect(col2X, line, colW, 22f), this.priorityFiddlehead, "Fiddlehead", 20f); line += 26f;
            this.priorityTallMustard = this.DrawWrappedSwitchToggle(new Rect(col2X, line, colW, 22f), this.priorityTallMustard, "Tall Mustard", 20f); line += 26f;
            this.priorityBurdock = this.DrawWrappedSwitchToggle(new Rect(col2X, line, colW, 22f), this.priorityBurdock, "Burdock", 20f); line += 26f;
            this.priorityMustardGreens = this.DrawWrappedSwitchToggle(new Rect(col2X, line, colW, 22f), this.priorityMustardGreens, "Mustard Greens", 20f);

            line = lootY;
            this.priorityBlueberry = this.DrawWrappedSwitchToggle(new Rect(col3X, line, colW, 22f), this.priorityBlueberry, "Blueberries", 20f); line += 26f;
            this.priorityRaspberry = this.DrawWrappedSwitchToggle(new Rect(col3X, line, colW, 22f), this.priorityRaspberry, "Raspberries", 20f); line += 26f;
            this.priorityBubble = this.DrawWrappedSwitchToggle(new Rect(col3X, line, colW, 22f), this.priorityBubble, "Bubbles", 20f); line += 26f;
            this.priorityInsect = this.DrawWrappedSwitchToggle(new Rect(col3X, line, colW, 22f), this.priorityInsect, "Insects", 20f);

            Vector3? activePriorityLoc = this.GetActivePriorityLocation();
            string priorityText = activePriorityLoc != null
                ? $"Priority Location: {activePriorityLoc.Value.x:F1}, {activePriorityLoc.Value.y:F1}, {activePriorityLoc.Value.z:F1}"
                : this.L("Priority Location: None");
            GUI.Label(new Rect(priorityPanel.x + 18f, priorityPanel.yMax - 34f, priorityPanel.width - 36f, 20f), priorityText, bodyStyle);

            return priorityPanel.yMax + 22f;
        }

        private void AutoFarmLog(string message)
        {
            if (!AutoFarmLogsEnabled)
            {
                return;
            }

            try
            {
                ModLogger.Msg("[AutoFarm] " + message);
            }
            catch
            {
            }
        }

        // Token: 0x0600000B RID: 11 RVA: 0x00003648 File Offset: 0x00001848
        private void MarkNearestBlueberryCollected()
        {
            Camera main = Camera.main;
            Transform transform = (main != null) ? main.transform : null;
            bool flag = transform == null;
            if (!flag)
            {
                Vector3 position = transform.position;
                int num = -1;
                float num2 = float.MaxValue;
                for (int i = 0; i < this.blueberryPositions.Length; i++)
                {
                    bool flag2 = this.blueberryCooldowns.ContainsKey(i) && Time.unscaledTime < this.blueberryCooldowns[i];
                    if (!flag2)
                    {
                        bool flag3 = this.blueberryJustCollected.ContainsKey(i) && Time.unscaledTime < this.blueberryJustCollected[i];
                        if (!flag3)
                        {
                            float num3 = Vector3.Distance(position, this.blueberryPositions[i]);
                            bool flag4 = num3 < num2 && num3 < 5f;
                            if (flag4)
                            {
                                num2 = num3;
                                num = i;
                            }
                        }
                    }
                }
                bool flag5 = num != -1;
                if (flag5)
                {
                    float unscaledTime = Time.unscaledTime;
                    this.blueberryJustCollected[num] = unscaledTime + 4f;
                    this.blueberryCooldowns[num] = unscaledTime + this.blueberryCooldownDuration;
                    this.blueberryHideUntil[num] = unscaledTime + 4f + 10f;
                }
            }
        }

        // Token: 0x0600000C RID: 12 RVA: 0x0000379C File Offset: 0x0000199C
        private void CheckManualBlueberryCollection()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            this.CheckManualBlueberryCollection(gameObject);
        }

        private void CheckManualBlueberryCollection(GameObject gameObject)
        {
            bool flag = gameObject != null && gameObject.activeInHierarchy;
            if (flag)
            {
                Image component = gameObject.GetComponent<Image>();
                bool flag2 = component != null && component.sprite != null && component.sprite.name.ToLower().Contains("interaction_8");
                if (flag2)
                {
                    Button component2 = gameObject.GetComponent<Button>();
                    bool flag3 = component2 != null;
                    if (flag3)
                    {
                        if (this.blueberryCollectListener == null)
                        {
                            this.blueberryCollectListener = new System.Action(this.MarkNearestBlueberryCollected);
                        }
                        component2.onClick.RemoveListener(this.blueberryCollectListener);
                        component2.onClick.AddListener(this.blueberryCollectListener);
                        this.lastBlueberryButton = component2;
                    }
                }
            }
            else
            {
                this.lastBlueberryButton = null;
            }
        }

        // Token: 0x0600000D RID: 13 RVA: 0x0000386C File Offset: 0x00001A6C
        private void MarkNearestRaspberryCollected()
        {
            Camera main = Camera.main;
            Transform transform = (main != null) ? main.transform : null;
            bool flag = transform == null;
            if (!flag)
            {
                Vector3 position = transform.position;
                int num = -1;
                float num2 = float.MaxValue;
                for (int i = 0; i < this.raspberryPositions.Length; i++)
                {
                    bool flag2 = this.raspberryCooldowns.ContainsKey(i) && Time.unscaledTime < this.raspberryCooldowns[i];
                    if (!flag2)
                    {
                        bool flag3 = this.raspberryJustCollected.ContainsKey(i) && Time.unscaledTime < this.raspberryJustCollected[i];
                        if (!flag3)
                        {
                            float num3 = Vector3.Distance(position, this.raspberryPositions[i]);
                            bool flag4 = num3 < num2 && num3 < 5f;
                            if (flag4)
                            {
                                num2 = num3;
                                num = i;
                            }
                        }
                    }
                }
                bool flag5 = num != -1;
                if (flag5)
                {
                    float unscaledTime = Time.unscaledTime;
                    this.raspberryJustCollected[num] = unscaledTime + 4f;
                    this.raspberryCooldowns[num] = unscaledTime + this.raspberryCooldownDuration;
                    this.raspberryHideUntil[num] = unscaledTime + 4f + 10f;
                }
            }
        }

        // Token: 0x0600000E RID: 14 RVA: 0x000039C0 File Offset: 0x00001BC0
        private void CheckManualRaspberryCollection()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            this.CheckManualRaspberryCollection(gameObject);
        }

        private void CheckManualRaspberryCollection(GameObject gameObject)
        {
            bool flag = gameObject != null && gameObject.activeInHierarchy;
            if (flag)
            {
                Image component = gameObject.GetComponent<Image>();
                bool flag2 = component != null && component.sprite != null;
                if (flag2)
                {
                    string text = component.sprite.name.ToLower();
                    bool flag3 = text.Contains("interaction_8");
                    if (flag3)
                    {
                        Button component2 = gameObject.GetComponent<Button>();
                        bool flag4 = component2 != null;
                        if (flag4)
                        {
                            if (this.raspberryCollectListener == null)
                            {
                                this.raspberryCollectListener = new System.Action(this.MarkNearestRaspberryCollected);
                            }
                            component2.onClick.RemoveListener(this.raspberryCollectListener);
                            component2.onClick.AddListener(this.raspberryCollectListener);
                            this.lastRaspberryButton = component2;
                        }
                    }
                }
            }
            else
            {
                this.lastRaspberryButton = null;
            }
        }

        private void CheckManualBerryCollectionListeners()
        {
            if (!this.isRadarActive && !this.autoFarmActive && !this.auraFarmEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.nextManualBerryListenerCheckAt)
            {
                return;
            }

            this.nextManualBerryListenerCheckAt = now + ManualBerryListenerCheckInterval;
            GameObject interactButton = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            this.CheckManualBlueberryCollection(interactButton);
            this.CheckManualRaspberryCollection(interactButton);
        }

        private void SyncNearbyLiveResourceCooldowns()
        {
            // Only sync when farming features are active - NOT when just radar is enabled
            // This prevents Mono API from activating when only Radar ESP is on
            bool shouldSync = this.autoFarmActive || this.auraFarmEnabled;
            if (!shouldSync)
            {
                return;
            }

            float nowUnscaled = Time.unscaledTime;
            if (this.auraFarmEnabled && nowUnscaled - this.auraLastSuccessfulCommandAt < 0.75f)
            {
                return;
            }

            if (nowUnscaled < this.nextLiveResourceCooldownSyncAt)
            {
                return;
            }

            this.nextLiveResourceCooldownSyncAt = nowUnscaled + this.liveResourceCooldownSyncInterval;

            if (!this.ResolveAuraFarmRuntimeMethods())
            {
                return;
            }

            this.auraOwnerTargetBuffer.Clear();
            if (this.auraFarmEnabled)
            {
                this.CollectAuraOwnerTargets(this.auraOwnerTargetBuffer);
            }
            else
            {
                this.TryCollectAuraOwnerTargetsViaSphereQuery(this.auraOwnerTargetBuffer);
                object interactSystem = this.GetAuraInteractSystemInstance();
                this.TryCollectAuraOwnerTargetsViaCylinderScan(interactSystem, this.auraOwnerTargetBuffer);
            }
            if (this.auraOwnerTargetBuffer.Count == 0)
            {
                return;
            }

            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (uint ownerNetId in this.auraOwnerTargetBuffer)
            {
                object entity = this.TryGetAuraOwnerEntity(ownerNetId);
                if (entity == null)
                {
                    continue;
                }

                object collectableObject = this.TryGetAuraEntityComponent(entity, this.auraCollectableObjectComponentType);
                if (collectableObject == null)
                {
                    continue;
                }

                if (!this.TryGetAuraEntityPosition(entity, out Vector3 entityPosition))
                {
                    continue;
                }

                if (!this.TryReadLiveCollectableCooldown(collectableObject, out long coldEndTimeMs, out int availableNum, out string resTypeName))
                {
                    continue;
                }

                this.ApplyLiveResourceCooldownByPosition(entityPosition, coldEndTimeMs, availableNum, resTypeName, nowUnixMs, nowUnscaled);
            }
        }

        // endAuthoritative: coldEndTimeMs is a REAL server end (entity read / CollectColdEvent) and
        // may correct an existing stamp in BOTH directions. False = synthetic placeholder (the
        // rolling 30s SyncLiveResourceColdStates emits when the entity's end is unreadable) — it
        // may only bump a stamp UP, never shorten one (it used to stomp the 125-300s visit stamps
        // down to 30s, re-exposing depleted nodes to the farm rotation within seconds).
        private void ApplyLiveResourceCooldownByPosition(Vector3 entityPosition, long coldEndTimeMs, int availableNum, string resTypeName, long nowUnixMs, float nowUnscaled, bool endAuthoritative = true)
        {
            bool isOnCooldown = coldEndTimeMs > nowUnixMs;
            bool isTreeType = resTypeName.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isStoneType = resTypeName.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0
                || resTypeName.IndexOf("Meteroite", StringComparison.OrdinalIgnoreCase) >= 0
                || resTypeName.IndexOf("Meteor", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBushType = resTypeName.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0
                || resTypeName.IndexOf("Berry", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isTreeType && !isStoneType && !isBushType)
            {
                AuraTargetKind fallbackKind = this.GetAuraTargetKindFromPosition(entityPosition);
                isTreeType = fallbackKind == AuraTargetKind.Tree;
                isStoneType = fallbackKind == AuraTargetKind.Stone;
                isBushType = fallbackKind == AuraTargetKind.Bush;
            }

            if (!isTreeType && !isStoneType && !isBushType)
            {
                return;
            }

            Dictionary<int, float> targetCooldowns = null;
            Dictionary<int, float> targetHideUntil = null;
            int targetIndex = -1;
            // Tight identification (2m): array points sit ~1m from their entity anchors; the old
            // 5m radius let a cold entity of ANOTHER nearby resource stamp a warm node's slot.
            float bestSqr = 4f;

            if (isTreeType)
            {
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.TreePositions, this.treeCooldowns_res, this.treeHideUntil_res, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.RareTreePositions, this.rareTreeCooldowns_res, this.rareTreeHideUntil_res, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.AppleTreePositions, this.appleTreeCooldowns_res, this.appleTreeHideUntil_res, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.OrangeTreePositions, this.orangeTreeCooldowns_res, this.orangeTreeHideUntil_res, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
            }
            else if (isStoneType)
            {
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.RockPositions, this.rockCooldowns, this.rockHideUntil, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
                this.TrySelectNearestCooldownEntry(entityPosition, HeartopiaComplete.OrePositions, this.oreCooldowns, this.oreHideUntil, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
            }
            else if (isBushType)
            {
                this.TrySelectNearestCooldownEntry(entityPosition, this.blueberryPositions, this.blueberryCooldowns, this.blueberryHideUntil, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
                this.TrySelectNearestCooldownEntry(entityPosition, this.raspberryPositions, this.raspberryCooldowns, this.raspberryHideUntil, ref targetCooldowns, ref targetHideUntil, ref targetIndex, ref bestSqr);
            }

            if (targetCooldowns == null || targetIndex < 0)
            {
                return;
            }

            if (isOnCooldown)
            {
                float secondsRemaining = Math.Max(0f, (float)(coldEndTimeMs - nowUnixMs) / 1000f);
                float newUntil = nowUnscaled + secondsRemaining;
                // A real server end overwrites unconditionally (corrects wrong long stamps DOWN so
                // a ripe-again node re-enters the rotation on time); a synthetic placeholder end
                // only bumps UP (never shortens the 125-300s visit stamps to its rolling 30s).
                float existingUntil;
                if (endAuthoritative
                    || !targetCooldowns.TryGetValue(targetIndex, out existingUntil)
                    || existingUntil < newUntil)
                {
                    targetCooldowns[targetIndex] = newUntil;
                }
                if (targetHideUntil != null)
                {
                    targetHideUntil[targetIndex] = nowUnscaled + 10f;
                }
            }
            else if (availableNum != 0)
            {
                // Mid-drain flip-flop protection ONLY: a multi-charge bush reads WARM between its
                // ~2.5s charge ticks while the aura drains it, so the send-time stamp of the node
                // being worked must survive those reads. The old unscoped guard kept ANY active
                // bush stamp against ANY warm read — a respawned (ready) bush whose slot kept
                // getting refreshed by nearby collect activity then stayed hidden on the radar
                // long after it was collectable. Everywhere else live-warm wins: clear the stamp.
                float localUntil;
                if (isBushType
                    && this.autoFarmActive
                    && this.farmState == HeartopiaComplete.AutoFarmState.Collecting
                    && Vector3.Distance(entityPosition, this.lastNodePosition) < 3f
                    && targetCooldowns.TryGetValue(targetIndex, out localUntil) && localUntil > nowUnscaled)
                {
                    return;
                }

                targetCooldowns.Remove(targetIndex);
                if (targetHideUntil != null)
                {
                    targetHideUntil.Remove(targetIndex);
                }
            }
        }

        // Token: 0x06000015 RID: 21 RVA: 0x00003ECC File Offset: 0x000020CC
        private void RunAutoFarmLogic()
        {
            this.RefreshActivePriorityLocations();
            this.autoFarmTimer += Time.unscaledDeltaTime;
            this.priorityRecheckTimer += Time.unscaledDeltaTime;
            bool flag = this.cameraStuckDisplayTimer > 0f;
            if (flag)
            {
                this.cameraStuckDisplayTimer -= Time.unscaledDeltaTime;
            }
            switch (this.farmState)
            {
                case HeartopiaComplete.AutoFarmState.ScanningForNodes:
                    {
                        // Auto Repair coordination (mirrors the FishingRoute hop gate): never
                        // start a teleport while a repair kit is in use or the restore aura is
                        // still ticking — the hop would yank the player out of the repair circle.
                        // Only the teleport-initiating states are gated; an in-flight
                        // collect/clean dwell (Collecting) always finishes.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Corrupted debuff (buff 610) + Contamination radar: park at the nearest
                        // cleansing coral until it clears. Repair still wins (gate above); an
                        // in-flight Collecting dwell is never interrupted (this state only).
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        // Teleport-rate throttle (Foraging Settings slider): hold the next hop until
                        // the configured delay has elapsed since the last teleport. Placed after the
                        // repair/corruption gates so those keep priority.
                        if (this.IsFarmTeleportThrottled(out float tpCooldownScan))
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownScan:F1}s)";
                            break;
                        }

                        // Periodic recheck of priority locations
                        if (this.priorityRecheckTimer >= 60f) // 1 minute
                        {
                            this.priorityRecheckTimer = 0f;
                            Vector3? recheckLocation = this.GetActivePriorityLocation();
                            if (recheckLocation != null)
                            {
                                float distance = Vector3.Distance(Camera.main.transform.position, recheckLocation.Value);
                                this.autoFarmStatus = $"Rechecking priority location ({distance:F0}m)...";
                                this.AutoFarmLog("Periodic priority recheck -> location " + recheckLocation.Value + " distance=" + distance.ToString("F1"));
                                this.FarmTeleportTo(recheckLocation.Value);
                                this.currentPriorityLocation = recheckLocation;
                                this.lastTeleportWasPriorityLocation = true;
                                this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                                this.autoFarmTimer = 0f;
                                break;
                            }
                        }

                        // If we're already working an active priority area, keep sweeping
                        // matching nodes in that area before jumping back to the anchor.
                        if (this.currentPriorityLocation.HasValue)
                        {
                            float distanceToActivePriorityArea = Vector3.Distance(Camera.main.transform.position, this.currentPriorityLocation.Value);
                            if (distanceToActivePriorityArea <= 120f)
                            {
                                Vector3? activeAreaPriorityNode = this.FindClosestPriorityNodeForLocation(this.currentPriorityLocation.Value, Camera.main.transform.position, false);
                                if (activeAreaPriorityNode != null)
                                {
                                    float distance = Vector3.Distance(Camera.main.transform.position, activeAreaPriorityNode.Value);
                                    this.autoFarmStatus = $"Sweeping active priority node ({distance:F0}m)...";
                                    this.AutoFarmLog("Active priority sweep -> node " + activeAreaPriorityNode.Value
                                        + " area=" + this.currentPriorityLocation.Value + " distance=" + distance.ToString("F1"));
                                    this.FarmTeleportTo(activeAreaPriorityNode.Value);
                                    this.lastNodePosition = activeAreaPriorityNode.Value;
                                    this.lastTeleportWasPriorityLocation = true;
                                    this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                                    this.autoFarmTimer = 0f;
                                    this.autoCollectClickedSinceArrival = false;
                                    this.cameraRotationAttempts = 0;
                                    this.ResetContaminationDwellState(); // priority nodes are plants
                                    this.ArmAuraCollectWait(true);
                                    break;
                                }
                            }
                        }

                        // FIRST: Check for priority nodes that are actually visible on screen right now.
                        Vector3? priorityNode = this.FindClosestVisiblePriorityNode(Camera.main.transform.position, Time.unscaledTime);
                        if (priorityNode != null)
                        {
                            float distance = Vector3.Distance(Camera.main.transform.position, priorityNode.Value);
                            this.autoFarmStatus = $"Teleporting to priority node ({distance:F0}m)...";
                            this.AutoFarmLog("Visible priority node -> " + priorityNode.Value
                                + " mappedArea=" + (this.lastFoundPriorityNodeLocation.HasValue ? this.lastFoundPriorityNodeLocation.Value.ToString() : "none")
                                + " distance=" + distance.ToString("F1"));
                            this.FarmTeleportTo(priorityNode.Value);
                            this.lastNodePosition = priorityNode.Value;
                            if (this.lastFoundPriorityNodeLocation.HasValue)
                            {
                                this.currentPriorityLocation = this.lastFoundPriorityNodeLocation;
                            }
                            this.lastTeleportWasPriorityLocation = this.currentPriorityLocation.HasValue;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.ResetContaminationDwellState(); // priority nodes are plants
                            this.ArmAuraCollectWait(true);
                            break;
                        }

                        // SECOND: Route to an active priority location even if no priority node is visible yet.
                        Vector3? priorityLocation = this.GetActivePriorityLocation();
                        if (priorityLocation != null)
                        {
                            float distance = Vector3.Distance(Camera.main.transform.position, priorityLocation.Value);
                            this.autoFarmStatus = $"Going to priority location ({distance:F0}m)...";
                            this.AutoFarmLog("Priority location fallback -> " + priorityLocation.Value + " distance=" + distance.ToString("F1"));
                            this.FarmTeleportTo(priorityLocation.Value);
                            this.currentPriorityLocation = priorityLocation;
                            this.lastTeleportWasPriorityLocation = true;
                            this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // THIRD: Normal scanning logic
                        Vector3? vector = this.FindClosestAvailableNode(out string scanNodeLabel);
                        bool flag2 = vector != null;
                        if (flag2)
                        {
                            float value = Vector3.Distance(Camera.main.transform.position, vector.Value);
                            this.autoFarmStatus = $"Teleporting to node ({value:F0}m)...";
                            this.AutoFarmLog("Normal node target -> " + vector.Value + " label=" + scanNodeLabel + " distance=" + value.ToString("F1"));
                            this.FarmTeleportTo(vector.Value);
                            this.lastNodePosition = vector.Value;
                            this.lastTeleportWasPriorityLocation = false;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(scanNodeLabel);
                        }
                        else
                        {
                            this.farmState = HeartopiaComplete.AutoFarmState.MovingToLocation;
                            this.autoFarmTimer = 0f;
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.Collecting:
                    {
                        // Contamination nodes get the sea-clean sweep dwell instead of the aura
                        // pick wait. Deliberately NOT gated on IsAutoRepairBusy — an in-flight
                        // dwell finishes (and can even hold for a cleaner repair).
                        if (this.autoFarmTargetIsContamination)
                        {
                            this.RunContaminationCleanWait();
                            break;
                        }
                        if (this.auraFarmEnabled && this.auraCollectWaitArmed)
                        {
                            this.RunAuraCollectWait();
                            break;
                        }
                        bool flag3 = this.autoFarmTimer >= 5f;
                        if (flag3)
                        {
                            this.recentlyVisitedNodes[this.lastNodePosition] = Time.unscaledTime + FarmVisitedRetryStampSeconds;
                            this.FinishCollectingCycle();
                        }
                        else
                        {
                            bool flag4 = this.autoFarmTimer >= 3f;
                            if (flag4)
                            {
                                this.recentlyVisitedNodes[this.lastNodePosition] = Time.unscaledTime + FarmVisitedRetryStampSeconds;
                                this.FinishCollectingCycle();
                            }
                            else
                            {
                                bool hasAnyPrompt = this.HasAnyVisibleInteractPrompt();
                                bool auraHasRecentCommand = this.auraFarmEnabled && Time.unscaledTime - this.auraLastSuccessfulCommandAt <= 1.2f;
                                bool flag5 = !this.autoCollectClickedSinceArrival && (!this.auraFarmEnabled || this.auraLastTargetCount <= 0 || !auraHasRecentCommand) && !this.mouseLookCaptureActive && !hasAnyPrompt;
                                if (flag5)
                                {
                                    bool flag6 = this.autoFarmTimer >= 1f && this.cameraRotationAttempts == 0;
                                    if (flag6)
                                    {
                                        this.RotateCameraAroundPlayer(90f);
                                        this.cameraRotationAttempts = 1;
                                        this.autoFarmStatus = "Adjusting camera (90 deg)...";
                                        this.cameraStuckDisplayTimer = 2f;
                                    }
                                    else
                                    {
                                        bool flag7 = this.autoFarmTimer >= 1.75f && this.cameraRotationAttempts == 1;
                                        if (flag7)
                                        {
                                            this.RotateCameraAroundPlayer(90f);
                                            this.cameraRotationAttempts = 2;
                                            this.autoFarmStatus = "Adjusting camera (180 deg)...";
                                            this.cameraStuckDisplayTimer = 2f;
                                        }
                                        else
                                        {
                                            bool flag8 = this.autoFarmTimer >= 2.5f && this.cameraRotationAttempts == 2;
                                            if (flag8)
                                            {
                                                this.RotateCameraAroundPlayer(90f);
                                                this.cameraRotationAttempts = 3;
                                                this.autoFarmStatus = "Adjusting camera (270 deg)...";
                                                this.cameraStuckDisplayTimer = 2f;
                                            }
                                            else
                                            {
                                                bool flag9 = this.cameraRotationAttempts < 3;
                                                if (flag9)
                                                {
                                                    this.autoFarmStatus = $"Collecting... ({3f - this.autoFarmTimer:F1}s remaining)";
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    this.autoFarmStatus = $"Collecting... ({3f - this.autoFarmTimer:F1}s remaining)";
                                }
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.MovingToLocation:
                    {
                        // Auto Repair coordination: hold the location hop while a repair runs.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Corrupted debuff: cleanse before hopping to the next farm location.
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        if (this.IsFarmTeleportThrottled(out float tpCooldownMove))
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownMove:F1}s)";
                            break;
                        }

                        bool flag10 = this.farmLocations.Count == 0;
                        if (flag10)
                        {
                            this.autoFarmStatus = "No locations configured!";
                        }
                        else
                        {
                            bool flag11 = this.IsAnyMushroomRadarEnabled();
                            bool flag12 = this.showBlueberryRadar || this.showRaspberryRadar;
                            bool flagTree = this.showTreeRadar;
                            bool flagRareTree = this.showRareTreeRadar;
                            bool flagAppleTree = this.showAppleTreeRadar;
                            bool flagMandarinTree = this.showOrangeTreeRadar;
                            bool flagStone = this.showStoneRadar;
                            bool flagOre = this.showOreRadar;
                            bool flagMeteor = this.showMeteorRadar;
                            bool flagEventFiddlehead = this.showFiddleheadRadar;
                            bool flagEventTallMustard = this.showTallMustardRadar;
                            bool flagEventBurdock = this.showBurdockRadar;
                            bool flagEventMustardGreens = this.showMustardGreensRadar;
                            // Any underwater radar category shares the sea-area waypoints so the farm
                            // can hop to a fresh sea region once the current one is cleared.
                            bool flagUnderwater = this.showContaminatedRadar || this.showGlasswortRadar
                                || this.showSeaGrapeRadar || this.showWakameRadar;
                            int num = this.currentLocationIndex;
                            HeartopiaComplete.FarmLocation farmLocation = null;
                            int num2 = 0;
                            HeartopiaComplete.FarmLocation farmLocation2;
                            for (; ; )
                            {
                                this.currentLocationIndex = (this.currentLocationIndex + 1) % this.farmLocations.Count;
                                farmLocation2 = this.farmLocations[this.currentLocationIndex];
                                bool flag13 = false;
                                bool flag14 = farmLocation2.Type == "any";
                                if (flag14)
                                {
                                    flag13 = true;
                                }
                                else
                                {
                                    bool flag15 = farmLocation2.Type == "both" && (flag11 || flag12);
                                    if (flag15)
                                    {
                                        flag13 = true;
                                    }
                                    else
                                    {
                                        bool flag16 = farmLocation2.Type == "mushroom" && flag11 && this.IsMushroomLocationEnabled(farmLocation2.Name);
                                        if (flag16)
                                        {
                                            flag13 = true;
                                        }
                                        else
                                        {
                                            bool flag17 = (farmLocation2.Type == "berry"
                                                || farmLocation2.Type == "blueberry"
                                                || farmLocation2.Type == "redberry") && flag12;
                                            if (flag17)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "tree" && flagTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "raretree" && flagRareTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "appletree" && flagAppleTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "mandarintree" && flagMandarinTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "stone" && flagStone)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "ore" && flagOre)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "meteor" && flagMeteor)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_fiddlehead" && flagEventFiddlehead)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_tall_mustard" && flagEventTallMustard)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_burdock" && flagEventBurdock)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_mustard_greens" && flagEventMustardGreens)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "underwater" && flagUnderwater)
                                            {
                                                flag13 = true;
                                            }
                                        }
                                    }
                                }
                                bool flag18 = flag13;
                                if (flag18)
                                {
                                    break;
                                }
                                num2++;
                                if (num2 >= this.farmLocations.Count)
                                {
                                    goto IL_4AB;
                                }
                            }
                            farmLocation = farmLocation2;
                        IL_4AB:
                            bool flag19 = farmLocation == null;
                            if (flag19)
                            {
                                // Wedge fix: toggles with no farmLocations entry (underwater
                                // plants / contamination have none) used to dead-end this state
                                // forever. Fall back to waiting on radar markers where we stand —
                                // markers keep rebuilding every RunRadar pass.
                                this.autoFarmStatus = "No matching locations for enabled toggles!";
                                this.farmState = HeartopiaComplete.AutoFarmState.WaitingForNodes;
                                this.autoFarmTimer = 0f;
                            }
                            else
                            {
                                this.autoFarmStatus = "Moving to " + farmLocation.Name + "...";
                                this.FarmTeleportTo(farmLocation.Position);
                                this.farmState = HeartopiaComplete.AutoFarmState.LoadingArea;
                                this.autoFarmTimer = 0f;
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.LoadingArea:
                    {
                        bool flag20 = this.autoFarmTimer >= this.areaLoadDelay;
                        if (flag20)
                        {
                            this.farmState = HeartopiaComplete.AutoFarmState.WaitingForNodes;
                            this.autoFarmTimer = 0f;
                        }
                        else
                        {
                            this.autoFarmStatus = $"Loading area... ({this.areaLoadDelay - this.autoFarmTimer:F1}s remaining)";
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.WaitingForNodes:
                    {
                        // Auto Repair coordination: hold the node hop while a repair runs.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Corrupted debuff: cleanse before hopping to the next node.
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        if (this.IsFarmTeleportThrottled(out float tpCooldownWait))
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownWait:F1}s)";
                            break;
                        }

                        Vector3? vector2 = this.FindClosestAvailableNode(out string waitingNodeLabel);
                        bool flag21 = vector2 != null;
                        if (flag21)
                        {
                            float value2 = Vector3.Distance(Camera.main.transform.position, vector2.Value);
                            this.autoFarmStatus = $"Node found! Teleporting ({value2:F0}m)...";
                            this.FarmTeleportTo(vector2.Value);
                            this.lastNodePosition = vector2.Value;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(waitingNodeLabel);
                        }
                        else
                        {
                            bool flag22 = this.autoFarmTimer >= 5f;
                            if (flag22)
                            {
                                this.autoFarmStatus = "No nodes found, cycling...";
                                this.farmState = HeartopiaComplete.AutoFarmState.MovingToLocation;
                                this.autoFarmTimer = 0f;
                            }
                            else
                            {
                                this.autoFarmStatus = $"Scanning for nodes... ({5f - this.autoFarmTimer:F1}s)";
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.WaitingForPriorityArea:
                    {
                        bool flag23 = this.autoFarmTimer >= this.areaLoadDelay;
                        if (flag23)
                        {
                            // Start collecting at priority location. lastNodePosition still points at a
                            // previous node here, so the radar-confirm wait must stay disarmed.
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.ResetContaminationDwellState(); // priority areas are plant dwells
                            this.ArmAuraCollectWait(false);
                            this.autoFarmStatus = "Farming at priority location...";
                        }
                        else
                        {
                            this.autoFarmStatus = $"Loading priority area... ({this.areaLoadDelay - this.autoFarmTimer:F1}s remaining)";
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.CleansingCorruption:
                    {
                        // Corrupted debuff: hold inside a cleansing-coral area until buff 610
                        // clears (CorruptionCleanseFeature.cs), then resume scanning.
                        this.RunCorruptionCleanseWait();
                        break;
                    }
            }
        }

        // ---- Contamination (sea-clean) farm dwell ------------------------------------------
        // The Aura Farm travels to "Contaminated" radar markers like any other node; the
        // Collecting dwell then runs the shared sea-clean sweep (SeaCleanQteFeature pass)
        // instead of the aura pick wait. All cross-frame state below is scalars — no
        // coroutines, no raw mono pointers held across frames.
        private bool autoFarmTargetIsContamination = false;
        // Bubble targets get their own dwell completion: the aura cannot collect bubbles (touch /
        // AutoBubbleCollect territory), so no aura confirmation ever fires for them.
        private bool autoFarmTargetIsBubble = false;
        private int contaminationZeroPassCount = 0;
        private int contaminationKillsThisNode = 0;
        private float contaminationLastConsumedPassAt = 0f;
        private float contaminationRepairHoldSince = -1f;
        private float contaminationNextToolCheckAt = 0f;
        private bool contaminationToolReady = false;
        private bool contaminationToolDepleted = false;
        private string contaminationToolStatus = string.Empty;

        // Global minimum interval between Aura Farm teleports (node / area / priority hops), user-set
        // 0-10s in Foraging Settings (0 = off). Real-time (unscaled) so 5x game speed doesn't shrink
        // it. Prevents the farm from teleporting too frequently.
        private float foragingTeleportDelaySeconds = 0f;
        private float lastFarmTeleportAt = -999f;

        // Visited-node stamp durations. A node PROVEN cold is blocked for its REAL remaining
        // cooldown when a server end time is known (CollectColdEvent.endUnixTimeMs for the node we
        // just drained, or the live scan's coldEndTime) — cooldowns differ per resource
        // (MapResourceProduce totalData: trees/bushes 120s, stones/ore 300s, rare tree & daily
        // rocks/meteors 86400s), so no single constant fits. When the node is proven cold but no
        // end time is readable, fall back to the common 120s; ambiguous outcomes (timeout with no
        // cooldown evidence — usually world streaming) keep the short retry stamp so a slow-loading
        // node isn't lost for minutes. The old flat 15s expired faster than a 2-3-dead-node loop
        // takes, so the farm circled the same depleted trees/bushes indefinitely.
        private const float FarmVisitedRetryStampSeconds = 15f;
        private const float FarmVisitedColdStampFallbackSeconds = 120f;
        // Upper bound for the visited stamp: recentlyVisitedNodes is a BACKSTOP (the label cooldown
        // dicts carry the authoritative long ends for daily resources), and unlike them it is only
        // ever corrected by TIME — cap it so a bad end-time read can't silently park a node for
        // hours. The live warm-purge in SyncLiveResourceColdStates clears it earlier anyway.
        private const float FarmVisitedColdStampMaxSeconds = 600f;

        // Real cooldown end (unix ms) of the node being collected, captured from the drain-end
        // CollectColdEvent (endMs > now). 0 = unknown (event-forage families drain with endMs=0).
        private long auraCollectNodeColdEndMs;

        // Remaining-cooldown stamp: real end + 2s grace when known, else the 120s fallback. Clamped
        // to [retry, 10min]: long (daily) cooldowns are carried by the label cooldown dicts (which
        // live corrections CAN shorten); this per-position backstop stays bounded so a bad end-time
        // read can't park a node for hours (see FarmVisitedColdStampMaxSeconds).
        private float GetVisitedColdStampSeconds(long coldEndUnixMs)
        {
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (coldEndUnixMs > nowUnixMs)
            {
                return Mathf.Clamp((float)((coldEndUnixMs - nowUnixMs) / 1000.0) + 2f,
                    FarmVisitedRetryStampSeconds, FarmVisitedColdStampMaxSeconds);
            }
            return FarmVisitedColdStampFallbackSeconds;
        }

        // Every Aura Farm hop goes through this wrapper so the throttle clock is stamped uniformly;
        // IsFarmTeleportThrottled() below paces the teleport-initiating states off it.
        private void FarmTeleportTo(Vector3 position)
        {
            this.lastFarmTeleportAt = Time.unscaledTime;
            this.TeleportToLocation(position);
        }

        // True while the configured teleport delay hasn't elapsed since the last farm teleport.
        private bool IsFarmTeleportThrottled(out float remaining)
        {
            remaining = 0f;
            float delay = this.foragingTeleportDelaySeconds;
            if (delay <= 0f)
            {
                return false;
            }
            remaining = delay - (Time.unscaledTime - this.lastFarmTeleportAt);
            return remaining > 0f;
        }

        // Clears all contamination-dwell bookkeeping (farm toggle on/off + every node hop).
        private void ResetContaminationDwellState()
        {
            this.autoFarmTargetIsContamination = false;
            this.autoFarmTargetIsBubble = false;
            this.contaminationZeroPassCount = 0;
            this.contaminationKillsThisNode = 0;
            this.contaminationLastConsumedPassAt = Time.unscaledTime;
            this.contaminationRepairHoldSince = -1f;
            this.contaminationNextToolCheckAt = 0f;
            this.contaminationToolReady = false;
            this.contaminationToolDepleted = false;
            this.contaminationToolStatus = string.Empty;
        }

        // Starts the Collecting dwell for a freshly targeted radar node: "Contaminated" markers
        // get the sea-clean sweep dwell (RunContaminationCleanWait), everything else the normal
        // aura collect wait.
        private void BeginFarmNodeDwell(string nodeLabel)
        {
            this.ResetContaminationDwellState();
            bool contamination = string.Equals(nodeLabel, "Contaminated", StringComparison.Ordinal);
            this.autoFarmTargetIsContamination = contamination;
            this.autoFarmTargetIsBubble = string.Equals(nodeLabel, "Bubble", StringComparison.Ordinal);
            if (contamination)
            {
                // Ignore sweep passes completed before (or immediately after) arrival — the
                // reported player position may not have settled yet right after the teleport,
                // so an early zero-actionable pass could belong to the OLD position.
                this.contaminationLastConsumedPassAt = Time.unscaledTime + 0.5f;
            }
            this.ArmAuraCollectWait(!contamination);
        }

        // Contamination-node Collecting dwell (Aura Farm x Auto Sea Clean): drive the shared
        // sweep pass at this node until nothing killable is left in range, then hop. The sweep
        // and its throttles (0.5s scan interval + kill pacing) are shared with the standalone
        // Auto Sea Clean tick — whichever caller runs a pass, the results land in
        // seaCleanLastPass* and are consumed here exactly once.
        private void RunContaminationCleanWait()
        {
            float now = Time.unscaledTime;
            float maxWait = Mathf.Max(6f, this.auraCollectWaitTimeout);

            // Types not ready — bounded wait (EnsureSeaCleanQteAuraResolved retries every 2s).
            if (!this.EnsureSeaCleanQteAuraResolved(out _)
                || this.seaCleanQteMonsterClass == IntPtr.Zero
                || this.seaCleanQteExecuteKillMethod == IntPtr.Zero)
            {
                if (this.autoFarmTimer >= maxWait)
                {
                    this.FinishContaminationCleanDwell(now, "types unavailable");
                    return;
                }

                this.autoFarmStatus = $"Cleaning... waiting for game types ({maxWait - this.autoFarmTimer:F0}s)";
                return;
            }

            // Farm-owned tool gate (allowSwap variant — the standalone no-yank rule is
            // untouched). The AuraMono tool read runs at 4Hz, cached in scalars between checks.
            if (now >= this.contaminationNextToolCheckAt)
            {
                this.contaminationNextToolCheckAt = now + 0.25f;
                this.contaminationToolReady = this.TrySeaCleanFarmEnsureCleanerEquipped(now, out bool cleanerDepleted, out string toolStatus);
                this.contaminationToolDepleted = cleanerDepleted;
                this.contaminationToolStatus = toolStatus;
            }

            if (!this.contaminationToolReady && this.contaminationToolDepleted)
            {
                if (this.IsAutoRepairBusy())
                {
                    // A repair for the depleted cleaner is running/queued: hold this dwell (and
                    // suspend its timeout) so the restorer can land, bounded so a wedged repair
                    // can never pin the farm here.
                    if (this.contaminationRepairHoldSince < 0f)
                    {
                        this.contaminationRepairHoldSince = now;
                    }

                    if (now - this.contaminationRepairHoldSince <= 45f)
                    {
                        this.autoFarmTimer = 0f;
                        this.autoFarmStatus = "Cleaning... waiting for sea cleaner repair";
                        return;
                    }
                }

                this.autoFarmStatus = "Sea cleaner depleted - repair needed";
                this.FinishContaminationCleanDwell(now, "cleaner depleted");
                return;
            }
            this.contaminationRepairHoldSince = -1f;

            // Run the shared sweep (self-throttled). It runs even while the equip is still
            // pending: kills stay blocked by the pass's own tool gate, but actionable counts
            // keep flowing so a node with nothing killable (shared/public only) hops early.
            this.TrySeaCleanAutoCleanPass(out _, out _, out _, out _);

            // Consume every completed pass exactly once, no matter which caller ran it.
            if (this.seaCleanLastPassCompletedAt > this.contaminationLastConsumedPassAt)
            {
                this.contaminationLastConsumedPassAt = this.seaCleanLastPassCompletedAt;
                this.contaminationKillsThisNode += this.seaCleanLastPassKilled;
                if (this.seaCleanLastPassKilled == 0 && this.seaCleanLastPassActionable == 0)
                {
                    this.contaminationZeroPassCount++;
                }
                else
                {
                    this.contaminationZeroPassCount = 0;
                }
            }

            // Done: two consecutive passes with nothing killable and nothing killed.
            if (this.contaminationZeroPassCount >= 2 && this.autoFarmTimer >= 1f)
            {
                this.FinishContaminationCleanDwell(now, "area clear");
                return;
            }

            // One unreachable/wedged node must not stall the loop forever.
            if (this.autoFarmTimer >= maxWait)
            {
                this.FinishContaminationCleanDwell(now, "timeout");
                return;
            }

            float remaining = maxWait - this.autoFarmTimer;
            if (!this.contaminationToolReady)
            {
                this.autoFarmStatus = $"Cleaning... {this.contaminationToolStatus} ({remaining:F0}s)";
            }
            else if (this.contaminationKillsThisNode > 0)
            {
                this.autoFarmStatus = $"Cleaning... {this.contaminationKillsThisNode} cleaned ({remaining:F0}s)";
            }
            else if (this.contaminationZeroPassCount > 0 && this.seaCleanLastPassNoLever > 0)
            {
                this.autoFarmStatus = $"Cleaning... only shared pollutants here ({remaining:F0}s)";
            }
            else
            {
                this.autoFarmStatus = $"Cleaning... sweeping pollutants ({remaining:F0}s)";
            }
        }

        // Finish the contamination dwell: stamp the node (15s when something was actually
        // cleaned here, 60s when nothing was killable — shared/public markers stay on the
        // radar and must not ping-pong the farm) and hop via the normal cycle finish.
        private void FinishContaminationCleanDwell(float now, string reason)
        {
            float stampSeconds = this.contaminationKillsThisNode > 0 ? 15f : 60f;
            this.recentlyVisitedNodes[this.lastNodePosition] = now + stampSeconds;
            this.AutoFarmLog($"Contamination dwell done at {this.lastNodePosition} (kills={this.contaminationKillsThisNode}, reason={reason}, stamp={stampSeconds:F0}s)");
            this.FinishCollectingCycle();
        }

        // Aura-mode Collecting: hold the hop to the next radar target until this node is
        // actually collected. After a long teleport the resource entity streams in late, so
        // the old fixed 3s dwell hopped away before the aura ever saw the target. Completion
        // is read from the radar itself: a collected node's marker is hidden by the cooldown
        // stamp (~10s) and later shown as [CD], while a still-loading node keeps (or regains)
        // an available marker. The aura-idle window keeps us in place while a tree is still
        // being chopped or a cluster around the node is still being swept.
        // Arms/disarms the radar-confirm wait for the next Collecting dwell and resets the
        // per-node entity tracking captured by TryCaptureAuraCollectNodeOwner.
        private void ArmAuraCollectWait(bool armed)
        {
            this.auraCollectWaitArmed = armed;
            this.auraCollectNodeOwnerNetId = 0U;
            this.auraCollectNodeResourceNetId = 0U;
            this.auraCollectNodeColdEndMs = 0L;
            this.auraCollectNodeEntitySeen = false;
            this.auraCollectNodeConfirmedAt = -1f;
            this.auraNextCollectNodeProbeAt = 0f;
            this.auraCollectNodeDiagLogged = false;
            this.auraCollectCaptureMissedOwners.Clear();
            this.auraCollectNodeAbsentTicks = 0;
            this.auraCollectSeenAvailByNetId.Clear();
            this.auraCollectOurNetIds.Clear();
            this.auraCollectLastBackpackAt = -1f;
            this.auraCollectNodeCapturedAt = -1f;
            if (armed)
            {
                this.EnsureAuraCollectColdEventHook();
            }
        }

        // EventCenter hooks: CollectColdEvent fires the instant a collectable flips to cooldown
        // (the exact moment the in-game interact icon disappears) and carries the resource netId
        // the pick command targeted — the only build-independent, per-resource collect signal
        // (managed XDT* entity resolution is dead on this build, and cold bush shapes stay in
        // the axe-checker). CollectObjectShowEvent covers despawn-style objects.
        private void EnsureAuraCollectColdEventHook()
        {
            if (this.auraCollectColdHookRegistered)
            {
                return;
            }

            // CollectColdEvent { uint resourceNetId@0; long endUnixTimeMs@8; float totalTime@16;
            // int availableNum@20; string displayIcon@24 }
            bool cold = this.RegisterGameEventHook(
                "ScriptsRefactory.DataAndProtocol.Events.CollectColdEvent",
                32,
                this.OnAuraCollectColdEvent);

            // CollectObjectShowEvent { uint netId@0; bool show@4 }
            bool show = this.RegisterGameEventHook(
                "ScriptsRefactory.DataAndProtocol.Events.CollectObjectShowEvent",
                8,
                this.OnAuraCollectObjectShowEvent);

            // RefreshBackPackEvent (shared with Auto Sell — same detour, extra handler): marks
            // when the gathered loot actually landed in the backpack.
            this.RegisterGameEventHook(AutoSellBackpackEventName, AutoSellBackpackEventBytes, this.OnAuraCollectBackpackRefresh);

            this.auraCollectColdHookRegistered = cold || show;
        }

        private void OnAuraCollectBackpackRefresh(GameEventSnapshot e)
        {
            if (!this.autoFarmActive
                || !this.auraFarmEnabled
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed)
            {
                return;
            }

            if (e.ReadInt32(0) != AutoSellBackpackStorageType)
            {
                return;
            }

            this.auraCollectLastBackpackAt = Time.unscaledTime;
        }

        // CollectColdEvent stream semantics (verified from live logs): while the aura drains a
        // multi-charge bush the server emits events with endMs=0 and a DECREMENTING availableNum
        // for the bush actually being picked (charges arrive on a ~2.5s server timer), plus
        // endMs=0/full-availableNum refreshes for every other spammed in-radius bush; the drain
        // completes with a single event carrying a real endMs (cooldown start) — that is the
        // "collected" moment. Captured owner/resource ids can be aggregate level-object ids that
        // never appear in events, so binding is done by the decrement pattern instead.
        private void OnAuraCollectColdEvent(GameEventSnapshot e)
        {
            if (!this.autoFarmActive || !this.auraFarmEnabled)
            {
                return;
            }

            uint resourceNetId = e.ReadUInt32(0);
            long endMs = (long)e.ReadUInt64(8);
            int availableNum = e.ReadInt32(20);
            this.AutoFarmLog("CollectColdEvent netId=" + resourceNetId
                + " endMs=" + endMs
                + " availableNum=" + availableNum
                + " (captured res=" + this.auraCollectNodeResourceNetId
                + " owner=" + this.auraCollectNodeOwnerNetId + ")");

            if (resourceNetId == 0U
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed)
            {
                return;
            }

            // Direct id match (when the capture yielded a real entity id) marks it as ours too.
            if (resourceNetId == this.auraCollectNodeResourceNetId
                || resourceNetId == this.auraCollectNodeOwnerNetId)
            {
                this.auraCollectOurNetIds.Add(resourceNetId);
            }

            int prevAvailable;
            if (this.auraCollectSeenAvailByNetId.TryGetValue(resourceNetId, out prevAvailable)
                && availableNum < prevAvailable)
            {
                if (this.auraCollectOurNetIds.Add(resourceNetId))
                {
                    this.AutoFarmLog($"Aura node bush bound by charge decrement: netId={resourceNetId} ({prevAvailable}->{availableNum})");
                }
            }
            this.auraCollectSeenAvailByNetId[resourceNetId] = availableNum;

            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Drained = a real cooldown end in the future OR charges exhausted: some resource
            // families (event forage) never set endMs/inCold — their drain event only zeroes
            // availableNum (and their shape leaves the axe-checker).
            bool eventSaysDrained = endMs > nowUnixMs || availableNum == 0;
            if (!eventSaysDrained || this.auraCollectNodeConfirmedAt >= 0f || !this.autoCollectClickedSinceArrival)
            {
                return;
            }

            // Cold with a real end time: ours if bound by decrement/id, or the first cold seen
            // before any binding (single-charge resources go cold on their first pick event).
            if (!this.auraCollectOurNetIds.Contains(resourceNetId) && this.auraCollectOurNetIds.Count != 0)
            {
                return;
            }

            this.auraCollectNodeConfirmedAt = Time.unscaledTime;
            if (endMs > nowUnixMs)
            {
                // Real server cooldown end for OUR node — the visited stamp uses it verbatim.
                this.auraCollectNodeColdEndMs = endMs;
            }
            this.AutoFarmLog($"Aura collect confirmed by CollectColdEvent (netId={resourceNetId}, endMs={endMs})");

            // Stamp the REAL cooldown onto the node position so the radar/ESP marker flips on
            // the next rescan instead of only after the hop.
            try
            {
                this.ApplyLiveResourceCooldownByPosition(this.lastNodePosition, endMs, availableNum, string.Empty, nowUnixMs, Time.unscaledTime);
            }
            catch { }
        }

        private void OnAuraCollectObjectShowEvent(GameEventSnapshot e)
        {
            if (!this.autoFarmActive || !this.auraFarmEnabled)
            {
                return;
            }

            uint netId = e.ReadUInt32(0);
            bool show = e.ReadBool(4);
            if (show || netId == 0U)
            {
                return;
            }

            this.AutoFarmLog("CollectObjectShowEvent netId=" + netId + " show=false"
                + " (captured res=" + this.auraCollectNodeResourceNetId
                + " owner=" + this.auraCollectNodeOwnerNetId + ")");

            if (this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeConfirmedAt >= 0f
                || !this.autoCollectClickedSinceArrival)
            {
                return;
            }

            // Despawn-style objects (single-charge gathers) hide on collect.
            if (netId == this.auraCollectNodeResourceNetId
                || netId == this.auraCollectNodeOwnerNetId
                || this.auraCollectOurNetIds.Contains(netId))
            {
                this.auraCollectNodeConfirmedAt = Time.unscaledTime;
                this.AutoFarmLog($"Aura collect confirmed by CollectObjectShowEvent (netId={netId})");
            }
        }

        // Called from the aura tick right after a collect command is sent: remember the owner
        // netId of the entity standing on the current foraging node so the wait loop can read
        // its collected state directly instead of waiting for the radar rescan.
        private void TryCaptureAuraCollectNodeOwner(uint ownerNetId, uint resourceNetId, Vector3 targetAnchor)
        {
            if (!this.autoFarmActive
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeOwnerNetId != 0U
                || ownerNetId == 0U)
            {
                return;
            }

            // Most discovery paths register targets WITHOUT positions (owner-only), so the
            // cached anchor is usually zero — resolve the entity position on demand instead
            // (same chain the live cooldown sync uses). Owners that resolved >3m away are
            // remembered to avoid re-resolving them every resend tick.
            Vector3 anchor = targetAnchor;
            if (anchor == Vector3.zero)
            {
                if (this.auraCollectCaptureMissedOwners.Contains(ownerNetId))
                {
                    return;
                }

                object entity = this.TryGetAuraOwnerEntity(ownerNetId);
                if (entity == null || !this.TryGetAuraEntityPosition(entity, out anchor))
                {
                    return;
                }
            }

            if ((anchor - this.lastNodePosition).sqrMagnitude > 9f)
            {
                this.auraCollectCaptureMissedOwners.Add(ownerNetId);
                return;
            }

            this.auraCollectNodeOwnerNetId = ownerNetId;
            this.auraCollectNodeResourceNetId = resourceNetId != 0U ? resourceNetId : ownerNetId;
            this.auraCollectNodeCapturedAt = Time.unscaledTime;
            this.auraCollectNodeEntitySeen = false;
            this.auraNextCollectNodeProbeAt = 0f;
            this.auraCollectNodeAbsentTicks = 0;
            this.AutoFarmLog($"Aura node owner captured netId={ownerNetId} res={this.auraCollectNodeResourceNetId} at {this.lastNodePosition} (anchor {anchor})");
        }

        // Build-independent collected signal, called from the aura tick right after the target
        // buffer was refreshed successfully: the captured owner vanishing from the axe-checker
        // means its physical gather shape was removed/deactivated — which is what actually stops
        // the aura from re-sending on this build (the managed inCold pre-send check never fires
        // here because XDT* entity resolution is Mono-only). Three consecutive absent ticks
        // (~0.25s) confirm, riding over single flaky scans.
        private void UpdateAuraCollectNodePresence()
        {
            if (!this.autoFarmActive
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeOwnerNetId == 0U
                || this.auraCollectNodeConfirmedAt >= 0f)
            {
                return;
            }

            if (this.auraOwnerTargetBuffer.Contains(this.auraCollectNodeOwnerNetId))
            {
                this.auraCollectNodeAbsentTicks = 0;
                return;
            }

            this.auraCollectNodeAbsentTicks++;
            if (this.auraCollectNodeAbsentTicks >= 3)
            {
                this.auraCollectNodeConfirmedAt = Time.unscaledTime;
                this.AutoFarmLog($"Aura node left axe-checker (netId={this.auraCollectNodeOwnerNetId}) -> collected");
            }
        }

        // Polls the captured node entity's CollectableObjectComponent (throttled): coldEndTime
        // in the future or availableNum==0 is exactly the state the game's interact icon reads,
        // so it flips within the server round-trip instead of the 2s radar cadence. An entity/
        // component that despawns after having been seen once counts as collected too.
        private void ProbeAuraCollectNodeState(float now)
        {
            if (this.auraCollectNodeConfirmedAt >= 0f
                || this.auraCollectNodeOwnerNetId == 0U
                || now < this.auraNextCollectNodeProbeAt)
            {
                return;
            }

            this.auraNextCollectNodeProbeAt = now + 0.2f;
            if (!this.ResolveAuraFarmRuntimeMethods())
            {
                return;
            }

            object entity = this.TryGetAuraOwnerEntity(this.auraCollectNodeOwnerNetId);
            object collectable = entity != null
                ? this.TryGetAuraEntityComponent(entity, this.auraCollectableObjectComponentType)
                : null;
            if (collectable == null)
            {
                if (this.auraCollectNodeEntitySeen)
                {
                    this.auraCollectNodeConfirmedAt = now;
                    this.AutoFarmLog($"Aura node probe: entity/component despawned (netId={this.auraCollectNodeOwnerNetId}) -> collected");
                }
                else if (!this.auraCollectNodeDiagLogged)
                {
                    this.auraCollectNodeDiagLogged = true;
                    this.AutoFarmLog($"Aura node probe diag: netId={this.auraCollectNodeOwnerNetId} entity={(entity != null ? "ok" : "null")} collectable=null");
                }
                return;
            }

            this.auraCollectNodeEntitySeen = true;

            // inCold is the exact flag the game's interact icon reads; coldEndTime/availableNum
            // cover builds where the bool member fails to resolve.
            bool inCold;
            bool inColdRead = this.TryGetAuraCollectableInCold(collectable, out inCold);
            long coldEndTimeMs = 0L;
            int availableNum = -1;
            string resTypeName = string.Empty;
            bool cooldownRead = this.TryReadLiveCollectableCooldown(collectable, out coldEndTimeMs, out availableNum, out resTypeName);
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!this.auraCollectNodeDiagLogged)
            {
                this.auraCollectNodeDiagLogged = true;
                this.AutoFarmLog("Aura node probe diag: netId=" + this.auraCollectNodeOwnerNetId
                    + " inCold=" + (inColdRead ? inCold.ToString() : "unreadable")
                    + " coldEndMs=" + (cooldownRead ? coldEndTimeMs.ToString() : "unreadable")
                    + " availableNum=" + (cooldownRead ? availableNum.ToString() : "unreadable")
                    + " resType=" + (string.IsNullOrEmpty(resTypeName) ? "<none>" : resTypeName));
            }

            if ((inColdRead && inCold)
                || (cooldownRead && (coldEndTimeMs > nowUnixMs || availableNum == 0)))
            {
                this.auraCollectNodeConfirmedAt = now;
            }
        }

        private void RunAuraCollectWait()
        {
            float now = Time.unscaledTime;
            float maxWait = Mathf.Max(4f, this.auraCollectWaitTimeout);
            bool auraIdle = now - this.auraLastSuccessfulCommandAt >= 1.25f;
            bool markerFound;
            string nodeMarkerLabel;
            bool markerOnCooldown = this.TryGetNodeMarkerState(this.lastNodePosition, out markerFound, out nodeMarkerLabel);

            // Bubble targets: the aura cannot collect bubbles (they pop by touch / AutoBubbleCollect),
            // so none of the aura confirmations below ever fire and the dwell burned the whole
            // Collect Wait Max per bubble (30s each, user report 2026-07-12). A bubble is done when
            // its tracked marker despawns (popped/collected — or drifted >2.5m, in which case it can
            // be re-targeted at its new spot); and if the marker still stands after a few seconds,
            // standing longer will not pop it — hop with the short retry stamp.
            if (this.autoFarmTargetIsBubble)
            {
                if (this.autoFarmTimer >= 1f && !markerFound)
                {
                    this.AutoFarmLog($"Bubble collected/despawned after {this.autoFarmTimer:F1}s at {this.lastNodePosition}");
                    this.recentlyVisitedNodes[this.lastNodePosition] = now + FarmVisitedRetryStampSeconds;
                    this.FinishCollectingCycle();
                    return;
                }

                if (this.autoFarmTimer >= 6f)
                {
                    this.AutoFarmLog($"Bubble dwell capped after {this.autoFarmTimer:F1}s at {this.lastNodePosition} (marker still present)");
                    this.recentlyVisitedNodes[this.lastNodePosition] = now + FarmVisitedRetryStampSeconds;
                    this.FinishCollectingCycle();
                    return;
                }

                this.autoFarmStatus = "Collecting bubble...";
                return;
            }

            // Fast path: the node reported collected (CollectColdEvent / despawn / entity state).
            // No aura-quiet gate here: the aura keeps re-spamming every in-radius bush (the server
            // just refuses the far ones), so auraLastSuccessfulCommandAt never goes quiet in berry
            // fields — a short post-confirm grace is all that's needed.
            this.ProbeAuraCollectNodeState(now);
            if (this.auraCollectNodeOwnerNetId == 0U
                && !this.auraCollectNodeDiagLogged
                && this.autoCollectClickedSinceArrival
                && this.autoFarmTimer >= 3f)
            {
                this.auraCollectNodeDiagLogged = true;
                this.AutoFarmLog($"Aura node probe diag: aura sent commands but no owner matched node {this.lastNodePosition} within 3m (missedOwners={this.auraCollectCaptureMissedOwners.Count})");
            }
            bool hasCollectProgress = this.auraCollectNodeConfirmedAt >= 0f
                || this.auraCollectOurNetIds.Count > 0
                || this.auraCollectLastBackpackAt >= 0f;

            // Authoritative live state of THIS node (tight XZ identification, scan must postdate
            // arrival). The scan arbitrates the event heuristics both ways below.
            bool liveNodeFound = this.TryGetLiveNodeColdState(this.lastNodePosition, now - this.autoFarmTimer, out bool liveNodeCold, out long liveNodeColdEndMs);

            // Best-known real cooldown end for the visited stamp: our own drain event's endMs wins
            // (freshest, always ours), else the live entity's coldEndTime; 0 => 120s fallback.
            long knownColdEndMs = this.auraCollectNodeColdEndMs;
            if (knownColdEndMs <= 0L && liveNodeFound && liveNodeCold)
            {
                knownColdEndMs = liveNodeColdEndMs;
            }

            // Scan-driven confirm: we made progress here and the node's entity flipped cold —
            // collected, even if the event binding missed it (partial event streams).
            if (liveNodeFound && liveNodeCold && hasCollectProgress && this.auraCollectNodeConfirmedAt < 0f)
            {
                this.auraCollectNodeConfirmedAt = now;
                this.AutoFarmLog($"Aura collect confirmed by live scan (node flipped cold) after {this.autoFarmTimer:F1}s");
            }

            // "The node exists locally" — the aura addressed an object ≤3m of it (capture) or a
            // post-arrival scan contains its entity. While the destination is still streaming in
            // after a long teleport NEITHER holds, and every confirm seen so far can only belong
            // to already-loaded NEIGHBORS the aura swept in parallel — never hop on those.
            bool nodePresent = this.auraCollectNodeOwnerNetId != 0U || liveNodeFound;

            if (this.auraCollectNodeConfirmedAt >= 0f && this.autoFarmTimer >= 0.5f && nodePresent)
            {
                // The scan is the arbiter against neighbor-misbound event confirms: when a scan
                // NEWER than the confirmation still sees the node warm, the confirm was for some
                // other bush — hold until the node truly flips (or the timeout bounds it).
                bool liveContradictsConfirm = liveNodeFound
                    && !liveNodeCold
                    && this.liveCollectableScanCompletedAt >= this.auraCollectNodeConfirmedAt + 0.2f;
                if (!liveContradictsConfirm)
                {
                    // Hop 1s after the loot actually landed in the backpack (RefreshBackPackEvent);
                    // when no bag refresh was seen this dwell, 1s after the collect confirmation.
                    // Unrelated bag traffic (Auto Pickup Drops vacuuming, neighbor loot) must not
                    // slide the anchor forever — 3s after the confirm the hop goes regardless.
                    float hopAnchor = Mathf.Max(this.auraCollectNodeConfirmedAt, this.auraCollectLastBackpackAt);
                    if (now - hopAnchor >= 1f || now - this.auraCollectNodeConfirmedAt >= 3f)
                    {
                        this.AutoFarmLog($"Aura collect done after {this.autoFarmTimer:F1}s at {this.lastNodePosition} (bagRefresh={(this.auraCollectLastBackpackAt >= 0f ? "yes" : "none")})");
                        // We just drained it — block for its real remaining cooldown.
                        this.recentlyVisitedNodes[this.lastNodePosition] = now + this.GetVisitedColdStampSeconds(knownColdEndMs);
                        this.FinishCollectingCycle();
                        return;
                    }
                }

                // Confirmed but the loot is still settling (or the scan says the node is still
                // active) — hold here so the radar fallbacks can't hop earlier; the shared
                // timeout below stays as the outer bound.
                if (this.autoFarmTimer < maxWait)
                {
                    this.autoFarmStatus = liveContradictsConfirm
                        ? "Collecting... node still active"
                        : "Collecting... securing loot";
                    return;
                }
            }

            // Authoritative live state arrived and says the node is already on server cooldown
            // -> skip right away. The capture-age guard gives our OWN pick's events 1.25s to
            // land first, so a node we just drained still goes through the normal completion
            // path (with its bag-settle wait).
            if (!hasCollectProgress
                && this.autoFarmTimer >= 0.75f
                && (!this.autoCollectClickedSinceArrival
                    || this.auraCollectNodeCapturedAt < 0f
                    || now - this.auraCollectNodeCapturedAt >= 1.25f)
                && liveNodeFound
                && liveNodeCold)
            {
                this.AutoFarmLog($"Aura node is live-cold (mono scan) after {this.autoFarmTimer:F1}s at {this.lastNodePosition} -> skipping");
                // Proven server cooldown — block for its real remaining window.
                this.recentlyVisitedNodes[this.lastNodePosition] = now + this.GetVisitedColdStampSeconds(knownColdEndMs);
                this.FinishCollectingCycle();
                return;
            }

            // NOTE: silence-based early bail removed by request — a silent node waits the full
            // Collect Wait Max slider (the world may still be loading / the server settling our
            // position after a teleport chain; the collect will happen). Fast skips remain only
            // for PROVEN cooldown: the live-scan branch above.

            if (this.autoFarmTimer >= 1f && auraIdle)
            {
                // Radar shows the node on cooldown -> collected (by us or by someone else).
                if (markerFound && markerOnCooldown)
                {
                    this.AutoFarmLog($"Aura collect confirmed (marker cooldown) after {this.autoFarmTimer:F1}s at {this.lastNodePosition}");
                    // Marker shows [CD] — proven cooldown, block for its known/fallback window.
                    this.recentlyVisitedNodes[this.lastNodePosition] = now + this.GetVisitedColdStampSeconds(knownColdEndMs);
                    this.FinishCollectingCycle();
                    return;
                }

                // Marker vanished after the aura actually addressed THIS node (capture): stamped
                // nodes are hidden from the radar before their [CD] marker appears. The capture
                // requirement keeps this from firing during world streaming, when clicks belong
                // to already-loaded neighbors and the node's mesh simply isn't there yet.
                if (!markerFound && this.autoCollectClickedSinceArrival && this.auraCollectNodeOwnerNetId != 0U)
                {
                    this.AutoFarmLog($"Aura collect confirmed (marker gone) after {this.autoFarmTimer:F1}s at {this.lastNodePosition}");
                    // Collected (stamped nodes hide their marker) — real/fallback cooldown, not 15s.
                    this.recentlyVisitedNodes[this.lastNodePosition] = now + this.GetVisitedColdStampSeconds(knownColdEndMs);
                    this.FinishCollectingCycle();
                    return;
                }
            }

            // One unreachable/bugged node must not stall the loop forever.
            if (this.autoFarmTimer >= maxWait)
            {
                string markerState = markerFound ? (markerOnCooldown ? "cooldown" : "available") : "none";
                this.AutoFarmLog($"Aura collect wait timed out after {this.autoFarmTimer:F1}s at {this.lastNodePosition} (marker={markerState}, label={(string.IsNullOrEmpty(nodeMarkerLabel) ? "<none>" : nodeMarkerLabel)}, clicked={this.autoCollectClickedSinceArrival})");
                // Cooldown evidence at timeout => real/fallback block; otherwise short retry (streaming lag).
                bool timedOutCold = (markerFound && markerOnCooldown) || (liveNodeFound && liveNodeCold);
                this.recentlyVisitedNodes[this.lastNodePosition] = now
                    + (timedOutCold ? this.GetVisitedColdStampSeconds(knownColdEndMs) : FarmVisitedRetryStampSeconds);
                this.FinishCollectingCycle();
                return;
            }

            float remaining = maxWait - this.autoFarmTimer;
            if (!auraIdle)
            {
                this.autoFarmStatus = $"Collecting... aura working ({remaining:F0}s)";
            }
            else if (!markerFound && !this.autoCollectClickedSinceArrival)
            {
                this.autoFarmStatus = $"Collecting... waiting for area to load ({remaining:F0}s)";
            }
            else
            {
                this.autoFarmStatus = $"Collecting... waiting for node ({remaining:F0}s)";
            }
        }

        // Reads the radar marker state at a node position: cooldown flag + label of the closest
        // labeled marker within 2.5m. markerFound=false when no such marker exists (hidden
        // after a collect stamp, not streamed in yet, or radar container unavailable).
        private bool TryGetNodeMarkerState(Vector3 nodePosition, out bool markerFound, out string markerLabel)
        {
            markerFound = false;
            markerLabel = string.Empty;
            bool onCooldown = false;
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return false;
            }

            float bestSqr = 6.25f;
            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                float sqr = (child.position - nodePosition).sqrMagnitude;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                string label = this.GetMarkerCanonicalLabel(child.gameObject);
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                bestSqr = sqr;
                markerFound = true;
                markerLabel = label;
                onCooldown = this.IsMarkerOnCooldown(child.gameObject);
            }

            return onCooldown;
        }

        // Live authoritative cooldown layer: the mono collectable scan (position + inCold +
        // coldEndTime, MapSpots.RefreshCollectableScan) synced into the radar's local cooldown
        // dicts every ~2s while the radar or foraging runs. Gives true server cooldown states
        // right after radar enable, so markers/ESP are correct and the foraging scanner skips
        // already-cold nodes BEFORE teleporting. This replaces SyncNearbyLiveResourceCooldowns,
        // whose managed entity resolution is dead on this build (XDT* types are Mono-only).
        private void SyncLiveResourceColdStates()
        {
            if (!this.isRadarActive && !this.autoFarmActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.nextLiveColdSyncAt)
            {
                return;
            }
            this.nextLiveColdSyncAt = now + 2f;

            // Shared throttle with the game-map feature (mapResNextScanAt) — whoever asks first
            // runs the scan, the other consumes the same snapshot.
            this.RefreshCollectableScan();
            if (this.liveCollectableColds.Count == 0)
            {
                return;
            }

            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            List<Vector3> warmVisitedPurge = null;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                LiveCollectableCold entry = this.liveCollectableColds[i];
                bool endReadable = entry.ColdEndMs > nowUnixMs;
                long endMs;
                if (entry.OnCooldown)
                {
                    // Real end time when readable; otherwise a rolling 30s re-confirmed each scan.
                    endMs = endReadable ? entry.ColdEndMs : nowUnixMs + 30000L;
                }
                else
                {
                    endMs = 0L;
                }

                try
                {
                    this.ApplyLiveResourceCooldownByPosition(entry.Position, endMs, entry.OnCooldown ? 0 : 1, string.Empty, nowUnixMs, now, endReadable);
                }
                catch
                {
                }

                // Live truth beats any stamp: a node the scan sees WARM (collectable right now) must
                // not stay parked in recentlyVisitedNodes — a wrong/stale visited stamp there is
                // corrected by nothing else (it only expires by time), and while nearby nodes sit
                // wrongly blocked FindClosestAvailableNode returns null and the farm wanders the
                // area-waypoint rotation ("jumps between empty loading spots"). The node currently
                // being worked (3m of lastNodePosition) is exempt: right after our drain the server
                // flip lags a beat and a warm read would purge the fresh stamp -> bounce-back.
                if (!entry.OnCooldown && this.recentlyVisitedNodes.Count > 0
                    && Vector3.Distance(entry.Position, this.lastNodePosition) > 3f)
                {
                    foreach (Vector3 visited in this.recentlyVisitedNodes.Keys)
                    {
                        if (Vector3.Distance(entry.Position, visited) < 2f)
                        {
                            (warmVisitedPurge ??= new List<Vector3>()).Add(visited);
                        }
                    }
                }
            }

            if (warmVisitedPurge != null)
            {
                for (int i = 0; i < warmVisitedPurge.Count; i++)
                {
                    this.recentlyVisitedNodes.Remove(warmVisitedPurge[i]);
                }
            }
        }

        // Reads the node's authoritative live state from the last mono collectable scan. Returns
        // false while the node's entity is not in a scan newer than minScanCompletedAt (world
        // still streaming in / scan stale) — the caller keeps waiting in that case.
        // IDENTIFICATION, not proximity: XZ-only match within 1.5m (entity anchors sit ~0.5-1m
        // above marker positions, and a looser 3D radius let a cold NEIGHBOR be attributed to a
        // warm node — false skips).
        private bool TryGetLiveNodeColdState(Vector3 nodePosition, float minScanCompletedAt, out bool onCooldown)
        {
            return this.TryGetLiveNodeColdState(nodePosition, minScanCompletedAt, out onCooldown, out _);
        }

        // coldEndUnixMs = the entity's real coldEndTime (unix ms) when on cooldown; 0 when warm or
        // unreadable (some families never set it — callers fall back to the 120s stamp).
        private bool TryGetLiveNodeColdState(Vector3 nodePosition, float minScanCompletedAt, out bool onCooldown, out long coldEndUnixMs)
        {
            onCooldown = false;
            coldEndUnixMs = 0L;
            if (this.liveCollectableScanCompletedAt < minScanCompletedAt)
            {
                return false;
            }

            float bestSqr = 2.25f;
            bool found = false;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                Vector3 delta = this.liveCollectableColds[i].Position - nodePosition;
                float sqr = delta.x * delta.x + delta.z * delta.z;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                found = true;
                onCooldown = this.liveCollectableColds[i].OnCooldown;
                coldEndUnixMs = this.liveCollectableColds[i].ColdEndMs;
            }

            return found;
        }

        // Token: 0x06000016 RID: 22 RVA: 0x0000459C File Offset: 0x0000279C
        // selectedLabel = canonical marker label of the returned node (empty when none) — the
        // caller uses it to route "Contaminated" nodes into the sea-clean dwell.
        private Vector3? FindClosestAvailableNode(out string selectedLabel)
        {
            selectedLabel = string.Empty;
            bool flag = !this.isRadarActive || this.radarContainer == null;
            Vector3? result;
            if (flag)
            {
                result = null;
            }
            else
            {
                Vector3 position = Camera.main.transform.position;
                Vector3? vector = null;
                float num = float.MaxValue;
                float unscaledTime = Time.unscaledTime;
                List<Vector3> list = new List<Vector3>();
                foreach (KeyValuePair<Vector3, float> keyValuePair in this.recentlyVisitedNodes)
                {
                    bool flag2 = unscaledTime >= keyValuePair.Value;
                    if (flag2)
                    {
                        list.Add(keyValuePair.Key);
                    }
                }
                foreach (Vector3 key in list)
                {
                    this.recentlyVisitedNodes.Remove(key);
                }

                // Scan for all enabled items
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    bool flag3 = child == null;
                    if (!flag3)
                    {
                        GameObject gameObject = child.gameObject;
                        string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                        bool markerOnCooldown = this.IsMarkerOnCooldown(gameObject);
                        bool flag4 = string.IsNullOrEmpty(markerLabel);
                        if (!flag4)
                        {
                            bool flag5 = markerOnCooldown;
                            if (!flag5)
                            {
                                // Authoritative live check bypassing marker-rebuild/stamp lag:
                                // a candidate whose entity is known cold is never targeted.
                                bool liveCandidateCold;
                                if (this.TryGetLiveNodeColdState(child.position, unscaledTime - 6f, out liveCandidateCold) && liveCandidateCold)
                                {
                                    continue;
                                }
                                bool flag6 = false;
                                foreach (Vector3 vector2 in this.recentlyVisitedNodes.Keys)
                                {
                                    bool flag7 = Vector3.Distance(child.position, vector2) < 2f;
                                    if (flag7)
                                    {
                                        flag6 = true;
                                        break;
                                    }
                                }
                                bool flag8 = flag6;
                                if (!flag8)
                                {
                                    bool flag9 = false;
                                    // Underwater update (2026-07-09): sea plants + contamination.
                                    // Exact-toggle branches checked before the generic substring
                                    // chain below so these labels can never be shadowed by it.
                                    bool flag10 = (this.showGlasswortRadar && markerLabel.Contains("Glasswort"))
                                        || (this.showSeaGrapeRadar && markerLabel.Contains("Sea Grape"))
                                        || (this.showWakameRadar && markerLabel.Contains("Wakame"))
                                        || (this.showContaminatedRadar && markerLabel.Contains("Contaminated"))
                                        || this.ShouldShowMushroomByLabel(markerLabel)
                                        || (this.showFiddleheadRadar && markerLabel.Contains("Fiddlehead"))
                                        || (this.showTallMustardRadar && markerLabel.Contains("Tall Mustard"))
                                        || (this.showBurdockRadar && markerLabel.Contains("Burdock"))
                                        || (this.showMustardGreensRadar && markerLabel.Contains("Mustard Greens"));
                                    if (flag10)
                                    {
                                        flag9 = true;
                                    }
                                    else
                                    {
                                        bool flag11 = markerLabel.Contains("Blueberry") && this.showBlueberryRadar;
                                        if (flag11)
                                        {
                                            flag9 = true;
                                        }
                                        else
                                        {
                                            bool flag12 = markerLabel.Contains("Raspberry") && this.showRaspberryRadar;
                                            if (flag12)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Rare Tree") && this.showRareTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Apple Tree") && this.showAppleTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Mandarin Tree") && this.showOrangeTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Stone") && this.showStoneRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Ore") && this.showOreRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Tree") && this.showTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else
                                            {
                                                bool flag13 = markerLabel.Contains("Bubble") && this.showBubbleRadar;
                                                if (flag13)
                                                {
                                                    flag9 = true;
                                                }
                                                else
                                                {
                                                    bool flagBird = markerLabel.Contains("Bird") && this.showBirdRadar;
                                                    if (flagBird)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else if (markerLabel.Contains("Player") && this.showOtherPlayersRadar)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else if (markerLabel.Contains("Morph") && this.showOtherPlayersRadar)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else
                                                    {
                                                        bool flag14 = markerLabel.Contains("Insect") && this.showInsectRadar;
                                                        if (flag14)
                                                        {
                                                            flag9 = true;
                                                        }
                                                        else if (markerLabel.Contains("Meteor") && this.showMeteorRadar)
                                                        {
                                                            flag9 = true;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    bool flag15 = !flag9;
                                    if (!flag15)
                                    {
                                        if (child.position.sqrMagnitude < 0.01f)
                                        {
                                            continue;
                                        }

                                        float num2 = Vector3.Distance(position, child.position);
                                        bool flag16 = num2 < num;
                                        if (flag16)
                                        {
                                            num = num2;
                                            vector = new Vector3?(child.position);
                                            selectedLabel = markerLabel;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                result = vector;
            }
            return result;
        }

        private Vector3? FindClosestVisiblePriorityNode(Vector3 playerPos, float currentTime)
        {
            this.lastFoundPriorityNodeLocation = null;
            // Check if any priorities are enabled
            bool hasPriorities = this.priorityOysterMushroom || this.priorityButtonMushroom || this.priorityPennyBun ||
                                this.priorityShiitake || this.priorityTruffle || this.priorityFiddlehead || this.priorityTallMustard || this.priorityBurdock || this.priorityMustardGreens || this.priorityBlueberry ||
                                this.priorityRaspberry || this.priorityBubble || this.priorityInsect;

            if (!hasPriorities)
            {
                return null; // No priorities set, return null to use normal scanning
            }

            Vector3? closestPriority = null;
            float closestDistance = float.MaxValue;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return null;
            }

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null) continue;

                GameObject gameObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(gameObject)) continue;

                // Check if recently visited
                bool isRecentlyVisited = false;
                foreach (Vector3 vector2 in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, vector2) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }
                if (isRecentlyVisited) continue;

                Vector3 screenPoint = cam.WorldToScreenPoint(child.position + new Vector3(0f, 1.1f, 0f));
                bool isVisibleOnScreen = screenPoint.z > 0.05f
                    && screenPoint.x >= 8f
                    && screenPoint.x <= (Screen.width - 8f)
                    && screenPoint.y >= 8f
                    && screenPoint.y <= (Screen.height - 8f);
                if (!isVisibleOnScreen)
                {
                    continue;
                }

                // Check if this node matches a priority
                bool isPriorityMatch = false;

                if (this.priorityOysterMushroom && markerLabel.Contains("Oyster"))
                    isPriorityMatch = true;
                else if (this.priorityButtonMushroom && markerLabel.Contains("Button"))
                    isPriorityMatch = true;
                else if (this.priorityPennyBun && markerLabel.Contains("Penny Bun"))
                    isPriorityMatch = true;
                else if (this.priorityShiitake && markerLabel.Contains("Shiitake"))
                    isPriorityMatch = true;
                else if (this.priorityTruffle && markerLabel.Contains("Truffle"))
                    isPriorityMatch = true;
                else if (this.priorityFiddlehead && markerLabel.Contains("Fiddlehead"))
                    isPriorityMatch = true;
                else if (this.priorityTallMustard && (markerLabel.Contains("Tall Mustard") || markerLabel.Contains("Mustard")))
                    isPriorityMatch = true;
                else if (this.priorityBurdock && markerLabel.Contains("Burdock"))
                    isPriorityMatch = true;
                else if (this.priorityMustardGreens && markerLabel.Contains("Mustard Greens"))
                    isPriorityMatch = true;
                else if (this.priorityBlueberry && markerLabel.Contains("Blueberry") && this.showBlueberryRadar)
                    isPriorityMatch = true;
                else if (this.priorityRaspberry && markerLabel.Contains("Raspberry") && this.showRaspberryRadar)
                    isPriorityMatch = true;
                else if (this.priorityBubble && markerLabel.Contains("Bubble") && this.showBubbleRadar)
                    isPriorityMatch = true;
                else if (this.priorityInsect && markerLabel.Contains("Insect") && this.showInsectRadar)
                    isPriorityMatch = true;

                if (isPriorityMatch)
                {
                    Vector3? mappedPriorityLocation = this.GetPriorityLocationForNodeText(markerLabel);
                    if (mappedPriorityLocation.HasValue && !this.IsPriorityLocationAvailable(mappedPriorityLocation.Value, currentTime))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(playerPos, child.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPriority = child.position;
                        this.lastFoundPriorityNodeLocation = mappedPriorityLocation;
                    }
                }
            }

            return closestPriority;
        }

        private Vector3? FindClosestPriorityNodeForLocation(Vector3 priorityLocation, Vector3 playerPos, bool requireVisibleOnScreen)
        {
            this.lastFoundPriorityNodeLocation = priorityLocation;
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return null;
            }

            Camera cam = requireVisibleOnScreen ? Camera.main : null;
            if (requireVisibleOnScreen && cam == null)
            {
                return null;
            }

            Vector3? closestPriority = null;
            float closestDistance = float.MaxValue;
            const float priorityAreaNodeSearchRadius = 120f;

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                GameObject gameObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(gameObject))
                {
                    continue;
                }

                Vector3? mappedPriorityLocation = this.GetPriorityLocationForNodeText(markerLabel);
                if (!mappedPriorityLocation.HasValue || mappedPriorityLocation.Value != priorityLocation)
                {
                    continue;
                }

                if (Vector3.Distance(child.position, priorityLocation) > priorityAreaNodeSearchRadius)
                {
                    continue;
                }

                bool isRecentlyVisited = false;
                foreach (Vector3 visitedNode in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, visitedNode) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }
                if (isRecentlyVisited)
                {
                    continue;
                }

                if (requireVisibleOnScreen)
                {
                    Vector3 screenPoint = cam.WorldToScreenPoint(child.position + new Vector3(0f, 1.1f, 0f));
                    bool isVisibleOnScreen = screenPoint.z > 0.05f
                        && screenPoint.x >= 8f
                        && screenPoint.x <= (Screen.width - 8f)
                        && screenPoint.y >= 8f
                        && screenPoint.y <= (Screen.height - 8f);
                    if (!isVisibleOnScreen)
                    {
                        continue;
                    }
                }

                float distance = Vector3.Distance(playerPos, child.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPriority = child.position;
                }
            }

            return closestPriority;
        }

        private Vector3? GetPriorityLocationForNodeText(string text)
        {
            if (text.Contains("Oyster")) return this.priorityLocations["Oyster Mushroom"];
            if (text.Contains("Button")) return this.priorityLocations["Button Mushroom"];
            if (text.Contains("Penny Bun")) return this.priorityLocations["Penny Bun"];
            if (text.Contains("Shiitake")) return this.priorityLocations["Shiitake"];
            if (text.Contains("Truffle")) return this.priorityLocations["Black Truffle"];
            if (text.Contains("Fiddlehead")) return this.priorityLocations["Fiddlehead"];
            if (text.Contains("Tall Mustard")) return this.priorityLocations["Tall Mustard"];
            if (text.Contains("Burdock")) return this.priorityLocations["Burdock"];
            if (text.Contains("Mustard Greens")) return this.priorityLocations["Mustard Greens"];
            if (text.Contains("Blueberry")) return this.priorityLocations["Blueberry"];
            if (text.Contains("Raspberry")) return this.priorityLocations["Raspberry"];
            return null;
        }

        private void TryStampVisitedResourceNodeCooldown(Vector3 nodePosition)
        {
            float bestSqr = 9f;
            int bestIndex = -1;
            float bestDuration = 0f;
            string bestLabel = string.Empty;
            Dictionary<int, float> bestCooldowns = null;
            Dictionary<int, float> bestHideUntil = null;

            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.TreePositions, this.treeCooldowns_res, this.treeHideUntil_res, this.treeCooldownDuration_res, "Tree", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.RareTreePositions, this.rareTreeCooldowns_res, this.rareTreeHideUntil_res, this.rareTreeCooldownDuration_res, "Rare Tree", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.AppleTreePositions, this.appleTreeCooldowns_res, this.appleTreeHideUntil_res, this.appleTreeCooldownDuration_res, "Apple Tree", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.OrangeTreePositions, this.orangeTreeCooldowns_res, this.orangeTreeHideUntil_res, this.orangeTreeCooldownDuration_res, "Mandarin Tree", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.RockPositions, this.rockCooldowns, this.rockHideUntil, this.rockCooldownDuration, "Stone", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, HeartopiaComplete.OrePositions, this.oreCooldowns, this.oreHideUntil, this.oreCooldownDuration, "Ore", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, this.blueberryPositions, this.blueberryCooldowns, this.blueberryHideUntil, this.blueberryCooldownDuration, "Blueberry", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            this.TrySelectVisitedResourceCooldown(nodePosition, this.raspberryPositions, this.raspberryCooldowns, this.raspberryHideUntil, this.raspberryCooldownDuration, "Raspberry", ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);

            if (bestCooldowns == null || bestHideUntil == null || bestIndex < 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            float until = now + Math.Max(1f, bestDuration);
            float hideUntil = now + 10f;

            float existing;
            if (!bestCooldowns.TryGetValue(bestIndex, out existing) || existing < until)
            {
                bestCooldowns[bestIndex] = until;
            }

            bestHideUntil[bestIndex] = hideUntil;
            ModLogger.Msg($"[AutoFarm] Visit fallback cooldown stamped: {bestLabel} #{bestIndex} ({Math.Max(1f, bestDuration):F1}s)");
            if (this.isRadarActive)
            {
                this.RunRadar();
            }
        }

        private void TrySelectVisitedResourceCooldown(
            Vector3 nodePosition,
            Vector3[] candidates,
            Dictionary<int, float> cooldowns,
            Dictionary<int, float> hideUntil,
            float duration,
            string label,
            ref float bestSqr,
            ref int bestIndex,
            ref float bestDuration,
            ref string bestLabel,
            ref Dictionary<int, float> bestCooldowns,
            ref Dictionary<int, float> bestHideUntil)
        {
            int idx = this.FindClosestItemIndexLocal(nodePosition, candidates);
            if (idx < 0)
            {
                return;
            }

            float sqr = (candidates[idx] - nodePosition).sqrMagnitude;
            if (sqr >= bestSqr)
            {
                return;
            }

            bestSqr = sqr;
            bestIndex = idx;
            bestDuration = duration;
            bestLabel = label;
            bestCooldowns = cooldowns;
            bestHideUntil = hideUntil;
        }

        private bool IsCurrentPriorityNodeNearby(float maxDistance)
        {
            if (!this.currentPriorityLocation.HasValue || this.radarContainer == null || Camera.main == null)
            {
                return false;
            }

            string token = this.GetPriorityTokenForLocation(this.currentPriorityLocation.Value);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            Vector3 playerPos = Camera.main.transform.position;
            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                GameObject marker = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(marker);
                if (string.IsNullOrEmpty(markerLabel))
                {
                    continue;
                }

                if (this.IsMarkerOnCooldown(marker))
                {
                    continue;
                }

                if (markerLabel.Contains(token) && Vector3.Distance(playerPos, child.position) <= maxDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAvailablePriorityNodeForLocation(Vector3 location)
        {
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return false;
            }

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                GameObject markerObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(markerObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(markerObject))
                {
                    continue;
                }

                Vector3? mappedLocation = this.GetPriorityLocationForNodeText(markerLabel);
                if (!mappedLocation.HasValue || mappedLocation.Value != location)
                {
                    continue;
                }

                bool isRecentlyVisited = false;
                foreach (Vector3 visitedNode in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, visitedNode) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }

                if (!isRecentlyVisited)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldShowMushroomByLabel(string labelText)
        {
            if (string.IsNullOrEmpty(labelText))
            {
                return false;
            }

            if (this.showMushroomRadar)
            {
                return labelText.Contains("Mushroom")
                    || labelText.Contains("Oyster")
                    || labelText.Contains("Button")
                    || labelText.Contains("Penny Bun")
                    || labelText.Contains("Shiitake")
                    || labelText.Contains("Truffle");
            }

            return (this.showOysterMushroomRadar && labelText.Contains("Oyster"))
                || (this.showButtonMushroomRadar && labelText.Contains("Button"))
                || (this.showPennyBunRadar && labelText.Contains("Penny Bun"))
                || (this.showShiitakeRadar && labelText.Contains("Shiitake"))
                || (this.showTruffleRadar && labelText.Contains("Truffle"));
        }

        private bool IsMushroomLocationEnabled(string locationName)
        {
            if (this.showMushroomRadar)
            {
                return true;
            }

            string name = (locationName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("oyster"))
            {
                return this.showOysterMushroomRadar;
            }

            if (name.Contains("button"))
            {
                return this.showButtonMushroomRadar;
            }

            if (name.Contains("penny"))
            {
                return this.showPennyBunRadar;
            }

            if (name.Contains("shiitake"))
            {
                return this.showShiitakeRadar;
            }

            if (name.Contains("truffle"))
            {
                return this.showTruffleRadar;
            }

            // Unknown mushroom location names: allow if any specific mushroom toggle is enabled.
            return this.showOysterMushroomRadar
                || this.showButtonMushroomRadar
                || this.showPennyBunRadar
                || this.showShiitakeRadar
                || this.showTruffleRadar;
        }

        private bool ShouldShowForageMesh(string forageText)
        {
            if (forageText.Contains("pleurotus"))
            {
                return this.showMushroomRadar || this.showOysterMushroomRadar;
            }

            if (forageText.Contains("tricholoma"))
            {
                return this.showMushroomRadar || this.showButtonMushroomRadar;
            }

            if (forageText.Contains("boletus"))
            {
                return this.showMushroomRadar || this.showPennyBunRadar;
            }

            if (forageText.Contains("shiitake"))
            {
                return this.showMushroomRadar || this.showShiitakeRadar;
            }

            if (forageText.Contains("truffle"))
            {
                return this.showMushroomRadar || this.showTruffleRadar;
            }

            // Underwater gatherables are owned by the live ECS scan (ScanUnderwaterGatherablesAura),
            // never by this BRG mesh path — explicitly ignore them here so a wakame mesh that happens
            // to flow through the dynamicbush batch isn't mislabeled as a mushroom.
            if (forageText.Contains("seaasparagus") || forageText.Contains("glasswort")
                || forageText.Contains("seagrape") || forageText.Contains("wakame"))
            {
                return false;
            }

            if (forageText.Contains("fiddlehead") || forageText.Contains("fiddle") || forageText.Contains("fern") || forageText.Contains("pterid") || forageText.Contains("bracken"))
            {
                return this.showFiddleheadRadar;
            }

            if (forageText.Contains("burdock"))
            {
                return this.showBurdockRadar;
            }

            if (forageText.Contains("shepherdspurse") || ((forageText.Contains("mustard") && forageText.Contains("green"))) || forageText.Contains("mustard greens") || forageText.Contains("mustardgreens") || forageText.Contains("mustard_green") || forageText.Contains("mustardgreen") || forageText.Contains("greens"))
            {
                return this.showMustardGreensRadar;
            }

            if (forageText.Contains("tall mustard") || forageText.Contains("tallmustard") || forageText.Contains("mustard"))
            {
                return this.showTallMustardRadar;
            }

            return this.showMushroomRadar;
        }

        private void ToggleAutoFarm()
        {
            bool flag = this.AnyRadarLootToggleEnabled();
            
            // Fix: Check if Radar is active before enabling Auto Farm
            if (!this.autoFarmActive)
            {
                if (!flag)
                {
                    this.autoFarmStatus = "NO_TOGGLES_ERROR";
                    return;
                }
                if (!this.auraFarmEnabled)
                {
                    this.autoFarmStatus = "MODE_REQUIRED_ERROR";
                    this.AddMenuNotification("Enable Aura Farm first", new Color(1f, 0.75f, 0.45f));
                    return;
                }
                if (!this.isRadarActive)
                {
                    this.autoFarmStatus = "RADAR_OFF_ERROR";
                    return;
                }
            }

            this.autoFarmActive = !this.autoFarmActive;
            bool flag3 = this.autoFarmActive;
            if (flag3)
            {
                this.SetGameSpeed(5f);
                this.CheckRadarAutoToggle(); // This won't auto-enable radar, but checks consistency
                this.autoFarmStatus = "Starting Auto Farm...";
                this.autoFarmTimer = 0f;
                this.nextLiveColdSyncAt = 0f; // fresh authoritative cold states before the first hop
                this.lastScanTime = 0f;       // rebuild radar markers from them in the same frame (sync -> RunRadar -> farm tick order in OnUpdate)
                this.currentLocationIndex = 0;
                this.recentlyVisitedNodes.Clear();
                this.cameraRotationAttempts = 0;
                this.ResetContaminationDwellState();
                this.ResetCorruptionCleanseState();
                this.priorityLocationCooldowns.Clear();
                this.RefreshActivePriorityLocations();
                this.currentPriorityLocation = this.GetActivePriorityLocation();
                this.lastTeleportWasPriorityLocation = false;
                this.priorityRecheckTimer = 0f; // Reset recheck timer
                this.AutoFarmLog("Started. activePriorityLocations=" + this.activePriorityLocations.Count
                    + " currentPriorityLocation=" + (this.currentPriorityLocation.HasValue ? this.currentPriorityLocation.Value.ToString() : "none"));

                if (this.currentPriorityLocation.HasValue)
                {
                    this.AutoFarmLog("Startup routing to priority location " + this.currentPriorityLocation.Value);
                    this.FarmTeleportTo(this.currentPriorityLocation.Value);
                    this.lastTeleportWasPriorityLocation = true;
                    this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                    this.autoFarmStatus = "Going to priority location...";
                }
                else
                {
                    this.AutoFarmLog("Startup entering normal scan mode (no active priority location).");
                    this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
                }

                int autoStopSeconds = this.GetAutoFarmAutoStopSeconds();
                if (this.autoFarmAutoStopEnabled && autoStopSeconds > 0)
                {
                    this.autoFarmAutoStopAt = Time.unscaledTime + autoStopSeconds;
                    this.AddMenuNotification("Auto Farm auto-stop set: " + this.FormatDurationHms(autoStopSeconds), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.autoFarmAutoStopAt = -1f;
                }

                ModLogger.Msg("[AUTO FARM] Enabled");
            }
            else
            {
                this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                this.autoFarmStatus = "READY";
                this.autoFarmTimer = 0f;
                this.SetGameSpeed(1f);
                this.currentPriorityLocation = null;
                this.lastTeleportWasPriorityLocation = false;
                this.autoFarmAutoStopAt = -1f;
                this.ResetContaminationDwellState();
                this.ResetCorruptionCleanseState();
                this.AutoFarmLog("Stopped. reason=manual-toggle");
                ModLogger.Msg("[AUTO FARM] Disabled");
            }
        }

        private int GetAutoFarmAutoStopSeconds()
        {
            return Math.Max(0, this.autoFarmAutoStopHours) * 3600
                + Math.Max(0, this.autoFarmAutoStopMinutes) * 60
                + Math.Max(0, this.autoFarmAutoStopSeconds);
        }

        private void StartPatrol()
        {
            if (patrolPoints.Count == 0) return;
            isPatrolActive = true;
            patrolCoroutine = ModCoroutines.Start(PatrolRoutine());
        }

        private System.Collections.IEnumerator PatrolRoutine()
        {
            int index = 0;
            while (isPatrolActive)
            {
                if (patrolPoints.Count == 0) break;

                // 1. TELEPORT
                TeleportTo(patrolPoints[index]);

                // 2. WAIT
                yield return new WaitForSeconds(waitAtSpot);

                // 3. WORK LOOP (Prioritize Cooking)
                // Loop 15 times to ensure buttons are clicked.
                for (int i = 0; i < 15; i++)
                {
                    RunSpamClicker();
                    yield return new WaitForSeconds(0.12f);
                }

                // 4. CLEANUP (Unstuck)
                // If menu is still open, close it now.
                ForceCloseMenuIfOpen();

                // 5. NEXT POINT
                index++;
                if (index >= patrolPoints.Count) index = 0;
            }
            isPatrolActive = false;
        }

        private string GetPatrolPath()
        {
            return HelperPaths.GetFile("patrol_points.json");
        }

        private void SavePatrolPoints()
        {
            try
            {
                UnifiedConfigData config = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(config);
                this.SaveUnifiedConfig(config);
                ModLogger.Msg("Patrol points saved!");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error saving patrol points: " + ex.Message);
            }
        }

        private void LoadPatrolPoints()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    patrolPoints.Clear();
                    foreach (SerializableVector3 point in config.Patrol.Points)
                    {
                        if (point != null) patrolPoints.Add(point.ToVector3());
                    }
                    ModLogger.Msg($"Loaded {patrolPoints.Count} patrol points.");
                    return;
                }
                string path = this.GetPatrolPath();
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path);
                patrolPoints.Clear();
                string[] lines = json.Split('{');
                foreach (string line in lines)
                {
                    if (line.Contains("\"x\":"))
                    {
                        float x = float.Parse(ExtractJsonVal(line, "\"x\":"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                        float y = float.Parse(ExtractJsonVal(line, "\"y\":"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                        float z = float.Parse(ExtractJsonVal(line, "\"z\":"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                        patrolPoints.Add(new Vector3(x, y, z));
                    }
                }
                ModLogger.Msg($"Loaded {patrolPoints.Count} patrol points.");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error loading patrol points: " + ex.Message);
            }
        }

        private bool AreHeavyFarmAutomationsActive()
        {
            return BirdNetFarm.IsEnabled || InsectNetFarm.IsEnabled;
        }

        private float DrawPatrolTab(int startY)
        {
            int num = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
            GUI.Label(new Rect(20f, (float)num, 260f, 25f), "PATROL SYSTEM", headerStyle);
            num += 30;

            GUI.Label(new Rect(20f, (float)num, 260f, 20f), $"Points: {patrolPoints.Count}");
            num += 25;

            if (GUI.Button(new Rect(20f, (float)num, 120f, 35f), "SAVE"))
            {
                SavePatrolPoints();
            }
            if (GUI.Button(new Rect(160f, (float)num, 120f, 35f), "LOAD"))
            {
                LoadPatrolPoints();
            }
            num += 45;

            if (GUI.Button(new Rect(20f, (float)num, 260f, 40f), "ADD CURRENT POSITION"))
            {
                GameObject p = GetPlayer();
                if (p != null)
                {
                    patrolPoints.Add(p.transform.position);
                    ModLogger.Msg("Added patrol point at current position.");
                }
            }
            num += 50;

            if (GUI.Button(new Rect(20f, (float)num, 260f, 35f), "CLEAR ALL"))
            {
                patrolPoints.Clear();
                ModLogger.Msg("Cleared all patrol points.");
            }
            num += 45;

            GUI.Label(new Rect(20f, (float)num, 260f, 20f), $"Wait Time: {waitAtSpot:F2}s");
            num += 22;
            waitAtSpot = Mathf.Round(this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), waitAtSpot, 0.1f, 2.0f) * 100f) / 100f;
            num += 40;

            GUI.color = isPatrolActive ? Color.red : Color.green;
            if (GUI.Button(new Rect(20f, (float)num, 260f, 50f), isPatrolActive ? "STOP PATROL" : "START PATROL"))
            {
                if (isPatrolActive)
                {
                    isPatrolActive = false;
                    if (patrolCoroutine != null)
                    {
                        ModCoroutines.Stop(patrolCoroutine);
                        patrolCoroutine = null;
                    }
                    ModLogger.Msg("Patrol stopped.");
                }
                else
                {
                    StartPatrol();
                }
            }
            GUI.color = Color.white;

            return (float)num + 60f;
        }

        // Token: 0x02000008 RID: 8
        private class FarmLocation
        {
            // Token: 0x0600002E RID: 46 RVA: 0x00008437 File Offset: 0x00006637
            public FarmLocation(string name, Vector3 position, string type)
            {
                this.Name = name;
                this.Position = position;
                this.Type = type;
            }

            // Token: 0x04000052 RID: 82
            public string Name;

            // Token: 0x04000053 RID: 83
            public Vector3 Position;

            // Token: 0x04000054 RID: 84
            public string Type;
        }

        // Token: 0x02000009 RID: 9
        private enum AutoFarmState
        {
            // Token: 0x04000056 RID: 86
            Idle,
            // Token: 0x04000057 RID: 87
            ScanningForNodes,
            // Token: 0x04000058 RID: 88
            TeleportingToNode,
            // Token: 0x04000059 RID: 89
            Collecting,
            // Token: 0x0400005A RID: 90
            MovingToLocation,
            // Token: 0x0400005B RID: 91
            LoadingArea,
            // Token: 0x0400005C RID: 92
            WaitingForNodes,
            // Token: 0x0400005D RID: 93
            WaitingForPriorityArea,
            // Corrupted-debuff cleanse hold (CorruptionCleanseFeature.cs)
            CleansingCorruption
        }

    }
}
