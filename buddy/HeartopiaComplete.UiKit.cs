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
            data.uiThemeVersion = 4;
            data.uiAccentR = this.uiAccentR;
            data.uiAccentG = this.uiAccentG;
            data.uiAccentB = this.uiAccentB;
            data.uiHeaderR = this.uiHeaderR;
            data.uiHeaderG = this.uiHeaderG;
            data.uiHeaderB = this.uiHeaderB;
            data.uiSuccessR = this.uiSuccessR;
            data.uiSuccessG = this.uiSuccessG;
            data.uiSuccessB = this.uiSuccessB;
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
            if (data.uiThemeVersion < 3)
            {
                // Saved before Header Text existed as its own field — match whatever Accent
                // this save already had, so existing themes look exactly as they did rather
                // than suddenly showing default-cyan headers against a custom accent.
                this.uiHeaderR = this.uiAccentR;
                this.uiHeaderG = this.uiAccentG;
                this.uiHeaderB = this.uiAccentB;
            }
            else
            {
                this.uiHeaderR = Mathf.Clamp01(data.uiHeaderR);
                this.uiHeaderG = Mathf.Clamp01(data.uiHeaderG);
                this.uiHeaderB = Mathf.Clamp01(data.uiHeaderB);
            }

            if (data.uiThemeVersion < 4)
            {
                // Saved before Success (LIVE panel / "enabled" toast green) existed as its own
                // field — no prior field to backfill from, so fall back to the fixed default
                // rather than leaving it at 0/0/0.
                this.uiSuccessR = 0.24f;
                this.uiSuccessG = 0.86f;
                this.uiSuccessB = 0.59f;
            }
            else
            {
                this.uiSuccessR = Mathf.Clamp01(data.uiSuccessR);
                this.uiSuccessG = Mathf.Clamp01(data.uiSuccessG);
                this.uiSuccessB = Mathf.Clamp01(data.uiSuccessB);
            }

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

            // Phase 5: the IMGUI GUIStyle bake is gone with the IMGUI menu — what remains in the
            // pool is the crosshair's circle sprite (EnsureUiPrimitiveTextures) and the UGUI
            // theme tab's picker textures (EnsureUiPickerTextures). Null the fields so the lazy
            // builders recreate them from the live theme values after the pool is destroyed.
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
                    this.MarkUguiKitThemeDirty(); // UGUI mirror — loaded values changed the theme
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
                // This raw line-by-line format predates Header Text existing as its own field
                // (nothing writes this legacy format anymore — SaveUiTheme goes through the
                // unified/versioned config path above), so it can never contain uiHeader* keys.
                // Match Accent, same backward-compat behavior as ApplyUiThemeConfig's version<3.
                this.uiHeaderR = this.uiAccentR;
                this.uiHeaderG = this.uiAccentG;
                this.uiHeaderB = this.uiAccentB;
                // Same story for Success — predates this format too, no uiSuccess* keys possible.
                this.uiSuccessR = 0.24f;
                this.uiSuccessG = 0.86f;
                this.uiSuccessB = 0.59f;
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
                this.MarkUguiKitThemeDirty(); // UGUI mirror — loaded values changed the theme
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

        // ----------------------------------------------------------------------------------------
        // Shared toast lifecycle — consumed by the UGUI renderer (HeartopiaComplete.UguiToast.cs,
        // which ticks the sweep every frame from OnUpdate). Phase 5 deleted the dormant IMGUI
        // drawer (DrawMenuNotifications); the lifecycle stays extracted here, OUTSIDE any
        // renderer, because a renderer-owned sweep stops pruning the moment that renderer stops
        // running and pins the list at AddOrUpdateMenuNotification's 6-cap forever.
        // ----------------------------------------------------------------------------------------

        // Reverse RemoveAt instead of RemoveAll: the list holds at most 6 items and this runs
        // every frame — no per-frame closure allocation for the predicate.
        private void SweepExpiredMenuNotifications(float now)
        {
            for (int i = this.menuNotifications.Count - 1; i >= 0; i--)
            {
                HeartopiaComplete.MenuNotification n = this.menuNotifications[i];
                if (n == null || n.ExpireAt <= now)
                {
                    this.menuNotifications.RemoveAt(i);
                }
            }
        }

        // Visibility filter + draw order in one place: newest first (producers Add to the tail),
        // honoring the notifications toggle — disabled means only Force items surface. An empty
        // result IS the "draw nothing" signal (covers the old early-outs for empty list and
        // "disabled with nothing forced").
        private void CollectVisibleMenuNotificationsNewestFirst(List<HeartopiaComplete.MenuNotification> into)
        {
            into.Clear();
            bool drawAll = this.notificationsEnabled;
            for (int i = this.menuNotifications.Count - 1; i >= 0; i--)
            {
                HeartopiaComplete.MenuNotification item = this.menuNotifications[i];
                if (item == null || (!drawAll && !item.Force))
                {
                    continue;
                }

                into.Add(item);
            }
        }


        // ----------------------------------------------------------------------------------------
        // Theme persistence tick — Phase 5 home of the two side-band jobs that used to ride at
        // the top of EnsureThemeStyles() in OnGUI (the EnsureThemeStyles GUIStyle bake itself is
        // gone with the IMGUI menu; the UGUI kit reads the live ui* fields directly):
        //  1. consume uiThemeStylesDirty (armed by the UGUI theme tab and ResetUiThemeToDefaults)
        //     into InvalidateThemeCache + MarkUguiKitThemeDirty, throttled exactly as before, and
        //  2. flush the debounced SaveUiTheme (uiThemePendingSaveAt).
        // Runs every frame from OnUpdate (HeartopiaComplete.cs) so the theme pipeline no longer
        // depends on the IMGUI paint loop. The old Layout-event gate existed so IMGUI never drew
        // destroyed textures mid-frame; OnUpdate runs before any OnGUI event of the same frame,
        // and the surviving IMGUI overlays lazily rebuild via EnsureUiPrimitiveTextures, so the
        // time-throttle alone is enough here.
        private void ProcessUiThemePersistenceOnUpdate()
        {
            if (this.uiThemeStylesDirty && Time.unscaledTime >= this.uiThemeNextRebuildAt)
            {
                this.uiThemeStylesDirty = false;
                this.uiThemeNextRebuildAt = Time.unscaledTime + 0.1f;
                this.InvalidateThemeCache();
                // UGUI mirror: kit-built windows snapshot theme colors at construction, so the
                // same theme-values-changed signal queues their state-preserving rebuild
                // (HeartopiaComplete.UguiKit.cs). Deliberately NOT inside InvalidateThemeCache —
                // that also runs for non-theme cache resets (world-change teardown, theme load).
                this.MarkUguiKitThemeDirty();
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
        }


        // Reset every theme field to the shipped defaults — the ONE shared implementation behind
        // both Reset buttons (this IMGUI tab and the UGUI twin, HeartopiaComplete.UguiThemeContent.cs
        // — extracted from the DrawDangerActionButton block above, values unchanged). Deliberately
        // does NOT call SaveUiTheme and does NOT arm uiThemePendingSaveAt: it only forces an
        // immediate visual rebuild, leaving the reset values unsaved until an explicit Save.
        private void ResetUiThemeToDefaults()
        {
            this.uiAccentR = 0.31f;
            this.uiAccentG = 0.78f;
            this.uiAccentB = 1.00f;
            this.uiHeaderR = 0.31f;
            this.uiHeaderG = 0.78f;
            this.uiHeaderB = 1.00f;
            this.uiSuccessR = 0.24f;
            this.uiSuccessG = 0.86f;
            this.uiSuccessB = 0.59f;
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
            this.uiThemeStylesDirty = true;
            this.uiThemeNextRebuildAt = 0f;
        }

        private Color GetUiThemeColorTargetValue(int target)
        {
            if (target == 0) return new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            if (target == 1) return new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            if (target == 2) return new Color(this.uiMainTabTextR, this.uiMainTabTextG, this.uiMainTabTextB);
            if (target == 3) return new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);
            if (target == 4) return new Color(this.uiWindowR, this.uiWindowG, this.uiWindowB);
            if (target == 5) return new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB);
            if (target == 6) return new Color(this.uiContentR, this.uiContentG, this.uiContentB);
            if (target == 7) return new Color(this.uiHeaderR, this.uiHeaderG, this.uiHeaderB);
            return new Color(this.uiSuccessR, this.uiSuccessG, this.uiSuccessB);
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
            else if (target == 6)
            {
                this.uiContentR = color.r; this.uiContentG = color.g; this.uiContentB = color.b;
            }
            else if (target == 7)
            {
                this.uiHeaderR = color.r; this.uiHeaderG = color.g; this.uiHeaderB = color.b;
            }
            else
            {
                this.uiSuccessR = color.r; this.uiSuccessG = color.g; this.uiSuccessB = color.b;
            }
        }

    }
}
