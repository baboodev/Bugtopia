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
        private float GetLogicalScreenWidth()
        {
            float scale = this.GetUiScale();
            return (float)Screen.width / Mathf.Max(scale, 0.001f);
        }

        private float GetLogicalScreenHeight()
        {
            float scale = this.GetUiScale();
            return (float)Screen.height / Mathf.Max(scale, 0.001f);
        }

        private void RunWithUiScale(Action draw)
        {
            if (draw == null)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;
            Color prevContent = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                float scale = this.GetUiScale();
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                draw();
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = prevColor;
                GUI.backgroundColor = prevBg;
                GUI.contentColor = prevContent;
            }
        }

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

        // ===== Bugtopia 2.0 theme plumbing =====
        // Cached OS font + derived colors + baked textures used by the redesigned chrome.
        private Font uiThemeFont;
        private bool uiThemeFontAttempted;
        private Texture2D uiAccentGradientTex;
        private Texture2D uiRoundedRectSprite;
        private bool uiThemeStylesDirty;
        private float uiThemeNextRebuildAt;
        private float uiThemePendingSaveAt = -1f;
        private readonly Dictionary<string, float> uiToggleAnimStates = new Dictionary<string, float>(64);
        private int uiToggleAnimFrame = -1;
        private float uiToggleAnimDt = 0.016f;
        private float uiToggleAnimPrevTime = -1f;

        // Advances (on Repaint) and returns the 0..1 knob position for an animated switch.
        private float StepToggleAnim(string key, bool value)
        {
            float target = value ? 1f : 0f;
            float t;
            if (!this.uiToggleAnimStates.TryGetValue(key, out t))
            {
                t = target;
            }

            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                if (Time.frameCount != this.uiToggleAnimFrame)
                {
                    float now = Time.unscaledTime;
                    this.uiToggleAnimDt = this.uiToggleAnimPrevTime > 0f
                        ? Mathf.Clamp(now - this.uiToggleAnimPrevTime, 0.001f, 0.05f)
                        : 0.016f;
                    this.uiToggleAnimPrevTime = now;
                    this.uiToggleAnimFrame = Time.frameCount;
                }

                t = Mathf.MoveTowards(t, target, this.uiToggleAnimDt * 7.5f);
                this.uiToggleAnimStates[key] = t;
            }

            return t;
        }

        // Shared switch renderer: capsule track (accent gradient fades in with the knob) + knob.
        private void DrawSwitchTrackAndKnob(Rect switchRect, float knobT, bool hovered)
        {
            this.EnsureUiPrimitiveTextures();
            this.DrawCapsule(switchRect, new Color(0.137f, 0.169f, 0.22f, 0.98f));
            if (knobT > 0.01f)
            {
                this.DrawAccentGradientCapsule(switchRect, knobT);
            }

            float knobD = switchRect.height - 6f;
            float x0 = switchRect.x + 3f;
            float x1 = switchRect.xMax - knobD - 3f;
            Rect knobRect = new Rect(Mathf.Lerp(x0, x1, knobT), switchRect.y + 3f, knobD, knobD);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(knobRect.x, knobRect.y + 1.5f, knobD, knobD), this.uiCircleTexture);
            Color knobColor = Color.Lerp(new Color(0.68f, 0.72f, 0.8f, 1f), Color.white, knobT);
            GUI.color = hovered ? Color.Lerp(knobColor, Color.white, 0.35f) : knobColor;
            GUI.DrawTexture(knobRect, this.uiCircleTexture);
            GUI.color = Color.white;
        }

        // Soft full-row hover wash behind a control row (no call-site changes needed).
        private void DrawRowHoverWash(Rect rowRect)
        {
            this.DrawTintedRoundedBox(
                new Rect(rowRect.x - 8f, rowRect.y - 3f, rowRect.width + 16f, rowRect.height + 6f),
                new Color(1f, 1f, 1f, 0.03f));
        }

        private Font EnsureUiThemeFont()
        {
            if (this.uiThemeFont != null || this.uiThemeFontAttempted)
            {
                return this.uiThemeFont;
            }

            this.uiThemeFontAttempted = true;
            string[] candidates = new string[] { "Segoe UI Variable Text", "Segoe UI" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(candidates[i], 14);
                    if (font != null)
                    {
                        font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        this.uiThemeFont = font;
                        break;
                    }
                }
                catch
                {
                    // OS font unavailable — keep IMGUI's built-in font.
                }
            }

            return this.uiThemeFont;
        }

        // Second stop of the accent gradient: hue nudged toward the next hue, slightly deeper.
        private Color GetUiAccentSecondary(Color accent)
        {
            float h;
            float s;
            float v;
            Color.RGBToHSV(accent, out h, out s, out v);
            h = Mathf.Repeat(h + 0.05f, 1f);
            s = Mathf.Clamp01((s * 1.08f) + 0.04f);
            v = Mathf.Clamp01(v * 0.9f);
            return Color.HSVToRGB(h, s, v);
        }

        // bg3 — control fill one step above the content surface, derived so custom
        // content colors picked in the theme tab keep a consistent ramp.
        private Color GetUiControlFill()
        {
            return new Color(
                Mathf.Clamp01(this.uiContentR + 0.032f),
                Mathf.Clamp01(this.uiContentG + 0.039f),
                Mathf.Clamp01(this.uiContentB + 0.051f));
        }

        private Color GetUiTextOnAccent(Color accent)
        {
            float luma = (0.299f * accent.r) + (0.587f * accent.g) + (0.114f * accent.b);
            return luma > 0.62f ? new Color(0.02f, 0.08f, 0.12f) : Color.white;
        }

        // Rounded on the LEFT two corners only; the right edge is perfectly flat/square all the
        // way to the texture edge. For the sidebar: it needs to look continuous with the
        // window's outer rounding on its left/outer edge while butting up flush (no curve, no
        // gap) against the main content column on its right/inner edge. Squaring off a
        // symmetric 4-corner-rounded fill by painting an opaque patch over the unwanted
        // corners only worked while the layer underneath was assumed near-opaque (the patch
        // color was pre-flattened to approximate "basePanel blended once over windowBase") —
        // once Window Alpha became genuinely adjustable and low, that patch was still a flat
        // OPAQUE square with no way to know it should show the game through it too, and it
        // rendered as a visible solid block at low transparency. Baking the true asymmetric
        // shape once avoids the problem structurally: every pixel is painted exactly once, at
        // whatever alpha the caller tints it with, so it stays correct at any transparency.
        private Texture2D MakeLeftRoundedRectTexture(int size, float radius)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = Mathf.Clamp(radius, 1f, (size * 0.5f) - 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float shapeA;
                    if (x >= r || (y >= r && y <= size - r))
                    {
                        // Right of the rounding margin, or vertically in the flat middle band
                        // between the two corners: always fully inside.
                        shapeA = 1f;
                    }
                    else
                    {
                        float cy = (y < r) ? r : (size - r);
                        float dxc = x - r;
                        float dyc = y - cy;
                        float dist = Mathf.Sqrt((dxc * dxc) + (dyc * dyc));
                        shapeA = Mathf.Clamp01(r + 0.5f - dist);
                    }

                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, shapeA));
                }
            }

            tex.Apply();
            this.themeTextures.Add(tex);
            return tex;
        }

        // Tinted per-call via GUI.color — see MakeLeftRoundedRectTexture / themeSidebarShapeStyle.
        private void DrawTintedLeftRoundedBox(Rect rect, Color tint)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            if (this.themeSidebarShapeStyle == null || this.themeSidebarShapeStyle.normal.background == null)
            {
                this.DrawRoundedPanel(rect, 15f, tint, Color.clear, 0f, Color.clear);
                return;
            }

            Color prev = GUI.color;
            GUI.color = tint;
            GUI.Box(rect, "", this.themeSidebarShapeStyle);
            GUI.color = prev;
        }

        private Texture2D MakeRoundedRectTexture(int size, float radius, Color fill, Color ring, float ringWidth)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = Mathf.Clamp(radius, 1f, (size * 0.5f) - 1f);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                        + Mathf.Min(Mathf.Max(dx, dy), 0f) - r + 1f;
                    float shapeA = Mathf.Clamp01(0.5f - outside);
                    Color c = fill;
                    float pixelA = fill.a;
                    if (ring.a > 0f && ringWidth > 0f)
                    {
                        float inside = -outside;
                        if (inside < ringWidth)
                        {
                            float t = Mathf.Clamp01(ringWidth - inside);
                            c = Color.Lerp(fill, new Color(ring.r, ring.g, ring.b, fill.a), ring.a * t);
                            pixelA = Mathf.Max(fill.a, ring.a * t);
                        }
                    }
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, pixelA * shapeA));
                }
            }

            tex.Apply();
            this.themeTextures.Add(tex);
            return tex;
        }

        private Texture2D MakeRoundedGradientTexture(int width, int height, float radius, Color left, Color right)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = Mathf.Clamp(radius, 1f, (Mathf.Min(width, height) * 0.5f) - 1f);
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - halfW) - (halfW - r);
                    float dy = Mathf.Abs(y + 0.5f - halfH) - (halfH - r);
                    float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                        + Mathf.Min(Mathf.Max(dx, dy), 0f) - r + 1f;
                    float shapeA = Mathf.Clamp01(0.5f - outside);
                    Color c = Color.Lerp(left, right, width <= 1 ? 0f : (float)x / (width - 1));
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * shapeA));
                }
            }

            tex.Apply();
            this.themeTextures.Add(tex);
            return tex;
        }

        private Texture2D MakeHorizontalGradientTexture(Color left, Color right)
        {
            const int width = 64;
            Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, 0, Color.Lerp(left, right, (float)x / (width - 1)));
            }

            tex.Apply();
            this.themeTextures.Add(tex);
            return tex;
        }

        private Texture2D EnsureUiAccentGradientTexture()
        {
            if (this.uiAccentGradientTex == null)
            {
                Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
                this.uiAccentGradientTex = this.MakeHorizontalGradientTexture(accent, this.GetUiAccentSecondary(accent));
            }

            return this.uiAccentGradientTex;
        }

        // Capsule filled with the accent→accent2 gradient (toggle-on track, slider fill).
        // Baked single-texture 9-slice when available (themeCapsuleGradientStyle) — the old
        // 3-piece assembly (mid rect + 2 clipped half-circle groups) still showed a hairline
        // seam at rest (full alpha, not just mid-animation) on some toggles: three independently
        // rasterized quads under a scaled GUI.matrix don't reliably share the exact same edge
        // pixel. Below the pill-readable size (12px either dimension) just stretches the flat
        // gradient texture with no rounding — a 1-2px corner radius is invisible at that scale
        // anyway, and a single textured quad cannot seam with itself.
        private void DrawAccentGradientCapsule(Rect rect, float alpha = 1f)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            this.EnsureUiPrimitiveTextures();
            rect = new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.width), Mathf.Round(rect.height));

            if (rect.height < 12f || rect.width < 12f)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(rect, this.EnsureUiAccentGradientTexture(), ScaleMode.StretchToFill);
                GUI.color = Color.white;
                return;
            }

            if (this.themeCapsuleGradientStyle != null && this.themeCapsuleGradientStyle.normal.background != null)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(rect, "", this.themeCapsuleGradientStyle);
                GUI.color = Color.white;
                return;
            }

            // Fallback for the brief pre-EnsureThemeStyles window only.
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, alpha);
            Color accent2 = this.GetUiAccentSecondary(accent);
            accent2.a = alpha;
            float r = Mathf.Max(0f, Mathf.Min(rect.height * 0.5f, rect.width * 0.5f));
            Rect mid = new Rect(rect.x + r, rect.y, rect.width - (2f * r), rect.height);
            if (mid.width > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(mid, this.EnsureUiAccentGradientTexture(), ScaleMode.StretchToFill);
            }

            GUI.BeginGroup(new Rect(rect.x, rect.y, r, rect.height));
            GUI.color = accent;
            GUI.DrawTexture(new Rect(0f, 0f, rect.height, rect.height), this.uiCircleTexture);
            GUI.EndGroup();
            GUI.BeginGroup(new Rect(rect.xMax - r, rect.y, r, rect.height));
            GUI.color = accent2;
            GUI.DrawTexture(new Rect(r - rect.height, 0f, rect.height, rect.height), this.uiCircleTexture);
            GUI.EndGroup();
            GUI.color = Color.white;
        }

        // Quiet hairline ring around a box. Rounded (baked single-texture, themeRoundedRingStyle)
        // to match the rounded corners already baked into every box background it outlines —
        // straight hairlines there poked out past the rounded shape as a mismatched rectangular
        // frame. Thin rects (dividers, degenerate cell borders) keep the old straight-line draw:
        // there's no meaningful "rounded corner" on a 1px-tall line, and the ring texture's fixed
        // border would just clip/distort at that size.
        private void DrawCardOutline(Rect rect, float thickness = 1f)
        {
            Color edge = new Color(1f, 1f, 1f, Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));

            if (rect.height >= 18f && rect.width >= 18f)
            {
                this.EnsureUiPrimitiveTextures();
                if (this.themeRoundedRingStyle != null && this.themeRoundedRingStyle.normal.background != null)
                {
                    Color prevTint = GUI.color;
                    GUI.color = edge;
                    GUI.Box(rect, "", this.themeRoundedRingStyle);
                    GUI.color = prevTint;
                    return;
                }
            }

            Color prev = GUI.color;
            GUI.color = edge;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawRoundedPanel(Rect rect, float radius, Color fill, Color border, float borderWidth, Color topAccent)
        {
            this.EnsureUiPrimitiveTextures();

            // Integer-aligned rect: the shape is assembled from adjacent rects + corner circles,
            // and fractional coordinates open subpixel seams (bright/dark 1px stripes) between
            // the patches when the fill is translucent.
            rect = new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.width), Mathf.Round(rect.height));
            float corner = Mathf.Clamp(Mathf.Round(radius), 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
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

            if (borderWidth > 0f && corner > 0.5f)
            {
                // Rounded ring, not 4 straight lines: draw the SAME corner-radius shape again in
                // the border color (reusing the exact rect+corner already used for the fill
                // above, so it can't mismatch), then redraw the fill inset by borderWidth on top.
                // The old straight-line border had square corners sitting on top of the rounded
                // fill drawn above — they poked out past the curve as a mismatched rectangular
                // frame (same defect DrawCardOutline had, fixed separately; this is the same bug
                // living inside DrawRoundedPanel's own border path).
                Color borderColor = border.a > 0f ? border : new Color(1f, 1f, 1f, 0.12f);
                this.DrawRoundedPanel(rect, radius, borderColor, Color.clear, 0f, Color.clear);
                float innerCorner = Mathf.Max(0f, corner - borderWidth);
                Rect innerRect = new Rect(rect.x + borderWidth, rect.y + borderWidth, rect.width - (2f * borderWidth), rect.height - (2f * borderWidth));
                if (innerRect.width > 0f && innerRect.height > 0f)
                {
                    this.DrawRoundedPanel(innerRect, innerCorner, fill, Color.clear, 0f, Color.clear);
                }

                if (topAccent.a > 0f)
                {
                    GUI.color = topAccent;
                    GUI.DrawTexture(new Rect(innerRect.x + 1f, innerRect.y + 1f, innerRect.width - 2f, 1.5f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
            else if (borderWidth > 0f)
            {
                // Degenerate (no meaningful corner radius): the old straight-line border is
                // already correct here, nothing to round.
                Color borderColor = border.a > 0f ? border : new Color(1f, 1f, 1f, 0.12f);
                GUI.color = borderColor;
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

        // Shared primitive for translucent hover/wash fills — single baked 9-slice texture,
        // tinted via GUI.color. See themeRoundedWhiteStyle bake comment for why this exists
        // instead of DrawRoundedPanel's rect+circle assembly (visible seam at low alpha).
        private void DrawTintedRoundedBox(Rect rect, Color tint)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            if (this.themeRoundedWhiteStyle == null || this.themeRoundedWhiteStyle.normal.background == null)
            {
                this.DrawRoundedPanel(rect, 10f, tint, Color.clear, 0f, Color.clear);
                return;
            }

            Color prev = GUI.color;
            GUI.color = tint;
            GUI.Box(rect, "", this.themeRoundedWhiteStyle);
            GUI.color = prev;
        }

        // Same idea as DrawTintedRoundedBox, but for LARGE chrome (main window slab, sidebar
        // column) at the bigger radius-15 baked into themeBigTintableStyle. Kept as a separate
        // function rather than parameterizing DrawTintedRoundedBox so its many existing small-
        // element call sites can't be accidentally affected.
        private void DrawTintedBigRoundedBox(Rect rect, Color tint)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            if (this.themeBigTintableStyle == null || this.themeBigTintableStyle.normal.background == null)
            {
                this.DrawRoundedPanel(rect, 15f, tint, Color.clear, 0f, Color.clear);
                return;
            }

            Color prev = GUI.color;
            GUI.color = tint;
            GUI.Box(rect, "", this.themeBigTintableStyle);
            GUI.color = prev;
        }

        private void DrawExentriSectionPanel(Rect rect, Color accent, Color fill, Color softLine)
        {
            // Card = rounded hairline ring (baked single-texture, themeBigCardRingStyle) +
            // rounded fill; no accent top line in the 2.0 look. The ring can't be a plain
            // DrawRoundedPanel translucent fill (5.5% alpha) — same seam bug as every other
            // low-alpha multi-piece fill in this file — and this is the single most-visible
            // panel in the app (every tab's main card, the LIVE rail).
            if (this.themeBigCardRingStyle != null && this.themeBigCardRingStyle.normal.background != null)
            {
                Color prevTint = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.055f);
                GUI.Box(rect, "", this.themeBigCardRingStyle);
                GUI.color = prevTint;
            }
            else
            {
                this.DrawRoundedPanel(rect, 14f, new Color(1f, 1f, 1f, 0.055f), Color.clear, 0f, Color.clear);
            }

            this.DrawRoundedPanel(
                new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f),
                13f,
                fill,
                Color.clear,
                0f,
                Color.clear);
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
            Event e = Event.current;
            bool hovered = e != null && rect.Contains(e.mousePosition);
            if (hovered)
            {
                this.DrawRowHoverWash(rect);
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 60f, rect.height), this.L(label), labelStyle);

            Rect switchRect = new Rect(rect.xMax - 48f, rect.y + Mathf.Max(-1f, (rect.height - 22f) * 0.5f), 42f, 22f);
            float knobT = this.StepToggleAnim(label ?? "toggle", value);
            this.DrawSwitchTrackAndKnob(switchRect, knobT, hovered);

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
            float rowHeight = Mathf.Max(rect.height, this.GetSwitchToggleHeight(rect.width, label, minHeight));
            Rect hitRect = new Rect(rect.x, rect.y, rect.width, rowHeight);
            Event e = Event.current;
            bool hovered = e != null && hitRect.Contains(e.mousePosition);
            if (hovered)
            {
                this.DrawRowHoverWash(hitRect);
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.wordWrap = true;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            float labelWidth = Mathf.Max(60f, rect.width - 60f);
            GUI.Label(new Rect(rect.x, rect.y, labelWidth, rowHeight), this.L(label), labelStyle);

            Rect switchRect = new Rect(rect.xMax - 48f, rect.y + Mathf.Max(-1f, (rowHeight - 22f) * 0.5f), 42f, 22f);
            float knobT = this.StepToggleAnim(label ?? "toggle", value);
            this.DrawSwitchTrackAndKnob(switchRect, knobT, hovered);

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
            float lineY = rect.y + (rect.height * 0.5f) - 3f;
            Rect bgRect = new Rect(rect.x, lineY, rect.width, 6f);
            Rect fillRect = new Rect(rect.x, lineY, Mathf.Max(6f, rect.width * t), 6f);
            float thumbX = Mathf.Clamp(rect.x + rect.width * t, rect.x + 8f, rect.xMax - 8f);
            Rect thumbRect = new Rect(thumbX - 8f, rect.y + (rect.height * 0.5f) - 8f, 16f, 16f);

            this.DrawCapsule(bgRect, new Color(0.137f, 0.169f, 0.22f, 0.95f));
            this.DrawAccentGradientCapsule(fillRect);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(thumbRect.x, thumbRect.y + 1.5f, 16f, 16f), this.uiCircleTexture);
            GUI.color = new Color(0.97f, 0.98f, 1f, 1f);
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
            if (this.uiCircleTexture != null && this.uiRoundedRectSprite != null)
            {
                return;
            }

            if (this.uiCircleTexture != null)
            {
                // Circle alive but sprite missing — just rebuild the sprite.
                this.uiRoundedRectSprite = this.MakeRoundedRectTexture(32, 9f, Color.white, Color.clear, 0f);
                return;
            }

            int size = 32;
            // Mip-mapped + trilinear + a soft 1px antialiased edge (not a hard 0/1 cutoff):
            // this sprite is drawn at everything from an 8px live-dot to a 42px switch cap, and
            // an unmipped hard-edge circle minifies inconsistently at small sizes (visible
            // shimmer/banding on the switch/nav-bar circle draws).
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            float radius = (size - 1f) * 0.5f;
            Vector2 c = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply(true);
            this.uiCircleTexture = tex;
            this.themeTextures.Add(tex);

            // White rounded-rect sprite: small translucent chips draw this as ONE tinted quad
            // (GUI.color) instead of assembling patches, which seams on translucent fills.
            if (this.uiRoundedRectSprite == null)
            {
                this.uiRoundedRectSprite = this.MakeRoundedRectTexture(32, 9f, Color.white, Color.clear, 0f);
            }
        }

        // Solid-color capsule (arbitrary tint). Baked single-texture 9-slice when the element
        // reads as a pill (≥12px both dimensions) — see DrawAccentGradientCapsule for why the
        // old rect+circle-group assembly is unreliable under a scaled GUI.matrix. Below that,
        // a flat untinted rect: rounding is imperceptible at that thinness and a single quad
        // can't seam with itself.
        private void DrawCapsule(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            this.EnsureUiPrimitiveTextures();
            rect = new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.width), Mathf.Round(rect.height));

            if (rect.height < 12f || rect.width < 12f)
            {
                GUI.color = color;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
                return;
            }

            if (this.themeCapsuleWhiteStyle != null && this.themeCapsuleWhiteStyle.normal.background != null)
            {
                GUI.color = color;
                GUI.Box(rect, "", this.themeCapsuleWhiteStyle);
                GUI.color = Color.white;
                return;
            }

            // Fallback for the brief pre-EnsureThemeStyles window only.
            float r = Mathf.Max(0f, Mathf.Min(rect.height * 0.5f, rect.width * 0.5f));
            GUI.color = color;
            Rect mid = new Rect(rect.x + r, rect.y, rect.width - (2f * r), rect.height);
            if (mid.width > 0f)
            {
                GUI.DrawTexture(mid, Texture2D.whiteTexture);
            }

            GUI.BeginGroup(new Rect(rect.x, rect.y, r, rect.height));
            GUI.DrawTexture(new Rect(0f, 0f, rect.height, rect.height), this.uiCircleTexture);
            GUI.EndGroup();
            GUI.BeginGroup(new Rect(rect.xMax - r, rect.y, r, rect.height));
            GUI.DrawTexture(new Rect(r - rect.height, 0f, rect.height, rect.height), this.uiCircleTexture);
            GUI.EndGroup();
            GUI.color = Color.white;
        }

        private void DrawQuickStatusPanel(Rect panelRect)
        {
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color panelFill = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiPanelAlpha, 0.15f, 1f));
            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);
            Color live = new Color(0.24f, 0.86f, 0.59f);

            this.DrawExentriSectionPanel(panelRect, accent, panelFill, Color.clear);
            this.EnsureUiPrimitiveTextures();

            List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();

            GUIStyle overline = new GUIStyle(GUI.skin.label);
            overline.fontSize = 11;
            overline.fontStyle = FontStyle.Bold;
            overline.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.95f);
            GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 14f, 100f, 18f), this.L("LIVE"), overline);

            string chipText = entries.Count > 0 ? this.LF("{0} active", entries.Count) : this.L("standby");
            GUIStyle chipStyle = new GUIStyle(GUI.skin.label);
            chipStyle.fontSize = 10;
            chipStyle.fontStyle = FontStyle.Normal;
            chipStyle.alignment = TextAnchor.MiddleCenter;
            chipStyle.clipping = TextClipping.Overflow;
            chipStyle.normal.textColor = entries.Count > 0 ? live : textMuted;
            // No CalcSize here: it under-measures this dynamic-font string in-game (observed
            // live), so the capsule is sized from the glyph count with generous slack instead.
            float chipWidth = Mathf.Min(30f + (chipText.Length * 7.5f), panelRect.width - 110f);
            Rect chipRect = new Rect(panelRect.xMax - 14f - chipWidth, panelRect.y + 12f, chipWidth, 20f);
            this.DrawCapsule(chipRect, entries.Count > 0 ? new Color(live.r, live.g, live.b, 0.13f) : new Color(1f, 1f, 1f, 0.05f));
            GUI.Label(chipRect, chipText, chipStyle);

            // Footer: FPS readout (same smoothing fields as the status overlay).
            Rect footerRect = new Rect(panelRect.x + 1f, panelRect.yMax - 40f, panelRect.width - 2f, 39f);
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(new Rect(footerRect.x + 10f, footerRect.y, footerRect.width - 20f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
            GUIStyle fpsLabelStyle = new GUIStyle(GUI.skin.label);
            fpsLabelStyle.fontSize = 10;
            fpsLabelStyle.fontStyle = FontStyle.Bold;
            fpsLabelStyle.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.95f);
            GUI.Label(new Rect(panelRect.x + 16f, footerRect.y + 12f, 60f, 16f), this.L("FPS"), fpsLabelStyle);
            GUIStyle fpsValueStyle = new GUIStyle(GUI.skin.label);
            fpsValueStyle.fontSize = 13;
            fpsValueStyle.fontStyle = FontStyle.Bold;
            fpsValueStyle.alignment = TextAnchor.MiddleRight;
            fpsValueStyle.normal.textColor = textPrimary;
            GUI.Label(new Rect(panelRect.xMax - 90f, footerRect.y + 10f, 74f, 18f), fpsText, fpsValueStyle);

            float x = panelRect.x + 16f;
            float w = panelRect.width - 32f;
            float y = panelRect.y + 44f;
            float maxY = footerRect.y - 10f;

            if (entries.Count == 0)
            {
                GUIStyle none = new GUIStyle(GUI.skin.label);
                none.fontSize = 12;
                none.fontStyle = FontStyle.Italic;
                none.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.6f);
                GUI.Label(new Rect(x, y + 4f, w, 22f), this.L("No active features"), none);
                return;
            }

            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 13;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = textPrimary;

            GUIStyle value = new GUIStyle(GUI.skin.label);
            value.fontSize = 11;
            value.normal.textColor = new Color(textMuted.r, textMuted.g, textMuted.b, 0.98f);

            float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 2.6f));
            for (int i = 0; i < entries.Count; i++)
            {
                if (y + 20f > maxY)
                {
                    int remaining = entries.Count - i;
                    GUI.Label(new Rect(x, Mathf.Min(y, maxY - 16f), w, 16f), this.LF("+{0} more", remaining), value);
                    break;
                }

                LiveFeatureStatusEntry entry = entries[i];

                // Pulsing live dot with a soft halo.
                GUI.color = new Color(live.r, live.g, live.b, 0.18f + (0.22f * pulse));
                GUI.DrawTexture(new Rect(x - 3f, y + 3f, 14f, 14f), this.uiCircleTexture);
                GUI.color = live;
                GUI.DrawTexture(new Rect(x, y + 6f, 8f, 8f), this.uiCircleTexture);
                GUI.color = Color.white;

                GUI.Label(new Rect(x + 18f, y, w - 18f, 18f), this.L(entry.Label), title);
                y += 19f;
                if (!string.IsNullOrWhiteSpace(entry.Summary))
                {
                    GUI.Label(new Rect(x + 18f, y, w - 18f, 16f), this.L(entry.Summary), value);
                    y += 17f;
                }

                List<LiveFeatureStatusDetail> details = entry.Details;
                if (details != null)
                {
                    for (int j = 0; j < details.Count; j++)
                    {
                        if (y + 16f > maxY)
                        {
                            break;
                        }

                        LiveFeatureStatusDetail detail = details[j];
                        GUI.Label(new Rect(x + 18f, y, w - 18f, 16f), this.L(detail.Label) + ": " + this.L(detail.Value), value);
                        y += 16f;
                    }
                }

                y += 9f;
            }
        }

        private static void ApplyStatusOverlayTextStyle(GUIStyle style)
        {
            style.clipping = TextClipping.Overflow;
            style.padding = new RectOffset(0, 0, 1, 3);
        }

        private float GetStatusOverlayHeight()
        {
            List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
            const float chrome = 38f + 36f + 28f;
            if (entries.Count == 0)
            {
                return chrome + 30f;
            }

            int lineCount = this.CountLiveFeatureStatusLines(entries);
            int blockGaps = Mathf.Max(0, entries.Count - 1);
            float body = (lineCount * 26f) + (blockGaps * 12f);
            return Mathf.Clamp(chrome + body + 10f, 172f, 760f);
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

            List<LiveFeatureStatusEntry> entries = this.CollectLiveFeatureStatusEntries();
            this.ConsiderLiveFeatureStatusTextLengths(entries, Consider);

            if (maxTextLength <= 0)
            {
                return 228f;
            }

            float width = 228f + Mathf.Max(0f, (maxTextLength - 14) * 5.6f);
            return Mathf.Clamp(width, 228f, Mathf.Min(420f, this.GetLogicalScreenWidth() - 16f));
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

            ApplyStatusOverlayTextStyle(headerStyle);
            ApplyStatusOverlayTextStyle(badgeStyle);
            ApplyStatusOverlayTextStyle(sectionStyle);
            ApplyStatusOverlayTextStyle(detailLabelStyle);
            ApplyStatusOverlayTextStyle(detailValueStyle);
            ApplyStatusOverlayTextStyle(hintStyle);
            ApplyStatusOverlayTextStyle(footerLabelStyle);
            ApplyStatusOverlayTextStyle(footerValueStyle);

            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.98f);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.88f);
            Color separator = new Color(1f, 1f, 1f, 0.06f);
            Color overlayFill = new Color(0.08f, 0.10f, 0.13f, 0.94f);
            Color overlayHeaderFill = new Color(0.10f, 0.12f, 0.17f, 0.98f);
            Color overlayFooterFill = new Color(0.07f, 0.08f, 0.11f, 0.98f);
            // Solid light-gray, not translucent white — the overlay floats over the game and a
            // translucent ring reads as a bright stripe on light scenes.
            Color overlayBorder = new Color(0.165f, 0.205f, 0.27f, 0.9f);
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

            List<LiveFeatureStatusEntry> liveEntries = this.CollectLiveFeatureStatusEntries();
            bool hasActiveSystems = liveEntries.Count > 0;
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

            if (this.themeStatusOverlayFrameStyle != null && this.themeStatusOverlayFrameStyle.normal.background != null)
            {
                GUI.Box(frameRect, "", this.themeStatusOverlayFrameStyle);
            }
            else
            {
                this.DrawRoundedPanel(frameRect, 10f, overlayFill, overlayBorder, 1f, Color.clear);
            }

            Rect headerRect = new Rect(frameRect.x + 1f, frameRect.y + 1f, frameRect.width - 2f, 38f);
            Rect footerRect = new Rect(frameRect.x + 1f, frameRect.yMax - 37f, frameRect.width - 2f, 36f);
            Rect bodyRect = new Rect(frameRect.x + 10f, headerRect.yMax + 8f, frameRect.width - 20f, footerRect.y - headerRect.yMax - 16f);

            this.DrawRoundedPanel(headerRect, 10f, overlayHeaderFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(bodyRect.x, bodyRect.y - 4f, bodyRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(headerRect.x + 12f, headerRect.y + 8f, 116f, 22f), "Bugtopia", headerStyle);

            Rect badgeRect = new Rect(headerRect.xMax - 82f, headerRect.y + 8f, 70f, 22f);
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
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 24f);
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 2f, 112f, 20f), this.L(label), sectionStyle);
                GUI.Label(new Rect(rowRect.x + 120f, rowRect.y + 2f, rowRect.width - 128f, 20f), this.L(value), detailValueStyle);
                rowY += 26f;
            };
            Action<string, string> drawDetail = (label, value) =>
            {
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 20f);
                GUI.Label(new Rect(rowRect.x + 18f, rowRect.y + 1f, 92f, 18f), this.L(label), detailLabelStyle);
                GUI.Label(new Rect(rowRect.x + 110f, rowRect.y + 1f, rowRect.width - 118f, 18f), this.L(value), detailValueStyle);
                rowY += 22f;
            };
            Action finishBlock = () =>
            {
                rowY += 6f;
                drawDivider();
                rowY += 8f;
            };

            if (!hasActiveSystems)
            {
                Rect idleRect = new Rect(bodyRect.x + 8f, bodyRect.y + 8f, bodyRect.width - 16f, 22f);
                GUI.Label(idleRect, this.L("All systems idle"), hintStyle);
            }
            else
            {
                for (int i = 0; i < liveEntries.Count; i++)
                {
                    LiveFeatureStatusEntry entry = liveEntries[i];
                    drawFeature(entry.Label, entry.Summary);
                    List<LiveFeatureStatusDetail> details = entry.Details;
                    if (details != null)
                    {
                        for (int j = 0; j < details.Count; j++)
                        {
                            LiveFeatureStatusDetail detail = details[j];
                            drawDetail(detail.Label, detail.Value);
                        }
                    }

                    if (i < liveEntries.Count - 1)
                    {
                        finishBlock();
                    }
                }
            }

            this.DrawRoundedPanel(footerRect, 10f, overlayFooterFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(footerRect.x, footerRect.y, footerRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(footerRect.x + 12f, footerRect.y + 9f, 60f, 20f), this.L("FPS"), footerLabelStyle);
            GUI.Label(new Rect(footerRect.x + 72f, footerRect.y + 8f, footerRect.width - 84f, 22f), fpsText, footerValueStyle);

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
