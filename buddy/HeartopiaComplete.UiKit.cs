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
        private void PopulateUiThemeConfig(UiThemeConfigData data)
        {
            data.uiAccentR = this.uiAccentR;
            data.uiAccentG = this.uiAccentG;
            data.uiAccentB = this.uiAccentB;
            data.uiTextR = this.uiTextR;
            data.uiTextG = this.uiTextG;
            data.uiTextB = this.uiTextB;
            data.uiMainTabTextR = this.uiMainTabTextR;
            data.uiMainTabTextG = this.uiMainTabTextG;
            data.uiMainTabTextB = this.uiMainTabTextB;
            data.uiSubTabTextR = this.uiSubTabTextR;
            data.uiSubTabTextG = this.uiSubTabTextG;
            data.uiSubTabTextB = this.uiSubTabTextB;
            data.uiWindowR = this.uiWindowR;
            data.uiWindowG = this.uiWindowG;
            data.uiWindowB = this.uiWindowB;
            data.uiPanelR = this.uiPanelR;
            data.uiPanelG = this.uiPanelG;
            data.uiPanelB = this.uiPanelB;
            data.uiContentR = this.uiContentR;
            data.uiContentG = this.uiContentG;
            data.uiContentB = this.uiContentB;
            data.uiWindowAlpha = this.uiWindowAlpha;
            data.uiPanelAlpha = this.uiPanelAlpha;
            data.uiContentAlpha = this.uiContentAlpha;
            data.uiScale = this.uiScale;
        }

        private void ApplyUiThemeConfig(UiThemeConfigData data)
        {
            if (data == null) return;
            this.uiAccentR = Mathf.Clamp01(data.uiAccentR);
            this.uiAccentG = Mathf.Clamp01(data.uiAccentG);
            this.uiAccentB = Mathf.Clamp01(data.uiAccentB);
            this.uiTextR = Mathf.Clamp01(data.uiTextR);
            this.uiTextG = Mathf.Clamp01(data.uiTextG);
            this.uiTextB = Mathf.Clamp01(data.uiTextB);
            this.uiMainTabTextR = Mathf.Clamp01(data.uiMainTabTextR);
            this.uiMainTabTextG = Mathf.Clamp01(data.uiMainTabTextG);
            this.uiMainTabTextB = Mathf.Clamp01(data.uiMainTabTextB);
            this.uiSubTabTextR = Mathf.Clamp01(data.uiSubTabTextR);
            this.uiSubTabTextG = Mathf.Clamp01(data.uiSubTabTextG);
            this.uiSubTabTextB = Mathf.Clamp01(data.uiSubTabTextB);
            this.uiWindowR = Mathf.Clamp01(data.uiWindowR);
            this.uiWindowG = Mathf.Clamp01(data.uiWindowG);
            this.uiWindowB = Mathf.Clamp01(data.uiWindowB);
            this.uiPanelR = Mathf.Clamp01(data.uiPanelR);
            this.uiPanelG = Mathf.Clamp01(data.uiPanelG);
            this.uiPanelB = Mathf.Clamp01(data.uiPanelB);
            this.uiContentR = Mathf.Clamp01(data.uiContentR);
            this.uiContentG = Mathf.Clamp01(data.uiContentG);
            this.uiContentB = Mathf.Clamp01(data.uiContentB);
            this.uiWindowAlpha = Mathf.Clamp(data.uiWindowAlpha, 0.15f, 1f);
            this.uiPanelAlpha = Mathf.Clamp(data.uiPanelAlpha, 0.15f, 1f);
            this.uiContentAlpha = Mathf.Clamp(data.uiContentAlpha, 0.15f, 1f);
            this.uiScale = this.NormalizeUiScale(data.uiScale > 0f ? data.uiScale : 1f);
        }

        private void InvalidateThemeCache()
        {
            this.themeInitialized = false;
            this.themeWindowStyle = null;
            this.themePanelStyle = null;
            this.themeContentStyle = null;
            this.themeSidebarButtonStyle = null;
            this.themeSidebarButtonActiveStyle = null;
            this.themePrimaryButtonStyle = null;
            this.themeDangerButtonStyle = null;
            this.themeTopTabStyle = null;
            this.themeTopTabActiveStyle = null;
            this.uiCircleTexture = null;
            this.uiHueTexture = null;
            this.uiSvTexture = null;
            this.uiPickerHueCached = -1f;
            if (this.themeTextures.Count > 0)
            {
                foreach (Texture2D texture in this.themeTextures)
                {
                    if (texture != null)
                    {
                        Object.Destroy(texture);
                    }
                }
                this.themeTextures.Clear();
            }
        }

        private string GetUiThemePath()
        {
            return HelperPaths.GetFile("ui_theme.json");
        }

        private void SaveUiTheme()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                ModLogger.Msg("UI Theme Saved.");
                this.AddMenuNotification("UI theme saved", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Saving UI Theme: " + ex.Message);
                this.AddMenuNotification("Failed to save UI theme", new Color(1f, 0.4f, 0.4f));
            }
        }

        private void LoadUiTheme()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    this.ApplyUiThemeConfig(config.UiTheme);
                    this.InvalidateThemeCache();
                    this.uiThemeHexInput = this.ColorToHex(this.GetUiThemeColorTargetValue(this.uiThemeColorTarget));
                    ModLogger.Msg("UI Theme Loaded.");
                    this.AddMenuNotification("UI theme loaded", new Color(0.55f, 0.88f, 1f));
                    return;
                }
                string path = this.GetUiThemePath();
                if (!File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (line.Contains("uiAccentR")) this.uiAccentR = GetJsonFloat(line, "\"uiAccentR\":");
                    else if (line.Contains("uiAccentG")) this.uiAccentG = GetJsonFloat(line, "\"uiAccentG\":");
                    else if (line.Contains("uiAccentB")) this.uiAccentB = GetJsonFloat(line, "\"uiAccentB\":");
                    else if (line.Contains("uiTextR")) this.uiTextR = GetJsonFloat(line, "\"uiTextR\":");
                    else if (line.Contains("uiTextG")) this.uiTextG = GetJsonFloat(line, "\"uiTextG\":");
                    else if (line.Contains("uiTextB")) this.uiTextB = GetJsonFloat(line, "\"uiTextB\":");
                    else if (line.Contains("uiMainTabTextR")) this.uiMainTabTextR = GetJsonFloat(line, "\"uiMainTabTextR\":");
                    else if (line.Contains("uiMainTabTextG")) this.uiMainTabTextG = GetJsonFloat(line, "\"uiMainTabTextG\":");
                    else if (line.Contains("uiMainTabTextB")) this.uiMainTabTextB = GetJsonFloat(line, "\"uiMainTabTextB\":");
                    else if (line.Contains("uiSubTabTextR")) this.uiSubTabTextR = GetJsonFloat(line, "\"uiSubTabTextR\":");
                    else if (line.Contains("uiSubTabTextG")) this.uiSubTabTextG = GetJsonFloat(line, "\"uiSubTabTextG\":");
                    else if (line.Contains("uiSubTabTextB")) this.uiSubTabTextB = GetJsonFloat(line, "\"uiSubTabTextB\":");
                    else if (line.Contains("uiWindowR")) this.uiWindowR = GetJsonFloat(line, "\"uiWindowR\":");
                    else if (line.Contains("uiWindowG")) this.uiWindowG = GetJsonFloat(line, "\"uiWindowG\":");
                    else if (line.Contains("uiWindowB")) this.uiWindowB = GetJsonFloat(line, "\"uiWindowB\":");
                    else if (line.Contains("uiPanelR")) this.uiPanelR = GetJsonFloat(line, "\"uiPanelR\":");
                    else if (line.Contains("uiPanelG")) this.uiPanelG = GetJsonFloat(line, "\"uiPanelG\":");
                    else if (line.Contains("uiPanelB")) this.uiPanelB = GetJsonFloat(line, "\"uiPanelB\":");
                    else if (line.Contains("uiContentR")) this.uiContentR = GetJsonFloat(line, "\"uiContentR\":");
                    else if (line.Contains("uiContentG")) this.uiContentG = GetJsonFloat(line, "\"uiContentG\":");
                    else if (line.Contains("uiContentB")) this.uiContentB = GetJsonFloat(line, "\"uiContentB\":");
                    else if (line.Contains("uiWindowAlpha")) this.uiWindowAlpha = GetJsonFloat(line, "\"uiWindowAlpha\":");
                    else if (line.Contains("uiPanelAlpha")) this.uiPanelAlpha = GetJsonFloat(line, "\"uiPanelAlpha\":");
                    else if (line.Contains("uiContentAlpha")) this.uiContentAlpha = GetJsonFloat(line, "\"uiContentAlpha\":");
                    else if (line.Contains("uiScale")) this.uiScale = GetJsonFloat(line, "\"uiScale\":");
                }

                this.uiAccentR = Mathf.Clamp01(this.uiAccentR);
                this.uiAccentG = Mathf.Clamp01(this.uiAccentG);
                this.uiAccentB = Mathf.Clamp01(this.uiAccentB);
                this.uiTextR = Mathf.Clamp01(this.uiTextR);
                this.uiTextG = Mathf.Clamp01(this.uiTextG);
                this.uiTextB = Mathf.Clamp01(this.uiTextB);
                this.uiMainTabTextR = Mathf.Clamp01(this.uiMainTabTextR);
                this.uiMainTabTextG = Mathf.Clamp01(this.uiMainTabTextG);
                this.uiMainTabTextB = Mathf.Clamp01(this.uiMainTabTextB);
                this.uiSubTabTextR = Mathf.Clamp01(this.uiSubTabTextR);
                this.uiSubTabTextG = Mathf.Clamp01(this.uiSubTabTextG);
                this.uiSubTabTextB = Mathf.Clamp01(this.uiSubTabTextB);
                this.uiWindowR = Mathf.Clamp01(this.uiWindowR);
                this.uiWindowG = Mathf.Clamp01(this.uiWindowG);
                this.uiWindowB = Mathf.Clamp01(this.uiWindowB);
                this.uiPanelR = Mathf.Clamp01(this.uiPanelR);
                this.uiPanelG = Mathf.Clamp01(this.uiPanelG);
                this.uiPanelB = Mathf.Clamp01(this.uiPanelB);
                this.uiContentR = Mathf.Clamp01(this.uiContentR);
                this.uiContentG = Mathf.Clamp01(this.uiContentG);
                this.uiContentB = Mathf.Clamp01(this.uiContentB);
                this.uiWindowAlpha = Mathf.Clamp(this.uiWindowAlpha, 0.15f, 1f);
                this.uiPanelAlpha = Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f);
                this.uiContentAlpha = Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f);
                this.uiScale = this.NormalizeUiScale(this.uiScale > 0f ? this.uiScale : 1f);

                this.InvalidateThemeCache();
                this.uiThemeHexInput = this.ColorToHex(this.GetUiThemeColorTargetValue(this.uiThemeColorTarget));
                ModLogger.Msg("UI Theme Loaded.");
                this.AddMenuNotification("UI theme loaded", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Loading UI Theme: " + ex.Message);
                this.AddMenuNotification("Failed to load UI theme", new Color(1f, 0.4f, 0.4f));
            }
        }

        private void DrawWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0f, 0f, this.windowRect.width, 30f));

            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color basePanel = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, this.uiPanelAlpha);
            Color baseCard = new Color(this.uiContentR, this.uiContentG, this.uiContentB, this.uiContentAlpha);
            Color lineSoft = new Color(1f, 1f, 1f, Mathf.Clamp(0.025f + (this.uiPanelAlpha * 0.02f), 0.03f, 0.06f));

            Rect chromeRect = new Rect(0f, 0f, this.windowRect.width, this.windowRect.height);
            Rect sidebarRect = new Rect(chromeRect.x, chromeRect.y, 190f, chromeRect.height);
            Rect mainRect = new Rect(sidebarRect.xMax + 10f, chromeRect.y, chromeRect.width - sidebarRect.width - 10f, chromeRect.height);
            Rect sidebarTopRect = new Rect(sidebarRect.x, sidebarRect.y, sidebarRect.width, 48f);
            Rect sidebarFooterRect = new Rect(sidebarRect.x, sidebarRect.yMax - 48f, sidebarRect.width, 48f);
            Rect navListRect = new Rect(sidebarRect.x + 16f, sidebarTopRect.yMax + 28f, sidebarRect.width - 32f, sidebarRect.height - 156f);
            Rect mainHeaderRect = new Rect(mainRect.x, mainRect.y, mainRect.width, 48f);
            Rect subTabRect = new Rect(mainRect.x + 22f, mainHeaderRect.yMax + 18f, mainRect.width - 44f, 28f);

            this.DrawRoundedPanel(sidebarRect, 10f, basePanel, Color.clear, 0f, Color.clear);
            this.DrawRoundedPanel(mainRect, 10f, basePanel, Color.clear, 0f, Color.clear);
            this.DrawRoundedPanel(sidebarTopRect, 10f, new Color(baseCard.r, baseCard.g, baseCard.b, 0.92f), Color.clear, 0f, Color.clear);
            this.DrawRoundedPanel(sidebarFooterRect, 10f, new Color(baseCard.r, baseCard.g, baseCard.b, 0.92f), Color.clear, 0f, Color.clear);
            this.DrawRoundedPanel(mainHeaderRect, 10f, new Color(baseCard.r, baseCard.g, baseCard.b, 0.92f), Color.clear, 0f, Color.clear);

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f);
            GUI.DrawTexture(new Rect(sidebarRect.x, sidebarTopRect.yMax - 1f, sidebarRect.width, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(sidebarRect.x, sidebarFooterRect.y, sidebarRect.width, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(mainRect.x, mainHeaderRect.yMax - 1f, mainRect.width, 1.5f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle logoStyle = new GUIStyle(GUI.skin.label);
            logoStyle.fontSize = 16;
            logoStyle.fontStyle = FontStyle.Bold;
            logoStyle.alignment = TextAnchor.MiddleCenter;
            logoStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);
            GUI.Label(sidebarTopRect, "Heartopia Helper", logoStyle);

            GUIStyle footerStyle = new GUIStyle(GUI.skin.label);
            footerStyle.fontSize = 10;
            footerStyle.fontStyle = FontStyle.Bold;
            footerStyle.alignment = TextAnchor.MiddleCenter;
            footerStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.85f);
            GUI.Label(sidebarFooterRect, "Rayyy", footerStyle);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 21;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleLeft;
            titleStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(new Rect(mainHeaderRect.x + 18f, mainHeaderRect.y + 2f, mainHeaderRect.width - 200f, mainHeaderRect.height - 4f), this.GetSelectedTabHeader(), titleStyle);

            Rect[] sidebarButtonRects = new Rect[]
            {
                new Rect(navListRect.x, navListRect.y, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 56f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 112f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 168f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 224f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 280f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 336f, navListRect.width, 40f),
                new Rect(navListRect.x, navListRect.y + 392f, navListRect.width, 40f)
            };
            this.DrawSidebarTabButton(sidebarButtonRects[0], "Self", 0);
            this.DrawSidebarTabButton(sidebarButtonRects[1], "Resource Gathering", 2);
            this.DrawSidebarTabButton(sidebarButtonRects[2], "Features", 3);
            this.DrawSidebarTabButton(sidebarButtonRects[3], "New Features", 8);
            this.DrawSidebarTabButton(sidebarButtonRects[4], "Radar", 4);
            this.DrawSidebarTabButton(sidebarButtonRects[5], "Teleport", 5);
            this.DrawSidebarTabButton(sidebarButtonRects[6], "Bag / Warehouse", 6);
            this.DrawSidebarTabButton(sidebarButtonRects[7], "Settings", 7);

            var subTabs = this.GetActiveTopSubTabs();
            if (subTabs.Count > 0)
            {
                float cursorX = subTabRect.x;
                for (int i = 0; i < subTabs.Count; i++)
                {
                    var tab = subTabs[i];
                    float width = Mathf.Max(82f, Mathf.Min(126f, 28f + (tab.label.Length * 7.7f)));
                    Rect tRect = new Rect(cursorX, subTabRect.y, width, subTabRect.height);
                    bool active = tab.isActive();

                    GUIStyle tabStyle = new GUIStyle(GUI.skin.label);
                    tabStyle.fontSize = 11;
                    tabStyle.fontStyle = FontStyle.Bold;
                    tabStyle.alignment = TextAnchor.MiddleCenter;
                    tabStyle.normal.textColor = active ? accent : new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB, 0.72f);

                    if (GUI.Button(tRect, "", GUIStyle.none))
                    {
                        tab.setActive();
                    }

                    GUI.Label(tRect, this.L(tab.label), tabStyle);
                    if (active)
                    {
                        GUI.color = accent;
                        GUI.DrawTexture(new Rect(tRect.x + 10f, tRect.yMax - 2f, tRect.width - 20f, 3f), Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }

                    cursorX += width + 10f;
                }
            }

            GUI.color = new Color(1f, 1f, 1f, 0.03f);
            GUI.DrawTexture(new Rect(subTabRect.x, subTabRect.yMax + 8f, mainRect.width - 44f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label);
            sectionStyle.fontSize = 13;
            sectionStyle.fontStyle = FontStyle.Bold;
            sectionStyle.normal.textColor = accent;

            float bodyTop = subTabRect.yMax + 32f;
            float bodyHeight = mainRect.yMax - bodyTop - 20f;
            float panelTopInset = 10f;
            Rect leftContentArea = new Rect(mainRect.x, bodyTop + panelTopInset, mainRect.width - 290f, bodyHeight - panelTopInset);
            Rect rightTopCard = new Rect(leftContentArea.xMax, bodyTop + panelTopInset, mainRect.xMax - leftContentArea.xMax, bodyHeight - panelTopInset);
            Color leftContentFill = new Color(
                this.uiContentR,
                this.uiContentG,
                this.uiContentB,
                Mathf.Clamp(this.uiPanelAlpha * 0.92f, 0.15f, 1f));
            Color statusFill = new Color(
                this.uiContentR,
                this.uiContentG,
                this.uiContentB,
                Mathf.Clamp(this.uiPanelAlpha * 0.96f, 0.15f, 1f));

            this.DrawRoundedPanel(leftContentArea, 10f, leftContentFill, Color.clear, 0f, Color.clear);
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f);
            GUI.DrawTexture(new Rect(leftContentArea.x, leftContentArea.y, leftContentArea.width, 1.5f), Texture2D.whiteTexture);
            GUI.color = lineSoft;
            GUI.DrawTexture(new Rect(leftContentArea.x, leftContentArea.y + 1f, 1f, leftContentArea.height - 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(leftContentArea.xMax - 1f, leftContentArea.y + 1f, 1f, leftContentArea.height - 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(leftContentArea.x, leftContentArea.yMax - 1f, leftContentArea.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            this.DrawExentriSectionPanel(rightTopCard, accent, statusFill, lineSoft);

            GUI.Label(new Rect(rightTopCard.x + 14f, rightTopCard.y + 12f, rightTopCard.width - 28f, 20f), "STATUS", sectionStyle);
            GUIStyle statusSub = new GUIStyle(GUI.skin.label);
            statusSub.fontSize = 11;
            statusSub.fontStyle = FontStyle.Italic;
            statusSub.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.72f);
            GUI.Label(new Rect(rightTopCard.x + 14f, rightTopCard.y + 34f, rightTopCard.width - 28f, 18f), "Live features", statusSub);

            Rect tabDrawRect = new Rect(leftContentArea.x + 28f, leftContentArea.y + 12f, leftContentArea.width - 36f, leftContentArea.height - 18f);
            GUI.BeginGroup(tabDrawRect);
            try
            {
                float estimatedHeight = this.GetSelectedTabEstimatedHeight() + 30f;
                float contentHeight = Mathf.Max(estimatedHeight, this.tabDrawContentHeight + 20f);
                float contentWidth = tabDrawRect.width - 18f;
                this.tabScrollPos = GUI.BeginScrollView(
                    new Rect(0f, 0f, tabDrawRect.width, tabDrawRect.height),
                    this.tabScrollPos,
                    new Rect(0f, 0f, contentWidth, contentHeight),
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar);
                float calculatedHeight = 500f;
                int contentY = 10;
                if (this.selectedTab == 0) calculatedHeight = this.DrawSelfTab(contentY);
                else if (this.selectedTab == 2) calculatedHeight = this.DrawAutoFarmTab(contentY);
                else if (this.selectedTab == 3) calculatedHeight = this.DrawAutomationTab(contentY);
                else if (this.selectedTab == 8) calculatedHeight = this.DrawNewFeaturesTab(contentY);
                else if (this.selectedTab == 4) calculatedHeight = this.DrawRadarTab(contentY);
                else if (this.selectedTab == 5) calculatedHeight = this.DrawTeleportTab(contentY);
                else if (this.selectedTab == 6) calculatedHeight = this.DrawBulkSelectorTab(contentY);
                else if (this.selectedTab == 7) calculatedHeight = this.DrawSettingsTab(contentY);
                this.tabDrawContentHeight = calculatedHeight;
                GUI.EndScrollView();
            }
            finally
            {
                GUI.EndGroup();
            }

            this.DrawQuickStatusPanel(rightTopCard);
        }

        public bool UI_DrawPrimaryActionButton(Rect rect, string label)
        {
            return this.DrawPrimaryActionButton(rect, label);
        }

        public float UI_DrawAccentSlider(Rect rect, float value, float min, float max)
        {
            return this.DrawAccentSlider(rect, value, min, max, false);
        }

        public int UI_DrawAccentIntSlider(Rect rect, int value, int min, int max)
        {
            float raw = this.DrawAccentSlider(rect, value, min, max, true);
            return Mathf.Clamp(Mathf.RoundToInt(raw), min, max);
        }

        public bool UI_DrawSwitchToggle(Rect rect, bool value, string label)
        {
            return this.DrawSwitchToggle(rect, value, label);
        }

        public int UI_DrawSingleSelectDropdown(Rect rect, string label, string[] options, int selectedIndex, ref bool isOpen)
        {
            if (options == null || options.Length == 0)
            {
                return 0;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, options.Length - 1);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(new Rect(rect.x, rect.y - 22f, rect.width, 20f), this.L(label), labelStyle);

            GUI.Box(rect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(rect, 1f);
            if (GUI.Button(rect, "", GUIStyle.none))
            {
                isOpen = !isOpen;
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

            GUI.Label(new Rect(rect.x + 10f, rect.y + 1f, rect.width - 32f, rect.height - 2f), this.L(options[selectedIndex]), valueStyle);
            GUI.Label(new Rect(rect.xMax - 22f, rect.y + 1f, 14f, rect.height - 2f), isOpen ? "^" : "v", arrowStyle);

            if (!isOpen)
            {
                return selectedIndex;
            }

            float optionHeight = 30f;
            Rect panelRect = new Rect(rect.x + 4f, rect.yMax + 4f, rect.width - 8f, options.Length * optionHeight + 8f);
            GUI.Box(panelRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(panelRect, 1f);

            GUIStyle optionTextStyle = new GUIStyle(GUI.skin.label);
            optionTextStyle.fontSize = 12;
            optionTextStyle.fontStyle = FontStyle.Bold;
            optionTextStyle.alignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < options.Length; i++)
            {
                Rect optionRect = new Rect(panelRect.x + 4f, panelRect.y + 4f + i * optionHeight, panelRect.width - 8f, optionHeight - 4f);
                bool selected = i == selectedIndex;
                GUI.Box(optionRect, "", selected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));
                if (GUI.Button(optionRect, "", GUIStyle.none))
                {
                    selectedIndex = i;
                    isOpen = false;
                }

                optionTextStyle.normal.textColor = selected ? Color.white : new Color(this.uiTextR, this.uiTextG, this.uiTextB);
                GUI.Label(optionRect, this.L(options[i]), optionTextStyle);
            }

            return selectedIndex;
        }

        public string UI_Localize(string text)
        {
            return this.L(text);
        }

        public string UI_LocalizeFormat(string format, params object[] args)
        {
            return this.LF(format, args);
        }

        public void UI_SaveKeybinds(bool showNotification = false)
        {
            try { this.SaveKeybinds(showNotification); } catch { }
        }

        public void UI_DirectClickInteractButton()
        {
            this.DirectClickInteractButton();
        }

        private void AddMenuNotification(string message, Color color, float duration = 5f, bool force = false)
        {
            this.AddOrUpdateMenuNotification(null, message, color, duration, force);
        }

        private void AddOrUpdateMenuNotification(string key, string message, Color color, float duration = 5f, bool force = false)
        {
            if (!force && !this.notificationsEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            float safeDuration = Mathf.Max(0.1f, duration);
            if (!string.IsNullOrWhiteSpace(key))
            {
                for (int i = 0; i < this.menuNotifications.Count; i++)
                {
                    HeartopiaComplete.MenuNotification existing = this.menuNotifications[i];
                    if (existing == null || !string.Equals(existing.Key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    existing.Message = this.L(message);
                    existing.Color = color;
                    existing.ExpireAt = now + safeDuration;
                    existing.Duration = safeDuration;
                    existing.Force = force;
                    return;
                }
            }

            this.menuNotifications.Add(new HeartopiaComplete.MenuNotification
            {
                Key = key,
                Message = this.L(message),
                Color = color,
                CreatedAt = now,
                ExpireAt = now + safeDuration,
                Duration = safeDuration,
                Force = force
            });
            if (this.menuNotifications.Count > 6)
            {
                this.menuNotifications.RemoveAt(0);
            }
        }

        public void UI_AddMenuNotification(string message, Color color, float duration = 5f)
        {
            this.AddMenuNotification(message, color, duration);
        }

        private void DrawMenuNotifications(Rect area)
        {
            float now = Time.unscaledTime;
            this.menuNotifications.RemoveAll(n => n == null || n.ExpireAt <= now);
            if (this.menuNotifications.Count == 0)
            {
                return;
            }

            bool drawAll = this.notificationsEnabled;
            if (!drawAll)
            {
                bool hasForcedNotification = false;
                for (int i = 0; i < this.menuNotifications.Count; i++)
                {
                    if (this.menuNotifications[i] != null && this.menuNotifications[i].Force)
                    {
                        hasForcedNotification = true;
                        break;
                    }
                }

                if (!hasForcedNotification)
                {
                    return;
                }
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 12;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleLeft;
            style.wordWrap = true;

            float maxWidth = Mathf.Clamp(Screen.width * 0.44f, 260f, 520f);
            List<HeartopiaComplete.MenuNotification> visibleNotifications = new List<HeartopiaComplete.MenuNotification>();
            for (int i = this.menuNotifications.Count - 1; i >= 0; i--)
            {
                HeartopiaComplete.MenuNotification item = this.menuNotifications[i];
                if (!drawAll && !item.Force)
                {
                    continue;
                }

                visibleNotifications.Add(item);
            }

            if (visibleNotifications.Count == 0)
            {
                return;
            }

            List<float> boxHeights = new List<float>(visibleNotifications.Count);
            List<float> boxWidths = new List<float>(visibleNotifications.Count);
            float totalHeight = 0f;
            for (int i = 0; i < visibleNotifications.Count; i++)
            {
                HeartopiaComplete.MenuNotification item = visibleNotifications[i];
                GUIContent content = new GUIContent(item.Message ?? string.Empty);
                Vector2 measured = style.CalcSize(content);
                float boxWidth = Mathf.Clamp(measured.x + 46f, area.width, maxWidth);
                float textWidth = boxWidth - 28f;
                float textHeight = Mathf.Clamp(style.CalcHeight(content, textWidth), 18f, 92f);
                float boxHeight = Mathf.Clamp(textHeight + 20f, 42f, 112f);
                boxWidths.Add(boxWidth);
                boxHeights.Add(boxHeight);
                totalHeight += boxHeight;
                if (i < visibleNotifications.Count - 1)
                {
                    totalHeight += 6f;
                }
            }

            float xMargin = 20f;
            float topY = 14f;
            float middleY = Mathf.Max(14f, (Screen.height - totalHeight) * 0.5f);
            float bottomY = Mathf.Max(14f, Screen.height - totalHeight - 20f);
            float y = topY;
            switch (this.notificationPosition)
            {
                case 1:
                case 6:
                    y = middleY;
                    break;
                case 2:
                case 4:
                case 7:
                    y = bottomY;
                    break;
                default:
                    y = topY;
                    break;
            }

            for (int i = 0; i < visibleNotifications.Count; i++)
            {
                HeartopiaComplete.MenuNotification item = visibleNotifications[i];

                float remain = Mathf.Clamp01((item.ExpireAt - now) / item.Duration);

                // Smooth in/out animation.
                float inAnim = Mathf.Clamp01((now - item.CreatedAt) / 0.12f);
                float outAnim = Mathf.Clamp01((item.ExpireAt - now) / 0.18f);
                float anim = Mathf.Min(inAnim, outAnim);
                float alpha = Mathf.Lerp(0f, 1f, anim);
                float slide = (1f - anim) * 18f;
                float boxWidth = boxWidths[i];
                float boxHeight = boxHeights[i];
                float boxX;
                switch (this.notificationPosition)
                {
                    case 0:
                    case 1:
                    case 2:
                        boxX = xMargin - slide;
                        break;
                    case 3:
                    case 4:
                        boxX = (Screen.width - boxWidth) * 0.5f;
                        break;
                    default:
                        boxX = Screen.width - boxWidth - xMargin + slide;
                        break;
                }

                Rect box = new Rect(boxX, y, boxWidth, boxHeight);
                Rect shadow = new Rect(box.x + 2f, box.y + 3f, box.width, box.height);
                Rect accentStrip = new Rect(box.x, box.y, 4f, box.height);
                Rect messageRect = new Rect(box.x + 18f, box.y + 6f, box.width - 28f, box.height - 16f);
                Rect progressBg = new Rect(box.x + 10f, box.y + box.height - 8f, box.width - 20f, 3f);
                Rect progressFg = new Rect(progressBg.x, progressBg.y, progressBg.width * remain, progressBg.height);

                Color cardBg = new Color(0.04f, 0.06f, 0.09f, 0.93f * alpha);
                Color shadowBg = new Color(0f, 0f, 0f, 0.32f * alpha);
                Color stripColor = new Color(item.Color.r, item.Color.g, item.Color.b, 0.95f * alpha);
                Color progressBgColor = new Color(0.2f, 0.24f, 0.32f, 0.6f * alpha);
                Color progressColor = new Color(item.Color.r, item.Color.g, item.Color.b, 0.95f * alpha);

                GUI.color = shadowBg;
                GUI.DrawTexture(shadow, Texture2D.whiteTexture);
                GUI.color = cardBg;
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = stripColor;
                GUI.DrawTexture(accentStrip, Texture2D.whiteTexture);
                GUI.color = progressBgColor;
                GUI.DrawTexture(progressBg, Texture2D.whiteTexture);
                GUI.color = progressColor;
                GUI.DrawTexture(progressFg, Texture2D.whiteTexture);
                GUI.color = Color.white;

                style.normal.textColor = new Color(0.94f, 0.96f, 1f, alpha);
                GUI.Label(messageRect, item.Message, style);

                // small status dot icon
                this.EnsureUiPrimitiveTextures();
                GUI.color = stripColor;
                GUI.DrawTexture(new Rect(box.x + 8f, box.y + 16f, 6f, 6f), this.uiCircleTexture);
                GUI.color = Color.white;

                y += boxHeight + 6f;
                if (y > Screen.height - 42f)
                {
                    break;
                }
            }
        }

        private void EnsureThemeStyles()
        {
            bool themeInvalid =
                this.themeWindowStyle == null ||
                this.themePanelStyle == null ||
                this.themeContentStyle == null ||
                this.themeSidebarButtonStyle == null ||
                this.themeSidebarButtonActiveStyle == null ||
                this.themePrimaryButtonStyle == null ||
                this.themeDangerButtonStyle == null ||
                this.themeTopTabStyle == null ||
                this.themeTopTabActiveStyle == null ||
                this.themeWindowStyle.normal.background == null ||
                this.themePanelStyle.normal.background == null ||
                this.themeContentStyle.normal.background == null;

            if (this.themeInitialized && !themeInvalid)
            {
                return;
            }

            if (GUI.skin == null)
            {
                return;
            }

            this.InvalidateThemeCache();

            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.82f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color mainTabText = new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB);
            Color windowBase = new Color(this.uiWindowR, this.uiWindowG, this.uiWindowB, this.uiWindowAlpha);
            Color panel = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, this.uiPanelAlpha);
            Color contentBase = new Color(this.uiContentR, this.uiContentG, this.uiContentB, this.uiContentAlpha);

            Texture2D windowBg = this.MakeThemeTexture(new Color(0f, 0f, 0f, 0f));
            Texture2D panelBg = this.MakeThemeTexture(panel);
            Texture2D contentBg = this.MakeThemeTexture(contentBase);
            float buttonAlpha = Mathf.Clamp(this.uiContentAlpha, 0.18f, 1f);
            Texture2D buttonBg = this.MakeThemeTexture(new Color(0.12f, 0.11f, 0.19f, buttonAlpha));
            Texture2D buttonHover = this.MakeThemeTexture(new Color(0.16f, 0.14f, 0.24f, Mathf.Min(1f, buttonAlpha + 0.08f)));
            Texture2D buttonActive = this.MakeThemeTexture(new Color(0.19f, 0.16f, 0.28f, buttonAlpha));
            Texture2D tabActive = this.MakeThemeTexture(new Color(accent.r * 0.35f, accent.g * 0.18f, accent.b * 0.45f, Mathf.Clamp(this.uiPanelAlpha, 0.2f, 1f)));
            Texture2D topTabBg = this.MakeThemeTexture(new Color(0.11f, 0.1f, 0.18f, Mathf.Clamp(this.uiPanelAlpha + 0.02f, 0.22f, 1f)));
            Texture2D topTabActive = this.MakeThemeTexture(new Color(accent.r * 0.25f, accent.g * 0.18f, accent.b * 0.36f, Mathf.Clamp(this.uiPanelAlpha + 0.1f, 0.22f, 1f)));
            Texture2D primaryButtonBg = this.MakeThemeTexture(new Color(
                Mathf.Lerp(0.12f, accent.r, 0.45f),
                Mathf.Lerp(0.12f, accent.g, 0.45f),
                Mathf.Lerp(0.16f, accent.b, 0.45f),
                Mathf.Clamp(this.uiContentAlpha + 0.08f, 0.3f, 1f)));
            Texture2D primaryButtonHover = this.MakeThemeTexture(new Color(
                Mathf.Lerp(0.16f, accent.r, 0.62f),
                Mathf.Lerp(0.16f, accent.g, 0.62f),
                Mathf.Lerp(0.2f, accent.b, 0.62f),
                Mathf.Clamp(this.uiContentAlpha + 0.12f, 0.34f, 1f)));
            Texture2D dangerButtonBg = this.MakeThemeTexture(new Color(0.4f, 0.12f, 0.2f, Mathf.Clamp(this.uiContentAlpha + 0.08f, 0.3f, 1f)));
            Texture2D dangerButtonHover = this.MakeThemeTexture(new Color(0.52f, 0.16f, 0.26f, Mathf.Clamp(this.uiContentAlpha + 0.08f, 0.3f, 1f)));
            Texture2D sliderBg = this.MakeThemeTexture(new Color(0.08f, 0.09f, 0.1f, 1f));
            Texture2D sliderThumb = this.MakeThemeTexture(accent);
            Texture2D boxBg = this.MakeThemeTexture(new Color(0.1f, 0.09f, 0.16f, Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f)));
            Texture2D fieldBg = this.MakeThemeTexture(new Color(0.12f, 0.1f, 0.18f, Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f)));
            Texture2D cursorBg = this.MakeThemeTexture(accent);

            this.themeWindowStyle = new GUIStyle(GUI.skin.box);
            this.themeWindowStyle.normal.background = windowBg;
            this.themeWindowStyle.onNormal.background = windowBg;
            this.themeWindowStyle.border = new RectOffset(0, 0, 0, 0);
            this.themeWindowStyle.padding = new RectOffset(0, 0, 0, 0);
            this.themeWindowStyle.margin = new RectOffset(0, 0, 0, 0);
            this.themeWindowStyle.normal.textColor = textPrimary;
            this.themeWindowStyle.alignment = TextAnchor.UpperCenter;
            this.themeWindowStyle.fontStyle = FontStyle.Bold;
            this.themeWindowStyle.fontSize = 16;

            this.themePanelStyle = new GUIStyle(GUI.skin.box);
            this.themePanelStyle.normal.background = panelBg;
            this.themePanelStyle.onNormal.background = panelBg;
            this.themePanelStyle.border = new RectOffset(4, 4, 4, 4);
            this.themePanelStyle.padding = new RectOffset(8, 8, 8, 8);
            this.themePanelStyle.normal.textColor = textMuted;

            this.themeContentStyle = new GUIStyle(GUI.skin.box);
            this.themeContentStyle.normal.background = contentBg;
            this.themeContentStyle.onNormal.background = contentBg;
            this.themeContentStyle.border = new RectOffset(4, 4, 4, 4);
            this.themeContentStyle.padding = new RectOffset(8, 8, 8, 8);

            this.themeSidebarButtonStyle = new GUIStyle(GUI.skin.button);
            this.themeSidebarButtonStyle.fixedHeight = 42f;
            this.themeSidebarButtonStyle.margin = new RectOffset(2, 2, 4, 4);
            this.themeSidebarButtonStyle.fontSize = 14;
            this.themeSidebarButtonStyle.fontStyle = FontStyle.Bold;
            this.themeSidebarButtonStyle.alignment = TextAnchor.MiddleLeft;
            this.themeSidebarButtonStyle.padding = new RectOffset(16, 10, 4, 4);
            this.themeSidebarButtonStyle.normal.background = buttonBg;
            this.themeSidebarButtonStyle.hover.background = buttonHover;
            this.themeSidebarButtonStyle.active.background = buttonActive;
            this.themeSidebarButtonStyle.normal.textColor = mainTabText;
            this.themeSidebarButtonStyle.hover.textColor = textPrimary;
            this.themeSidebarButtonStyle.active.textColor = accent;

            this.themeSidebarButtonActiveStyle = new GUIStyle(this.themeSidebarButtonStyle);
            this.themeSidebarButtonActiveStyle.normal.background = tabActive;
            this.themeSidebarButtonActiveStyle.hover.background = tabActive;
            this.themeSidebarButtonActiveStyle.active.background = tabActive;
            this.themeSidebarButtonActiveStyle.normal.textColor = accent;
            this.themeSidebarButtonActiveStyle.hover.textColor = accent;
            this.themeSidebarButtonActiveStyle.active.textColor = accent;

            this.themeTopTabStyle = new GUIStyle(GUI.skin.box);
            this.themeTopTabStyle.normal.background = topTabBg;
            this.themeTopTabStyle.onNormal.background = topTabBg;
            this.themeTopTabStyle.border = new RectOffset(5, 5, 5, 5);

            this.themeTopTabActiveStyle = new GUIStyle(GUI.skin.box);
            this.themeTopTabActiveStyle.normal.background = topTabActive;
            this.themeTopTabActiveStyle.onNormal.background = topTabActive;
            this.themeTopTabActiveStyle.border = new RectOffset(5, 5, 5, 5);

            this.themePrimaryButtonStyle = new GUIStyle(GUI.skin.button);
            this.themePrimaryButtonStyle.normal.background = primaryButtonBg;
            this.themePrimaryButtonStyle.hover.background = primaryButtonHover;
            this.themePrimaryButtonStyle.active.background = primaryButtonHover;
            this.themePrimaryButtonStyle.fontStyle = FontStyle.Bold;
            this.themePrimaryButtonStyle.fontSize = 13;
            this.themePrimaryButtonStyle.normal.textColor = Color.white;
            this.themePrimaryButtonStyle.hover.textColor = Color.white;
            this.themePrimaryButtonStyle.active.textColor = Color.white;

            this.themeDangerButtonStyle = new GUIStyle(this.themePrimaryButtonStyle);
            this.themeDangerButtonStyle.normal.background = dangerButtonBg;
            this.themeDangerButtonStyle.hover.background = dangerButtonHover;
            this.themeDangerButtonStyle.active.background = dangerButtonHover;

            GUI.skin.label.normal.textColor = textPrimary;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = buttonBg;
            buttonStyle.hover.background = buttonHover;
            buttonStyle.active.background = buttonActive;
            buttonStyle.normal.textColor = textPrimary;
            buttonStyle.hover.textColor = textPrimary;
            buttonStyle.active.textColor = accent;
            buttonStyle.fontStyle = FontStyle.Bold;
            GUI.skin.button = buttonStyle;

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.normal.textColor = textPrimary;
            toggleStyle.onNormal.textColor = accent;
            toggleStyle.hover.textColor = accent;
            toggleStyle.onHover.textColor = accent;
            toggleStyle.active.textColor = accent;
            toggleStyle.onActive.textColor = accent;
            GUI.skin.toggle = toggleStyle;

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = boxBg;
            boxStyle.onNormal.background = boxBg;
            boxStyle.normal.textColor = textPrimary;
            GUI.skin.box = boxStyle;

            GUIStyle sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            sliderStyle.normal.background = sliderBg;
            sliderStyle.fixedHeight = 6f;
            sliderStyle.margin = new RectOffset(4, 4, 10, 10);
            GUI.skin.horizontalSlider = sliderStyle;

            GUIStyle sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
            sliderThumbStyle.normal.background = sliderThumb;
            sliderThumbStyle.hover.background = sliderThumb;
            sliderThumbStyle.active.background = sliderThumb;
            sliderThumbStyle.fixedWidth = 12f;
            sliderThumbStyle.fixedHeight = 16f;
            GUI.skin.horizontalSliderThumb = sliderThumbStyle;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.normal.background = fieldBg;
            textFieldStyle.focused.background = fieldBg;
            textFieldStyle.hover.background = fieldBg;
            textFieldStyle.active.background = fieldBg;
            textFieldStyle.normal.textColor = textPrimary;
            textFieldStyle.focused.textColor = Color.white;
            textFieldStyle.padding = new RectOffset(8, 8, 4, 4);
            GUI.skin.textField = textFieldStyle;

            GUIStyle scrollStyle = new GUIStyle(GUI.skin.verticalScrollbar);
            scrollStyle.normal.background = this.MakeThemeTexture(new Color(0.12f, 0.11f, 0.17f, 1f));
            GUI.skin.verticalScrollbar = scrollStyle;

            GUIStyle scrollThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb);
            scrollThumbStyle.normal.background = this.MakeThemeTexture(new Color(accent.r, accent.g, accent.b, 0.86f));
            scrollThumbStyle.hover.background = this.MakeThemeTexture(new Color(accent.r, accent.g, accent.b, 0.96f));
            scrollThumbStyle.active.background = cursorBg;
            GUI.skin.verticalScrollbarThumb = scrollThumbStyle;

            this.themeInitialized = true;
        }

        private Texture2D MakeThemeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            this.themeTextures.Add(texture);
            return texture;
        }

        private float DrawUiThemeTab(int startY)
        {
            int num = startY;
            float left = 20f;
            float contentWidth = 580f;
            float rowHeight = 34f;
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color panelFill = new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
            Color panelLine = new Color(accent.r, accent.g, accent.b, 0.24f);
            Color rowFill = new Color(1f, 1f, 1f, 0.025f);
            Color rowBorder = new Color(1f, 1f, 1f, 0.045f);

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 14 };
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 12 };
            sectionStyle.normal.textColor = accent;
            GUIStyle rowLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            rowLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            GUIStyle rowValueStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 12, fontStyle = FontStyle.Bold };
            rowValueStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);

            GUI.Label(new Rect(left, (float)num, contentWidth, 25f), "UI THEME", headerStyle);
            num += 32;

            bool changed = false;
            Rect displayPanel = new Rect(left, (float)num, contentWidth, 90f);
            this.DrawExentriSectionPanel(displayPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(displayPanel.x + 14f, displayPanel.y + 12f, displayPanel.width - 28f, 18f), "DISPLAY", sectionStyle);
            Rect scaleRowRect = new Rect(displayPanel.x + 14f, displayPanel.y + 44f, displayPanel.width - 28f, 28f);
            Rect scaleSliderRect = new Rect(scaleRowRect.x + 164f, scaleRowRect.y + 1f, scaleRowRect.width - 282f, 20f);
            GUI.Label(new Rect(scaleRowRect.x, scaleRowRect.y, 150f, 22f), $"UI Scale: {Mathf.RoundToInt(this.uiScale * 100f)}%", rowLabelStyle);
            float newScale = this.DrawAccentSlider(scaleSliderRect, this.uiScale, UiScaleMin, UiScaleMax);
            if (Math.Abs(newScale - this.uiScale) > 0.001f)
            {
                this.uiScale = this.NormalizeUiScale(newScale);
                this.KeepMenuWindowOnScreen(this.GetUiScale());
                changed = true;
            }
            if (GUI.Button(new Rect(scaleRowRect.xMax - 94f, scaleRowRect.y - 2f, 94f, 26f), "100%", this.themeTopTabStyle ?? GUI.skin.button))
            {
                this.uiScale = 1f;
                this.KeepMenuWindowOnScreen(this.GetUiScale());
                changed = true;
            }
            num += (int)displayPanel.height + 14;

            string[] colorTargets = new string[]
            {
                "Accent",
                "Text",
                "Main Tab Text",
                "Sub Tab Text",
                "Panel Bg",
                "Content Bg"
            };
            int[] colorTargetIndices = new int[] { 0, 1, 2, 3, 5, 6 };
            float pickerHeight = this.uiThemePickerOpen ? 260f : 0f;
            Rect colorPanel = new Rect(left, (float)num, contentWidth, 42f + (colorTargets.Length * rowHeight) + pickerHeight + 12f);
            this.DrawExentriSectionPanel(colorPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(colorPanel.x + 14f, colorPanel.y + 12f, colorPanel.width - 28f, 18f), "THEME COLORS", sectionStyle);
            num += 42;

            for (int i = 0; i < colorTargets.Length; i++)
            {
                int targetIndex = colorTargetIndices[i];
                Rect rowRect = new Rect(left + 14f, (float)num, contentWidth - 28f, rowHeight - 4f);
                this.DrawRoundedPanel(rowRect, 6f, rowFill, rowBorder, 1f, Color.clear);
                GUI.Label(new Rect(rowRect.x + 12f, rowRect.y + 5f, 220f, 20f), colorTargets[i], rowLabelStyle);

                Rect swatchRect = new Rect(rowRect.xMax - 32f, rowRect.y + 6f, 18f, 18f);
                Color targetColor = this.GetUiThemeColorTargetValue(targetIndex);
                GUI.color = targetColor;
                GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);
                GUI.color = Color.white;

                if (this.uiThemeColorTarget == targetIndex && this.uiThemePickerOpen)
                {
                    GUI.DrawTexture(new Rect(swatchRect.x - 2f, swatchRect.y - 2f, swatchRect.width + 4f, 1f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(swatchRect.x - 2f, swatchRect.yMax + 1f, swatchRect.width + 4f, 1f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(swatchRect.x - 2f, swatchRect.y - 1f, 1f, swatchRect.height + 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(swatchRect.xMax + 1f, swatchRect.y - 1f, 1f, swatchRect.height + 2f), Texture2D.whiteTexture);
                }

                if (GUI.Button(rowRect, "", GUIStyle.none))
                {
                    if (this.uiThemeColorTarget == targetIndex && this.uiThemePickerOpen)
                    {
                        this.uiThemePickerOpen = false;
                    }
                    else
                    {
                        this.uiThemeColorTarget = targetIndex;
                        this.uiThemeHexInput = this.ColorToHex(this.GetUiThemeColorTargetValue(this.uiThemeColorTarget));
                        this.uiThemePickerOpen = true;
                    }
                }
                num += (int)rowHeight;
            }

            Color originalColor = this.GetUiThemeColorTargetValue(this.uiThemeColorTarget);
            float h;
            float s;
            float v;
            Color.RGBToHSV(originalColor, out h, out s, out v);
            Color pickedColor = originalColor;

            if (this.uiThemePickerOpen)
            {
                num += 8;
                this.EnsureUiPickerTextures(h);
                Rect svRect = new Rect(left + 14f, (float)num, 260f, 190f);
                Rect hueRect = new Rect(svRect.xMax + 10f, (float)num, 18f, 190f);
                GUI.DrawTexture(svRect, this.uiSvTexture);
                GUI.DrawTexture(hueRect, this.uiHueTexture);

                Event e = Event.current;
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                {
                    if (svRect.Contains(e.mousePosition))
                    {
                        s = Mathf.Clamp01((e.mousePosition.x - svRect.x) / svRect.width);
                        v = 1f - Mathf.Clamp01((e.mousePosition.y - svRect.y) / svRect.height);
                        changed = true;
                        e.Use();
                    }
                    else if (hueRect.Contains(e.mousePosition))
                    {
                        h = 1f - Mathf.Clamp01((e.mousePosition.y - hueRect.y) / hueRect.height);
                        this.EnsureUiPickerTextures(h);
                        changed = true;
                        e.Use();
                    }
                }

                float svX = svRect.x + s * svRect.width;
                float svY = svRect.y + (1f - v) * svRect.height;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(svX - 1f, svY - 6f, 2f, 12f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(svX - 6f, svY - 1f, 12f, 2f), Texture2D.whiteTexture);
                float hueY = hueRect.y + (1f - h) * hueRect.height;
                GUI.DrawTexture(new Rect(hueRect.x - 2f, hueY - 1f, hueRect.width + 4f, 2f), Texture2D.whiteTexture);

                pickedColor = Color.HSVToRGB(h, s, v);
                if (changed)
                {
                    this.SetUiThemeColorTargetValue(this.uiThemeColorTarget, pickedColor);
                    this.uiThemeHexInput = this.ColorToHex(pickedColor);
                }

                Rect previewCurrent = new Rect(hueRect.xMax + 22f, (float)num, 72f, 88f);
                Rect previewOriginal = new Rect(hueRect.xMax + 22f, (float)num + 102f, 72f, 88f);
                this.DrawRoundedPanel(previewCurrent, 6f, rowFill, rowBorder, 1f, Color.clear);
                this.DrawRoundedPanel(previewOriginal, 6f, rowFill, rowBorder, 1f, Color.clear);
                GUI.color = pickedColor;
                GUI.DrawTexture(new Rect(previewCurrent.x + 8f, previewCurrent.y + 8f, 56f, 72f), Texture2D.whiteTexture);
                GUI.color = originalColor;
                GUI.DrawTexture(new Rect(previewOriginal.x + 8f, previewOriginal.y + 8f, 56f, 72f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(previewCurrent.xMax + 12f, (float)num + 10f, 120f, 18f), "Current", rowLabelStyle);
                GUI.Label(new Rect(previewOriginal.xMax + 12f, (float)num + 112f, 120f, 18f), "Original", rowLabelStyle);

                num += 200;

                int r = Mathf.RoundToInt(pickedColor.r * 255f);
                int g = Mathf.RoundToInt(pickedColor.g * 255f);
                int b = Mathf.RoundToInt(pickedColor.b * 255f);
                GUI.Label(new Rect(left + 14f, (float)num, 260f, 18f), $"R:{r}  G:{g}  B:{b}", rowLabelStyle);
                GUI.Label(new Rect(left + 284f, (float)num, 240f, 18f), $"H:{Mathf.RoundToInt(h * 360f)}  S:{Mathf.RoundToInt(s * 100f)}  V:{Mathf.RoundToInt(v * 100f)}", rowLabelStyle);
                num += 22;

                GUI.Label(new Rect(left + 14f, (float)num, 40f, 20f), "Hex:", rowLabelStyle);
                this.uiThemeHexInput = GUI.TextField(new Rect(left + 56f, (float)num, 140f, 24f), this.uiThemeHexInput);
                if (GUI.Button(new Rect(left + 204f, (float)num, 78f, 24f), "Apply", this.themeTopTabStyle ?? GUI.skin.button))
                {
                    Color parsed;
                    if (this.TryParseHexColor(this.uiThemeHexInput, out parsed))
                    {
                        this.SetUiThemeColorTargetValue(this.uiThemeColorTarget, parsed);
                        this.uiThemeHexInput = this.ColorToHex(parsed);
                        changed = true;
                    }
                }
                num += 34;
            }
            else
            {
                num += 12;
            }

            num = (int)(colorPanel.y + colorPanel.height + 14f);

            Rect transparencyPanel = new Rect(left, (float)num, contentWidth, 124f);
            this.DrawExentriSectionPanel(transparencyPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(transparencyPanel.x + 14f, transparencyPanel.y + 12f, transparencyPanel.width - 28f, 18f), "TRANSPARENCY", sectionStyle);
            num += 42;

            Rect panelAlphaRow = new Rect(transparencyPanel.x + 14f, (float)num, transparencyPanel.width - 28f, 24f);
            GUI.Label(new Rect(panelAlphaRow.x, panelAlphaRow.y, 150f, 20f), "Panel Alpha", rowLabelStyle);
            float newPanelA = this.DrawAccentSlider(new Rect(panelAlphaRow.x + 164f, panelAlphaRow.y + 1f, panelAlphaRow.width - 226f, 20f), this.uiPanelAlpha, 0.15f, 1f);
            GUI.Label(new Rect(panelAlphaRow.xMax - 52f, panelAlphaRow.y, 52f, 20f), this.uiPanelAlpha.ToString("F2"), rowValueStyle);
            if (Math.Abs(newPanelA - this.uiPanelAlpha) > 0.001f) { this.uiPanelAlpha = newPanelA; changed = true; }
            num += 34;

            Rect contentAlphaRow = new Rect(transparencyPanel.x + 14f, (float)num, transparencyPanel.width - 28f, 24f);
            GUI.Label(new Rect(contentAlphaRow.x, contentAlphaRow.y, 150f, 20f), "Content Alpha", rowLabelStyle);
            float newContentA = this.DrawAccentSlider(new Rect(contentAlphaRow.x + 164f, contentAlphaRow.y + 1f, contentAlphaRow.width - 226f, 20f), this.uiContentAlpha, 0.15f, 1f);
            GUI.Label(new Rect(contentAlphaRow.xMax - 52f, contentAlphaRow.y, 52f, 20f), this.uiContentAlpha.ToString("F2"), rowValueStyle);
            if (Math.Abs(newContentA - this.uiContentAlpha) > 0.001f) { this.uiContentAlpha = newContentA; changed = true; }
            num = (int)(transparencyPanel.y + transparencyPanel.height + 14f);

            if (changed)
            {
                this.uiAccentR = Mathf.Clamp01(this.uiAccentR);
                this.uiAccentG = Mathf.Clamp01(this.uiAccentG);
                this.uiAccentB = Mathf.Clamp01(this.uiAccentB);
                this.uiTextR = Mathf.Clamp01(this.uiTextR);
                this.uiTextG = Mathf.Clamp01(this.uiTextG);
                this.uiTextB = Mathf.Clamp01(this.uiTextB);
                this.uiMainTabTextR = Mathf.Clamp01(this.uiMainTabTextR);
                this.uiMainTabTextG = Mathf.Clamp01(this.uiMainTabTextG);
                this.uiMainTabTextB = Mathf.Clamp01(this.uiMainTabTextB);
                this.uiSubTabTextR = Mathf.Clamp01(this.uiSubTabTextR);
                this.uiSubTabTextG = Mathf.Clamp01(this.uiSubTabTextG);
                this.uiSubTabTextB = Mathf.Clamp01(this.uiSubTabTextB);
                this.uiPanelR = Mathf.Clamp01(this.uiPanelR);
                this.uiPanelG = Mathf.Clamp01(this.uiPanelG);
                this.uiPanelB = Mathf.Clamp01(this.uiPanelB);
                this.uiContentR = Mathf.Clamp01(this.uiContentR);
                this.uiContentG = Mathf.Clamp01(this.uiContentG);
                this.uiContentB = Mathf.Clamp01(this.uiContentB);
                this.uiPanelAlpha = Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f);
                this.uiContentAlpha = Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f);
                this.uiScale = this.NormalizeUiScale(this.uiScale);
                this.InvalidateThemeCache();
                // Auto-save theme when any picker/transparency changed
                try { this.SaveUiTheme(); this.AddMenuNotification("UI theme auto-saved", new Color(0.55f, 0.88f, 1f)); } catch { }
            }

            Rect actionPanel = new Rect(left, (float)num, contentWidth, 66f);
            this.DrawExentriSectionPanel(actionPanel, accent, panelFill, panelLine);
            if (this.DrawPrimaryActionButton(new Rect(actionPanel.x + 14f, actionPanel.y + 17f, 130f, 32f), "SAVE"))
            {
                this.SaveUiTheme();
            }

            if (this.DrawPrimaryActionButton(new Rect(actionPanel.x + 154f, actionPanel.y + 17f, 130f, 32f), "LOAD"))
            {
                this.LoadUiTheme();
            }

            if (this.DrawDangerActionButton(new Rect(actionPanel.x + 294f, actionPanel.y + 17f, 130f, 32f), "RESET"))
            {
                this.uiAccentR = 0.36f;
                this.uiAccentG = 0.70f;
                this.uiAccentB = 0.98f;
                this.uiTextR = 0.95f;
                this.uiTextG = 0.97f;
                this.uiTextB = 0.99f;
                this.uiMainTabTextR = 0.75f;
                this.uiMainTabTextG = 0.82f;
                this.uiMainTabTextB = 0.90f;
                this.uiSubTabTextR = 0.53f;
                this.uiSubTabTextG = 0.59f;
                this.uiSubTabTextB = 0.67f;
                this.uiWindowR = 0.055f;
                this.uiWindowG = 0.065f;
                this.uiWindowB = 0.085f;
                this.uiPanelR = 0.075f;
                this.uiPanelG = 0.085f;
                this.uiPanelB = 0.11f;
                this.uiContentR = 0.095f;
                this.uiContentG = 0.11f;
                this.uiContentB = 0.14f;
                this.uiWindowAlpha = 0.94f;
                this.uiPanelAlpha = 0.90f;
                this.uiContentAlpha = 0.86f;
                this.uiScale = 1f;
                this.uiThemeHexInput = this.ColorToHex(new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB));
                this.KeepMenuWindowOnScreen(this.GetUiScale());
                this.InvalidateThemeCache();
            }
            num += (int)actionPanel.height + 16;

            return (float)num + 10f;
        }

        private Color GetUiThemeColorTargetValue(int target)
        {
            if (target == 0) return new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            if (target == 1) return new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            if (target == 2) return new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB);
            if (target == 3) return new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);
            if (target == 4) return new Color(this.uiWindowR, this.uiWindowG, this.uiWindowB);
            if (target == 5) return new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB);
            return new Color(this.uiContentR, this.uiContentG, this.uiContentB);
        }

        private void SetUiThemeColorTargetValue(int target, Color color)
        {
            if (target == 0)
            {
                this.uiAccentR = color.r; this.uiAccentG = color.g; this.uiAccentB = color.b;
            }
            else if (target == 1)
            {
                this.uiTextR = color.r; this.uiTextG = color.g; this.uiTextB = color.b;
            }
            else if (target == 2)
            {
                this.uiMainTabTextR = color.r; this.uiMainTabTextG = color.g; this.uiMainTabTextB = color.b;
            }
            else if (target == 3)
            {
                this.uiSubTabTextR = color.r; this.uiSubTabTextG = color.g; this.uiSubTabTextB = color.b;
            }
            else if (target == 4)
            {
                this.uiWindowR = color.r; this.uiWindowG = color.g; this.uiWindowB = color.b;
            }
            else if (target == 5)
            {
                this.uiPanelR = color.r; this.uiPanelG = color.g; this.uiPanelB = color.b;
            }
            else
            {
                this.uiContentR = color.r; this.uiContentG = color.g; this.uiContentB = color.b;
            }
        }

    }
}
