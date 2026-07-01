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
        private bool IsForceShopBuyAllSupported(int selectedIndex, out string reason)
        {
            reason = null;
            switch (selectedIndex)
            {
                case 0:
                    reason = "No shop selected.";
                    return false;
                case 18:
                    reason = "Meteor Exchange uses item cost, not Coin.";
                    return false;
            }

            return true;
        }

        private bool TryResolveForceOpenShopStoreId(int selectedIndex, out int storeId, out string label, out string unsupportedReason)
        {
            storeId = 0;
            label = string.Empty;
            unsupportedReason = null;

            if (!this.IsForceShopBuyAllSupported(selectedIndex, out unsupportedReason))
            {
                return false;
            }

            switch (selectedIndex)
            {
                case 4:
                    storeId = 5;
                    label = "Clothing Store";
                    return true;
                case 1:
                    storeId = 55;
                    label = "Birdwatching Store";
                    return true;
                case 2:
                    storeId = 147;
                    label = "Book Shop";
                    return true;
                case 3:
                    storeId = 10;
                    label = "Carpet Shop";
                    return true;
                case 5:
                    storeId = 53;
                    label = "Cooking Store";
                    return true;
                case 7:
                    storeId = 52;
                    label = "Fishing Store";
                    return true;
                case 8:
                    storeId = 6;
                    label = "Furniture Extra";
                    return true;
                case 9:
                    storeId = 86;
                    label = "Fortune Store - Rainbow";
                    return true;
                case 10:
                    storeId = 87;
                    label = "Fortune Store - Rain";
                    return true;
                case 11:
                    storeId = 51;
                    label = "Garden Store";
                    return true;
                case 13:
                    storeId = 56;
                    label = "Insect Catching Store";
                    return true;
                case 14:
                    storeId = 54;
                    label = "Pet Store";
                    return true;
                case 15:
                    storeId = 82;
                    label = "Special Home Decor Store";
                    return true;
                case 16:
                    storeId = 7;
                    label = "Showroom";
                    return true;
                case 17:
                    storeId = 81;
                    label = "Music Store";
                    return true;
                case 12:
                    label = "General Store";
                    if (this.forceOpenShopResolvedStoreIds.TryGetValue(label, out int cachedStoreId) && cachedStoreId > 0 && cachedStoreId != 88)
                    {
                        storeId = cachedStoreId;
                        return true;
                    }

                    string[] keywords = new string[]
                    {
                        "ui_picture_shop_img_1001",
                        "shop_img_1001",
                        "ka ching",
                        "kaching",
                        "general goods",
                        "general store"
                    };
                    if (!this.TryResolveStoreIdByKeywords(keywords, out storeId, out string matchedName))
                    {
                        unsupportedReason = "General Store store id not found.";
                        return false;
                    }

                    if (storeId == 88)
                    {
                        unsupportedReason = "Resolved pay shop (88), refused.";
                        return false;
                    }

                    label = string.IsNullOrWhiteSpace(matchedName) ? label : matchedName;
                    this.forceOpenShopResolvedStoreIds["General Store"] = storeId;
                    return true;
                default:
                    unsupportedReason = "Unknown shop index " + selectedIndex + ".";
                    return false;
            }
        }

        private bool TryOpenSelectedForceShop(out string status)
        {
            status = "No shop selected.";
            string selection = (this.forceOpenShopSelectedIndex >= 0 && this.forceOpenShopSelectedIndex < this.forceOpenShopOptions.Length)
                ? this.forceOpenShopOptions[this.forceOpenShopSelectedIndex]
                : ("Index " + this.forceOpenShopSelectedIndex);
            this.LogForceOpenShop("Requested shop open for selection: " + selection);

            switch (this.forceOpenShopSelectedIndex)
            {
                case 4:
                    if (this.TryInvokeAuraMonoStaticNullBoolMethod("XDTGame.UI.Panel.DressShopPanel", "Open", false, "Opened Clothing Store.")
                        || this.TryOpenPanelByRegistry("DressShopPanel", intent => this.TryConfigureIntentBool(intent, "disrobe", false), "Opened Clothing Store.")
                        || this.TryOpenPanelByResolvedTypeName("XDTGame.UI.Panel.DressShopPanel", intent => this.TryConfigureIntentBool(intent, "disrobe", false), "Opened Clothing Store.")) { status = this.forceOpenShopStatus; return true; }
                    status = this.forceOpenShopStatus; return false;
                case 6:
                    if (this.TryInvokeAuraMonoStaticIntMethod("XDTGame.UI.Panel.FaceShopPanel", "OpenAvatarPanelShop", 3008, "Opened Face Shop Panel.")
                        || this.TryOpenPanelByRegistry(
                        "FaceShopPanel",
                        intent => this.TryConfigureIntentInt(intent, "id", 3008),
                        "Opened Face Shop Panel.")
                        || this.TryOpenPanelByResolvedTypeName(
                        "XDTGame.UI.Panel.FaceShopPanel",
                        intent => this.TryConfigureIntentInt(intent, "id", 3008),
                        "Opened Face Shop Panel.")) { status = this.forceOpenShopStatus; return true; }
                    status = this.forceOpenShopStatus; return false;
                case 12:
                    if (this.TryOpenGeneralStore()) { status = this.forceOpenShopStatus; return true; }
                    status = this.forceOpenShopStatus; return false;
                case 18:
                    if (this.TryOpenMeteorWeatherExchangeShop()) { status = this.forceOpenShopStatus; return true; }
                    status = this.forceOpenShopStatus; return false;
                default:
                    if (!this.TryResolveForceOpenShopStoreId(this.forceOpenShopSelectedIndex, out int storeId, out string label, out string unsupportedReason))
                    {
                        status = unsupportedReason ?? "Shop not supported.";
                        this.LogForceOpenShop(status);
                        return false;
                    }

                    if (this.TryOpenShopPanelByStoreId(storeId, 0, label))
                    {
                        status = this.forceOpenShopStatus;
                        return true;
                    }

                    status = this.forceOpenShopStatus;
                    return false;
            }
        }

        private bool TryOpenShopPanelByStoreId(int storeId, int slotId, string label)
        {
            if (storeId <= 0)
            {
                this.forceOpenShopStatus = "Invalid store id for " + label + ".";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            this.LogForceOpenShop("Opening " + label + " via storeId=" + storeId + " slotId=" + slotId);
            return this.TryInvokeAuraMonoStaticIntIntMethod(
                "XDTGame.UI.Panel.ShopPanel",
                "OpenShopPanel",
                storeId,
                slotId,
                "Opened " + label + ".");
        }

        private bool TryOpenWeatherExchangeShopPanelByStoreId(int storeId, int slotId, string label)
        {
            if (storeId <= 0)
            {
                this.forceOpenShopStatus = "Invalid store id for " + label + ".";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            this.LogForceOpenShop("Opening " + label + " via WeatherExchange storeId=" + storeId + " slotId=" + slotId);
            if (this.TryInvokeAuraMonoStaticIntIntMethod(
                "XDTGame.UI.Panel.WeatherExchangeShopPanel",
                "OpenWeatherExchangePanel",
                storeId,
                slotId,
                "Opened " + label + "."))
            {
                return true;
            }

            return this.TryInvokeAuraMonoStaticIntIntMethod(
                "XDTGame.UI.Panel.WeatherExchangeShopPanel",
                "OpenWeatherExchangePanel",
                storeId,
                0,
                "Opened " + label + ".");
        }

        private bool TryOpenResolvedStorePanel(string label, string[] keywords)
        {
            if (!this.TryResolveStoreIdByKeywords(keywords, out int storeId, out string matchedName))
            {
                this.forceOpenShopStatus = label + " store id not found.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            this.LogForceOpenShop("Resolved " + label + " to storeId=" + storeId + " name='" + matchedName + "'");
            return this.TryOpenShopPanelByStoreId(storeId, 0, label);
        }

        private bool TryOpenGeneralStore()
        {
            string label = "General Store";
            string[] keywords = new string[]
            {
                "ui_picture_shop_img_1001",
                "shop_img_1001",
                "ka ching",
                "kaching",
                "general goods",
                "general store"
            };

            if (this.forceOpenShopResolvedStoreIds.TryGetValue(label, out int cachedStoreId) && cachedStoreId > 0 && cachedStoreId != 88)
            {
                this.LogForceOpenShop("Opening cached " + label + " storeId=" + cachedStoreId);
                return this.TryOpenShopPanelByStoreId(cachedStoreId, 0, label);
            }

            if (!this.TryResolveStoreIdByKeywords(keywords, out int storeId, out string matchedName))
            {
                this.forceOpenShopStatus = "General Store store id not found. Look for a candidate with npcPicture='ui_picture_shop_img_1001' in the log.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            if (storeId == 88)
            {
                this.forceOpenShopStatus = "Resolved General Store to storeId=88, but 88 is the pay/material shop. Refusing to open wrong shop.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            this.forceOpenShopResolvedStoreIds[label] = storeId;
            this.LogForceOpenShop("Resolved " + label + " to storeId=" + storeId + " name='" + matchedName + "' using Ka Ching/general-store markers.");
            bool opened = this.TryOpenShopPanelByStoreId(storeId, 0, label);
            if (opened)
            {
                this.forceOpenShopStatus = "Opened " + label + " (storeId " + storeId + ").";
            }
            return opened;
        }

        private bool TryOpenForceShopByResolvedStoreId(string label, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                this.forceOpenShopStatus = "Invalid force-open shop label.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            if (this.forceOpenShopResolvedStoreIds.TryGetValue(label, out int cachedStoreId) && cachedStoreId > 0)
            {
                this.LogForceOpenShop("Opening cached " + label + " storeId=" + cachedStoreId);
                return this.TryOpenShopPanelByStoreId(cachedStoreId, 0, label);
            }

            if (!this.TryResolveStoreIdByKeywords(keywords, out int storeId, out string matchedName))
            {
                this.forceOpenShopStatus = label + " store id not found.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            this.forceOpenShopResolvedStoreIds[label] = storeId;
            this.LogForceOpenShop("Resolved " + label + " to storeId=" + storeId + " name='" + matchedName + "'");
            bool opened = this.TryOpenShopPanelByStoreId(storeId, 0, label);
            if (opened)
            {
                this.forceOpenShopStatus = "Opened " + label + " (storeId " + storeId + ").";
            }
            return opened;
        }

        private bool TryOpenForceShopByManualStoreId(out string status)
        {
            status = "Enter a positive store id.";
            string raw = (this.forceOpenShopManualStoreIdInput ?? string.Empty).Trim();
            if (!int.TryParse(raw, out int storeId) || storeId <= 0)
            {
                this.LogForceOpenShop("Manual store id invalid: '" + raw + "'");
                return false;
            }

            this.LogForceOpenShop("Manual store id open requested: storeId=" + storeId);
            bool opened = this.TryOpenShopPanelByStoreId(storeId, 0, "Manual store " + storeId);
            status = this.forceOpenShopStatus;
            return opened;
        }

        private bool TryOpenForceShopByManualStoreName(out string status)
        {
            status = "Enter a store name.";
            string rawName = (this.forceOpenShopManualStoreNameInput ?? string.Empty).Trim();
            if (rawName.Length < 2)
            {
                this.LogForceOpenShop("Manual store name invalid: '" + rawName + "'");
                return false;
            }

            if (!this.TryResolveStoreIdByKeywords(new string[] { rawName }, out int storeId, out string matchedName))
            {
                status = "Store name not found: " + rawName;
                this.LogForceOpenShop(status);
                return false;
            }

            this.LogForceOpenShop("Manual store name resolved '" + rawName + "' to storeId=" + storeId + " name='" + matchedName + "'");
            bool opened = this.TryOpenShopPanelByStoreId(storeId, 0, matchedName);
            status = this.forceOpenShopStatus;
            return opened;
        }

        private bool TryResolveStoreIdByKeywords(string[] keywords, out int storeId, out string matchedName)
        {
            storeId = 0;
            matchedName = string.Empty;

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    this.LogForceOpenShop("Store resolve failed: managed TableData type not found. Trying Aura fallback.");
                    return this.TryResolveStoreIdByKeywordsMono(keywords, out storeId, out matchedName);
                }

                FieldInfo storeInfosField = tableDataType.GetField("TableStoreInfos", BindingFlags.Public | BindingFlags.Static);
                object storeInfosObj = storeInfosField?.GetValue(null);
                if (storeInfosObj == null)
                {
                    this.LogForceOpenShop("Store resolve failed: TableStoreInfos unavailable.");
                    return false;
                }

                MethodInfo localizationMethod = this.FindTableLocalizationMethod(tableDataType);
                IDictionary dictionary = storeInfosObj as IDictionary;
                if (dictionary == null)
                {
                    this.LogForceOpenShop("Store resolve failed: TableStoreInfos is not a dictionary.");
                    return false;
                }

                int bestScore = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null)
                    {
                        continue;
                    }

                    int candidateId;
                    try
                    {
                        candidateId = Convert.ToInt32(entry.Key);
                    }
                    catch
                    {
                        object idObj;
                        if (!this.TryGetObjectMember(entry.Value, "id", out idObj))
                        {
                            continue;
                        }

                        try
                        {
                            candidateId = Convert.ToInt32(idObj);
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    string displayName = this.TryGetLocalizedStoreName(entry.Value, localizationMethod);
                    string npcPictureName = this.TryReadObjectString(entry.Value, "npcPictureName");
                    string bgPictureId = this.TryReadObjectString(entry.Value, "bgPictureId");
                    string decorationPictureName = this.TryReadObjectString(entry.Value, "decorationPictureName");
                    string combined = ((displayName ?? string.Empty) + " " + npcPictureName + " " + bgPictureId + " " + decorationPictureName).ToLowerInvariant();
                    int score = this.ScoreForceOpenStoreMatch(combined, keywords);
                    if (score <= 0)
                    {
                        continue;
                    }

                    this.LogForceOpenShop("Store candidate match id=" + candidateId + " score=" + score + " name='" + (displayName ?? string.Empty) + "' npcPicture='" + npcPictureName + "'");
                    if (score > bestScore
                        || (score == bestScore && this.ShouldPreferForceOpenStoreCandidate(keywords, candidateId, displayName, npcPictureName, bgPictureId, decorationPictureName, storeId, matchedName, null, null, null)))
                    {
                        bestScore = score;
                        storeId = candidateId;
                        matchedName = string.IsNullOrWhiteSpace(displayName) ? ("Store " + candidateId) : displayName;
                    }
                }

                return storeId > 0;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Store resolve exception: " + ex);
                return this.TryResolveStoreIdByKeywordsMono(keywords, out storeId, out matchedName);
            }
        }

        private bool TryResolveStoreIdByKeywordsMono(string[] keywords, out int storeId, out string matchedName)
        {
            storeId = 0;
            matchedName = string.Empty;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
                {
                    this.LogForceOpenShop("Aura store resolve failed: runtime not ready.");
                    return false;
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage == IntPtr.Zero)
                {
                    this.LogForceOpenShop("Aura store resolve failed: EcsClient image not found.");
                    return false;
                }

                IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    this.LogForceOpenShop("Aura store resolve failed: TableData class not found.");
                    return false;
                }

                IntPtr tableStoreInfosObj;
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableStoreInfos", out tableStoreInfosObj) || tableStoreInfosObj == IntPtr.Zero)
                {
                    this.LogForceOpenShop("Aura store resolve failed: TableStoreInfos unavailable.");
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(tableStoreInfosObj, items) || items.Count == 0)
                {
                    this.LogForceOpenShop("Aura store resolve failed: TableStoreInfos enumeration empty.");
                    return false;
                }

                int bestScore = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (!this.TryReadStoreTableEntryMono(tableDataClass, items[i], out int candidateId, out string displayName, out string npcPictureName, out string bgPictureId, out string decorationPictureName))
                    {
                        continue;
                    }

                    string combined = ((displayName ?? string.Empty) + " " + npcPictureName + " " + bgPictureId + " " + decorationPictureName).ToLowerInvariant();
                    int score = this.ScoreForceOpenStoreMatch(combined, keywords);
                    if (score <= 0)
                    {
                        continue;
                    }

                    this.LogForceOpenShop("Aura store candidate match id=" + candidateId + " score=" + score + " name='" + (displayName ?? string.Empty) + "' npcPicture='" + npcPictureName + "'");
                    if (score > bestScore
                        || (score == bestScore && this.ShouldPreferForceOpenStoreCandidate(keywords, candidateId, displayName, npcPictureName, bgPictureId, decorationPictureName, storeId, matchedName, null, null, null)))
                    {
                        bestScore = score;
                        storeId = candidateId;
                        matchedName = string.IsNullOrWhiteSpace(displayName) ? ("Store " + candidateId) : displayName;
                    }
                }

                return storeId > 0;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Aura store resolve exception: " + ex);
                return false;
            }
        }

        private string TryGetLocalizedStoreName(object tableStoreInfoObj, MethodInfo localizationMethod)
        {
            if (tableStoreInfoObj == null)
            {
                return string.Empty;
            }

            string localized = string.Empty;
            object nameLocIdObj;
            if (localizationMethod != null && this.TryGetObjectMember(tableStoreInfoObj, "nameLocId", out nameLocIdObj))
            {
                try
                {
                    int nameLocId = Convert.ToInt32(nameLocIdObj);
                    ParameterInfo[] parameters = localizationMethod.GetParameters();
                    object[] args = parameters.Length >= 2
                        ? new object[] { nameLocId, false }
                        : new object[] { nameLocId };
                    localized = localizationMethod.Invoke(null, args) as string;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized.Trim();
            }

            return this.TryReadObjectString(tableStoreInfoObj, "npcPictureName");
        }

        private bool ShouldPreferForceOpenStoreCandidate(
            string[] keywords,
            int candidateId,
            string displayName,
            string npcPictureName,
            string bgPictureId,
            string decorationPictureName,
            int currentBestId,
            string currentBestDisplayName,
            string currentBestNpcPictureName,
            string currentBestBgPictureId,
            string currentBestDecorationPictureName)
        {
            if (candidateId <= 0 || currentBestId <= 0)
            {
                return false;
            }

            if (!this.IsFortuneWeatherForceOpenRequest(keywords))
            {
                return false;
            }

            int candidatePreference = this.GetFortuneWeatherStorePreference(keywords, candidateId, displayName, npcPictureName, bgPictureId, decorationPictureName);
            int currentPreference = this.GetFortuneWeatherStorePreference(keywords, currentBestId, currentBestDisplayName, currentBestNpcPictureName, currentBestBgPictureId, currentBestDecorationPictureName);
            return candidatePreference > currentPreference;
        }

        private bool IsFortuneWeatherForceOpenRequest(string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
            {
                return false;
            }

            bool hasFortune = false;
            bool hasWeatherVariant = false;
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = (keywords[i] ?? string.Empty).Trim().ToLowerInvariant();
                if (keyword == "fortune")
                {
                    hasFortune = true;
                }
                else if (keyword == "rainbow" || keyword == "rain" || keyword == "meteor")
                {
                    hasWeatherVariant = true;
                }
            }

            return hasFortune && hasWeatherVariant;
        }

        private int GetFortuneWeatherStorePreference(string[] keywords, int storeId, string displayName, string npcPictureName, string bgPictureId, string decorationPictureName)
        {
            string keywordBlob = string.Join(" ", keywords ?? Array.Empty<string>()).ToLowerInvariant();
            string metadata = ((displayName ?? string.Empty) + " " + (npcPictureName ?? string.Empty) + " " + (bgPictureId ?? string.Empty) + " " + (decorationPictureName ?? string.Empty)).ToLowerInvariant();
            int preference = 0;

            if (keywordBlob.Contains("rainbow"))
            {
                if (storeId == 86)
                {
                    preference += 20;
                }
                if (metadata.Contains("1030"))
                {
                    preference += 10;
                }
            }

            if (keywordBlob.Contains("rain") || keywordBlob.Contains("meteor"))
            {
                if (storeId == 87)
                {
                    preference += 20;
                }
                if (metadata.Contains("1031"))
                {
                    preference += 10;
                }
            }

            return preference;
        }

        private int ScoreForceOpenStoreMatch(string haystack, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(haystack) || keywords == null || keywords.Length == 0)
            {
                return 0;
            }

            int score = 0;
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (!haystack.Contains(keyword.ToLowerInvariant()))
                {
                    continue;
                }

                score += keyword.Length >= 6 ? 3 : 2;
            }

            if (haystack.Contains("store"))
            {
                score += 1;
            }

            return score;
        }

        private bool TryReadStoreTableEntryMono(IntPtr tableDataClass, IntPtr itemObj, out int storeId, out string displayName, out string npcPictureName, out string bgPictureId, out string decorationPictureName)
        {
            storeId = 0;
            displayName = string.Empty;
            npcPictureName = string.Empty;
            bgPictureId = string.Empty;
            decorationPictureName = string.Empty;

            if (itemObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr valueObj = IntPtr.Zero;
            IntPtr keyObj = IntPtr.Zero;
            bool hasValue = this.TryGetMonoObjectMember(itemObj, "Value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "_value", out valueObj);
            bool hasKey = this.TryGetMonoObjectMember(itemObj, "Key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "_key", out keyObj);

            if (!hasValue || valueObj == IntPtr.Zero)
            {
                valueObj = itemObj;
            }

            if (hasKey && keyObj != IntPtr.Zero)
            {
                this.TryGetMonoInt32Member(keyObj, "m_value", out storeId);
            }

            if (storeId <= 0)
            {
                this.TryGetMonoInt32Member(valueObj, "id", out storeId);
                if (storeId <= 0)
                {
                    this.TryGetMonoIntMember(valueObj, "id", out storeId);
                }
            }

            npcPictureName = this.TryReadMonoStringMemberOrEmpty(valueObj, "npcPictureName");
            bgPictureId = this.TryReadMonoStringMemberOrEmpty(valueObj, "bgPictureId");
            decorationPictureName = this.TryReadMonoStringMemberOrEmpty(valueObj, "decorationPictureName");

            int nameLocId = 0;
            this.TryGetMonoInt32Member(valueObj, "nameLocId", out nameLocId);
            if (nameLocId <= 0)
            {
                this.TryGetMonoIntMember(valueObj, "nameLocId", out nameLocId);
            }

            if (nameLocId > 0)
            {
                displayName = this.TryGetLocalizationTextMono(tableDataClass, nameLocId);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = npcPictureName;
            }

            return storeId > 0;
        }

        private void LogForceOpenShop(string message)
        {
            if (this.forceOpenShopLogsEnabled && !string.IsNullOrWhiteSpace(message))
            {
                ModLogger.Msg("[ForceOpenShop] " + message);
            }
        }

        private void StartAutoBuy()
        {
            try
            {
                GameObject p = GameObject.Find("p_player_skeleton(Clone)");
                if (p != null) this.autoBuySavedPosition = p.transform.position;
                this.autoBuySubState = 1; // teleporting
                this.autoBuyStepTimer = Time.unscaledTime + 0.1f;
                this.autoBuyShopWaitStartedAt = 0f;
                this.autoBuyStoreSelectRetryCount = 0;
                this.autoBuyPreviousGameSpeed = this.gameSpeed;
                this.SetGameSpeed(5f);
                this.autoBuyForcedGameSpeed = true;
                this.autoBuyCurrentIngredientIndex = 0;
                this.autoBuyPurchasedCount = 0;
                this.autoBuyShopScrollStep = -1;
                this.autoBuyPopupCloseRetryAt = 0f;
                this.autoBuyPopupSlowScanAt = 0f;
                this.TeleportToLocation(this.autoBuyNearbyPos);
                LogAutoBuy(" Started: teleporting to nearby position first (Game Speed x5.0)");
            }
            catch (Exception ex) { LogAutoBuy(" Start error: " + ex.Message); this.StopAutoBuy("Start error"); }
        }

        private void StopAutoBuy(string reason)
        {
            LogAutoBuy(" Stopped: " + reason);
            this.CloseAutoBuyPanels();
            this.autoBuyEnabled = false;
            this.autoBuySubState = 0;
            this.autoBuyShopScrollStep = -1;
            this.autoBuyPopupCloseRetryAt = 0f;
            this.autoBuyPopupSlowScanAt = 0f;
            if (this.autoBuyForcedGameSpeed)
            {
                this.SetGameSpeed(Mathf.Max(1f, this.autoBuyPreviousGameSpeed));
                this.autoBuyForcedGameSpeed = false;
            }
            try
            {
                EventSystem current = EventSystem.current;
                EventSystem restoreTarget = this.blockedEventSystem != null ? this.blockedEventSystem : current;
                if (restoreTarget != null)
                {
                    restoreTarget.enabled = true;
                }
                this.eventSystemBlockedByMenu = false;
                this.blockedEventSystem = null;
                if (current != null)
                {
                    current.SetSelectedGameObject(null);
                    current.sendNavigationEvents = true;
                }
            }
            catch { }
            // teleport back if we have a saved position
            if (this.autoBuySavedPosition != Vector3.zero)
            {
                this.TeleportToLocation(this.autoBuySavedPosition);
                this.autoBuySavedPosition = Vector3.zero;
            }
        }

        private void LogAutoBuy(string message)
        {
            if (this.autoBuyLogsEnabled)
            {
                ModLogger.Msg("[AutoBuy] " + message);
            }
        }

        private void CloseAutoBuyPanels()
        {
            try
            {
                this.TryCloseAutoBuyObtainedPopup();
                string[] forceHidePanels = new string[]
                {
                    "GameApp/startup_root(Clone)/XDRUIRoot/Top/AlertRewardPanel(Clone)"
                };
                for (int i = 0; i < forceHidePanels.Length; i++)
                {
                    GameObject panel = GameObject.Find(forceHidePanels[i]);
                    if (panel != null && panel.activeInHierarchy) SimulateClick(panel);
                }

                // Dialogue panel close/back
                this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/back@btn");
                this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/exit@btn@go");

                // Some dialogue steps require clicking content/background to advance before close works.
                // Use exact inspector-confirmed paths first, then generic dialogue advance fallback.
                string[] dialogueTapPaths = new string[]
                {
                    "GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/text@list@t/Viewport/textContent@t/DialogueTextWidget(Clone)/content@txt",
                    "GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/bg",
                    "GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go",
                    "GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/main@go/DialogMsgWidget@go@w"
                };

                for (int i = 0; i < dialogueTapPaths.Length; i++)
                {
                    GameObject go = GameObject.Find(dialogueTapPaths[i]);
                    if (go != null && go.activeInHierarchy)
                    {
                        SimulateClick(go);
                    }
                }

                this.TryAdvanceDialogueText();

                // Try closing SalePanel and ShopPanel using generic close/back buttons.
                string[] panelPaths = new string[]
                {
                    "GameApp/startup_root(Clone)/XDUIRoot/Top/SalePanel(Clone)",
                    "GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)"
                };

                for (int p = 0; p < panelPaths.Length; p++)
                {
                    GameObject panel = GameObject.Find(panelPaths[p]);
                    if (panel == null || !panel.activeInHierarchy) continue;

                    Button[] buttons = panel.GetComponentsInChildren<Button>(true);
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Button b = buttons[i];
                        if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                        string n = (b.name ?? string.Empty).ToLowerInvariant();
                        if (n.Contains("close") || n.Contains("back") || n.Contains("return") || n.Contains("exit") || n.Contains("cancel"))
                        {
                            b.onClick.Invoke();
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private bool TryCloseAutoBuyObtainedPopup()
        {
            try
            {
                string panelPath = "GameApp/startup_root(Clone)/XDUIRoot/Top/AlertRewardPanel(Clone)";
                GameObject rewardPanel = GameObject.Find(panelPath);
                
                // Only proceed if panel is actually detected
                if (rewardPanel == null || !rewardPanel.activeInHierarchy)
                {
                    return false;
                }

                // Track that we detected the panel - we'll verify it closes after attempting
                bool attemptedClose = false;

                if (this.TryCloseAlertRewardPopupViaTipManager())
                {
                    attemptedClose = true;
                }

                if (!attemptedClose && this.TryInvokeAlertRewardPanelConfirm(rewardPanel))
                {
                    attemptedClose = true;
                }

                string confirmPath = panelPath + "/AniRoot@ani/mask/operatorsframe/confirm@btn";
                if (!attemptedClose && this.ClickButtonIfExistsReturn(confirmPath))
                {
                    attemptedClose = true;
                }

                if (!attemptedClose)
                {
                    string[] directPaths = new string[]
                    {
                        panelPath + "/AniRoot@ani/popup/background",
                        panelPath + "/AniRoot@ani/mask",
                        panelPath + "/AniRoot@ani/popup/content/rewards@list/Viewport/Content/RewardWidget(Clone)/cell@btn"
                    };

                    for (int i = 0; i < directPaths.Length; i++)
                    {
                        GameObject target = GameObject.Find(directPaths[i]);
                        if (target != null && target.activeInHierarchy && (this.DirectClickGameButtonReturn(target) || SimulateClick(target)))
                        {
                            attemptedClose = true;
                            break;
                        }
                    }
                }

                if (!attemptedClose)
                {
                    if (this.DirectClickGameButtonReturn(rewardPanel) || SimulateClick(rewardPanel))
                    {
                        attemptedClose = true;
                    }
                    else
                    {
                        this.SendLeftClickInputTap();
                        this.SendEnterMessage();
                        this.SendEscMessage();
                        attemptedClose = true;
                    }
                }

                if (!attemptedClose && Time.unscaledTime >= this.autoBuyPopupSlowScanAt)
                {
                    this.autoBuyPopupSlowScanAt = Time.unscaledTime + 1f;
                    // Use Object.FindObjectsOfType (active-only) instead of Resources.FindObjectsOfTypeAll (all including inactive)
                    Transform[] allTransforms = Object.FindObjectsOfType<Transform>();
                    for (int i = 0; i < allTransforms.Length; i++)
                    {
                        Transform tr = allTransforms[i];
                        if (tr == null) continue;
                        string name = tr.name ?? string.Empty;
                        if (!name.Equals("AlertRewardPanel(Clone)", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        GameObject go = tr.gameObject;
                        if (go == null || !go.activeInHierarchy) continue;
                        if (this.DirectClickGameButtonReturn(go) || SimulateClick(go))
                        {
                            attemptedClose = true;
                            break;
                        }
                    }
                }

                // Verify the panel actually closed before returning true
                if (attemptedClose)
                {
                    GameObject verifyPanel = GameObject.Find(panelPath);
                    if (verifyPanel == null || !verifyPanel.activeInHierarchy)
                    {
                        LogAutoBuy(" Obtained popup closed successfully");
                        return true;
                    }
                    // Panel still exists, don't return true yet - let caller retry
                    LogAutoBuy(" Attempted to close popup but panel still visible");
                    return false;
                }
            }
            catch { }
            return false;
        }

        private bool ClickGardenStoreItemByMatch(string match)
        {
            try
            {
                GameObject shop = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                if (shop == null) return false;
                Transform scrollTransform = shop.transform.Find("goods@scroll");
                Transform content = shop.transform.Find("goods@scroll/Content");
                if (content == null) return false;
                if (content.childCount == 0)
                {
                    LogAutoBuy("[Garden] Shop content empty - will retry shortly");
                    return false;
                }

                ScrollRect scrollRect = null;
                if (scrollTransform != null)
                {
                    scrollRect = scrollTransform.GetComponent<ScrollRect>() ?? scrollTransform.GetComponentInChildren<ScrollRect>(true);
                }

                if (TryClickVisibleCookingStoreItemByMatch(content, match)) return true;

                if (scrollRect != null)
                {
                    const int scrollSteps = 12;
                    if (this.autoBuyGardenShopScrollStep < 0)
                    {
                        this.autoBuyGardenShopScrollStep = 0;
                        SetCookingStoreScrollPosition(scrollRect, 1f);
                        return false;
                    }
                    else if (this.autoBuyGardenShopScrollStep < scrollSteps)
                    {
                        this.autoBuyGardenShopScrollStep++;
                        float normalized = 1f - ((float)this.autoBuyGardenShopScrollStep / (float)scrollSteps);
                        SetCookingStoreScrollPosition(scrollRect, normalized);
                        return false;
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy("[Garden] ClickGardenStoreItemByMatch error: " + ex.Message); }
            return false;
        }

        private void RunAutoBuyLogic()
        {
            try
            {
                if (!this.autoBuyEnabled)
                {
                    if (this.autoBuyForcedGameSpeed)
                    {
                        this.SetGameSpeed(Mathf.Max(1f, this.autoBuyPreviousGameSpeed));
                        this.autoBuyForcedGameSpeed = false;
                    }
                    return;
                }
                float now = Time.unscaledTime;
                if (now >= this.autoBuyPopupCloseRetryAt && this.TryCloseAutoBuyObtainedPopup())
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.12f;
                    this.autoBuyStepTimer = now + 0.12f;
                    return;
                }
                if (now >= this.autoBuyPopupCloseRetryAt)
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.2f;
                }
                switch (this.autoBuySubState)
                {
                    case 1: // teleporting to nearby position
                        // wait for teleport to finish (teleportFramesRemaining decreases)
                        if (this.teleportFramesRemaining <= 0)
                        {
                            this.autoBuySubState = 12; // wait 3s then teleport to NPC front
                            this.autoBuyStepTimer = now + 3f;
                            LogAutoBuy(" Arrived at nearby position, waiting 3s before approaching NPC");
                        }
                        break;
                    case 12: // waiting at nearby pos, then teleport to NPC front
                        if (now < this.autoBuyStepTimer) break;
                        this.TeleportToLocation(this.autoBuyTargetPos);
                        this.autoBuySubState = 2; // waiting for dialogue
                        this.autoBuyStepTimer = now + 0.8f;
                        LogAutoBuy(" Teleporting to NPC front position");
                        break;
                    case 2: // waiting for dialogue - click chat icon until dialogue shows
                        if (now < this.autoBuyStepTimer) break;
                        // try to click chat icon
                        if (TryClickNpcChatIcon()) { this.autoBuyStepTimer = now + 0.5f; }
                        else { this.autoBuyStepTimer = now + 0.12f; }
                        // check if dialogue panel present
                        GameObject dlg = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                        if (dlg != null && dlg.activeInHierarchy)
                        {
                            this.autoBuySubState = 3; this.autoBuyStepTimer = now + 0.2f; LogAutoBuy(" Dialogue opened");
                        }
                        break;
                    case 3: // select cooking store
                        if (now < this.autoBuyStepTimer) break;
                        // NPC text may still be typing; options often appear only after advancing dialogue.
                        if (!HasDialogueOptionsVisible())
                        {
                            if (TryAdvanceDialogueText())
                            {
                                LogAutoBuy(" Advanced dialogue text, waiting for options");
                            }
                            this.autoBuyStepTimer = now + 0.12f;
                            break;
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "cooking store", "cook", "store" }))
                        {
                            // go to a waiting-for-shop state so we only attempt purchases after shop content is populated
                            this.autoBuySubState = 31;
                            this.autoBuyStepTimer = now + 0.25f;
                            this.autoBuyShopWaitStartedAt = now;
                            this.autoBuyStoreSelectRetryCount++;
                            LogAutoBuy(" Selected Cooking Store, waiting for shop content");
                        }
                        else { this.autoBuyStepTimer = now + 0.15f; }
                        break;
                    case 31: // wait for ShopPanel to appear and be populated
                        if (now < this.autoBuyStepTimer) break;
                        GameObject shopChk = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                        if (shopChk != null && shopChk.activeInHierarchy)
                        {
                            Transform contentChk = shopChk.transform.Find("goods@scroll/Content");
                            if (contentChk != null && contentChk.childCount > 0)
                            {
                                this.autoBuySubState = 4; this.autoBuyStepTimer = now + 0.12f; LogAutoBuy(" ShopPanel populated, proceeding to buy");
                                break;
                            }
                            else
                            {
                                // content not yet populated, retry a few times
                                LogAutoBuy(" Waiting for ShopPanel content to populate...");
                                this.autoBuyStepTimer = now + 0.25f;
                                break;
                            }
                        }
                        // ShopPanel not yet present. Retry the option click while dialogue is visible.
                        if (ClickDialogueOptionByKeywords(new string[] { "cooking store", "cook", "store" }))
                        {
                            this.autoBuyStoreSelectRetryCount++;
                            this.autoBuyStepTimer = now + 0.25f;
                            LogAutoBuy(" Retried Cooking Store option while waiting for shop");
                            break;
                        }

                        if (TryAdvanceDialogueText())
                        {
                            this.autoBuyStepTimer = now + 0.12f;
                            break;
                        }

                        if (this.autoBuyShopWaitStartedAt <= 0f)
                        {
                            this.autoBuyShopWaitStartedAt = now;
                        }

                        if ((now - this.autoBuyShopWaitStartedAt) > 2.5f)
                        {
                            LogAutoBuy(" Shop panel did not open yet, returning to Cooking Store selection");
                            this.autoBuySubState = 3;
                            this.autoBuyStepTimer = now + 0.1f;
                            this.autoBuyShopWaitStartedAt = 0f;
                            break;
                        }

                        this.autoBuyStepTimer = now + 0.25f;
                        break;
                    case 4: // buying items
                        if (now < this.autoBuyStepTimer) break;
                        if (this.autoBuyCurrentIngredientIndex >= this.autoBuyIngredientsMatch.Length)
                        {
                            this.autoBuySubState = 5;
                            this.autoBuyStepTimer = now + 3f;
                            LogAutoBuy(" Finished ingredient loop, waiting 3s before return");
                            break;
                        }
                        string match = this.autoBuyIngredientsMatch[this.autoBuyCurrentIngredientIndex];
                        // attempt to click the item up to max; we assume each click buys one
                        if (this.autoBuyPurchasedCount >= this.autoBuyMaxPerIngredient)
                        {
                            this.autoBuyPurchasedCount = 0; this.autoBuyCurrentIngredientIndex++; this.autoBuyShopScrollStep = -1; this.autoBuyStepTimer = now + 0.1f; break;
                        }
                        bool clicked = ClickCookingStoreItemByMatch(match);
                        if (clicked)
                        {
                            this.autoBuyShopScrollStep = -1;
                            // go to purchase dialog handling substate
                            this.autoBuySubState = 41; // purchase dialog
                            this.autoBuyStepTimer = now + 0.12f;
                            if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Opened purchase dialog for {match}"); }
                        }
                        else
                        {
                            // item not found / sold out; but if the shop panel exists and content is empty, retry a few times
                            GameObject shopProbe = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                            Transform contentProbe = shopProbe != null ? shopProbe.transform.Find("goods@scroll/Content") : null;
                            if (shopProbe != null && contentProbe != null && contentProbe.childCount == 0)
                            {
                                // shop present but not populated yet - retry this ingredient shortly
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Shop content empty for item {match}, retrying shortly"); }
                                this.autoBuyStepTimer = now + 0.25f;
                            }
                            else if (this.autoBuyShopScrollStep >= 0 && this.autoBuyShopScrollStep < 12)
                            {
                                this.autoBuyStepTimer = now + 0.15f;
                            }
                            else
                            {
                                // truly not found or sold out; move to next
                                this.autoBuyPurchasedCount = 0; this.autoBuyCurrentIngredientIndex++; this.autoBuyShopScrollStep = -1; this.autoBuyStepTimer = now + 0.2f; if (this.autoBuyLogsEnabled) { ModLogger.Msg("[AutoBuy] Item " + match + " not found or sold out, skipping"); }
                            }
                        }
                        break;
                    case 41: // handle purchase dialog: press +10 until target then Purchase
                        if (now < this.autoBuyStepTimer) break;
                        GameObject sale = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Top/SalePanel(Clone)");
                        if (sale == null || !sale.activeInHierarchy)
                        {
                            // Sale panel not present, abort this item
                            this.autoBuyPurchasedCount = 0; this.autoBuyCurrentIngredientIndex++; this.autoBuyShopScrollStep = -1; this.autoBuyStepTimer = now + 0.2f; this.autoBuySubState = 4; LogAutoBuy(" Sale panel not found, skipping"); break;
                        }
                        // read current count and remaining stock
                        int currentCount = GetSalePanelCurrentCount(sale);
                        int remainingStock = GetSalePanelRemainingStock(sale);
                        if (currentCount < 0) currentCount = 1; // default if cannot read
                        // Cap the target purchase amount to available stock
                        int effectiveMax = this.autoBuyMaxPerIngredient;
                        if (remainingStock > 0)
                        {
                            effectiveMax = Mathf.Min(this.autoBuyMaxPerIngredient, remainingStock);
                        }
                        if (currentCount >= effectiveMax)
                        {
                            // directly click purchase
                            if (ClickSalePurchase(sale))
                            {
                                this.autoBuyPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Purchased {currentCount} items (target: {effectiveMax})"); }
                            }
                            // after purchase, proceed to next ingredient
                            this.autoBuyPurchasedCount = 0; this.autoBuyCurrentIngredientIndex++; this.autoBuyShopScrollStep = -1; this.autoBuySubState = 4; this.autoBuyStepTimer = now + 0.25f;
                            break;
                        }
                        // need to increase: press +10 button
                        int needed = effectiveMax - currentCount;
                        int clicks = Mathf.CeilToInt((float)needed / 10f);
                        // limit clicks this tick to avoid long blocking
                        int doClicks = Mathf.Min(clicks, 3);
                        bool anyClicked = false;
                        for (int i = 0; i < doClicks; i++)
                        {
                            if (ClickSaleAddMore(sale)) { anyClicked = true; }
                        }
                        this.autoBuyStepTimer = now + 0.12f;
                        if (!anyClicked)
                        {
                            // couldn't click addMore (shop limit reached or button unavailable)
                            // purchase whatever quantity we have available (>0)
                            if (currentCount > 0 && ClickSalePurchase(sale))
                            {
                                this.autoBuyPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Purchased {currentCount} items (shop stock limited, target was {effectiveMax})"); }
                            }
                            else
                            {
                                LogAutoBuy(" Could not purchase item - no stock available");
                            }
                            this.autoBuyPurchasedCount = 0; this.autoBuyCurrentIngredientIndex++; this.autoBuyShopScrollStep = -1; this.autoBuySubState = 4; this.autoBuyStepTimer = now + 0.25f;
                        }
                        break;
                    case 5: // return
                        // Keep closing lingering panels (dialogue/shop/sale) during delay window.
                        this.CloseAutoBuyPanels();
                        if (now < this.autoBuyStepTimer) break;
                        this.StopAutoBuy("Done, returning");
                        break;
                }
            }
            catch (Exception ex) { LogAutoBuy(" Run error: " + ex.Message); this.StopAutoBuy("Error"); }
        }

        private void StartAutoBuyGarden()
        {
            try
            {
                GameObject p = GameObject.Find("p_player_skeleton(Clone)");
                if (p != null) this.autoBuyGardenSavedPosition = p.transform.position;
                this.autoBuyGardenSubState = 1;
                this.autoBuyGardenStepTimer = Time.unscaledTime + 0.1f;
                this.autoBuyGardenShopWaitStartedAt = 0f;
                this.autoBuyGardenStoreSelectRetryCount = 0;
                this.autoBuyGardenCurrentItemIndex = 0;
                this.autoBuyGardenPurchasedCount = 0;
                this.autoBuyGardenShopScrollStep = -1;
                this.autoBuyGardenPreviousGameSpeed = this.gameSpeed;
                this.SetGameSpeed(5f);
                this.autoBuyGardenForcedGameSpeed = true;
                this.TeleportToLocation(this.autoBuyGardenNearbyPos);
                LogAutoBuy("[Garden] Started: teleporting to nearby position (Game Speed x5.0)");
            }
            catch (Exception ex) { LogAutoBuy("[Garden] Start error: " + ex.Message); this.StopAutoBuyGarden("Start error"); }
        }

        private void StopAutoBuyGarden(string reason)
        {
            LogAutoBuy("[Garden] Stopped: " + reason);
            this.CloseAutoBuyPanels();
            this.autoBuyGardenEnabled = false;
            this.autoBuyGardenSubState = 0;
            this.autoBuyGardenShopScrollStep = -1;
            if (this.autoBuyGardenForcedGameSpeed)
            {
                this.SetGameSpeed(Mathf.Max(1f, this.autoBuyGardenPreviousGameSpeed));
                this.autoBuyGardenForcedGameSpeed = false;
            }
            if (this.autoBuyGardenSavedPosition != Vector3.zero)
            {
                this.TeleportToLocation(this.autoBuyGardenSavedPosition);
                this.autoBuyGardenSavedPosition = Vector3.zero;
            }
        }

        private void RunAutoBuyGardenLogic()
        {
            try
            {
                if (!this.autoBuyGardenEnabled)
                {
                    if (this.autoBuyGardenForcedGameSpeed)
                    {
                        this.SetGameSpeed(Mathf.Max(1f, this.autoBuyGardenPreviousGameSpeed));
                        this.autoBuyGardenForcedGameSpeed = false;
                    }
                    return;
                }
                float now = Time.unscaledTime;

                // Close any popup that appears
                if (now >= this.autoBuyPopupCloseRetryAt && this.TryCloseAutoBuyObtainedPopup())
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.12f;
                    this.autoBuyGardenStepTimer = now + 0.12f;
                    return;
                }
                if (now >= this.autoBuyPopupCloseRetryAt)
                {
                    this.autoBuyPopupCloseRetryAt = now + 0.2f;
                }

                switch (this.autoBuyGardenSubState)
                {
                    case 1: // teleporting to nearby position
                        if (this.teleportFramesRemaining <= 0)
                        {
                            this.autoBuyGardenSubState = 12;
                            this.autoBuyGardenStepTimer = now + 3f;
                            LogAutoBuy("[Garden] Arrived at nearby position, waiting before approaching NPC");
                        }
                        break;
                    case 12: // waiting at nearby pos, then teleport to NPC front
                        if (now < this.autoBuyGardenStepTimer) break;
                        this.TeleportToLocation(this.autoBuyGardenTargetPos);
                        this.autoBuyGardenSubState = 2;
                        this.autoBuyGardenStepTimer = now + 0.8f;
                        LogAutoBuy("[Garden] Teleporting to NPC front position");
                        break;
                    case 2: // waiting for dialogue - click chat icon until dialogue shows
                        if (now < this.autoBuyGardenStepTimer) break;
                        if (TryClickNpcChatIcon()) { this.autoBuyGardenStepTimer = now + 0.5f; }
                        else { this.autoBuyGardenStepTimer = now + 0.12f; }
                        GameObject dlg = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                        if (dlg != null && dlg.activeInHierarchy)
                        {
                            this.autoBuyGardenSubState = 3; this.autoBuyGardenStepTimer = now + 0.2f; LogAutoBuy("[Garden] Dialogue opened");
                        }
                        break;
                    case 3: // select gardening store
                        if (now < this.autoBuyGardenStepTimer) break;
                        if (!HasDialogueOptionsVisible())
                        {
                            if (TryAdvanceDialogueText())
                            {
                                LogAutoBuy("[Garden] Advanced dialogue text, waiting for options");
                            }
                            this.autoBuyGardenStepTimer = now + 0.12f;
                            break;
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "gardening store", "garden", "store" }))
                        {
                            this.autoBuyGardenSubState = 31;
                            this.autoBuyGardenStepTimer = now + 0.25f;
                            this.autoBuyGardenShopWaitStartedAt = now;
                            this.autoBuyGardenStoreSelectRetryCount++;
                            LogAutoBuy("[Garden] Selected Gardening Store, waiting for shop content");
                        }
                        else { this.autoBuyGardenStepTimer = now + 0.15f; }
                        break;
                    case 31: // wait for ShopPanel to appear and be populated
                        if (now < this.autoBuyGardenStepTimer) break;
                        GameObject shopChk = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                        if (shopChk != null && shopChk.activeInHierarchy)
                        {
                            Transform contentChk = shopChk.transform.Find("goods@scroll/Content");
                            if (contentChk != null && contentChk.childCount > 0)
                            {
                                this.autoBuyGardenSubState = 4; this.autoBuyGardenStepTimer = now + 0.12f; LogAutoBuy("[Garden] ShopPanel populated, proceeding to buy");
                                break;
                            }
                            else
                            {
                                LogAutoBuy("[Garden] Waiting for ShopPanel content to populate...");
                                this.autoBuyGardenStepTimer = now + 0.25f;
                                break;
                            }
                        }
                        if (ClickDialogueOptionByKeywords(new string[] { "gardening store", "garden", "store" }))
                        {
                            this.autoBuyGardenStoreSelectRetryCount++;
                            this.autoBuyGardenStepTimer = now + 0.25f;
                            LogAutoBuy("[Garden] Retried Gardening Store option while waiting for shop");
                            break;
                        }
                        if (TryAdvanceDialogueText())
                        {
                            this.autoBuyGardenStepTimer = now + 0.12f;
                            break;
                        }
                        if (this.autoBuyGardenShopWaitStartedAt <= 0f) this.autoBuyGardenShopWaitStartedAt = now;
                        if ((now - this.autoBuyGardenShopWaitStartedAt) > 2.5f)
                        {
                            LogAutoBuy("[Garden] Shop panel did not open yet, returning to store selection");
                            this.autoBuyGardenSubState = 3;
                            this.autoBuyGardenStepTimer = now + 0.1f;
                            this.autoBuyGardenShopWaitStartedAt = 0f;
                            break;
                        }
                        this.autoBuyGardenStepTimer = now + 0.25f;
                        break;
                    case 4: // buying items
                        if (now < this.autoBuyGardenStepTimer) break;
                        if (this.autoBuyGardenCurrentItemIndex >= this.autoBuyGardenItemsMatch.Length)
                        {
                            this.autoBuyGardenSubState = 5;
                            this.autoBuyGardenStepTimer = now + 3f;
                            LogAutoBuy("[Garden] Finished item loop, waiting before return");
                            break;
                        }
                        string match = this.autoBuyGardenItemsMatch[this.autoBuyGardenCurrentItemIndex];
                        if (this.autoBuyGardenPurchasedCount >= this.autoBuyGardenMaxPerItem)
                        {
                            this.autoBuyGardenPurchasedCount = 0; this.autoBuyGardenCurrentItemIndex++; this.autoBuyGardenShopScrollStep = -1; this.autoBuyGardenStepTimer = now + 0.1f; break;
                        }
                        bool clicked = ClickGardenStoreItemByMatch(match);
                        if (clicked)
                        {
                            this.autoBuyGardenShopScrollStep = -1;
                            this.autoBuyGardenSubState = 41;
                            this.autoBuyGardenStepTimer = now + 0.12f;
                            if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Garden] Opened purchase dialog for {match}"); }
                        }
                        else
                        {
                            GameObject shopProbe = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                            Transform contentProbe = shopProbe != null ? shopProbe.transform.Find("goods@scroll/Content") : null;
                            if (shopProbe != null && contentProbe != null && contentProbe.childCount == 0)
                            {
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Garden] Shop content empty for item {match}, retrying shortly"); }
                                this.autoBuyGardenStepTimer = now + 0.25f;
                            }
                            else if (this.autoBuyGardenShopScrollStep >= 0 && this.autoBuyGardenShopScrollStep < 12)
                            {
                                this.autoBuyGardenStepTimer = now + 0.15f;
                            }
                            else
                            {
                                this.autoBuyGardenPurchasedCount = 0; this.autoBuyGardenCurrentItemIndex++; this.autoBuyGardenShopScrollStep = -1; this.autoBuyGardenStepTimer = now + 0.2f;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg("[Garden] Item " + match + " not found or sold out, skipping"); }
                            }
                        }
                        break;
                    case 41: // handle purchase dialog: press +10 until target then Purchase
                        if (now < this.autoBuyGardenStepTimer) break;
                        GameObject sale = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Top/SalePanel(Clone)");
                        if (sale == null || !sale.activeInHierarchy)
                        {
                            this.autoBuyGardenPurchasedCount = 0; this.autoBuyGardenCurrentItemIndex++; this.autoBuyGardenShopScrollStep = -1; this.autoBuyGardenStepTimer = now + 0.2f; this.autoBuyGardenSubState = 4;
                            LogAutoBuy("[Garden] Sale panel not found, skipping"); break;
                        }
                        int currentCount = GetSalePanelCurrentCount(sale);
                        int remainingStock = GetSalePanelRemainingStock(sale);
                        if (currentCount < 0) currentCount = 1;
                        int effectiveMax = this.autoBuyGardenMaxPerItem;
                        if (remainingStock > 0) effectiveMax = Mathf.Min(this.autoBuyGardenMaxPerItem, remainingStock);
                        if (currentCount >= effectiveMax)
                        {
                            if (ClickSalePurchase(sale))
                            {
                                this.autoBuyGardenPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Garden] Purchased {currentCount} items (target: {effectiveMax})"); }
                            }
                            this.autoBuyGardenPurchasedCount = 0; this.autoBuyGardenCurrentItemIndex++; this.autoBuyGardenShopScrollStep = -1; this.autoBuyGardenSubState = 4; this.autoBuyGardenStepTimer = now + 0.25f;
                            break;
                        }
                        int needed = effectiveMax - currentCount;
                        int clicks = Mathf.CeilToInt((float)needed / 10f);
                        int doClicks = Mathf.Min(clicks, 3);
                        bool anyClicked = false;
                        for (int i = 0; i < doClicks; i++)
                        {
                            if (ClickSaleAddMore(sale)) { anyClicked = true; }
                        }
                        this.autoBuyGardenStepTimer = now + 0.12f;
                        if (!anyClicked)
                        {
                            if (currentCount > 0 && ClickSalePurchase(sale))
                            {
                                this.autoBuyGardenPurchasedCount = currentCount;
                                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[Garden] Purchased {currentCount} items (shop stock limited, target was {effectiveMax})"); }
                            }
                            else
                            {
                                LogAutoBuy("[Garden] Could not purchase item - no stock available");
                            }
                            this.autoBuyGardenPurchasedCount = 0; this.autoBuyGardenCurrentItemIndex++; this.autoBuyGardenShopScrollStep = -1; this.autoBuyGardenSubState = 4; this.autoBuyGardenStepTimer = now + 0.25f;
                        }
                        break;
                    case 5: // return
                        this.CloseAutoBuyPanels();
                        if (now < this.autoBuyGardenStepTimer) break;
                        this.StopAutoBuyGarden("Done, returning");
                        break;
                }
            }
            catch (Exception ex) { LogAutoBuy("[Garden] Run error: " + ex.Message); this.StopAutoBuyGarden("Error"); }
        }

    }
}
