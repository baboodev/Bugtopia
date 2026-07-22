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


        private float GetUiScale()
        {
            // Fit-cap design space: the retired IMGUI menu's fixed 1180x720 window size, kept as
            // literals (Phase 5 deleted targetWindowWidth/Height with the menu). The UGUI shell
            // and overlays scaled against this exact space the whole migration, so changing it
            // would silently change every user's effective UI scale on small screens.
            const float baseWidth = 1180f;
            const float baseHeight = 720f;
            float requested = this.NormalizeUiScale(this.uiScale > 0f ? this.uiScale : 1f);
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
        private bool uiThemeStylesDirty;
        private float uiThemeNextRebuildAt;
        private float uiThemePendingSaveAt = -1f;


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


        // Phase 5: shrunk to the circle sprite only — the rounded-rect / ring / knob sprites
        // died with the IMGUI widget set. The circle survives for DrawMouseLookCrosshair (and
        // stays pooled in themeTextures so InvalidateThemeCache keeps managing its lifetime).
        private void EnsureUiPrimitiveTextures()
        {
            if (this.uiCircleTexture != null)
            {
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
