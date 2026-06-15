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
        private float GetUiScale()
        {
            float requested = this.NormalizeUiScale(this.uiScale > 0f ? this.uiScale : 1f);
            float baseWidth = this.targetWindowWidth > 1f ? this.targetWindowWidth : 1180f;
            float baseHeight = this.targetWindowHeight > 1f ? this.targetWindowHeight : 720f;
            float fitScale = Mathf.Min((float)Screen.width / baseWidth, (float)Screen.height / baseHeight);
            fitScale = Mathf.Clamp(fitScale, UiScaleMin, UiScaleMax);
            return Mathf.Min(requested, fitScale);
        }

        private float NormalizeUiScale(float scale)
        {
            scale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
            return Mathf.Clamp(Mathf.Round(scale / UiScaleStep) * UiScaleStep, UiScaleMin, UiScaleMax);
        }

        private void DrawCardOutline(Rect rect, float thickness = 1f)
        {
            Color prev = GUI.color;
            Color edge = new Color(1f, 1f, 1f, Mathf.Clamp(0.06f + (this.uiPanelAlpha * 0.1f), 0.08f, 0.18f));
            Color accentTop = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, Mathf.Clamp(0.28f + (this.uiPanelAlpha * 0.28f), 0.25f, 0.6f));
            Color shadow = new Color(0f, 0f, 0f, 0.28f);

            GUI.color = edge;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = accentTop;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.color = shadow;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.yMax - 1f, rect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawRoundedPanel(Rect rect, float radius, Color fill, Color border, float borderWidth, Color topAccent)
        {
            this.EnsureUiPrimitiveTextures();

            float corner = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (corner <= 0.5f)
            {
                GUI.color = fill;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            else
            {
                float diameter = corner * 2f;
                Rect center = new Rect(rect.x + corner, rect.y + corner, rect.width - diameter, rect.height - diameter);
                Rect top = new Rect(rect.x + corner, rect.y, rect.width - diameter, corner);
                Rect bottom = new Rect(rect.x + corner, rect.yMax - corner, rect.width - diameter, corner);
                Rect left = new Rect(rect.x, rect.y + corner, corner, rect.height - diameter);
                Rect right = new Rect(rect.xMax - corner, rect.y + corner, corner, rect.height - diameter);
                Rect topLeft = new Rect(rect.x, rect.y, corner, corner);
                Rect topRight = new Rect(rect.xMax - corner, rect.y, corner, corner);
                Rect bottomLeft = new Rect(rect.x, rect.yMax - corner, corner, corner);
                Rect bottomRight = new Rect(rect.xMax - corner, rect.yMax - corner, corner, corner);

                GUI.color = fill;
                GUI.DrawTexture(center, Texture2D.whiteTexture);
                GUI.DrawTexture(top, Texture2D.whiteTexture);
                GUI.DrawTexture(bottom, Texture2D.whiteTexture);
                GUI.DrawTexture(left, Texture2D.whiteTexture);
                GUI.DrawTexture(right, Texture2D.whiteTexture);
                GUI.BeginGroup(topLeft);
                GUI.DrawTexture(new Rect(0f, 0f, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(topRight);
                GUI.DrawTexture(new Rect(-corner, 0f, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(bottomLeft);
                GUI.DrawTexture(new Rect(0f, -corner, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(bottomRight);
                GUI.DrawTexture(new Rect(-corner, -corner, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.color = Color.white;
            }

            if (borderWidth > 0f)
            {
                GUI.color = border.a > 0f ? border : new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderWidth), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderWidth, rect.width, borderWidth), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, borderWidth, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - borderWidth, rect.y, borderWidth, rect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                if (topAccent.a > 0f)
                {
                    GUI.color = topAccent;
                    GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1.5f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
        }

        private void DrawExentriSectionPanel(Rect rect, Color accent, Color fill, Color softLine)
        {
            this.DrawRoundedPanel(rect, 10f, fill, softLine, 1f, Color.clear);

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1.5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private bool DrawPrimaryActionButton(Rect rect, string label)
        {
            return GUI.Button(rect, this.L(label), this.themePrimaryButtonStyle ?? GUI.skin.button);
        }

        private bool DrawDangerActionButton(Rect rect, string label)
        {
            return GUI.Button(rect, this.L(label), this.themeDangerButtonStyle ?? GUI.skin.button);
        }

        private bool DrawSwitchToggle(Rect rect, bool value, string label)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 60f, rect.height), this.L(label), labelStyle);

            this.EnsureUiPrimitiveTextures();
            Rect switchRect = new Rect(rect.xMax - 46f, rect.y + Mathf.Max(0f, (rect.height - 20f) * 0.5f), 40f, 20f);
            Rect trackRect = new Rect(switchRect.x, switchRect.y + 1f, switchRect.width, 18f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.98f);
            Color offTrack = new Color(0.16f, 0.16f, 0.22f, 0.98f);
            Color onTrack = new Color(accent.r * 0.92f, accent.g * 0.72f, accent.b, 1f);

            if (value)
            {
                Rect glowRect = new Rect(trackRect.x - 2f, trackRect.y - 2f, trackRect.width + 4f, trackRect.height + 4f);
                this.DrawCapsule(glowRect, new Color(accent.r, accent.g, accent.b, 0.28f));
            }

            this.DrawCapsule(trackRect, value ? onTrack : offTrack);

            float knobDiameter = 14f;
            float knobX = value ? (trackRect.xMax - knobDiameter - 2f) : (trackRect.x + 2f);
            Rect knobRect = new Rect(knobX, trackRect.y + (trackRect.height - knobDiameter) * 0.5f, knobDiameter, knobDiameter);
            Color knobColor = value ? new Color(0.96f, 0.97f, 1f, 1f) : new Color(0.68f, 0.7f, 0.78f, 1f);
            GUI.color = knobColor;
            GUI.DrawTexture(knobRect, this.uiCircleTexture);
            GUI.color = Color.white;

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                value = !value;
                e.Use();
            }

            return value;
        }

        private float GetSwitchToggleHeight(float width, string label, float minHeight = 20f)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.wordWrap = true;
            float labelWidth = Mathf.Max(60f, width - 60f);
            float labelHeight = labelStyle.CalcHeight(new GUIContent(this.L(label)), labelWidth);
            return Mathf.Max(minHeight, labelHeight);
        }

        private bool DrawWrappedSwitchToggle(Rect rect, bool value, string label, float minHeight = 20f)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.wordWrap = true;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            float rowHeight = Mathf.Max(rect.height, this.GetSwitchToggleHeight(rect.width, label, minHeight));
            float labelWidth = Mathf.Max(60f, rect.width - 60f);
            GUI.Label(new Rect(rect.x, rect.y, labelWidth, rowHeight), this.L(label), labelStyle);

            this.EnsureUiPrimitiveTextures();
            Rect switchRect = new Rect(rect.xMax - 46f, rect.y + Mathf.Max(0f, (rowHeight - 20f) * 0.5f), 40f, 20f);
            Rect trackRect = new Rect(switchRect.x, switchRect.y + 1f, switchRect.width, 18f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.98f);
            Color offTrack = new Color(0.16f, 0.16f, 0.22f, 0.98f);
            Color onTrack = new Color(accent.r * 0.92f, accent.g * 0.72f, accent.b, 1f);

            if (value)
            {
                Rect glowRect = new Rect(trackRect.x - 2f, trackRect.y - 2f, trackRect.width + 4f, trackRect.height + 4f);
                this.DrawCapsule(glowRect, new Color(accent.r, accent.g, accent.b, 0.28f));
            }

            this.DrawCapsule(trackRect, value ? onTrack : offTrack);

            float knobDiameter = 14f;
            float knobX = value ? (trackRect.xMax - knobDiameter - 2f) : (trackRect.x + 2f);
            Rect knobRect = new Rect(knobX, trackRect.y + (trackRect.height - knobDiameter) * 0.5f, knobDiameter, knobDiameter);
            Color knobColor = value ? new Color(0.96f, 0.97f, 1f, 1f) : new Color(0.68f, 0.7f, 0.78f, 1f);
            GUI.color = knobColor;
            GUI.DrawTexture(knobRect, this.uiCircleTexture);
            GUI.color = Color.white;

            Event e = Event.current;
            Rect hitRect = new Rect(rect.x, rect.y, rect.width, rowHeight);
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && hitRect.Contains(e.mousePosition))
            {
                value = !value;
                e.Use();
            }

            return value;
        }

        private float ReadAccentSliderMouseValue(Rect rect, float mouseX, float min, float max, bool integerSteps)
        {
            float tInput = Mathf.Clamp01((mouseX - rect.x) / Mathf.Max(1f, rect.width));
            if (!integerSteps || Mathf.Approximately(min, max))
            {
                return Mathf.Lerp(min, max, tInput);
            }

            int iMin = Mathf.RoundToInt(min);
            int iMax = Mathf.RoundToInt(max);
            int range = Mathf.Max(0, iMax - iMin);
            int stepped = iMin + Mathf.Clamp(Mathf.FloorToInt(tInput * (range + 1)), 0, range);
            return stepped;
        }

        private void DrawAccentSliderVisual(Rect rect, float value, float min, float max)
        {
            float t = Mathf.InverseLerp(min, max, value);
            float lineY = rect.y + rect.height * 0.5f - 2.5f;
            Rect bgRect = new Rect(rect.x, lineY, rect.width, 5f);
            Rect fillRect = new Rect(rect.x, lineY, Mathf.Max(5f, rect.width * t), 5f);
            float thumbX = Mathf.Clamp(rect.x + rect.width * t, rect.x + 6f, rect.xMax - 6f);
            Rect thumbGlowRect = new Rect(thumbX - 8f, rect.y + rect.height * 0.5f - 8f, 16f, 16f);
            Rect thumbRect = new Rect(thumbX - 6f, rect.y + rect.height * 0.5f - 6f, 12f, 12f);

            this.DrawCapsule(bgRect, new Color(0.18f, 0.19f, 0.24f, 0.92f));
            this.DrawCapsule(fillRect, new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.94f));
            GUI.color = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.28f);
            GUI.DrawTexture(thumbGlowRect, this.uiCircleTexture);
            GUI.color = new Color(0.95f, 0.97f, 1f, 1f);
            GUI.DrawTexture(thumbRect, this.uiCircleTexture);
            GUI.color = Color.white;
        }

        private float DrawAccentSlider(Rect rect, float value, float min, float max, bool integerSteps = false)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            if (e != null && e.button == 0)
            {
                if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    value = this.ReadAccentSliderMouseValue(rect, e.mousePosition.x, min, max, integerSteps);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    value = this.ReadAccentSliderMouseValue(rect, e.mousePosition.x, min, max, integerSteps);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }

            value = Mathf.Clamp(value, min, max);
            this.DrawAccentSliderVisual(rect, value, min, max);
            return value;
        }

        private void EnsureUiPrimitiveTextures()
        {
            if (this.uiCircleTexture != null)
            {
                return;
            }

            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float radius = (size - 1f) * 0.5f;
            Vector2 c = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float a = d <= radius ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            this.uiCircleTexture = tex;
            this.themeTextures.Add(tex);
        }

        private void DrawCapsule(Rect rect, Color color)
        {
            this.EnsureUiPrimitiveTextures();
            float r = rect.height * 0.5f;
            Rect mid = new Rect(rect.x + r, rect.y, rect.width - 2f * r, rect.height);
            Rect left = new Rect(rect.x, rect.y, rect.height, rect.height);
            Rect right = new Rect(rect.xMax - rect.height, rect.y, rect.height, rect.height);
            GUI.color = color;
            GUI.DrawTexture(mid, Texture2D.whiteTexture);
            GUI.DrawTexture(left, this.uiCircleTexture);
            GUI.DrawTexture(right, this.uiCircleTexture);
            GUI.color = Color.white;
        }

        private void DrawQuickStatusPanel(Rect panelRect)
        {
            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 14;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle value = new GUIStyle(GUI.skin.label);
            value.fontSize = 13;
            value.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);

            GUIStyle none = new GUIStyle(GUI.skin.label);
            none.fontSize = 12;
            none.fontStyle = FontStyle.Italic;
            none.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.55f);

            float x = panelRect.x + 14f;
            float w = panelRect.width - 28f;
            float y = panelRect.y + 46f;
            bool anyActive = false;

            // Helper to draw a feature row
            void Row(string label, string detail)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L(label), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(detail), value);
                y += 26f;
                anyActive = true;
            }

            if (this.isRadarActive)
                Row("Radar", "Active");

            if (this.autoFarmActive)
                Row("Foraging", this.GetForagingStatusDisplayText(false));

            if (this.autoCookEnabled)

            if (this.gameSpeed != 1.0f)
                Row("Speed", $"{this.gameSpeed:F1}x");

            if (this.noclipEnabled)
                Row("Noclip", "Active");

            if (this.bypassOverlapEnabled)
                Row("Bypass Overlap", "Active");

            if (this.birdVacuumEnabled)
                Row("Bird Vacuum", "Active");

            if (this.autoSnowEnabled)
                Row("Auto Snow", "Active");

            if (this.autoJoinFriendEnabled)
                Row("Auto Join Friend", "Active");

            if (InsectNetFarm.IsEnabled)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L("Insect Farm"), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(InsectNetFarm.GetLastStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Tool") + ": " + this.L(InsectNetFarm.GetLastToolStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Caught") + ": " + InsectNetFarm.GetSessionCatchCount().ToString(), value);
                y += 26f;
                anyActive = true;
            }

            if (BirdNetFarm.IsEnabled)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L("Bird Farm"), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(BirdNetFarm.GetLastStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Tool") + ": " + this.L(BirdNetFarm.GetLastToolStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Caught") + ": " + BirdNetFarm.GetSessionCatchCount().ToString(), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Scared") + ": " + BirdNetFarm.GetSessionScaredCount().ToString(), value);
                y += 26f;
                anyActive = true;
            }

            if (!anyActive)
                GUI.Label(new Rect(x, y, w, 24f), this.L("No active features"), none);
        }

        private float GetStatusOverlayHeight()
        {
            int lineCount = 0;
            if (this.isRadarActive) lineCount++;
            if (this.gameSpeed != 1.0f) lineCount++;
            if (this.noclipEnabled) lineCount++;
            if (this.bypassOverlapEnabled) lineCount++;
            if (this.birdVacuumEnabled) lineCount++;
            if (this.autoFarmActive) lineCount += 2;
            else if (this.auraFarmEnabled) lineCount++;
            if (InsectNetFarm.IsEnabled) lineCount += 4;
            if (BirdNetFarm.IsEnabled) lineCount += 5;
            if (AutoFishingFarm.IsEnabled) lineCount += 4;
            if (this.autoSnowEnabled) lineCount++;
            if (this.autoJoinFriendEnabled) lineCount++;

            if (lineCount == 0)
            {
                return 124f;
            }

            return Mathf.Clamp(112f + (lineCount * 24f), 154f, 448f);
        }

        private float GetStatusOverlayWidth()
        {
            int maxTextLength = 0;

            void Consider(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                maxTextLength = Math.Max(maxTextLength, text.Trim().Length);
            }

            if (this.isRadarActive) Consider("Active");
            if (this.gameSpeed != 1.0f) Consider(string.Format("{0:F1}x", this.gameSpeed));
            if (this.noclipEnabled) Consider("Active");
            if (this.bypassOverlapEnabled) Consider("Active");
            if (this.birdVacuumEnabled) Consider("Active");

            if (this.autoFarmActive)
            {
                Consider(this.GetForagingModeLabel());
                Consider(this.GetForagingStatusDisplayText(false));
            }
            else if (this.auraFarmEnabled)
            {
                Consider("Running");
            }

            if (InsectNetFarm.IsEnabled)
            {
                Consider(InsectNetFarm.GetLastStatus());
                Consider(InsectNetFarm.GetLastToolStatus());
                Consider(InsectNetFarm.GetSessionCatchCount().ToString());
            }
            if (BirdNetFarm.IsEnabled)
            {
                Consider(BirdNetFarm.GetLastStatus());
                Consider(BirdNetFarm.GetLastToolStatus());
                Consider(BirdNetFarm.GetSessionCatchCount().ToString());
                Consider(BirdNetFarm.GetSessionScaredCount().ToString());
            }
            if (AutoFishingFarm.IsEnabled)
            {
                Consider(AutoFishingFarm.GetLastStatus());
                Consider(AutoFishingFarm.GetLastToolStatus());
                Consider(AutoFishingFarm.GetLastTargetStatus());
            }
            if (BirdNetFarm.IsEnabled) Consider("Running");
            if (this.autoSnowEnabled) Consider("Active");
            if (this.autoJoinFriendEnabled) Consider("Active");

            if (maxTextLength <= 0)
            {
                return 228f;
            }

            float width = 228f + Mathf.Max(0f, (maxTextLength - 14) * 5.6f);
            return Mathf.Clamp(width, 228f, Mathf.Min(420f, Screen.width - 16f));
        }

        private void DrawStatusOverlay(Rect panelRect)
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle detailLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle detailValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleRight, wordWrap = false };
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle footerLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle footerValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };

            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.98f);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.88f);
            Color separator = new Color(1f, 1f, 1f, 0.06f);
            Color overlayFill = new Color(0.08f, 0.10f, 0.13f, 0.94f);
            Color overlayHeaderFill = new Color(0.10f, 0.12f, 0.17f, 0.98f);
            Color overlayFooterFill = new Color(0.07f, 0.08f, 0.11f, 0.98f);
            Color overlayBorder = new Color(1f, 1f, 1f, 0.07f);
            Color badgeFill = new Color(this.uiAccentR * 0.42f, this.uiAccentG * 0.42f, this.uiAccentB * 0.58f, 0.98f);
            Color badgeIdleFill = new Color(0.17f, 0.20f, 0.27f, 0.98f);

            headerStyle.normal.textColor = textPrimary;
            badgeStyle.normal.textColor = textPrimary;
            sectionStyle.normal.textColor = textPrimary;
            detailLabelStyle.normal.textColor = textMuted;
            detailValueStyle.normal.textColor = textPrimary;
            hintStyle.normal.textColor = textMuted;
            footerLabelStyle.normal.textColor = textMuted;
            footerValueStyle.normal.textColor = textPrimary;

            float x = panelRect.x;
            float y = panelRect.y;
            float w = panelRect.width;

            Color prevColor = GUI.color;

            int activeRows = 0;
            if (this.isRadarActive) activeRows++;
            if (this.gameSpeed != 1.0f) activeRows++;
            if (this.noclipEnabled) activeRows++;
            if (this.bypassOverlapEnabled) activeRows++;
            if (this.birdVacuumEnabled) activeRows++;
            if (this.autoFarmActive) activeRows++;
            else if (this.auraFarmEnabled) activeRows++;
            if (InsectNetFarm.IsEnabled) activeRows++;
            if (BirdNetFarm.IsEnabled) activeRows++;
            if (AutoFishingFarm.IsEnabled) activeRows++;
            if (this.autoSnowEnabled) activeRows++;
            if (this.autoJoinFriendEnabled) activeRows++;

            bool hasActiveSystems = activeRows > 0;
            Rect frameRect = new Rect(x - 6f, y - 6f, w + 12f, panelRect.height + 12f);
            float currentFps = Time.unscaledDeltaTime > 0.0001f ? (1f / Time.unscaledDeltaTime) : this.statusOverlaySmoothedFps;
            if (this.statusOverlaySmoothedFps <= 0f)
            {
                this.statusOverlaySmoothedFps = currentFps;
            }
            else if (currentFps > 0f)
            {
                this.statusOverlaySmoothedFps = Mathf.Lerp(this.statusOverlaySmoothedFps, currentFps, 0.05f);
            }
            if (Time.unscaledTime >= this.nextStatusOverlayFpsRefreshAt)
            {
                this.statusOverlayDisplayedFps = this.statusOverlaySmoothedFps;
                this.nextStatusOverlayFpsRefreshAt = Time.unscaledTime + 0.35f;
            }
            string fpsText = this.statusOverlayDisplayedFps > 0f ? Mathf.RoundToInt(this.statusOverlayDisplayedFps).ToString() : "--";

            this.DrawRoundedPanel(frameRect, 10f, overlayFill, overlayBorder, 1f, Color.clear);
            Rect headerRect = new Rect(frameRect.x + 1f, frameRect.y + 1f, frameRect.width - 2f, 34f);
            Rect footerRect = new Rect(frameRect.x + 1f, frameRect.yMax - 33f, frameRect.width - 2f, 32f);
            Rect bodyRect = new Rect(frameRect.x + 10f, headerRect.yMax + 8f, frameRect.width - 20f, footerRect.y - headerRect.yMax - 14f);

            this.DrawRoundedPanel(headerRect, 10f, overlayHeaderFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(bodyRect.x, bodyRect.y - 4f, bodyRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(headerRect.x + 12f, headerRect.y + 7f, 116f, 18f), this.L("Helper Status"), headerStyle);

            Rect badgeRect = new Rect(headerRect.xMax - 82f, headerRect.y + 6f, 70f, 20f);
            this.DrawCapsule(badgeRect, hasActiveSystems ? badgeFill : badgeIdleFill);
            GUI.Label(badgeRect, hasActiveSystems ? this.L("ACTIVE") : this.L("STANDBY"), badgeStyle);

            float rowY = bodyRect.y;
            Action drawDivider = () =>
            {
                GUI.color = separator;
                GUI.DrawTexture(new Rect(bodyRect.x + 2f, rowY - 2f, bodyRect.width - 4f, 1f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            };
            Action<string, string> drawFeature = (label, value) =>
            {
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 18f);
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 1f, 112f, 16f), this.L(label), sectionStyle);
                GUI.Label(new Rect(rowRect.x + 120f, rowRect.y + 1f, rowRect.width - 128f, 16f), this.L(value), detailValueStyle);
                rowY += 20f;
            };
            Action<string, string> drawDetail = (label, value) =>
            {
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 16f);
                GUI.Label(new Rect(rowRect.x + 18f, rowRect.y, 92f, 16f), this.L(label), detailLabelStyle);
                GUI.Label(new Rect(rowRect.x + 110f, rowRect.y, rowRect.width - 118f, 16f), this.L(value), detailValueStyle);
                rowY += 18f;
            };
            Action finishBlock = () =>
            {
                rowY += 4f;
                drawDivider();
                rowY += 6f;
            };

            if (!hasActiveSystems)
            {
                Rect idleRect = new Rect(bodyRect.x + 8f, bodyRect.y + 8f, bodyRect.width - 16f, 18f);
                GUI.Label(idleRect, this.L("All systems idle"), hintStyle);
            }
            else
            {
                if (this.isRadarActive)
                {
                    drawFeature("Radar", "Active");
                    finishBlock();
                }
                if (this.gameSpeed != 1.0f)
                {
                    drawFeature("Speed", string.Format("{0:F1}x", this.gameSpeed));
                    finishBlock();
                }
                if (this.noclipEnabled)
                {
                    drawFeature("Noclip", "Active");
                    finishBlock();
                }
                if (this.bypassOverlapEnabled)
                {
                    drawFeature("Bypass Overlap", "Active");
                    finishBlock();
                }
                if (this.birdVacuumEnabled)
                {
                    drawFeature("Bird Vacuum", "Active");
                    finishBlock();
                }
                if (this.autoFarmActive)
                {
                    drawFeature("Foraging", this.GetForagingModeLabel());
                    drawDetail("Status", this.GetForagingStatusDisplayText(false));
                    finishBlock();
                }
                else if (this.auraFarmEnabled)
                {
                    drawFeature("Aura Farm", "Running");
                    finishBlock();
                }
                if (InsectNetFarm.IsEnabled)
                {
                    drawFeature("Insect Farm", "Active");
                    drawDetail("Status", InsectNetFarm.GetLastStatus());
                    drawDetail("Tool", InsectNetFarm.GetLastToolStatus());
                    drawDetail("Caught", InsectNetFarm.GetSessionCatchCount().ToString());
                    finishBlock();
                }
                if (BirdNetFarm.IsEnabled)
                {
                    drawFeature("Bird Farm", "Active");
                    drawDetail("Status", BirdNetFarm.GetLastStatus());
                    drawDetail("Tool", BirdNetFarm.GetLastToolStatus());
                    drawDetail("Caught", BirdNetFarm.GetSessionCatchCount().ToString());
                    drawDetail("Scared", BirdNetFarm.GetSessionScaredCount().ToString());
                    finishBlock();
                }
                if (AutoFishingFarm.IsEnabled)
                {
                    drawFeature("Fishing Farm", "Active");
                    drawDetail("Status", AutoFishingFarm.GetLastStatus());
                    drawDetail("Tool", AutoFishingFarm.GetLastToolStatus());
                    drawDetail("Target", AutoFishingFarm.GetLastTargetStatus());
                    finishBlock();
                }

                if (this.autoSnowEnabled)
                {
                    drawFeature("Auto Snow", "Active");
                    finishBlock();
                }
                if (this.autoJoinFriendEnabled)
                {
                    drawFeature("Auto Join Friend", "Active");
                }
            }

            this.DrawRoundedPanel(footerRect, 10f, overlayFooterFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(footerRect.x, footerRect.y, footerRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(footerRect.x + 12f, footerRect.y + 8f, 60f, 16f), this.L("FPS"), footerLabelStyle);
            GUI.Label(new Rect(footerRect.x + 72f, footerRect.y + 7f, footerRect.width - 84f, 18f), fpsText, footerValueStyle);

            GUI.color = prevColor;
        }

        private Texture2D CopySpriteTexture(Sprite sprite, string logPrefix)
        {
            try
            {
                if (sprite == null || sprite.texture == null)
                {
                    return null;
                }

                Texture2D original = sprite.texture;
                RenderTexture rt = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(original, rt);

                Texture2D copy = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
                RenderTexture previousRT = RenderTexture.active;
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previousRT;
                RenderTexture.ReleaseTemporary(rt);
                return copy;
            }
            catch (Exception ex)
            {
                ModLogger.Msg((logPrefix ?? "[BagScan]") + " Failed to copy texture: " + ex.Message);
                return null;
            }
        }

        private void EnsureUiPickerTextures(float hue)
        {
            if (this.uiHueTexture == null)
            {
                this.uiHueTexture = this.CreateHueTexture(18, 180);
                this.themeTextures.Add(this.uiHueTexture);
            }

            if (this.uiSvTexture == null || Math.Abs(this.uiPickerHueCached - hue) > 0.001f)
            {
                if (this.uiSvTexture != null)
                {
                    Object.Destroy(this.uiSvTexture);
                    this.themeTextures.Remove(this.uiSvTexture);
                }
                this.uiSvTexture = this.CreateSvTexture(220, 180, hue);
                this.uiPickerHueCached = hue;
                this.themeTextures.Add(this.uiSvTexture);
            }
        }

        private Texture2D CreateHueTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++)
            {
                float h = (float)y / (height - 1);
                Color c = Color.HSVToRGB(h, 1f, 1f);
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        private Texture2D CreateSvTexture(int width, int height, float hue)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / (height - 1);
                for (int x = 0; x < width; x++)
                {
                    float s = (float)x / (width - 1);
                    tex.SetPixel(x, y, Color.HSVToRGB(hue, s, v));
                }
            }
            tex.Apply();
            return tex;
        }

        private string ColorToHex(Color color)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private bool TryParseHexColor(string input, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string hex = input.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6) return false;

            int r;
            int g;
            int b;
            if (!int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out r)) return false;
            if (!int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out g)) return false;
            if (!int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

    }
}
