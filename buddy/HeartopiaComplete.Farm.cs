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
        private float CalculateAutoFarmTabHeight()
        {
            if (this.autoFarmSubTab == 1)
            {
                return this.CalculateTreeFarmTabHeight();
            }
            if (this.autoFarmSubTab == 2)
            {
                return this.CalculateNewSubTabHeight();
            }
            if (this.autoFarmSubTab == 3)
            {
                return 980f; // Insect farm tab height estimate
            }
            if (this.autoFarmSubTab == 4)
            {
                return 980f; // Bird farm tab height estimate
            }

            // Main tab content - estimate based on typical layout
            return 780f; // Conservative estimate for main foraging tab
        }

        private float CalculateTreeFarmTabHeight()
        {
            // Chop & Mine now includes a larger Aura section; keep scroll content tall enough
            // so status rows (targets/tree hits/bush picks/last) are always reachable.
            return 1500f;
        }

        private string GetForagingModeLabel()
        {
            if (this.auraFarmEnabled)
            {
                return "Aura Farm";
            }

            if (this.autoFarmEnabled)
            {
                return "Auto Collect";
            }

            return "No mode";
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
                return this.DrawTreeFarmTab(startY);
            }
            if (this.autoFarmSubTab == 2)
            {
                return this.DrawNewSubTab(startY);
            }
            if (this.autoFarmSubTab == 3)
            {
                return InsectNetFarm.DrawSection(this, startY);
            }
            if (this.autoFarmSubTab == 4)
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

            if (this.auraFarmEnabled && this.autoFarmEnabled)
            {
                this.SetAutoCollectEnabled(false, false);
            }

            bool hasForagingMode = this.auraFarmEnabled || this.autoFarmEnabled;
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
                    this.autoFarmEnabled = false;
                    this.SetGameSpeed(1f);
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                }
            }
            else if (!hasForagingMode)
            {
                statusText = "Choose Aura Farm or Auto Collect";
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
                statusText = "Choose Aura Farm or Auto Collect";
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
            string modeText = this.auraFarmEnabled ? "Aura Farm" : (this.autoFarmEnabled ? "Auto Collect" : "No mode");
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

            float settingsHeight = 184f + (this.autoFarmEnabled ? 98f : 0f) + (this.autoFarmAutoStopEnabled ? 44f : 0f);
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

            float autoCollectToggleWidth = 250f;
            float autoCollectToggleHeight = this.GetSwitchToggleHeight(autoCollectToggleWidth, "Auto Collect", 25f);
            bool newAutoCollectEnabled = this.DrawWrappedSwitchToggle(new Rect(settingsPanel.x + 14f, rowY, autoCollectToggleWidth, autoCollectToggleHeight), this.autoFarmEnabled, "Auto Collect", 25f);
            if (newAutoCollectEnabled != this.autoFarmEnabled)
            {
                this.SetAutoCollectEnabled(newAutoCollectEnabled);
            }
            rowY += Mathf.Ceil(autoCollectToggleHeight + 8f);
            if (this.autoFarmEnabled)
            {
                GUI.Label(new Rect(settingsPanel.x + 34f, rowY, 160f, 20f), this.L("Collect Types:"), bodyStyle);
                rowY += 24f;
                float collectToggleWidth = 240f;
                float collectHeight = this.GetSwitchToggleHeight(collectToggleWidth, "Mushrooms", 20f);
                this.collectMushrooms = this.DrawWrappedSwitchToggle(new Rect(settingsPanel.x + 34f, rowY, collectToggleWidth, collectHeight), this.collectMushrooms, "Mushrooms", 20f);
                rowY += Mathf.Ceil(collectHeight + 4f);
                collectHeight = this.GetSwitchToggleHeight(collectToggleWidth, "Berries / Bushes / Plants", 20f);
                this.collectBerries = this.DrawWrappedSwitchToggle(new Rect(settingsPanel.x + 34f, rowY, collectToggleWidth, collectHeight), this.collectBerries, "Berries / Bushes / Plants", 20f);
                rowY += Mathf.Ceil(collectHeight + 4f);
                collectHeight = this.GetSwitchToggleHeight(collectToggleWidth, "Event Resources", 20f);
                this.collectEventResources = this.DrawWrappedSwitchToggle(new Rect(settingsPanel.x + 34f, rowY, collectToggleWidth, collectHeight), this.collectEventResources, "Event Resources", 20f);
                rowY += Mathf.Ceil(collectHeight + 8f);
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

        private float DrawTreeFarmTab(int startY)
        {
            int num = startY;
            if (this.DrawPrimaryActionButton(new Rect(20f, (float)num, 260f, 35f), "Equip Axe"))
            {
                this.EquipHandTool(1);
            }
            num += 45;
            string toggleText = this.autoResourceFarmEnabled ? "DISABLE CHOP & MINE" : "ENABLE CHOP & MINE";
            if (this.DrawPrimaryActionButton(new Rect(20f, (float)num, 260f, 40f), toggleText))
            {
                this.ToggleResourceFarm();
            }
            num += 50;

            GUI.Label(new Rect(20f, (float)num, 320f, 24f), this.LF("Status: {0}", this.L(this.GetResourceFarmStatus())));
            num += 28;
            GUI.Label(new Rect(20f, (float)num, 320f, 24f), this.LF("Available: {0}", this.GetTotalAvailableResources()));
            num += 28;
            GUI.Label(new Rect(20f, (float)num, 320f, 24f), this.LF("Markers: {0}", this.resourceMarkerPositions.Count));
            num += 32;

            // --- AUTO STOP TIMER for Resource Farm (moved above Teleport Cooldown) ---
            this.autoResourceFarmAutoStopEnabled = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.autoResourceFarmAutoStopEnabled, "Auto Stop Timer");
            num += 30;

            if (this.autoResourceFarmAutoStopEnabled)
            {
                GUIStyle timerSmall = new GUIStyle(GUI.skin.label) { fontSize = 12 };

                GUI.Label(new Rect(20f, (float)num, 260f, 18f), "Timer (HH:MM:SS)", timerSmall);
                num += 20;

                GUI.Label(new Rect(20f, (float)num, 45f, 20f), "H", timerSmall);
                this.autoResourceFarmAutoStopHoursInput = GUI.TextField(new Rect(35f, (float)num, 55f, 22f), this.autoResourceFarmAutoStopHoursInput, 2);
                GUI.Label(new Rect(95f, (float)num, 10f, 20f), ":", timerSmall);

                GUI.Label(new Rect(108f, (float)num, 45f, 20f), "M", timerSmall);
                this.autoResourceFarmAutoStopMinutesInput = GUI.TextField(new Rect(123f, (float)num, 55f, 22f), this.autoResourceFarmAutoStopMinutesInput, 2);
                GUI.Label(new Rect(183f, (float)num, 10f, 20f), ":", timerSmall);

                GUI.Label(new Rect(196f, (float)num, 45f, 20f), "S", timerSmall);
                this.autoResourceFarmAutoStopSecondsInput = GUI.TextField(new Rect(211f, (float)num, 55f, 22f), this.autoResourceFarmAutoStopSecondsInput, 2);
                num += 28;

                int parsed;
                if (int.TryParse(this.autoResourceFarmAutoStopHoursInput, out parsed))
                {
                    this.autoResourceFarmAutoStopHours = Mathf.Clamp(parsed, 0, 23);
                    this.autoResourceFarmAutoStopHoursInput = this.autoResourceFarmAutoStopHours.ToString();
                }
                if (int.TryParse(this.autoResourceFarmAutoStopMinutesInput, out parsed))
                {
                    this.autoResourceFarmAutoStopMinutes = Mathf.Clamp(parsed, 0, 59);
                    this.autoResourceFarmAutoStopMinutesInput = this.autoResourceFarmAutoStopMinutes.ToString();
                }
                if (int.TryParse(this.autoResourceFarmAutoStopSecondsInput, out parsed))
                {
                    this.autoResourceFarmAutoStopSeconds = Mathf.Clamp(parsed, 0, 59);
                    this.autoResourceFarmAutoStopSecondsInput = this.autoResourceFarmAutoStopSeconds.ToString();
                }

                int asSeconds = this.GetAutoResourceFarmAutoStopSeconds();
                if (asSeconds <= 0)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.45f, 0.45f);
                    GUI.Label(new Rect(20f, (float)num, 300f, 20f), "Set at least 1 second to enable auto-stop.", timerSmall);
                    GUI.color = prev;
                    num += 24;
                }
                else
                {
                    GUI.Label(new Rect(20f, (float)num, 320f, 20f), "Auto-stop after: " + this.FormatDurationHms(asSeconds), timerSmall);
                    num += 22;

                    if (this.autoResourceFarmEnabled && this.autoResourceFarmAutoStopAt > 0f)
                    {
                        int remaining = Mathf.Max(0, Mathf.CeilToInt(this.autoResourceFarmAutoStopAt - Time.unscaledTime));
                        GUI.Label(new Rect(20f, (float)num, 320f, 20f), "Time remaining: " + this.FormatDurationHms(remaining), timerSmall);
                        num += 22;
                    }
                }
            }

            GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Teleport Cooldown: {0:F1}s", this.resourceTeleportCooldown));
            num += 22;
            float prevResourceTp = this.resourceTeleportCooldown;
            this.resourceTeleportCooldown = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.resourceTeleportCooldown, 0f, 10f);
            if (Math.Abs(this.resourceTeleportCooldown - prevResourceTp) > 0.0001f) { try { this.SaveKeybinds(false); } catch { } }
            num += 30;

            GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Click Duration: {0:F1}s", this.resourceClickDuration));
            num += 22;
            float prevResourceClick = this.resourceClickDuration;
            this.resourceClickDuration = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.resourceClickDuration, 0.1f, 5f);
            if (Math.Abs(this.resourceClickDuration - prevResourceClick) > 0.0001f) { try { this.SaveKeybinds(false); } catch { } }
            num += 30;
            // Auto Repair pause slider: how long to pause teleports after a repair toast
            GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Auto-Repair Tool (Paused TP FARM): {0:F0}s", this.resourceAutoRepairPauseSeconds));
            num += 22;
            float prevResourcePause = this.resourceAutoRepairPauseSeconds;
            this.resourceAutoRepairPauseSeconds = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.resourceAutoRepairPauseSeconds, 0f, 60f);
            if (Math.Abs(this.resourceAutoRepairPauseSeconds - prevResourcePause) > 0.0001f) { try { this.SaveKeybinds(false); } catch { } }
            num += 30;

            
            this.farmRocks = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmRocks, "Farm Rocks");
            num += 25;
            this.farmOres = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmOres, "Farm Ores");
            num += 25;
            this.farmTrees = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmTrees, "Farm Trees");
            num += 25;
            this.farmRareTrees = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmRareTrees, "Farm Rare Trees");
            num += 25;
            this.farmAppleTrees = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmAppleTrees, "Farm Apple Trees");
            num += 25;
            this.farmOrangeTrees = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.farmOrangeTrees, "Farm Mandarin Trees");
            num += 28;

            if (this.DrawDangerActionButton(new Rect(20f, (float)num, 260f, 35f), "Reset Cooldowns"))
            {
                this.ResetAllCooldowns();
            }
            num += 45;
            GUI.Label(
                new Rect(20f, (float)num, 360f, 120f),
                this.L("Chop & Mine flow:")
                + "\n" + this.L("- Build list of available markers")
                + "\n" + this.L("- Shuffle and teleport to markers")
                + "\n" + this.L("- Simulate F key for configured duration")
                + "\n" + this.L("- Mark resource collected and set cooldowns"));
            return (float)num + 120f;
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
            if (!this.isRadarActive && !this.autoFarmActive && !this.autoResourceFarmEnabled && !this.auraFarmEnabled)
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

        private void TryMarkNearestNodeCollectedFromPrompt()
        {
            Vector3 playerPos;
            if (!this.TryGetLocalPlayerPosition(out playerPos))
            {
                return;
            }

            bool hasMinePrompt = this.IsPromptButtonReady("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            bool hasChopPrompt = this.IsPromptButtonReady("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");

            if (hasMinePrompt)
            {
                this.MarkNearestCooldownEntry(playerPos,
                    new Vector3[][] { HeartopiaComplete.RockPositions, HeartopiaComplete.OrePositions },
                    new Dictionary<int, float>[] { this.rockCooldowns, this.oreCooldowns },
                    new Dictionary<int, float>[] { this.rockHideUntil, this.oreHideUntil },
                    new float[] { this.rockCooldownDuration, this.oreCooldownDuration },
                    new string[] { "Stone", "Ore" },
                    new bool[] { this.showStoneRadar || this.farmRocks, this.showOreRadar || this.farmOres });
            }

            if (hasChopPrompt)
            {
                this.MarkNearestCooldownEntry(playerPos,
                    new Vector3[][] { HeartopiaComplete.TreePositions, HeartopiaComplete.RareTreePositions, HeartopiaComplete.AppleTreePositions, HeartopiaComplete.OrangeTreePositions },
                    new Dictionary<int, float>[] { this.treeCooldowns_res, this.rareTreeCooldowns_res, this.appleTreeCooldowns_res, this.orangeTreeCooldowns_res },
                    new Dictionary<int, float>[] { this.treeHideUntil_res, this.rareTreeHideUntil_res, this.appleTreeHideUntil_res, this.orangeTreeHideUntil_res },
                    new float[] { this.treeCooldownDuration_res, this.rareTreeCooldownDuration_res, this.appleTreeCooldownDuration_res, this.orangeTreeCooldownDuration_res },
                    new string[] { "Tree", "Rare Tree", "Apple Tree", "Mandarin Tree" },
                    new bool[] { this.showTreeRadar || this.farmTrees, this.showRareTreeRadar || this.farmRareTrees, this.showAppleTreeRadar || this.farmAppleTrees, this.showOrangeTreeRadar || this.farmOrangeTrees });
            }
        }

        private void StartTreeFarm()
        {
            if (this.autoFarmActive)
            {
                this.autoFarmActive = false;
                this.autoFarmEnabled = false;
                this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                this.autoFarmStatus = "READY";
            }
            this.treeFarmEnabled = true;
            this.treeFarmState = HeartopiaComplete.TreeFarmState.EquipAxe;
            this.treeFarmCurrentIndex = Mathf.Clamp(this.treeFarmCurrentIndex, 0, Math.Max(0, this.treeFarmPoints.Count - 1));
            this.treeFarmChopSent = 0;
            this.treeFarmNextActionAt = Time.time;
            this.treeFarmStatus = "Equipping Axe...";
            this.EquipHandTool(1);
            // If using hardcoded positions, populate the patrol points list from static arrays
            if (this.treeFarmUseHardcoded)
            {
                this.treeFarmPoints.Clear();
                foreach (Vector3 v in TreePositions) this.treeFarmPoints.Add(new TreeFarmPatrolPoint(v, Quaternion.identity));
                foreach (Vector3 v2 in RareTreePositions) this.treeFarmPoints.Add(new TreeFarmPatrolPoint(v2, Quaternion.identity));
                foreach (Vector3 v3 in AppleTreePositions) this.treeFarmPoints.Add(new TreeFarmPatrolPoint(v3, Quaternion.identity));
                foreach (Vector3 v4 in OrangeTreePositions) this.treeFarmPoints.Add(new TreeFarmPatrolPoint(v4, Quaternion.identity));
                // Shuffle the points to avoid predictable order
                int n = this.treeFarmPoints.Count;
                while (n > 1)
                {
                    n--;
                    int k = this.instanceRng.Next(n + 1);
                    TreeFarmPatrolPoint tmp = this.treeFarmPoints[k];
                    this.treeFarmPoints[k] = this.treeFarmPoints[n];
                    this.treeFarmPoints[n] = tmp;
                }
                this.treeFarmCurrentIndex = 0;
                this.AddMenuNotification($"Tree farm points populated ({this.treeFarmPoints.Count})", new Color(0.45f, 1f, 0.55f));
            }

            this.AddMenuNotification("Tree Farm enabled", new Color(0.45f, 1f, 0.55f));
        }

        private void ToggleResourceFarm()
        {
            this.autoResourceFarmEnabled = !this.autoResourceFarmEnabled;
            if (this.autoResourceFarmEnabled)
            {
                ModLogger.Msg("[ResourceFarm] ENABLED! Make sure you're holding an axe/pickaxe!");
                this.ResetResourceFarmState();
                int autoStopSeconds = this.GetAutoResourceFarmAutoStopSeconds();
                if (this.autoResourceFarmAutoStopEnabled && autoStopSeconds > 0)
                {
                    this.autoResourceFarmAutoStopAt = Time.unscaledTime + autoStopSeconds;
                    this.AddMenuNotification("Resource Farm auto-stop set: " + this.FormatDurationHms(autoStopSeconds), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.autoResourceFarmAutoStopAt = -1f;
                }
            }
            else
            {
                ModLogger.Msg("[ResourceFarm] DISABLED!");
                this.ResetResourceFarmState();
                this.resourceJustArrived = false;
                SimulateFKeyHeld = false;
                SimulateFKeyDown = false;
                SimulateFKeyUp = false;
                this.fKeySimFrame = 0;
                this.autoResourceFarmAutoStopAt = -1f;
            }
        }

        private void ResetResourceFarmState()
        {
            this.hasResourceStartPosition = false;
            this.currentResourceMarkerIndex = 0;
            this.isResourceReturningToStart = false;
            this.visitedResourceMarkerIndices.Clear();
            this.resourceMarkersNeedShuffle = true;
        }

        public void UpdateResourceFarm()
        {
            if (!this.autoResourceFarmEnabled) return;
            this.UpdateResourceMarkerPositions();
            // Auto-stop check
            if (this.autoResourceFarmAutoStopEnabled && this.autoResourceFarmAutoStopAt > 0f && Time.unscaledTime >= this.autoResourceFarmAutoStopAt)
            {
                ModLogger.Msg("[ResourceFarm] Auto-stop timer reached. Stopping resource farm.");
                this.AddMenuNotification("Resource Farm auto-stopped", new Color(0.9f, 0.55f, 0.55f));
                this.autoResourceFarmEnabled = false;
                this.ResetResourceFarmState();
                this.resourceJustArrived = false;
                SimulateFKeyHeld = false;
                SimulateFKeyDown = false;
                SimulateFKeyUp = false;
                this.fKeySimFrame = 0;
                this.autoResourceFarmAutoStopAt = -1f;
                return;
            }
            if (!OverridePlayerPosition && !this.resourceJustArrived && Time.unscaledTime - this.lastResourceTeleportTime > this.resourceTeleportCooldown)
            {
                if (this.resourceMarkerPositions.Count > 0)
                {
                    // If paused due to auto-repair, skip starting a teleport until pause expires
                    if (Time.time < this.resourceRepairPauseUntil)
                    {
                        return;
                    }
                    this.TeleportToNextResource();
                    this.lastResourceTeleportTime = Time.unscaledTime;
                }
            }
            if (this.resourceJustArrived)
            {
                float dt = Time.unscaledTime - this.resourceArrivalTime;
                if (dt > this.resourceClickDuration)
                {
                    this.resourceJustArrived = false;
                    SimulateFKeyHeld = false;
                    SimulateFKeyDown = false;
                    SimulateFKeyUp = false;
                    this.fKeySimFrame = 0;
                    GameObject player = this.FindPlayerRoot();
                    if (player != null)
                    {
                        this.MarkResourceCollected(player.transform.position);
                    }
                    ModLogger.Msg("[ResourceFarm] Done pressing F, ready for next resource");
                }
                else if (dt > this.resourceArrivalDelay)
                {
                    // Wait until the gather UI is present before attempting interaction
                    if (!this.IsGatherWidgetVisible())
                    {
                        this.autoFarmStatus = "Waiting for gather UI...";
                        return;
                    }
                    this.fKeySimFrame++;
                    int m = this.fKeySimFrame % 6;
                    if (m == 0)
                    {
                        SimulateFKeyDown = true;
                        SimulateFKeyHeld = true;
                        SimulateFKeyUp = false;
                        this.resourceClickCount++;
                    }
                    else if (m <= 3)
                    {
                        SimulateFKeyDown = false;
                        SimulateFKeyHeld = true;
                        SimulateFKeyUp = false;
                    }
                    else if (m == 4)
                    {
                        SimulateFKeyDown = false;
                        SimulateFKeyHeld = false;
                        SimulateFKeyUp = true;
                    }
                    else
                    {
                        SimulateFKeyDown = false;
                        SimulateFKeyHeld = false;
                        SimulateFKeyUp = false;
                    }
                    this.DirectClickInteractButton();
                }
            }
            else
            {
                if (SimulateFKeyHeld || SimulateFKeyDown)
                {
                    SimulateFKeyHeld = false;
                    SimulateFKeyDown = false;
                    SimulateFKeyUp = false;
                    this.fKeySimFrame = 0;
                }
            }
        }

        public int GetResourceAvailableCount(Dictionary<int,float> cooldowns, int total)
        {
            int c = 0;
            float t = Time.time;
            for (int i=0;i<total;i++)
            {
                float until;
                if (cooldowns.TryGetValue(i,out until) && until > t) continue;
                c++;
            }
            return c;
        }

        public int GetTotalAvailableResources()
        {
            return GetResourceAvailableCount(this.rockCooldowns, HeartopiaComplete.RockPositions.Length)
                + GetResourceAvailableCount(this.oreCooldowns, HeartopiaComplete.OrePositions.Length)
                + GetResourceAvailableCount(this.treeCooldowns_res, HeartopiaComplete.TreePositions.Length)
                + GetResourceAvailableCount(this.rareTreeCooldowns_res, HeartopiaComplete.RareTreePositions.Length)
                + GetResourceAvailableCount(this.appleTreeCooldowns_res, HeartopiaComplete.AppleTreePositions.Length)
                + GetResourceAvailableCount(this.orangeTreeCooldowns_res, HeartopiaComplete.OrangeTreePositions.Length);
        }

        public string GetResourceFarmStatus()
        {
            if (!this.autoResourceFarmEnabled) return "DISABLED";
            if (this.resourceJustArrived) return "GATHERING...";
            if (this.isResourceFarmTeleport) return "TELEPORTING...";
            return "IDLE";
        }

        private void MarkResourceCollected(Vector3 playerPos)
        {
            float hide = 10f;
            if (this.farmRocks)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.RockPositions);
                if (idx >= 0)
                {
                    this.rockCooldowns[idx] = Time.time + this.rockCooldownDuration;
                    this.rockHideUntil[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Rock #{idx} collected, cooldown {this.rockCooldownDuration}s");
                }
            }
            if (this.farmOres)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.OrePositions);
                if (idx >= 0)
                {
                    this.oreCooldowns[idx] = Time.time + this.oreCooldownDuration;
                    this.oreHideUntil[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Ore #{idx} collected, cooldown {this.oreCooldownDuration}s");
                }
            }
            if (this.farmTrees)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.TreePositions);
                if (idx >= 0)
                {
                    this.treeCooldowns_res[idx] = Time.time + this.treeCooldownDuration_res;
                    this.treeHideUntil_res[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Tree #{idx} collected, cooldown {this.treeCooldownDuration_res}s");
                }
            }
            if (this.farmRareTrees)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.RareTreePositions);
                if (idx >= 0)
                {
                    this.rareTreeCooldowns_res[idx] = Time.time + this.rareTreeCooldownDuration_res;
                    this.rareTreeHideUntil_res[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Rare Tree #{idx} collected, cooldown {this.rareTreeCooldownDuration_res}s");
                }
            }
            if (this.farmAppleTrees)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.AppleTreePositions);
                if (idx >= 0)
                {
                    this.appleTreeCooldowns_res[idx] = Time.time + this.appleTreeCooldownDuration_res;
                    this.appleTreeHideUntil_res[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Apple Tree #{idx} collected, cooldown {this.appleTreeCooldownDuration_res}s");
                }
            }
            if (this.farmOrangeTrees)
            {
                int idx = this.FindClosestItemIndexLocal(playerPos, HeartopiaComplete.OrangeTreePositions);
                if (idx >= 0)
                {
                    this.orangeTreeCooldowns_res[idx] = Time.time + this.orangeTreeCooldownDuration_res;
                    this.orangeTreeHideUntil_res[idx] = Time.time + hide;
                    ModLogger.Msg($"[ResourceFarm] Mandarin Tree #{idx} collected, cooldown {this.orangeTreeCooldownDuration_res}s");
                }
            }
        }

        private void SyncNearbyLiveResourceCooldowns()
        {
            // Only sync when farming features are active - NOT when just radar is enabled
            // This prevents Mono API from activating when only Radar ESP is on
            bool shouldSync = this.autoFarmActive || this.autoResourceFarmEnabled || this.auraFarmEnabled;
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

        private void ApplyLiveResourceCooldownByPosition(Vector3 entityPosition, long coldEndTimeMs, int availableNum, string resTypeName, long nowUnixMs, float nowUnscaled)
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
            float bestSqr = 25f;

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
                targetCooldowns[targetIndex] = nowUnscaled + secondsRemaining;
                if (targetHideUntil != null)
                {
                    targetHideUntil[targetIndex] = nowUnscaled + 10f;
                }
            }
            else if (availableNum != 0)
            {
                float localUntil;
                if (isBushType && targetCooldowns.TryGetValue(targetIndex, out localUntil) && localUntil > nowUnscaled)
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

        private void StopTreeFarm(string reason = "Idle")
        {
            this.treeFarmEnabled = false;
            this.treeFarmState = HeartopiaComplete.TreeFarmState.Idle;
            this.treeFarmChopSent = 0;
            this.treeFarmStatus = reason;
            this.CloseToolboxIfOpen();
        }

        private void RunTreeFarmLogic()
        {
            if (!this.treeFarmEnabled)
            {
                this.awaitingSwingConfirm = false;
                return;
            }

            // If we're waiting for a recent swing attempt to be confirmed, poll for confirmation non-blocking
            if (this.awaitingSwingConfirm)
            {
                try
                {
                    bool confirmed = false;
                    // Check animator change
                    // Animator checks removed (not available in this build); rely on swing button state only

                    // Check swing button change
                    if (!confirmed)
                    {
                        GameObject swingBtn = GameObject.Find(this.swingButtonPath);
                        if (swingBtn != null)
                        {
                            Button b = swingBtn.GetComponent<Button>();
                            bool nowInteract = (b != null) ? b.interactable : swingBtn.activeInHierarchy;
                            if (nowInteract != this.swingConfirmStartBtnInteract)
                            {
                                confirmed = true;
                                ModLogger.Msg("[TreeFarm] Swing confirmed by button interactable change (async)");
                            }
                        }
                    }

                    if (confirmed)
                    {
                        this.treeFarmChopSent++;
                        this.treeFarmNoPromptAttempts = 0;
                        this.awaitingSwingConfirm = false;
                        this.treeFarmStatus = $"Chopping {this.treeFarmChopSent}/{this.treeFarmChopPressCount}...";
                        this.treeFarmNextActionAt = Time.time + this.treeFarmChopPressGap;
                        return;
                    }

                    if (Time.time > this.swingConfirmDeadline)
                    {
                        // confirmation timed out
                        this.awaitingSwingConfirm = false;
                        this.treeFarmNoPromptAttempts++;
                        this.treeFarmNextActionAt = Time.time + 0.15f;
                        ModLogger.Msg("[TreeFarm] Swing confirmation timed out (async)");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[TreeFarm] Async confirm error: " + ex.Message);
                    this.awaitingSwingConfirm = false;
                }
            }

            if (this.treeFarmPoints.Count == 0)
            {
                this.StopTreeFarm("No points");
                return;
            }

            if (Time.time < this.treeFarmNextActionAt)
            {
                return;
            }

            switch (this.treeFarmState)
            {
                case HeartopiaComplete.TreeFarmState.EquipAxe:
                    this.EquipHandTool(1);
                    this.treeFarmStatus = "Waiting after axe equip...";
                    this.treeFarmState = HeartopiaComplete.TreeFarmState.WaitAfterEquip;
                    this.treeFarmNextActionAt = Time.time + 2f;
                    break;

                case HeartopiaComplete.TreeFarmState.WaitAfterEquip:
                    this.treeFarmState = HeartopiaComplete.TreeFarmState.TeleportToPoint;
                    this.treeFarmNextActionAt = Time.time;
                    break;

                case HeartopiaComplete.TreeFarmState.TeleportToPoint:
                    if (this.treeFarmCurrentIndex < 0 || this.treeFarmCurrentIndex >= this.treeFarmPoints.Count)
                    {
                        this.treeFarmCurrentIndex = 0;
                    }
                    TreeFarmPatrolPoint point = this.treeFarmPoints[this.treeFarmCurrentIndex];
                    Vector3 targetPos = point.Position.ToVector3();
                    Quaternion targetRot = point.Rotation.ToQuaternion();
                    // If hardcoded resource-style mode is active, skip points that are on cooldown
                    if (this.treeFarmUseHardcoded)
                    {
                        int attempts = 0;
                        bool found = false;
                        while (attempts < this.treeFarmPoints.Count)
                        {
                            Vector3 checkPos = this.treeFarmPoints[this.treeFarmCurrentIndex].Position.ToVector3();
                            int tIdx = this.FindClosestItemIndexLocal(checkPos, TreePositions);
                            if (tIdx >= 0)
                            {
                                float until;
                                if (this.treeCooldowns.TryGetValue(tIdx, out until) && until > Time.time)
                                {
                                    // skip
                                    this.treeFarmCurrentIndex++;
                                    if (this.treeFarmCurrentIndex >= this.treeFarmPoints.Count) this.treeFarmCurrentIndex = 0;
                                    attempts++;
                                    continue;
                                }
                            }
                            int rIdx = this.FindClosestItemIndexLocal(checkPos, RareTreePositions);
                            if (rIdx >= 0)
                            {
                                float until2;
                                if (this.rareTreeCooldowns.TryGetValue(rIdx, out until2) && until2 > Time.time)
                                {
                                    this.treeFarmCurrentIndex++;
                                    if (this.treeFarmCurrentIndex >= this.treeFarmPoints.Count) this.treeFarmCurrentIndex = 0;
                                    attempts++;
                                    continue;
                                }
                            }
                            int aIdx = this.FindClosestItemIndexLocal(checkPos, AppleTreePositions);
                            if (aIdx >= 0)
                            {
                                float until3;
                                if (this.appleTreeCooldowns.TryGetValue(aIdx, out until3) && until3 > Time.time)
                                {
                                    this.treeFarmCurrentIndex++;
                                    if (this.treeFarmCurrentIndex >= this.treeFarmPoints.Count) this.treeFarmCurrentIndex = 0;
                                    attempts++;
                                    continue;
                                }
                            }
                            int oIdx = this.FindClosestItemIndexLocal(checkPos, OrangeTreePositions);
                            if (oIdx >= 0)
                            {
                                float until4;
                                if (this.orangeTreeCooldowns.TryGetValue(oIdx, out until4) && until4 > Time.time)
                                {
                                    this.treeFarmCurrentIndex++;
                                    if (this.treeFarmCurrentIndex >= this.treeFarmPoints.Count) this.treeFarmCurrentIndex = 0;
                                    attempts++;
                                    continue;
                                }
                            }
                            found = true;
                            break;
                        }
                        if (!found)
                        {
                            this.StopTreeFarm("No available tree positions");
                            return;
                        }
                        point = this.treeFarmPoints[this.treeFarmCurrentIndex];
                        targetPos = point.Position.ToVector3();
                        targetRot = point.Rotation.ToQuaternion();
                    }
                    ModLogger.Msg($"[TreeFarm] Teleporting to point {this.treeFarmCurrentIndex + 1}/{this.treeFarmPoints.Count} at {targetPos}");
                    this.TeleportToLocation(targetPos, targetRot);
                    this.treeFarmStatus = $"Teleported to tree point {this.treeFarmCurrentIndex + 1}/{this.treeFarmPoints.Count}";
                    this.treeFarmState = HeartopiaComplete.TreeFarmState.WaitAfterTeleport;
                    this.treeFarmNextActionAt = Time.time + this.treeFarmArrivalDelay;
                    break;

                case HeartopiaComplete.TreeFarmState.WaitAfterTeleport:
                    GameObject player = GameObject.Find("p_player_skeleton(Clone)");
                    if (player != null)
                    {
                        Vector3 currentPos = player.transform.position;
                        ModLogger.Msg($"[TreeFarm] After teleport, current position: {currentPos}");
                    }
                    this.treeFarmChopSent = 0;
                    this.treeFarmNoPromptAttempts = 0;
                    this.treeFarmState = HeartopiaComplete.TreeFarmState.ChopAtPoint;
                    this.treeFarmNextActionAt = Time.time;
                    break;

                case HeartopiaComplete.TreeFarmState.ChopAtPoint:
                    bool chopped = false;
                    // Respect a cooldown so we don't spam triggers too quickly
                    if (Time.time - this.lastAutoSwingTime >= this.swingCooldown)
                    {
                        bool attempted = false;
                        // Prefer direct trigger activation
                        if (this.PerformAutoSwing())
                        {
                            attempted = true;
                            this.lastAutoSwingTime = Time.time;
                            // Start async confirmation window; actual counting happens in the async poll above
                            this.awaitingSwingConfirm = true;
                            this.swingConfirmDeadline = Time.time + 0.9f;
                            // clear anim-hash baseline (anim not relied upon)
                            this.swingConfirmStartAnimHash = 0;
                            GameObject swingBtnObj = GameObject.Find(this.swingButtonPath);
                            if (swingBtnObj != null)
                            {
                                Button bb = swingBtnObj.GetComponent<Button>();
                                this.swingConfirmStartBtnInteract = (bb != null) ? bb.interactable : swingBtnObj.activeInHierarchy;
                            }
                        }
                        else
                        {
                            // Fallback to existing TryClickInteractPrompt
                            if (this.TryClickInteractPrompt())
                            {
                                attempted = true;
                                this.lastAutoSwingTime = Time.time;
                                this.awaitingSwingConfirm = true;
                                this.swingConfirmDeadline = Time.time + 0.9f;
                                this.swingConfirmStartAnimHash = 0;
                                GameObject swingBtnObj2 = GameObject.Find(this.swingButtonPath);
                                if (swingBtnObj2 != null)
                                {
                                    Button bb2 = swingBtnObj2.GetComponent<Button>();
                                    this.swingConfirmStartBtnInteract = (bb2 != null) ? bb2.interactable : swingBtnObj2.activeInHierarchy;
                                }
                            }
                        }

                        if (!attempted)
                        {
                            this.treeFarmNoPromptAttempts++;
                        }
                        else if (!chopped)
                        {
                            // Attempted but no confirmed swing
                            this.treeFarmNoPromptAttempts++;
                        }
                    }

                    ModLogger.Msg($"[TreeFarm] Chop attempt {this.treeFarmChopSent}/{this.treeFarmChopPressCount} - Success: {chopped}, NoPromptAttempts: {this.treeFarmNoPromptAttempts}");
                    this.treeFarmStatus = chopped
                        ? $"Chopping {this.treeFarmChopSent}/{this.treeFarmChopPressCount}..."
                        : "Waiting for chop prompt...";

                    if (this.treeFarmChopSent >= Math.Max(1, this.treeFarmChopPressCount))
                    {
                        ModLogger.Msg($"[TreeFarm] Finished chopping at point {this.treeFarmCurrentIndex + 1}, moving to next");
                        // If using hardcoded resource-style mode, mark the closest tree as collected so cooldowns apply
                        if (this.treeFarmUseHardcoded)
                        {
                            try
                            {
                                GameObject playerObj = GameObject.Find("p_player_skeleton(Clone)");
                                if (playerObj != null)
                                {
                                    this.MarkTreeCollected(playerObj.transform.position);
                                }
                            }
                            catch (Exception ex)
                            {
                                ModLogger.Msg("[TreeFarm] MarkTreeCollected error: " + ex.Message);
                            }
                        }
                        this.treeFarmState = HeartopiaComplete.TreeFarmState.WaitNextPoint;
                        this.treeFarmNextActionAt = Time.time + this.treeFarmNextLocationWait;
                    }
                    else if (this.treeFarmNoPromptAttempts >= 20)
                    {
                        ModLogger.Msg($"[TreeFarm] No chop action after 20 attempts at point {this.treeFarmCurrentIndex + 1}, skipping");
                        this.treeFarmStatus = "No chop action, skipping point...";
                        this.treeFarmState = HeartopiaComplete.TreeFarmState.WaitNextPoint;
                        this.treeFarmNextActionAt = Time.time + 0.3f;
                    }
                    else
                    {
                        this.treeFarmNextActionAt = Time.time + (chopped ? this.treeFarmChopPressGap : 0.15f);
                    }
                    break;

                case HeartopiaComplete.TreeFarmState.WaitNextPoint:
                    this.treeFarmCurrentIndex++;
                    if (this.treeFarmCurrentIndex >= this.treeFarmPoints.Count)
                    {
                        this.treeFarmCurrentIndex = 0;
                    }
                    this.treeFarmState = HeartopiaComplete.TreeFarmState.TeleportToPoint;
                    this.treeFarmNextActionAt = Time.time;
                    this.treeFarmStatus = "Moving to next point...";
                    break;
            }
        }

        private void MarkTreeCollected(Vector3 playerPos)
        {
            float hideDelay = this.treeHideDelay;

            int idx = this.FindClosestItemIndexLocal(playerPos, TreePositions);
            if (idx >= 0)
            {
                this.treeCooldowns[idx] = Time.time + this.treeCooldownDuration;
                this.treeHideUntil[idx] = Time.time + hideDelay;
                ModLogger.Msg($"[TreeFarm] Tree #{idx} collected, cooldown {this.treeCooldownDuration}s");
            }

            int idx2 = this.FindClosestItemIndexLocal(playerPos, RareTreePositions);
            if (idx2 >= 0)
            {
                this.rareTreeCooldowns[idx2] = Time.time + this.rareTreeCooldownDuration;
                this.rareTreeHideUntil[idx2] = Time.time + hideDelay;
                ModLogger.Msg($"[TreeFarm] Rare Tree #{idx2} collected, cooldown {this.rareTreeCooldownDuration}s");
            }

            int idx3 = this.FindClosestItemIndexLocal(playerPos, AppleTreePositions);
            if (idx3 >= 0)
            {
                this.appleTreeCooldowns[idx3] = Time.time + this.appleTreeCooldownDuration;
                this.appleTreeHideUntil[idx3] = Time.time + hideDelay;
                ModLogger.Msg($"[TreeFarm] Apple Tree #{idx3} collected, cooldown {this.appleTreeCooldownDuration}s");
            }

            int idx4 = this.FindClosestItemIndexLocal(playerPos, OrangeTreePositions);
            if (idx4 >= 0)
            {
                this.orangeTreeCooldowns[idx4] = Time.time + this.orangeTreeCooldownDuration;
                this.orangeTreeHideUntil[idx4] = Time.time + hideDelay;
                ModLogger.Msg($"[TreeFarm] Mandarin Tree #{idx4} collected, cooldown {this.orangeTreeCooldownDuration}s");
            }
        }

        private bool CanHarvestTree()
        {
            // Removed world-object checks - allow auto-chop unconditionally
            // This will let the bot attempt swings at each point regardless of unknown in-game object names
            return true;
        }

        // Token: 0x06000015 RID: 21 RVA: 0x00003ECC File Offset: 0x000020CC
        private void RunAutoFarmLogic()
        {
            this.RefreshActivePriorityLocations();
            this.autoFarmTimer += Time.unscaledDeltaTime;
            this.priorityRecheckTimer += Time.unscaledDeltaTime;
            if (this.ShouldRunMeteorAutoInteract())
            {
                if (!this.meteorAutoInteractActive)
                {
                    this.StartMeteorAutoInteractSequence();
                }
                this.UpdateMeteorAutoInteractSequence();
            }
            else if (this.meteorAutoInteractActive)
            {
                this.StopMeteorAutoInteractSequence();
            }
            bool flag = this.cameraStuckDisplayTimer > 0f;
            if (flag)
            {
                this.cameraStuckDisplayTimer -= Time.unscaledDeltaTime;
            }
            switch (this.farmState)
            {
                case HeartopiaComplete.AutoFarmState.ScanningForNodes:
                    {
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
                                this.TeleportToLocation(recheckLocation.Value);
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
                                    this.TeleportToLocation(activeAreaPriorityNode.Value);
                                    this.lastNodePosition = activeAreaPriorityNode.Value;
                                    this.lastTeleportWasPriorityLocation = true;
                                    this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                                    this.autoFarmTimer = 0f;
                                    this.autoCollectClickedSinceArrival = false;
                                    this.cameraRotationAttempts = 0;
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
                            this.TeleportToLocation(priorityNode.Value);
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
                            break;
                        }

                        // SECOND: Route to an active priority location even if no priority node is visible yet.
                        Vector3? priorityLocation = this.GetActivePriorityLocation();
                        if (priorityLocation != null)
                        {
                            float distance = Vector3.Distance(Camera.main.transform.position, priorityLocation.Value);
                            this.autoFarmStatus = $"Going to priority location ({distance:F0}m)...";
                            this.AutoFarmLog("Priority location fallback -> " + priorityLocation.Value + " distance=" + distance.ToString("F1"));
                            this.TeleportToLocation(priorityLocation.Value);
                            this.currentPriorityLocation = priorityLocation;
                            this.lastTeleportWasPriorityLocation = true;
                            this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // THIRD: Normal scanning logic
                        Vector3? vector = this.FindClosestAvailableNode();
                        bool flag2 = vector != null;
                        if (flag2)
                        {
                            float value = Vector3.Distance(Camera.main.transform.position, vector.Value);
                            this.autoFarmStatus = $"Teleporting to node ({value:F0}m)...";
                            this.AutoFarmLog("Normal node target -> " + vector.Value + " distance=" + value.ToString("F1"));
                            this.TeleportToLocation(vector.Value);
                            this.lastNodePosition = vector.Value;
                            this.lastTeleportWasPriorityLocation = false;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
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
                        bool flag3 = this.autoFarmTimer >= 5f;
                        if (flag3)
                        {
                            this.recentlyVisitedNodes[this.lastNodePosition] = Time.unscaledTime + 15f;
                            this.FinishCollectingCycle();
                        }
                        else
                        {
                            bool flag4 = this.autoFarmTimer >= 3f;
                            if (flag4)
                            {
                                this.recentlyVisitedNodes[this.lastNodePosition] = Time.unscaledTime + 15f;
                                this.FinishCollectingCycle();
                            }
                            else
                            {
                                bool hasReadyPrompt = this.HasReadyAutoCollectPrompt();
                                bool hasAnyPrompt = hasReadyPrompt || this.HasAnyVisibleInteractPrompt();
                                bool auraHasRecentCommand = this.auraFarmEnabled && Time.unscaledTime - this.auraLastSuccessfulCommandAt <= 1.2f;
                                bool flag5 = !this.autoCollectClickedSinceArrival && (!this.auraFarmEnabled || this.auraLastTargetCount <= 0 || !auraHasRecentCommand) && !HeartopiaComplete.OverrideCameraPosition && !hasAnyPrompt;
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
                                this.autoFarmStatus = "No matching locations for enabled toggles!";
                            }
                            else
                            {
                                this.autoFarmStatus = "Moving to " + farmLocation.Name + "...";
                                this.TeleportToLocation(farmLocation.Position);
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
                        Vector3? vector2 = this.FindClosestAvailableNode();
                        bool flag21 = vector2 != null;
                        if (flag21)
                        {
                            float value2 = Vector3.Distance(Camera.main.transform.position, vector2.Value);
                            this.autoFarmStatus = $"Node found! Teleporting ({value2:F0}m)...";
                            this.TeleportToLocation(vector2.Value);
                            this.lastNodePosition = vector2.Value;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
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
                            // Start collecting at priority location
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.autoFarmStatus = "Farming at priority location...";
                        }
                        else
                        {
                            this.autoFarmStatus = $"Loading priority area... ({this.areaLoadDelay - this.autoFarmTimer:F1}s remaining)";
                        }
                        break;
                    }
            }
        }

        // Token: 0x06000016 RID: 22 RVA: 0x0000459C File Offset: 0x0000279C
        private Vector3? FindClosestAvailableNode()
        {
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
                                    bool flag10 = this.ShouldShowMushroomByLabel(markerLabel)
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
                if (!this.auraFarmEnabled && !this.autoFarmEnabled)
                {
                    this.autoFarmStatus = "MODE_REQUIRED_ERROR";
                    this.AddMenuNotification("Select Aura Farm or Auto Collect first", new Color(1f, 0.75f, 0.45f));
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
                this.currentLocationIndex = 0;
                this.recentlyVisitedNodes.Clear();
                this.cameraRotationAttempts = 0;
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
                    this.TeleportToLocation(this.currentPriorityLocation.Value);
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
                this.StopMeteorAutoInteractSequence();
                this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                this.autoFarmStatus = "READY";
                this.autoFarmTimer = 0f;
                this.SetGameSpeed(1f);
                HeartopiaComplete.OverrideCameraPosition = false;
                this.cameraOverrideFramesRemaining = 0;
                this.currentPriorityLocation = null;
                this.lastTeleportWasPriorityLocation = false;
                this.autoFarmAutoStopAt = -1f;
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

        private int GetAutoResourceFarmAutoStopSeconds()
        {
            return Math.Max(0, this.autoResourceFarmAutoStopHours) * 3600
                + Math.Max(0, this.autoResourceFarmAutoStopMinutes) * 60
                + Math.Max(0, this.autoResourceFarmAutoStopSeconds);
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

        private string GetTreeFarmPatrolPath()
        {
            return HelperPaths.GetFile("tree_farm_patrol_points.json");
        }

        private void SaveTreeFarmPatrolPoints()
        {
            try
            {
                UnifiedConfigData config = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(config);
                this.SaveUnifiedConfig(config);
                ModLogger.Msg($"Tree farm patrol points saved! ({treeFarmPoints.Count} points with rotations)");
                this.AddMenuNotification($"Tree farm points saved ({treeFarmPoints.Count})", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error saving tree farm patrol points: " + ex.Message);
                this.AddMenuNotification("Failed to save tree farm points", new Color(1f, 0.4f, 0.4f));
            }
        }

        private void LoadTreeFarmPatrolPoints()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    treeFarmPoints.Clear();
                    foreach (TreeFarmPatrolPoint point in config.TreeFarmPatrol.Points)
                    {
                        if (point != null) treeFarmPoints.Add(point);
                    }
                    ModLogger.Msg($"Loaded {treeFarmPoints.Count} tree farm patrol points (with rotations: true).");
                    return;
                }
                string path = this.GetTreeFarmPatrolPath();
                if (!File.Exists(path))
                {
                    ModLogger.Msg("Tree farm patrol points file not found.");
                    this.AddMenuNotification("No saved tree farm points found", new Color(1f, 0.55f, 0.55f));
                    return;
                }
                treeFarmPoints.Clear();
                string json = File.ReadAllText(path);

                // Check if this is the new format (with Rotation) or old format (just Vector3)
                bool hasRotation = json.Contains("\"Rotation\"");

                // Parse JSON manually - find all coordinate blocks
                int pointsStart = json.IndexOf("[");
                int pointsEnd = json.LastIndexOf("]");
                if (pointsStart == -1 || pointsEnd == -1) return;

                string pointsSection = json.Substring(pointsStart + 1, pointsEnd - pointsStart - 1);

                if (hasRotation)
                {
                    // New format with Position and Rotation
                    string[] pointBlocks = pointsSection.Split(new string[] { "    }" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string block in pointBlocks)
                    {
                        if (!block.Contains("Position")) continue;

                        // Extract Position block (between "Position": { and })
                        int posStart = block.IndexOf("\"Position\"");
                        if (posStart == -1) continue;
                        int posObjStart = block.IndexOf("{", posStart);
                        int posObjEnd = block.IndexOf("}", posObjStart);
                        string posBlock = block.Substring(posObjStart, posObjEnd - posObjStart + 1);

                        float px = ExtractCoordinate(posBlock, "x");
                        float py = ExtractCoordinate(posBlock, "y");
                        float pz = ExtractCoordinate(posBlock, "z");

                        // Extract Rotation block (between "Rotation": { and })
                        int rotStart = block.IndexOf("\"Rotation\"");
                        Quaternion rotation = Quaternion.identity;
                        if (rotStart != -1)
                        {
                            int rotObjStart = block.IndexOf("{", rotStart);
                            int rotObjEnd = block.IndexOf("}", rotObjStart);
                            string rotBlock = block.Substring(rotObjStart, rotObjEnd - rotObjStart + 1);

                            float rx = ExtractCoordinate(rotBlock, "x");
                            float ry = ExtractCoordinate(rotBlock, "y");
                            float rz = ExtractCoordinate(rotBlock, "z");
                            float rw = ExtractCoordinate(rotBlock, "w");
                            rotation = new Quaternion(rx, ry, rz, rw);
                        }

                        treeFarmPoints.Add(new TreeFarmPatrolPoint(new Vector3(px, py, pz), rotation));
                    }
                }
                else
                {
                    // Old format - just Vector3 positions, use default rotation
                    string[] pointBlocks = pointsSection.Split(new string[] { "}," }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string block in pointBlocks)
                    {
                        string cleanBlock = block.Trim().Trim('{').Trim('}').Trim(',');
                        if (string.IsNullOrEmpty(cleanBlock)) continue;

                        // Extract x, y, z values
                        float x = ExtractCoordinate(cleanBlock, "\"x\"");
                        float y = ExtractCoordinate(cleanBlock, "\"y\"");
                        float z = ExtractCoordinate(cleanBlock, "\"z\"");

                        treeFarmPoints.Add(new TreeFarmPatrolPoint(new Vector3(x, y, z), Quaternion.identity));
                    }
                }
                ModLogger.Msg($"Loaded {treeFarmPoints.Count} tree farm patrol points (with rotations: {hasRotation}).");
                this.AddMenuNotification($"Tree farm points loaded ({treeFarmPoints.Count})", new Color(0.45f, 1f, 0.55f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error loading tree farm patrol points: " + ex.Message);
                this.AddMenuNotification("Failed to load tree farm points", new Color(1f, 0.4f, 0.4f));
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

    }
}
