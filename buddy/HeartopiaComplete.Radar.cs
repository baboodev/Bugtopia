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
        private void PopulateRadarConfig(RadarConfigData data)
        {
            data.radarMarkerStyle = this.radarMarkerStyle;
            data.radarMaxDistance = this.radarMaxDistance;
            data.radarDisplayMode = this.radarDisplayMode;
            data.radarGameTrackLimit = this.radarGameTrackLimit;
            data.radarBigMapSpots = this.radarBigMapSpots;
            data.radarPlayerAvatarsAll = this.radarPlayerAvatarsAll;
            data.resourceVisualEspEnabled = this.resourceVisualEspEnabled;
            data.resourceVisualEspStyle = this.resourceVisualEspStyle;
            data.resourceVisualEspShowDistance = this.resourceVisualEspShowDistance;
            data.resourceVisualEspShowConnector = this.resourceVisualEspShowConnector;
            data.resourceVisualEspShowOffscreen = this.resourceVisualEspShowOffscreen;
            data.resourceVisualEspShowGroundRing = this.resourceVisualEspShowGroundRing;
            data.resourceVisualEspScale = this.resourceVisualEspScale;
            data.resourceVisualEspOpacity = this.resourceVisualEspOpacity;
            data.resourceVisualEspMaxMarkers = this.resourceVisualEspMaxMarkers;
            data.priorityFiddlehead = this.priorityFiddlehead;
            data.priorityTallMustard = this.priorityTallMustard;
            data.priorityBurdock = this.priorityBurdock;
            data.priorityMustardGreens = this.priorityMustardGreens;
        }

        private void ApplyRadarConfig(RadarConfigData data)
        {
            if (data == null) return;
            this.radarMarkerStyle = Mathf.Clamp(data.radarMarkerStyle, 0, 2);
            this.radarMaxDistance = Mathf.Clamp(data.radarMaxDistance <= 0f ? 75f : data.radarMaxDistance, 25f, 1000f);
            this.radarDisplayMode = Mathf.Clamp(data.radarDisplayMode, 0, 1);
            this.radarGameTrackLimit = Mathf.Clamp(data.radarGameTrackLimit <= 0 ? 5 : data.radarGameTrackLimit, 1, 30);
            this.radarBigMapSpots = data.radarBigMapSpots;
            this.radarPlayerAvatarsAll = data.radarPlayerAvatarsAll;
            this.resourceVisualEspEnabled = data.resourceVisualEspEnabled;
            bool showGroundRing = data.resourceVisualEspShowGroundRing;
            int visualEspStyle = data.resourceVisualEspStyle;
            if (visualEspStyle == 3)
            {
                visualEspStyle = 0;
                showGroundRing = true;
            }

            this.resourceVisualEspStyle = Mathf.Clamp(visualEspStyle, 0, 2);
            this.resourceVisualEspShowDistance = data.resourceVisualEspShowDistance;
            this.resourceVisualEspShowConnector = data.resourceVisualEspShowConnector;
            this.resourceVisualEspShowOffscreen = data.resourceVisualEspShowOffscreen;
            this.resourceVisualEspShowGroundRing = showGroundRing;
            this.resourceVisualEspScale = Mathf.Clamp(data.resourceVisualEspScale <= 0f ? 1f : data.resourceVisualEspScale, 0.8f, 1.5f);
            this.resourceVisualEspOpacity = Mathf.Clamp(data.resourceVisualEspOpacity <= 0f ? 0.92f : data.resourceVisualEspOpacity, 0.35f, 1f);
            this.resourceVisualEspMaxMarkers = Mathf.Clamp(data.resourceVisualEspMaxMarkers <= 0 ? 120 : data.resourceVisualEspMaxMarkers, 20, 200);
            this.priorityFiddlehead = data.priorityFiddlehead;
            this.priorityTallMustard = data.priorityTallMustard;
            this.priorityBurdock = data.priorityBurdock;
            this.priorityMustardGreens = data.priorityMustardGreens;
        }

        private string GetRadarSettingsPath()
        {
            return HelperPaths.GetFile("radar_settings.json");
        }

        private void SaveRadarSettings()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                ModLogger.Msg("Radar settings saved.");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Saving Radar Settings: " + ex.Message);
                this.AddMenuNotification(this.L("Failed to save radar settings"), new Color(1f, 0.4f, 0.4f));
            }
        }

        private void QueueRadarSettingsSave()
        {
            this.pendingRadarSettingsSave = true;
            this.nextRadarSettingsSaveAt = Time.unscaledTime + 0.6f;
        }

        private void FlushPendingRadarSettingsSave()
        {
            if (!this.pendingRadarSettingsSave || Time.unscaledTime < this.nextRadarSettingsSaveAt)
            {
                return;
            }

            this.pendingRadarSettingsSave = false;
            this.SaveRadarSettings();
        }

        private void LoadRadarSettings()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    this.ApplyRadarConfig(config.Radar);
                    ModLogger.Msg("Radar settings loaded.");
                    return;
                }
                string path = this.GetRadarSettingsPath();
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (line.Contains("radarMarkerStyle"))
                    {
                        int v = (int)GetJsonFloat(line, "\"radarMarkerStyle\":");
                        this.radarMarkerStyle = Mathf.Clamp(v, 0, 2);
                    }
                    else if (line.Contains("radarMaxDistance"))
                    {
                        this.radarMaxDistance = Mathf.Clamp(GetJsonFloat(line, "\"radarMaxDistance\":"), 25f, 1000f);
                    }
                    else if (line.Contains("priorityFiddlehead"))
                    {
                        this.priorityFiddlehead = GetJsonFloat(line, "\"priorityFiddlehead\":") != 0f;
                    }
                    else if (line.Contains("priorityTallMustard"))
                    {
                        this.priorityTallMustard = GetJsonFloat(line, "\"priorityTallMustard\":") != 0f;
                    }
                    else if (line.Contains("priorityBurdock"))
                    {
                        this.priorityBurdock = GetJsonFloat(line, "\"priorityBurdock\":") != 0f;
                    }
                    else if (line.Contains("priorityMustardGreens"))
                    {
                        this.priorityMustardGreens = GetJsonFloat(line, "\"priorityMustardGreens\":") != 0f;
                    }
                }
                ModLogger.Msg("Radar settings loaded.");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Loading Radar Settings: " + ex.Message);
            }
        }

        private float CalculateRadarTabHeight()
        {
            // Radar settings tab
            return 1180f; // Conservative estimate
        }

        private string GetMarkerDisplayTitle(RadarMarkerMetadata metadata)
        {
            if (metadata == null)
            {
                return string.Empty;
            }

            string localizedLabel = this.L(metadata.CanonicalLabel);
            if (this.radarMarkerStyle == 1)
            {
                return localizedLabel;
            }

            return metadata.IsCooldown
                ? metadata.Icon + " " + localizedLabel + " [CD]"
                : metadata.Icon + " " + localizedLabel;
        }

        private string NormalizeRadarIconSpriteKey(string key)
        {
            string normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.EndsWith(".png", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 4);
            }
            return normalized;
        }

        private string GetRadarSpeciesIconIndexPath()
        {
            return HelperPaths.GetFile("radar_species_icons.txt", "Cache");
        }

        private void LoadRadarSpeciesIconIndex()
        {
            this.radarStaticIdToIconKey.Clear();
            try
            {
                string path = this.GetRadarSpeciesIconIndexPath();
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = (rawLine ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string[] parts = line.Split(new[] { '|' }, 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    if (!int.TryParse(parts[0], out int staticId) || staticId <= 0)
                    {
                        continue;
                    }

                    string spriteKey = this.NormalizeRadarIconSpriteKey(parts[1]);
                    if (string.IsNullOrWhiteSpace(spriteKey))
                    {
                        continue;
                    }

                    this.radarStaticIdToIconKey[staticId] = spriteKey;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[RadarIconESP] Failed to load species icon index: " + ex.Message);
            }
        }

        private void SaveRadarSpeciesIconIndex()
        {
            try
            {
                string path = this.GetRadarSpeciesIconIndexPath();
                List<string> lines = this.radarStaticIdToIconKey
                    .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value))
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key.ToString() + "|" + pair.Value)
                    .ToList();
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[RadarIconESP] Failed to save species icon index: " + ex.Message);
            }
        }

        private void RememberRadarStaticIdIconMapping(int staticId, string spriteKey)
        {
            string normalizedKey = this.NormalizeRadarIconSpriteKey(spriteKey);
            if (staticId <= 0 || string.IsNullOrWhiteSpace(normalizedKey))
            {
                return;
            }

            if (this.radarStaticIdToIconKey.TryGetValue(staticId, out string existing)
                && string.Equals(existing, normalizedKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.radarStaticIdToIconKey[staticId] = normalizedKey;
            this.SaveRadarSpeciesIconIndex();
        }

        private bool TryGetRadarStaticIdIconKey(int staticId, out string spriteKey)
        {
            spriteKey = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            return this.radarStaticIdToIconKey.TryGetValue(staticId, out spriteKey) && !string.IsNullOrWhiteSpace(spriteKey);
        }

        private void LogRadarSpeciesDebug(string key, string message, float cooldownSeconds = 4f)
        {
            if (!RadarIconEspDebugLoggingEnabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (this.radarSpeciesDebugNextLogAt.TryGetValue(key, out float nextAllowedAt) && now < nextAllowedAt)
            {
                return;
            }

            this.radarSpeciesDebugNextLogAt[key] = now + Mathf.Max(0.5f, cooldownSeconds);
            ModLogger.Msg("[RadarIconESP] " + message);
        }

        private void BubbleRadarLog(string message)
        {
            if (!BubbleRadarDebugLoggingEnabled || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            ModLogger.Msg("[BubbleRadar] " + message);
        }

        private void BubbleRadarLogThrottled(string key, string message, float cooldownSeconds = 4f)
        {
            if (!BubbleRadarDebugLoggingEnabled || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (this.bubbleRadarDebugNextLogAt.TryGetValue(key, out float nextAllowedAt) && now < nextAllowedAt)
            {
                return;
            }

            this.bubbleRadarDebugNextLogAt[key] = now + Mathf.Max(0.5f, cooldownSeconds);
            ModLogger.Msg("[BubbleRadar] " + message);
        }

        private string TryGetRadarIconKeyFromTargetName(string canonicalLabel, GameObject targetObject)
        {
            if (targetObject == null)
            {
                return string.Empty;
            }

            string rawName = (targetObject.name ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            string normalizedName = rawName.Replace("(clone)", string.Empty).Trim();
            if (normalizedName.EndsWith("_t", StringComparison.Ordinal))
            {
                normalizedName = normalizedName.Substring(0, normalizedName.Length - 2);
            }

            switch ((canonicalLabel ?? string.Empty).Trim())
            {
                case "Bird":
                    if (normalizedName.StartsWith("p_bird_bird", StringComparison.Ordinal))
                    {
                        return normalizedName;
                    }
                    break;
                case "Insect":
                    if (normalizedName.StartsWith("p_insect_insect", StringComparison.Ordinal))
                    {
                        if (normalizedName.IndexOf("_aquarium", StringComparison.Ordinal) >= 0)
                        {
                            return normalizedName;
                        }
                        return normalizedName;
                    }
                    break;
            }

            return string.Empty;
        }

        private IEnumerable<string> GetRadarIconKeyCandidates(string canonicalLabel, string specificIconKey)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> candidates = new List<string>();

            void Add(string value)
            {
                string normalized = this.NormalizeRadarIconSpriteKey(value);
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                {
                    return;
                }
                candidates.Add(normalized);
            }

            Add(specificIconKey);
            Add(this.GetRadarIconSpriteKey(canonicalLabel));

            if (!string.IsNullOrWhiteSpace(specificIconKey))
            {
                string key = this.NormalizeRadarIconSpriteKey(specificIconKey);
                if (key.EndsWith("_t", StringComparison.Ordinal))
                {
                    Add(key.Substring(0, key.Length - 2));
                }
                if (key.IndexOf("_aquarium", StringComparison.Ordinal) >= 0)
                {
                    Add(key.Replace("_aquarium", string.Empty));
                }

                if (key.StartsWith("p_bird_bird", StringComparison.Ordinal))
                {
                    string suffix = key.Substring("p_bird_bird".Length);
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        Add("p_birdphoto_birdphoto" + suffix);
                    }
                }
                else if (key.StartsWith("p_birdphoto_birdphoto", StringComparison.Ordinal))
                {
                    string suffix = key.Substring("p_birdphoto_birdphoto".Length);
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        Add("p_bird_bird" + suffix);
                    }
                }
            }

            return candidates;
        }

        private bool TryResolveRadarTargetSpecificIconKey(string canonicalLabel, GameObject targetObject, out string spriteKey)
        {
            spriteKey = string.Empty;
            if (targetObject == null)
            {
                return false;
            }

            int staticId = 0;
            uint netId = 0U;
            string debugType = (canonicalLabel ?? string.Empty).Trim();
            switch ((canonicalLabel ?? string.Empty).Trim())
            {
                case "Bird":
                    if (!this.TryResolveBirdStaticIdFromGameObject(targetObject, out staticId, out _)
                        && this.TryResolveBirdNetId(targetObject, out uint birdNetId, out _))
                    {
                        netId = birdNetId;
                        staticId = this.TryGetEntityStaticId(birdNetId);
                    }
                    else
                    {
                        this.TryResolveBirdNetId(targetObject, out netId, out _);
                    }
                    break;
                case "Insect":
                    if (this.TryResolveInsectNetId(targetObject, out uint insectNetId, out _))
                    {
                        netId = insectNetId;
                        staticId = this.TryGetEntityStaticId(insectNetId);
                    }
                    break;
            }

            if (this.TryGetRadarStaticIdIconKey(staticId, out spriteKey))
            {
                this.LogRadarSpeciesDebug(
                    debugType + "|mapped|" + staticId.ToString(),
                    debugType + " icon mapped: staticId=" + staticId + " netId=" + netId + " key=" + spriteKey);
                return true;
            }

            string nameDerivedKey = this.TryGetRadarIconKeyFromTargetName(canonicalLabel, targetObject);
            if (!string.IsNullOrWhiteSpace(nameDerivedKey))
            {
                spriteKey = nameDerivedKey;
                this.LogRadarSpeciesDebug(
                    debugType + "|name-derived|" + nameDerivedKey,
                    debugType + " icon candidate from object name: netId=" + netId + " key=" + nameDerivedKey);
                return true;
            }

            if (staticId > 0 || netId > 0U)
            {
                this.LogRadarSpeciesDebug(
                    debugType + "|unmapped|" + staticId.ToString() + "|" + netId.ToString(),
                    debugType + " icon fallback: staticId=" + staticId + " netId=" + netId + " reason=no species icon mapping");
            }
            else if (!string.IsNullOrWhiteSpace(debugType))
            {
                string objectName = targetObject.name ?? "unknown";
                this.LogRadarSpeciesDebug(
                    debugType + "|unresolved|" + objectName,
                    debugType + " icon fallback: could not resolve target identity from object=" + objectName,
                    6f);
            }

            return false;
        }

        private string GetRadarIconSpriteKey(string canonicalLabel)
        {
            switch ((canonicalLabel ?? string.Empty).Trim())
            {
                case "Oyster":
                    return "p_gather_pleurotus_00";
                case "Button":
                    return "p_gather_tricholoma_00";
                case "Penny Bun":
                    return "p_gather_boletus_00";
                case "Shiitake":
                    return "p_gather_shiitake_00";
                case "Truffle":
                case "Black Truffle":
                    return "p_gather_truffle_00";
                case "Blueberry":
                    return "p_fruit_blueberry";
                case "Raspberry":
                    return "p_fruit_raspberry";
                case "Fiddlehead":
                    return "p_gather_bracken_00";
                case "Tall Mustard":
                    return "p_gather_garlicmustard_00";
                case "Burdock":
                    return "p_gather_burdock_00";
                case "Mustard Greens":
                    return "p_gather_shepherdspurse_00";
                case "Stone":
                    return "p_material_stone1";
                case "Ore":
                    return "p_material_stone2";
                case "Tree":
                    return "tree";
                case "Rare Tree":
                    return "rare_tree";
                case "Apple Tree":
                    return "p_fruit_apple";
                case "Mandarin Tree":
                    return "p_fruit_citrus";
                default:
                    return string.Empty;
            }
        }

        private Texture2D GetRadarIconFallbackTexture(string canonicalLabel)
        {
            string key = "fallback:" + ((canonicalLabel ?? "unknown").Trim().ToLowerInvariant());
            if (this.radarIconEspTextures.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            switch ((canonicalLabel ?? string.Empty).Trim())
            {
                case "Tree":
                    return this.CreateRadarIconFallbackTexture(key, new Color(0.28f, 0.72f, 0.34f), new Color(0.43f, 0.24f, 0.08f), false, false, true);
                case "Rare Tree":
                    return this.CreateRadarIconFallbackTexture(key, new Color(0.92f, 0.78f, 0.25f), new Color(0.43f, 0.24f, 0.08f), true, false, true);
                case "Bubble":
                    return this.CreateRadarIconFallbackTexture(key, new Color(0.58f, 0.88f, 1f, 0.95f), new Color(0.24f, 0.56f, 0.94f, 0.85f), true, false, false);
                case "Bird":
                    return this.CreateRadarIconFallbackTexture(key, new Color(0.96f, 0.88f, 0.34f), new Color(1f, 0.98f, 0.7f), false, true, false);
                case "Insect":
                    return this.CreateRadarIconFallbackTexture(key, new Color(1f, 0.65f, 0.2f), new Color(0.54f, 0.24f, 0.05f), false, true, false);
                case "Fish Shadow":
                    return this.CreateRadarIconFallbackTexture(key, new Color(0.26f, 0.72f, 1f), new Color(0.08f, 0.3f, 0.55f), false, false, false);
                case "Meteor":
                    return this.CreateRadarIconFallbackTexture(key, new Color(1f, 0.55f, 0.15f), new Color(0.98f, 0.83f, 0.36f), false, true, false);
                default:
                    return null;
            }
        }

        private bool TryResolveRadarIconFromLoadedSprites(string spriteKey, out Texture2D texture)
        {
            texture = null;
            try
            {
                string normalizedTarget = this.NormalizeAutoSellMatchKey(this.NormalizeRadarIconSpriteKey(spriteKey));
                if (string.IsNullOrWhiteSpace(normalizedTarget))
                {
                    return false;
                }

                Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                if (sprites == null || sprites.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < sprites.Length; i++)
                {
                    Sprite sprite = sprites[i];
                    if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
                    {
                        continue;
                    }

                    string normalizedName = this.NormalizeAutoSellMatchKey(this.NormalizeRadarIconSpriteKey(sprite.name));
                    if (!string.Equals(normalizedName, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Texture2D copy = this.CopySpriteTexture(sprite, "[RadarIconESP]");
                    if (copy == null)
                    {
                        continue;
                    }

                    this.SaveCachedItemIcon(normalizedTarget, copy);
                    texture = copy;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetRadarIconTexture(string canonicalLabel, out Texture2D texture)
        {
            return this.TryGetRadarIconTexture(canonicalLabel, string.Empty, out texture);
        }

        private bool TryGetRadarIconTexture(string canonicalLabel, string specificIconKey, out Texture2D texture)
        {
            texture = null;
            foreach (string spriteKey in this.GetRadarIconKeyCandidates(canonicalLabel, specificIconKey))
            {
                string normalizedKey = this.NormalizeRadarIconSpriteKey(spriteKey);
                if (this.radarIconEspTextures.TryGetValue(normalizedKey, out texture) && texture != null)
                {
                    return true;
                }

                float nextRetryAt;
                if (!this.radarIconEspRetryAt.TryGetValue(normalizedKey, out nextRetryAt) || Time.unscaledTime >= nextRetryAt)
                {
                    if (this.TryLoadCachedItemIcon(normalizedKey, out texture) && texture != null)
                    {
                        this.radarIconEspTextures[normalizedKey] = texture;
                        this.radarIconEspRetryAt.Remove(normalizedKey);
                        return true;
                    }

                    if (this.TryResolveRadarIconFromLoadedSprites(normalizedKey, out texture) && texture != null)
                    {
                        this.radarIconEspTextures[normalizedKey] = texture;
                        this.radarIconEspRetryAt.Remove(normalizedKey);
                        return true;
                    }

                    this.radarIconEspRetryAt[normalizedKey] = Time.unscaledTime + 5f;
                }
            }

            texture = this.GetRadarIconFallbackTexture(canonicalLabel);
            return texture != null;
        }

        private void DrawRadarIconEspOverlay()
        {
            if (!this.isRadarActive || this.radarMarkerStyle != 2 || this.radarContainer == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 cameraPos = cam.transform.position;
            float maxDistance = Mathf.Max(25f, this.radarMaxDistance);
            float iconSize = 55f;
            Color previousColor = GUI.color;

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                RadarMarkerMetadata metadata = this.GetMarkerMetadata(child.gameObject);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.CanonicalLabel))
                {
                    continue;
                }

                float distance = Vector3.Distance(cameraPos, child.position);
                float itemMaxDistance = string.Equals(metadata.CanonicalLabel, "Bubble", StringComparison.Ordinal)
                    ? Mathf.Max(BubbleRadarMaxDistance, maxDistance)
                    : maxDistance;
                if (distance > itemMaxDistance)
                {
                    continue;
                }

                if (!this.TryGetRadarIconTexture(metadata.CanonicalLabel, metadata.SpecificIconKey, out Texture2D texture) || texture == null)
                {
                    continue;
                }

                Vector3 screen = cam.WorldToScreenPoint(child.position + new Vector3(0f, 1.35f, 0f));
                if (screen.z <= 0.05f)
                {
                    continue;
                }

                float screenX = screen.x - (iconSize * 0.5f);
                float screenY = Screen.height - screen.y - iconSize;
                if (screenX < -iconSize || screenX > Screen.width || screenY < -iconSize || screenY > Screen.height)
                {
                    continue;
                }

                Rect shadowRect = new Rect(screenX + 1f, screenY + 2f, iconSize, iconSize);
                Rect iconRect = new Rect(screenX, screenY, iconSize, iconSize);
                float alpha = metadata.IsCooldown ? 0.38f : 0.96f;

                GUI.color = new Color(0f, 0f, 0f, alpha * 0.45f);
                GUI.DrawTexture(shadowRect, texture, ScaleMode.ScaleToFit, true);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true);
            }

            GUI.color = previousColor;
        }

        private RadarMarkerMetadata GetMarkerMetadata(GameObject marker)
        {
            if (marker == null)
            {
                return null;
            }

            RadarMarkerMetadata metadata;
            if (this.markerMetadataById.TryGetValue(marker.GetInstanceID(), out metadata))
            {
                return metadata;
            }

            return null;
        }

        private void SetMarkerMetadata(GameObject marker, RadarMarkerMetadata metadata)
        {
            if (marker == null || metadata == null)
            {
                return;
            }

            this.markerMetadataById[marker.GetInstanceID()] = metadata;
        }

        private void RemoveMarkerMetadata(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            this.markerMetadataById.Remove(marker.GetInstanceID());
        }

        private string GetMarkerCanonicalLabel(GameObject marker)
        {
            RadarMarkerMetadata metadata = this.GetMarkerMetadata(marker);
            if (metadata != null && !string.IsNullOrEmpty(metadata.CanonicalLabel))
            {
                return metadata.CanonicalLabel;
            }

            TextMesh label = marker != null ? marker.GetComponentInChildren<TextMesh>() : null;
            if (label == null || string.IsNullOrEmpty(label.text))
            {
                return string.Empty;
            }

            string[] lines = label.text.Split(new char[] { '\n' }, StringSplitOptions.None);
            string firstLine = lines.Length > 0 ? lines[0] : label.text;
            if (firstLine.EndsWith(" [CD]", StringComparison.Ordinal))
            {
                firstLine = firstLine.Substring(0, firstLine.Length - 5);
            }

            int firstSpace = firstLine.IndexOf(' ');
            if (firstSpace >= 0 && firstSpace < firstLine.Length - 1)
            {
                firstLine = firstLine.Substring(firstSpace + 1);
            }

            return firstLine.Trim();
        }

        private bool IsMarkerOnCooldown(GameObject marker)
        {
            RadarMarkerMetadata metadata = this.GetMarkerMetadata(marker);
            if (metadata != null)
            {
                return metadata.IsCooldown;
            }

            TextMesh label = marker != null ? marker.GetComponentInChildren<TextMesh>() : null;
            return label != null && !string.IsNullOrEmpty(label.text) && label.text.Contains("[CD]");
        }

        private void SetRadarSubTab(int subTab)
        {
            if (this.radarSubTab != subTab)
            {
                this.radarSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
            }
        }

        private Texture2D CreateRadarIconFallbackTexture(string key, Color primary, Color secondary, bool ring = false, bool diamond = false, bool addStem = false)
        {
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Vector2 center = new Vector2(15.5f, 15.5f);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Color pixel = Color.clear;
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);

                    if (diamond)
                    {
                        if (manhattan <= 10.5f)
                        {
                            pixel = primary;
                        }
                        if (manhattan <= 4.5f)
                        {
                            pixel = secondary;
                        }
                    }
                    else if (ring)
                    {
                        if (distance <= 11f && distance >= 7.5f)
                        {
                            pixel = primary;
                        }
                        else if (distance < 7.5f)
                        {
                            pixel = secondary;
                        }
                    }
                    else
                    {
                        if (distance <= 11f)
                        {
                            pixel = primary;
                        }
                        if (distance <= 5.5f)
                        {
                            pixel = secondary;
                        }
                    }

                    if (addStem && x >= 13 && x <= 18 && y >= 20 && y <= 30)
                    {
                        pixel = secondary;
                    }

                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            this.radarIconEspTextures[key] = texture;
            return texture;
        }

        private float DrawRadarSettingsTab(int startY)
        {
            int num = startY;
            float panelX = 20f;
            float panelWidth = 520f;
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 16;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label);
            subStyle.fontSize = 11;
            subStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.72f);

            GUI.Label(new Rect(panelX, (float)num, 280f, 24f), this.L("Radar Settings"), headerStyle);
            GUI.Label(new Rect(panelX, (float)num + 20f, 360f, 18f), this.L("Range, visual style, and overlay behavior."), subStyle);
            if (this.DrawDangerActionButton(new Rect(panelX + panelWidth - 118f, (float)num + 4f, 118f, 28f), "Reset Defaults"))
            {
                this.ResetRadarSettingsToDefaults();
            }
            num += 52;

            Rect rangeCard = new Rect(panelX, num, panelWidth, 112f);
            GUI.Box(rangeCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(rangeCard, 1f);

            GUIStyle cardTitleStyle = new GUIStyle(GUI.skin.label);
            cardTitleStyle.fontSize = 13;
            cardTitleStyle.fontStyle = FontStyle.Bold;
            cardTitleStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUI.Label(new Rect(rangeCard.x + 16f, rangeCard.y + 12f, 240f, 20f), this.L("Range"), cardTitleStyle);

            GUI.Label(new Rect(rangeCard.x + 16f, rangeCard.y + 42f, rangeCard.width - 32f, 20f), this.LF("Radar Max Distance: {0}m", this.radarMaxDistance.ToString("F0")));
            float prevRadarMaxDistance = this.radarMaxDistance;
            this.radarMaxDistance = Mathf.Round(this.DrawAccentSlider(new Rect(rangeCard.x + 16f, rangeCard.y + 68f, rangeCard.width - 32f, 20f), this.radarMaxDistance, 25f, 1000f));
            if (this.radarMaxDistance >= 995f)
            {
                this.radarMaxDistance = 1000f;
            }
            if (Math.Abs(this.radarMaxDistance - prevRadarMaxDistance) > 0.0001f)
            {
                this.QueueRadarSettingsSave();
                if (this.isRadarActive)
                {
                    this.lastScanTime = 0f;
                    this.bubbleRadarForceRefresh = true;
                }
            }
            num += 128;

            // Resource display routing: ESP screen overlay vs in-game map spots.
            GUI.Label(new Rect((float)panelX + 16f, (float)num + 6f, 200f, 18f), this.L("Resource Display"), subStyle);
            float modeSegGap = 10f;
            float modeSegWidth = (panelWidth - 32f - modeSegGap) / 2f;
            if (this.DrawRadarStyleSegmentButton(new Rect((float)panelX + 16f, (float)num + 26f, modeSegWidth, 30f), this.L("ESP Overlay"), this.radarDisplayMode == 0))
            {
                if (this.radarDisplayMode != 0)
                {
                    this.radarDisplayMode = 0;
                    this.OnRadarDisplayModeChanged();
                    this.QueueRadarSettingsSave();
                }
            }
            if (this.DrawRadarStyleSegmentButton(new Rect((float)panelX + 16f + modeSegWidth + modeSegGap, (float)num + 26f, modeSegWidth, 30f), this.L("Game Map"), this.radarDisplayMode == 1))
            {
                if (this.radarDisplayMode != 1)
                {
                    this.radarDisplayMode = 1;
                    this.OnRadarDisplayModeChanged();
                    this.QueueRadarSettingsSave();
                }
            }
            num += 64;

            if (this.radarDisplayMode == 1)
            {
                GUI.Label(new Rect((float)panelX + 16f, (float)num, 240f, 20f), this.LF("Map Markers (nearest): {0}", this.radarGameTrackLimit.ToString()), subStyle);
                int prevTrackLimit = this.radarGameTrackLimit;
                this.radarGameTrackLimit = Mathf.Clamp(Mathf.RoundToInt(this.DrawAccentSlider(new Rect((float)panelX + 190f, (float)num + 1f, panelWidth - 206f, 20f), (float)this.radarGameTrackLimit, 1f, 30f)), 1, 30);
                if (prevTrackLimit != this.radarGameTrackLimit)
                {
                    this.QueueRadarSettingsSave();
                }
                num += 36;

                GUI.Label(new Rect((float)panelX + 16f, (float)num + 6f, 200f, 20f), this.L("Show on big map"), subStyle);
                if (this.DrawRadarStyleSegmentButton(new Rect((float)panelX + 190f, (float)num, panelWidth - 206f, 30f),
                    this.radarBigMapSpots ? this.L("On") : this.L("Off"), this.radarBigMapSpots))
                {
                    this.radarBigMapSpots = !this.radarBigMapSpots;
                    this.QueueRadarSettingsSave();
                }
                num += 36;

                GUI.Label(new Rect((float)panelX + 16f, (float)num + 6f, 200f, 20f), this.L("Player Avatars (all)"), subStyle);
                if (this.DrawRadarStyleSegmentButton(new Rect((float)panelX + 190f, (float)num, panelWidth - 206f, 30f),
                    this.radarPlayerAvatarsAll ? this.L("On") : this.L("Off"), this.radarPlayerAvatarsAll))
                {
                    this.radarPlayerAvatarsAll = !this.radarPlayerAvatarsAll;
                    this.QueueRadarSettingsSave();
                }
                num += 36;
            }

            Rect visualCard = new Rect(panelX, num, panelWidth, 332f);
            GUI.Box(visualCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(visualCard, 1f);

            GUI.Label(new Rect(visualCard.x + 16f, visualCard.y + 12f, 220f, 20f), this.L("Visual ESP"), cardTitleStyle);
            GUI.Label(new Rect(visualCard.x + 16f, visualCard.y + 32f, visualCard.width - 32f, 18f), this.L("Clean screen-space overlay for resources and radar targets."), subStyle);

            GUI.Label(new Rect(visualCard.x + 16f, visualCard.y + 62f, 180f, 18f), this.L("Overlay Style"), subStyle);
            float segmentY = visualCard.y + 84f;
            float segmentGap = 10f;
            float segmentWidth = (visualCard.width - 32f - segmentGap * 2f) / 3f;
            if (this.DrawRadarStyleSegmentButton(new Rect(visualCard.x + 16f, segmentY, segmentWidth, 34f), this.L("Beacon"), this.resourceVisualEspStyle == 0))
            {
                if (this.resourceVisualEspStyle != 0)
                {
                    this.resourceVisualEspStyle = 0;
                    this.QueueRadarSettingsSave();
                }
            }
            if (this.DrawRadarStyleSegmentButton(new Rect(visualCard.x + 16f + segmentWidth + segmentGap, segmentY, segmentWidth, 34f), this.L("Card"), this.resourceVisualEspStyle == 1))
            {
                if (this.resourceVisualEspStyle != 1)
                {
                    this.resourceVisualEspStyle = 1;
                    this.QueueRadarSettingsSave();
                }
            }
            if (this.DrawRadarStyleSegmentButton(new Rect(visualCard.x + 16f + (segmentWidth + segmentGap) * 2f, segmentY, segmentWidth, 34f), this.L("Minimal"), this.resourceVisualEspStyle == 2))
            {
                if (this.resourceVisualEspStyle != 2)
                {
                    this.resourceVisualEspStyle = 2;
                    this.QueueRadarSettingsSave();
                }
            }

            bool prevShowDistance = this.resourceVisualEspShowDistance;
            bool prevShowConnector = this.resourceVisualEspShowConnector;
            bool prevShowOffscreen = this.resourceVisualEspShowOffscreen;
            bool prevShowGroundRing = this.resourceVisualEspShowGroundRing;
            float toggleY = segmentY + 52f;
            float toggleWidth = (visualCard.width - 48f) * 0.5f;
            this.resourceVisualEspShowDistance = this.DrawSwitchToggle(new Rect(visualCard.x + 16f, toggleY, toggleWidth, 24f), this.resourceVisualEspShowDistance, "Show Distance");
            this.resourceVisualEspShowConnector = this.DrawSwitchToggle(new Rect(visualCard.x + 24f + toggleWidth, toggleY, toggleWidth, 24f), this.resourceVisualEspShowConnector, "Connector Lines");
            this.resourceVisualEspShowOffscreen = this.DrawSwitchToggle(new Rect(visualCard.x + 16f, toggleY + 30f, toggleWidth, 24f), this.resourceVisualEspShowOffscreen, "Offscreen Chips");
            this.resourceVisualEspShowGroundRing = this.DrawSwitchToggle(new Rect(visualCard.x + 24f + toggleWidth, toggleY + 30f, toggleWidth, 24f), this.resourceVisualEspShowGroundRing, "Ground Ring");
            if (prevShowDistance != this.resourceVisualEspShowDistance || prevShowConnector != this.resourceVisualEspShowConnector || prevShowOffscreen != this.resourceVisualEspShowOffscreen || prevShowGroundRing != this.resourceVisualEspShowGroundRing)
            {
                this.QueueRadarSettingsSave();
            }

            float rowY = toggleY + 72f;
            GUI.Label(new Rect(visualCard.x + 16f, rowY, 220f, 20f), this.LF("Overlay Scale: {0}", this.resourceVisualEspScale.ToString("F2")));
            float prevVisualEspScale = this.resourceVisualEspScale;
            this.resourceVisualEspScale = Mathf.Round(this.DrawAccentSlider(new Rect(visualCard.x + 190f, rowY + 1f, visualCard.width - 206f, 20f), this.resourceVisualEspScale, 0.8f, 1.5f) * 100f) / 100f;
            if (Math.Abs(prevVisualEspScale - this.resourceVisualEspScale) > 0.0001f)
            {
                this.QueueRadarSettingsSave();
            }

            rowY += 42f;
            GUI.Label(new Rect(visualCard.x + 16f, rowY, 220f, 20f), this.LF("Overlay Opacity: {0}", this.resourceVisualEspOpacity.ToString("F2")));
            float prevVisualEspOpacity = this.resourceVisualEspOpacity;
            this.resourceVisualEspOpacity = Mathf.Round(this.DrawAccentSlider(new Rect(visualCard.x + 190f, rowY + 1f, visualCard.width - 206f, 20f), this.resourceVisualEspOpacity, 0.35f, 1f) * 100f) / 100f;
            if (Math.Abs(prevVisualEspOpacity - this.resourceVisualEspOpacity) > 0.0001f)
            {
                this.QueueRadarSettingsSave();
            }

            rowY += 42f;
            GUI.Label(new Rect(visualCard.x + 16f, rowY, 240f, 20f), this.LF("Overlay Marker Limit: {0}", this.resourceVisualEspMaxMarkers.ToString()));
            int prevVisualEspMaxMarkers = this.resourceVisualEspMaxMarkers;
            this.resourceVisualEspMaxMarkers = Mathf.Clamp(Mathf.RoundToInt(this.DrawAccentSlider(new Rect(visualCard.x + 190f, rowY + 1f, visualCard.width - 206f, 20f), (float)this.resourceVisualEspMaxMarkers, 20f, 200f)), 20, 200);
            if (prevVisualEspMaxMarkers != this.resourceVisualEspMaxMarkers)
            {
                this.QueueRadarSettingsSave();
            }

            num += 348;
            return (float)num;
        }

        private bool DrawRadarStyleSegmentButton(Rect rect, string label, bool selected)
        {
            GUI.Box(rect, "", selected
                ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box)
                : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));

            GUIStyle textStyle = new GUIStyle(GUI.skin.label);
            textStyle.fontSize = 12;
            textStyle.fontStyle = FontStyle.Bold;
            textStyle.alignment = TextAnchor.MiddleCenter;
            textStyle.normal.textColor = selected
                ? new Color(0.98f, 0.99f, 1f, 1f)
                : new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);

            GUI.Label(rect, selected ? label + "  [ON]" : label, textStyle);
            return GUI.Button(rect, "", GUIStyle.none);
        }

        private void ResetRadarSettingsToDefaults()
        {
            this.radarMaxDistance = 75f;
            this.radarDisplayMode = 0;
            this.radarGameTrackLimit = 5;
            this.radarBigMapSpots = false;
            this.radarPlayerAvatarsAll = false;
            this.resourceVisualEspEnabled = true;
            this.resourceVisualEspStyle = 0;
            this.resourceVisualEspShowDistance = true;
            this.resourceVisualEspShowConnector = true;
            this.resourceVisualEspShowOffscreen = true;
            this.resourceVisualEspShowGroundRing = false;
            this.resourceVisualEspScale = 1f;
            this.resourceVisualEspOpacity = 0.92f;
            this.resourceVisualEspMaxMarkers = 120;
            this.QueueRadarSettingsSave();

            if (this.isRadarActive)
            {
                this.lastScanTime = 0f;
                this.bubbleRadarForceRefresh = true;
            }

            this.AddMenuNotification("Radar settings reset", new Color(0.55f, 0.88f, 1f));
        }

        // Token: 0x0600000A RID: 10 RVA: 0x000030E8 File Offset: 0x000012E8
        private float DrawRadarTab(int startY)
        {
            int num = startY;
            if (this.radarSubTab == 1)
            {
                return this.DrawRadarSettingsTab(startY);
            }
            string text = this.isRadarActive ? "DISABLE RADAR" : "ENABLE RADAR";
            bool flag = this.DrawPrimaryActionButton(new Rect(20f, (float)num, 260f, 40f), text);
            if (flag)
            {
                this.ToggleRadar();
            }
            num += 50;

            // Select / Clear shortcuts
            if (this.DrawPrimaryActionButton(new Rect(20f, (float)num, 125f, 30f), "Select All Loots"))
            {
                this.showMushroomRadar = true;
                this.showOysterMushroomRadar = true;
                this.showButtonMushroomRadar = true;
                this.showPennyBunRadar = true;
                this.showShiitakeRadar = true;
                this.showTruffleRadar = true;
                this.showFiddleheadRadar = true;
                this.showTallMustardRadar = true;
                this.showBurdockRadar = true;
                this.showMustardGreensRadar = true;
                this.showBlueberryRadar = true;
                this.showRaspberryRadar = true;
                this.showStoneRadar = true;
                this.showOreRadar = true;
                this.showTreeRadar = true;
                this.showRareTreeRadar = true;
                this.showAppleTreeRadar = true;
                this.showOrangeTreeRadar = true;
                this.showBubbleRadar = true;
                this.showBirdRadar = true;
                this.showInsectRadar = true;
                this.showFishShadowRadar = true;
                this.showMeteorRadar = true;
                this.showOtherPlayersRadar = true;
                this.CheckRadarAutoToggle();
                if (this.isRadarActive) this.RunRadar();
            }
            if (this.DrawPrimaryActionButton(new Rect(155f, (float)num, 125f, 30f), "Clear All Loots"))
            {
                this.showMushroomRadar = false;
                this.showOysterMushroomRadar = false;
                this.showButtonMushroomRadar = false;
                this.showPennyBunRadar = false;
                this.showShiitakeRadar = false;
                this.showTruffleRadar = false;
                this.showFiddleheadRadar = false;
                this.showTallMustardRadar = false;
                this.showBurdockRadar = false;
                this.showMustardGreensRadar = false;
                this.showBlueberryRadar = false;
                this.showRaspberryRadar = false;
                this.showStoneRadar = false;
                this.showOreRadar = false;
                this.showTreeRadar = false;
                this.showRareTreeRadar = false;
                this.showAppleTreeRadar = false;
                this.showOrangeTreeRadar = false;
                this.showBubbleRadar = false;
                this.showBirdRadar = false;
                this.showInsectRadar = false;
                this.showFishShadowRadar = false;
                this.showMeteorRadar = false;
                this.showOtherPlayersRadar = false;
                this.CheckRadarAutoToggle();
                this.Cleanup();
            }
            num += 45;

            num = this.DrawRadarMushroomDropdown(num);
            num = this.DrawRadarBerriesDropdown(num);
            num = this.DrawRadarEventsDropdown(num);
            num = this.DrawRadarResourcesDropdown(num);
            num = this.DrawRadarTreesDropdown(num);
            num = this.DrawRadarMiscDropdown(num);
            bool flag21 = this.DrawPrimaryActionButton(new Rect(20f, (float)num, 260f, 30f), "Force Refresh Scan") && this.isRadarActive;
            if (flag21)
            {
                this.RunRadar();
            }
            num += 40;

            GUI.Label(new Rect(20f, (float)num, 260f, 120f), "  Credits: OG dll creator :)\n- breckdareck for ForagerRadar");
            return (float)num + 130f;
        }

        private int DrawRadarDropdownHeader(int y, string title, string summary, ref bool isOpen)
        {
            GUI.Label(new Rect(20f, y, 260f, 20f), "== " + this.L(title) + " ==");
            y += 24;

            Rect dropdownRect = new Rect(20f, y, 260f, 28f);
            GUI.Box(dropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(dropdownRect, 1f);
            if (GUI.Button(dropdownRect, "", GUIStyle.none))
            {
                bool nextOpen = !isOpen;
                if (nextOpen)
                {
                    this.CloseAllRadarDropdowns();
                }
                isOpen = nextOpen;
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

            GUI.Label(new Rect(dropdownRect.x + 10f, dropdownRect.y + 1f, dropdownRect.width - 32f, dropdownRect.height - 2f), summary, valueStyle);
            GUI.Label(new Rect(dropdownRect.xMax - 22f, dropdownRect.y + 1f, 14f, dropdownRect.height - 2f), isOpen ? "^" : "v", arrowStyle);

            y += 34;
            return y;
        }

        private int DrawSingleSelectRadarStyleDropdown(int y, string title, string summary, string[] options, ref int selectedIndex, ref bool isOpen)
        {
            Rect dropdownRect = new Rect(20f, y, 260f, 28f);
            GUI.Box(dropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(dropdownRect, 1f);
            if (GUI.Button(dropdownRect, "", GUIStyle.none))
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

            GUI.Label(new Rect(dropdownRect.x + 10f, dropdownRect.y + 1f, dropdownRect.width - 32f, dropdownRect.height - 2f), summary, valueStyle);
            GUI.Label(new Rect(dropdownRect.xMax - 22f, dropdownRect.y + 1f, 14f, dropdownRect.height - 2f), isOpen ? "^" : "v", arrowStyle);

            y += 34;
            if (!isOpen)
            {
                return y + 8;
            }

            int optionCount = options.Length;
            float dropdownHeight = (optionCount * 28f) + 8f;
            Rect optionsBoxRect = new Rect(24f, y, 252f, dropdownHeight);
            GUI.Box(optionsBoxRect, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(optionsBoxRect, 1f);

            for (int i = 0; i < options.Length; i++)
            {
                bool isSelected = i == selectedIndex;
                Rect optionRect = new Rect(28f, y, 244f, 26f);
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

                GUI.Label(
                    new Rect(optionRect.x + 10f, optionRect.y + 1f, optionRect.width - 20f, optionRect.height - 2f),
                    (isSelected ? "> " : "") + this.L(options[i]),
                    optionStyle);

                if (clicked)
                {
                    selectedIndex = i;
                    isOpen = false;
                }

                y += 28;
            }

            return y + 8;
        }

        private void CloseAllRadarDropdowns()
        {
            this.radarMushroomsDropdownOpen = false;
            this.radarBerriesDropdownOpen = false;
            this.radarEventsDropdownOpen = false;
            this.radarResourcesDropdownOpen = false;
            this.radarTreesDropdownOpen = false;
            this.radarMiscDropdownOpen = false;
        }

        private int DrawRadarMushroomDropdown(int y)
        {
            string summary;
            if (this.showMushroomRadar)
            {
                summary = this.L("All Mushrooms");
            }
            else
            {
                List<string> selected = new List<string>();
                if (this.showOysterMushroomRadar) selected.Add("Oyster Mushroom");
                if (this.showButtonMushroomRadar) selected.Add("Button Mushroom");
                if (this.showPennyBunRadar) selected.Add("Penny Bun");
                if (this.showShiitakeRadar) selected.Add("Shiitake");
                if (this.showTruffleRadar) selected.Add("Black Truffle");
                summary = this.GetRadarSelectionSummary(selected);
            }
            y = this.DrawRadarDropdownHeader(y, "Mushrooms", summary, ref this.radarMushroomsDropdownOpen);
            if (!this.radarMushroomsDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool nextAllMush = this.DrawRadarDropdownOption(y, "All Mushrooms", this.showMushroomRadar);
            if (nextAllMush != this.showMushroomRadar)
            {
                this.showMushroomRadar = nextAllMush;
                this.showOysterMushroomRadar = nextAllMush;
                this.showButtonMushroomRadar = nextAllMush;
                this.showPennyBunRadar = nextAllMush;
                this.showShiitakeRadar = nextAllMush;
                this.showTruffleRadar = nextAllMush;
                changed = true;
            }
            y += 30;

            bool v = this.DrawRadarDropdownOption(y, "Oyster Mushroom", this.showOysterMushroomRadar);
            if (v != this.showOysterMushroomRadar) { this.showOysterMushroomRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Button Mushroom", this.showButtonMushroomRadar);
            if (v != this.showButtonMushroomRadar) { this.showButtonMushroomRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Penny Bun", this.showPennyBunRadar);
            if (v != this.showPennyBunRadar) { this.showPennyBunRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Shiitake", this.showShiitakeRadar);
            if (v != this.showShiitakeRadar) { this.showShiitakeRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Black Truffle", this.showTruffleRadar);
            if (v != this.showTruffleRadar) { this.showTruffleRadar = v; changed = true; }
            y += 30;

            if (changed)
            {
                this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private int DrawRadarBerriesDropdown(int y)
        {
            List<string> selected = new List<string>();
            if (this.showBlueberryRadar) selected.Add("Blueberries");
            if (this.showRaspberryRadar) selected.Add("Raspberries");
            string summary = this.GetRadarSelectionSummary(selected);
            y = this.DrawRadarDropdownHeader(y, "Berries", summary, ref this.radarBerriesDropdownOpen);
            if (!this.radarBerriesDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool v = this.DrawRadarDropdownOption(y, "Blueberries", this.showBlueberryRadar);
            if (v != this.showBlueberryRadar) { this.showBlueberryRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Raspberries", this.showRaspberryRadar);
            if (v != this.showRaspberryRadar) { this.showRaspberryRadar = v; changed = true; }
            y += 30;

            if (changed)
            {
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private int DrawRadarEventsDropdown(int y)
        {
            List<string> selected = new List<string>();
            if (this.showFiddleheadRadar) selected.Add("Fiddlehead");
            if (this.showTallMustardRadar) selected.Add("Tall Mustard");
            if (this.showBurdockRadar) selected.Add("Burdock");
            if (this.showMustardGreensRadar) selected.Add("Mustard Greens");
            string summary = this.GetRadarSelectionSummary(selected);
            y = this.DrawRadarDropdownHeader(y, "Events", summary, ref this.radarEventsDropdownOpen);
            if (!this.radarEventsDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool v = this.DrawRadarDropdownOption(y, "Fiddlehead", this.showFiddleheadRadar);
            if (v != this.showFiddleheadRadar) { this.showFiddleheadRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Tall Mustard", this.showTallMustardRadar);
            if (v != this.showTallMustardRadar) { this.showTallMustardRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Burdock", this.showBurdockRadar);
            if (v != this.showBurdockRadar) { this.showBurdockRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Mustard Greens", this.showMustardGreensRadar);
            if (v != this.showMustardGreensRadar) { this.showMustardGreensRadar = v; changed = true; }
            y += 30;

            if (changed)
            {
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private int DrawRadarResourcesDropdown(int y)
        {
            List<string> selected = new List<string>();
            if (this.showStoneRadar) selected.Add("Stones");
            if (this.showOreRadar) selected.Add("Ores");
            string summary = this.GetRadarSelectionSummary(selected);
            y = this.DrawRadarDropdownHeader(y, "Resources", summary, ref this.radarResourcesDropdownOpen);
            if (!this.radarResourcesDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool v = this.DrawRadarDropdownOption(y, "Stones", this.showStoneRadar);
            if (v != this.showStoneRadar) { this.showStoneRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Ores", this.showOreRadar);
            if (v != this.showOreRadar) { this.showOreRadar = v; changed = true; }
            y += 30;

            if (changed)
            {
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private int DrawRadarTreesDropdown(int y)
        {
            List<string> selected = new List<string>();
            if (this.showTreeRadar) selected.Add("Trees");
            if (this.showRareTreeRadar) selected.Add("Rare Trees");
            if (this.showAppleTreeRadar) selected.Add("Apple Trees");
            if (this.showOrangeTreeRadar) selected.Add("Mandarin Trees");
            string summary = this.GetRadarSelectionSummary(selected);
            y = this.DrawRadarDropdownHeader(y, "Trees", summary, ref this.radarTreesDropdownOpen);
            if (!this.radarTreesDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool v = this.DrawRadarDropdownOption(y, "Trees", this.showTreeRadar);
            if (v != this.showTreeRadar) { this.showTreeRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Rare Trees", this.showRareTreeRadar);
            if (v != this.showRareTreeRadar) { this.showRareTreeRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Apple Trees", this.showAppleTreeRadar);
            if (v != this.showAppleTreeRadar) { this.showAppleTreeRadar = v; changed = true; }
            y += 30;
            v = this.DrawRadarDropdownOption(y, "Mandarin Trees", this.showOrangeTreeRadar);
            if (v != this.showOrangeTreeRadar) { this.showOrangeTreeRadar = v; changed = true; }
            y += 30;

            if (changed)
            {
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private int DrawRadarMiscDropdown(int y)
        {
            List<string> selected = new List<string>();
            if (this.showBubbleRadar) selected.Add("Bubbles");
            if (this.showBirdRadar) selected.Add("Birds");
            if (this.showInsectRadar) selected.Add("Insects");
            if (this.showFishShadowRadar) selected.Add("Fish Shadows");
            if (this.showMeteorRadar) selected.Add("Meteors");
            if (this.showOtherPlayersRadar) selected.Add("Players");
            string summary = this.GetRadarSelectionSummary(selected);
            y = this.DrawRadarDropdownHeader(y, "Misc", summary, ref this.radarMiscDropdownOpen);
            if (!this.radarMiscDropdownOpen)
            {
                return y + 8;
            }

            bool changed = false;
            bool v = this.DrawRadarDropdownOption(y, "Bubbles", this.showBubbleRadar);
            if (v != this.showBubbleRadar) { this.showBubbleRadar = v; changed = true; }
            y += 30;

            bool prevBird = this.showBirdRadar;
            v = this.DrawRadarDropdownOption(y, "Birds", this.showBirdRadar);
            if (v != prevBird)
            {
                this.showBirdRadar = v;
                changed = true;
                if (!this.showBirdRadar)
                {
                    this.RemoveTrackedMarkersByNameContains("p_bird_bird");
                    this.RemoveTrackedMarkersByNameContains("p_bird_");
                    this.RemoveTrackedMarkersByNameContains("bird");
                }
            }
            y += 30;

            bool prevInsect = this.showInsectRadar;
            v = this.DrawRadarDropdownOption(y, "Insects", this.showInsectRadar);
            if (v != prevInsect)
            {
                this.showInsectRadar = v;
                changed = true;
                if (this.showInsectRadar)
                {
                    if (this.isRadarActive)
                    {
                        this.RunRadar();
                    }
                }
                else
                {
                    this.RemoveTrackedMarkersByNameContains("p_insect_insect");
                    this.RemoveTrackedMarkersByNameContains("p_insect_");
                    this.RemoveTrackedMarkersByNameContains("insect");
                }
            }
            y += 30;

            bool prevFish = this.showFishShadowRadar;
            v = this.DrawRadarDropdownOption(y, "Fish Shadows", this.showFishShadowRadar);
            if (v != prevFish)
            {
                this.showFishShadowRadar = v;
                changed = true;
                if (!this.showFishShadowRadar)
                {
                    this.RemoveTrackedMarkersByNameContains("fishshadow");
                    this.RemoveTrackedMarkersByNameContains("fish_shadow");
                    this.RemoveTrackedMarkersByNameContains("p_fish");
                }
            }
            y += 30;

            bool prevMeteor = this.showMeteorRadar;
            v = this.DrawRadarDropdownOption(y, "Meteors", this.showMeteorRadar);
            if (v != prevMeteor)
            {
                this.showMeteorRadar = v;
                changed = true;
                if (!this.showMeteorRadar)
                {
                    this.RemoveTrackedMarkersByNameContains("p_rock_meteorite");
                    this.RemoveTrackedMarkersByNameContains("meteorite");
                }
            }
            y += 30;

            bool prevOtherPlayers = this.showOtherPlayersRadar;
            v = this.DrawRadarDropdownOption(y, "Other Players", this.showOtherPlayersRadar);
            if (v != prevOtherPlayers)
            {
                this.showOtherPlayersRadar = v;
                changed = true;
                if (!this.showOtherPlayersRadar)
                {
                    this.RemoveTrackedMarkersByNameContains("p_player_skeleton");
                    this.ClearHideAndSeekMorphMarkers();
                }
                else if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }
            y += 30;

            if (changed)
            {
                this.CheckRadarAutoToggle();
                if (this.isRadarActive)
                {
                    this.RunRadar();
                }
            }

            return y + 8;
        }

        private bool DrawRadarDropdownOption(int y, string label, bool selected)
        {
            Rect optionRect = new Rect(28f, y, 252f, 26f);
            GUIStyle optionStyle = this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box;
            GUIStyle optionSelectedStyle = this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box;
            GUI.Box(optionRect, "", selected ? optionSelectedStyle : optionStyle);

            if (GUI.Button(optionRect, "", GUIStyle.none))
            {
                return !selected;
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = selected ? Color.white : new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(optionRect, this.L(label), labelStyle);

            return selected;
        }

        private string GetRadarSelectionSummary(List<string> selected)
        {
            if (selected == null || selected.Count == 0)
            {
                return this.L("None");
            }

            string joined = string.Join(", ", selected.Select(new Func<string, string>(this.L)).ToArray());
            const int maxLen = 30;
            if (joined.Length <= maxLen)
            {
                return joined;
            }

            return joined.Substring(0, maxLen - 3) + "...";
        }

        private void RemoveTrackedMarkersByNameContains(string targetNamePart)
        {
            if (string.IsNullOrEmpty(targetNamePart) || this.radarContainer == null)
            {
                return;
            }

            List<GameObject> markersToDestroy = new List<GameObject>();
            List<int> trackedIdsToRemove = new List<int>();

            foreach (KeyValuePair<int, GameObject> entry in this.trackedObjectMarkers)
            {
                GameObject marker = entry.Value;
                if (marker == null || !marker.name.StartsWith("TrackedMarker_"))
                {
                    continue;
                }

                GameObject target = null;
                foreach (KeyValuePair<GameObject, GameObject> mapping in this.markerToTarget)
                {
                    if (mapping.Key == marker)
                    {
                        target = mapping.Value;
                        break;
                    }
                }

                if (target != null && target.name.ToLower().Contains(targetNamePart))
                {
                    markersToDestroy.Add(marker);
                    trackedIdsToRemove.Add(entry.Key);
                }
            }

            foreach (GameObject marker in markersToDestroy)
            {
                this.markerToTarget.Remove(marker);
                Object.Destroy(marker);
            }

            foreach (int id in trackedIdsToRemove)
            {
                this.trackedObjectMarkers.Remove(id);
            }
        }

        private bool TryResolveManagedBubbleMarker(object bubbleComponent, out int markerId, out Vector3 position)
        {
            markerId = 0;
            position = Vector3.zero;
            if (bubbleComponent == null)
            {
                return false;
            }

            object componentData = this.TryGetManagedMemberValue(bubbleComponent, "ComponentData")
                ?? this.TryGetManagedMemberValue(bubbleComponent, "_componentData")
                ?? this.TryGetManagedMemberValue(bubbleComponent, "componentData");
            if (componentData == null)
            {
                return false;
            }

            int bubbleId = this.TryReadIntMember(componentData, "bubbleId", out int resolvedBubbleId) ? resolvedBubbleId : 0;
            uint netId = 0U;
            object entityObj = this.TryGetManagedMemberValue(bubbleComponent, "entity");
            if (entityObj != null)
            {
                if (!this.TryReadManagedNetIdMember(entityObj, "netId", out netId))
                {
                    this.TryInvokeManagedNetIdMethod(entityObj, "GetNetId", out netId);
                }

                if (this.TryGetObjectMember(entityObj, "position", out object entityPositionObj) && entityPositionObj is Vector3 entityPosition && entityPosition != Vector3.zero)
                {
                    position = entityPosition;
                }
                else if (this.TryGetObjectMember(entityObj, "worldPosition", out object entityWorldPositionObj) && entityWorldPositionObj is Vector3 entityWorldPosition && entityWorldPosition != Vector3.zero)
                {
                    position = entityWorldPosition;
                }
            }

            if (position == Vector3.zero && this.TryGetObjectMember(bubbleComponent, "transform", out object transformObj) && transformObj is Transform componentTransform && componentTransform != null)
            {
                position = componentTransform.position;
            }

            if (position == Vector3.zero && this.TryGetObjectMember(componentData, "bornPosition", out object bornPositionObj) && bornPositionObj is Vector3 bornPosition && bornPosition != Vector3.zero)
            {
                position = bornPosition;
            }

            if (position == Vector3.zero && this.TryGetObjectMember(componentData, "tarPosition", out object targetPositionObj) && targetPositionObj is Vector3 targetPosition && targetPosition != Vector3.zero)
            {
                position = targetPosition;
            }

            if (position == Vector3.zero && netId != 0U)
            {
                if (!this.TryGetEntityPositionByNetIdMono(netId, out position))
                {
                    this.TryGetEntityPositionByNetId(netId, out position);
                }
            }

            if (netId != 0U)
            {
                markerId = unchecked((int)netId);
            }
            else if (bubbleId != 0)
            {
                markerId = bubbleId;
            }
            else if (this.TryReadFloatMember(componentData, "bubbleLocationID", out float bubbleLocationId) && Math.Abs(bubbleLocationId) > 0.001f)
            {
                markerId = Mathf.RoundToInt(bubbleLocationId * 1000f);
            }

            if (markerId == 0 && position != Vector3.zero)
            {
                markerId = (Mathf.RoundToInt(position.x * 10f) * 397) ^ Mathf.RoundToInt(position.z * 10f);
            }

            return markerId != 0 && position != Vector3.zero;
        }

        private bool TryResolveAuraMonoBubbleEntityMarker(IntPtr entityObj, out int markerId, out Vector3 position)
        {
            markerId = 0;
            position = Vector3.zero;
            if (entityObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
            {
                return false;
            }

            try
            {
                IntPtr entityClass = auraMonoObjectGetClass(entityObj);
                if (entityClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
                if (getAllComponentsMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr invokeExc = IntPtr.Zero;
                IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref invokeExc);
                if (invokeExc != IntPtr.Zero || componentsObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> components = this.bubbleRadarAuraComponentsBuffer;
                components.Clear();
                if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
                {
                    return false;
                }

                bool hasBubbleSignature = false;
                int bid = 0;
                int refreshType = 0;
                uint netId = 0U;
                for (int i = 0; i < components.Count && i < 96; i++)
                {
                    IntPtr componentObj = components[i];
                    if (componentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                    if (string.IsNullOrEmpty(className) || className.IndexOf("Bubble", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    hasBubbleSignature = true;

                    if (className.IndexOf("BubbleLocationComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!this.TryResolveAuraMonoBubbleLocation(componentObj, out position))
                        {
                            this.TryResolveAuraMonoBubbleLocationFromNestedData(componentObj, out position);
                        }
                    }
                    else if (className.IndexOf("BubbleIdComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        this.TryGetMonoInt32Member(componentObj, "bid", out bid);
                        this.TryGetMonoInt32Member(componentObj, "type", out refreshType);
                        if (netId == 0U)
                        {
                            this.TryGetMonoUInt32Member(componentObj, "netId", out netId);
                            if (netId == 0U)
                            {
                                this.TryGetMonoUInt32Member(componentObj, "_netId", out netId);
                            }
                        }
                    }
                }

                if (!hasBubbleSignature)
                {
                    return false;
                }

                if (netId == 0U)
                {
                    this.TryGetAuraMonoEntityNetId(entityObj, out netId);
                }

                if (position == Vector3.zero)
                {
                    this.TryGetAuraMonoEntityPosition(entityObj, out position);
                }

                if (position == Vector3.zero && netId != 0U)
                {
                    if (!this.TryGetEntityPositionByNetIdMono(netId, out position))
                    {
                        this.TryGetEntityPositionByNetId(netId, out position);
                    }
                }

                if (netId != 0U)
                {
                    markerId = unchecked((int)netId);
                }

                if (markerId == 0 && (bid != 0 || refreshType != 0))
                {
                    markerId = (bid * 397) ^ (refreshType * 31);
                }

                if (markerId == 0 && position != Vector3.zero)
                {
                    markerId = (Mathf.RoundToInt(position.x * 10f) * 397) ^ Mathf.RoundToInt(position.z * 10f);
                }

                return markerId != 0 && position != Vector3.zero;
            }
            catch (Exception ex)
            {
                this.BubbleRadarLogThrottled("aura-entity-resolve-error", "Aura bubble entity resolve error: " + ex.GetType().Name + " - " + ex.Message, 6f);
                return false;
            }
        }

        private bool TryResolveBubbleEntityMarker(object bubbleEntity, out int markerId, out Vector3 position)
        {
            markerId = 0;
            position = Vector3.zero;

            if (bubbleEntity == null)
            {
                return false;
            }

            try
            {
                if (this.cachedBubbleOptDataType == null)
                {
                    this.cachedBubbleOptDataType = this.FindLoadedType(
                        "XDT.Scene.Shared.Entity.EntityOptData.BubbleEntityOptData",
                        "Il2CppXDT.Scene.Shared.Entity.EntityOptData.BubbleEntityOptData",
                        "BubbleEntityOptData")
                        ?? this.FindLoadedTypeBySuffix("BubbleEntityOptData");
                }

                if (this.cachedBubbleOptDataType == null)
                {
                    return false;
                }

                if (this.cachedBubbleOptDataAsMethod == null)
                {
                    this.cachedBubbleOptDataAsMethod = this.cachedBubbleOptDataType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "As" && m.GetParameters().Length >= 1);
                }

                if (this.cachedBubbleOptDataAsMethod == null)
                {
                    return false;
                }

                ParameterInfo[] asParameters = this.cachedBubbleOptDataAsMethod.GetParameters();
                object[] asArgs = asParameters.Length >= 2
                    ? new object[] { bubbleEntity, Activator.CreateInstance(asParameters[1].ParameterType) }
                    : new object[] { bubbleEntity };
                object bubbleOptData = this.cachedBubbleOptDataAsMethod.Invoke(null, asArgs);
                if (bubbleOptData == null)
                {
                    return false;
                }

                if (this.cachedBubbleOptDataGetNetIdMethod == null)
                {
                    this.cachedBubbleOptDataGetNetIdMethod = this.cachedBubbleOptDataType.GetMethod("GetNetId", BindingFlags.Public | BindingFlags.Instance);
                }

                uint netId = 0U;
                if (this.cachedBubbleOptDataGetNetIdMethod != null)
                {
                    object netIdObj = this.cachedBubbleOptDataGetNetIdMethod.Invoke(bubbleOptData, null);
                    this.TryConvertToUInt(netIdObj, out netId);
                }

                if (this.cachedBubbleLocationComponentType == null)
                {
                    this.cachedBubbleLocationComponentType = this.FindLoadedType(
                        "XDT.Scene.Shared.Modules.Bubble.BubbleLocationComponent",
                        "Il2CppXDT.Scene.Shared.Modules.Bubble.BubbleLocationComponent",
                        "BubbleLocationComponent")
                        ?? this.FindLoadedTypeBySuffix("BubbleLocationComponent");
                }

                if (this.TryGetBubbleOptComponentValue(bubbleOptData, "BubbleLocationComponent", this.cachedBubbleLocationComponentType, out object locationCompObj)
                    && this.TryGetObjectMember(locationCompObj, "value", out object locationValueObj)
                    && locationValueObj is Vector3 locationVector)
                {
                    position = locationVector;
                }

                if (position == Vector3.zero && netId != 0U)
                {
                    if (!this.TryGetEntityPositionByNetIdMono(netId, out position))
                    {
                        this.TryGetEntityPositionByNetId(netId, out position);
                    }
                }

                if (netId != 0U)
                {
                    markerId = unchecked((int)netId);
                }

                if (markerId == 0)
                {
                    if (this.cachedBubbleIdComponentType == null)
                    {
                        this.cachedBubbleIdComponentType = this.FindLoadedType(
                            "XDT.Scene.Shared.Modules.Bubble.BubbleIdComponent",
                            "Il2CppXDT.Scene.Shared.Modules.Bubble.BubbleIdComponent",
                            "BubbleIdComponent")
                            ?? this.FindLoadedTypeBySuffix("BubbleIdComponent");
                    }

                    if (this.TryGetBubbleOptComponentValue(bubbleOptData, "BubbleIdComponent", this.cachedBubbleIdComponentType, out object bubbleIdCompObj))
                    {
                        int bid = this.TryReadIntMember(bubbleIdCompObj, "bid", out int bidValue) ? bidValue : 0;
                        int refreshType = this.TryReadIntMember(bubbleIdCompObj, "type", out int typeValue) ? typeValue : 0;
                        float bubbleLocationId = this.TryReadFloatMember(bubbleIdCompObj, "bubbleLocationId", out float locationIdValue) ? locationIdValue : 0f;
                        markerId = (bid * 397) ^ (refreshType * 31) ^ Mathf.RoundToInt(bubbleLocationId * 1000f);
                        if (markerId == 0)
                        {
                            markerId = (Mathf.RoundToInt(position.x * 10f) * 397) ^ Mathf.RoundToInt(position.z * 10f);
                        }
                    }
                }

                return markerId != 0 && position != Vector3.zero;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldRetainMissingBubbleSceneMarker(Vector3 scanOrigin, Vector3 lastKnownBubblePos)
        {
            float retainDistanceSqr = BubbleRadarSceneMissingRetainMinDistance * BubbleRadarSceneMissingRetainMinDistance;
            return (scanOrigin - lastKnownBubblePos).sqrMagnitude >= retainDistanceSqr;
        }

        private void SyncBubbleRadarMarkers(Vector3 scanOrigin, Material xRay, Material bg)
        {
            float now = Time.unscaledTime;
            float sinceLastRefresh = now - this._cachedBubbleRadarAt;
            bool snapshotEmpty = this.bubbleRadarSnapshotPositions.Count == 0;
            float rescanMoveThresholdSqr = BubbleRadarRescanMoveThreshold * BubbleRadarRescanMoveThreshold;
            bool movedFarEnough = this.bubbleRadarHasLastScanOrigin
                && (scanOrigin - this.bubbleRadarLastScanOrigin).sqrMagnitude >= rescanMoveThresholdSqr;
            bool shouldRefresh = this.bubbleRadarForceRefresh
                || (snapshotEmpty ? sinceLastRefresh > BubbleRadarEmptyRefreshInterval : sinceLastRefresh > BubbleRadarRefreshInterval)
                || (movedFarEnough && sinceLastRefresh > BubbleRadarMovedRefreshInterval);

            if (shouldRefresh)
            {
                Dictionary<int, Vector3> refreshedSnapshot = new Dictionary<int, Vector3>();
                bool refreshed = this.TryGetAllSpawnedBubblePositions(refreshedSnapshot, out string refreshStatus);
                bool sceneOnlySnapshot = !string.IsNullOrEmpty(refreshStatus)
                    && refreshStatus.IndexOf("Bubble scene scan", StringComparison.OrdinalIgnoreCase) >= 0
                    && refreshStatus.IndexOf("Bubble entity scan ready", StringComparison.OrdinalIgnoreCase) < 0
                    && refreshStatus.IndexOf("Bubble ECS scan resolved", StringComparison.OrdinalIgnoreCase) < 0
                    && refreshStatus.IndexOf("Bubble service ready", StringComparison.OrdinalIgnoreCase) < 0;
                bool retainedSnapshot = !string.IsNullOrEmpty(refreshStatus)
                    && refreshStatus.IndexOf("snapshot retained", StringComparison.OrdinalIgnoreCase) >= 0;

                if (refreshed)
                {
                    if (sceneOnlySnapshot)
                    {
                        foreach (int staleBubbleId in this.bubbleRadarSnapshotPositions.Keys.ToArray())
                        {
                            if (!refreshedSnapshot.ContainsKey(staleBubbleId))
                            {
                                Vector3 lastKnownBubblePos = this.bubbleRadarSnapshotPositions[staleBubbleId];
                                if (this.ShouldRetainMissingBubbleSceneMarker(scanOrigin, lastKnownBubblePos))
                                {
                                    continue;
                                }

                                this.RemoveBubbleTrackedMarker(staleBubbleId);
                                this.bubbleRadarSnapshotPositions.Remove(staleBubbleId);
                            }
                        }

                        foreach (KeyValuePair<int, Vector3> entry in refreshedSnapshot)
                        {
                            this.bubbleRadarSnapshotPositions[entry.Key] = entry.Value;
                        }
                    }
                    else
                    {
                        this.bubbleRadarSnapshotPositions.Clear();
                        foreach (KeyValuePair<int, Vector3> entry in refreshedSnapshot)
                        {
                            this.bubbleRadarSnapshotPositions[entry.Key] = entry.Value;
                        }
                    }
                }
                else if (sceneOnlySnapshot)
                {
                    if (this.ShouldRetainEmptyBubbleSceneSnapshot(scanOrigin))
                    {
                        refreshStatus += " | Empty scene scan retained last bubble snapshot.";
                    }
                    else
                    {
                        this.ClearBubbleTrackedMarkers();
                        this.bubbleRadarSnapshotPositions.Clear();
                    }
                }
                else if (!sceneOnlySnapshot && !retainedSnapshot)
                {
                    this.bubbleRadarSnapshotPositions.Clear();
                }

                this._cachedBubbleRadarAt = now;
                this.bubbleRadarLastScanOrigin = scanOrigin;
                this.bubbleRadarHasLastScanOrigin = true;
                this.bubbleRadarForceRefresh = false;
                this.BubbleRadarLogThrottled("refresh", "Bubble snapshot refresh " + (refreshed ? "ok" : "empty") + ". " + refreshStatus, 1.5f);
            }

            if (!shouldRefresh && now < this.nextBubbleMarkerSyncAt)
            {
                return;
            }

            this.nextBubbleMarkerSyncAt = now + BubbleRadarMarkerSyncInterval;
            float maxBubbleRange = Mathf.Max(BubbleRadarMaxDistance, this.radarMaxDistance);
            float maxBubbleRangeSqr = maxBubbleRange * maxBubbleRange;
            this.bubbleRadarSeenIds.Clear();

            foreach (int bubbleId in this.bubbleRadarSnapshotPositions.Keys.ToArray())
            {
                Vector3 bubblePos = this.bubbleRadarSnapshotPositions[bubbleId];
                bool hasSceneTarget = this.bubbleRadarSceneTargets.TryGetValue(bubbleId, out GameObject bubbleTarget);
                if (hasSceneTarget && !this.IsUsableBubbleSceneObject(bubbleTarget))
                {
                    if (this.ShouldRetainMissingBubbleSceneMarker(scanOrigin, bubblePos))
                    {
                        this.bubbleRadarSceneTargets.Remove(bubbleId);
                        hasSceneTarget = false;
                        bubbleTarget = null;
                    }
                    else
                    {
                        this.RemoveBubbleTrackedMarker(bubbleId);
                        this.bubbleRadarSnapshotPositions.Remove(bubbleId);
                        continue;
                    }
                }

                if (hasSceneTarget)
                {
                    bubblePos = bubbleTarget.transform.position;
                    this.bubbleRadarSnapshotPositions[bubbleId] = bubblePos;
                }

                if ((scanOrigin - bubblePos).sqrMagnitude > maxBubbleRangeSqr)
                {
                    continue;
                }

                this.bubbleRadarSeenIds.Add(bubbleId);
                this.bubbleRadarTrackedPositions[bubbleId] = bubblePos;
                this.bubbleRadarLastSeenAt[bubbleId] = now;

                if (this.trackedBubbleMarkers.TryGetValue(bubbleId, out GameObject existingMarker) && existingMarker != null)
                {
                    continue;
                }

                GameObject bubbleMarker = this.CreateMarker(bubblePos, "bubble", xRay, bg, bubbleTarget);
                if (bubbleMarker == null)
                {
                    continue;
                }

                bubbleMarker.name = BubbleTrackedMarkerPrefix + bubbleId.ToString();
                this.trackedBubbleMarkers[bubbleId] = bubbleMarker;
            }

            this.radarCleanupTrackedIds.Clear();
            foreach (KeyValuePair<int, GameObject> tracked in this.trackedBubbleMarkers)
            {
                if (tracked.Value == null)
                {
                    this.radarCleanupTrackedIds.Add(tracked.Key);
                    continue;
                }

                if (this.bubbleRadarSeenIds.Contains(tracked.Key))
                {
                    continue;
                }

                if (!this.bubbleRadarLastSeenAt.TryGetValue(tracked.Key, out float lastSeenAt) || now - lastSeenAt > BubbleRadarMarkerGraceSeconds)
                {
                    this.radarCleanupTrackedIds.Add(tracked.Key);
                }
            }

            foreach (int bubbleId in this.radarCleanupTrackedIds)
            {
                this.RemoveBubbleTrackedMarker(bubbleId);
            }

            if (BubbleRadarDebugLoggingEnabled)
            {
                this.BubbleRadarLogThrottled(
                    "sync-summary",
                    "Marker sync complete. snapshot=" + this.bubbleRadarSnapshotPositions.Count.ToString()
                        + " inRange=" + this.bubbleRadarSeenIds.Count.ToString()
                        + " tracked=" + this.trackedBubbleMarkers.Count.ToString()
                        + " maxRange=" + maxBubbleRange.ToString("F0") + "m",
                    10f);
            }
        }

        private void RemoveBubbleTrackedMarker(int bubbleId)
        {
            if (this.trackedBubbleMarkers.TryGetValue(bubbleId, out GameObject marker) && marker != null)
            {
                this.RemoveMarkerMetadata(marker);
                this.RemoveTrackedMarkerMapping(marker);
                Object.Destroy(marker);
            }

            this.trackedBubbleMarkers.Remove(bubbleId);
            this.bubbleRadarTrackedPositions.Remove(bubbleId);
            this.bubbleRadarSceneTargets.Remove(bubbleId);
            this.bubbleRadarLastSeenAt.Remove(bubbleId);
        }

        private void ClearBubbleTrackedMarkers()
        {
            this.radarCleanupTrackedIds.Clear();
            foreach (KeyValuePair<int, GameObject> entry in this.trackedBubbleMarkers)
            {
                if (entry.Value != null)
                {
                    this.RemoveMarkerMetadata(entry.Value);
                    this.RemoveTrackedMarkerMapping(entry.Value);
                    Object.Destroy(entry.Value);
                }
                this.radarCleanupTrackedIds.Add(entry.Key);
            }

            foreach (int bubbleId in this.radarCleanupTrackedIds)
            {
                this.trackedBubbleMarkers.Remove(bubbleId);
            }

            this.bubbleRadarTrackedPositions.Clear();
            this.bubbleRadarSnapshotPositions.Clear();
            this.bubbleRadarSceneTargets.Clear();
            this.bubbleRadarSeenIds.Clear();
            this.bubbleRadarLastSeenAt.Clear();
        }

        private bool TryParseBubbleTrackedMarkerId(string markerName, out int bubbleId)
        {
            bubbleId = 0;
            if (string.IsNullOrEmpty(markerName) || !markerName.StartsWith(BubbleTrackedMarkerPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(markerName.Substring(BubbleTrackedMarkerPrefix.Length), out bubbleId);
        }

        private bool TryGetRadarMarkerTrackedTarget(GameObject marker, out GameObject target)
        {
            target = null;
            if (marker == null)
            {
                return false;
            }

            foreach (KeyValuePair<GameObject, GameObject> mapping in this.markerToTarget)
            {
                if (mapping.Key != null && mapping.Key.name == marker.name)
                {
                    target = mapping.Value;
                    return target != null;
                }
            }

            return false;
        }

        private void SyncHideAndSeekMorphRadarMarkers(Vector3 scanOrigin, Material xRay, Material bg, float maxRange)
        {
            if (!this.showOtherPlayersRadar || this.radarContainer == null)
            {
                return;
            }

            float maxRangeSqr = maxRange * maxRange;
            this.hideAndSeekMorphSeenNetIds.Clear();
            this.hideAndSeekMorphCollectBuffer.Clear();
            this.TryCollectHideAndSeekMorphRadarSpots(this.hideAndSeekMorphCollectBuffer);

            for (int i = 0; i < this.hideAndSeekMorphCollectBuffer.Count; i++)
            {
                HideAndSeekMorphRadarSpot spot = this.hideAndSeekMorphCollectBuffer[i];
                if (spot.MarkerNetId == 0U || spot.Position.sqrMagnitude < 0.01f)
                {
                    continue;
                }

                if ((scanOrigin - spot.Position).sqrMagnitude > maxRangeSqr)
                {
                    continue;
                }

                this.hideAndSeekMorphSeenNetIds.Add(spot.MarkerNetId);
                this.hideAndSeekMorphTrackedPositions[spot.MarkerNetId] = spot.Position;

                if (this.trackedHideAndSeekMorphMarkers.TryGetValue(spot.MarkerNetId, out GameObject existingMarker) && existingMarker != null)
                {
                    continue;
                }

                GameObject morphMarker = this.CreateMarker(spot.Position, "otherplayermorph", xRay, bg, null);
                if (morphMarker == null)
                {
                    continue;
                }

                morphMarker.name = HideAndSeekMorphMarkerPrefix + spot.MarkerNetId.ToString();
                this.trackedHideAndSeekMorphMarkers[spot.MarkerNetId] = morphMarker;
            }

            this.radarCleanupTrackedIds.Clear();
            foreach (KeyValuePair<uint, GameObject> tracked in this.trackedHideAndSeekMorphMarkers)
            {
                if (tracked.Value == null || !this.hideAndSeekMorphSeenNetIds.Contains(tracked.Key))
                {
                    this.radarCleanupTrackedIds.Add((int)tracked.Key);
                }
            }

            for (int i = 0; i < this.radarCleanupTrackedIds.Count; i++)
            {
                this.RemoveHideAndSeekMorphMarker((uint)this.radarCleanupTrackedIds[i]);
            }
        }

        private void RemoveHideAndSeekMorphMarker(uint markerNetId)
        {
            if (this.trackedHideAndSeekMorphMarkers.TryGetValue(markerNetId, out GameObject marker) && marker != null)
            {
                this.RemoveMarkerMetadata(marker);
                Object.Destroy(marker);
            }

            this.trackedHideAndSeekMorphMarkers.Remove(markerNetId);
            this.hideAndSeekMorphTrackedPositions.Remove(markerNetId);
        }

        private void ClearHideAndSeekMorphMarkers()
        {
            this.radarCleanupTrackedIds.Clear();
            foreach (KeyValuePair<uint, GameObject> entry in this.trackedHideAndSeekMorphMarkers)
            {
                if (entry.Value != null)
                {
                    this.RemoveMarkerMetadata(entry.Value);
                    Object.Destroy(entry.Value);
                }

                this.radarCleanupTrackedIds.Add((int)entry.Key);
            }

            for (int i = 0; i < this.radarCleanupTrackedIds.Count; i++)
            {
                this.trackedHideAndSeekMorphMarkers.Remove((uint)this.radarCleanupTrackedIds[i]);
            }

            this.hideAndSeekMorphTrackedPositions.Clear();
            this.hideAndSeekMorphSeenNetIds.Clear();
            this.hideAndSeekMorphCollectBuffer.Clear();
        }

        private bool TryParseHideAndSeekMorphMarkerId(string markerName, out uint markerNetId)
        {
            markerNetId = 0U;
            if (string.IsNullOrEmpty(markerName) || !markerName.StartsWith(HideAndSeekMorphMarkerPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return uint.TryParse(markerName.Substring(HideAndSeekMorphMarkerPrefix.Length), out markerNetId);
        }

        private void TryCollectHideAndSeekMorphRadarSpots(List<HideAndSeekMorphRadarSpot> spots)
        {
            if (spots == null)
            {
                return;
            }

            spots.Clear();
            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId))
            {
                selfNetId = 0U;
            }

            HashSet<uint> seen = new HashSet<uint>();
            this.TryCollectHideAndSeekMorphFromTracks(selfNetId, seen, spots);
            this.TryCollectHideAndSeekMorphFromRemotePlayers(selfNetId, seen, spots);
            this.TryCollectHideAndSeekMorphFromHiderPlacedPositions(selfNetId, seen, spots);
        }

        private string GetNearestRadarNodeLabel(float maxDistance)
        {
            if (this.radarContainer == null)
            {
                return string.Empty;
            }

            GameObject player = GetPlayer();
            Vector3 origin = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            float nearestDistance = maxDistance;
            string nearestLabel = string.Empty;

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

                float dist = Vector3.Distance(origin, child.position);
                if (dist > nearestDistance)
                {
                    continue;
                }

                nearestDistance = dist;
                nearestLabel = markerLabel;
            }

            return nearestLabel;
        }

        private void UpdateResourceMarkerPositions()
        {
            this.resourceMarkerBuffer.Clear();
            float time = Time.time;
            if (this.farmRocks)
            {
                // RockPositions assumed present in file (ported arrays)
                for (int i=0;i<HeartopiaComplete.RockPositions.Length;i++)
                {
                    float until;
                    if (this.rockCooldowns.TryGetValue(i, out until) && until > time) continue;
                    float hu;
                    if (this.rockHideUntil.TryGetValue(i, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.RockPositions[i]);
                }
            }
            if (this.farmOres)
            {
                for (int j=0;j<HeartopiaComplete.OrePositions.Length;j++)
                {
                    float until;
                    if (this.oreCooldowns.TryGetValue(j, out until) && until > time) continue;
                    float hu;
                    if (this.oreHideUntil.TryGetValue(j, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.OrePositions[j]);
                }
            }
            if (this.farmTrees)
            {
                for (int k=0;k<HeartopiaComplete.TreePositions.Length;k++)
                {
                    float until;
                    if (this.treeCooldowns_res.TryGetValue(k, out until) && until > time) continue;
                    float hu;
                    if (this.treeHideUntil_res.TryGetValue(k, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.TreePositions[k]);
                }
            }
            if (this.farmRareTrees)
            {
                for (int l=0;l<HeartopiaComplete.RareTreePositions.Length;l++)
                {
                    float until;
                    if (this.rareTreeCooldowns_res.TryGetValue(l, out until) && until > time) continue;
                    float hu;
                    if (this.rareTreeHideUntil_res.TryGetValue(l, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.RareTreePositions[l]);
                }
            }
            if (this.farmAppleTrees)
            {
                for (int m=0;m<HeartopiaComplete.AppleTreePositions.Length;m++)
                {
                    float until;
                    if (this.appleTreeCooldowns_res.TryGetValue(m, out until) && until > time) continue;
                    float hu;
                    if (this.appleTreeHideUntil_res.TryGetValue(m, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.AppleTreePositions[m]);
                }
            }
            if (this.farmOrangeTrees)
            {
                for (int n=0;n<HeartopiaComplete.OrangeTreePositions.Length;n++)
                {
                    float until;
                    if (this.orangeTreeCooldowns_res.TryGetValue(n, out until) && until > time) continue;
                    float hu;
                    if (this.orangeTreeHideUntil_res.TryGetValue(n, out hu) && hu > time) continue;
                    this.resourceMarkerBuffer.Add(HeartopiaComplete.OrangeTreePositions[n]);
                }
            }
            if (this.resourceMarkerBuffer.Count != this.lastResourceMarkerCount || this.resourceMarkersNeedShuffle)
            {
                this.resourceMarkerPositions.Clear();
                this.resourceMarkerPositions.AddRange(this.resourceMarkerBuffer);
                this.lastResourceMarkerCount = this.resourceMarkerPositions.Count;
                int num = this.resourceMarkerPositions.Count;
                while (num > 1)
                {
                    num--;
                    int idx = this.instanceRng.Next(num + 1);
                    Vector3 tmp = this.resourceMarkerPositions[idx];
                    this.resourceMarkerPositions[idx] = this.resourceMarkerPositions[num];
                    this.resourceMarkerPositions[num] = tmp;
                }
                if (this.resourceMarkerPositions.Count > 0 && this.currentResourceMarkerIndex <= 0)
                {
                    this.currentResourceMarkerIndex = this.instanceRng.Next(0, this.resourceMarkerPositions.Count) - 1;
                }
                this.resourceMarkersNeedShuffle = false;
                this.visitedResourceMarkerIndices.Clear();
                ModLogger.Msg($"[ResourceFarm] Shuffled markers: {this.resourceMarkerPositions.Count} available");
            }
        }

        // Token: 0x06000017 RID: 23 RVA: 0x00004900 File Offset: 0x00002B00
        private void ToggleRadar()
        {
            this.isRadarActive = !this.isRadarActive;
            bool flag = !this.isRadarActive;
            if (flag)
            {
                this.Cleanup();
            }
            else
            {
                this.lastScanTime = Time.unscaledTime;
                this.bubbleRadarActivatedAt = Time.unscaledTime;
                this.bubbleRadarForceRefresh = true;
                this.nextBubbleMarkerSyncAt = -999f;
                this._cachedBubbleRadarAt = -999f;
                this.nextAuraBubbleScanAttemptAt = -999f;
                this.lastAuraBubbleScanSuccessAt = -999f;
                this.lastAuraBubbleScanFailureAt = -999f;
                this.bubbleRadarAuraConsecutiveFailures = 0;
                this.bubbleRadarHasLastScanOrigin = false;
                this.RunRadar();
            }
            ModLogger.Msg(this.isRadarActive ? "Radar Active" : "Radar Cleaned Up");
        }

        private bool AnyRadarLootToggleEnabled()
        {
            return this.IsAnyMushroomRadarEnabled() || this.showFiddleheadRadar || this.showTallMustardRadar || this.showBurdockRadar || this.showMustardGreensRadar
                || this.showBlueberryRadar || this.showRaspberryRadar || this.showStoneRadar || this.showOreRadar
                || this.showTreeRadar || this.showRareTreeRadar || this.showAppleTreeRadar || this.showOrangeTreeRadar
                || this.showBubbleRadar || this.showBirdRadar || this.showInsectRadar || this.showFishShadowRadar || this.showMeteorRadar
                || this.showOtherPlayersRadar;
        }

        private bool IsAnyMushroomRadarEnabled()
        {
            return this.showMushroomRadar
                || this.showOysterMushroomRadar
                || this.showButtonMushroomRadar
                || this.showPennyBunRadar
                || this.showShiitakeRadar
                || this.showTruffleRadar;
        }

        private bool AreAllMushroomRadarsEnabled()
        {
            return this.showOysterMushroomRadar
                && this.showButtonMushroomRadar
                && this.showPennyBunRadar
                && this.showShiitakeRadar
                && this.showTruffleRadar;
        }

        // Token: 0x06000018 RID: 24 RVA: 0x00004964 File Offset: 0x00002B64
        private void CheckRadarAutoToggle()
        {
            bool flag = this.AnyRadarLootToggleEnabled();
            // REMOVED AUTO-ENABLE: Checking a radar type will NOT automatically turn on the radar
            // You must manually press the "ENABLE RADAR" button to activate
            bool flag3 = !flag && this.isRadarActive;
            if (flag3)
            {
                this.isRadarActive = false;
                this.Cleanup();
                ModLogger.Msg("Radar Auto-Disabled");
            }
            bool flag4 = !this.autoFarmActive;
            if (flag4)
            {
                bool flag5 = flag;
                if (flag5)
                {
                    this.autoFarmStatus = "READY";
                }
                else
                {
                    this.autoFarmStatus = "NO_TOGGLES";
                }
            }
        }

        // Token: 0x0600001A RID: 26 RVA: 0x00004B54 File Offset: 0x00002D54
        public void RunRadar()
        {
            bool flag = this.radarContainer == null;
            if (flag)
            {
                this.radarContainer = new GameObject("Universal_Mushroom_Radar");
                Object.DontDestroyOnLoad(this.radarContainer);
            }
            else
            {
                this.radarCleanupMarkers.Clear();
                foreach (KeyValuePair<GameObject, GameObject> keyValuePair in this.markerToTarget)
                {
                    bool flag2 = keyValuePair.Key == null || keyValuePair.Value == null;
                    if (flag2)
                    {
                        this.radarCleanupMarkers.Add(keyValuePair.Key);
                    }
                }
                foreach (GameObject key in this.radarCleanupMarkers)
                {
                    this.markerToTarget.Remove(key);
                }

                this.radarCleanupTrackedIds.Clear();
                foreach (KeyValuePair<int, GameObject> tracked in this.trackedObjectMarkers)
                {
                    if (tracked.Value == null)
                    {
                        this.radarCleanupTrackedIds.Add(tracked.Key);
                    }
                }
                foreach (int trackedId in this.radarCleanupTrackedIds)
                {
                    this.trackedObjectMarkers.Remove(trackedId);
                }

                this.radarDestroyBuffer.Clear();
                for (int i = this.radarContainer.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    bool flag3 = child == null;
                    if (!flag3)
                    {
                        GameObject gameObject = child.gameObject;
                        bool flag4 = gameObject.name.StartsWith("TrackedMarker_") || this.TryParseBubbleTrackedMarkerId(gameObject.name, out _);
                        bool flag5 = !flag4;
                        if (flag5)
                        {
                            this.radarDestroyBuffer.Add(gameObject);
                        }
                    }
                }
                foreach (GameObject gameObject2 in this.radarDestroyBuffer)
                {
                    this.RemoveMarkerMetadata(gameObject2);
                    Object.Destroy(gameObject2);
                }
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Il2CppBindingFlags bindingFlags = (Il2CppBindingFlags)62;
            Il2CppType type = Il2CppType.GetType("ScriptsRefactory.BaseService.RenderSystem.Brg.BrgManager, Client");
            Il2CppObject @object;
            if (type == null)
            {
                @object = null;
            }
            else
            {
                Il2CppFieldInfo field = type.GetField("_manager", bindingFlags);
                @object = ((field != null) ? (Il2CppObject)field.GetValue(null) : null);
            }
            Il2CppObject object2 = @object;
            this.EnsureRadarMaterials();
            Material material = this.radarLineMaterial;
            Material material2 = this.radarFillMaterial;
            if (material == null || material2 == null)
            {
                return;
            }
            Vector3 position = cam.transform.position;
            float radarDistanceLimit = Mathf.Max(25f, this.radarMaxDistance);
            bool flag6 = this.showBlueberryRadar;
            if (flag6)
            {
                float unscaledTime = Time.unscaledTime;
                int j = 0;
                while (j < this.blueberryPositions.Length)
                {
                    Vector3 vector = this.blueberryPositions[j];
                    bool flag7 = Vector3.Distance(position, vector) <= radarDistanceLimit;
                    if (flag7)
                    {
                        bool flag8 = this.blueberryHideUntil.ContainsKey(j) && unscaledTime < this.blueberryHideUntil[j];
                        if (flag8)
                        {
                            float num = this.blueberryHideUntil[j] - 10f - 4f;
                            float num2 = num + 4f;
                            bool flag9 = unscaledTime >= num2;
                            if (flag9)
                            {
                                goto IL_38F;
                            }
                        }
                        bool flag10 = this.blueberryCooldowns.ContainsKey(j) && unscaledTime < this.blueberryCooldowns[j];
                        bool flag11 = flag10 && (!this.blueberryHideUntil.ContainsKey(j) || unscaledTime >= this.blueberryHideUntil[j]);
                        if (flag11)
                        {
                            this.CreateMarker(vector, "blueberry_cooldown", material, material2, null);
                        }
                        else
                        {
                            bool flag12 = !flag10;
                            if (flag12)
                            {
                                this.CreateMarker(vector, "blueberry", material, material2, null);
                            }
                        }
                    }
                IL_38F:
                    j++;
                    continue;
                }
            }
            bool flagRockScan = this.showStoneRadar;
            if (flagRockScan)
            {
                float unscaledRock = Time.unscaledTime;
                for (int r = 0; r < HeartopiaComplete.RockPositions.Length; r++)
                {
                    Vector3 rockPos = HeartopiaComplete.RockPositions[r];
                    if (Vector3.Distance(position, rockPos) <= radarDistanceLimit)
                    {
                        bool onCD = this.rockCooldowns.ContainsKey(r) && unscaledRock < this.rockCooldowns[r];
                        bool hidden = this.rockHideUntil.ContainsKey(r) && unscaledRock < this.rockHideUntil[r];
                        if (onCD && (!this.rockHideUntil.ContainsKey(r) || unscaledRock >= this.rockHideUntil[r]))
                        {
                            this.CreateMarker(rockPos, "stone_cooldown", material, material2, null);
                        }
                        else if (!onCD && !hidden)
                        {
                            this.CreateMarker(rockPos, "stone", material, material2, null);
                        }
                    }
                }
            }
            bool flagTreeScan = this.showTreeRadar;
            if (flagTreeScan)
            {
                float unscaledTree = Time.unscaledTime;
                for (int tIdx = 0; tIdx < HeartopiaComplete.TreePositions.Length; tIdx++)
                {
                    Vector3 treePos = HeartopiaComplete.TreePositions[tIdx];
                    if (Vector3.Distance(position, treePos) <= radarDistanceLimit)
                    {
                        bool onCDt = this.treeCooldowns_res.ContainsKey(tIdx) && unscaledTree < this.treeCooldowns_res[tIdx];
                        bool hiddent = this.treeHideUntil_res.ContainsKey(tIdx) && unscaledTree < this.treeHideUntil_res[tIdx];
                        if (onCDt && (!this.treeHideUntil_res.ContainsKey(tIdx) || unscaledTree >= this.treeHideUntil_res[tIdx]))
                        {
                            this.CreateMarker(treePos, "tree_cooldown", material, material2, null);
                        }
                        else if (!onCDt && !hiddent)
                        {
                            this.CreateMarker(treePos, "tree", material, material2, null);
                        }
                    }
                }
            }
            bool flagRareTreeScan = this.showRareTreeRadar;
            if (flagRareTreeScan)
            {
                float unscaledRare = Time.unscaledTime;
                for (int rt = 0; rt < HeartopiaComplete.RareTreePositions.Length; rt++)
                {
                    Vector3 rarePos = HeartopiaComplete.RareTreePositions[rt];
                    if (Vector3.Distance(position, rarePos) <= radarDistanceLimit)
                    {
                        bool onCD = this.rareTreeCooldowns_res.ContainsKey(rt) && unscaledRare < this.rareTreeCooldowns_res[rt];
                        bool hidden = this.rareTreeHideUntil_res.ContainsKey(rt) && unscaledRare < this.rareTreeHideUntil_res[rt];
                        if (onCD && (!this.rareTreeHideUntil_res.ContainsKey(rt) || unscaledRare >= this.rareTreeHideUntil_res[rt]))
                        {
                            this.CreateMarker(rarePos, "rare_tree_cooldown", material, material2, null);
                        }
                        else if (!onCD && !hidden)
                        {
                            this.CreateMarker(rarePos, "rare_tree", material, material2, null);
                        }
                    }
                }
            }
            bool flagAppleScan = this.showAppleTreeRadar;
            if (flagAppleScan)
            {
                float unscaledApple = Time.unscaledTime;
                for (int a = 0; a < HeartopiaComplete.AppleTreePositions.Length; a++)
                {
                    Vector3 applePos = HeartopiaComplete.AppleTreePositions[a];
                    if (Vector3.Distance(position, applePos) <= radarDistanceLimit)
                    {
                        bool onCDa = this.appleTreeCooldowns_res.ContainsKey(a) && unscaledApple < this.appleTreeCooldowns_res[a];
                        bool hid = this.appleTreeHideUntil_res.ContainsKey(a) && unscaledApple < this.appleTreeHideUntil_res[a];
                        if (onCDa && (!this.appleTreeHideUntil_res.ContainsKey(a) || unscaledApple >= this.appleTreeHideUntil_res[a]))
                        {
                            this.CreateMarker(applePos, "apple_tree_cooldown", material, material2, null);
                        }
                        else if (!onCDa && !hid)
                        {
                            this.CreateMarker(applePos, "apple_tree", material, material2, null);
                        }
                    }
                }
            }
            bool flagOrangeScan = this.showOrangeTreeRadar;
            if (flagOrangeScan)
            {
                float unscaledOrange = Time.unscaledTime;
                for (int oT = 0; oT < HeartopiaComplete.OrangeTreePositions.Length; oT++)
                {
                    Vector3 orangePos = HeartopiaComplete.OrangeTreePositions[oT];
                    if (Vector3.Distance(position, orangePos) <= radarDistanceLimit)
                    {
                        bool onCDo = this.orangeTreeCooldowns_res.ContainsKey(oT) && unscaledOrange < this.orangeTreeCooldowns_res[oT];
                        bool hidO = this.orangeTreeHideUntil_res.ContainsKey(oT) && unscaledOrange < this.orangeTreeHideUntil_res[oT];
                        if (onCDo && (!this.orangeTreeHideUntil_res.ContainsKey(oT) || unscaledOrange >= this.orangeTreeHideUntil_res[oT]))
                        {
                            this.CreateMarker(orangePos, "orange_tree_cooldown", material, material2, null);
                        }
                        else if (!onCDo && !hidO)
                        {
                            this.CreateMarker(orangePos, "orange_tree", material, material2, null);
                        }
                    }
                }
            }
            bool flagOreScan = this.showOreRadar;
            if (flagOreScan)
            {
                float unscaledOre = Time.unscaledTime;
                for (int o = 0; o < HeartopiaComplete.OrePositions.Length; o++)
                {
                    Vector3 orePos = HeartopiaComplete.OrePositions[o];
                    if (Vector3.Distance(position, orePos) <= radarDistanceLimit)
                    {
                        bool onCD = this.oreCooldowns.ContainsKey(o) && unscaledOre < this.oreCooldowns[o];
                        bool hidden = this.oreHideUntil.ContainsKey(o) && unscaledOre < this.oreHideUntil[o];
                        if (onCD && (!this.oreHideUntil.ContainsKey(o) || unscaledOre >= this.oreHideUntil[o]))
                        {
                            this.CreateMarker(orePos, "ore_cooldown", material, material2, null);
                        }
                        else if (!onCD && !hidden)
                        {
                            this.CreateMarker(orePos, "ore", material, material2, null);
                        }
                    }
                }
            }
            bool flag13 = this.showRaspberryRadar;
            if (flag13)
            {
                float unscaledTime2 = Time.unscaledTime;
                int k = 0;
                while (k < this.raspberryPositions.Length)
                {
                    Vector3 vector2 = this.raspberryPositions[k];
                    bool flag14 = Vector3.Distance(position, vector2) <= radarDistanceLimit;
                    if (flag14)
                    {
                        bool flag15 = this.raspberryHideUntil.ContainsKey(k) && unscaledTime2 < this.raspberryHideUntil[k];
                        if (flag15)
                        {
                            float num3 = this.raspberryHideUntil[k] - 10f - 4f;
                            float num4 = num3 + 4f;
                            bool flag16 = unscaledTime2 >= num4;
                            if (flag16)
                            {
                                goto IL_4E9;
                            }
                        }
                        bool flag17 = this.raspberryCooldowns.ContainsKey(k) && unscaledTime2 < this.raspberryCooldowns[k];
                        bool flag18 = flag17 && (!this.raspberryHideUntil.ContainsKey(k) || unscaledTime2 >= this.raspberryHideUntil[k]);
                        if (flag18)
                        {
                            this.CreateMarker(vector2, "raspberry_cooldown", material, material2, null);
                        }
                        else
                        {
                            bool flag19 = !flag17;
                            if (flag19)
                            {
                                this.CreateMarker(vector2, "raspberry", material, material2, null);
                            }
                        }
                    }
                IL_4E9:
                    k++;
                    continue;
                }
            }
            // -- Throttled GameObject scan for bubble / bird-fallback / fish-shadow / meteor radars --
            // FindObjectsOfType<GameObject>() is expensive. We throttle it to at most once every 2s
            // inside RunRadar. We intentionally do NOT cache the result in a class field:
            // storing IL2CPP native object references between frames causes native access-violation
            // crashes when Unity destroys those objects while we still hold the C# wrapper.
            bool needGOScan = this.showInsectRadar || this.showFishShadowRadar || this.showMeteorRadar
                              || this.showBirdRadar || this.showOtherPlayersRadar;
            GameObject[] freshGOs = null;
            if (needGOScan && Time.unscaledTime - this._cachedRadarGameObjectsAt > RadarGOScanInterval)
            {
                freshGOs = Object.FindObjectsOfType<GameObject>();
                this._cachedRadarGameObjectsAt = Time.unscaledTime;
            }

            if (this.showBubbleRadar)
            {
                this.SyncBubbleRadarMarkers(position, material, material2);
            }
            else if (this.trackedBubbleMarkers.Count > 0 || this.bubbleRadarTrackedPositions.Count > 0)
            {
                this.ClearBubbleTrackedMarkers();
            }

            // Match the older dedicated insect radar scan path from backup logic, but reuse the
            // shared throttled scene scan so low-spec machines do not pay for a second full walk.
            if (this.showInsectRadar && freshGOs != null)
            {
                this.radarCleanupTrackedIds.Clear();
                foreach (KeyValuePair<int, GameObject> tracked in this.trackedObjectMarkers)
                {
                    if (tracked.Value == null)
                    {
                        this.radarCleanupTrackedIds.Add(tracked.Key);
                    }
                }
                foreach (int trackedId in this.radarCleanupTrackedIds)
                {
                    this.trackedObjectMarkers.Remove(trackedId);
                }

                foreach (GameObject insectObject in freshGOs)
                {
                    if (insectObject == null || string.IsNullOrEmpty(insectObject.name))
                    {
                        continue;
                    }

                    string insectNameLower = insectObject.name.ToLowerInvariant();
                    if (!this.ShouldTrackInsectObject(insectNameLower))
                    {
                        continue;
                    }

                    int insectInstanceId = insectObject.GetInstanceID();
                    if (!this.trackedObjectMarkers.ContainsKey(insectInstanceId))
                    {
                        this.CreateMarker(insectObject.transform.position, "insect", material, material2, insectObject);
                        this.RegisterTrackedMarkerForTarget(insectInstanceId, insectObject);
                    }
                }
            }


            // Single combined scan for bubble / fish-shadow / meteor radar (GameObject name-matching).
            // Birds are handled separately below via the farm's AuraMono position cache.
            if ((this.showFishShadowRadar || this.showMeteorRadar) && freshGOs != null)
            {
                Vector3 scanOrigin = position;
                float maxMiscRange = Mathf.Max(25f, this.radarMaxDistance);

                foreach (GameObject candidate in freshGOs)
                {
                    try
                    {
                        if (candidate == null || candidate.name == null) continue;
                        if (!candidate.activeInHierarchy) continue;
                        if (Vector3.Distance(scanOrigin, candidate.transform.position) > maxMiscRange) continue;

                        string nameLower = candidate.name.ToLowerInvariant();
                        string markerType = null;

                        if (this.showFishShadowRadar && this.ShouldTrackFishShadowObject(candidate))
                            markerType = "fishshadow";
                        else if (this.showMeteorRadar && this.ShouldTrackMeteorObject(nameLower))
                            markerType = "meteor";

                        if (markerType == null) continue;

                        int instanceID = candidate.GetInstanceID();
                        if (!this.trackedObjectMarkers.ContainsKey(instanceID))
                        {
                            this.CreateMarker(candidate.transform.position, markerType, material, material2, candidate);
                            this.RegisterTrackedMarkerForTarget(instanceID, candidate);
                        }
                    }
                    catch { /* destroyed native object - skip silently */ }
                }
            }

            // -- Bird radar: NO Mono API calls here. ------------------------------------------
            // When Auto Bird Farm is running, _auraMonoBirdRadarPositions is populated by
            // the farm's entity scan (free -- no extra scan needed). We just read that list.
            // When the farm is off we fall back to a fresh GO scan (same throttle as above).
            if (this.showBirdRadar)
            {
                if (freshGOs != null)
                {
                    float maxBirdRange2 = Mathf.Max(25f, this.radarMaxDistance);
                    foreach (GameObject candidate in freshGOs)
                    {
                        try
                        {
                            if (candidate == null || candidate.name == null) continue;
                            if (!candidate.activeInHierarchy) continue;
                            if (Vector3.Distance(position, candidate.transform.position) > maxBirdRange2) continue;
                            if (!this.ShouldTrackBirdObject(candidate.name.ToLowerInvariant())) continue;
                            int instanceID = candidate.GetInstanceID();
                            if (!this.trackedObjectMarkers.ContainsKey(instanceID))
                            {
                                this.CreateMarker(candidate.transform.position, "bird", material, material2, candidate);
                                this.RegisterTrackedMarkerForTarget(instanceID, candidate);
                            }
                        }
                        catch { /* destroyed native object - skip silently */ }
                    }
                }
            }

            if (this.showOtherPlayersRadar && freshGOs != null)
            {
                float maxPlayerRange = Mathf.Max(25f, this.radarMaxDistance);
                for (int p = 0; p < freshGOs.Length; p++)
                {
                    try
                    {
                        GameObject candidate = freshGOs[p];
                        if (candidate == null || candidate.name == null)
                        {
                            continue;
                        }

                        if (this.IsLocalPlayerSkeletonGameObject(candidate))
                        {
                            continue;
                        }

                        if (!this.IsOtherPlayerSkeletonGameObject(candidate))
                        {
                            continue;
                        }

                        if (Vector3.Distance(position, candidate.transform.position) > maxPlayerRange)
                        {
                            continue;
                        }

                        int instanceId = candidate.GetInstanceID();
                        if (!this.trackedObjectMarkers.ContainsKey(instanceId))
                        {
                            this.CreateMarker(candidate.transform.position, "otherplayer", material, material2, candidate);
                            this.RegisterTrackedMarkerForTarget(instanceId, candidate);
                        }
                    }
                    catch
                    {
                    }
                }

                float morphMaxRange = Mathf.Max(25f, this.radarMaxDistance);
                this.SyncHideAndSeekMorphRadarMarkers(position, material, material2, morphMaxRange);
            }
            else if (this.trackedHideAndSeekMorphMarkers.Count > 0 || this.hideAndSeekMorphTrackedPositions.Count > 0)
            {
                this.ClearHideAndSeekMorphMarkers();
            }

            bool flag34 = object2 != null;
            if (this.IsAnyMushroomRadarEnabled() || this.showFiddleheadRadar || this.showTallMustardRadar || this.showBurdockRadar || this.showMustardGreensRadar)
            {
                if (flag34)
                {
                    HashSet<string> hashSet = new HashSet<string>();
                    Il2CppReferenceArray<Il2CppFieldInfo> fields = object2.GetIl2CppType().GetFields(bindingFlags);
                    for (int n = 0; n < fields.Count; n++)
                    {
                        Il2CppObject value = fields[n].GetValue(object2);
                        Il2CppObject object3;
                        if (value == null)
                        {
                            object3 = null;
                        }
                        else
                        {
                            Il2CppFieldInfo field2 = value.GetIl2CppType().GetField("_brgData", bindingFlags);
                            object3 = ((field2 != null) ? field2.GetValue(value) : null);
                        }
                        Il2CppObject object4 = object3;
                        Il2CppObject object5;
                        if (object4 == null)
                        {
                            object5 = null;
                        }
                        else
                        {
                            Il2CppFieldInfo field3 = object4.GetIl2CppType().GetField("CycleEntities", bindingFlags);
                            object5 = ((field3 != null) ? field3.GetValue(object4) : null);
                        }
                        Il2CppObject object6 = object5;
                        bool flag30 = object6 != null;
                        if (flag30)
                        {
                            Il2CppType il2CppType = object6.GetIl2CppType();
                            int num5 = il2CppType.GetProperty("Count").GetValue(object6).Unbox<int>();
                            for (int num6 = 0; num6 < num5; num6++)
                            {
                                try
                                {
                                    Il2CppObject boxedIndex = this.BoxInt(num6);
                                    Il2CppObject object7 = il2CppType.GetMethod("get_Item").Invoke(object6, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[]
                                    {
                                        boxedIndex
                                    }));
                                    string meshName = this.GetMeshName(object7, bindingFlags);
                                    string forageText = meshName.ToLower();
                                    bool flag31 = forageText.Contains("dynamicbush");
                                    if (flag31)
                                    {
                                        if (!this.ShouldShowForageMesh(forageText))
                                        {
                                            continue;
                                        }
                                        Il2CppFieldInfo field4 = object7.GetIl2CppType().GetField("blocks", bindingFlags);
                                        Il2CppObject object8 = (field4 != null) ? field4.GetValue(object7) : null;
                                        bool flag32 = object8 != null;
                                        if (flag32)
                                        {
                                            int num7 = object8.GetIl2CppType().GetProperty("Count").GetValue(object8).Unbox<int>();
                                            for (int num8 = 0; num8 < num7; num8++)
                                            {
                                                Il2CppObject boxedBlockIndex = this.BoxInt(num8);
                                                Il2CppObject block = object8.GetIl2CppType().GetMethod("get_Item").Invoke(object8, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[]
                                                {
                                                    boxedBlockIndex
                                                }));
                                                Vector3 blockPos = this.GetBlockPos(block, bindingFlags);
                                                string item = $"{blockPos.x:F1}{blockPos.z:F1}";
                                                bool flag33 = !hashSet.Contains(item);
                                                if (flag33)
                                                {
                                                    hashSet.Add(item);
                                                    this.CreateMarker(blockPos, meshName, material, material2, null);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    ModLogger.Msg($"Error processing item {num6}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        // Token: 0x06000018 RID: 24 RVA: 0x00005598 File Offset: 0x00003798
        private GameObject CreateMarker(Vector3 pos, string meshName, Material xRay, Material bg, GameObject targetObject = null)
        {
            bool flag = meshName.Contains("step0") || meshName.Contains("_cooldown") || meshName == "blueberry_cooldown" || meshName == "raspberry_cooldown";
            string text = meshName.ToLower();
            string text2 = "Mushroom";
            string icon = "?"; // Default mushroom icon
            Color endColor = Color.white;
            Color bgColor = new Color(0.5f, 0.5f, 0.5f, 0.7f); // Default gray background
            bool flag2 = meshName == "blueberry" || meshName == "blueberry_cooldown";
            if (flag2)
            {
                text2 = "Blueberry";
                icon = "?";
                endColor = new Color(0.3f, 0.5f, 1f); // Light blue
                bgColor = new Color(0.1f, 0.2f, 0.5f, 0.85f);
            }
            else
            {
                bool flag3 = meshName == "raspberry" || meshName == "raspberry_cooldown";
                if (flag3)
                {
                    text2 = "Raspberry";
                    icon = "?";
                    endColor = new Color(1f, 0.3f, 0.4f); // Light red
                    bgColor = new Color(0.5f, 0.1f, 0.15f, 0.85f);
                }
                else
                {
                    bool flag4 = meshName == "bubble";
                    if (flag4)
                    {
                        text2 = "Bubble";
                        icon = "?";
                        endColor = new Color(1f, 0.5f, 1f); // Light magenta
                        bgColor = new Color(0.4f, 0.1f, 0.4f, 0.85f);
                    }
                    else
                    {
                        bool flag5 = meshName == "insect";
                        if (flag5)
                        {
                            text2 = "Insect";
                            icon = "?";
                            endColor = new Color(1f, 0.8f, 0.4f); // Light orange
                            bgColor = new Color(0.5f, 0.3f, 0.1f, 0.85f);
                        }
                        else
                        {
                            bool flagBird = meshName == "bird";
                            if (flagBird)
                            {
                                text2 = "Bird";
                                icon = "?";
                                endColor = new Color(0.95f, 0.95f, 0.55f);
                                bgColor = new Color(0.45f, 0.35f, 0.08f, 0.85f);
                            }
                            else
                            {
                                if (meshName == "otherplayermorph")
                                {
                                    text2 = "Morph";
                                    icon = "[M]";
                                    endColor = new Color(1f, 0.72f, 0.38f);
                                    bgColor = new Color(0.42f, 0.22f, 0.08f, 0.9f);
                                }
                                else if (meshName == "otherplayer")
                                {
                                    text2 = "Player";
                                    icon = "[P]";
                                    endColor = new Color(0.45f, 0.88f, 1f);
                                    bgColor = new Color(0.08f, 0.22f, 0.42f, 0.9f);
                                }
                                else
                                {
                                bool flagRareTree = meshName == "rare_tree" || meshName == "rare_tree_cooldown";
                                if (flagRareTree)
                                {
                                    text2 = "Rare Tree";
                                    icon = "?";
                                    endColor = new Color(1f, 0.84f, 0f);
                                    bgColor = new Color(0.6f, 0.45f, 0.05f, 0.86f);
                                }

                                bool flagApple = meshName == "apple_tree" || meshName == "apple_tree_cooldown";
                                if (flagApple)
                                {
                                    text2 = "Apple Tree";
                                    icon = "[A]";
                                    endColor = new Color(1f, 0.45f, 0.45f);
                                    bgColor = new Color(0.15f, 0.35f, 0.1f, 0.85f);
                                }

                                bool flagOrange = meshName == "orange_tree" || meshName == "orange_tree_cooldown";
                                if (flagOrange)
                                {
                                    text2 = "Mandarin Tree";
                                    icon = "[M]";
                                    endColor = new Color(1f, 0.7f, 0.35f);
                                    bgColor = new Color(0.35f, 0.18f, 0.05f, 0.85f);
                                }

                                bool flagTree = meshName == "tree" || meshName == "tree_cooldown";
                                if (flagTree)
                                {
                                    text2 = "Tree";
                                    icon = "?";
                                    endColor = new Color(0.72f, 0.86f, 1f);
                                    bgColor = new Color(0.05f, 0.22f, 0.72f, 0.86f);
                                }
                                else
                                {
                                    bool flag35 = meshName == "fishshadow" || meshName == "fish";
                                    if (flag35)
                                    {
                                        text2 = "Fish Shadow";
                                        icon = "[F]";
                                        endColor = new Color(0.2f, 0.6f, 1f); // Light blue
                                        bgColor = new Color(0.05f, 0.2f, 0.5f, 0.85f);
                                    }
                                    else
                                    {
                                        bool flagMeteorType = meshName == "meteor";
                                        if (flagMeteorType)
                                        {
                                            text2 = "Meteor";
                                            icon = "?";
                                            endColor = new Color(1f, 0.6f, 0.2f);
                                            bgColor = new Color(0.45f, 0.2f, 0.05f, 0.88f);
                                        }

                                        bool flagRockType = meshName == "stone" || meshName == "stone_cooldown";
                                        if (flagRockType)
                                        {
                                            text2 = "Stone";
                                            icon = "?";
                                            endColor = new Color(0.7f, 0.7f, 0.7f);
                                            bgColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
                                        }

                                        bool flagOreType = meshName == "ore" || meshName == "ore_cooldown";
                                        if (flagOreType)
                                        {
                                            text2 = "Ore";
                                            icon = "?";
                                            endColor = new Color(0.8f, 0.6f, 0.4f); // brownish
                                            bgColor = new Color(0.25f, 0.18f, 0.1f, 0.85f);
                                        }

                                        bool flag6 = text.Contains("pleurotus");
                                        if (flag6)
                                        {
                                            text2 = "Oyster";
                                            icon = "?";
                                            endColor = new Color(0.5f, 1f, 1f); // Light cyan
                                            bgColor = new Color(0.1f, 0.4f, 0.4f, 0.85f);
                                        }
                                        else
                                        {
                                            bool flag7 = text.Contains("tricholoma");
                                            if (flag7)
                                            {
                                                text2 = "Button";
                                                icon = "?";
                                                endColor = new Color(0.6f, 1f, 0.6f); // Light green
                                                bgColor = new Color(0.15f, 0.4f, 0.15f, 0.85f);
                                            }
                                            else
                                            {
                                                bool flag8 = text.Contains("boletus");
                                                if (flag8)
                                                {
                                                    text2 = "Penny Bun";
                                                    icon = "?";
                                                    endColor = new Color(0.9f, 0.7f, 1f); // Light purple
                                                    bgColor = new Color(0.35f, 0.2f, 0.5f, 0.85f);
                                                }
                                                else
                                                {
                                                    bool flag9 = text.Contains("shiitake");
                                                    if (flag9)
                                                    {
                                                        text2 = "Shiitake";
                                                        icon = "?";
                                                        endColor = new Color(1f, 0.7f, 0.5f); // Light orange-brown
                                                        bgColor = new Color(0.5f, 0.25f, 0.1f, 0.85f);
                                                    }
                                                    else
                                                    {
                                                        bool flag10 = text.Contains("truffle");
                                                        if (flag10)
                                                        {
                                                            text2 = "Truffle";
                                                            icon = "?";
                                                            endColor = new Color(1f, 1f, 0.5f); // Light yellow
                                                            bgColor = new Color(0.5f, 0.5f, 0.1f, 0.85f);
                                                        }
                                                        else if (text.Contains("fiddlehead") || text.Contains("fiddle") || text.Contains("fern") || text.Contains("pterid") || text.Contains("bracken"))
                                                        {
                                                            text2 = "Fiddlehead";
                                                            icon = "[Fd]";
                                                            endColor = new Color(0.6f, 0.95f, 0.6f);
                                                            bgColor = new Color(0.15f, 0.45f, 0.15f, 0.85f);
                                                        }
                                                        else if (text.Contains("burdock"))
                                                        {
                                                            text2 = "Burdock";
                                                            icon = "?";
                                                            endColor = new Color(0.86f, 0.72f, 0.48f);
                                                            bgColor = new Color(0.38f, 0.26f, 0.12f, 0.85f);
                                                        }
                                                        else if (text.Contains("shepherdspurse") || (text.Contains("mustard") && text.Contains("green")) || text.Contains("mustard greens") || text.Contains("mustardgreens") || text.Contains("mustard_green") || text.Contains("mustardgreen") || text.Contains("greens"))
                                                        {
                                                            text2 = "Mustard Greens";
                                                            icon = "[Mg]";
                                                            endColor = new Color(0.58f, 0.95f, 0.52f);
                                                            bgColor = new Color(0.14f, 0.42f, 0.14f, 0.85f);
                                                        }
                                                        else if (text.Contains("tall mustard") || text.Contains("tallmustard") || text.Contains("mustard"))
                                                        {
                                                            text2 = "Tall Mustard";
                                                            icon = "[TM]";
                                                            endColor = new Color(0.7f, 1f, 0.5f);
                                                            bgColor = new Color(0.2f, 0.45f, 0.12f, 0.85f);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
                            }
            if (text2 == "Mushroom" && text.Contains("dynamicbush") && !this.loggedUnknownForageMeshNames.Contains(meshName))
            {
                this.loggedUnknownForageMeshNames.Add(meshName);
                ModLogger.Msg("[RadarDebug] Unmapped forage mesh: " + meshName);
            }
            string canonicalLabel = text2;
            string specificIconKey = string.Empty;
            if (targetObject != null)
            {
                this.TryResolveRadarTargetSpecificIconKey(canonicalLabel, targetObject, out specificIconKey);
            }
            bool flag11 = flag;
            if (flag11)
            {
                endColor = new Color(1f, 0.3f, 0.3f); // Red for cooldown
                bgColor = new Color(0.5f, 0.05f, 0.05f, 0.9f); // Dark red background
            }
            if (this.ShouldUseModernRadarVisualEsp())
            {
                return this.CreateModernRadarMarkerAnchor(pos, canonicalLabel, icon, specificIconKey, flag, targetObject);
            }
            if (this.radarMarkerStyle == 2)
            {
                GameObject iconMarker = new GameObject("ItemMarker");
                iconMarker.transform.position = pos;
                iconMarker.transform.SetParent(this.radarContainer.transform);
                if (targetObject != null)
                {
                    iconMarker.name = "TrackedMarker_" + targetObject.GetInstanceID().ToString();
                    this.markerToTarget[iconMarker] = targetObject;
                }
                RadarMarkerMetadata iconMetadata = new RadarMarkerMetadata
                {
                    CanonicalLabel = canonicalLabel,
                    Icon = icon,
                    SpecificIconKey = specificIconKey,
                    IsCooldown = flag
                };
                this.SetMarkerMetadata(iconMarker, iconMetadata);
                if (this.IsResourceVisualEspLabel(canonicalLabel))
                {
                    return iconMarker;
                }
                if (meshName == "bubble")
                {
                    this.ConfigureBubbleMarkerRenderers(iconMarker);
                }
                return iconMarker;
            }
            // If user selected simple text markers, create a minimal label-only marker with a circle
            if (this.radarMarkerStyle == 1)
            {
                GameObject simpleMarker = new GameObject("ItemMarker");
                simpleMarker.transform.position = pos;
                simpleMarker.transform.SetParent(this.radarContainer.transform);
                if (targetObject != null)
                {
                    simpleMarker.name = "TrackedMarker_" + targetObject.GetInstanceID().ToString();
                    this.markerToTarget[simpleMarker] = targetObject;
                }
                RadarMarkerMetadata simpleMetadata = new RadarMarkerMetadata
                {
                    CanonicalLabel = canonicalLabel,
                    Icon = icon,
                    SpecificIconKey = specificIconKey,
                    IsCooldown = flag
                };
                this.SetMarkerMetadata(simpleMarker, simpleMetadata);
                if (this.IsResourceVisualEspLabel(canonicalLabel))
                {
                    return simpleMarker;
                }

                // Draw a simple circular ground marker using LineRenderer
                LineRenderer circle = simpleMarker.AddComponent<LineRenderer>();
                circle.material = xRay;
                circle.useWorldSpace = false;
                circle.startWidth = (circle.endWidth = 0.08f);
                int segments = 48;
                circle.positionCount = segments + 1;
                Color circleColor = endColor;
                circleColor.a = 0.85f;
                circle.startColor = (circle.endColor = circleColor);
                float radius = 0.8f;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / (float)segments * 2f * 3.1415927f;
                    float x = Mathf.Cos(t) * radius;
                    float z = Mathf.Sin(t) * radius;
                    circle.SetPosition(s, new Vector3(x, 0.1f, z));
                }

                GameObject anchorSimple = new GameObject("LabelAnchor");
                anchorSimple.transform.SetParent(simpleMarker.transform);
                anchorSimple.transform.localPosition = new Vector3(0f, 1.8f, 0f);
                GameObject textGoSimple = new GameObject("Text");
                TextMesh textMeshSimple = textGoSimple.AddComponent<TextMesh>();
                textGoSimple.transform.SetParent(anchorSimple.transform);
                textGoSimple.transform.localPosition = Vector3.zero;
                textMeshSimple.text = this.GetMarkerDisplayTitle(simpleMetadata);
                textMeshSimple.color = endColor;
                textMeshSimple.fontStyle = (FontStyle)1;
                textMeshSimple.fontSize = 85;
                textMeshSimple.characterSize = 0.06f;
                textMeshSimple.anchor = (TextAnchor)4;
                if (meshName == "bubble")
                {
                    this.ConfigureBubbleMarkerRenderers(simpleMarker);
                }
                return simpleMarker;
            }

            GameObject gameObject = new GameObject("ItemMarker");
            gameObject.transform.position = pos;
            gameObject.transform.SetParent(this.radarContainer.transform);
            bool flag12 = targetObject != null;
            if (flag12)
            {
                gameObject.name = "TrackedMarker_" + targetObject.GetInstanceID().ToString();
                this.markerToTarget[gameObject] = targetObject;
            }
            RadarMarkerMetadata metadata = new RadarMarkerMetadata
            {
                CanonicalLabel = canonicalLabel,
                Icon = icon,
                SpecificIconKey = specificIconKey,
                IsCooldown = flag
            };
            this.SetMarkerMetadata(gameObject, metadata);

            if (this.IsResourceVisualEspLabel(canonicalLabel))
            {
                return gameObject;
            }

            LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.material = xRay;
            lineRenderer.useWorldSpace = false;
            lineRenderer.startWidth = (lineRenderer.endWidth = 0.08f);
            lineRenderer.positionCount = 5;
            endColor.a = 0.8f;
            lineRenderer.startColor = (lineRenderer.endColor = endColor);
            lineRenderer.SetPosition(0, new Vector3(-0.6f, 0.1f, -0.6f));
            lineRenderer.SetPosition(1, new Vector3(0.6f, 0.1f, -0.6f));
            lineRenderer.SetPosition(2, new Vector3(0.6f, 0.1f, 0.6f));
            lineRenderer.SetPosition(3, new Vector3(-0.6f, 0.1f, 0.6f));
            lineRenderer.SetPosition(4, new Vector3(-0.6f, 0.1f, -0.6f));
            GameObject gameObject2 = new GameObject("LabelAnchor");
            gameObject2.transform.SetParent(gameObject.transform);
            gameObject2.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            GameObject gameObject3 = GameObject.CreatePrimitive((PrimitiveType)5);
            Object.Destroy(gameObject3.GetComponent<MeshCollider>());
            gameObject3.transform.SetParent(gameObject2.transform);
            gameObject3.transform.localPosition = Vector3.zero;
            gameObject3.transform.localScale = new Vector3(3.2f, 1.1f, 1f); // Slightly larger
            gameObject3.GetComponent<MeshRenderer>().material = bg;
            gameObject3.GetComponent<MeshRenderer>().material.color = bgColor; // Use colored background
            
            // Add subtle border effect
            GameObject border = GameObject.CreatePrimitive((PrimitiveType)5);
            Object.Destroy(border.GetComponent<MeshCollider>());
            border.transform.SetParent(gameObject2.transform);
            border.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            border.transform.localScale = new Vector3(3.3f, 1.2f, 1f);
            border.GetComponent<MeshRenderer>().material = bg;
            border.GetComponent<MeshRenderer>().material.color = new Color(endColor.r * 0.8f, endColor.g * 0.8f, endColor.b * 0.8f, 0.6f);
            
            GameObject gameObject4 = new GameObject("Text");
            TextMesh textMesh = gameObject4.AddComponent<TextMesh>();
            gameObject4.transform.SetParent(gameObject2.transform);
            gameObject4.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            textMesh.text = this.GetMarkerDisplayTitle(metadata);
            textMesh.color = endColor; // Use colored text
            textMesh.fontStyle = (FontStyle)1;
            textMesh.fontSize = 55; // Slightly larger font
            textMesh.characterSize = 0.065f;
            textMesh.anchor = (TextAnchor)4;
            if (meshName == "bubble")
            {
                this.ConfigureBubbleMarkerRenderers(gameObject);
            }
            return gameObject;
        }

        // Token: 0x0600001C RID: 28 RVA: 0x00005A64 File Offset: 0x00003C64
        private void UpdateMarkers()
        {
            bool flag = this.radarContainer == null;
            if (!flag)
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                Transform transform = cam.transform;
                Vector3 position = transform.position;
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    bool flag2 = child == null;
                    if (!flag2)
                    {
                        GameObject gameObject = child.gameObject;
                        bool isBubbleTrackedMarker = this.TryParseBubbleTrackedMarkerId(gameObject.name, out int bubbleTrackedId);
                        bool isHideAndSeekMorphMarker = this.TryParseHideAndSeekMorphMarkerId(gameObject.name, out uint hideAndSeekMorphTrackedId);
                        if (isBubbleTrackedMarker)
                        {
                            if (this.bubbleRadarSceneTargets.TryGetValue(bubbleTrackedId, out GameObject bubbleTarget)
                                && !this.IsUsableBubbleSceneObject(bubbleTarget))
                            {
                                if (this.bubbleRadarTrackedPositions.TryGetValue(bubbleTrackedId, out Vector3 lastTrackedBubblePos)
                                    && this.ShouldRetainMissingBubbleSceneMarker(position, lastTrackedBubblePos))
                                {
                                    this.bubbleRadarSceneTargets.Remove(bubbleTrackedId);
                                }
                                else
                                {
                                    this.RemoveBubbleTrackedMarker(bubbleTrackedId);
                                    this.bubbleRadarSnapshotPositions.Remove(bubbleTrackedId);
                                    goto IL_505;
                                }
                            }

                            if (!this.showBubbleRadar || !this.bubbleRadarTrackedPositions.TryGetValue(bubbleTrackedId, out Vector3 bubblePos))
                            {
                                this.RemoveBubbleTrackedMarker(bubbleTrackedId);
                                goto IL_505;
                            }

                            child.position = bubblePos;
                        }
                        else if (isHideAndSeekMorphMarker)
                        {
                            if (!this.showOtherPlayersRadar
                                || !this.hideAndSeekMorphTrackedPositions.TryGetValue(hideAndSeekMorphTrackedId, out Vector3 morphPos))
                            {
                                this.RemoveHideAndSeekMorphMarker(hideAndSeekMorphTrackedId);
                                goto IL_505;
                            }

                            child.position = morphPos;
                        }

                        float num = Vector3.Distance(position, child.position);
                        float markerMaxDistance = isBubbleTrackedMarker
                            ? Mathf.Max(BubbleRadarMaxDistance, this.radarMaxDistance)
                            : Mathf.Max(25f, this.radarMaxDistance);
                        bool flag3 = num > markerMaxDistance;
                        if (flag3)
                        {
                            this.RemoveMarkerMetadata(gameObject);
                            Object.Destroy(gameObject);
                            if (isBubbleTrackedMarker)
                            {
                                this.trackedBubbleMarkers.Remove(bubbleTrackedId);
                                this.bubbleRadarTrackedPositions.Remove(bubbleTrackedId);
                            }
                            if (isHideAndSeekMorphMarker)
                            {
                                this.RemoveHideAndSeekMorphMarker(hideAndSeekMorphTrackedId);
                            }
                            bool flag4 = gameObject.name.StartsWith("TrackedMarker_");
                            if (flag4)
                            {
                                this.RemoveTrackedMarkerMapping(gameObject);
                            }
                        }
                        else
                        {
                            bool flag7 = !isBubbleTrackedMarker && !isHideAndSeekMorphMarker && gameObject.name.StartsWith("TrackedMarker_");
                            if (flag7)
                            {
                                // Use name-based lookup: Il2Cpp managed wrapper identity differs on each cross-boundary access
                                GameObject gameObject2 = null;
                                foreach (KeyValuePair<GameObject, GameObject> kv in this.markerToTarget)
                                {
                                    if (kv.Key != null && kv.Key.name == gameObject.name)
                                    {
                                        gameObject2 = kv.Value;
                                        break;
                                    }
                                }
                                string trackedName = (gameObject2 != null && gameObject2.name != null) ? gameObject2.name.ToLowerInvariant() : string.Empty;
                                bool flag9 = gameObject2 != null && this.ShouldTrackInsectObject(trackedName);
                                bool flag10 = flag9 && !this.showInsectRadar;
                                if (flag10)
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                bool flagBird = gameObject2 != null && this.ShouldTrackBirdObject(trackedName);
                                if (flagBird && !this.showBirdRadar)
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                bool flagOtherPlayer = gameObject2 != null && this.IsOtherPlayerSkeletonGameObject(gameObject2);
                                if (flagOtherPlayer && !this.showOtherPlayersRadar)
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                if (flagOtherPlayer && this.IsLocalPlayerSkeletonGameObject(gameObject2))
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                bool flag11 = gameObject2 != null && this.ShouldTrackFishShadowObject(gameObject2);
                                if (flag11 && !this.showFishShadowRadar)
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                bool flag12 = gameObject2 != null && this.ShouldTrackMeteorObject(trackedName);
                                if (flag12 && !this.showMeteorRadar)
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                    goto IL_505;
                                }
                                bool flag13 = gameObject2 != null && gameObject2.activeInHierarchy;
                                if (flag13)
                                {
                                    child.position = gameObject2.transform.position;
                                }
                                else
                                {
                                    Object.Destroy(gameObject);
                                    this.RemoveTrackedMarkerMapping(gameObject);
                                }
                            }
                            Transform transform2 = child.Find("LabelAnchor");
                            bool flag16 = transform2 != null;
                            if (flag16)
                            {
                                Vector3 labelToCamera = transform.position - transform2.position;
                                if (labelToCamera.sqrMagnitude > 0.0001f)
                                {
                                    transform2.LookAt(transform);
                                    transform2.Rotate(0f, 180f, 0f);
                                }
                                TextMesh componentInChildren = transform2.GetComponentInChildren<TextMesh>();
                                bool flag17 = componentInChildren != null;
                                if (flag17)
                                {
                                    float value = Vector3.Distance(transform.position, child.position);
                                    RadarMarkerMetadata metadata = this.GetMarkerMetadata(child.gameObject);
                                    bool flag18 = metadata != null;
                                    if (flag18)
                                    {
                                        componentInChildren.text = this.GetMarkerDisplayTitle(metadata) + "\n" + value.ToString("F0") + "m";
                                    }
                                    else
                                    {
                                        string[] array = componentInChildren.text.Split(new char[] { '\n' }, StringSplitOptions.None);
                                        if (array.Length != 0)
                                        {
                                            TextMesh textMesh = componentInChildren;
                                            textMesh.text = $"{array[0]}\n{value:F0}m";
                                        }
                                    }
                                }
                            }
                        }
                    }
                IL_505:;
                }
            }
        }

        private void ConfigureBubbleMarkerRenderers(GameObject markerRoot)
        {
            if (markerRoot == null)
            {
                return;
            }

            try
            {
                LineRenderer[] lines = markerRoot.GetComponentsInChildren<LineRenderer>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    LineRenderer line = lines[i];
                    if (line == null)
                    {
                        continue;
                    }

                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    line.allowOcclusionWhenDynamic = false;
                }

                MeshRenderer[] renderers = markerRoot.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    MeshRenderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;

                    Material shared = renderer.sharedMaterial;
                    if (shared == null)
                    {
                        continue;
                    }

                    try
                    {
                        Material tuned = new Material(shared);
                        if (tuned.HasProperty("_ZTest"))
                        {
                            tuned.SetInt("_ZTest", 0);
                        }
                        if (tuned.HasProperty("_ZWrite"))
                        {
                            tuned.SetInt("_ZWrite", 0);
                        }
                        tuned.renderQueue = 5000;
                        renderer.material = tuned;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void RemoveTrackedMarkerMapping(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            this.RemoveMarkerMetadata(marker);

            // Name-based removal: Il2Cpp wrapper reference may differ from stored key
            foreach (KeyValuePair<GameObject, GameObject> kv in this.markerToTarget.ToList<KeyValuePair<GameObject, GameObject>>())
            {
                if (kv.Key != null && kv.Key.name == marker.name)
                {
                    this.markerToTarget.Remove(kv.Key);
                    break;
                }
            }
            int removeId = -1;
            foreach (KeyValuePair<int, GameObject> tracked in this.trackedObjectMarkers)
            {
                if (tracked.Value == marker)
                {
                    removeId = tracked.Key;
                    break;
                }
            }
            if (removeId != -1)
            {
                this.trackedObjectMarkers.Remove(removeId);
            }
        }

        private void RegisterTrackedMarkerForTarget(int instanceId, GameObject target)
        {
            if (target == null)
            {
                return;
            }

            foreach (KeyValuePair<GameObject, GameObject> pair in this.markerToTarget)
            {
                if (pair.Value == target && pair.Key != null)
                {
                    this.trackedObjectMarkers[instanceId] = pair.Key;
                    return;
                }
            }

            foreach (KeyValuePair<GameObject, GameObject> pair in this.markerToTarget)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                if (pair.Value.GetInstanceID() == instanceId)
                {
                    this.trackedObjectMarkers[instanceId] = pair.Key;
                    return;
                }
            }

            if (this.radarContainer != null)
            {
                string expectedName = "TrackedMarker_" + instanceId.ToString();
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    if (child == null || child.gameObject == null)
                    {
                        continue;
                    }

                    GameObject marker = child.gameObject;
                    if (marker.name == expectedName)
                    {
                        this.trackedObjectMarkers[instanceId] = marker;
                        this.markerToTarget[marker] = target;
                        return;
                    }
                }
            }
        }

        private void EnsureRadarMaterials()
        {
            if (this.radarLineMaterial == null)
            {
                this.radarLineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                this.radarLineMaterial.SetInt("_ZTest", 0);
            }

            if (this.radarFillMaterial == null)
            {
                this.radarFillMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                this.radarFillMaterial.SetInt("_ZTest", 0);
                this.radarFillMaterial.SetInt("_SrcBlend", 5);
                this.radarFillMaterial.SetInt("_DstBlend", 10);
                this.radarFillMaterial.SetInt("_ZWrite", 0);
            }
        }

        private string GetItemIconCacheDirectory()
        {
            return HelperPaths.GetDirectory("Cache", ITEM_ICON_CACHE_FOLDER);
        }

        private string GetItemIconCachePath(string key)
        {
            string safeName = this.SanitizeCacheFileName(this.NormalizeAutoSellMatchKey(key));
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = this.SanitizeCacheFileName(key);
            }
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "unknown";
            }

            return Path.Combine(this.GetItemIconCacheDirectory(), safeName + ".png");
        }

        private bool TryLoadCachedItemIcon(string key, out Texture2D texture)
        {
            texture = null;
            try
            {
                string path = this.GetItemIconCachePath(key);
                if (!File.Exists(path))
                {
                    if (this.TryLoadEmbeddedItemIcon(key, out texture) && texture != null)
                    {
                        this.SaveCachedItemIcon(key, texture);
                        return true;
                    }

                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!loaded.LoadImage(bytes))
                {
                    Object.Destroy(loaded);
                    return false;
                }

                texture = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryLoadEmbeddedItemIcon(string key, out Texture2D texture)
        {
            texture = null;
            string normalizedKey = this.NormalizeAutoSellMatchKey(key);
            string embeddedFileName;
            switch (normalizedKey)
            {
                case "tree":
                    embeddedFileName = "tree.png";
                    break;
                case "rare_tree":
                    embeddedFileName = "rare_tree.png";
                    break;
                default:
                    return false;
            }

            try
            {
                Assembly assembly = typeof(HeartopiaComplete).Assembly;
                string[] resourceNames = assembly.GetManifestResourceNames();
                if (resourceNames == null || resourceNames.Length == 0)
                {
                    return false;
                }

                string resourceName = resourceNames.FirstOrDefault(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    name.EndsWith(".Assets." + embeddedFileName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    resourceName = resourceNames.FirstOrDefault(name =>
                        !string.IsNullOrWhiteSpace(name) &&
                        name.EndsWith("." + embeddedFileName, StringComparison.OrdinalIgnoreCase));
                }
                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    return false;
                }

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return false;
                    }

                    byte[] bytes = new byte[stream.Length];
                    int read = stream.Read(bytes, 0, bytes.Length);
                    if (read <= 0)
                    {
                        return false;
                    }

                    Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!loaded.LoadImage(bytes))
                    {
                        Object.Destroy(loaded);
                        return false;
                    }

                    texture = loaded;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void SaveCachedItemIcon(string key, Texture2D texture)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || texture == null)
                {
                    return;
                }

                string path = this.GetItemIconCachePath(key);
                if (File.Exists(path))
                {
                    return;
                }

                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    return;
                }

                File.WriteAllBytes(path, png);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[IconCache] Failed to save item icon: " + ex.Message);
            }
        }

        private sealed class RadarMarkerMetadata
        {
            public string CanonicalLabel = string.Empty;
            public string Icon = string.Empty;
            public string SpecificIconKey = string.Empty;
            public bool IsCooldown;
            public Texture2D ResourceVisualEspIconTexture;
            public float ResourceVisualEspNextIconResolveAt;
        }

    }
}
