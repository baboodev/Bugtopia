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

        // All store ids from the static TableData.TableStoreInfos table — the same enumeration
        // TryResolveStoreIdByKeywordsMono walks, without the keyword scoring. The Quest Assistant uses
        // this to search every store's goods for a quest-required item staticId (PurchaseItem steps).
        // Raw pointers are read within this single call (no yields), matching the resolver's pattern.
        private bool TryGetAllStoreIdsMono(List<int> storeIds)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
                {
                    return false;
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableStoreInfos", out IntPtr tableStoreInfosObj) || tableStoreInfosObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(tableStoreInfosObj, items) || items.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (this.TryReadStoreTableEntryMono(tableDataClass, items[i], out int candidateId, out _, out _, out _, out _)
                        && candidateId > 0
                        && !storeIds.Contains(candidateId))
                    {
                        storeIds.Add(candidateId);
                    }
                }

                return storeIds.Count > 0;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Store id enumeration failed: " + ex.Message);
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

        // Survivor of the 2026-08-07 auto-buy removal: despite the name this is the shared
        // shop/dialog logger, still used by the LIVE dialog-option clicker (Interaction.cs), the
        // cooking-store item clicker (NetCook.cs), the currency check (PeriodCurrency.cs) and the
        // NPC-approach helpers (Teleport.cs). Its gate, autoBuyLogsEnabled => MasterLogAutoBuy, is
        // likewise shared. Only the four auto-buy state machines that also used it are gone.
        private void LogAutoBuy(string message)
        {
            if (this.autoBuyLogsEnabled)
            {
                ModLogger.Msg("[AutoBuy] " + message);
            }
        }


    }
}
