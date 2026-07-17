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
            data.uiThemeVersion = 2;
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
            if (data.uiThemeVersion < 2)
            {
                // Pre-redesign palette: keep the new defaults, honor only the saved scale.
                this.uiScale = this.NormalizeUiScale(data.uiScale > 0f ? data.uiScale : 1f);
                return;
            }
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

        private static int uiThemeInvalidateCount;
        private static float uiThemeNextInvalidateLogAt;

        private void InvalidateThemeCache()
        {
            // Diagnostic: a healthy session invalidates a handful of times (boot + theme edits).
            // A counter racing upward means something is rebuilding the theme every frame —
            // that destroys/recreates all UI textures and shows up as interface flicker.
            uiThemeInvalidateCount++;
            if (Time.realtimeSinceStartup >= uiThemeNextInvalidateLogAt)
            {
                uiThemeNextInvalidateLogAt = Time.realtimeSinceStartup + 5f;
                ModLogger.Msg("[UiTheme] cache invalidated (#" + uiThemeInvalidateCount + " this session).");
            }

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
            this.themeSegContainerStyle = null;
            this.themeSegPlateStyle = null;
            this.themeToastCardStyle = null;
            this.themeRoundedWhiteStyle = null;
            this.themeCapsuleWhiteStyle = null;
            this.themeCapsuleGradientStyle = null;
            this.themeRoundedRingStyle = null;
            this.themeBigCardRingStyle = null;
            this.themeStatusOverlayFrameStyle = null;
            this.themeBigTintableStyle = null;
            this.themeSidebarShapeStyle = null;
            this.uiCircleTexture = null;
            this.uiHueTexture = null;
            this.uiSvTexture = null;
            this.uiAccentGradientTex = null;
            this.uiRoundedRectSprite = null;
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
            // Keep the top-right corner (hotkey chip + close button) out of the drag strip,
            // otherwise DragWindow eats their MouseDown.
            GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(60f, this.windowRect.width - 130f), 30f));

            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);
            Color navText = new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB);
            Color windowBase = new Color(this.uiWindowR, this.uiWindowG, this.uiWindowB, Mathf.Clamp(this.uiWindowAlpha, 0.15f, 1f));
            Color basePanel = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, this.uiPanelAlpha);
            Color hairline = new Color(1f, 1f, 1f, 0.06f);
            Color controlFill = this.GetUiControlFill();
            Event evt = Event.current;

            Rect chromeRect = new Rect(0f, 0f, this.windowRect.width, this.windowRect.height);
            Rect sidebarRect = new Rect(0f, 0f, 200f, chromeRect.height);
            Rect mainRect = new Rect(sidebarRect.xMax, 0f, chromeRect.width - sidebarRect.width, chromeRect.height);
            Rect logoRect = new Rect(0f, 0f, sidebarRect.width, 56f);
            Rect footerRect = new Rect(0f, chromeRect.height - 56f, sidebarRect.width, 56f);
            Rect navListRect = new Rect(10f, logoRect.yMax + 10f, sidebarRect.width - 20f, footerRect.y - logoRect.yMax - 18f);
            Rect headerRect = new Rect(mainRect.x, 0f, mainRect.width, 56f);
            Rect subTabRect = new Rect(mainRect.x + 20f, headerRect.yMax + 12f, mainRect.width - 40f, 34f);

            // One bg0 slab with a soft ring; the sidebar column sits on it as bg1.
            // The slab is drawn twice: at 0.96 alpha a single pass lets bright game UI
            // (white quest text etc.) ghost through the header — two passes ≈ 0.998 opacity.
            // Ring is solid light-gray, not translucent white (bright stripe over light scenes).
            this.DrawRoundedPanel(chromeRect, 16f, new Color(0.165f, 0.205f, 0.27f, 0.92f), Color.clear, 0f, Color.clear);
            this.DrawTintedBigRoundedBox(new Rect(1f, 1f, chromeRect.width - 2f, chromeRect.height - 2f), windowBase);
            this.DrawTintedBigRoundedBox(new Rect(1f, 1f, chromeRect.width - 2f, chromeRect.height - 2f), windowBase);
            // Rounded-left/square-right shape (see MakeLeftRoundedRectTexture) — replaces an
            // earlier "round all 4 corners then patch the 2 unwanted ones opaque" approach that
            // rendered as a visible solid block once Window Alpha could be turned down low
            // enough to show the game through the corners (the patch had no way to be see-
            // through too, since it was pre-flattened assuming a near-opaque background).
            this.DrawTintedLeftRoundedBox(new Rect(1f, 1f, sidebarRect.width - 1f, chromeRect.height - 2f), basePanel);
            GUI.color = hairline;
            GUI.DrawTexture(new Rect(sidebarRect.xMax, 1f, 1f, chromeRect.height - 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(1f, logoRect.yMax, sidebarRect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(1f, footerRect.y, sidebarRect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(mainRect.x + 1f, headerRect.yMax, mainRect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Sidebar logo: wordmark + build (no icon mark).
            this.EnsureUiPrimitiveTextures();
            GUIStyle logoStyle = new GUIStyle(GUI.skin.label);
            logoStyle.fontSize = 15;
            logoStyle.fontStyle = FontStyle.Bold;
            logoStyle.alignment = TextAnchor.MiddleLeft;
            logoStyle.normal.textColor = textPrimary;
            GUI.Label(new Rect(logoRect.x + 16f, logoRect.y + 9f, 150f, 20f), "Bugtopia", logoStyle);
            GUIStyle logoSubStyle = new GUIStyle(GUI.skin.label);
            logoSubStyle.fontSize = 10;
            logoSubStyle.alignment = TextAnchor.MiddleLeft;
            logoSubStyle.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.9f);
            GUI.Label(new Rect(logoRect.x + 16f, logoRect.y + 28f, 150f, 16f), "build " + ModBuildVersion.Display, logoSubStyle);

            // Sidebar navigation.
            string[] navLabels = new string[] { "Self", "Resource Gathering", "Features", "New Features", "Radar", "Teleport", "Bag / Warehouse", "Research", "Settings" };
            int[] navIndices = new int[] { 0, 2, 3, 8, 4, 5, 6, 9, 7 };
            for (int i = 0; i < navLabels.Length; i++)
            {
                this.DrawSidebarTabButton(new Rect(navListRect.x, navListRect.y + (i * 42f), navListRect.width, 37f), navLabels[i], navIndices[i]);
            }

            // Sidebar footer: name only; click hides the menu.
            bool footerHovered = evt != null && footerRect.Contains(evt.mousePosition);
            if (footerHovered)
            {
                this.DrawTintedRoundedBox(new Rect(footerRect.x + 6f, footerRect.y + 5f, footerRect.width - 12f, footerRect.height - 10f), new Color(1f, 1f, 1f, 0.05f));
            }

            GUIStyle footerNameStyle = new GUIStyle(GUI.skin.label);
            footerNameStyle.fontSize = 12;
            footerNameStyle.fontStyle = FontStyle.Bold;
            footerNameStyle.alignment = TextAnchor.MiddleCenter;
            footerNameStyle.normal.textColor = textPrimary;
            GUI.Label(footerRect, "Baboodev", footerNameStyle);
            if (GUI.Button(footerRect, "", GUIStyle.none))
            {
                this.showMenu = false;
            }

            // Header: page title + subtitle, hotkey chip, close.
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 17;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleLeft;
            titleStyle.normal.textColor = textPrimary;
            GUI.Label(new Rect(headerRect.x + 22f, headerRect.y + 8f, headerRect.width - 240f, 24f), this.GetSelectedTabHeader(), titleStyle);
            GUIStyle headerSubStyle = new GUIStyle(GUI.skin.label);
            headerSubStyle.fontSize = 11;
            headerSubStyle.alignment = TextAnchor.MiddleLeft;
            headerSubStyle.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.95f);
            GUI.Label(new Rect(headerRect.x + 22f, headerRect.y + 31f, headerRect.width - 240f, 16f), this.GetSelectedTabSubtitle(), headerSubStyle);

            Rect closeRect = new Rect(headerRect.xMax - 20f - 30f, headerRect.y + 13f, 30f, 30f);

            GUIStyle closeStyle = new GUIStyle(GUI.skin.button);
            closeStyle.fontSize = 14;
            closeStyle.alignment = TextAnchor.MiddleCenter;
            closeStyle.padding = new RectOffset(0, 0, 0, 2);
            if (GUI.Button(closeRect, "×", closeStyle))
            {
                this.showMenu = false;
            }

            // Sub-tabs as a segmented control.
            var subTabs = this.GetActiveTopSubTabs();
            if (subTabs.Count > 0)
            {
                float segPad = 3f;
                float segGap = 3f;
                float[] tabWidths = new float[subTabs.Count];
                float segTotal = segPad * 2f;
                for (int i = 0; i < subTabs.Count; i++)
                {
                    tabWidths[i] = Mathf.Clamp(26f + (this.L(subTabs[i].label).Length * 7.0f), 64f, 138f);
                    segTotal += tabWidths[i] + (i > 0 ? segGap : 0f);
                }

                Rect segRect = new Rect(subTabRect.x, subTabRect.y, Mathf.Min(segTotal, subTabRect.width), subTabRect.height);
                if (this.themeSegContainerStyle != null)
                {
                    GUI.Box(segRect, "", this.themeSegContainerStyle);
                }
                else
                {
                    this.DrawRoundedPanel(segRect, 12f, basePanel, Color.clear, 0f, Color.clear);
                }

                float cursorX = segRect.x + segPad;
                for (int i = 0; i < subTabs.Count; i++)
                {
                    var tab = subTabs[i];
                    Rect tRect = new Rect(cursorX, segRect.y + segPad, tabWidths[i], segRect.height - (segPad * 2f));
                    if (tRect.xMax > segRect.xMax - segPad)
                    {
                        tRect.width = Mathf.Max(30f, segRect.xMax - segPad - tRect.x);
                    }

                    bool active = tab.isActive();
                    bool tabHovered = evt != null && tRect.Contains(evt.mousePosition);
                    if (active)
                    {
                        if (this.themeSegPlateStyle != null)
                        {
                            GUI.Box(tRect, "", this.themeSegPlateStyle);
                        }
                        else
                        {
                            this.DrawRoundedPanel(tRect, 9f, new Color(controlFill.r, controlFill.g, controlFill.b, 0.98f), Color.clear, 0f, Color.clear);
                        }
                    }
                    else if (tabHovered)
                    {
                        this.DrawTintedRoundedBox(tRect, new Color(1f, 1f, 1f, 0.05f));
                    }

                    GUIStyle tabStyle = new GUIStyle(GUI.skin.label);
                    tabStyle.fontSize = 12;
                    tabStyle.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                    tabStyle.alignment = TextAnchor.MiddleCenter;
                    tabStyle.normal.textColor = active ? textPrimary : (tabHovered ? new Color(textPrimary.r, textPrimary.g, textPrimary.b, 0.92f) : navText);

                    if (GUI.Button(tRect, "", GUIStyle.none))
                    {
                        tab.setActive();
                    }

                    GUI.Label(tRect, this.L(tab.label), tabStyle);
                    cursorX += tabWidths[i] + segGap;
                    if (cursorX > segRect.xMax - segPad)
                    {
                        break;
                    }
                }
            }

            // Body: content card + LIVE rail.
            float bodyTop = subTabRect.yMax + 14f;
            float bodyBottom = chromeRect.height - 16f;
            Rect railRect = new Rect(mainRect.xMax - 20f - 240f, bodyTop, 240f, bodyBottom - bodyTop);
            Rect leftContentArea = new Rect(mainRect.x + 20f, bodyTop, railRect.x - 14f - (mainRect.x + 20f), bodyBottom - bodyTop);
            Color leftContentFill = new Color(
                this.uiContentR,
                this.uiContentG,
                this.uiContentB,
                Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f));

            this.DrawExentriSectionPanel(leftContentArea, accent, leftContentFill, hairline);

            Rect tabDrawRect = new Rect(leftContentArea.x + 22f, leftContentArea.y + 14f, leftContentArea.width - 32f, leftContentArea.height - 24f);
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
                else if (this.selectedTab == 9) calculatedHeight = this.DrawResearchTab(contentY);
                else if (this.selectedTab == 7) calculatedHeight = this.DrawSettingsTab(contentY);
                this.tabDrawContentHeight = calculatedHeight;
                GUI.EndScrollView();
            }
            finally
            {
                GUI.EndGroup();
            }

            this.DrawQuickStatusPanel(railRect);
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
            arrowStyle.fontSize = 9;
            arrowStyle.fontStyle = FontStyle.Bold;
            arrowStyle.alignment = TextAnchor.MiddleCenter;
            arrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            GUI.Label(new Rect(rect.x + 10f, rect.y + 1f, rect.width - 32f, rect.height - 2f), this.L(options[selectedIndex]), valueStyle);
            GUI.Label(new Rect(rect.xMax - 24f, rect.y + 1f, 16f, rect.height - 2f), isOpen ? "▲" : "▼", arrowStyle);

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
            string localized = this.L(message);
            if (!string.IsNullOrWhiteSpace(key))
            {
                for (int i = 0; i < this.menuNotifications.Count; i++)
                {
                    HeartopiaComplete.MenuNotification existing = this.menuNotifications[i];
                    if (existing == null || !string.Equals(existing.Key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    existing.Message = localized;
                    existing.Color = color;
                    existing.ExpireAt = now + safeDuration;
                    existing.Duration = safeDuration;
                    existing.Force = force;
                    return;
                }
            }
            else if (this.menuNotifications.Count > 0)
            {
                // Un-keyed duplicate of the most recent toast: refresh it instead of stacking.
                HeartopiaComplete.MenuNotification newest = this.menuNotifications[this.menuNotifications.Count - 1];
                if (newest != null && string.IsNullOrWhiteSpace(newest.Key) && string.Equals(newest.Message, localized, StringComparison.Ordinal))
                {
                    newest.Color = color;
                    newest.ExpireAt = now + safeDuration;
                    newest.Duration = safeDuration;
                    newest.Force = force || newest.Force;
                    return;
                }
            }

            this.menuNotifications.Add(new HeartopiaComplete.MenuNotification
            {
                Key = key,
                Message = localized,
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

            float screenW = this.GetLogicalScreenWidth();
            float screenH = this.GetLogicalScreenHeight();
            float maxWidth = Mathf.Clamp(screenW * 0.44f, 260f, 520f);
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
                float boxWidth = Mathf.Round(Mathf.Clamp(measured.x + 78f, area.width, maxWidth));
                float textWidth = boxWidth - 60f;
                float textHeight = Mathf.Clamp(style.CalcHeight(content, textWidth), 18f, 92f);
                float boxHeight = Mathf.Round(Mathf.Clamp(textHeight + 20f, 42f, 112f));
                boxWidths.Add(boxWidth);
                boxHeights.Add(boxHeight);
                totalHeight += boxHeight;
                if (i < visibleNotifications.Count - 1)
                {
                    totalHeight += 10f;
                }
            }

            float xMargin = 20f;
            float topY = 14f;
            float middleY = Mathf.Max(14f, (screenH - totalHeight) * 0.5f);
            float bottomY = Mathf.Max(14f, screenH - totalHeight - 20f);
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
                        boxX = (screenW - boxWidth) * 0.5f;
                        break;
                    default:
                        boxX = screenW - boxWidth - xMargin + slide;
                        break;
                }

                Rect box = new Rect(boxX, y, boxWidth, boxHeight);
                Rect messageRect = new Rect(box.x + 48f, box.y + 6f, box.width - 60f, box.height - 16f);
                Rect iconRect = new Rect(box.x + 12f, box.y + ((box.height - 26f) * 0.5f) - 1f, 26f, 26f);
                Rect progressFg = new Rect(box.x + 12f, box.yMax - 6f, (box.width - 24f) * remain, 2.5f);

                this.EnsureUiPrimitiveTextures();
                // Card: shadow + ONE baked ring+fill texture. The shadow is INFLATED 1px past
                // the card on every side: the card shape's antialiased outer pixel row is
                // semi-transparent, and over a bright scene it reads as a bright stripe along
                // the top edge unless it blends against this dark halo instead of the game.
                this.DrawRoundedPanel(new Rect(box.x - 1f, box.y - 1f, box.width + 2f, box.height + 3f), 14f, new Color(0f, 0f, 0f, 0.32f * alpha), Color.clear, 0f, Color.clear);
                if (this.themeToastCardStyle != null && this.themeToastCardStyle.normal.background != null)
                {
                    Color prevToastTint = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.Box(box, "", this.themeToastCardStyle);
                    GUI.color = prevToastTint;
                }
                else
                {
                    this.DrawRoundedPanel(box, 13f, new Color(0.082f, 0.11f, 0.153f, 0.96f * alpha), Color.clear, 0f, Color.clear);
                }

                // Icon chip tinted by the notification color, with a status dot. Drawn as one
                // tinted sprite quad — assembled patches seam on translucent fills.
                GUI.color = new Color(item.Color.r, item.Color.g, item.Color.b, 0.16f * alpha);
                GUI.DrawTexture(iconRect, this.uiRoundedRectSprite);
                GUI.color = new Color(item.Color.r, item.Color.g, item.Color.b, 0.95f * alpha);
                GUI.DrawTexture(new Rect(iconRect.x + 9f, iconRect.y + 9f, 8f, 8f), this.uiCircleTexture);
                GUI.color = Color.white;

                if (progressFg.width > 5f)
                {
                    this.DrawCapsule(progressFg, new Color(item.Color.r, item.Color.g, item.Color.b, 0.85f * alpha));
                }

                style.normal.textColor = new Color(0.94f, 0.96f, 1f, alpha);
                GUI.Label(messageRect, item.Message, style);

                y += boxHeight + 10f;
                if (y > screenH - 42f)
                {
                    break;
                }
            }
        }

        private void EnsureThemeStyles()
        {
            // Deferred theme rebuild: only on Layout (start of an event pair) and throttled,
            // so a slider drag never leaves the current frame drawing destroyed textures.
            if (this.uiThemeStylesDirty
                && Event.current != null
                && Event.current.type == EventType.Layout
                && Time.unscaledTime >= this.uiThemeNextRebuildAt)
            {
                this.uiThemeStylesDirty = false;
                this.uiThemeNextRebuildAt = Time.unscaledTime + 0.1f;
                this.InvalidateThemeCache();
            }

            if (this.uiThemePendingSaveAt > 0f && Time.unscaledTime >= this.uiThemePendingSaveAt)
            {
                this.uiThemePendingSaveAt = -1f;
                try
                {
                    this.SaveUiTheme();
                }
                catch
                {
                }
            }

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
                this.themeSegContainerStyle == null ||
                this.themeSegPlateStyle == null ||
                this.themeToastCardStyle == null ||
                this.themeRoundedWhiteStyle == null ||
                this.themeCapsuleWhiteStyle == null ||
                this.themeCapsuleGradientStyle == null ||
                this.themeRoundedRingStyle == null ||
                this.themeBigCardRingStyle == null ||
                this.themeStatusOverlayFrameStyle == null ||
                this.themeBigTintableStyle == null ||
                this.themeSidebarShapeStyle == null ||
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
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.9f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color accent2 = this.GetUiAccentSecondary(accent);
            Color textOnAccent = this.GetUiTextOnAccent(accent);
            Color mainTabText = new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB);
            Color panel = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, this.uiPanelAlpha);
            Color contentBase = new Color(this.uiContentR, this.uiContentG, this.uiContentB, this.uiContentAlpha);
            Color controlFill = this.GetUiControlFill();

            Font themeFont = this.EnsureUiThemeFont();
            if (themeFont != null)
            {
                GUI.skin.font = themeFont;
            }

            float controlAlpha = Mathf.Clamp(this.uiContentAlpha + 0.05f, 0.3f, 1f);
            Texture2D windowBg = this.MakeThemeTexture(new Color(0f, 0f, 0f, 0f));
            Texture2D panelBg = this.MakeRoundedRectTexture(32, 12f, panel, new Color(1f, 1f, 1f, 0.05f), 1.2f);
            Texture2D contentBg = this.MakeRoundedRectTexture(32, 12f, contentBase, new Color(1f, 1f, 1f, 0.05f), 1.2f);
            Texture2D buttonBg = this.MakeRoundedRectTexture(32, 10f,
                new Color(controlFill.r, controlFill.g, controlFill.b, controlAlpha),
                new Color(1f, 1f, 1f, 0.09f), 1.2f);
            Texture2D buttonHover = this.MakeRoundedRectTexture(32, 10f,
                new Color(Mathf.Clamp01(controlFill.r + 0.035f), Mathf.Clamp01(controlFill.g + 0.04f), Mathf.Clamp01(controlFill.b + 0.05f), Mathf.Min(1f, controlAlpha + 0.04f)),
                new Color(1f, 1f, 1f, 0.13f), 1.2f);
            Texture2D buttonActive = this.MakeRoundedRectTexture(32, 10f,
                new Color(controlFill.r * 0.88f, controlFill.g * 0.88f, controlFill.b * 0.88f, controlAlpha),
                new Color(1f, 1f, 1f, 0.08f), 1.2f);
            Texture2D tabActive = this.MakeRoundedRectTexture(32, 10f,
                new Color(accent.r, accent.g, accent.b, 0.13f),
                new Color(accent.r, accent.g, accent.b, 0.2f), 1.2f);
            Texture2D topTabBg = this.MakeRoundedRectTexture(32, 9f,
                new Color(controlFill.r, controlFill.g, controlFill.b, Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f)),
                new Color(1f, 1f, 1f, 0.08f), 1.2f);
            Texture2D topTabActive = this.MakeRoundedRectTexture(32, 9f,
                new Color(accent.r, accent.g, accent.b, 0.18f),
                new Color(accent.r, accent.g, accent.b, 0.35f), 1.2f);
            Texture2D primaryButtonBg = this.MakeRoundedGradientTexture(96, 32, 10f,
                new Color(accent.r, accent.g, accent.b, 0.98f),
                new Color(accent2.r, accent2.g, accent2.b, 0.98f));
            Texture2D primaryButtonHover = this.MakeRoundedGradientTexture(96, 32, 10f,
                Color.Lerp(accent, Color.white, 0.12f),
                Color.Lerp(accent2, Color.white, 0.12f));
            Texture2D dangerButtonBg = this.MakeRoundedRectTexture(32, 10f,
                new Color(1f, 0.42f, 0.5f, 0.10f),
                new Color(1f, 0.42f, 0.5f, 0.24f), 1.2f);
            Texture2D dangerButtonHover = this.MakeRoundedRectTexture(32, 10f,
                new Color(1f, 0.42f, 0.5f, 0.17f),
                new Color(1f, 0.42f, 0.5f, 0.36f), 1.2f);
            Texture2D sliderBg = this.MakeRoundedRectTexture(12, 3f, new Color(0.137f, 0.169f, 0.22f, 1f), Color.clear, 0f);
            Texture2D sliderThumb = this.MakeRoundedRectTexture(16, 8f, Color.white, Color.clear, 0f);
            Texture2D boxBg = this.MakeRoundedRectTexture(32, 12f,
                new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f)),
                new Color(1f, 1f, 1f, 0.06f), 1.2f);
            Texture2D fieldBg = this.MakeRoundedRectTexture(24, 8f,
                new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha + 0.04f, 0.2f, 1f)),
                new Color(1f, 1f, 1f, 0.09f), 1.2f);
            Texture2D fieldFocusBg = this.MakeRoundedRectTexture(24, 8f,
                new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiContentAlpha + 0.08f, 0.24f, 1f)),
                new Color(accent.r, accent.g, accent.b, 0.55f), 1.4f);
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
            this.themePanelStyle.border = new RectOffset(14, 14, 14, 14);
            this.themePanelStyle.padding = new RectOffset(8, 8, 8, 8);
            this.themePanelStyle.normal.textColor = textMuted;

            this.themeContentStyle = new GUIStyle(GUI.skin.box);
            this.themeContentStyle.normal.background = contentBg;
            this.themeContentStyle.onNormal.background = contentBg;
            this.themeContentStyle.border = new RectOffset(14, 14, 14, 14);
            this.themeContentStyle.padding = new RectOffset(8, 8, 8, 8);

            this.themeSidebarButtonStyle = new GUIStyle(GUI.skin.button);
            this.themeSidebarButtonStyle.fixedHeight = 42f;
            this.themeSidebarButtonStyle.margin = new RectOffset(2, 2, 4, 4);
            this.themeSidebarButtonStyle.fontSize = 14;
            this.themeSidebarButtonStyle.fontStyle = FontStyle.Bold;
            this.themeSidebarButtonStyle.alignment = TextAnchor.MiddleLeft;
            this.themeSidebarButtonStyle.padding = new RectOffset(16, 10, 4, 4);
            this.themeSidebarButtonStyle.contentOffset = new Vector2(0f, -1f); // see themePrimaryButtonStyle comment
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
            this.themeTopTabStyle.border = new RectOffset(11, 11, 11, 11);

            this.themeTopTabActiveStyle = new GUIStyle(GUI.skin.box);
            this.themeTopTabActiveStyle.normal.background = topTabActive;
            this.themeTopTabActiveStyle.onNormal.background = topTabActive;
            this.themeTopTabActiveStyle.border = new RectOffset(11, 11, 11, 11);

            Texture2D segContainerBg = this.MakeRoundedRectTexture(32, 12f,
                new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f)),
                new Color(1f, 1f, 1f, 0.05f), 1.2f);
            this.themeSegContainerStyle = new GUIStyle(GUI.skin.box);
            this.themeSegContainerStyle.normal.background = segContainerBg;
            this.themeSegContainerStyle.onNormal.background = segContainerBg;
            this.themeSegContainerStyle.border = new RectOffset(13, 13, 13, 13);

            Texture2D segPlateBg = this.MakeRoundedRectTexture(32, 8f,
                new Color(controlFill.r, controlFill.g, controlFill.b, 0.98f),
                new Color(1f, 1f, 1f, 0.10f), 1.2f);
            this.themeSegPlateStyle = new GUIStyle(GUI.skin.box);
            this.themeSegPlateStyle.normal.background = segPlateBg;
            this.themeSegPlateStyle.onNormal.background = segPlateBg;
            this.themeSegPlateStyle.border = new RectOffset(8, 8, 8, 8);

            // Toast card baked as ONE texture (fill + solid ring): stacking three rounded
            // panels left a visible stripe along the top edge on some backgrounds.
            Texture2D toastCardBg = this.MakeRoundedRectTexture(48, 13f,
                new Color(0.082f, 0.11f, 0.153f, 0.97f),
                new Color(0.165f, 0.205f, 0.27f, 1f), 1.5f);
            this.themeToastCardStyle = new GUIStyle(GUI.skin.box);
            this.themeToastCardStyle.normal.background = toastCardBg;
            this.themeToastCardStyle.onNormal.background = toastCardBg;
            this.themeToastCardStyle.border = new RectOffset(15, 15, 15, 15);

            // Plain white rounded-rect, 9-sliced, tinted per-call via GUI.color — the shared
            // primitive for every translucent hover wash (see DrawTintedRoundedBox). Hand-
            // assembling rect+circle patches for a near-transparent fill visibly seams: the
            // corner groups and edge rects overlap by a hair, and while invisible on opaque
            // fills, a 3-5% alpha fill double-blends at that overlap into a bright stripe.
            Texture2D roundedWhiteBg = this.MakeRoundedRectTexture(32, 9f, Color.white, Color.clear, 0f);
            this.themeRoundedWhiteStyle = new GUIStyle(GUI.skin.box);
            this.themeRoundedWhiteStyle.normal.background = roundedWhiteBg;
            this.themeRoundedWhiteStyle.onNormal.background = roundedWhiteBg;
            this.themeRoundedWhiteStyle.border = new RectOffset(9, 9, 9, 9);

            // Pill-shaped variants for switch tracks / hotkey / status chips (~20-22px tall).
            // White one is tinted per-call via GUI.color (DrawCapsule); the gradient one bakes
            // shape+accent gradient together in a single pass so a toggle-on track / slider
            // fill can never seam (see DrawAccentGradientCapsule). Border is DELIBERATELY less
            // than half the smallest known display height (border-sum=16 vs the 20-22px targets):
            // a 9-slice's stretchable middle collapses to zero when border-sum == the destination
            // size exactly (switchRect is EXACTLY 22 tall) — Unity then renders the WHOLE box as
            // nothing rather than gracefully clamping, which is why every toggle track went
            // invisible (bare knob, no track) after this was first baked at border=11 (22 total,
            // zero margin). Keep at least a few px of margin below the smallest caller's size.
            Texture2D capsuleWhiteBg = this.MakeRoundedRectTexture(24, 8f, Color.white, Color.clear, 0f);
            this.themeCapsuleWhiteStyle = new GUIStyle(GUI.skin.box);
            this.themeCapsuleWhiteStyle.normal.background = capsuleWhiteBg;
            this.themeCapsuleWhiteStyle.onNormal.background = capsuleWhiteBg;
            this.themeCapsuleWhiteStyle.border = new RectOffset(8, 8, 8, 8);

            Texture2D capsuleGradientBg = this.MakeRoundedGradientTexture(64, 24, 8f, accent, accent2);
            this.themeCapsuleGradientStyle = new GUIStyle(GUI.skin.box);
            this.themeCapsuleGradientStyle.normal.background = capsuleGradientBg;
            this.themeCapsuleGradientStyle.onNormal.background = capsuleGradientBg;
            this.themeCapsuleGradientStyle.border = new RectOffset(8, 8, 8, 8);

            // Ring-only (clear fill), tinted per-call via GUI.color — DrawCardOutline's shared
            // primitive. The old implementation drew 4 straight hairlines forming a hard
            // rectangle; every box it outlines (dropdowns, cards) now has ROUNDED corners baked
            // into its own background texture, so the straight-edged outline's corners poked
            // out past the rounded shape beneath it — reads as a mismatched rectangular frame.
            // Border kept comfortably below common dropdown/row heights (many cluster around
            // 26-30px) — see the capsule-style comment above for why border-sum too close to
            // the destination size is dangerous (degenerate 9-slice → the whole box vanishes).
            Texture2D roundedRingBg = this.MakeRoundedRectTexture(32, 7f, Color.clear, Color.white, 1.4f);
            this.themeRoundedRingStyle = new GUIStyle(GUI.skin.box);
            this.themeRoundedRingStyle.normal.background = roundedRingBg;
            this.themeRoundedRingStyle.onNormal.background = roundedRingBg;
            this.themeRoundedRingStyle.border = new RectOffset(7, 7, 7, 7);

            // Same ring-only bake at the BIGGER radius used by DrawExentriSectionPanel's outer
            // ring (every tab's main card + the LIVE rail). Needs its own texture, not a reuse
            // of themeRoundedRingStyle: pairing a smaller-radius ring with the panel's
            // radius-13 inset fill reproduces the corner-leak bug (sharper ring corner shows
            // outside the more-rounded fill beneath it).
            Texture2D bigCardRingBg = this.MakeRoundedRectTexture(32, 14f, Color.clear, Color.white, 1.4f);
            this.themeBigCardRingStyle = new GUIStyle(GUI.skin.box);
            this.themeBigCardRingStyle.normal.background = bigCardRingBg;
            this.themeBigCardRingStyle.onNormal.background = bigCardRingBg;
            this.themeBigCardRingStyle.border = new RectOffset(15, 15, 15, 15);

            // DrawStatusOverlay's floating frame — fixed literal colors (not theme-derived),
            // but still rebaked/invalidated on every theme edit like everything else here:
            // InvalidateThemeCache destroys every texture in themeTextures, so leaving this
            // one un-cleared would render a destroyed (solid white) background on the next
            // frame after any theme change (see the deferred-rebuild white-flash fix).
            Texture2D statusOverlayFrameBg = this.MakeRoundedRectTexture(32, 10f,
                new Color(0.08f, 0.10f, 0.13f, 0.94f),
                new Color(0.165f, 0.205f, 0.27f, 0.9f), 1.3f);
            this.themeStatusOverlayFrameStyle = new GUIStyle(GUI.skin.box);
            this.themeStatusOverlayFrameStyle.normal.background = statusOverlayFrameBg;
            this.themeStatusOverlayFrameStyle.onNormal.background = statusOverlayFrameBg;
            this.themeStatusOverlayFrameStyle.border = new RectOffset(13, 13, 13, 13);

            // White, tinted per-call via GUI.color, radius matches the main window chrome
            // (windowBase/sidebar fills, radius 15) — those are the single most prominent,
            // always-visible, EVERY-FRAME translucent multi-piece fills in the whole app
            // (their alpha is the live-adjustable Window/Panel Alpha sliders, down to 0.15),
            // so unlike the small hover washes they can't lean on "usually near-opaque anyway".
            Texture2D bigTintableBg = this.MakeRoundedRectTexture(36, 15f, Color.white, Color.clear, 0f);
            this.themeBigTintableStyle = new GUIStyle(GUI.skin.box);
            this.themeBigTintableStyle.normal.background = bigTintableBg;
            this.themeBigTintableStyle.onNormal.background = bigTintableBg;
            this.themeBigTintableStyle.border = new RectOffset(16, 16, 16, 16);

            // Sidebar fill shape: rounded left corners, square right edge, one bake, one draw —
            // see MakeLeftRoundedRectTexture for why this replaced a "round then patch" approach.
            // border.right = 0: the right portion of the source is uniformly opaque with no
            // vertical curvature to protect, so it can stretch freely with no distortion.
            Texture2D sidebarShapeBg = this.MakeLeftRoundedRectTexture(36, 15f);
            this.themeSidebarShapeStyle = new GUIStyle(GUI.skin.box);
            this.themeSidebarShapeStyle.normal.background = sidebarShapeBg;
            this.themeSidebarShapeStyle.onNormal.background = sidebarShapeBg;
            this.themeSidebarShapeStyle.border = new RectOffset(16, 0, 16, 16);

            this.themePrimaryButtonStyle = new GUIStyle(GUI.skin.button);
            this.themePrimaryButtonStyle.normal.background = primaryButtonBg;
            this.themePrimaryButtonStyle.hover.background = primaryButtonHover;
            this.themePrimaryButtonStyle.active.background = primaryButtonHover;
            this.themePrimaryButtonStyle.border = new RectOffset(12, 12, 12, 12);
            this.themePrimaryButtonStyle.fontStyle = FontStyle.Bold;
            this.themePrimaryButtonStyle.fontSize = 13;
            // Segoe UI's reported line-height sits well above its visible cap-height (a known
            // Windows-system-font quirk under legacy IMGUI, which centers on the FULL line box,
            // not the glyph ink) — MiddleCenter text renders noticeably above true center, with
            // extra empty space below. Nudge the content down to compensate.
            this.themePrimaryButtonStyle.contentOffset = new Vector2(0f, -1f);
            this.themePrimaryButtonStyle.normal.textColor = textOnAccent;
            this.themePrimaryButtonStyle.hover.textColor = textOnAccent;
            this.themePrimaryButtonStyle.active.textColor = textOnAccent;

            this.themeDangerButtonStyle = new GUIStyle(this.themePrimaryButtonStyle);
            this.themeDangerButtonStyle.normal.background = dangerButtonBg;
            this.themeDangerButtonStyle.hover.background = dangerButtonHover;
            this.themeDangerButtonStyle.active.background = dangerButtonHover;
            Color dangerText = new Color(1f, 0.56f, 0.63f);
            this.themeDangerButtonStyle.normal.textColor = dangerText;
            this.themeDangerButtonStyle.hover.textColor = new Color(1f, 0.68f, 0.74f);
            this.themeDangerButtonStyle.active.textColor = dangerText;

            GUI.skin.label.normal.textColor = textPrimary;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = buttonBg;
            buttonStyle.hover.background = buttonHover;
            buttonStyle.active.background = buttonActive;
            buttonStyle.border = new RectOffset(12, 12, 12, 12);
            buttonStyle.normal.textColor = textPrimary;
            buttonStyle.hover.textColor = textPrimary;
            buttonStyle.active.textColor = accent;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.contentOffset = new Vector2(0f, -1f); // see themePrimaryButtonStyle comment
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
            boxStyle.border = new RectOffset(14, 14, 14, 14);
            boxStyle.normal.textColor = textPrimary;
            GUI.skin.box = boxStyle;

            GUIStyle sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            sliderStyle.normal.background = sliderBg;
            sliderStyle.border = new RectOffset(4, 4, 4, 4);
            sliderStyle.fixedHeight = 6f;
            sliderStyle.margin = new RectOffset(4, 4, 10, 10);
            GUI.skin.horizontalSlider = sliderStyle;

            GUIStyle sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
            sliderThumbStyle.normal.background = sliderThumb;
            sliderThumbStyle.hover.background = sliderThumb;
            sliderThumbStyle.active.background = sliderThumb;
            sliderThumbStyle.fixedWidth = 14f;
            sliderThumbStyle.fixedHeight = 14f;
            GUI.skin.horizontalSliderThumb = sliderThumbStyle;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.normal.background = fieldBg;
            textFieldStyle.focused.background = fieldFocusBg;
            textFieldStyle.hover.background = fieldBg;
            textFieldStyle.active.background = fieldBg;
            textFieldStyle.border = new RectOffset(10, 10, 10, 10);
            textFieldStyle.normal.textColor = textPrimary;
            textFieldStyle.focused.textColor = Color.white;
            textFieldStyle.padding = new RectOffset(8, 8, 4, 4);
            GUI.skin.textField = textFieldStyle;

            try
            {
                GUI.skin.settings.cursorColor = accent;
                GUI.skin.settings.selectionColor = new Color(accent.r, accent.g, accent.b, 0.35f);
            }
            catch
            {
                // Skin settings are cosmetic; ignore if the interop surface lacks them.
            }

            GUIStyle scrollStyle = new GUIStyle(GUI.skin.verticalScrollbar);
            scrollStyle.normal.background = this.MakeThemeTexture(new Color(1f, 1f, 1f, 0.04f));
            scrollStyle.fixedWidth = 6f;
            GUI.skin.verticalScrollbar = scrollStyle;

            GUIStyle scrollThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb);
            scrollThumbStyle.normal.background = this.MakeThemeTexture(new Color(accent.r, accent.g, accent.b, 0.45f));
            scrollThumbStyle.hover.background = this.MakeThemeTexture(new Color(accent.r, accent.g, accent.b, 0.7f));
            scrollThumbStyle.active.background = cursorBg;
            scrollThumbStyle.fixedWidth = 6f;
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
            // Fixed white wash regardless of the Panel/Content Alpha sliders: dark near-black
            // colors compress perceived contrast, so this row-vs-card highlight read fine at low
            // panel opacity (where the card itself is lighter/more see-through) but nearly
            // vanished once Panel Alpha was pushed to 1.0 (fully saturated dark card, same 5%
            // white wash on top reads far weaker against it). Bumped so the rows stay legible at
            // any Panel Alpha setting.
            Color rowFill = new Color(1f, 1f, 1f, 0.09f);

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
                this.DrawTintedRoundedBox(rowRect, rowFill);
                this.DrawCardOutline(rowRect, 1f);
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
                this.DrawTintedRoundedBox(previewCurrent, rowFill);
                this.DrawCardOutline(previewCurrent, 1f);
                this.DrawTintedRoundedBox(previewOriginal, rowFill);
                this.DrawCardOutline(previewOriginal, 1f);
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

            Rect transparencyPanel = new Rect(left, (float)num, contentWidth, 158f);
            this.DrawExentriSectionPanel(transparencyPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(transparencyPanel.x + 14f, transparencyPanel.y + 12f, transparencyPanel.width - 28f, 18f), "TRANSPARENCY", sectionStyle);
            num += 42;

            // Outermost layer (the window slab itself, behind the sidebar/content panels below)
            // — previously saved/loaded but had no slider, so the menu could never actually look
            // transparent: Panel/Content Alpha only control the layers ON TOP of this one, and it
            // defaulted to a near-opaque 0.96 with no way to lower it from the UI.
            Rect windowAlphaRow = new Rect(transparencyPanel.x + 14f, (float)num, transparencyPanel.width - 28f, 24f);
            GUI.Label(new Rect(windowAlphaRow.x, windowAlphaRow.y, 150f, 20f), "Window Alpha", rowLabelStyle);
            float newWindowA = this.DrawAccentSlider(new Rect(windowAlphaRow.x + 164f, windowAlphaRow.y + 1f, windowAlphaRow.width - 226f, 20f), this.uiWindowAlpha, 0.15f, 1f);
            GUI.Label(new Rect(windowAlphaRow.xMax - 52f, windowAlphaRow.y, 52f, 20f), this.uiWindowAlpha.ToString("F2"), rowValueStyle);
            if (Math.Abs(newWindowA - this.uiWindowAlpha) > 0.001f) { this.uiWindowAlpha = newWindowA; changed = true; }
            num += 34;

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
                this.uiWindowAlpha = Mathf.Clamp(this.uiWindowAlpha, 0.15f, 1f);
                this.uiPanelAlpha = Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f);
                this.uiContentAlpha = Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f);
                this.uiScale = this.NormalizeUiScale(this.uiScale);
                // Deferred rebuild + save: invalidating mid-event destroys the theme textures
                // while the rest of the frame still draws with them (destroyed textures render
                // WHITE), and slider drags would otherwise rebuild textures + hit disk per tick.
                this.uiThemeStylesDirty = true;
                this.uiThemePendingSaveAt = Time.unscaledTime + 0.6f;
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
                this.uiAccentR = 0.31f;
                this.uiAccentG = 0.78f;
                this.uiAccentB = 1.00f;
                this.uiTextR = 0.93f;
                this.uiTextG = 0.95f;
                this.uiTextB = 0.976f;
                this.uiMainTabTextR = 0.545f;
                this.uiMainTabTextG = 0.584f;
                this.uiMainTabTextB = 0.655f;
                this.uiSubTabTextR = 0.357f;
                this.uiSubTabTextG = 0.392f;
                this.uiSubTabTextB = 0.471f;
                this.uiWindowR = 0.039f;
                this.uiWindowG = 0.051f;
                this.uiWindowB = 0.071f;
                this.uiPanelR = 0.059f;
                this.uiPanelG = 0.075f;
                this.uiPanelB = 0.106f;
                this.uiContentR = 0.078f;
                this.uiContentG = 0.102f;
                this.uiContentB = 0.141f;
                this.uiWindowAlpha = 0.96f;
                this.uiPanelAlpha = 0.96f;
                this.uiContentAlpha = 0.94f;
                this.uiScale = 1f;
                this.uiThemeHexInput = this.ColorToHex(new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB));
                this.KeepMenuWindowOnScreen(this.GetUiScale());
                this.uiThemeStylesDirty = true;
                this.uiThemeNextRebuildAt = 0f;
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
