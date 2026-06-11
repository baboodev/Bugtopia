using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int ShopDumpClothingStoreId = 5;

        private string shopDumpStatus = "Idle.";
        private IntPtr shopDumpAuraGetGroupGoodsDataMethod = IntPtr.Zero;

        private void StartShopResearchDump()
        {
            try
            {
                this.shopDumpStatus = "Dumping to log...";
                int lineCount = this.DumpShopResearchToLog();
                this.shopDumpStatus = "Dumped " + lineCount + " lines — see [ShopDump] in log.";
                this.AddMenuNotification(this.shopDumpStatus, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.shopDumpStatus = "Dump failed: " + ex.Message;
                this.ShopDumpLog(this.shopDumpStatus, always: true);
                this.AddMenuNotification(this.shopDumpStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

        private void ShopDumpLog(string message, bool always = true)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[ShopDump] " + message);
            }
            catch
            {
            }
        }

        private int DumpShopResearchToLog()
        {
            int lines = 0;
            lines += this.ShopDumpLogLine("========== shop research dump begin (ALL TableStoreInfos) ==========");
            lines += this.ShopDumpLogLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            List<ShopDumpStoreInfoRow> allStores = new List<ShopDumpStoreInfoRow>();
            if (!this.TryCollectTableStoreInfos(allStores))
            {
                lines += this.ShopDumpLogLine("ERROR: TableStoreInfos unavailable (enter town, wait for tables).");
                lines += this.ShopDumpLogLine("========== shop research dump end ==========");
                return lines;
            }

            Dictionary<int, ShopDumpStoreInfoRow> storeInfoById = new Dictionary<int, ShopDumpStoreInfoRow>();
            lines += this.ShopDumpLogLine("TableStoreInfos count=" + allStores.Count);
            for (int i = 0; i < allStores.Count; i++)
            {
                ShopDumpStoreInfoRow row = allStores[i];
                if (row.StoreId <= 0)
                {
                    continue;
                }

                storeInfoById[row.StoreId] = row;
                lines += this.ShopDumpLogLine(
                    "storeInfo id=" + row.StoreId
                    + " name='" + row.DisplayName + "'"
                    + " npc='" + row.NpcPicture + "'"
                    + " bg='" + row.BgPicture + "'"
                    + " decor='" + row.DecorationPicture + "'");
            }

            List<ShopDumpSlotRow> allSlots = new List<ShopDumpSlotRow>();
            if (!this.TryCollectTableStoreSlots(allSlots))
            {
                lines += this.ShopDumpLogLine("WARN: TableStoreSlots unavailable.");
            }

            List<ShopDumpSelectorRow> allSelectors = new List<ShopDumpSelectorRow>();
            if (!this.TryCollectTableStoreSlotSelectors(allSelectors))
            {
                lines += this.ShopDumpLogLine("WARN: TableStoreSlotSelectors unavailable.");
            }

            List<ShopDumpGroupRow> canBuyGroups = new List<ShopDumpGroupRow>();
            List<ShopDumpGroupRow> tableGroups = new List<ShopDumpGroupRow>();
            this.TryCollectStoreGroupWithCanBuy(canBuyGroups);
            this.TryCollectTableStoreGroups(tableGroups);
            List<ShopDumpGroupRow> mergedGroups = this.MergeShopDumpCatalogGroups(canBuyGroups, tableGroups);
            lines += this.ShopDumpLogLine(
                "global catalog: StoreGroupWithCanBuy=" + canBuyGroups.Count
                + " TableStoreGroups=" + tableGroups.Count
                + " merged=" + mergedGroups.Count);

            if (mergedGroups.Count == 0)
            {
                lines += this.ShopDumpLogLine("WARN: no static catalog rows (enter town, wait for tables).");
            }

            lines += this.ShopDumpLogLine("methods per store: static/StoreGroupWithCanBuy, static/TableStoreGroups, static/merged,"
                + " runtime/GetStoreGoodsData(managed), runtime/GetStoreGoodsData(aura),"
                + " runtime/GetGroupGoodsData(managed,per-slot), runtime/GetGroupGoodsData(aura,per-slot)");

            for (int i = 0; i < allStores.Count; i++)
            {
                int storeId = allStores[i].StoreId;
                if (storeId <= 0)
                {
                    continue;
                }

                storeInfoById.TryGetValue(storeId, out ShopDumpStoreInfoRow info);
                lines += this.DumpAllMethodsForStore(
                    storeId,
                    info,
                    allSlots,
                    allSelectors,
                    canBuyGroups,
                    tableGroups,
                    mergedGroups);
            }

            lines += this.ShopDumpLogLine("========== shop research dump end ==========");
            return lines;
        }

        private int DumpAllMethodsForStore(
            int storeId,
            ShopDumpStoreInfoRow info,
            List<ShopDumpSlotRow> allSlots,
            List<ShopDumpSelectorRow> allSelectors,
            List<ShopDumpGroupRow> canBuyGroups,
            List<ShopDumpGroupRow> tableGroups,
            List<ShopDumpGroupRow> mergedGroups)
        {
            int lines = 0;
            string storeLabel = info.DisplayName ?? string.Empty;
            lines += this.ShopDumpLogLine(
                "===== storeId=" + storeId
                + " name='" + storeLabel + "'"
                + (this.ShopDumpIsClothingStore(storeId, info.CombinedText) ? " [clothing]" : string.Empty)
                + " =====");

            lines += this.DumpStoreSlotLayout(storeId, allSlots, allSelectors);
            lines += this.DumpRuntimeStoreSlotSelectors(storeId, allSelectors);

            int staticCanBuyItems = this.DumpStaticPurchaseCatalog(storeId, allSlots, canBuyGroups, "static/StoreGroupWithCanBuy", info.CombinedText);
            int staticTableItems = this.DumpStaticPurchaseCatalog(storeId, allSlots, tableGroups, "static/TableStoreGroups", info.CombinedText);
            int staticMergedItems = this.DumpStaticPurchaseCatalog(storeId, allSlots, mergedGroups, "static/merged", info.CombinedText);
            int runtimeStoreManagedItems = this.DumpRuntimeGetStoreGoodsData(storeId, "runtime/GetStoreGoodsData", "managed", info.CombinedText);
            int runtimeStoreAuraItems = this.DumpRuntimeGetStoreGoodsData(storeId, "runtime/GetStoreGoodsData", "aura", info.CombinedText);
            int runtimeGroupManagedItems = this.DumpRuntimeGetGroupGoodsDataBySlots(storeId, allSlots, "runtime/GetGroupGoodsData", "managed", info.CombinedText);
            int runtimeGroupAuraItems = this.DumpRuntimeGetGroupGoodsDataBySlots(storeId, allSlots, "runtime/GetGroupGoodsData", "aura", info.CombinedText);

            lines += this.ShopDumpLogLine(
                "store summary id=" + storeId
                + " staticCanBuy=" + staticCanBuyItems
                + " staticTable=" + staticTableItems
                + " staticMerged=" + staticMergedItems
                + " runtimeStoreManaged=" + runtimeStoreManagedItems
                + " runtimeStoreAura=" + runtimeStoreAuraItems
                + " runtimeGroupManaged=" + runtimeGroupManagedItems
                + " runtimeGroupAura=" + runtimeGroupAuraItems);
            return lines;
        }

        private bool ShopDumpIsClothingStore(int storeId, string combinedText)
        {
            if (storeId == ShopDumpClothingStoreId)
            {
                return true;
            }

            if (string.IsNullOrEmpty(combinedText))
            {
                return false;
            }

            return combinedText.IndexOf("dress", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedText.IndexOf("clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedText.IndexOf("clothes", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int DumpStoreSlotLayout(int storeId, List<ShopDumpSlotRow> allSlots, List<ShopDumpSelectorRow> allSelectors)
        {
            int lines = 0;
            int slotCount = 0;
            HashSet<int> poolGroupIds = new HashSet<int>();
            for (int i = 0; i < allSlots.Count; i++)
            {
                ShopDumpSlotRow slot = allSlots[i];
                if (slot.StoreId != storeId)
                {
                    continue;
                }

                slotCount++;
                if (slot.GroupId > 0)
                {
                    poolGroupIds.Add(slot.GroupId);
                }

                lines += this.ShopDumpLogLine(
                    "slot tableId=" + slot.TableId
                    + " slotId=" + slot.SlotId
                    + " poolGroupId=" + slot.GroupId
                    + " refresh=" + slot.Refresh
                    + " (" + ShopDumpDescribeRefresh(slot.Refresh) + ")"
                    + " tab='" + slot.TabName + "'"
                    + " fn='" + slot.Function + "'");
            }

            for (int i = 0; i < allSelectors.Count; i++)
            {
                ShopDumpSelectorRow selector = allSelectors[i];
                for (int j = 0; j < selector.Options.Count; j++)
                {
                    ShopDumpSelectorOptionRow opt = selector.Options[j];
                    if (opt.StoreId != storeId)
                    {
                        continue;
                    }

                    if (opt.GroupId > 0)
                    {
                        poolGroupIds.Add(opt.GroupId);
                    }

                    lines += this.ShopDumpLogLine(
                        "selector id=" + selector.SelectorId
                        + " name='" + selector.Name + "'"
                        + " option=" + opt.OptionId
                        + " -> store=" + opt.StoreId
                        + " slot=" + opt.SlotId
                        + " poolGroupId=" + opt.GroupId);
                }
            }

            lines += this.ShopDumpLogLine("layout slots=" + slotCount + " poolGroupIds=" + string.Join(",", poolGroupIds));
            return lines;
        }

        private int DumpStaticPurchaseCatalog(
            int storeId,
            List<ShopDumpSlotRow> allSlots,
            List<ShopDumpGroupRow> allGroups,
            string methodTag,
            string storeCombinedText)
        {
            bool clothingStore = this.ShopDumpIsClothingStore(storeId, storeCombinedText);
            this.ShopDumpLogLine("--- METHOD " + methodTag + " ---");
            Dictionary<int, List<int>> poolToSlots = this.BuildStorePoolToSlots(storeId, allSlots);
            if (poolToSlots.Count == 0)
            {
                this.ShopDumpLogLine(methodTag + ": no slots/pools for storeId=" + storeId);
                return 0;
            }

            if (allGroups.Count == 0)
            {
                this.ShopDumpLogLine(methodTag + ": source table empty");
                return 0;
            }

            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            int catalogCount = 0;
            for (int i = 0; i < allGroups.Count; i++)
            {
                ShopDumpGroupRow group = allGroups[i];
                if (!this.TryGetCatalogSlotsForGroup(group, poolToSlots, out List<int> slotIds))
                {
                    continue;
                }

                for (int s = 0; s < slotIds.Count; s++)
                {
                    int slotId = slotIds[s];
                    string key = storeId + ":" + slotId + ":" + group.ItemId;
                    if (!emitted.Add(key))
                    {
                        continue;
                    }

                    catalogCount++;
                    this.ShopDumpLogLine(this.FormatStaticPurchaseLine(methodTag, storeId, slotId, group, clothingStore));
                }
            }

            this.ShopDumpLogLine(methodTag + " entries=" + catalogCount + " storeId=" + storeId);
            return catalogCount;
        }

        private Dictionary<int, List<int>> BuildStorePoolToSlots(int storeId, List<ShopDumpSlotRow> allSlots)
        {
            Dictionary<int, List<int>> poolToSlots = new Dictionary<int, List<int>>();
            for (int i = 0; i < allSlots.Count; i++)
            {
                ShopDumpSlotRow slot = allSlots[i];
                if (slot.StoreId != storeId || slot.GroupId <= 0 || slot.SlotId <= 0)
                {
                    continue;
                }

                if (!poolToSlots.TryGetValue(slot.GroupId, out List<int> slotIds))
                {
                    slotIds = new List<int>();
                    poolToSlots[slot.GroupId] = slotIds;
                }

                if (!slotIds.Contains(slot.SlotId))
                {
                    slotIds.Add(slot.SlotId);
                }
            }

            return poolToSlots;
        }

        private bool TryGetCatalogSlotsForGroup(in ShopDumpGroupRow group, Dictionary<int, List<int>> poolToSlots, out List<int> slotIds)
        {
            slotIds = null;
            if (group.PoolGroupId > 0 && poolToSlots.TryGetValue(group.PoolGroupId, out slotIds))
            {
                return slotIds.Count > 0;
            }

            if (group.ModelGroupId > 0 && poolToSlots.TryGetValue(group.ModelGroupId, out slotIds))
            {
                return slotIds.Count > 0;
            }

            return false;
        }

        private string FormatStaticPurchaseLine(string methodTag, int storeId, int slotId, in ShopDumpGroupRow group, bool clothingStore)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(methodTag).Append(' ');
            if (clothingStore)
            {
                sb.Append("buy=BuyClothes");
                sb.Append(" storeId=").Append(storeId);
                sb.Append(" slotId=").Append(slotId);
                sb.Append(" itemId=").Append(group.ItemId);
                sb.Append(" color1=0 color2=0");
            }
            else
            {
                sb.Append("buy=BuyItem");
                sb.Append(" storeId=").Append(storeId);
                sb.Append(" slotId=").Append(slotId);
                sb.Append(" itemId=").Append(group.ItemId);
                sb.Append(" count=").Append(group.BuyCount > 0 ? group.BuyCount : 1);
                sb.Append(" currencyEnum=").Append(group.MoneyValue);
            }

            sb.Append(" price=").Append(group.Price);
            sb.Append(" moneyType=").Append(group.MoneyType);
            sb.Append(" moneyValue=").Append(group.MoneyValue);
            sb.Append(" poolGroupId=").Append(group.PoolGroupId);
            if (group.ModelGroupId > 0)
            {
                sb.Append(" modelGroupId=").Append(group.ModelGroupId);
            }

            if (group.SuitId > 0)
            {
                sb.Append(" suitId=").Append(group.SuitId);
            }

            sb.Append(" labelType=").Append(group.LabelType);
            sb.Append(" available=").Append(group.Available);
            if (group.PrimaryRewardType > 0)
            {
                sb.Append(" rewardType=").Append(group.PrimaryRewardType);
                sb.Append(" rewardParam=").Append(group.PrimaryRewardParam);
            }

            if (!string.IsNullOrEmpty(group.RewardsSummary))
            {
                sb.Append(" rewards='").Append(group.RewardsSummary).Append('\'');
            }

            return sb.ToString();
        }

        private List<ShopDumpGroupRow> MergeShopDumpCatalogGroups(List<ShopDumpGroupRow> canBuyGroups, List<ShopDumpGroupRow> tableGroups)
        {
            Dictionary<int, ShopDumpGroupRow> merged = new Dictionary<int, ShopDumpGroupRow>();
            for (int i = 0; i < tableGroups.Count; i++)
            {
                ShopDumpGroupRow row = tableGroups[i];
                if (row.ItemId > 0)
                {
                    merged[row.ItemId] = row;
                }
            }

            for (int i = 0; i < canBuyGroups.Count; i++)
            {
                ShopDumpGroupRow row = canBuyGroups[i];
                if (row.ItemId > 0)
                {
                    merged[row.ItemId] = row;
                }
            }

            List<ShopDumpGroupRow> result = new List<ShopDumpGroupRow>(merged.Values);
            result.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
            return result;
        }

        private bool TryCollectTableStoreGroups(List<ShopDumpGroupRow> rows)
        {
            rows.Clear();
            if (this.TryCollectTableStoreGroupsManaged(rows))
            {
                return rows.Count > 0;
            }

            return this.TryCollectTableStoreGroupsAura(rows);
        }

        private bool TryCollectTableStoreGroupsManaged(List<ShopDumpGroupRow> rows)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                FieldInfo field = tableDataType.GetField("TableStoreGroups", BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is IDictionary dictionary))
                {
                    return false;
                }

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null || !this.TryReadShopDumpGroupManaged(entry.Value, out ShopDumpGroupRow row))
                    {
                        continue;
                    }

                    rows.Add(row);
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCollectTableStoreGroupsAura(List<ShopDumpGroupRow> rows)
        {
            return this.TryCollectStoreGroupsAuraField("TableStoreGroups", rows);
        }

        private bool TryCollectStoreGroupsAuraField(string fieldName, List<ShopDumpGroupRow> rows)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, fieldName, out IntPtr groupsObj) || groupsObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(groupsObj, items))
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr valueObj = this.TryGetMonoDictionaryEntryValue(items[i]);
                    if (valueObj == IntPtr.Zero)
                    {
                        valueObj = items[i];
                    }

                    if (!this.TryReadShopDumpGroupAura(valueObj, out ShopDumpGroupRow row))
                    {
                        continue;
                    }

                    rows.Add(row);
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private int DumpRuntimeStoreSlotSelectors(int storeId, List<ShopDumpSelectorRow> selectors)
        {
            int lines = 0;
            HashSet<int> selectorIds = new HashSet<int>();
            for (int i = 0; i < selectors.Count; i++)
            {
                for (int j = 0; j < selectors[i].Options.Count; j++)
                {
                    if (selectors[i].Options[j].StoreId == storeId)
                    {
                        selectorIds.Add(selectors[i].SelectorId);
                    }
                }
            }

            if (selectorIds.Count == 0)
            {
                lines += this.ShopDumpLogLine("runtime selectors: none linked in tables");
                return lines;
            }

            foreach (int selectorId in selectorIds)
            {
                if (this.TryGetRuntimeStoreSlotSelectorOption(selectorId, out int option, out string path))
                {
                    lines += this.ShopDumpLogLine("runtime selector id=" + selectorId + " activeOption=" + option + " via=" + path);
                }
                else
                {
                    lines += this.ShopDumpLogLine("runtime selector id=" + selectorId + " activeOption=unavailable");
                }
            }

            return lines;
        }

        private int DumpRuntimeGetStoreGoodsData(int storeId, string methodTag, string path, string storeCombinedText)
        {
            string fullTag = methodTag + "/" + path;
            this.ShopDumpLogLine("--- METHOD " + fullTag + " ---");
            List<ShopBuyAllCandidate> items = new List<ShopBuyAllCandidate>();
            string error = null;
            bool ok = string.Equals(path, "aura", StringComparison.Ordinal)
                ? this.TryCollectShopDumpRuntimeItemsAura(storeId, items, out error)
                : this.TryCollectShopDumpRuntimeItemsManaged(storeId, items, out error);

            if (!ok || items.Count == 0)
            {
                this.ShopDumpLogLine(fullTag + ": empty or unreadable (" + (error ?? "no error") + ")");
                return 0;
            }

            bool clothingStore = this.ShopDumpIsClothingStore(storeId, storeCombinedText);
            for (int i = 0; i < items.Count; i++)
            {
                this.ShopDumpLogLine(this.FormatShopDumpRuntimeItem(fullTag, items[i], clothingStore));
            }

            this.ShopDumpLogLine(fullTag + " entries=" + items.Count + " storeId=" + storeId);
            return items.Count;
        }

        private int DumpRuntimeGetGroupGoodsDataBySlots(
            int storeId,
            List<ShopDumpSlotRow> allSlots,
            string methodTag,
            string path,
            string storeCombinedText)
        {
            string fullTag = methodTag + "/" + path;
            this.ShopDumpLogLine("--- METHOD " + fullTag + " ---");
            List<int> slotIds = this.CollectStoreSlotIds(storeId, allSlots);
            if (slotIds.Count == 0)
            {
                this.ShopDumpLogLine(fullTag + ": no slots for storeId=" + storeId);
                return 0;
            }

            bool clothingStore = this.ShopDumpIsClothingStore(storeId, storeCombinedText);
            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            string lastError = null;
            for (int i = 0; i < slotIds.Count; i++)
            {
                int slotId = slotIds[i];
                List<ShopBuyAllCandidate> items = new List<ShopBuyAllCandidate>();
                bool ok = string.Equals(path, "aura", StringComparison.Ordinal)
                    ? this.TryCollectShopDumpRuntimeItemsBySlotAura(slotId, items, out lastError)
                    : this.TryCollectShopDumpRuntimeItemsBySlotManaged(slotId, items, out lastError);
                if (!ok || items.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < items.Count; j++)
                {
                    ShopBuyAllCandidate item = items[j];
                    if (item.StoreId > 0 && item.StoreId != storeId)
                    {
                        continue;
                    }

                    string key = item.StoreId + ":" + item.SlotId + ":" + item.ItemId + ":" + item.NetId;
                    if (!emitted.Add(key))
                    {
                        continue;
                    }

                    total++;
                    this.ShopDumpLogLine(this.FormatShopDumpRuntimeItem(fullTag + " slot=" + slotId, item, clothingStore));
                }
            }

            if (total == 0)
            {
                this.ShopDumpLogLine(fullTag + ": empty or unreadable (" + (lastError ?? "no error") + ")");
                return 0;
            }

            this.ShopDumpLogLine(fullTag + " entries=" + total + " storeId=" + storeId + " slotsScanned=" + slotIds.Count);
            return total;
        }

        private List<int> CollectStoreSlotIds(int storeId, List<ShopDumpSlotRow> allSlots)
        {
            List<int> slotIds = new List<int>();
            for (int i = 0; i < allSlots.Count; i++)
            {
                if (allSlots[i].StoreId != storeId || allSlots[i].SlotId <= 0)
                {
                    continue;
                }

                if (!slotIds.Contains(allSlots[i].SlotId))
                {
                    slotIds.Add(allSlots[i].SlotId);
                }
            }

            slotIds.Sort();
            return slotIds;
        }

        private bool TryCollectShopDumpRuntimeItemsBySlotManaged(int slotId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            items.Clear();
            try
            {
                Type shopType = this.FindLoadedType("XDTGameSystem.GameplaySystem.Shop.ShopSystem", "ShopSystem");
                if (shopType == null)
                {
                    error = "managed ShopSystem type missing";
                    return false;
                }

                object shopObj = null;
                PropertyInfo instanceProperty = this.GetDataModuleInstanceProperty(shopType);
                if (instanceProperty != null)
                {
                    shopObj = instanceProperty.GetValue(null, null);
                }

                if (shopObj == null && !this.TryGetManagedModule(shopType, out shopObj))
                {
                    error = "managed ShopSystem instance missing";
                    return false;
                }

                MethodInfo getGroupGoods = shopType.GetMethod(
                    "GetGroupGoodsData",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);
                if (getGroupGoods == null)
                {
                    error = "managed GetGroupGoodsData missing";
                    return false;
                }

                if (!(getGroupGoods.Invoke(shopObj, new object[] { slotId }) is IEnumerable enumerable))
                {
                    error = "managed GetGroupGoodsData returned non-enumerable";
                    return false;
                }

                foreach (object entry in enumerable)
                {
                    if (entry != null && this.TryReadManagedShopItemData(entry, out ShopBuyAllCandidate candidate))
                    {
                        items.Add(candidate);
                    }
                }

                return items.Count > 0;
            }
            catch (Exception ex)
            {
                error = "managed GetGroupGoodsData failed: " + ex.Message;
                return false;
            }
        }

        private bool TryCollectShopDumpRuntimeItemsBySlotAura(int slotId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            items.Clear();
            if (!this.TryInvokeAuraGetGroupGoodsData(slotId, out IntPtr listObj, out error))
            {
                return false;
            }

            List<IntPtr> elements = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, elements) || elements.Count == 0)
            {
                error = "Aura GetGroupGoodsData list empty.";
                return false;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                if (this.TryReadAuraShopItemData(elements[i], out ShopBuyAllCandidate candidate))
                {
                    items.Add(candidate);
                }
            }

            return items.Count > 0;
        }

        private unsafe bool TryInvokeAuraGetGroupGoodsData(int slotId, out IntPtr listObj, out string error)
        {
            listObj = IntPtr.Zero;
            error = null;
            if (!this.TryEnsureShopDumpAuraGetGroupGoodsData(out error) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int slotValue = slotId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&slotValue);
            IntPtr exc = IntPtr.Zero;
            listObj = auraMonoRuntimeInvoke(this.shopDumpAuraGetGroupGoodsDataMethod, this.shopBuyAllShopSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                error = "Aura GetGroupGoodsData invoke failed.";
                return false;
            }

            return true;
        }

        private bool TryEnsureShopDumpAuraGetGroupGoodsData(out string error)
        {
            error = null;
            if (this.shopDumpAuraGetGroupGoodsDataMethod != IntPtr.Zero && this.TryEnsureShopBuyAllAuraShopSystem(out error))
            {
                return true;
            }

            if (!this.TryEnsureShopBuyAllAuraShopSystem(out error))
            {
                return false;
            }

            IntPtr shopClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(this.shopBuyAllShopSystemObj) : IntPtr.Zero;
            IntPtr getGroupGoods = shopClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(shopClass, "GetGroupGoodsData", 1)
                : IntPtr.Zero;
            if (getGroupGoods == IntPtr.Zero)
            {
                error = "Aura GetGroupGoodsData missing.";
                return false;
            }

            this.shopDumpAuraGetGroupGoodsDataMethod = getGroupGoods;
            return true;
        }

        private bool TryCollectShopDumpRuntimeItemsManaged(int storeId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            try
            {
                Type shopType = this.FindLoadedType("XDTGameSystem.GameplaySystem.Shop.ShopSystem", "ShopSystem");
                if (shopType == null)
                {
                    error = "managed ShopSystem type missing";
                    return false;
                }

                object shopObj = null;
                PropertyInfo instanceProperty = this.GetDataModuleInstanceProperty(shopType);
                if (instanceProperty != null)
                {
                    shopObj = instanceProperty.GetValue(null, null);
                }

                if (shopObj == null && !this.TryGetManagedModule(shopType, out shopObj))
                {
                    error = "managed ShopSystem instance missing";
                    return false;
                }

                MethodInfo getStoreGoods = shopType.GetMethod("GetStoreGoodsData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (getStoreGoods == null)
                {
                    error = "managed GetStoreGoodsData missing";
                    return false;
                }

                if (!(getStoreGoods.Invoke(shopObj, new object[] { storeId }) is IEnumerable enumerable))
                {
                    error = "managed GetStoreGoodsData returned non-enumerable";
                    return false;
                }

                int added = 0;
                foreach (object entry in enumerable)
                {
                    if (entry == null || !this.TryReadManagedShopItemData(entry, out ShopBuyAllCandidate candidate))
                    {
                        continue;
                    }

                    if (candidate.StoreId > 0 && candidate.StoreId != storeId)
                    {
                        continue;
                    }

                    items.Add(candidate);
                    added++;
                }

                if (added <= 0)
                {
                    error = "managed runtime list empty";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "managed runtime collect failed: " + ex.Message;
                return false;
            }
        }

        private bool TryCollectShopDumpRuntimeItemsAura(int storeId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            if (!this.TryInvokeAuraGetStoreGoodsData(storeId, out IntPtr listObj, out error))
            {
                return false;
            }

            List<IntPtr> elements = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, elements) || elements.Count == 0)
            {
                error = "Aura shop item list empty.";
                return false;
            }

            int added = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                if (!this.TryReadAuraShopItemData(elements[i], out ShopBuyAllCandidate candidate))
                {
                    continue;
                }

                if (candidate.StoreId > 0 && candidate.StoreId != storeId)
                {
                    continue;
                }

                items.Add(candidate);
                added++;
            }

            if (added <= 0)
            {
                error = "Aura runtime list empty.";
                return false;
            }

            return true;
        }

        private string FormatShopDumpRuntimeItem(string methodTag, in ShopBuyAllCandidate item, bool clothingStore)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(methodTag).Append(' ');
            if (clothingStore)
            {
                sb.Append("buy=BuyClothes");
                sb.Append(" storeId=").Append(item.StoreId);
                sb.Append(" slotId=").Append(item.SlotId);
                sb.Append(" itemId=").Append(item.ItemId);
                sb.Append(" color1=0 color2=0");
            }
            else
            {
                sb.Append("buy=BuyItem");
                sb.Append(" storeId=").Append(item.StoreId);
                sb.Append(" slotId=").Append(item.SlotId);
                sb.Append(" itemId=").Append(item.ItemId);
                sb.Append(" count=1");
                sb.Append(" currencyEnum=").Append(item.CurrencyType);
            }

            sb.Append(" netId=").Append(item.NetId);
            sb.Append(" staticId=").Append(item.ItemStaticId);
            sb.Append(" price=").Append(item.Price);
            sb.Append(" left=").Append(item.LeftCount);
            sb.Append(" currency=").Append(item.CurrencyType);
            sb.Append(" moneyType=").Append(item.StoreMoneyType);
            sb.Append(" unlock=").Append(item.IsUnlock);
            sb.Append(" bought=").Append(item.BoughtCount);
            sb.Append(" rewardType=").Append(item.RewardType);
            sb.Append(" rewardId=").Append(item.RewardId);
            return sb.ToString();
        }

        private bool TryGetRuntimeStoreSlotSelectorOption(int selectorId, out int option, out string path)
        {
            option = -1;
            path = null;
            if (this.TryGetRuntimeStoreSlotSelectorOptionManaged(selectorId, out option))
            {
                path = "managed-StoreService";
                return true;
            }

            if (this.TryGetRuntimeStoreSlotSelectorOptionAura(selectorId, out option))
            {
                path = "aura-StoreService";
                return true;
            }

            return false;
        }

        private bool TryGetRuntimeStoreSlotSelectorOptionManaged(int selectorId, out int option)
        {
            option = -1;
            try
            {
                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService");
                Type storeServiceType = this.FindLoadedType(
                    "ClientSystem.Store.StoreService",
                    "XDT.Scene.Shared.Modules.DepartmentStore.IStoreService",
                    "IStoreService");
                if (ecsServiceType == null || storeServiceType == null)
                {
                    return false;
                }

                MethodInfo tryGetMethod = null;
                foreach (MethodInfo method in ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(method.Name, "TryGet", StringComparison.Ordinal) || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2)
                    {
                        tryGetMethod = method;
                        break;
                    }
                }

                if (tryGetMethod == null)
                {
                    return false;
                }

                MethodInfo genericTryGet = tryGetMethod.MakeGenericMethod(storeServiceType);
                object[] serviceArgs = new object[] { null, false };
                if (!(genericTryGet.Invoke(null, serviceArgs) is bool ok) || !ok || serviceArgs[0] == null)
                {
                    return false;
                }

                MethodInfo selectorMethod = serviceArgs[0].GetType().GetMethod(
                    "TryGetStoreSlotSelector",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(int), typeof(int).MakeByRefType() },
                    null);
                if (selectorMethod == null)
                {
                    return false;
                }

                object[] invokeArgs = new object[] { selectorId, 0 };
                if (!(selectorMethod.Invoke(serviceArgs[0], invokeArgs) is bool found) || !found)
                {
                    return false;
                }

                option = Convert.ToInt32(invokeArgs[1]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetRuntimeStoreSlotSelectorOptionAura(int selectorId, out int option)
        {
            option = -1;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr storeServiceClass = this.FindAuraMonoClassByFullName("ClientSystem.Store.StoreService");
                if (storeServiceClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr storeServiceObj = IntPtr.Zero;
                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService");
                Type storeServiceType = this.FindLoadedType("ClientSystem.Store.StoreService", "IStoreService");
                if (ecsServiceType != null && storeServiceType != null)
                {
                    MethodInfo tryGetMethod = null;
                    foreach (MethodInfo method in ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (string.Equals(method.Name, "TryGet", StringComparison.Ordinal) && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
                        {
                            tryGetMethod = method;
                            break;
                        }
                    }

                    if (tryGetMethod != null)
                    {
                        MethodInfo genericTryGet = tryGetMethod.MakeGenericMethod(storeServiceType);
                        object[] serviceArgs = new object[] { null, false };
                        if (genericTryGet.Invoke(null, serviceArgs) is bool ok && ok && serviceArgs[0] != null)
                        {
                            this.TryGetIl2CppObjectPointer(serviceArgs[0], out storeServiceObj);
                        }
                    }
                }

                if (storeServiceObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tryGetSelectorMethod = this.FindAuraMonoMethodOnHierarchy(storeServiceClass, "TryGetStoreSlotSelector", 2);
                if (tryGetSelectorMethod == IntPtr.Zero)
                {
                    return false;
                }

                unsafe
                {
                    int sel = selectorId;
                    int opt = 0;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&sel);
                    args[1] = (IntPtr)(&opt);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr result = auraMonoRuntimeInvoke(tryGetSelectorMethod, storeServiceObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || result == IntPtr.Zero)
                    {
                        return false;
                    }

                    if ((this.TryUnboxMonoBoolean(result, out bool ok) && ok)
                        || (this.TryUnboxMonoInt32(result, out int raw) && raw != 0))
                    {
                        option = opt;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryCollectTableStoreInfos(List<ShopDumpStoreInfoRow> rows)
        {
            rows.Clear();
            if (this.TryCollectTableStoreInfosManaged(rows))
            {
                return rows.Count > 0;
            }

            return this.TryCollectTableStoreInfosAura(rows);
        }

        private bool TryCollectTableStoreInfosManaged(List<ShopDumpStoreInfoRow> rows)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                FieldInfo field = tableDataType.GetField("TableStoreInfos", BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is IDictionary dictionary))
                {
                    return false;
                }

                MethodInfo localizationMethod = this.FindTableLocalizationMethod(tableDataType);
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null)
                    {
                        continue;
                    }

                    if (!this.TryReadShopDumpStoreInfoManaged(entry, localizationMethod, out ShopDumpStoreInfoRow row))
                    {
                        continue;
                    }

                    rows.Add(row);
                }

                rows.Sort((a, b) => a.StoreId.CompareTo(b.StoreId));
                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadShopDumpStoreInfoManaged(DictionaryEntry entry, MethodInfo localizationMethod, out ShopDumpStoreInfoRow row)
        {
            row = default(ShopDumpStoreInfoRow);
            object value = entry.Value;
            try
            {
                row.StoreId = Convert.ToInt32(entry.Key);
            }
            catch
            {
                if (!this.TryGetManagedInt32Member(value, "id", out row.StoreId))
                {
                    return false;
                }
            }

            row.DisplayName = this.TryGetLocalizedStoreName(value, localizationMethod);
            row.NpcPicture = this.TryReadObjectString(value, "npcPictureName");
            row.BgPicture = this.TryReadObjectString(value, "bgPictureId");
            row.DecorationPicture = this.TryReadObjectString(value, "decorationPictureName");
            row.CombinedText = ((row.DisplayName ?? string.Empty) + " " + row.NpcPicture + " " + row.BgPicture + " " + row.DecorationPicture).ToLowerInvariant();
            return row.StoreId > 0;
        }

        private bool TryCollectTableStoreInfosAura(List<ShopDumpStoreInfoRow> rows)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            try
            {
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
                if (!this.TryEnumerateAuraMonoCollectionItems(tableStoreInfosObj, items))
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (!this.TryReadStoreTableEntryMono(tableDataClass, items[i], out int storeId, out string displayName, out string npcPicture, out string bgPicture, out string decorationPicture))
                    {
                        continue;
                    }

                    rows.Add(new ShopDumpStoreInfoRow
                    {
                        StoreId = storeId,
                        DisplayName = displayName,
                        NpcPicture = npcPicture,
                        BgPicture = bgPicture,
                        DecorationPicture = decorationPicture,
                        CombinedText = ((displayName ?? string.Empty) + " " + npcPicture + " " + bgPicture + " " + decorationPicture).ToLowerInvariant()
                    });
                }

                rows.Sort((a, b) => a.StoreId.CompareTo(b.StoreId));
                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCollectTableStoreSlots(List<ShopDumpSlotRow> rows)
        {
            rows.Clear();
            if (this.TryCollectTableStoreSlotsManaged(rows))
            {
                return rows.Count > 0;
            }

            return this.TryCollectTableStoreSlotsAura(rows);
        }

        private bool TryCollectTableStoreSlotsManaged(List<ShopDumpSlotRow> rows)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                FieldInfo field = tableDataType?.GetField("TableStoreSlots", BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is IDictionary dictionary))
                {
                    return false;
                }

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null || !this.TryReadShopDumpSlotManaged(entry.Value, out ShopDumpSlotRow row))
                    {
                        continue;
                    }

                    rows.Add(row);
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadShopDumpSlotManaged(object slotObj, out ShopDumpSlotRow row)
        {
            row = default(ShopDumpSlotRow);
            if (slotObj == null)
            {
                return false;
            }

            this.TryGetManagedInt32Member(slotObj, "id", out row.TableId);
            this.TryGetManagedInt32Member(slotObj, "slotId", out row.SlotId);
            this.TryGetManagedInt32Member(slotObj, "storeId", out row.StoreId);
            this.TryGetManagedInt32Member(slotObj, "groupId", out row.GroupId);
            this.TryGetManagedInt32Member(slotObj, "refresh", out row.Refresh);
            row.TabName = this.TryReadObjectString(slotObj, "tabName");
            row.Function = this.TryReadObjectString(slotObj, "function");
            return row.StoreId > 0 && row.SlotId > 0;
        }

        private bool TryCollectTableStoreSlotsAura(List<ShopDumpSlotRow> rows)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableStoreSlots", out IntPtr slotsObj) || slotsObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, items))
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr valueObj = this.TryGetMonoDictionaryEntryValue(items[i]);
                    if (valueObj == IntPtr.Zero)
                    {
                        valueObj = items[i];
                    }

                    ShopDumpSlotRow row = new ShopDumpSlotRow
                    {
                        TableId = this.TryReadMonoIntMember(valueObj, "id"),
                        SlotId = this.TryReadMonoIntMember(valueObj, "slotId"),
                        StoreId = this.TryReadMonoIntMember(valueObj, "storeId"),
                        GroupId = this.TryReadMonoIntMember(valueObj, "groupId"),
                        Refresh = this.TryReadMonoIntMember(valueObj, "refresh"),
                        TabName = this.TryReadMonoStringMemberOrEmpty(valueObj, "tabName"),
                        Function = this.TryReadMonoStringMemberOrEmpty(valueObj, "function")
                    };

                    if (row.StoreId > 0 && row.SlotId > 0)
                    {
                        rows.Add(row);
                    }
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCollectTableStoreSlotSelectors(List<ShopDumpSelectorRow> rows)
        {
            rows.Clear();
            if (this.TryCollectTableStoreSlotSelectorsManaged(rows))
            {
                return rows.Count > 0;
            }

            return this.TryCollectTableStoreSlotSelectorsAura(rows);
        }

        private bool TryCollectTableStoreSlotSelectorsManaged(List<ShopDumpSelectorRow> rows)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                FieldInfo field = tableDataType?.GetField("TableStoreSlotSelectors", BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is IDictionary dictionary))
                {
                    return false;
                }

                Dictionary<int, int> slotGroupByKey = this.BuildSlotGroupLookupManaged(tableDataType);

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null)
                    {
                        continue;
                    }

                    ShopDumpSelectorRow selector = new ShopDumpSelectorRow();
                    object selectorObj = entry.Value;
                    try
                    {
                        selector.SelectorId = Convert.ToInt32(entry.Key);
                    }
                    catch
                    {
                        this.TryGetManagedInt32Member(selectorObj, "id", out selector.SelectorId);
                    }

                    selector.Name = this.TryReadObjectString(selectorObj, "name");
                    this.TryReadShopDumpSelectorOptionsManaged(selectorObj, slotGroupByKey, selector.Options);
                    if (selector.SelectorId > 0 && selector.Options.Count > 0)
                    {
                        rows.Add(selector);
                    }
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<int, int> BuildSlotGroupLookupManaged(Type tableDataType)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            if (tableDataType == null)
            {
                return map;
            }

            FieldInfo slotsField = tableDataType.GetField("TableStoreSlots", BindingFlags.Public | BindingFlags.Static);
            if (!(slotsField?.GetValue(null) is IDictionary slots))
            {
                return map;
            }

            foreach (DictionaryEntry entry in slots)
            {
                if (entry.Value == null || !this.TryReadShopDumpSlotManaged(entry.Value, out ShopDumpSlotRow row))
                {
                    continue;
                }

                int key = (row.StoreId << 16) | (row.SlotId & 0xFFFF);
                if (!map.ContainsKey(key))
                {
                    map[key] = row.GroupId;
                }
            }

            return map;
        }

        private void TryReadShopDumpSelectorOptionsManaged(object selectorObj, Dictionary<int, int> slotGroupByKey, List<ShopDumpSelectorOptionRow> options)
        {
            options.Clear();
            if (selectorObj == null)
            {
                return;
            }

            int[] optionIds = null;
            if (this.TryGetObjectMember(selectorObj, "Options", out object optionsObj) && optionsObj is int[] ints)
            {
                optionIds = ints;
            }
            else if (optionsObj is Array arr)
            {
                optionIds = new int[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    optionIds[i] = Convert.ToInt32(arr.GetValue(i));
                }
            }

            object[] optionConfig = null;
            if (this.TryGetObjectMember(selectorObj, "OptionConfig", out object configObj) && configObj is Array configArr)
            {
                optionConfig = new object[configArr.Length];
                for (int i = 0; i < configArr.Length; i++)
                {
                    optionConfig[i] = configArr.GetValue(i);
                }
            }

            if (optionIds == null || optionConfig == null)
            {
                return;
            }

            int count = Math.Min(optionIds.Length, optionConfig.Length);
            for (int i = 0; i < count; i++)
            {
                object cfg = optionConfig[i];
                if (cfg == null)
                {
                    continue;
                }

                this.TryGetManagedInt32Member(cfg, "store", out int storeId);
                this.TryGetManagedInt32Member(cfg, "slot", out int slotId);
                if (storeId <= 0)
                {
                    this.TryGetManagedInt32Member(cfg, "Store", out storeId);
                }

                if (slotId <= 0)
                {
                    this.TryGetManagedInt32Member(cfg, "Slot", out slotId);
                }

                int key = (storeId << 16) | (slotId & 0xFFFF);
                slotGroupByKey.TryGetValue(key, out int groupId);
                options.Add(new ShopDumpSelectorOptionRow
                {
                    OptionId = optionIds[i],
                    StoreId = storeId,
                    SlotId = slotId,
                    GroupId = groupId
                });
            }
        }

        private bool TryCollectTableStoreSlotSelectorsAura(List<ShopDumpSelectorRow> rows)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                Dictionary<int, int> slotGroupByKey = this.BuildSlotGroupLookupAura(tableDataClass);
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableStoreSlotSelectors", out IntPtr selectorsObj) || selectorsObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(selectorsObj, items))
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr valueObj = this.TryGetMonoDictionaryEntryValue(items[i]);
                    if (valueObj == IntPtr.Zero)
                    {
                        valueObj = items[i];
                    }

                    ShopDumpSelectorRow selector = new ShopDumpSelectorRow
                    {
                        SelectorId = this.TryReadMonoIntMember(valueObj, "id"),
                        Name = this.TryReadMonoStringMemberOrEmpty(valueObj, "name")
                    };

                    this.TryReadShopDumpSelectorOptionsAura(valueObj, slotGroupByKey, selector.Options);
                    if (selector.SelectorId > 0 && selector.Options.Count > 0)
                    {
                        rows.Add(selector);
                    }
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<int, int> BuildSlotGroupLookupAura(IntPtr tableDataClass)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            if (tableDataClass == IntPtr.Zero)
            {
                return map;
            }

            if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableStoreSlots", out IntPtr slotsObj) || slotsObj == IntPtr.Zero)
            {
                return map;
            }

            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, items))
            {
                return map;
            }

            for (int i = 0; i < items.Count; i++)
            {
                IntPtr valueObj = this.TryGetMonoDictionaryEntryValue(items[i]);
                if (valueObj == IntPtr.Zero)
                {
                    valueObj = items[i];
                }

                int storeId = this.TryReadMonoIntMember(valueObj, "storeId");
                int slotId = this.TryReadMonoIntMember(valueObj, "slotId");
                int groupId = this.TryReadMonoIntMember(valueObj, "groupId");
                if (storeId <= 0 || slotId <= 0)
                {
                    continue;
                }

                int key = (storeId << 16) | (slotId & 0xFFFF);
                if (!map.ContainsKey(key))
                {
                    map[key] = groupId;
                }
            }

            return map;
        }

        private void TryReadShopDumpSelectorOptionsAura(IntPtr selectorObj, Dictionary<int, int> slotGroupByKey, List<ShopDumpSelectorOptionRow> options)
        {
            options.Clear();
            if (selectorObj == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(selectorObj, "Options", out IntPtr optionsArray) || optionsArray == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(selectorObj, "OptionConfig", out IntPtr configArray) || configArray == IntPtr.Zero)
            {
                return;
            }

            List<IntPtr> optionValues = new List<IntPtr>();
            List<IntPtr> configValues = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(optionsArray, optionValues) || !this.TryEnumerateAuraMonoCollectionItems(configArray, configValues))
            {
                return;
            }

            int count = Math.Min(optionValues.Count, configValues.Count);
            for (int i = 0; i < count; i++)
            {
                int optionId = 0;
                if (!this.TryUnboxMonoInt32(optionValues[i], out optionId))
                {
                    optionId = this.TryReadMonoIntMember(optionValues[i], "m_value");
                }

                IntPtr cfg = configValues[i];
                int storeId = this.TryReadMonoIntMember(cfg, "store");
                int slotId = this.TryReadMonoIntMember(cfg, "slot");
                int key = (storeId << 16) | (slotId & 0xFFFF);
                slotGroupByKey.TryGetValue(key, out int groupId);
                options.Add(new ShopDumpSelectorOptionRow
                {
                    OptionId = optionId,
                    StoreId = storeId,
                    SlotId = slotId,
                    GroupId = groupId
                });
            }
        }

        private bool TryCollectStoreGroupWithCanBuy(List<ShopDumpGroupRow> rows)
        {
            rows.Clear();
            if (this.TryCollectStoreGroupWithCanBuyManaged(rows))
            {
                return rows.Count > 0;
            }

            return this.TryCollectStoreGroupWithCanBuyAura(rows);
        }

        private bool TryCollectStoreGroupWithCanBuyManaged(List<ShopDumpGroupRow> rows)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                PropertyInfo prop = tableDataType.GetProperty("StoreGroupWithCanBuy", BindingFlags.Public | BindingFlags.Static);
                object groupsObj = prop?.GetValue(null);
                if (groupsObj == null)
                {
                    FieldInfo field = tableDataType.GetField("StoreGroupWithCanBuy", BindingFlags.Public | BindingFlags.Static);
                    groupsObj = field?.GetValue(null);
                }

                if (!(groupsObj is IDictionary dictionary))
                {
                    return false;
                }

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null || !this.TryReadShopDumpGroupManaged(entry.Value, out ShopDumpGroupRow row))
                    {
                        continue;
                    }

                    rows.Add(row);
                }

                return rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadShopDumpGroupManaged(object groupObj, out ShopDumpGroupRow row)
        {
            row = default(ShopDumpGroupRow);
            if (groupObj == null)
            {
                return false;
            }

            this.TryGetManagedInt32Member(groupObj, "id", out row.ItemId);
            this.TryGetManagedInt32Member(groupObj, "groupId", out row.PoolGroupId);
            this.TryGetManagedInt32Member(groupObj, "modelgroupId", out row.ModelGroupId);
            this.TryGetManagedInt32Member(groupObj, "suitId", out row.SuitId);
            this.TryGetManagedInt32Member(groupObj, "price", out row.Price);
            this.TryGetManagedInt32Member(groupObj, "moneyType", out row.MoneyType);
            this.TryGetManagedInt32Member(groupObj, "moneyValue", out row.MoneyValue);
            this.TryGetManagedInt32Member(groupObj, "labelType", out row.LabelType);
            this.TryGetManagedInt32Member(groupObj, "buyCount", out row.BuyCount);
            if (this.TryGetObjectMember(groupObj, "available", out object availableObj) && availableObj is bool available)
            {
                row.Available = available;
            }

            this.TryReadShopDumpRewardsManaged(groupObj, out row.RewardsSummary, out row.PrimaryRewardType, out row.PrimaryRewardParam);
            return row.ItemId > 0;
        }

        private void TryReadShopDumpRewardsManaged(object groupObj, out string rewardsSummary, out int primaryRewardType, out int primaryRewardParam)
        {
            rewardsSummary = string.Empty;
            primaryRewardType = 0;
            primaryRewardParam = 0;
            if (!this.TryGetObjectMember(groupObj, "reward", out object rewardObj) || !(rewardObj is IEnumerable enumerable))
            {
                return;
            }

            StringBuilder rewards = new StringBuilder();
            int index = 0;
            foreach (object rewardItem in enumerable)
            {
                if (rewardItem == null)
                {
                    continue;
                }

                this.TryGetManagedInt32Member(rewardItem, "rewardType", out int rewardType);
                this.TryGetManagedInt32Member(rewardItem, "rewardParam", out int rewardParam);
                this.TryGetManagedInt32Member(rewardItem, "value", out int value);
                if (index == 0)
                {
                    primaryRewardType = rewardType;
                    primaryRewardParam = rewardParam;
                }

                if (index > 0)
                {
                    rewards.Append(';');
                }

                rewards.Append(rewardType).Append(':').Append(rewardParam).Append('x').Append(value);
                index++;
            }

            rewardsSummary = rewards.ToString();
        }

        private bool TryReadShopDumpGroupAura(IntPtr valueObj, out ShopDumpGroupRow row)
        {
            row = new ShopDumpGroupRow
            {
                ItemId = this.TryReadMonoIntMember(valueObj, "id"),
                PoolGroupId = this.TryReadMonoIntMember(valueObj, "groupId"),
                ModelGroupId = this.TryReadMonoIntMember(valueObj, "modelgroupId"),
                SuitId = this.TryReadMonoIntMember(valueObj, "suitId"),
                Price = this.TryReadMonoIntMember(valueObj, "price"),
                MoneyType = this.TryReadMonoIntMember(valueObj, "moneyType"),
                MoneyValue = this.TryReadMonoIntMember(valueObj, "moneyValue"),
                LabelType = this.TryReadMonoIntMember(valueObj, "labelType"),
                BuyCount = this.TryReadMonoIntMember(valueObj, "buyCount"),
                Available = this.TryReadMonoIntMember(valueObj, "available") != 0
            };
            row.RewardsSummary = this.TryReadShopDumpRewardsAura(valueObj, out row.PrimaryRewardType, out row.PrimaryRewardParam);
            return row.ItemId > 0;
        }

        private bool TryCollectStoreGroupWithCanBuyAura(List<ShopDumpGroupRow> rows)
        {
            rows.Clear();
            return this.TryCollectStoreGroupsAuraField("StoreGroupWithCanBuy", rows);
        }

        private string TryReadShopDumpRewardsAura(IntPtr groupObj, out int primaryRewardType, out int primaryRewardParam)
        {
            primaryRewardType = 0;
            primaryRewardParam = 0;
            if (groupObj == IntPtr.Zero || !this.TryGetMonoObjectMember(groupObj, "reward", out IntPtr rewardArray) || rewardArray == IntPtr.Zero)
            {
                return string.Empty;
            }

            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(rewardArray, items))
            {
                return string.Empty;
            }

            StringBuilder rewards = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                int rewardType = this.TryReadMonoIntMember(items[i], "rewardType");
                int rewardParam = this.TryReadMonoIntMember(items[i], "rewardParam");
                int value = this.TryReadMonoIntMember(items[i], "value");
                if (i == 0)
                {
                    primaryRewardType = rewardType;
                    primaryRewardParam = rewardParam;
                }

                if (i > 0)
                {
                    rewards.Append(';');
                }

                rewards.Append(rewardType).Append(':').Append(rewardParam).Append('x').Append(value);
            }

            return rewards.ToString();
        }

        private IntPtr FindAuraMonoTableDataClass()
        {
            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }

            return tableDataClass;
        }

        private IntPtr TryGetMonoDictionaryEntryValue(IntPtr entryObj)
        {
            if (entryObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr value) && value != IntPtr.Zero)
            {
                return value;
            }

            if (this.TryGetMonoObjectMember(entryObj, "value", out value) && value != IntPtr.Zero)
            {
                return value;
            }

            return IntPtr.Zero;
        }

        private int TryReadMonoIntMember(IntPtr obj, string memberName)
        {
            if (obj == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryGetMonoInt32Member(obj, memberName, out int value))
            {
                return value;
            }

            if (this.TryGetMonoIntMember(obj, memberName, out int fallback))
            {
                return fallback;
            }

            return 0;
        }

        private static string ShopDumpDescribeRefresh(int refresh)
        {
            switch (refresh)
            {
                case 1:
                    return "daily";
                case 2:
                    return "weekly";
                default:
                    return refresh == 0 ? "none" : "kind=" + refresh;
            }
        }

        private int ShopDumpLogLine(string message)
        {
            this.ShopDumpLog(message);
            return 1;
        }

        private struct ShopDumpStoreInfoRow
        {
            public int StoreId;
            public string DisplayName;
            public string NpcPicture;
            public string BgPicture;
            public string DecorationPicture;
            public string CombinedText;
        }

        private struct ShopDumpSlotRow
        {
            public int TableId;
            public int SlotId;
            public int StoreId;
            public int GroupId;
            public int Refresh;
            public string TabName;
            public string Function;
        }

        private struct ShopDumpSelectorOptionRow
        {
            public int OptionId;
            public int StoreId;
            public int SlotId;
            public int GroupId;
        }

        private class ShopDumpSelectorRow
        {
            public int SelectorId;
            public string Name;
            public List<ShopDumpSelectorOptionRow> Options = new List<ShopDumpSelectorOptionRow>();
        }

        private struct ShopDumpGroupRow
        {
            public int ItemId;
            public int PoolGroupId;
            public int ModelGroupId;
            public int SuitId;
            public int Price;
            public int MoneyType;
            public int MoneyValue;
            public int LabelType;
            public int BuyCount;
            public bool Available;
            public int PrimaryRewardType;
            public int PrimaryRewardParam;
            public string RewardsSummary;
        }
    }
}
