using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private string itemDumpStatus = "Idle.";
        private string shopItemDumpStatus = "Idle.";

        private void StartTableEntityDump()
        {
            try
            {
                this.itemDumpStatus = "Dumping TableEntitys...";
                int count = this.DumpTableEntitysToLog();
                this.itemDumpStatus = "Dumped " + count + " entities — see [ItemDump] in log.";
                this.AddMenuNotification(this.itemDumpStatus, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.itemDumpStatus = "Dump failed: " + ex.Message;
                this.ItemDumpLog(this.itemDumpStatus, always: true);
                this.AddMenuNotification(this.itemDumpStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

        private void ItemDumpLog(string message, bool always = true)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[ItemDump] " + message);
            }
            catch
            {
            }
        }

        private int DumpTableEntitysToLog()
        {
            int lines = 0;
            lines += this.ItemDumpLogLine("========== TableEntitys dump begin ==========");
            lines += this.ItemDumpLogLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            List<ItemDumpEntityRow> rows = new List<ItemDumpEntityRow>();
            string path = null;
            if (this.TryCollectTableEntityRowsManaged(rows))
            {
                path = "managed";
            }
            else if (this.TryCollectTableEntityRowsAura(rows))
            {
                path = "aura";
            }

            if (rows.Count == 0)
            {
                lines += this.ItemDumpLogLine("ERROR: TableEntitys unavailable (enter town, wait for tables).");
                lines += this.ItemDumpLogLine("========== TableEntitys dump end ==========");
                return lines;
            }

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            lines += this.ItemDumpLogLine("source=" + path + " count=" + rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                ItemDumpEntityRow row = rows[i];
                lines += this.ItemDumpLogLine(this.FormatItemDumpEntityRow(row));
            }

            lines += this.ItemDumpLogLine("TableEntitys entries=" + rows.Count);
            lines += this.ItemDumpLogLine("========== TableEntitys dump end ==========");
            return lines;
        }

        private string FormatItemDumpEntityRow(in ItemDumpEntityRow row)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("id=").Append(row.Id);
            sb.Append(" name='").Append(ItemDumpEscape(row.Name)).Append('\'');
            if (row.EntityTypeId >= 0)
            {
                sb.Append(" entityTypeId=").Append(row.EntityTypeId);
            }

            if (!string.IsNullOrEmpty(row.EntityTypeName))
            {
                sb.Append(" entityType='").Append(ItemDumpEscape(row.EntityTypeName)).Append('\'');
            }

            if (row.Rarity > 0)
            {
                sb.Append(" rarity=").Append(row.Rarity);
            }

            if (row.QuickSalePrice > 0)
            {
                sb.Append(" quickSale=").Append(row.QuickSalePrice);
            }

            return sb.ToString();
        }

        private static string ItemDumpEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("'", "\\'");
        }

        private int ItemDumpLogLine(string message)
        {
            this.ItemDumpLog(message);
            return 1;
        }

        private bool TryCollectTableEntityRowsManaged(List<ItemDumpEntityRow> rows)
        {
            rows.Clear();
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                FieldInfo field = tableDataType.GetField("TableEntitys", BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is IDictionary dictionary))
                {
                    return false;
                }

                MethodInfo localizeMethod = tableDataType.GetMethod(
                    "Localize",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                MethodInfo getEntityTypeIdMethod = tableDataType.GetMethod(
                    "GetEntityTypeID",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
                MethodInfo getEntityTypeNameMethod = tableDataType.GetMethod(
                    "GetEntityTypeName",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value == null || !this.TryReadItemDumpEntityManaged(
                            entry.Value,
                            localizeMethod,
                            getEntityTypeIdMethod,
                            getEntityTypeNameMethod,
                            out ItemDumpEntityRow row))
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

        private bool TryReadItemDumpEntityManaged(
            object entityObj,
            MethodInfo localizeMethod,
            MethodInfo getEntityTypeIdMethod,
            MethodInfo getEntityTypeNameMethod,
            out ItemDumpEntityRow row)
        {
            row = default(ItemDumpEntityRow);
            if (entityObj == null)
            {
                return false;
            }

            if (!this.TryGetManagedInt32Member(entityObj, "id", out row.Id) || row.Id <= 0)
            {
                return false;
            }

            row.Name = this.TryResolveItemDumpEntityName(row.Id, entityObj, localizeMethod);
            this.TryGetManagedInt32Member(entityObj, "rarity", out row.Rarity);
            if (row.Rarity <= 0)
            {
                this.TryGetManagedInt32Member(entityObj, "_rarity", out row.Rarity);
            }

            this.TryGetManagedInt32Member(entityObj, "quickSalePrice", out row.QuickSalePrice);

            if (getEntityTypeIdMethod != null)
            {
                try
                {
                    row.EntityTypeId = Convert.ToInt32(getEntityTypeIdMethod.Invoke(null, new object[] { row.Id }));
                }
                catch
                {
                    row.EntityTypeId = -1;
                }
            }

            if (getEntityTypeNameMethod != null)
            {
                try
                {
                    row.EntityTypeName = Convert.ToString(getEntityTypeNameMethod.Invoke(null, new object[] { row.Id })) ?? string.Empty;
                }
                catch
                {
                    row.EntityTypeName = string.Empty;
                }
            }

            return true;
        }

        private string TryResolveItemDumpEntityName(int staticId, object entityObj, MethodInfo localizeMethod)
        {
            if (staticId > 0
                && this.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                && !this.IsPoorBagItemDisplayName(resolved, staticId))
            {
                return resolved;
            }

            string legacy = this.TryReadTableEntityNameManaged(entityObj, localizeMethod);
            if (!this.IsPoorBagItemDisplayName(legacy, staticId))
            {
                return legacy;
            }

            return string.Empty;
        }

        private string TryResolveItemDumpEntityNameAura(int staticId, IntPtr entityObj, IntPtr localizeMethod)
        {
            if (staticId > 0
                && this.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                && !this.IsPoorBagItemDisplayName(resolved, staticId))
            {
                return resolved;
            }

            string legacy = this.TryReadTableEntityNameAura(entityObj, localizeMethod);
            if (!this.IsPoorBagItemDisplayName(legacy, staticId))
            {
                return legacy;
            }

            return string.Empty;
        }

        private string TryReadTableEntityNameManaged(object entityObj, MethodInfo localizeMethod)
        {
            if (this.TryGetObjectMember(entityObj, "name", out object nameObj) && nameObj != null)
            {
                string localized = Convert.ToString(nameObj);
                if (!string.IsNullOrWhiteSpace(localized))
                {
                    return localized;
                }
            }

            string rawName = this.TryReadObjectString(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            if (localizeMethod != null)
            {
                try
                {
                    object localizedObj = localizeMethod.Invoke(null, new object[] { rawName });
                    if (localizedObj != null)
                    {
                        return Convert.ToString(localizedObj) ?? rawName;
                    }
                }
                catch
                {
                }
            }

            return rawName;
        }

        private bool TryCollectTableEntityRowsAura(List<ItemDumpEntityRow> rows)
        {
            rows.Clear();
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

                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableEntitys", out IntPtr entitiesObj) || entitiesObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr localizeMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "Localize", 1);
                IntPtr getEntityTypeIdMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntityTypeID", 1);
                IntPtr getEntityTypeNameMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntityTypeName", 1);

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(entitiesObj, items))
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

                    if (!this.TryReadItemDumpEntityAura(
                            valueObj,
                            tableDataClass,
                            localizeMethod,
                            getEntityTypeIdMethod,
                            getEntityTypeNameMethod,
                            out ItemDumpEntityRow row))
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

        private unsafe bool TryReadItemDumpEntityAura(
            IntPtr entityObj,
            IntPtr tableDataClass,
            IntPtr localizeMethod,
            IntPtr getEntityTypeIdMethod,
            IntPtr getEntityTypeNameMethod,
            out ItemDumpEntityRow row)
        {
            row = default(ItemDumpEntityRow);
            if (entityObj == IntPtr.Zero)
            {
                return false;
            }

            row.Id = this.TryReadMonoIntMember(entityObj, "id");
            if (row.Id <= 0)
            {
                return false;
            }

            row.Name = this.TryResolveItemDumpEntityNameAura(row.Id, entityObj, localizeMethod);
            row.Rarity = this.TryReadMonoIntMember(entityObj, "rarity");
            if (row.Rarity <= 0)
            {
                row.Rarity = this.TryReadMonoIntMember(entityObj, "_rarity");
            }

            row.QuickSalePrice = this.TryReadMonoIntMember(entityObj, "quickSalePrice");
            row.EntityTypeId = this.TryInvokeAuraTableEntityIntHelper(getEntityTypeIdMethod, row.Id);
            row.EntityTypeName = this.TryInvokeAuraTableEntityStringHelper(getEntityTypeNameMethod, row.Id);
            return true;
        }

        private unsafe string TryReadTableEntityNameAura(IntPtr entityObj, IntPtr localizeMethod)
        {
            string rawName = this.TryReadMonoStringMemberOrEmpty(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            if (localizeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoStringNew == null)
            {
                return rawName;
            }

            IntPtr rawNameObj = auraMonoStringNew(this.auraMonoRootDomain, rawName);
            if (rawNameObj == IntPtr.Zero)
            {
                return rawName;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = rawNameObj;
            IntPtr exc = IntPtr.Zero;
            IntPtr resultObj = auraMonoRuntimeInvoke(localizeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || resultObj == IntPtr.Zero)
            {
                return rawName;
            }

            if (this.TryReadMonoString(resultObj, out string localized) && !string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return rawName;
        }

        private unsafe int TryInvokeAuraTableEntityIntHelper(IntPtr method, int id)
        {
            if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return -1;
            }

            int idValue = id;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero)
            {
                return -1;
            }

            if (this.TryUnboxMonoInt32(result, out int value))
            {
                return value;
            }

            return -1;
        }

        private unsafe string TryInvokeAuraTableEntityStringHelper(IntPtr method, int id)
        {
            if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return string.Empty;
            }

            int idValue = id;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero)
            {
                return string.Empty;
            }

            if (this.TryReadMonoString(result, out string value))
            {
                return value ?? string.Empty;
            }

            return string.Empty;
        }

        private void StartShopItemIdDump()
        {
            try
            {
                this.shopItemDumpStatus = "Dumping TableStoreGroups...";
                int count = this.DumpShopItemIdsToLog();
                this.shopItemDumpStatus = "Dumped " + count + " shop itemIds — see [ShopItemDump] in log.";
                this.AddMenuNotification(this.shopItemDumpStatus, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.shopItemDumpStatus = "Dump failed: " + ex.Message;
                this.ShopItemDumpLog(this.shopItemDumpStatus, always: true);
                this.AddMenuNotification(this.shopItemDumpStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

        private void ShopItemDumpLog(string message, bool always = true)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[ShopItemDump] " + message);
            }
            catch
            {
            }
        }

        private int DumpShopItemIdsToLog()
        {
            int lines = 0;
            lines += this.ShopItemDumpLogLine("========== shop itemId dump (TableStoreGroups) begin ==========");
            lines += this.ShopItemDumpLogLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            lines += this.ShopItemDumpLogLine("itemId = TableStoreGroup.id (for QuickBuyItem / OPEN BUY PANEL)");
            lines += this.ShopItemDumpLogLine("staticId = reward[0].rewardParam (TableEntity.id when applicable)");

            List<ShopDumpGroupRow> canBuy = new List<ShopDumpGroupRow>();
            List<ShopDumpGroupRow> tableGroups = new List<ShopDumpGroupRow>();
            this.TryCollectStoreGroupWithCanBuy(canBuy);
            this.TryCollectTableStoreGroups(tableGroups);
            List<ShopDumpGroupRow> groups = this.MergeShopDumpCatalogGroups(canBuy, tableGroups);
            if (groups.Count == 0)
            {
                lines += this.ShopItemDumpLogLine("ERROR: TableStoreGroups unavailable (enter town, wait for tables).");
                lines += this.ShopItemDumpLogLine("========== shop itemId dump end ==========");
                return lines;
            }

            List<ShopDumpSlotRow> slots = new List<ShopDumpSlotRow>();
            this.TryCollectTableStoreSlots(slots);
            Dictionary<int, List<string>> poolToStoreSlots = this.BuildShopItemPoolToStoreSlots(slots);

            ItemDumpEntityNameResolver nameResolver = this.CreateItemDumpEntityNameResolver();
            lines += this.ShopItemDumpLogLine(
                "sources: StoreGroupWithCanBuy=" + canBuy.Count
                + " TableStoreGroups=" + tableGroups.Count
                + " merged=" + groups.Count
                + " slotPools=" + poolToStoreSlots.Count);

            for (int i = 0; i < groups.Count; i++)
            {
                lines += this.ShopItemDumpLogLine(this.FormatShopItemIdDumpLine(groups[i], poolToStoreSlots, ref nameResolver));
            }

            lines += this.ShopItemDumpLogLine("entries=" + groups.Count);
            lines += this.ShopItemDumpLogLine("========== shop itemId dump end ==========");
            return lines;
        }

        private Dictionary<int, List<string>> BuildShopItemPoolToStoreSlots(List<ShopDumpSlotRow> slots)
        {
            Dictionary<int, List<string>> poolToStoreSlots = new Dictionary<int, List<string>>();
            for (int i = 0; i < slots.Count; i++)
            {
                ShopDumpSlotRow slot = slots[i];
                if (slot.GroupId <= 0 || slot.StoreId <= 0 || slot.SlotId <= 0)
                {
                    continue;
                }

                string label = slot.StoreId + ":" + slot.SlotId;
                if (!poolToStoreSlots.TryGetValue(slot.GroupId, out List<string> labels))
                {
                    labels = new List<string>();
                    poolToStoreSlots[slot.GroupId] = labels;
                }

                if (!labels.Contains(label))
                {
                    labels.Add(label);
                }
            }

            return poolToStoreSlots;
        }

        private string FormatShopItemIdDumpLine(
            in ShopDumpGroupRow group,
            Dictionary<int, List<string>> poolToStoreSlots,
            ref ItemDumpEntityNameResolver nameResolver)
        {
            int staticId = group.PrimaryRewardParam;
            string entityName = staticId > 0 ? nameResolver.Resolve(staticId) : string.Empty;
            StringBuilder sb = new StringBuilder();
            sb.Append("itemId=").Append(group.ItemId);
            if (staticId > 0)
            {
                sb.Append(" staticId=").Append(staticId);
            }

            if (!string.IsNullOrEmpty(entityName))
            {
                sb.Append(" name='").Append(ItemDumpEscape(entityName)).Append('\'');
            }

            sb.Append(" price=").Append(group.Price);
            sb.Append(" moneyType=").Append(group.MoneyType);
            sb.Append(" moneyValue=").Append(group.MoneyValue);
            sb.Append(" poolGroupId=").Append(group.PoolGroupId);
            if (group.ModelGroupId > 0)
            {
                sb.Append(" modelGroupId=").Append(group.ModelGroupId);
            }

            sb.Append(" available=").Append(group.Available);
            if (group.PrimaryRewardType > 0)
            {
                sb.Append(" rewardType=").Append(group.PrimaryRewardType);
            }

            if (!string.IsNullOrEmpty(group.RewardsSummary))
            {
                sb.Append(" rewards='").Append(ItemDumpEscape(group.RewardsSummary)).Append('\'');
            }

            List<string> slotLabels = this.CollectShopItemStoreSlotLabels(group, poolToStoreSlots);
            if (slotLabels.Count > 0)
            {
                sb.Append(" storeSlots=");
                sb.Append(string.Join(",", slotLabels));
            }

            return sb.ToString();
        }

        private List<string> CollectShopItemStoreSlotLabels(in ShopDumpGroupRow group, Dictionary<int, List<string>> poolToStoreSlots)
        {
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            this.AddShopItemStoreSlotLabels(labels, group.PoolGroupId, poolToStoreSlots);
            if (group.ModelGroupId > 0)
            {
                this.AddShopItemStoreSlotLabels(labels, group.ModelGroupId, poolToStoreSlots);
            }

            List<string> sorted = new List<string>(labels);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        private void AddShopItemStoreSlotLabels(HashSet<string> labels, int poolGroupId, Dictionary<int, List<string>> poolToStoreSlots)
        {
            if (poolGroupId <= 0 || !poolToStoreSlots.TryGetValue(poolGroupId, out List<string> poolLabels))
            {
                return;
            }

            for (int i = 0; i < poolLabels.Count; i++)
            {
                labels.Add(poolLabels[i]);
            }
        }

        private int ShopItemDumpLogLine(string message)
        {
            this.ShopItemDumpLog(message);
            return 1;
        }

        private ItemDumpEntityNameResolver CreateItemDumpEntityNameResolver()
        {
            ItemDumpEntityNameResolver resolver = new ItemDumpEntityNameResolver();
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType != null)
                {
                    resolver.LocalizeMethod = tableDataType.GetMethod(
                        "Localize",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
                        null);
                    resolver.GetEntityMethod = null;
                    foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!string.Equals(method.Name, "GetEntity", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            resolver.GetEntityMethod = method;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            if (this.EnsureAuraMonoApiReady())
            {
                IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
                if (tableDataClass != IntPtr.Zero)
                {
                    resolver.AuraLocalizeMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "Localize", 1);
                    resolver.AuraGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 1);
                    if (resolver.AuraGetEntityMethod == IntPtr.Zero)
                    {
                        resolver.AuraGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                    }
                }
            }

            return resolver;
        }

        private sealed class ItemDumpEntityNameResolver
        {
            private readonly Dictionary<int, string> cache = new Dictionary<int, string>();

            public MethodInfo LocalizeMethod;
            public MethodInfo GetEntityMethod;
            public IntPtr AuraLocalizeMethod;
            public IntPtr AuraGetEntityMethod;

            public string Resolve(int staticId)
            {
                if (staticId <= 0)
                {
                    return string.Empty;
                }

                if (this.cache.TryGetValue(staticId, out string cached))
                {
                    return cached;
                }

                string name = this.ResolveUncached(staticId);
                this.cache[staticId] = name ?? string.Empty;
                return name ?? string.Empty;
            }

            private string ResolveUncached(int staticId)
            {
                HeartopiaComplete host = HeartopiaComplete.Instance;
                if (host == null)
                {
                    return string.Empty;
                }

                if (host.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                    && !host.IsPoorBagItemDisplayName(resolved, staticId))
                {
                    return resolved;
                }

                if (this.GetEntityMethod != null && this.LocalizeMethod != null)
                {
                    try
                    {
                        object[] args = this.GetEntityMethod.GetParameters().Length >= 2
                            ? new object[] { staticId, false }
                            : new object[] { staticId };
                        if (this.GetEntityMethod.Invoke(null, args) is object entityObj)
                        {
                            string legacy = host.TryResolveItemDumpEntityName(staticId, entityObj, this.LocalizeMethod);
                            if (!host.IsPoorBagItemDisplayName(legacy, staticId))
                            {
                                return legacy;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                string auraLegacy = host.TryResolveEntityNameAura(staticId, this.AuraGetEntityMethod, this.AuraLocalizeMethod);
                if (!host.IsPoorBagItemDisplayName(auraLegacy, staticId))
                {
                    return auraLegacy;
                }

                return string.Empty;
            }
        }

        private unsafe string TryResolveEntityNameAura(int staticId, IntPtr getEntityMethod, IntPtr localizeMethod)
        {
            if (staticId <= 0 || getEntityMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return string.Empty;
            }

            int idValue = staticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                bool needException = false;
                IntPtr* args2 = stackalloc IntPtr[2];
                args2[0] = (IntPtr)(&idValue);
                args2[1] = (IntPtr)(&needException);
                exc = IntPtr.Zero;
                entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args2, ref exc);
                if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
                {
                    return string.Empty;
                }
            }

            string rawName = this.TryReadMonoStringMemberOrEmpty(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                if (this.TryGetMonoStringMember(entityObj, "name", out string propertyName))
                {
                    return propertyName ?? string.Empty;
                }

                return string.Empty;
            }

            string legacy = this.TryReadTableEntityNameAura(entityObj, localizeMethod);
            if (!this.IsPoorBagItemDisplayName(legacy, staticId))
            {
                return legacy;
            }

            if (this.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                && !this.IsPoorBagItemDisplayName(resolved, staticId))
            {
                return resolved;
            }

            return string.Empty;
        }

        private struct ItemDumpEntityRow
        {
            public int Id;
            public string Name;
            public int EntityTypeId;
            public string EntityTypeName;
            public int Rarity;
            public int QuickSalePrice;
        }
    }
}
