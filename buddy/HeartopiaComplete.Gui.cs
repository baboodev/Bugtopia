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
        // Token: 0x06000006 RID: 6 RVA: 0x000027FC File Offset: 0x000009FC
        public void OnGUI()
        {
            Breadcrumbs.Tick("OnGUI");
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
            this.EnsureThemeStyles();

            // Reset Color
            GUI.color = Color.white;

            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                this.DrawResourceVisualEspOverlay();
                this.DrawVisualDebugEspOverlay();
            }

            this.DrawBuildingMovePanel();

            bool flag = !this.showMenu;
            if (flag)
            {
                this.wasMouseOverMenuLastFrame = false;
            }
            else
            {
                this.targetWindowWidth = 1180f;
                this.targetWindowHeight = 720f;

                // Auto - Resize Window Height & Width
                if (Mathf.Abs(this.windowRect.height - this.targetWindowHeight) > 1f)
                {
                    this.windowRect.height = Mathf.Lerp(this.windowRect.height, this.targetWindowHeight, Time.unscaledDeltaTime * 10f);
                }
                if (Mathf.Abs(this.windowRect.width - this.targetWindowWidth) > 1f)
                {
                    this.windowRect.width = Mathf.Lerp(this.windowRect.width, this.targetWindowWidth, Time.unscaledDeltaTime * 10f);
                }

                Color prevColor = GUI.color;
                Color prevBg = GUI.backgroundColor;
                Color prevContent = GUI.contentColor;
                Matrix4x4 previousMatrix = GUI.matrix;
                try
                {
                    GUI.color = Color.white;
                    GUI.backgroundColor = Color.white;
                    GUI.contentColor = Color.white;
                    float menuScale = this.GetUiScale();
                    this.KeepMenuWindowOnScreen(menuScale);
                    GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(menuScale, menuScale, 1f));
                    this.windowRect = GUI.Window(0, this.windowRect, (GUI.WindowFunction)this.DrawWindow, "", this.themeWindowStyle ?? GUI.skin.window);
                }
                finally
                {
                    GUI.matrix = previousMatrix;
                    GUI.color = prevColor;
                    GUI.backgroundColor = prevBg;
                    GUI.contentColor = prevContent;
                }
                Vector2 vector = new Vector2(Input.mousePosition.x, (float)Screen.height - Input.mousePosition.y);
                float hitScale = this.GetUiScale();
                Vector2 logicalVector = hitScale > 0.001f ? vector / hitScale : vector;
                this.wasMouseOverMenuLastFrame = this.windowRect.Contains(logicalVector);
                bool flag2 = this.wasMouseOverMenuLastFrame;
                if (flag2)
                {
                    Event current = Event.current;
                    if (current != null && current.isMouse && current.type != EventType.Used)
                    {
                        current.Use();
                    }
                }
            }

            // Draw optional on-screen status overlay (left-center)
            if (this.showStatusOverlay)
            {
                float ow = this.GetStatusOverlayWidth();
                float oh = this.GetStatusOverlayHeight();
                float ox = Mathf.Clamp(16f, 8f, Screen.width - ow - 8f);
                float oy = Mathf.Clamp((Screen.height - oh) * 0.5f, 72f, Screen.height - oh - 24f);
                Rect overlayRect = new Rect(ox, oy, ow, oh);

                Rect inner = new Rect(overlayRect.x + 10f, overlayRect.y + 10f, overlayRect.width - 20f, overlayRect.height - 20f);
                this.DrawStatusOverlay(inner);
            }

            this.DrawMouseLookCrosshair();
            this.DrawMenuNotifications(new Rect((float)Screen.width - 280f, 14f, 260f, (float)Screen.height - 20f));
        }

        private void KeepMenuWindowOnScreen(float scale)
        {
            scale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
            float maxX = Mathf.Max(0f, ((float)Screen.width / scale) - this.windowRect.width);
            float maxY = Mathf.Max(0f, ((float)Screen.height / scale) - this.windowRect.height);
            this.windowRect.x = Mathf.Clamp(this.windowRect.x, 0f, maxX);
            this.windowRect.y = Mathf.Clamp(this.windowRect.y, 0f, maxY);
        }

        private float GetSelectedTabEstimatedHeight()
        {
            // Dynamic height calculation - future-proof for UI changes
            return this.GetSelectedTabCalculatedHeight();
        }

        private float GetSelectedTabCalculatedHeight()
        {
            if (this.selectedTab == 0)
            {
                return this.CalculateSelfTabHeight();
            }
            if (this.selectedTab == 2)
            {
                return this.CalculateAutoFarmTabHeight();
            }
            if (this.selectedTab == 3)
            {
                return this.CalculateFeaturesTabHeight();
            }
            if (this.selectedTab == 8)
            {
                return this.CalculateNewFeaturesTabHeight();
            }
            if (this.selectedTab == 4)
            {
                return this.CalculateRadarTabHeight();
            }
            if (this.selectedTab == 5)
            {
                return this.CalculateTeleportTabHeight();
            }
            if (this.selectedTab == 6)
            {
                return this.CalculateBulkSelectorTabHeight();
            }
            if (this.selectedTab == 7)
            {
                return this.CalculateSettingsTabHeight();
            }
            return 740f; // Default fallback
        }

        private float CalculateSelfTabHeight()
        {
            int num = 0 + 25; // startY is 0 for calculation

            if (this.selfSubTab == 0)
            {
                // Camera Toggle
                num += 30;

                if (this.mouseLookEnabled)
                {
                    // Crosshair toggle
                    num += 30;
                }

                // Noclip Toggle
                num += 30;

                // Noclip Speed label and slider
                num += 22 + 30;

                // Noclip Boost label and slider
                num += 22 + 30;

                // Anti AFK toggle
                num += 26;

                // AFK interval label and slider
                num += 22 + 30;

                // Help text
                return (float)num + 160f;
            }

            // Building sub-tab: Bypass overlap toggle
            if (this.selfSubTab == 1)
            {
                num += 26; // Header
                num += 36; // Toggle
                // Credits text
                return (float)num + 50f;
            }

            return (float)num + 50f;
        }

        private float CalculateNewSubTabHeight()
        {
            // Fish farming tab - estimate based on settings and toggles
            return 780f; // Conservative estimate
        }

        private float CalculateFeaturesTabHeight()
        {
            if (this.automationSubTab == 1)
            {
                float height = 550f;
                if (this.autoEatFoodDropdownOpen)
                {
                    height += 240f;
                }
                if (this.customFoodPickMode && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1)
                {
                    height += 310f;
                }
                return height;
            }

            if (this.automationSubTab == 4)
            {
                return 980f;
            }

            if (this.automationSubTab == 3)
            {
                return this.forceOpenShopDropdownOpen ? 1348f : 1128f;
            }

            if (this.automationSubTab == 5)
            {
                if (this.netCookMiniGameOnly)
                {
                    return 540f;
                }

                return this.netCookRecipeDropdownOpen ? 840f : 620f;
            }

            if (this.automationSubTab == 6)
            {
                return 620f;
            }

            if (this.automationSubTab == 7)
            {
                return 898f + this.GetPetFeedFavoriteUiTableHeight();
            }

            return 900f;
        }

        private float CalculateBulkSelectorTabHeight()
        {
            // Items selector tab
            return 780f; // Conservative estimate
        }

        private void DrawSidebarTabButton(Rect rect, string label, int tabIndex)
        {
            bool active = this.selectedTab == tabIndex;
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color fill = active
                ? new Color(accent.r * 0.18f, accent.g * 0.12f, accent.b * 0.22f, 0.95f)
                : new Color(0f, 0f, 0f, 0f);
            if (active)
            {
                this.DrawRoundedPanel(rect, 18f, fill, Color.clear, 0f, Color.clear);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.04f);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (active)
            {
                this.EnsureUiPrimitiveTextures();
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f);
                GUI.DrawTexture(new Rect(rect.xMax - 20f, rect.center.y - 5f, 10f, 10f), this.uiCircleTexture);
                GUI.color = Color.white;
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.fontSize = 12;
            labelStyle.normal.textColor = active ? new Color(this.uiTextR, this.uiTextG, this.uiTextB) : new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB, 0.62f);
            GUI.Label(new Rect(rect.x + 18f, rect.y, rect.width - 42f, rect.height), this.L(label), labelStyle);

            if (GUI.Button(rect, "", GUIStyle.none))
            {
                if (this.selectedTab != tabIndex)
                {
                    this.selectedTab = tabIndex;
                    this.tabScrollPos = Vector2.zero;
                    this.tabDrawContentHeight = 0f;
                }
            }
        }

        private string GetSelectedTabHeader()
        {
            if (this.selectedTab == 0) return this.L("Self");
            if (this.selectedTab == 2) return this.L("Resource Gathering");
            if (this.selectedTab == 3) return this.L("Features");
            if (this.selectedTab == 8) return this.L("New Features");
            if (this.selectedTab == 4) return this.L("Radar");
            if (this.selectedTab == 5) return this.L("Teleport");
            if (this.selectedTab == 6) return this.L("Bag / Warehouse");
            if (this.selectedTab == 7) return this.L("Settings");
            return "Unknown";
        }

        private void SetAutomationSubTab(int subTab)
        {
            if (this.automationSubTab != subTab)
            {
                this.automationSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
                if (subTab == 1)
                {
                    this.RefreshFoodRepairUiStatusSnapshot(true);
                }
            }
        }

        private void SetSelfSubTab(int subTab)
        {
            if (this.selfSubTab != subTab)
            {
                this.selfSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
            }
        }

        private float DrawNewSubTab(int startY)
        {
            return AutoFishingFarm.DrawSection(this, startY);
        }

        // Token: 0x06000009 RID: 9 RVA: 0x00002E24 File Offset: 0x00001024
        private float DrawAutomationTab(int startY)
        {
            int num = startY + 25;

            if (this.automationSubTab == 0)
            {
                float automationToggleWidth = 260f;
                float toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Hide UI + Player (Client Side)", 25f);
                this.bypassEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.bypassEnabled, "Hide UI + Player (Client Side)", 25f);
                num += Mathf.CeilToInt(toggleHeight + 8f);
                bool prevHideJumpButton = this.hideJumpButtonEnabled;
                toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Hide Jump Button (Space still works)", 25f);
                this.hideJumpButtonEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.hideJumpButtonEnabled, "Hide Jump Button (Space still works)", 25f);
                if (this.hideJumpButtonEnabled != prevHideJumpButton)
                {
                    this.cachedJumpButtonGo = null;
                    try { this.SaveKeybinds(false); } catch { }
                }
                num += Mathf.CeilToInt(toggleHeight + 8f);
                bool prevBunnyHop = this.bunnyHopEnabled;
                toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Bunny Hop (hold Space)", 25f);
                this.bunnyHopEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.bunnyHopEnabled, "Bunny Hop (hold Space)", 25f);
                if (this.bunnyHopEnabled != prevBunnyHop)
                {
                    if (!this.bunnyHopEnabled)
                    {
                        this.ResetBunnyHopState();
                    }

                    try { this.SaveKeybinds(false); } catch { }
                }
                num += Mathf.CeilToInt(toggleHeight + 8f);
                toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Bird Vacuum (Client Side)", 25f);
                this.birdVacuumEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.birdVacuumEnabled, "Bird Vacuum (Client Side)", 25f);
                num += Mathf.CeilToInt(toggleHeight + 8f);
                
                bool prevFastBubbleGen = this.fastBubbleGenEnabled;
                toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Fast Bubble Gen", 25f);
                this.fastBubbleGenEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.fastBubbleGenEnabled, "Fast Bubble Gen", 25f);
                if (this.fastBubbleGenEnabled != prevFastBubbleGen)
                {
                    this.bubbleSpawnRateAccumulator = 0f;
                    this.RequestBubbleFeatureImmediateRetry();
                    try { this.SaveKeybinds(false); } catch { }
                }
                num += Mathf.CeilToInt(toggleHeight + 8f);

                if (this.fastBubbleGenEnabled)
                {
                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), string.Format("Bubbles per minute: {0:F0}", this.bubbleBubblesPerMinute));
                    num += 22;
                    float prevBubbleRate = this.bubbleBubblesPerMinute;
                    float newBubbleRate = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.bubbleBubblesPerMinute, 0f, 100f);
                    if (Math.Abs(newBubbleRate - prevBubbleRate) > 0.01f)
                    {
                        this.bubbleBubblesPerMinute = Mathf.Clamp(newBubbleRate, 0f, 100f);
                        this.bubbleSpawnRateAccumulator = 0f;
                        try { this.SaveKeybinds(false); } catch { }
                    }
                    num += 28;
                }

                Rect rect = new Rect(20f, (float)num, 260f, 20f);
                GUI.Label(rect, this.LF("Game Speed: {0:F1}x", this.gameSpeed));
                num += 22;
                float prevGameSpeed = this.gameSpeed;
                float newGameSpeed = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.gameSpeed, 1f, 10f);
                if (Math.Abs(newGameSpeed - prevGameSpeed) > 0.0001f)
                {
                    this.SetGameSpeed(newGameSpeed);
                    this.QueueGameSpeedConfigSave();
                }

                num += 30;
                bool prevCustomCameraFOVEnabled = this.customCameraFOVEnabled;
                toggleHeight = this.GetSwitchToggleHeight(automationToggleWidth, "Custom Camera FOV", 25f);
                this.customCameraFOVEnabled = this.DrawWrappedSwitchToggle(new Rect(20f, (float)num, automationToggleWidth, toggleHeight), this.customCameraFOVEnabled, "Custom Camera FOV", 25f);
                if (this.customCameraFOVEnabled != prevCustomCameraFOVEnabled)
                {
                    if (this.customCameraFOVEnabled)
                    {
                        this.ApplyCameraFOV();
                    }
                    else
                    {
                        this.RestoreCameraFOV();
                    }
                    try { this.SaveKeybinds(false); } catch { }
                }
                num += Mathf.CeilToInt(toggleHeight + 8f);
                Rect rectFOV = new Rect(20f, (float)num, 260f, 20f);
                GUI.Label(rectFOV, this.LF("Camera FOV: {0:F0}", this.cameraFOV));
                num += 22;
                float newFOV = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.cameraFOV, 30f, 120f);
                if (newFOV != this.cameraFOV)
                {
                    this.cameraFOV = newFOV;
                    if (this.customCameraFOVEnabled)
                    {
                        this.ApplyCameraFOV();
                    }
                    try { this.SaveKeybinds(false); } catch { }
                }
                num += 40;
                
                bool flag2 = this.DrawDangerActionButton(new Rect(20f, (float)num, 260f, 35f), "DISABLE ALL");
                if (flag2)
                {
                    this.autoFarmEnabled = false;
                    this.bypassEnabled = false;
                    this.hideJumpButtonEnabled = false;
                    this.cachedJumpButtonGo = null;
                    this.bunnyHopEnabled = false;
                    this.ResetBunnyHopState();
                    this.analogMoveBridgeEnabled = false;
                    this.ReleaseMovementBridgeIfInjecting();
                    this.birdVacuumEnabled = false;
                    this.antiAfkEnabled = false;
                    this.StopAutoCookInternal("Disabled");
                    this.isAutoEating = false;
                    this.autoSellEnabled = false;
                    this.SetAuraFarmEnabled(false);
                    this.StopTreeFarm("Stopped");
                    this.cookingCleanupMode = false;
                    this.SetGameSpeed(1f);
                    this.customCameraFOVEnabled = false;
                    this.cameraFOV = 60f;
                    this.noclipEnabled = false;
                    HeartopiaComplete.OverridePlayerPosition = false;
                    this.ClearNoclipVehicleOverride();
                    this.noclipBoostMultiplier = 2f;
                    this.RestoreCameraFOV();
                }
                num += 45;
                return (float)num + 170f;
            }

            if (this.automationSubTab == 1)
            {
                string repairStatus = this.GetRepairStatusDisplay();
                string autoEatStatus = this.GetAutoEatStatusDisplay();
                string currentEnergyStatus = this.cachedFoodRepairEnergyStatusDisplay;
                string currentDurabilityStatus = this.GetCurrentToolDurabilityStatusDisplay();

                GUIStyle statLabelStyle = new GUIStyle(GUI.skin.label);
                statLabelStyle.fontSize = 11;
                statLabelStyle.fontStyle = FontStyle.Bold;
                statLabelStyle.alignment = TextAnchor.UpperLeft;
                statLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

                GUIStyle statValueStyle = new GUIStyle(GUI.skin.label);
                statValueStyle.fontSize = 12;
                statValueStyle.fontStyle = FontStyle.Bold;
                statValueStyle.alignment = TextAnchor.UpperLeft;
                statValueStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);
                statValueStyle.wordWrap = false;

                GUIStyle compactValueStyle = new GUIStyle(statValueStyle);
                compactValueStyle.fontSize = 11;

                float statusCardHeight = 126f;
                GUI.Box(new Rect(20f, (float)num, 320f, statusCardHeight), "", this.themePanelStyle ?? GUI.skin.box);
                float cardX = 32f;
                float topY = (float)num + 12f;
                float columnWidth = 132f;
                float columnGap = 20f;
                float rightColumnX = cardX + columnWidth + columnGap;

                GUI.Label(new Rect(cardX, topY, columnWidth, 18f), this.L("Repair Status"), statLabelStyle);
                GUI.Label(new Rect(cardX, topY + 18f, columnWidth, 20f), repairStatus, statValueStyle);
                GUI.Label(new Rect(rightColumnX, topY, columnWidth, 18f), this.L("Eat Status"), statLabelStyle);
                GUI.Label(new Rect(rightColumnX, topY + 18f, columnWidth, 20f), autoEatStatus, statValueStyle);

                float rowY = topY + 42f;
                this.DrawFoodRepairStatusRow(new Rect(cardX, rowY, 108f, 20f), new Rect(cardX + 112f, rowY, 164f, 20f), this.L("Current Energy"), currentEnergyStatus, statLabelStyle, statValueStyle);
                rowY += 24f;
                GUI.Label(new Rect(cardX, rowY, 132f, 18f), this.L("Tool Durability"), statLabelStyle);
                GUI.Label(new Rect(cardX + 112f, rowY, 176f, 18f), currentDurabilityStatus, compactValueStyle);
                num += 138;

                // Action buttons
                if (this.DrawPrimaryActionButton(new Rect(20f, (float)num, 120f, 35f), "Auto Repair"))
                {
                    if (!this.isRepairing && !this.isAutoEating)
                    {
                        this.AutoEatRepairLog("[AutoRepair] UI button requested StartRepair");
                        this.StartRepair();
                        this.AddMenuNotification(this.L("Auto Repair started"), new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.AddMenuNotification(this.L("Bag automation already running"), new Color(1f, 0.85f, 0.35f));
                    }
                }

                if (GUI.Button(new Rect(160f, (float)num, 125f, 35f), this.L("Eat Selected Food"), this.themePrimaryButtonStyle))
                {
                    if (!this.isRepairing && !this.isAutoEating)
                    {
                        this.StartAutoEat(false);
                        this.AddMenuNotification(this.LF("Auto Eat started ({0})", this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)), new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.AddMenuNotification(this.L("Auto Eat already running"), new Color(1f, 0.55f, 0.55f));
                    }
                }
                num += 45;
                GUI.Label(new Rect(20f, (float)num, 320f, 20f), this.L("Auto Eat will continue until energy is full."));
                num += 30;

                bool newRepairTeleportBackEnabled = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.repairTeleportBackEnabled, "Repair Teleport Backward");
                if (newRepairTeleportBackEnabled != this.repairTeleportBackEnabled)
                {
                    this.repairTeleportBackEnabled = newRepairTeleportBackEnabled;
                    this.SaveKeybinds(false);
                }
                num += 30;

                // Toast notification auto-trigger toggles
                bool newAutoRepairOnToast = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.autoRepairOnToastEnabled, "Auto Repair on Durability");
                if (newAutoRepairOnToast != this.autoRepairOnToastEnabled)
                {
                    this.autoRepairOnToastEnabled = newAutoRepairOnToast;
                    this.SaveKeybinds(false);
                }
                num += 30;

                bool newAutoEatAutoTrigger = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.autoEatAutoTriggerEnabled, "Auto Eat Energy Panel");
                if (newAutoEatAutoTrigger != this.autoEatAutoTriggerEnabled)
                {
                    this.autoEatAutoTriggerEnabled = newAutoEatAutoTrigger;
                    this.SaveKeybinds(false);
                }
                num += 30;

                GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Auto Eat Trigger: {0}% or lower", this.autoEatTriggerPercent));
                int previousAutoEatTriggerPercent = this.autoEatTriggerPercent;
                this.autoEatTriggerPercent = Mathf.Clamp(Mathf.RoundToInt(this.DrawAccentSlider(new Rect(20f, (float)num + 18f, 260f, 18f), this.autoEatTriggerPercent, 1f, 100f)), 1, 100);
                if (this.autoEatTriggerPercent != previousAutoEatTriggerPercent)
                {
                    this.SaveKeybinds(false);
                }
                num += 45;

                GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Auto Repair Trigger: {0}% or lower", this.autoRepairTriggerPercent));
                int previousAutoRepairTriggerPercent = this.autoRepairTriggerPercent;
                this.autoRepairTriggerPercent = Mathf.Clamp(Mathf.RoundToInt(this.DrawAccentSlider(new Rect(20f, (float)num + 18f, 260f, 18f), this.autoRepairTriggerPercent, 1f, 100f)), 1, 100);
                if (this.autoRepairTriggerPercent != previousAutoRepairTriggerPercent)
                {
                    this.SaveKeybinds(false);
                }
                num += 45;

                GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Repair Kit Uses: {0}", this.autoRepairUseTarget));
                int previousAutoRepairUseTarget = this.autoRepairUseTarget;
                this.autoRepairUseTarget = Mathf.Clamp(Mathf.RoundToInt(this.DrawAccentSlider(new Rect(20f, (float)num + 18f, 260f, 18f), this.autoRepairUseTarget, 1f, 3f)), 1, 3);
                if (this.autoRepairUseTarget != previousAutoRepairUseTarget)
                {
                    this.SaveKeybinds(false);
                }
                num += 45;

                // Configuration section
                GUIStyle bagFieldLabelStyle = new GUIStyle(GUI.skin.label);
                bagFieldLabelStyle.fontSize = 13;
                bagFieldLabelStyle.fontStyle = FontStyle.Bold;
                bagFieldLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

                GUIStyle dropdownValueStyle = new GUIStyle(GUI.skin.label);
                dropdownValueStyle.fontSize = 13;
                dropdownValueStyle.fontStyle = FontStyle.Bold;
                dropdownValueStyle.alignment = TextAnchor.MiddleLeft;
                dropdownValueStyle.normal.textColor = Color.white;

                GUIStyle wrappedDropdownValueStyle = new GUIStyle(dropdownValueStyle);
                wrappedDropdownValueStyle.fontSize = 11;
                wrappedDropdownValueStyle.alignment = TextAnchor.MiddleLeft;
                wrappedDropdownValueStyle.wordWrap = true;
                wrappedDropdownValueStyle.clipping = TextClipping.Clip;

                GUIStyle dropdownArrowStyle = new GUIStyle(GUI.skin.label);
                dropdownArrowStyle.fontSize = 12;
                dropdownArrowStyle.fontStyle = FontStyle.Bold;
                dropdownArrowStyle.alignment = TextAnchor.MiddleCenter;
                dropdownArrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

                GUIStyle dropdownOptionStyle = new GUIStyle(GUI.skin.label);
                dropdownOptionStyle.fontSize = 13;
                dropdownOptionStyle.fontStyle = FontStyle.Bold;
                dropdownOptionStyle.alignment = TextAnchor.MiddleCenter;
                dropdownOptionStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

                GUIStyle dropdownOptionActiveStyle = new GUIStyle(dropdownOptionStyle);
                dropdownOptionActiveStyle.normal.textColor = Color.white;

                GUIStyle wrappedDropdownOptionStyle = new GUIStyle(dropdownOptionStyle);
                wrappedDropdownOptionStyle.fontSize = 11;
                wrappedDropdownOptionStyle.alignment = TextAnchor.MiddleCenter;
                wrappedDropdownOptionStyle.wordWrap = true;
                wrappedDropdownOptionStyle.clipping = TextClipping.Clip;

                GUIStyle wrappedDropdownOptionActiveStyle = new GUIStyle(wrappedDropdownOptionStyle);
                wrappedDropdownOptionActiveStyle.normal.textColor = Color.white;

                float fieldLabelX = 20f;
                float fieldLabelWidth = 78f;
                float fieldX = 110f;
                float fieldWidth = 160f;
                float fieldHeight = 28f;
                float foodFieldHeight = 40f;
                float rowHeight = 36f;
                float repairPanelHeight = this.autoRepairDropdownOpen ? (this.autoRepairOptions.Length * 30f + 8f + 6f) : 0f;
                float foodOptionHeight = 40f;
                float foodDropdownPanelHeight = this.autoEatFoodOptions.Length * foodOptionHeight + 8f;
                float repairRowY = (float)num;
                float foodRowY = repairRowY + rowHeight + repairPanelHeight;

                Rect repairDropdownRect = new Rect(fieldX, repairRowY, fieldWidth, fieldHeight);
                Rect foodDropdownRect = new Rect(fieldX, foodRowY, fieldWidth, foodFieldHeight);

                GUI.Label(new Rect(fieldLabelX, repairRowY + 3f, fieldLabelWidth, 22f), this.L("Repair Kit"), bagFieldLabelStyle);
                GUI.Label(new Rect(fieldLabelX, foodRowY + 3f, fieldLabelWidth, 22f), this.L("Food Type"), bagFieldLabelStyle);

                GUI.Box(repairDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(repairDropdownRect, 1f);
                if (GUI.Button(repairDropdownRect, "", GUIStyle.none))
                {
                    this.autoRepairDropdownOpen = !this.autoRepairDropdownOpen;
                    if (this.autoRepairDropdownOpen)
                    {
                        this.autoEatFoodDropdownOpen = false;
                    }
                }
                GUI.Label(new Rect(repairDropdownRect.x + 12f, repairDropdownRect.y + 1f, repairDropdownRect.width - 34f, repairDropdownRect.height - 2f), this.GetAutoRepairOptionLabel(this.autoRepairType), dropdownValueStyle);
                GUI.Label(new Rect(repairDropdownRect.xMax - 24f, repairDropdownRect.y + 1f, 16f, repairDropdownRect.height - 2f), this.autoRepairDropdownOpen ? "^" : "v", dropdownArrowStyle);

                GUI.Box(foodDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(foodDropdownRect, 1f);
                if (GUI.Button(foodDropdownRect, "", GUIStyle.none))
                {
                    this.autoEatFoodDropdownOpen = !this.autoEatFoodDropdownOpen;
                    if (this.autoEatFoodDropdownOpen)
                    {
                        this.autoRepairDropdownOpen = false;
                    }
                }
                GUI.Label(new Rect(foodDropdownRect.xMax - 24f, foodDropdownRect.y + 1f, 16f, foodDropdownRect.height - 2f), this.autoEatFoodDropdownOpen ? "^" : "v", dropdownArrowStyle);
                GUI.Label(new Rect(foodDropdownRect.x + 12f, foodDropdownRect.y + 2f, foodDropdownRect.width - 34f, foodDropdownRect.height - 4f), this.GetAutoEatFoodOptionLabel(this.autoEatFoodType), wrappedDropdownValueStyle);

                if (this.autoRepairDropdownOpen)
                {
                    float panelHeight = this.autoRepairOptions.Length * 30f + 8f;
                    Rect panelRect = new Rect(repairDropdownRect.x, repairDropdownRect.yMax + 4f, repairDropdownRect.width, panelHeight);
                    GUI.Box(panelRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(panelRect, 1f);

                    for (int i = 0; i < this.autoRepairOptions.Length; i++)
                    {
                        Rect optionRect = new Rect(panelRect.x + 4f, panelRect.y + 4f + i * 30f, panelRect.width - 8f, 26f);
                        bool isSelected = i == this.autoRepairType;
                        GUI.Box(optionRect, "", isSelected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));
                        if (GUI.Button(optionRect, "", GUIStyle.none))
                        {
                            this.autoRepairType = i;
                            this.autoRepairDropdownOpen = false;
                            this.SaveKeybinds(false);
                        }
                        GUI.Label(optionRect, this.GetAutoRepairOptionLabel(i), isSelected ? dropdownOptionActiveStyle : dropdownOptionStyle);
                    }
                }

                if (this.autoEatFoodDropdownOpen)
                {
                    Rect panelRect = new Rect(foodDropdownRect.x, foodDropdownRect.yMax + 4f, foodDropdownRect.width, foodDropdownPanelHeight);
                    GUI.Box(panelRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(panelRect, 1f);

                    for (int i = 0; i < this.autoEatFoodOptions.Length; i++)
                    {
                        Rect optionRect = new Rect(panelRect.x + 4f, panelRect.y + 4f + i * foodOptionHeight, panelRect.width - 8f, 36f);
                        bool isSelected = i == this.autoEatFoodType;
                        GUI.Box(optionRect, "", isSelected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));
                        if (GUI.Button(optionRect, "", GUIStyle.none))
                        {
                            this.autoEatFoodType = i;
                            this.autoEatFoodDropdownOpen = false;
                            // If "Custom Food" selected, enter pick mode and open bag
                            if (i == this.autoEatFoodOptions.Length - 1)
                            {
                                this.customFoodPickMode = true;
                                this.lastClickedBagFood = "";
                                OpenInventory();
                                this.AddMenuNotification(this.L("Custom Food: Scanning your bag..."), new Color(1f, 0.8f, 0.4f));
                                // Clear any previous scan so it will scan when bag opens
                                this.scannedBagFoods = null;
                                this.customFoodScanRetryTime = Time.time + 0.5f; // Schedule retry scan
                            }
                            else
                            {
                                this.customFoodPickMode = false;
                            }
                            this.SaveKeybinds(false);
                        }
                        GUI.Label(optionRect, this.GetAutoEatFoodOptionLabel(i), isSelected ? wrappedDropdownOptionActiveStyle : wrappedDropdownOptionStyle);
                    }
                }

                // Custom Food Selection UI
                if (this.customFoodPickMode && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1)
                {
                    // Add extra spacing to clear any open dropdowns above
                    num += 80;

                    // Auto-scan bag if not yet scanned and bag is open
                    if (IsBagOpen() && this.scannedBagFoods == null)
                    {
                        this.scannedBagFoods = this.ScanBagForFoodItems();
                        if (this.scannedBagFoods.Length > 0)
                        {
                            this.AddMenuNotification(this.LF("Found {0} food item(s) in bag.", this.scannedBagFoods.Length), new Color(0.45f, 1f, 0.55f));
                        }
                    }
                    // Retry scan if scheduled
                    else if (this.customFoodScanRetryTime > 0f && Time.time >= this.customFoodScanRetryTime)
                    {
                        this.customFoodScanRetryTime = 0f;
                        if (IsBagOpen())
                        {
                            this.scannedBagFoods = this.ScanBagForFoodItems();
                        }
                    }

                    // Show scanned food items list
                    if (this.scannedBagFoods != null && this.scannedBagFoods.Length > 0)
                    {
                        GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
                        headerStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
                        headerStyle.fontStyle = FontStyle.Bold;
                        GUI.Label(new Rect(20f, num, 500f, 24f), "Select Food:", headerStyle);
                        num += 28;

                        // Keep the action buttons visible; the list itself scrolls when many foods are found.
                        float listHeight = Mathf.Min(this.scannedBagFoods.Length * 36f, 214f);
                        Rect scrollViewRect = new Rect(20f, num, 300f, listHeight);
                        Rect scrollContentRect = new Rect(0f, 0f, 280f, this.scannedBagFoods.Length * 36f);
                        this.customFoodScrollPos = GUI.BeginScrollView(scrollViewRect, this.customFoodScrollPos, scrollContentRect);

                        for (int i = 0; i < this.scannedBagFoods.Length; i++)
                        {
                            string foodSprite = this.scannedBagFoods[i];
                            string foodName = this.GetFoodDisplayName(foodSprite);

                            if (foodName.StartsWith("Food "))
                            {
                                foodName = foodName.Substring(5);
                            }
                            bool isSelected = this.autoEatCustomFoodName == foodSprite;

                            Rect itemRect = new Rect(0f, i * 36f, 280f, 34f);

                            // Highlight selected item with a colored box behind it
                            if (isSelected)
                            {
                                GUI.color = new Color(0.3f, 0.7f, 0.4f);
                                GUI.Box(new Rect(itemRect.x - 2f, itemRect.y - 2f, itemRect.width + 4f, itemRect.height + 4f), "");
                                GUI.color = Color.white;
                            }

                            // Draw icon on the left (28x28)
                            Rect iconRect = new Rect(4f, i * 36f + 3f, 28f, 28f);
                            if (this.scannedBagFoodTextures.TryGetValue(foodSprite, out Texture2D tex) && tex != null)
                            {
                                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
                            }

                            // Draw text to the right of the icon
                            Rect textRect = new Rect(38f, i * 36f + 4f, 240f, 30f);
                            GUIStyle textStyle = new GUIStyle(GUI.skin.label);
                            textStyle.alignment = TextAnchor.MiddleLeft;
                            textStyle.fontSize = 12;

                            if (GUI.Button(itemRect, GUIContent.none, GUIStyle.none))
                            {
                                this.autoEatCustomFoodName = foodSprite;
                                this.SaveKeybinds(false);
                                this.AddMenuNotification(this.LF("Custom food set to: {0}", foodName), new Color(0.45f, 1f, 0.55f));
                                this.customFoodPickMode = false;
                                this.scannedBagFoods = null;
                                this.scannedBagFoodTextures.Clear();
                                this.scannedBagFoodDisplayNames.Clear();
                                this.customFoodScanRetryTime = 0f;
                                if (IsBagOpen()) CloseInventory();
                            }
                            GUI.Label(textRect, foodName, textStyle);
                        }

                        GUI.EndScrollView();
                        num += (int)listHeight + 10;
                    }
                    else if (this.scannedBagFoods != null && this.scannedBagFoods.Length == 0)
                    {
                        GUIStyle noItemsStyle = new GUIStyle(GUI.skin.label);
                        noItemsStyle.normal.textColor = new Color(1f, 0.55f, 0.55f);
                        GUI.Label(new Rect(20f, num, 500f, 24f), "No food items found. Open your bag and try again.", noItemsStyle);
                        num += 30;
                    }
                    else
                    {
                        GUIStyle scanningStyle = new GUIStyle(GUI.skin.label);
                        scanningStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
                        GUI.Label(new Rect(20f, num, 500f, 24f), "Open your bag to scan for food items...", scanningStyle);
                        num += 30;
                    }

                    // Show current selection
                    if (!string.IsNullOrEmpty(this.autoEatCustomFoodName))
                    {
                        GUIStyle selectedStyle = new GUIStyle(GUI.skin.label);
                        selectedStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
                        string selectedName = this.GetFoodDisplayName(this.autoEatCustomFoodName);
                        if (selectedName.StartsWith("Food ")) selectedName = selectedName.Substring(5);
                        GUI.Label(new Rect(20f, num, 500f, 24f), "Selected: " + selectedName, selectedStyle);
                        num += 26;
                    }

                    // Rescan, Done and Cancel buttons
                    Rect btnY = new Rect(20f, num, 100f, 26f);
                    if (GUI.Button(btnY, "Rescan", this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.scannedBagFoods = null;
                        this.scannedBagFoodTextures.Clear();
                        this.scannedBagFoodDisplayNames.Clear();
                        this.customFoodScanRetryTime = Time.time + 0.25f;
                    }
                    btnY.x += 110f;
                    if (GUI.Button(btnY, "Done", this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.customFoodPickMode = false;
                        this.scannedBagFoods = null;
                        this.scannedBagFoodTextures.Clear();
                        this.scannedBagFoodDisplayNames.Clear();
                        this.customFoodScanRetryTime = 0f;
                        if (IsBagOpen()) CloseInventory();
                    }
                    btnY.x += 110f;
                    if (GUI.Button(btnY, "Cancel", this.themeDangerButtonStyle ?? GUI.skin.button))
                    {
                        this.customFoodPickMode = false;
                        this.scannedBagFoods = null;
                        this.scannedBagFoodTextures.Clear();
                        this.scannedBagFoodDisplayNames.Clear();
                        this.customFoodScanRetryTime = 0f;
                        if (IsBagOpen()) CloseInventory();
                    }
                    num += 35;
                }

                // Calculate proper return height based on what was rendered
                float returnHeight = num + 220f;
                
                // If dropdown is open, account for its height
                if (this.autoEatFoodDropdownOpen)
                {
                    returnHeight = Mathf.Max(returnHeight, foodDropdownRect.yMax + 4f + foodDropdownPanelHeight + 20f);
                }
                if (this.customFoodPickMode && this.autoEatFoodType == this.autoEatFoodOptions.Length - 1)
                {
                    returnHeight = Mathf.Max(returnHeight, num + 260f);
                }

                return returnHeight;
            }

            if (this.automationSubTab == 2)
            {
                float left = 20f;
                GUI.Label(new Rect(left, (float)num, 360f, 30f), this.L("AUTO SNOW SCULPTURE"));
                num += 40;
                bool prevAutoSnow = this.autoSnowEnabled;
                this.autoSnowEnabled = this.DrawSwitchToggle(new Rect(left, (float)num, 360f, 30f), this.autoSnowEnabled, "? Auto Snow Sculpture");
                if (this.autoSnowEnabled != prevAutoSnow)
                {
                    this.AddMenuNotification($"Auto Snow Sculpture {(this.autoSnowEnabled ? "Enabled" : "Disabled")}", this.autoSnowEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                num += 40;
                if (this.autoSnowEnabled)
                {
                    GUI.Box(new Rect(left, (float)num, 520f, 80f), "");
                    GUIStyle header = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
                    GUI.Label(new Rect(left + 10f, (float)num + 8f, 400f, 22f), $"Round: {this.snowApiRoundCount}/20  (total {this.snowClickCount})", header);
                    GUI.Label(new Rect(left + 10f, (float)num + 30f, 400f, 22f), $"API: {this.snowSculptureLastActionStatus}", header);
                    num += 100;
                }
                else
                {
                    num += 20;
                }

                if (GUI.Button(new Rect(left, (float)num, 280f, 32f), this.L("Move snowballs to backpack"), this.themePrimaryButtonStyle ?? GUI.skin.button))
                {
                    if (this.TryMoveSnowballsWarehouseToBackpack(out string moveStatus))
                    {
                        this.snowMoveSnowballsStatus = moveStatus;
                        this.AddMenuNotification(moveStatus, new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.snowMoveSnowballsStatus = moveStatus;
                        this.AddMenuNotification(moveStatus, new Color(1f, 0.55f, 0.55f));
                    }
                }

                num += 38;
                if (!string.IsNullOrEmpty(this.snowMoveSnowballsStatus))
                {
                    GUIStyle moveStatusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                    moveStatusStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
                    GUI.Label(new Rect(left, (float)num, 520f, 36f), this.snowMoveSnowballsStatus, moveStatusStyle);
                    num += 40;
                }

                return (float)num;
            }

            if (this.automationSubTab == 3)
            {
                float left = 20f;
                float width = 360f;
                float panelWidth = 580f;
                Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
                Color panelFill = new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
                Color panelLine = new Color(accent.r, accent.g, accent.b, 0.24f);
                GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 12 };
                sectionStyle.normal.textColor = accent;
                GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
                bodyStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
                GUIStyle mutedStyle = new GUIStyle(bodyStyle) { fontSize = 11, wordWrap = true };
                mutedStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
                GUIStyle summaryStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };

                string autoBuySummaryStore = this.autoBuyStoreOptions[this.autoBuySelectedStore];
                bool autoBuySummaryActive = this.autoBuyEnabled || this.autoBuyBirdEnabled || this.autoBuyGardenEnabled || this.autoBuyFishingEnabled;
                string autoBuySummaryState = "Idle";
                string autoBuySummaryDetail = "Select a store to enable auto buy.";
                if (this.autoBuySelectedStore == 1)
                {
                    autoBuySummaryState = this.autoBuyEnabled ? this.autoBuySubState.ToString() : "Idle";
                    autoBuySummaryDetail = "Ingredient cap: " + this.autoBuyMaxPerIngredient;
                }
                else if (this.autoBuySelectedStore == 2)
                {
                    autoBuySummaryState = this.autoBuyBirdEnabled ? this.autoBuyBirdSubState.ToString() : "Idle";
                    autoBuySummaryDetail = "Item cap: " + this.autoBuyBirdMaxPerItem;
                }
                else if (this.autoBuySelectedStore == 3)
                {
                    autoBuySummaryState = this.autoBuyGardenEnabled ? this.autoBuyGardenSubState.ToString() : "Idle";
                    autoBuySummaryDetail = "Item cap: " + this.autoBuyGardenMaxPerItem;
                }
                else if (this.autoBuySelectedStore == 4)
                {
                    autoBuySummaryState = this.autoBuyFishingEnabled ? this.autoBuyFishingSubState.ToString() : "Idle";
                    autoBuySummaryDetail = "Item cap: " + this.autoBuyFishingMaxPerItem;
                }
                summaryStyle.normal.textColor = autoBuySummaryActive
                    ? new Color(0.45f, 1f, 0.55f)
                    : (this.autoBuySelectedStore == 0 ? new Color(1f, 0.7f, 0.45f) : new Color(this.uiTextR, this.uiTextG, this.uiTextB));

                float autoBuyBodyHeight = 236f + (this.autoBuyStoreDropdownOpen ? (this.autoBuyStoreOptions.Length * 28f) + 12f : 0f);
                if (this.autoBuySelectedStore == 0)
                {
                    autoBuyBodyHeight += 18f;
                }
                else
                {
                    autoBuyBodyHeight += 92f;
                    if ((this.autoBuySelectedStore == 1 && this.autoBuyEnabled) ||
                        (this.autoBuySelectedStore == 2 && this.autoBuyBirdEnabled) ||
                        (this.autoBuySelectedStore == 3 && this.autoBuyGardenEnabled) ||
                        (this.autoBuySelectedStore == 4 && this.autoBuyFishingEnabled))
                    {
                        autoBuyBodyHeight += 92f;
                    }
                }
                Rect autoBuyBodyPanel = new Rect(left, (float)num, panelWidth, autoBuyBodyHeight);
                this.DrawExentriSectionPanel(autoBuyBodyPanel, accent, panelFill, panelLine);
                GUI.Label(new Rect(autoBuyBodyPanel.x + 14f, autoBuyBodyPanel.y + 12f, autoBuyBodyPanel.width - 28f, 18f), this.L("AUTO BUY"), sectionStyle);
                Rect autoBuyStoreBox = new Rect(autoBuyBodyPanel.x + 14f, autoBuyBodyPanel.y + 36f, 164f, 60f);
                this.DrawRoundedPanel(autoBuyStoreBox, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, Color.clear);
                GUI.Label(new Rect(autoBuyStoreBox.x + 12f, autoBuyStoreBox.y + 7f, autoBuyStoreBox.width - 24f, 18f), this.L("STORE"), sectionStyle);
                GUI.Label(new Rect(autoBuyStoreBox.x + 12f, autoBuyStoreBox.y + 28f, autoBuyStoreBox.width - 24f, 18f), autoBuySummaryStore, bodyStyle);
                Rect autoBuySummaryBox = new Rect(autoBuyStoreBox.xMax + 14f, autoBuyBodyPanel.y + 36f, autoBuyBodyPanel.xMax - (autoBuyStoreBox.xMax + 28f), 60f);
                this.DrawRoundedPanel(autoBuySummaryBox, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, new Color(accent.r, accent.g, accent.b, 0.35f));
                GUI.Label(new Rect(autoBuySummaryBox.x + 12f, autoBuySummaryBox.y + 7f, 92f, 18f), this.L("STATUS"), sectionStyle);
                GUI.Label(new Rect(autoBuySummaryBox.x + 12f, autoBuySummaryBox.y + 26f, autoBuySummaryBox.width - 24f, 18f), autoBuySummaryState, summaryStyle);
                GUI.Label(new Rect(autoBuySummaryBox.x + 12f, autoBuySummaryBox.y + 42f, autoBuySummaryBox.width - 24f, 16f), autoBuySummaryDetail, mutedStyle);
                float sectionLeft = autoBuyBodyPanel.x + 14f;
                float sectionWidth = autoBuyBodyPanel.width - 28f;
                float dropdownWidth = 280f;
                float detailWidth = autoBuyBodyPanel.width - 28f;
                this.DrawCardOutline(new Rect(autoBuyBodyPanel.x + 14f, autoBuyBodyPanel.y + 112f, autoBuyBodyPanel.width - 28f, 1f), 1f);
                num += 126;

                // Store Selection Dropdown
                GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.L("Select Store"), bodyStyle);
                num += 26;

                Rect dropdownRect = new Rect(sectionLeft, (float)num, dropdownWidth, 28f);
                GUI.Box(dropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(dropdownRect, 1f);
                if (GUI.Button(dropdownRect, "", GUIStyle.none))
                {
                    this.autoBuyStoreDropdownOpen = !this.autoBuyStoreDropdownOpen;
                    if (this.autoBuyStoreDropdownOpen)
                    {
                        this.forceOpenShopDropdownOpen = false;
                    }
                }

                GUIStyle valueStyle = new GUIStyle(GUI.skin.label);
                valueStyle.fontSize = 12;
                valueStyle.fontStyle = FontStyle.Bold;
                valueStyle.alignment = TextAnchor.MiddleLeft;
                valueStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

                GUIStyle arrowStyle = new GUIStyle(GUI.skin.label);
                arrowStyle.fontSize = 12;
                arrowStyle.fontStyle = FontStyle.Bold;
                arrowStyle.alignment = TextAnchor.MiddleCenter;
                arrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

                string currentStoreName = this.autoBuyStoreOptions[this.autoBuySelectedStore];
                GUI.Label(new Rect(dropdownRect.x + 10f, dropdownRect.y + 1f, dropdownRect.width - 32f, dropdownRect.height - 2f), currentStoreName, valueStyle);
                GUI.Label(new Rect(dropdownRect.xMax - 22f, dropdownRect.y + 1f, 14f, dropdownRect.height - 2f), this.autoBuyStoreDropdownOpen ? "^" : "v", arrowStyle);

                num += 42;

                // Dropdown options
                if (this.autoBuyStoreDropdownOpen)
                {
                    int optionCount = this.autoBuyStoreOptions.Length;
                    float dropdownHeight = (optionCount * 28f) + 8f;

                    // Border box behind options
                    Rect optionsBoxRect = new Rect(sectionLeft + 4f, (float)num, dropdownWidth - 8f, dropdownHeight);
                    GUI.Box(optionsBoxRect, "", this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(optionsBoxRect, 1f);

                    for (int i = 0; i < this.autoBuyStoreOptions.Length; i++)
                    {
                        string optionName = this.autoBuyStoreOptions[i];
                        bool isSelected = (i == this.autoBuySelectedStore);

                        Rect optionRect = new Rect(sectionLeft + 8f, (float)num, dropdownWidth - 16f, 26f);
                        bool clicked = GUI.Button(optionRect, "", GUIStyle.none);

                        // Highlight if selected
                        if (isSelected)
                        {
                            GUI.Box(optionRect, "", this.themePanelStyle ?? GUI.skin.box);
                        }

                        GUIStyle optionStyle = new GUIStyle(GUI.skin.label);
                        optionStyle.fontSize = 12;
                        optionStyle.alignment = TextAnchor.MiddleLeft;
                        optionStyle.normal.textColor = isSelected ? new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB) : new Color(this.uiTextR, this.uiTextG, this.uiTextB);

                        GUI.Label(new Rect(optionRect.x + 10f, optionRect.y + 1f, optionRect.width - 20f, optionRect.height - 2f), (isSelected ? "> " : "") + optionName, optionStyle);

                        if (clicked)
                        {
                            // Stop previous store if running
                            if (this.autoBuyEnabled && this.autoBuySelectedStore != 1)
                            {
                                this.StopAutoBuy("Store switched");
                            }
                            if (this.autoBuyBirdEnabled && this.autoBuySelectedStore != 2)
                            {
                                this.StopAutoBuyBird("Store switched");
                            }
                            if (this.autoBuyGardenEnabled && this.autoBuySelectedStore != 3)
                            {
                                this.StopAutoBuyGarden("Store switched");
                            }
                            this.autoBuySelectedStore = i;
                            this.autoBuyStoreDropdownOpen = false;
                        }

                        num += 28;
                    }
                    num += 12;
                }

                num += 18;

                // Show toggle and settings based on selected store
                if (this.autoBuySelectedStore == 1) // Cooking Store
                {
                    // Enable/Disable Toggle
                    bool prevAuto = this.autoBuyEnabled;
                    this.autoBuyEnabled = this.DrawSwitchToggle(new Rect(sectionLeft, (float)num, sectionWidth, 30f), this.autoBuyEnabled, "Auto Buy: Teleport -> Buy -> Return");
                    if (this.autoBuyEnabled != prevAuto)
                    {
                        if (this.autoBuyEnabled) { this.StartAutoBuy(); }
                        else { this.StopAutoBuy("Disabled"); }
                    }
                    num += 40;

                    // Status box when running
                    if (this.autoBuyEnabled)
                    {
                        Rect detailRect = new Rect(sectionLeft, (float)num, detailWidth, 72f);
                        this.DrawRoundedPanel(detailRect, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, Color.clear);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 10f, detailRect.width - 24f, 18f), this.LF("State: {0}", this.autoBuySubState), bodyStyle);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 32f, detailRect.width - 24f, 18f), this.LF("Current Ingredient: {0}", this.autoBuyCurrentIngredientIndex), mutedStyle);
                        num += 92;
                    }
                    else
                    {
                        num += 16;
                    }

                    // Cooking Store slider
                    GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.LF("Max per ingredient: {0}", this.autoBuyMaxPerIngredient), bodyStyle);
                    num += 26;
                    int prevMax = this.autoBuyMaxPerIngredient;
                    this.autoBuyMaxPerIngredient = Mathf.RoundToInt(this.UI_DrawAccentSlider(new Rect(sectionLeft, (float)num, sectionWidth, 20f), (float)this.autoBuyMaxPerIngredient, 1f, 50f));
                    if (this.autoBuyMaxPerIngredient != prevMax) { try { this.SaveKeybinds(false); } catch { } }
                    num += 28;
                }
                else if (this.autoBuySelectedStore == 2) // Birdwatching Store
                {
                    // Enable/Disable Toggle
                    bool prevBird = this.autoBuyBirdEnabled;
                    this.autoBuyBirdEnabled = this.DrawSwitchToggle(new Rect(sectionLeft, (float)num, sectionWidth, 30f), this.autoBuyBirdEnabled, "Auto Buy: Teleport -> Buy -> Return");
                    if (this.autoBuyBirdEnabled != prevBird)
                    {
                        if (this.autoBuyBirdEnabled) { this.StartAutoBuyBird(); }
                        else { this.StopAutoBuyBird("Disabled"); }
                    }
                    num += 40;

                    // Status box when running
                    if (this.autoBuyBirdEnabled)
                    {
                        Rect detailRect = new Rect(sectionLeft, (float)num, detailWidth, 72f);
                        this.DrawRoundedPanel(detailRect, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, Color.clear);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 10f, detailRect.width - 24f, 18f), this.LF("State: {0}", this.autoBuyBirdSubState), bodyStyle);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 32f, detailRect.width - 24f, 18f), this.LF("Current Item: {0}", this.autoBuyBirdCurrentItemIndex), mutedStyle);
                        num += 92;
                    }
                    else
                    {
                        num += 16;
                    }

                    // Birdwatching Store slider
                    GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.LF("Max per item: {0}", this.autoBuyBirdMaxPerItem), bodyStyle);
                    num += 26;
                    int prevBirdMax = this.autoBuyBirdMaxPerItem;
                    this.autoBuyBirdMaxPerItem = Mathf.RoundToInt(this.UI_DrawAccentSlider(new Rect(sectionLeft, (float)num, sectionWidth, 20f), (float)this.autoBuyBirdMaxPerItem, 1f, 10f));
                    if (this.autoBuyBirdMaxPerItem != prevBirdMax) { try { this.SaveKeybinds(false); } catch { } }
                    num += 28;
                }
                else if (this.autoBuySelectedStore == 3) // Garden Store
                {
                    // Enable/Disable Toggle
                    bool prevGarden = this.autoBuyGardenEnabled;
                    this.autoBuyGardenEnabled = this.DrawSwitchToggle(new Rect(sectionLeft, (float)num, sectionWidth, 30f), this.autoBuyGardenEnabled, "Auto Buy: Teleport -> Buy -> Return");
                    if (this.autoBuyGardenEnabled != prevGarden)
                    {
                        if (this.autoBuyGardenEnabled) { this.StartAutoBuyGarden(); }
                        else { this.StopAutoBuyGarden("Disabled"); }
                    }
                    num += 40;

                    // Status box when running
                    if (this.autoBuyGardenEnabled)
                    {
                        Rect detailRect = new Rect(sectionLeft, (float)num, detailWidth, 72f);
                        this.DrawRoundedPanel(detailRect, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, Color.clear);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 10f, detailRect.width - 24f, 18f), this.LF("State: {0}", this.autoBuyGardenSubState), bodyStyle);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 32f, detailRect.width - 24f, 18f), this.LF("Current Item: {0}", this.autoBuyGardenCurrentItemIndex), mutedStyle);
                        num += 92;
                    }
                    else
                    {
                        num += 16;
                    }

                    // Garden Store slider
                    GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.LF("Max per item: {0}", this.autoBuyGardenMaxPerItem), bodyStyle);
                    num += 26;
                    int prevGardenMax = this.autoBuyGardenMaxPerItem;
                    this.autoBuyGardenMaxPerItem = Mathf.RoundToInt(this.UI_DrawAccentSlider(new Rect(sectionLeft, (float)num, sectionWidth, 20f), (float)this.autoBuyGardenMaxPerItem, 1f, 25f));
                    if (this.autoBuyGardenMaxPerItem != prevGardenMax) { try { this.SaveKeybinds(false); } catch { } }
                    num += 28;
                }
                else if (this.autoBuySelectedStore == 4) // Fishing Store
                {
                    // Enable/Disable Toggle
                    bool prevFishing = this.autoBuyFishingEnabled;
                    this.autoBuyFishingEnabled = this.DrawSwitchToggle(new Rect(sectionLeft, (float)num, sectionWidth, 30f), this.autoBuyFishingEnabled, "Auto Buy: Teleport -> Buy -> Return");
                    if (this.autoBuyFishingEnabled != prevFishing)
                    {
                        if (this.autoBuyFishingEnabled) { this.StartAutoBuyFishing(); }
                        else { this.StopAutoBuyFishing("Disabled"); }
                    }
                    num += 40;

                    // Status box when running
                    if (this.autoBuyFishingEnabled)
                    {
                        Rect detailRect = new Rect(sectionLeft, (float)num, detailWidth, 72f);
                        this.DrawRoundedPanel(detailRect, 6f, new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), panelLine, 1f, Color.clear);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 10f, detailRect.width - 24f, 18f), this.LF("State: {0}", this.autoBuyFishingSubState), bodyStyle);
                        GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 32f, detailRect.width - 24f, 18f), this.LF("Current Item: {0}", this.autoBuyFishingCurrentItemIndex), mutedStyle);
                        num += 92;
                    }
                    else
                    {
                        num += 16;
                    }

                    // Fishing Store slider
                    GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.LF("Max per item: {0}", this.autoBuyFishingMaxPerItem), bodyStyle);
                    num += 26;
                    int prevFishingMax = this.autoBuyFishingMaxPerItem;
                    this.autoBuyFishingMaxPerItem = Mathf.RoundToInt(this.UI_DrawAccentSlider(new Rect(sectionLeft, (float)num, sectionWidth, 20f), (float)this.autoBuyFishingMaxPerItem, 1f, 50f));
                    if (this.autoBuyFishingMaxPerItem != prevFishingMax) { try { this.SaveKeybinds(false); } catch { } }
                    num += 28;
                }
                else // None selected
                {
                    GUI.Label(new Rect(sectionLeft, (float)num, sectionWidth, 20f), this.L("Select a store to enable auto buy"), mutedStyle);
                    num += 24;

                    // Stop any running auto buy if switched to None
                    if (this.autoBuyEnabled)
                    {
                        this.StopAutoBuy("Store deselected");
                    }
                    if (this.autoBuyBirdEnabled)
                    {
                        this.StopAutoBuyBird("Store deselected");
                    }
                    if (this.autoBuyGardenEnabled)
                    {
                        this.StopAutoBuyGarden("Store deselected");
                    }
                    if (this.autoBuyFishingEnabled)
                    {
                        this.StopAutoBuyFishing("Store deselected");
                    }
                }

                num += 36;
                float forcePanelHeight = 734f + (this.forceOpenShopDropdownOpen ? (this.forceOpenShopOptions.Length * 28f) + 12f : 0f);
                Rect forcePanel = new Rect(left, (float)num, panelWidth, forcePanelHeight);
                this.DrawExentriSectionPanel(forcePanel, accent, panelFill, panelLine);
                GUI.Label(new Rect(forcePanel.x + 14f, forcePanel.y + 12f, forcePanel.width - 28f, 18f), this.L("FORCE OPEN SHOP"), sectionStyle);
                float forceLeft = forcePanel.x + 14f;
                float forceWidth = forcePanel.width - 28f;
                num += 48;

                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 20f), this.L("Select Shop Panel"), bodyStyle);
                num += 26;

                Rect forceDropdownRect = new Rect(forceLeft, (float)num, dropdownWidth, 28f);
                GUI.Box(forceDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(forceDropdownRect, 1f);
                if (GUI.Button(forceDropdownRect, "", GUIStyle.none))
                {
                    this.forceOpenShopDropdownOpen = !this.forceOpenShopDropdownOpen;
                    if (this.forceOpenShopDropdownOpen)
                    {
                        this.autoBuyStoreDropdownOpen = false;
                    }
                }

                GUI.Label(new Rect(forceDropdownRect.x + 10f, forceDropdownRect.y + 1f, forceDropdownRect.width - 32f, forceDropdownRect.height - 2f), this.forceOpenShopOptions[this.forceOpenShopSelectedIndex], valueStyle);
                GUI.Label(new Rect(forceDropdownRect.xMax - 22f, forceDropdownRect.y + 1f, 14f, forceDropdownRect.height - 2f), this.forceOpenShopDropdownOpen ? "^" : "v", arrowStyle);
                num += 42;

                if (this.forceOpenShopDropdownOpen)
                {
                    int optionCount = this.forceOpenShopOptions.Length;
                    float dropdownHeight = (optionCount * 28f) + 8f;
                    Rect optionsBoxRect = new Rect(forceLeft + 4f, (float)num, dropdownWidth - 8f, dropdownHeight);
                    GUI.Box(optionsBoxRect, "", this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(optionsBoxRect, 1f);

                    for (int i = 0; i < this.forceOpenShopOptions.Length; i++)
                    {
                        bool isSelected = i == this.forceOpenShopSelectedIndex;
                        Rect optionRect = new Rect(forceLeft + 8f, (float)num, dropdownWidth - 16f, 26f);
                        bool clicked = GUI.Button(optionRect, "", GUIStyle.none);
                        if (isSelected)
                        {
                            GUI.Box(optionRect, "", this.themePanelStyle ?? GUI.skin.box);
                        }

                        GUIStyle optionStyle = new GUIStyle(GUI.skin.label);
                        optionStyle.fontSize = 12;
                        optionStyle.alignment = TextAnchor.MiddleLeft;
                        optionStyle.normal.textColor = isSelected
                            ? new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB)
                            : new Color(this.uiTextR, this.uiTextG, this.uiTextB);
                        GUI.Label(new Rect(optionRect.x + 10f, optionRect.y + 1f, optionRect.width - 20f, optionRect.height - 2f), (isSelected ? "> " : "") + this.forceOpenShopOptions[i], optionStyle);

                        if (clicked)
                        {
                            this.forceOpenShopSelectedIndex = i;
                            this.forceOpenShopDropdownOpen = false;
                            this.forceOpenShopStatus = i == 0
                                ? "No shop selected."
                                : ("Selected: " + this.forceOpenShopOptions[i]);
                        }

                        num += 28;
                    }

                    num += 12;
                }

                if (this.DrawPrimaryActionButton(new Rect(forceLeft, (float)num, 220f, 32f), "OPEN SELECTED SHOP"))
                {
                    if (this.TryOpenSelectedForceShop(out string openStatus))
                    {
                        this.forceOpenShopStatus = openStatus;
                        this.AddMenuNotification(openStatus, new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.forceOpenShopStatus = openStatus;
                        this.AddMenuNotification(openStatus, new Color(1f, 0.55f, 0.55f));
                    }
                }
                num += 40;

                bool shopBuyAllSupported = this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex, out string shopBuyAllBlockReason);
                GUI.enabled = !this.shopBuyAllRunning && shopBuyAllSupported;
                if (this.DrawPrimaryActionButton(new Rect(forceLeft, (float)num, 220f, 32f), this.L("BUY ALL (COIN)")))
                {
                    this.StartShopBuyAllGold();
                }
                GUI.enabled = true;
                num += 36;
                if (!shopBuyAllSupported && !string.IsNullOrEmpty(shopBuyAllBlockReason))
                {
                    GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 32f), shopBuyAllBlockReason, bodyStyle);
                    num += 20;
                }
                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 18f), this.shopBuyAllStatus, bodyStyle);
                num += 22;

                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 20f), "QuickBuyItem (store / slot / item)", bodyStyle);
                num += 22;
                this.shopQuickBuyStoreIdInput = GUI.TextField(new Rect(forceLeft, (float)num, 90f, 28f), this.shopQuickBuyStoreIdInput ?? string.Empty, 8);
                this.shopQuickBuySlotIdInput = GUI.TextField(new Rect(forceLeft + 98f, (float)num, 90f, 28f), this.shopQuickBuySlotIdInput ?? string.Empty, 8);
                this.shopQuickBuyItemIdInput = GUI.TextField(new Rect(forceLeft + 196f, (float)num, 100f, 28f), this.shopQuickBuyItemIdInput ?? string.Empty, 10);
                num += 34;
                if (this.DrawPrimaryActionButton(new Rect(forceLeft, (float)num, 220f, 32f), "OPEN BUY PANEL"))
                {
                    this.StartShopQuickBuyOpenPanel();
                }
                num += 36;
                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 36f), this.shopQuickBuyStatus ?? "Idle.", bodyStyle);
                num += 40;

                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 20f), this.L("Manual Store ID"), bodyStyle);
                num += 24;
                this.forceOpenShopManualStoreIdInput = GUI.TextField(
                    new Rect(forceLeft, (float)num, 120f, 28f),
                    this.forceOpenShopManualStoreIdInput ?? string.Empty,
                    8);
                if (this.DrawPrimaryActionButton(new Rect(forceLeft + 130f, (float)num, 120f, 28f), "OPEN ID"))
                {
                    if (this.TryOpenForceShopByManualStoreId(out string manualIdStatus))
                    {
                        this.forceOpenShopStatus = manualIdStatus;
                        this.AddMenuNotification(manualIdStatus, new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.forceOpenShopStatus = manualIdStatus;
                        this.AddMenuNotification(manualIdStatus, new Color(1f, 0.55f, 0.55f));
                    }
                }
                num += 38;

                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 20f), this.L("Manual Store Name"), bodyStyle);
                num += 24;
                this.forceOpenShopManualStoreNameInput = GUI.TextField(
                    new Rect(forceLeft, (float)num, 240f, 28f),
                    this.forceOpenShopManualStoreNameInput ?? string.Empty,
                    64);
                if (this.DrawPrimaryActionButton(new Rect(forceLeft + 250f, (float)num, 130f, 28f), "OPEN NAME"))
                {
                    if (this.TryOpenForceShopByManualStoreName(out string manualNameStatus))
                    {
                        this.forceOpenShopStatus = manualNameStatus;
                        this.AddMenuNotification(manualNameStatus, new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.forceOpenShopStatus = manualNameStatus;
                        this.AddMenuNotification(manualNameStatus, new Color(1f, 0.55f, 0.55f));
                    }
                }
                num += 42;

                GUI.Label(new Rect(forceLeft, (float)num, forceWidth, 40f), this.forceOpenShopStatus ?? "No shop selected.", mutedStyle);
                num += 52;

                return (float)num;
            }

            if (this.automationSubTab == 4)
            {
                return this.DrawAutoSellTab(startY);
            }

            if (this.automationSubTab == 5)
            {
                return this.DrawMassCookTab(startY);
            }

            if (this.automationSubTab == 6)
            {
                return this.DrawPuzzleTab(startY);
            }

            if (this.automationSubTab == 7)
            {
                return this.DrawPetPlayTab(startY);
            }

            return (float)num;
        }

        // Token: 0x06000026B RID: 38B - Bag / Warehouse (backpack <-> warehouse)
        private float DrawBulkSelectorTab(int startY)
        {
            int num = startY;
            float left = 20f;
            float panelWidth = 580f;

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            GUIStyle tinyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            tinyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);
            GUIStyle fieldLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            fieldLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle dropdownValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            dropdownValueStyle.normal.textColor = Color.white;
            GUIStyle dropdownArrowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            dropdownArrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            TransferItemEntry selectedEntry = this.GetSelectedTransferItemEntry();
            int sourceStorage = this.GetTransferScanStorageType();
            string destLabel = this.GetTransferTargetStorageType(sourceStorage) == 2 ? "Warehouse" : "Bag";

            GUI.Label(new Rect(left, (float)num, panelWidth, 24f), this.L("Bag / Warehouse"), titleStyle);
            num += 26;
            GUI.Label(new Rect(left, (float)num, panelWidth, 18f), "Transfer via BackPackSystem API (no bag UI). Direction: " + this.GetTransferScanSourceLabel() + " -> " + destLabel, tinyStyle);
            num += 22;

            Rect selectedCard = new Rect(left, (float)num, panelWidth, 78f);
            GUI.Box(selectedCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(selectedCard, 1f);
            Rect selectedIconRect = new Rect(selectedCard.x + 12f, selectedCard.y + 12f, 54f, 54f);
            GUI.Box(selectedIconRect, "", this.themeContentStyle ?? GUI.skin.box);
            if (selectedEntry != null && this.TryGetTransferItemTexture(selectedEntry, out Texture2D selectedTex) && selectedTex != null)
            {
                GUI.DrawTexture(new Rect(selectedIconRect.x + 5f, selectedIconRect.y + 5f, 44f, 44f), selectedTex, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.Label(selectedIconRect, "?", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold });
            }

            string selectedTitle = selectedEntry != null ? selectedEntry.DisplayName : "No stack selected";
            string selectedMeta = selectedEntry != null
                ? ("netId=" + selectedEntry.NetId + "  qty=" + Math.Max(1, selectedEntry.Count) + (selectedEntry.IsLocked ? "  LOCKED" : "") + (selectedEntry.StaticId > 0 ? ("  id=" + selectedEntry.StaticId) : ""))
                : (this.transferBatch.Count > 0 ? ("Batch: " + this.transferBatch.Count + " stack(s)") : "Scan and pick a stack");
            GUI.Label(new Rect(selectedCard.x + 78f, selectedCard.y + 10f, 360f, 22f), selectedTitle, fieldLabelStyle);
            GUI.Label(new Rect(selectedCard.x + 78f, selectedCard.y + 34f, 420f, 36f), selectedMeta, tinyStyle);
            num += 88;

            float sourceRowY = (float)num;
            Rect sourceDropdownRect = new Rect(left, sourceRowY, 120f, 28f);
            GUI.Box(sourceDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(sourceDropdownRect, 1f);
            if (GUI.Button(sourceDropdownRect, "", GUIStyle.none))
            {
                this.transferScanSourceDropdownOpen = !this.transferScanSourceDropdownOpen;
            }
            GUI.Label(new Rect(sourceDropdownRect.x + 12f, sourceDropdownRect.y + 1f, sourceDropdownRect.width - 34f, sourceDropdownRect.height - 2f), this.GetTransferScanSourceLabel(), dropdownValueStyle);
            GUI.Label(new Rect(sourceDropdownRect.xMax - 24f, sourceDropdownRect.y + 1f, 16f, sourceDropdownRect.height - 2f), this.transferScanSourceDropdownOpen ? "^" : "v", dropdownArrowStyle);

            float primaryStartX = sourceDropdownRect.xMax + 12f;
            float primaryButtonWidth = 130f;
            float actionRowHeight = 34f;
            GUI.enabled = !this.transferScanSourceDropdownOpen;
            if (this.DrawPrimaryActionButton(new Rect(primaryStartX, sourceRowY - 3f, primaryButtonWidth, actionRowHeight), "SCAN ITEMS"))
            {
                this.transferItems = this.ScanTransferItems();
                this.selectedTransferIndex = -1;
                this.transferBatch.Clear();
            }
            if (GUI.Button(new Rect(primaryStartX + primaryButtonWidth + 8f, sourceRowY - 3f, primaryButtonWidth, actionRowHeight), this.L("TRANSFER"), this.themePrimaryButtonStyle))
            {
                Dictionary<uint, int> pendingMap = this.BuildTransferItemMapForSend(out _);
                if (pendingMap != null && pendingMap.Count > TransferBatchMaxCount)
                {
                    this.ExecuteTransferItemsChunked();
                }
                else
                {
                    this.ExecuteTransferItems();
                }
            }
            GUI.enabled = true;

            float toggleRowY = sourceRowY + actionRowHeight + 6f;
            if (this.transferScanSourceDropdownOpen)
            {
                float panelHeight = this.transferScanSourceLabels.Length * 30f + 8f;
                toggleRowY = sourceDropdownRect.yMax + 4f + panelHeight + 8f;
            }

            float toggleGap = 12f;
            float toggleWidth = (panelWidth - toggleGap) * 0.5f;
            bool prevMulti = this.transferMultiSelectMode;
            this.transferMultiSelectMode = this.DrawSwitchToggle(new Rect(left, toggleRowY, toggleWidth, 28f), this.transferMultiSelectMode, "Multi");
            if (this.transferMultiSelectMode != prevMulti)
            {
                this.transferBatch.Clear();
            }
            this.transferSelectFullStack = this.DrawSwitchToggle(new Rect(left + toggleWidth + toggleGap, toggleRowY, toggleWidth, 28f), this.transferSelectFullStack, "Full stack");
            num = Mathf.CeilToInt(toggleRowY + 34f);

            if (this.transferBatch.Count > 0)
            {
                Rect batchBar = new Rect(left, (float)num, panelWidth, 34f);
                GUI.Box(batchBar, "", this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(batchBar, 1f);
                GUI.Label(new Rect(batchBar.x + 12f, batchBar.y + 8f, batchBar.width - 140f, 20f), "Batch selection: " + this.transferBatch.Count + " stack(s) ready to transfer", fieldLabelStyle);
                if (GUI.Button(new Rect(batchBar.xMax - 118f, batchBar.y + 5f, 106f, 24f), "Clear batch", this.themeSidebarButtonStyle ?? GUI.skin.button))
                {
                    this.transferBatch.Clear();
                    this.transferStatus = "Batch cleared";
                }
                num += 40;
            }

            Rect statusCard = new Rect(left, (float)num, panelWidth, 44f);
            GUI.Box(statusCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(statusCard, 1f);
            GUI.Label(new Rect(statusCard.x + 12f, statusCard.y + 12f, statusCard.width - 24f, 20f), this.transferStatus ?? "Idle", tinyStyle);
            num += 52;

            if (this.transferItems != null && this.transferItems.Count > 0)
            {
                GUI.Label(new Rect(left, (float)num, panelWidth, 22f), this.LF("{0} stacks ({1})", this.GetTransferScanSourceLabel(), this.transferItems.Count), fieldLabelStyle);
                num += 24;

                float cellW = 92f;
                float cellH = 100f;
                int columns = 6;
                int rows = Mathf.CeilToInt(this.transferItems.Count / (float)columns);
                float listHeight = Mathf.Min(rows * cellH, 276f);
                Rect scrollViewRect = new Rect(left, (float)num, panelWidth, listHeight);
                Rect scrollContentRect = new Rect(0f, 0f, panelWidth - 20f, rows * cellH);
                this.transferItemScrollPos = GUI.BeginScrollView(scrollViewRect, this.transferItemScrollPos, scrollContentRect);

                int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(this.transferItemScrollPos.y / cellH));
                int visibleRowCount = Mathf.CeilToInt(listHeight / cellH) + 1;
                int lastVisibleRow = Mathf.Min(rows - 1, firstVisibleRow + visibleRowCount);
                int firstVisibleIndex = Mathf.Clamp(firstVisibleRow * columns, 0, this.transferItems.Count);
                int lastVisibleIndexExclusive = Mathf.Clamp((lastVisibleRow + 1) * columns, 0, this.transferItems.Count);
                GUIStyle itemStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 9, wordWrap = true };
                itemStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
                itemStyle.clipping = TextClipping.Overflow;
                GUIStyle stackBadgeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 9, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                stackBadgeStyle.normal.textColor = Color.white;
                GUIStyle starBadgeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 9, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                starBadgeStyle.normal.textColor = new Color(1f, 0.86f, 0.36f);
                GUIStyle pickQtyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                pickQtyStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
                GUIStyle tileBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                tileBtnStyle.normal.textColor = Color.white;
                tileBtnStyle.hover.textColor = Color.white;
                tileBtnStyle.active.textColor = Color.white;
                GUIStyle initialsStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

                for (int i = firstVisibleIndex; i < lastVisibleIndexExclusive; i++)
                {
                    TransferItemEntry entry = this.transferItems[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    int col = i % columns;
                    int row = i / columns;
                    Rect cellRect = new Rect(col * cellW + 2f, row * cellH + 2f, cellW - 8f, cellH - 8f);
                    bool isSelected = this.selectedTransferIndex == i;
                    bool inBatch = this.transferBatch.ContainsKey(entry.NetId);
                    int pickQty = this.GetTransferTilePickQuantity(entry, i, out bool showPickQty);
                    GUI.Box(cellRect, "", isSelected || inBatch ? (this.themeTopTabActiveStyle ?? this.themePanelStyle ?? GUI.skin.box) : (this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box));
                    this.DrawCardOutline(cellRect, isSelected ? 2f : 1f);
                    if (entry.IsLocked)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.45f);
                    }

                    Rect iconRect = new Rect(cellRect.x + 21f, cellRect.y + 14f, 42f, 36f);
                    if (this.TryGetTransferItemTexture(entry, out Texture2D tex) && tex != null)
                    {
                        GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
                    }
                    else
                    {
                        GUI.Label(iconRect, this.GetAutoSellItemInitials(entry.DisplayName), initialsStyle);
                    }

                    if (entry.Count > 0)
                    {
                        GUI.Label(new Rect(cellRect.x + 4f, cellRect.y + 3f, 36f, 18f), "x" + entry.Count, stackBadgeStyle);
                    }

                    string starLabel = this.GetTransferTileStarLabel(entry);
                    if (!string.IsNullOrEmpty(starLabel))
                    {
                        GUI.Label(new Rect(cellRect.x + cellRect.width - 36f, cellRect.y + 3f, 32f, 18f), starLabel, starBadgeStyle);
                    }

                    GUI.Label(new Rect(cellRect.x + 3f, cellRect.y + 68f, cellRect.width - 6f, 28f), entry.DisplayName, itemStyle);

                    if (!entry.IsLocked)
                    {
                        if (isSelected)
                        {
                            float controlY = cellRect.y + 50f;
                            float controlH = 16f;
                            Rect minusRect = new Rect(cellRect.x + 5f, controlY, 18f, controlH);
                            Rect plusRect = new Rect(cellRect.x + cellRect.width - 23f, controlY, 18f, controlH);
                            Rect qtyRect = new Rect(cellRect.x + (cellRect.width - 22f) * 0.5f, controlY, 22f, controlH);

                            Rect iconSelectRect = new Rect(cellRect.x + 4f, cellRect.y + 10f, cellRect.width - 8f, 38f);
                            Rect nameSelectRect = new Rect(cellRect.x + 3f, cellRect.y + 66f, cellRect.width - 6f, 30f);
                            if (GUI.Button(iconSelectRect, GUIContent.none, GUIStyle.none)
                                || GUI.Button(nameSelectRect, GUIContent.none, GUIStyle.none))
                            {
                                this.SelectTransferTile(entry, i);
                            }

                            this.DrawTransferQtyStepButton(minusRect, "-", -1, entry, i, tileBtnStyle);
                            if (showPickQty)
                            {
                                GUI.Label(qtyRect, pickQty.ToString(), pickQtyStyle);
                            }
                            this.DrawTransferQtyStepButton(plusRect, "+", 1, entry, i, tileBtnStyle);
                        }
                        else
                        {
                            if (showPickQty)
                            {
                                GUI.Label(new Rect(cellRect.x + (cellRect.width - 22f) * 0.5f, cellRect.y + 50f, 22f, 16f), pickQty.ToString(), pickQtyStyle);
                            }

                            if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                            {
                                this.SelectTransferTile(entry, i);
                            }
                        }
                    }
                    GUI.color = Color.white;
                }
                GUI.EndScrollView();
                num += Mathf.CeilToInt(listHeight + 12f);
            }
            else if (this.transferItems != null)
            {
                GUI.Label(new Rect(left, (float)num, panelWidth, 24f), "No stacks in " + this.GetTransferScanSourceLabel().ToLowerInvariant() + ".");
                num += 28;
            }
            else
            {
                GUI.Label(new Rect(left, (float)num, panelWidth, 24f), "Press Scan Items to load inventory from BackPackSystem.");
                num += 28;
            }

            if (this.transferScanSourceDropdownOpen)
            {
                float panelHeight = this.transferScanSourceLabels.Length * 30f + 8f;
                Rect dropdownPanel = new Rect(left, sourceDropdownRect.yMax + 4f, 120f, panelHeight);
                GUI.Box(dropdownPanel, "", this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(dropdownPanel, 1f);
                for (int i = 0; i < this.transferScanSourceLabels.Length; i++)
                {
                    if (GUI.Button(new Rect(dropdownPanel.x + 4f, dropdownPanel.y + 4f + i * 30f, dropdownPanel.width - 8f, 26f), this.transferScanSourceLabels[i], this.themeSidebarButtonStyle ?? GUI.skin.button))
                    {
                        this.transferScanSource = i;
                        this.transferScanSourceDropdownOpen = false;
                        this.transferItems = null;
                        this.selectedTransferIndex = -1;
                        this.transferBatch.Clear();
                    }
                }
            }

            return (float)num + 12f;
        }

        private float DrawSelfTab(int startY)
        {
            int num = startY + 25;

            if (this.selfSubTab == 0)
            {
                bool newMouseLookEnabled = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.mouseLookEnabled, "Camera Toggle");
                if (newMouseLookEnabled != this.mouseLookEnabled)
                {
                    this.mouseLookEnabled = newMouseLookEnabled;
                    this.SaveKeybinds(false);
                    this.UpdateMouseLookState();
                    this.AddMenuNotification(
                        $"Camera Toggle {(this.mouseLookEnabled ? "Enabled" : "Disabled")}",
                        this.mouseLookEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                num += 30;

                if (this.mouseLookEnabled)
                {
                    bool newShowMouseLookCrosshair = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.showMouseLookCrosshair, "Show Crosshair");
                    if (newShowMouseLookCrosshair != this.showMouseLookCrosshair)
                    {
                        this.showMouseLookCrosshair = newShowMouseLookCrosshair;
                        this.SaveKeybinds(false);
                        this.AddMenuNotification(
                            $"Crosshair {(this.showMouseLookCrosshair ? "Enabled" : "Disabled")}",
                            this.showMouseLookCrosshair ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                    }
                    num += 30;
                }

                // Noclip Toggle
                this.noclipEnabled = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.noclipEnabled, "Noclip");
                if (this.noclipEnabled)
                {
                    this.InitializeNoclipOverridePosition();
                }
                else
                {
                    HeartopiaComplete.OverridePlayerPosition = false;
                    this.ClearNoclipVehicleOverride();
                }
                num += 30;

                if (this.noclipEnabled)
                {
                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Noclip Speed: {0:F1}", this.noclipSpeed));
                    num += 22;
                    float prevNoclipSpeed = this.noclipSpeed;
                    this.noclipSpeed = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.noclipSpeed, 5f, 50f);
                    if (Math.Abs(this.noclipSpeed - prevNoclipSpeed) > 0.0001f)
                    {
                        try { this.SaveKeybinds(false); } catch {}
                    }
                    num += 30;

                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("Noclip Boost: {0:F1}x", this.noclipBoostMultiplier));
                    num += 22;
                    float prevNoclipBoost = this.noclipBoostMultiplier;
                    this.noclipBoostMultiplier = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.noclipBoostMultiplier, 1f, 5f);
                    if (Math.Abs(this.noclipBoostMultiplier - prevNoclipBoost) > 0.0001f)
                    {
                        try { this.SaveKeybinds(false); } catch {}
                    }
                    num += 30;
                }

                bool newAntiAfk = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.antiAfkEnabled, "Anti AFK (Auto Click)");
                if (newAntiAfk != this.antiAfkEnabled)
                {
                    this.antiAfkEnabled = newAntiAfk;
                    this.lastAntiAfkPulseAt = Time.unscaledTime;
                    this.SaveKeybinds(false);
                    this.AddMenuNotification($"Anti AFK {(this.antiAfkEnabled ? "Enabled" : "Disabled")}", this.antiAfkEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                num += 26;

                if (this.antiAfkEnabled)
                {
                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.LF("AFK Click Interval: {0:F0}s", this.antiAfkInterval));
                    num += 22;
                    float newAfkInterval = this.DrawAccentSlider(new Rect(20f, (float)num, 260f, 20f), this.antiAfkInterval, 5f, 9f);
                    if (Math.Abs(newAfkInterval - this.antiAfkInterval) > 0.01f)
                    {
                        this.antiAfkInterval = newAfkInterval;
                        this.SaveKeybinds(false);
                    }
                    num += 30;
                }

                if (this.noclipEnabled)
                {
                    GUI.Label(new Rect(20f, (float)num, 260f, 120f), this.L("Noclip: WASD + Space/Ctrl\nShift = Speed Boost"));
                    return (float)num + 160f;
                }

                return (float)num + 24f;
            }

            // Building sub-tab: Bypass overlap toggle
            if (this.selfSubTab == 1)
            {
                GUI.Label(new Rect(20f, (float)num, 260f, 20f), this.L("Building - Bypass Overlap"));
                num += 26;

                this.bypassOverlapEnabled = this.DrawSwitchToggle(new Rect(20f, (float)num, 260f, 25f), this.bypassOverlapEnabled, "Bypass Overlap");
                // reflect into static flag used by prefix
                HeartopiaComplete.bypassOverlapEnabledStatic = this.bypassOverlapEnabled;
                if (this.bypassOverlapEnabled && !this.bypassOverlapPatched)
                {
                    this.EnsureBypassPatched();
                }
                num += 36;

                GUI.Label(new Rect(20f, (float)num, 260f, 120f), this.L("Credits: evermoreee12 for Bypass Overlap"));
                return (float)num + 50f;
            }

            return (float)num + 50f;
        }

    }
}
