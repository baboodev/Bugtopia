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
        private bool IsGoldCurrencyItem(Transform goodsWidget)
        {
            try
            {
                // Check currency icon path: AniRoot@ani/info@group/currency@go/currency@w/Group/icon@img
                Transform iconImg = goodsWidget.Find("AniRoot@ani/info@group/currency@go/currency@w/Group/icon@img")
                    ?? goodsWidget.Find("AniRoot@ani/info@group/currency@go/icon@img")
                    ?? goodsWidget.Find("currency@go/icon@img");
                if (iconImg != null)
                {
                    var img = iconImg.GetComponent<Image>();
                    if (img != null && img.sprite != null)
                    {
                        string spriteName = img.sprite.name ?? "";
                        // Sprite names are numeric:
                        // "1(Clone)" = gold coin (what we want)
                        // "4(Clone)" = theme coin (star - skip)
                        // "2(Clone)" = diamond (skip)
                        bool isGold = spriteName.StartsWith("1");
                        if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Currency sprite: {spriteName}, isGold={isGold}"); }
                        return isGold;
                    }
                    else
                    {
                        LogAutoBuy(" Currency Image component or sprite is null");
                    }
                }
                else
                {
                    // No currency icon found - might be gold by default, allow it
                    LogAutoBuy(" No currency icon found, allowing item");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] IsGoldCurrencyItem error: {ex.Message}"); }
            }
            // Default to true (allow) if we can't determine currency type
            return true;
        }

        // matchedEntityId = the TablePeriodCurrencySale entityId that classified this stack as
        // festival-sellable (0 when not festival) — the split uses it to look up the row's
        // per-period sell cap (maxSellCount) and the player's sold-so-far count.
        private void ClassifyPeriodCurrencyStack(int currencyTypeId, uint netId, string descriptor, HashSet<int> allowedStaticIds, bool useAllowlist, out bool isFestival, out int matchedEntityId)
        {
            isFestival = false;
            matchedEntityId = 0;
            this.CollectPeriodCurrencyCandidateEntityIds(netId, descriptor, allowedStaticIds, out List<int> candidateIds, out _);
            for (int c = 0; c < candidateIds.Count; c++)
            {
                int candidateId = candidateIds[c];
                if (candidateId <= 0)
                {
                    continue;
                }
                if (useAllowlist ? allowedStaticIds.Contains(candidateId) : this.TryLookupPeriodCurrencySale(currencyTypeId, candidateId, out _))
                {
                    isFestival = true;
                    matchedEntityId = candidateId;
                    return;
                }
            }
        }

        private void CollectPeriodCurrencyCandidateEntityIds(uint netId, string descriptor, HashSet<int> periodAllowlist, out List<int> candidateIds, out string diagnostics)
        {
            candidateIds = new List<int>(12);
            diagnostics = string.Empty;
            HashSet<int> seen = new HashSet<int>();

            int primaryStaticId = 0;
            if (this.TryResolveStaticIdForSell(netId, descriptor, out primaryStaticId, out string staticSource))
            {
                this.AddPeriodCurrencyCandidate(primaryStaticId, staticSource, seen, candidateIds, ref diagnostics);
            }

            int descriptorSubId = 0;
            if (this.TryExtractStaticIdFromAutoSellText(descriptor, out descriptorSubId))
            {
                this.AddPeriodCurrencyCandidate(descriptorSubId, "descriptor", seen, candidateIds, ref diagnostics);
            }

            if (this.TryExtractStaticIdFromAutoSellText(this.autoSellItemKey, out int keyId))
            {
                this.AddPeriodCurrencyCandidate(keyId, "matchKey", seen, candidateIds, ref diagnostics);
            }

            if (periodAllowlist == null || periodAllowlist.Count == 0)
            {
                if (primaryStaticId > 0 && this.TryDecodeEntityTypeIdFromStaticId(primaryStaticId, out int decodedTypeId))
                {
                    this.AddPeriodCurrencyCandidate(decodedTypeId, "decodedType", seen, candidateIds, ref diagnostics);
                }
            }
            if (primaryStaticId >= 1000)
            {
                this.AddPeriodCurrencyCandidate(primaryStaticId % 1000, "mod1000", seen, candidateIds, ref diagnostics);
                this.AddPeriodCurrencyCandidate(primaryStaticId % 10000, "mod10000", seen, candidateIds, ref diagnostics);
            }

            this.AddDerivedPeriodCurrencyTableEntityIds(primaryStaticId, descriptorSubId, periodAllowlist, seen, candidateIds, ref diagnostics);

            if (primaryStaticId <= 0 && this.TryResolveStaticIdFromLiveMonoBackpack(netId, out int liveStaticId))
            {
                this.AddPeriodCurrencyCandidate(liveStaticId, "liveMono", seen, candidateIds, ref diagnostics);
                if (this.TryDecodeEntityTypeIdFromStaticId(liveStaticId, out int liveDecoded))
                {
                    this.AddPeriodCurrencyCandidate(liveDecoded, "liveDecoded", seen, candidateIds, ref diagnostics);
                }

                int liveSubId = 0;
                if (this.TryExtractStaticIdFromAutoSellText(descriptor, out liveSubId))
                {
                    this.AddDerivedPeriodCurrencyTableEntityIds(liveStaticId, liveSubId, periodAllowlist, seen, candidateIds, ref diagnostics);
                }
            }
        }

        private void AddDerivedPeriodCurrencyTableEntityIds(int backpackStaticId, int descriptorSubId, HashSet<int> periodAllowlist, HashSet<int> seen, List<int> candidateIds, ref string diagnostics)
        {
            int subId = descriptorSubId > 0 ? descriptorSubId : (backpackStaticId > 0 ? backpackStaticId % 1000 : 0);
            if (subId <= 0)
            {
                return;
            }

            List<int> prefixes = this.GetPeriodCurrencyTableIdPrefixes(periodAllowlist);
            for (int i = 0; i < prefixes.Count; i++)
            {
                int prefix = prefixes[i];
                if (prefix <= 0)
                {
                    continue;
                }

                int derivedId = prefix + subId;
                if (periodAllowlist != null && periodAllowlist.Count > 0 && !periodAllowlist.Contains(derivedId))
                {
                    continue;
                }

                this.AddPeriodCurrencyCandidate(derivedId, "table" + prefix + "+" + subId, seen, candidateIds, ref diagnostics);
            }
        }

        private List<int> GetPeriodCurrencyTableIdPrefixes(HashSet<int> periodAllowlist)
        {
            List<int> prefixes = new List<int>(4);
            if (periodAllowlist != null && periodAllowlist.Count > 0)
            {
                HashSet<int> unique = new HashSet<int>();
                foreach (int entityId in periodAllowlist)
                {
                    if (entityId < 1000)
                    {
                        continue;
                    }

                    int prefix = (entityId / 1000) * 1000;
                    if (prefix > 0)
                    {
                        unique.Add(prefix);
                    }
                }

                prefixes.AddRange(unique);
                prefixes.Sort();
                if (prefixes.Count > 0)
                {
                    return prefixes;
                }
            }

            prefixes.Add(732000);
            prefixes.Add(769000);
            return prefixes;
        }

        private void AddPeriodCurrencyCandidate(int value, string label, HashSet<int> seen, List<int> candidateIds, ref string diagnostics)
        {
            if (value <= 0 || seen == null || candidateIds == null || !seen.Add(value))
            {
                return;
            }

            candidateIds.Add(value);
            diagnostics += (diagnostics.Length > 0 ? "," : "") + value + ":" + label;
        }

        private bool TryLookupPeriodCurrencySale(int currencyTypeId, int entityId, out string lookupPath)
        {
            lookupPath = string.Empty;
            if (currencyTypeId <= 0 || entityId <= 0)
            {
                return false;
            }

            if (!ModLoaderInfo.IsMelonLoader && this.TryLookupPeriodCurrencySaleIl2CppContains(currencyTypeId, entityId))
            {
                lookupPath = "il2cpp[ContainsKey]";
                return true;
            }

            if (this.TryLookupPeriodCurrencySaleAura(currencyTypeId, entityId))
            {
                lookupPath = "aura[PeriodCurrencySales]";
                return true;
            }

            if (this.TryLookupPeriodCurrencySaleManaged(currencyTypeId, entityId))
            {
                lookupPath = "managed[PeriodCurrencySales]";
                return true;
            }

            if (this.TryLookupPeriodCurrencySaleFlatTable(currencyTypeId, entityId, out lookupPath))
            {
                return true;
            }

            return false;
        }

        private bool TryLookupPeriodCurrencySaleFlatTable(int currencyTypeId, int entityId, out string lookupPath)
        {
            lookupPath = string.Empty;
            if (this.TryFlatTableContainsPeriodCurrencyEntityManaged(currencyTypeId, entityId))
            {
                lookupPath = "flatManaged";
                return true;
            }

            if (this.TryFlatTableContainsPeriodCurrencyEntityAura(currencyTypeId, entityId))
            {
                lookupPath = "flatAura";
                return true;
            }

            return false;
        }

        private bool TryFlatTableContainsPeriodCurrencyEntityManaged(int currencyTypeId, int entityId)
        {
            if (currencyTypeId <= 0 || entityId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    return false;
                }

                object flatTable = null;
                FieldInfo flatField = tableDataType.GetField("TablePeriodCurrencySales", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (flatField != null)
                {
                    flatTable = flatField.GetValue(null);
                }
                else
                {
                    PropertyInfo flatProp = tableDataType.GetProperty("TablePeriodCurrencySales", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (flatProp != null)
                    {
                        flatTable = flatProp.GetValue(null, null);
                    }
                }

                if (flatTable is IDictionary dictionary)
                {
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (this.TryReadPeriodCurrencySaleRowManaged(entry.Value, out int rowCurrency, out int rowEntityId, out _)
                            && rowCurrency == currencyTypeId
                            && rowEntityId == entityId)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryFlatTableContainsPeriodCurrencyEntityAura(int currencyTypeId, int entityId)
        {
            if (currencyTypeId <= 0 || entityId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out _))
            {
                return false;
            }

            IntPtr flatTable = IntPtr.Zero;
            string[] staticFieldNames = new[] { "TablePeriodCurrencySales", "tablePeriodCurrencySales", "_tablePeriodCurrencySales" };
            for (int i = 0; i < staticFieldNames.Length; i++)
            {
                if (this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldNames[i], out flatTable) && flatTable != IntPtr.Zero)
                {
                    break;
                }
            }

            if (flatTable == IntPtr.Zero)
            {
                return false;
            }

            // Every member read below allocates mono-side (boxed returns), so unpinned row
            // pointers race the moving sgen GC -> stale IntPtr -> native AV without a crashlog
            // (the 2026-07-10 Auto Sell death). Pin the rows for the whole walk, and each value
            // pulled OUT of its pinned pair box for its reads (pinning is not transitive).
            List<IntPtr> rows = new List<IntPtr>(512);
            List<uint> rowPins = new List<uint>(512);
            if (!this.TryEnumerateAuraMonoCollectionItems(flatTable, rows, rowPins))
            {
                FreeAuraMonoPins(rowPins);
                return false;
            }

            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr rowObj = rows[i];
                    if (rowObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr valueObj = rowObj;
                    uint valuePin = 0U;
                    if (this.TryGetAuraMonoDictionaryEntryIntKey(rowObj, out int pairKey, out IntPtr pairValue) && pairValue != IntPtr.Zero)
                    {
                        valueObj = pairValue;
                        valuePin = AuraMonoPinNew(valueObj);
                    }
                    else if (this.TryGetMonoObjectMember(rowObj, "Value", out IntPtr boxedValue) && boxedValue != IntPtr.Zero)
                    {
                        valueObj = boxedValue;
                        valuePin = AuraMonoPinNew(valueObj);
                    }

                    try
                    {
                        if (this.TryReadPeriodCurrencySaleRowAura(valueObj, out int rowCurrency, out int rowEntityId, out _)
                            && rowCurrency == currencyTypeId
                            && rowEntityId == entityId)
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(valuePin);
                    }
                }

                return false;
            }
            finally
            {
                FreeAuraMonoPins(rowPins);
            }
        }

        private bool TryLookupPeriodCurrencySaleManaged(int currencyTypeId, int entityId)
        {
            try
            {
                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    return false;
                }

                PropertyInfo prop = tableDataType.GetProperty("PeriodCurrencySales", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                {
                    return false;
                }

                if (!(prop.GetValue(null, null) is System.Collections.IDictionary byCurrency))
                {
                    return false;
                }

                IDictionary byEntity = null;
                foreach (DictionaryEntry bucket in byCurrency)
                {
                    int keyValue = 0;
                    try { keyValue = Convert.ToInt32(bucket.Key); } catch { continue; }
                    if (keyValue == currencyTypeId)
                    {
                        byEntity = bucket.Value as IDictionary;
                        break;
                    }
                }

                return byEntity != null && byEntity.Contains(entityId);
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryLookupPeriodCurrencySaleIl2CppContains(int currencyTypeId, int entityId)
        {
            if (currencyTypeId <= 0 || entityId <= 0)
            {
                return false;
            }

            IntPtr tableClass = this.TryFindIl2CppClass("TableData", "EcsClient", "Il2CppEcsClient", string.Empty);
            if (tableClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(tableClass, "PeriodCurrencySales");
            if (field == IntPtr.Zero)
            {
                return false;
            }

            IntPtr salesObj = IL2CPP.il2cpp_field_get_value_object(field, IntPtr.Zero);
            if (salesObj == IntPtr.Zero || !this.TryIl2CppDictionaryTryGetIntObject(salesObj, currencyTypeId, out IntPtr entityMapObj) || entityMapObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryIl2CppDictionaryContainsIntKey(entityMapObj, entityId);
        }

        private bool TryLookupPeriodCurrencySaleAura(int currencyTypeId, int staticId)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAuraMonoTableDataPeriodCurrencySales(out IntPtr salesMapObj))
            {
                return false;
            }

            if (!this.TryFindAuraMonoPeriodCurrencyEntityMap(currencyTypeId, out IntPtr entityMapObj, out uint entityMapPin) || entityMapObj == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                // ContainsKey boxes its key mono-side before the invoke — keep the map pinned.
                return this.TryAuraMonoDictionaryContainsIntKey(entityMapObj, staticId);
            }
            finally
            {
                AuraMonoPinFree(entityMapPin);
            }
        }

        private bool TryGetAuraMonoTableDataPeriodCurrencySales(out IntPtr salesMapObj)
        {
            salesMapObj = IntPtr.Zero;
            if (!this.TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out _))
            {
                return false;
            }

            IntPtr getSalesMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "get_PeriodCurrencySales", 0);
            if (getSalesMethod == IntPtr.Zero)
            {
                string[] staticFieldNames = new[]
                {
                    "PeriodCurrencySales", "periodCurrencySales", "_periodCurrencySales", "TablePeriodCurrencySales"
                };

                for (int i = 0; i < staticFieldNames.Length; i++)
                {
                    if (this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldNames[i], out salesMapObj) && salesMapObj != IntPtr.Zero)
                    {
                        return true;
                    }
                }

                return false;
            }

            IntPtr exc = IntPtr.Zero;
            salesMapObj = auraMonoRuntimeInvoke(getSalesMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && salesMapObj != IntPtr.Zero;
        }

        // Session cache: the allowlist is static design-table data, but it was re-walked from
        // the live mono tables on EVERY sell tick — each walk a fresh window for the moving-GC
        // stale-pointer crash and a few ms of wasted invokes. Resolve once per currency.
        // Callers only read the returned set (Contains/foreach), so sharing the instance is safe.
        private readonly Dictionary<int, HashSet<int>> periodCurrencyAllowlistCache = new Dictionary<int, HashSet<int>>();

        // entityId -> TablePeriodCurrencySale.maxSellCount (0 = unlimited) per currency, captured
        // by the SAME table walk that builds the allowlist above (static design-table data — the
        // two caches are always written together). Entries the walk could not read stay absent
        // and are treated as unlimited by the split (today's pre-clamp behavior).
        private readonly Dictionary<int, Dictionary<int, int>> periodCurrencyMaxSellCountCache = new Dictionary<int, Dictionary<int, int>>();

        private bool TryGetPeriodCurrencyMaxSellCounts(int currencyTypeId, out Dictionary<int, int> maxSellByEntityId)
        {
            maxSellByEntityId = null;
            return currencyTypeId > 0
                && this.periodCurrencyMaxSellCountCache.TryGetValue(currencyTypeId, out maxSellByEntityId)
                && maxSellByEntityId != null;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIds(int currencyTypeId, out HashSet<int> allowedStaticIds)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0)
            {
                return false;
            }
            if (this.periodCurrencyAllowlistCache.TryGetValue(currencyTypeId, out HashSet<int> cachedAllowlist)
                && cachedAllowlist != null && cachedAllowlist.Count > 0)
            {
                allowedStaticIds = cachedAllowlist;
                return true;
            }
            string source = string.Empty;
            Dictionary<int, int> maxSellByEntityId = new Dictionary<int, int>();
            if (this.TryGetAllowedPeriodCurrencyStaticIdsAura(currencyTypeId, out allowedStaticIds, maxSellByEntityId) && allowedStaticIds.Count > 0)
            {
                source = "nestedAura";
            }
            else if (this.TryGetAllowedPeriodCurrencyStaticIdsManaged(currencyTypeId, out allowedStaticIds, maxSellByEntityId) && allowedStaticIds.Count > 0)
            {
                source = "nestedManaged";
            }
            else if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTable(currencyTypeId, out allowedStaticIds, out string flatSource, maxSellByEntityId) && allowedStaticIds.Count > 0)
            {
                source = flatSource;
            }
            if (allowedStaticIds.Count > 0)
            {
                this.periodCurrencyAllowlistCache[currencyTypeId] = allowedStaticIds;
                this.periodCurrencyMaxSellCountCache[currencyTypeId] = maxSellByEntityId;
                int cappedCount = 0;
                foreach (int max in maxSellByEntityId.Values)
                {
                    if (max > 0)
                    {
                        cappedCount++;
                    }
                }
                this.AutoSellLogSellResult("Period allowlist via " + source + " count=" + allowedStaticIds.Count + " capped=" + cappedCount + "/" + maxSellByEntityId.Count + " sample=" + this.FormatAutoSellIdSample(allowedStaticIds, 8) + " currency=" + currencyTypeId + " (cached for session)");
                return true;
            }
            allowedStaticIds = new HashSet<int>();
            return false;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTable(int currencyTypeId, out HashSet<int> allowedStaticIds, out string source, Dictionary<int, int> maxSellByEntityId)
        {
            allowedStaticIds = new HashSet<int>();
            source = string.Empty;
            if (currencyTypeId <= 0)
            {
                return false;
            }

            if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableManaged(currencyTypeId, allowedStaticIds, maxSellByEntityId))
            {
                source = "flatManaged";
                return allowedStaticIds.Count > 0;
            }

            if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableAura(currencyTypeId, allowedStaticIds, maxSellByEntityId))
            {
                source = "flatAura";
                return allowedStaticIds.Count > 0;
            }

            return false;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableManaged(int currencyTypeId, HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId)
        {
            if (allowedStaticIds == null || currencyTypeId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    return false;
                }

                object flatTable = null;
                FieldInfo flatField = tableDataType.GetField("TablePeriodCurrencySales", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (flatField != null)
                {
                    flatTable = flatField.GetValue(null);
                }
                else
                {
                    PropertyInfo flatProp = tableDataType.GetProperty("TablePeriodCurrencySales", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (flatProp != null)
                    {
                        flatTable = flatProp.GetValue(null, null);
                    }
                }

                return this.TryAddPeriodCurrencyEntityIdsFromFlatTableObject(flatTable, currencyTypeId, allowedStaticIds, maxSellByEntityId);
            }
            catch (Exception ex)
            {
                this.AutoSellLog("flatManaged allowlist exception: " + ex.Message);
                return false;
            }
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableAura(int currencyTypeId, HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId)
        {
            if (allowedStaticIds == null || currencyTypeId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out _))
            {
                return false;
            }

            IntPtr flatTable = IntPtr.Zero;
            string[] staticFieldNames = new[] { "TablePeriodCurrencySales", "tablePeriodCurrencySales", "_tablePeriodCurrencySales" };
            for (int i = 0; i < staticFieldNames.Length; i++)
            {
                if (this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldNames[i], out flatTable) && flatTable != IntPtr.Zero)
                {
                    break;
                }
            }

            if (flatTable == IntPtr.Zero)
            {
                return false;
            }

            // The crash path of the 2026-07-10 no-crashlog Auto Sell death: this walk kept
            // unpinned row pointers while every read below allocated mono-side, so a mid-loop
            // sgen GC pass relocated the rows out from under the loop. Pin the rows for the
            // walk, and each row value pulled out of its pinned pair box (not transitive).
            List<IntPtr> rows = new List<IntPtr>(512);
            List<uint> rowPins = new List<uint>(512);
            if (!this.TryEnumerateAuraMonoCollectionItems(flatTable, rows, rowPins) || rows.Count == 0)
            {
                FreeAuraMonoPins(rowPins);
                return false;
            }

            try
            {
                bool foundAny = false;
                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr rowObj = rows[i];
                    if (rowObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr valueObj = rowObj;
                    uint valuePin = 0U;
                    if (this.TryGetAuraMonoDictionaryEntryIntKey(rowObj, out int pairKey, out IntPtr pairValue) && pairValue != IntPtr.Zero)
                    {
                        valueObj = pairValue;
                        valuePin = AuraMonoPinNew(valueObj);
                    }
                    else if (this.TryGetMonoObjectMember(rowObj, "Value", out IntPtr boxedValue) && boxedValue != IntPtr.Zero)
                    {
                        valueObj = boxedValue;
                        valuePin = AuraMonoPinNew(valueObj);
                    }

                    bool rowOk;
                    int rowCurrency;
                    int entityId;
                    int rowMaxSell;
                    try
                    {
                        rowOk = this.TryReadPeriodCurrencySaleRowAura(valueObj, out rowCurrency, out entityId, out rowMaxSell);
                    }
                    finally
                    {
                        AuraMonoPinFree(valuePin);
                    }

                    if (!rowOk || entityId <= 0)
                    {
                        continue;
                    }

                    if (rowCurrency == currencyTypeId)
                    {
                        allowedStaticIds.Add(entityId);
                        if (maxSellByEntityId != null && rowMaxSell >= 0)
                        {
                            maxSellByEntityId[entityId] = rowMaxSell;
                        }
                        foundAny = true;
                    }
                }

                return foundAny;
            }
            finally
            {
                FreeAuraMonoPins(rowPins);
            }
        }

        // maxSellCount: TablePeriodCurrencySale per-period sell cap (0 = unlimited); -1 when the
        // row exposed neither the maxSellCount property nor the _maxSellCount backing byte
        // (callers treat unknown as unlimited — today's pre-clamp behavior).
        private bool TryReadPeriodCurrencySaleRowAura(IntPtr rowObj, out int currency, out int entityId, out int maxSellCount)
        {
            currency = 0;
            entityId = 0;
            maxSellCount = -1;
            if (rowObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoInt32Member(rowObj, "entityId", out entityId))
            {
                this.TryGetMonoIntMember(rowObj, "entityId", out entityId);
            }

            if (!this.TryGetMonoInt32Member(rowObj, "currency", out currency))
            {
                this.TryGetMonoIntMember(rowObj, "currency", out currency);
            }

            if (currency <= 0 && this.TryGetMonoInt32Member(rowObj, "_currency", out int rawCurrency))
            {
                currency = rawCurrency;
            }

            // maxSellCount is a property (get_maxSellCount, int) backed by the byte _maxSellCount —
            // same property-then-backing-field pattern as the _currency read above.
            if (!this.TryGetMonoInt32Member(rowObj, "maxSellCount", out maxSellCount)
                && !this.TryGetMonoIntMember(rowObj, "maxSellCount", out maxSellCount)
                && !this.TryGetMonoInt32Member(rowObj, "_maxSellCount", out maxSellCount))
            {
                maxSellCount = -1;
            }

            return entityId > 0;
        }

        private bool TryAddPeriodCurrencyEntityIdsFromFlatTableObject(object flatTable, int currencyTypeId, HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId)
        {
            if (flatTable == null || allowedStaticIds == null || currencyTypeId <= 0)
            {
                return false;
            }

            bool foundAny = false;
            if (flatTable is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!this.TryReadPeriodCurrencySaleRowManaged(entry.Value, out int rowCurrency, out int entityId, out int rowMaxSell))
                    {
                        continue;
                    }

                    if (rowCurrency == currencyTypeId && entityId > 0)
                    {
                        allowedStaticIds.Add(entityId);
                        if (maxSellByEntityId != null && rowMaxSell >= 0)
                        {
                            maxSellByEntityId[entityId] = rowMaxSell;
                        }
                        foundAny = true;
                    }
                }

                return foundAny;
            }

            if (flatTable is IEnumerable enumerable)
            {
                foreach (object row in enumerable)
                {
                    if (!this.TryReadPeriodCurrencySaleRowManaged(row, out int rowCurrency, out int entityId, out int rowMaxSell))
                    {
                        continue;
                    }

                    if (rowCurrency == currencyTypeId && entityId > 0)
                    {
                        allowedStaticIds.Add(entityId);
                        if (maxSellByEntityId != null && rowMaxSell >= 0)
                        {
                            maxSellByEntityId[entityId] = rowMaxSell;
                        }
                        foundAny = true;
                    }
                }
            }

            return foundAny;
        }

        // maxSellCount: per-period sell cap (0 = unlimited); -1 when unreadable (treated unlimited).
        private bool TryReadPeriodCurrencySaleRowManaged(object row, out int currency, out int entityId, out int maxSellCount)
        {
            currency = 0;
            entityId = 0;
            maxSellCount = -1;
            if (row == null)
            {
                return false;
            }

            if (!this.TryReadObjectInt(row, "entityId", out entityId) || entityId <= 0)
            {
                this.TryReadObjectInt(row, "EntityId", out entityId);
            }

            if (!this.TryReadObjectInt(row, "currency", out currency) || currency <= 0)
            {
                this.TryReadObjectInt(row, "Currency", out currency);
            }

            if (!this.TryReadObjectInt(row, "maxSellCount", out maxSellCount)
                && !this.TryReadObjectInt(row, "_maxSellCount", out maxSellCount))
            {
                maxSellCount = -1;
            }

            return entityId > 0;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsManaged(int currencyTypeId, out HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    return false;
                }

                PropertyInfo prop = tableDataType.GetProperty("PeriodCurrencySales", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                {
                    return false;
                }

                if (!(prop.GetValue(null, null) is IDictionary byCurrency))
                {
                    return false;
                }

                IDictionary byEntity = null;
                foreach (DictionaryEntry bucket in byCurrency)
                {
                    int keyValue = 0;
                    try { keyValue = Convert.ToInt32(bucket.Key); } catch { continue; }
                    if (keyValue == currencyTypeId)
                    {
                        byEntity = bucket.Value as IDictionary;
                        break;
                    }
                }

                if (byEntity == null)
                {
                    return false;
                }

                foreach (DictionaryEntry e in byEntity)
                {
                    try
                    {
                        int entityId = Convert.ToInt32(e.Key);
                        if (entityId > 0)
                        {
                            allowedStaticIds.Add(entityId);
                            if (maxSellByEntityId != null
                                && this.TryReadPeriodCurrencySaleRowManaged(e.Value, out _, out _, out int rowMaxSell)
                                && rowMaxSell >= 0)
                            {
                                maxSellByEntityId[entityId] = rowMaxSell;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return allowedStaticIds.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsAura(int currencyTypeId, out HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryFindAuraMonoPeriodCurrencyEntityMap(currencyTypeId, out IntPtr entityMapObj, out uint entityMapPin) || entityMapObj == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return this.TryCollectPeriodSaleEntityIdsFromAuraEntityMap(entityMapObj, allowedStaticIds, maxSellByEntityId) && allowedStaticIds.Count > 0;
            }
            finally
            {
                AuraMonoPinFree(entityMapPin);
            }
        }

        private unsafe bool TryGetAllowedPeriodCurrencyStaticIdsIl2Cpp(int currencyTypeId, out HashSet<int> allowedStaticIds)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0)
            {
                return false;
            }

            IntPtr tableClass = this.TryFindIl2CppClass("TableData", "EcsClient", "Il2CppEcsClient", string.Empty);
            if (tableClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(tableClass, "PeriodCurrencySales");
            if (field == IntPtr.Zero)
            {
                return false;
            }

            IntPtr salesObj = IL2CPP.il2cpp_field_get_value_object(field, IntPtr.Zero);
            if (salesObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryCollectPeriodCurrencyEntityIdsFromIl2CppMap(salesObj, currencyTypeId, allowedStaticIds);
        }

        private unsafe bool TryCollectPeriodCurrencyEntityIdsFromIl2CppMap(IntPtr mapObj, int currencyTypeId, HashSet<int> allowedStaticIds)
        {
            if (mapObj == IntPtr.Zero || allowedStaticIds == null || currencyTypeId <= 0)
            {
                return false;
            }

            IntPtr mapClass = IL2CPP.il2cpp_object_get_class(mapObj);
            if (mapClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getEnumerator = IL2CPP.il2cpp_class_get_method_from_name(mapClass, "GetEnumerator", 0);
            if (getEnumerator == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr enumerator = IL2CPP.il2cpp_runtime_invoke(getEnumerator, mapObj, null, ref exc);
            if (exc != IntPtr.Zero || enumerator == IntPtr.Zero)
            {
                return false;
            }

            IntPtr enumeratorClass = IL2CPP.il2cpp_object_get_class(enumerator);
            if (enumeratorClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr moveNext = IL2CPP.il2cpp_class_get_method_from_name(enumeratorClass, "MoveNext", 0);
            IntPtr getCurrent = IL2CPP.il2cpp_class_get_method_from_name(enumeratorClass, "get_Current", 0);
            if (moveNext == IntPtr.Zero || getCurrent == IntPtr.Zero)
            {
                return false;
            }

            bool foundBucket = false;
            for (int guard = 0; guard < 4096; guard++)
            {
                exc = IntPtr.Zero;
                IntPtr movedObj = IL2CPP.il2cpp_runtime_invoke(moveNext, enumerator, null, ref exc);
                if (exc != IntPtr.Zero || movedObj == IntPtr.Zero)
                {
                    break;
                }

                IntPtr unboxed = IL2CPP.il2cpp_object_unbox(movedObj);
                if (unboxed == IntPtr.Zero || Marshal.ReadByte(unboxed) == 0)
                {
                    break;
                }

                exc = IntPtr.Zero;
                IntPtr currentObj = IL2CPP.il2cpp_runtime_invoke(getCurrent, enumerator, null, ref exc);
                if (exc != IntPtr.Zero || currentObj == IntPtr.Zero)
                {
                    continue;
                }

                int bucketCurrency = this.TryReadIl2CppKeyValuePairIntKey(currentObj, out IntPtr entityMap);
                if (bucketCurrency != currencyTypeId || entityMap == IntPtr.Zero)
                {
                    continue;
                }

                foundBucket = this.TryCollectPeriodCurrencyEntityIdsFromIl2CppEntityMap(entityMap, allowedStaticIds) || foundBucket;
            }

            return foundBucket && allowedStaticIds.Count > 0;
        }

        private unsafe bool TryCollectPeriodCurrencyEntityIdsFromIl2CppEntityMap(IntPtr mapObj, HashSet<int> allowedStaticIds)
        {
            if (mapObj == IntPtr.Zero || allowedStaticIds == null)
            {
                return false;
            }

            IntPtr mapClass = IL2CPP.il2cpp_object_get_class(mapObj);
            if (mapClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getEnumerator = IL2CPP.il2cpp_class_get_method_from_name(mapClass, "GetEnumerator", 0);
            if (getEnumerator == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr enumerator = IL2CPP.il2cpp_runtime_invoke(getEnumerator, mapObj, null, ref exc);
            if (exc != IntPtr.Zero || enumerator == IntPtr.Zero)
            {
                return false;
            }

            IntPtr enumeratorClass = IL2CPP.il2cpp_object_get_class(enumerator);
            IntPtr moveNext = IL2CPP.il2cpp_class_get_method_from_name(enumeratorClass, "MoveNext", 0);
            IntPtr getCurrent = IL2CPP.il2cpp_class_get_method_from_name(enumeratorClass, "get_Current", 0);
            if (moveNext == IntPtr.Zero || getCurrent == IntPtr.Zero)
            {
                return false;
            }

            bool foundAny = false;
            for (int guard = 0; guard < 4096; guard++)
            {
                exc = IntPtr.Zero;
                IntPtr movedObj = IL2CPP.il2cpp_runtime_invoke(moveNext, enumerator, null, ref exc);
                if (exc != IntPtr.Zero || movedObj == IntPtr.Zero)
                {
                    break;
                }

                IntPtr unboxed = IL2CPP.il2cpp_object_unbox(movedObj);
                if (unboxed == IntPtr.Zero || Marshal.ReadByte(unboxed) == 0)
                {
                    break;
                }

                exc = IntPtr.Zero;
                IntPtr currentObj = IL2CPP.il2cpp_runtime_invoke(getCurrent, enumerator, null, ref exc);
                if (exc != IntPtr.Zero || currentObj == IntPtr.Zero)
                {
                    continue;
                }

                int keyInt = this.TryReadIl2CppKeyValuePairIntKey(currentObj, out IntPtr valueObj);
                if (this.TryExtractPeriodSaleEntityIdFromIl2CppObject(keyInt, valueObj, out int entityId) && entityId > 0)
                {
                    allowedStaticIds.Add(entityId);
                    foundAny = true;
                }
            }

            return foundAny;
        }

        private unsafe bool TryExtractPeriodSaleEntityIdFromIl2CppObject(int keyInt, IntPtr valueObj, out int entityId)
        {
            entityId = 0;
            if (valueObj != IntPtr.Zero)
            {
                entityId = this.TryReadIl2CppObjectIntField(valueObj, "entityId");
                if (entityId <= 0)
                {
                    entityId = this.TryReadIl2CppObjectIntField(valueObj, "EntityId");
                }

                if (entityId <= 0)
                {
                    entityId = this.TryReadIl2CppObjectIntField(valueObj, "staticId");
                }

                if (entityId <= 0)
                {
                    entityId = this.TryReadIl2CppObjectIntField(valueObj, "StaticId");
                }
            }

            if (entityId <= 0 && keyInt > 0)
            {
                entityId = keyInt;
            }

            return entityId > 0;
        }

        private bool TryCollectPeriodCurrencyEntityIdsFromAuraMap(IntPtr mapObj, int currencyTypeId, HashSet<int> allowedStaticIds)
        {
            if (mapObj == IntPtr.Zero || allowedStaticIds == null || currencyTypeId <= 0)
            {
                return false;
            }

            List<IntPtr> buckets = new List<IntPtr>(64);
            List<uint> bucketPins = new List<uint>(64);
            if (!this.TryEnumerateAuraMonoCollectionItems(mapObj, buckets, bucketPins) || buckets.Count == 0)
            {
                FreeAuraMonoPins(bucketPins);
                return false;
            }

            bool foundAny = false;
            try
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    IntPtr bucketObj = buckets[i];
                    if (bucketObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoDictionaryEntryIntKey(bucketObj, out int bucketCurrency, out IntPtr entityMapObj))
                    {
                        continue;
                    }

                    if (bucketCurrency != currencyTypeId || entityMapObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    // The nested map came out of the pinned pair box — pin it across the
                    // allocating walk below (pinning is not transitive).
                    uint entityMapPin = AuraMonoPinNew(entityMapObj);
                    try
                    {
                        foundAny = this.TryCollectPeriodSaleEntityIdsFromAuraEntityMap(entityMapObj, allowedStaticIds) || foundAny;
                    }
                    finally
                    {
                        AuraMonoPinFree(entityMapPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(bucketPins);
            }

            return foundAny;
        }

        // maxSellByEntityId (optional): filled with entityId -> maxSellCount for every row whose
        // cap could be read off the pinned row value (see TryExtractPeriodSaleEntityIdFromAuraValue).
        private bool TryCollectPeriodSaleEntityIdsFromAuraEntityMap(IntPtr entityMapObj, HashSet<int> allowedStaticIds, Dictionary<int, int> maxSellByEntityId = null)
        {
            if (entityMapObj == IntPtr.Zero || allowedStaticIds == null)
            {
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>(128);
            List<uint> entryPins = new List<uint>(128);
            if (!this.TryEnumerateAuraMonoCollectionItems(entityMapObj, entries, entryPins) || entries.Count == 0)
            {
                FreeAuraMonoPins(entryPins);
                return false;
            }

            bool foundAny = false;
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entryObj = entries[i];
                    if (entryObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryGetAuraMonoDictionaryEntryIntKey(entryObj, out int keyInt, out IntPtr valueObj))
                    {
                        // The row value came out of the pinned entry box — pin it across the
                        // multi-field reads below (pinning is not transitive).
                        uint valuePin = AuraMonoPinNew(valueObj);
                        bool extracted;
                        int entityId;
                        int rowMaxSell;
                        try
                        {
                            extracted = this.TryExtractPeriodSaleEntityIdFromAuraValue(keyInt, valueObj, out entityId, out rowMaxSell);
                        }
                        finally
                        {
                            AuraMonoPinFree(valuePin);
                        }

                        if (extracted)
                        {
                            allowedStaticIds.Add(entityId);
                            if (maxSellByEntityId != null && rowMaxSell >= 0)
                            {
                                maxSellByEntityId[entityId] = rowMaxSell;
                            }
                            foundAny = true;
                            continue;
                        }
                    }

                    int fallbackKey = 0;
                    if (this.TryGetMonoInt32Member(entryObj, "Key", out fallbackKey) || this.TryGetMonoIntMember(entryObj, "Key", out fallbackKey))
                    {
                        // Key-only fallback: no row object, so the cap stays unknown (absent from
                        // the map = treated as unlimited by the split).
                        if (this.TryExtractPeriodSaleEntityIdFromAuraValue(fallbackKey, IntPtr.Zero, out int fallbackEntityId, out _))
                        {
                            allowedStaticIds.Add(fallbackEntityId);
                            foundAny = true;
                        }
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(entryPins);
            }

            return foundAny;
        }

        // maxSellCount: the row's per-period sell cap (0 = unlimited), -1 when unreadable or when
        // there is no row object (key-only fallback). Caller must keep valueObj pinned across
        // this call — every member read below allocates mono-side.
        private bool TryExtractPeriodSaleEntityIdFromAuraValue(int keyInt, IntPtr valueObj, out int entityId, out int maxSellCount)
        {
            entityId = 0;
            maxSellCount = -1;
            if (valueObj != IntPtr.Zero)
            {
                if (!this.TryGetMonoInt32Member(valueObj, "entityId", out entityId))
                {
                    this.TryGetMonoIntMember(valueObj, "entityId", out entityId);
                }

                if (!this.TryGetMonoInt32Member(valueObj, "maxSellCount", out maxSellCount)
                    && !this.TryGetMonoIntMember(valueObj, "maxSellCount", out maxSellCount)
                    && !this.TryGetMonoInt32Member(valueObj, "_maxSellCount", out maxSellCount))
                {
                    maxSellCount = -1;
                }

                if (entityId <= 0 && !this.TryGetMonoInt32Member(valueObj, "EntityId", out entityId))
                {
                    this.TryGetMonoIntMember(valueObj, "EntityId", out entityId);
                }

                if (entityId <= 0 && !this.TryGetMonoInt32Member(valueObj, "staticId", out entityId))
                {
                    this.TryGetMonoIntMember(valueObj, "staticId", out entityId);
                }

                if (entityId <= 0 && !this.TryGetMonoInt32Member(valueObj, "StaticId", out entityId))
                {
                    this.TryGetMonoIntMember(valueObj, "StaticId", out entityId);
                }
            }

            if (entityId <= 0 && keyInt > 0)
            {
                entityId = keyInt;
            }

            return entityId > 0;
        }

        // On success entityMapPin is a pinned gchandle rooting entityMapObj (the nested
        // per-entity dictionary): the caller keeps using the raw pointer across allocating
        // invokes, so it must stay pinned until the caller AuraMonoPinFree()s it.
        private bool TryFindAuraMonoPeriodCurrencyEntityMap(int currencyTypeId, out IntPtr entityMapObj, out uint entityMapPin)
        {
            entityMapObj = IntPtr.Zero;
            entityMapPin = 0U;
            if (currencyTypeId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out _))
            {
                return false;
            }

            IntPtr salesMapObj = IntPtr.Zero;
            string[] staticFieldNames = new[]
            {
                "PeriodCurrencySales", "periodCurrencySales", "_periodCurrencySales", "TablePeriodCurrencySales"
            };

            for (int i = 0; i < staticFieldNames.Length; i++)
            {
                if (this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldNames[i], out salesMapObj) && salesMapObj != IntPtr.Zero)
                {
                    break;
                }
            }

            if (salesMapObj == IntPtr.Zero && !this.TryGetAuraMonoTableDataPeriodCurrencySales(out salesMapObj))
            {
                return false;
            }

            if (salesMapObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> buckets = new List<IntPtr>(64);
            List<uint> bucketPins = new List<uint>(64);
            if (!this.TryEnumerateAuraMonoCollectionItems(salesMapObj, buckets, bucketPins) || buckets.Count == 0)
            {
                FreeAuraMonoPins(bucketPins);
                return false;
            }

            try
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    IntPtr bucketObj = buckets[i];
                    if (bucketObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoDictionaryEntryIntKey(bucketObj, out int bucketCurrency, out IntPtr mapObj))
                    {
                        continue;
                    }

                    if (bucketCurrency == currencyTypeId && mapObj != IntPtr.Zero)
                    {
                        entityMapObj = mapObj;
                        entityMapPin = AuraMonoPinNew(mapObj);
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                FreeAuraMonoPins(bucketPins);
            }
        }

        // ---- Per-period sold counts (PeriodStallRecordComponent.SellRecord) ----
        // Class/method pointers live for the image lifetime (same convention as the DailyClaims
        // EcsService fields); the SERVICE INSTANCE pointer is re-fetched on every read and never
        // cached across frames.
        private IntPtr periodStallDataCenterClassPtr = IntPtr.Zero;
        private IntPtr periodStallTryGetRecordMethodPtr = IntPtr.Zero;

        // Live read of how many of each token-sellable entityId the player already sold this
        // period, for one currency — the exact read the game's own BattlePassSellPanel.GetSlotData
        // performs (ilspy-dumps/XDTGameUI/XDTGame.UI.Panel/BattlePassSellPanel.cs:226-260):
        //   EcsService.TryGet<SellPeriodSellGameDataCenter> →
        //   TryGetPeriodStallRecordComponent((NetworkEntityRef.RefId)self, currency, out hasValue)
        //   → PeriodStallRecordComponent.SellRecord (Dictionary<int,int> entityId → sold).
        // NetworkEntityRef.RefId is a readonly struct wrapping the raw uint netId (it has an
        // implicit conversion FROM uint), so the self player netId passed byref IS the RefId —
        // no EcsEntity/GetSelfEntity dance needed. The method returns
        // `ref readonly PeriodStallRecordComponent`; this build's mono (mono-2.0-sgen.dll,
        // dotnet/runtime 8.0 fork) dereferences byref returns inside the runtime-invoke wrapper
        // and hands back a BOXED PeriodStallRecordComponent, so SellRecord is a plain field read
        // off the pinned box. Fail-closed: any miss returns false and the split clamps assuming
        // sold=0 (a valid first-sell clamp — strictly better than the old unclamped send).
        private unsafe bool TryGetPeriodStallSoldCounts(int currencyTypeId, Dictionary<int, int> soldByEntityId, out string status)
        {
            status = "unavailable";
            if (currencyTypeId <= 0 || soldByEntityId == null)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (this.periodStallDataCenterClassPtr == IntPtr.Zero)
                {
                    this.periodStallDataCenterClassPtr = this.FindAuraMonoClassByFullName(
                        "Sazabi.Scene.XDT.Scene.Server.SceneGameLogic.Sell.PeriodSell.SellPeriodSellGameDataCenter");
                    if (this.periodStallDataCenterClassPtr == IntPtr.Zero)
                    {
                        this.periodStallDataCenterClassPtr = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            "Sazabi.Scene.XDT.Scene.Server.SceneGameLogic.Sell.PeriodSell",
                            "SellPeriodSellGameDataCenter");
                    }
                }

                if (this.periodStallDataCenterClassPtr == IntPtr.Zero)
                {
                    status = "SellPeriodSellGameDataCenter class unresolved";
                    return false;
                }

                // Proven generic inflate: EcsService.TryGet<SellPeriodSellGameDataCenter>.
                if (!this.TryDailyClaimsAuraMonoEcsTryGet(this.periodStallDataCenterClassPtr, false, out IntPtr serviceObj, out string tryGetStatus)
                    || serviceObj == IntPtr.Zero)
                {
                    status = "EcsService.TryGet miss: " + tryGetStatus;
                    return false;
                }

                if (!this.TryResolveSelfPlayerNetId(out uint selfPlayerNetId) || selfPlayerNetId == 0U)
                {
                    status = "self player netId unresolved";
                    return false;
                }

                uint servicePin = AuraMonoPinNew(serviceObj);
                try
                {
                    if (this.periodStallTryGetRecordMethodPtr == IntPtr.Zero)
                    {
                        IntPtr runtimeClass = auraMonoObjectGetClass(serviceObj);
                        this.periodStallTryGetRecordMethodPtr = runtimeClass != IntPtr.Zero
                            ? this.FindAuraMonoMethodOnHierarchy(runtimeClass, "TryGetPeriodStallRecordComponent", 3)
                            : IntPtr.Zero;
                    }

                    if (this.periodStallTryGetRecordMethodPtr == IntPtr.Zero)
                    {
                        status = "TryGetPeriodStallRecordComponent method unresolved";
                        return false;
                    }

                    // Signature: (in NetworkEntityRef.RefId player, int currencyId, out bool hasValue).
                    // Byref args take a pointer to the value data: RefId = { uint _id } so &netId IS
                    // the byref RefId; the out bool gets an 8-byte zeroed slot (mono writes 1 byte
                    // through the pointer — narrower than a pointer, so no slot-corruption risk).
                    uint refIdValue = selfPlayerNetId;
                    int currencyArg = currencyTypeId;
                    long hasValueSlot = 0L;
                    IntPtr* invokeArgs = stackalloc IntPtr[3];
                    invokeArgs[0] = (IntPtr)(&refIdValue);
                    invokeArgs[1] = (IntPtr)(&currencyArg);
                    invokeArgs[2] = (IntPtr)(&hasValueSlot);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxedRecord = auraMonoRuntimeInvoke(this.periodStallTryGetRecordMethodPtr, serviceObj, (IntPtr)invokeArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "TryGetPeriodStallRecordComponent raised an exception";
                        return false;
                    }

                    if (*(byte*)&hasValueSlot == 0)
                    {
                        // No stall record component for this currency = nothing sold this period —
                        // an authoritative EMPTY result, not a failure.
                        status = "no record yet (sold=0)";
                        return true;
                    }

                    if (boxedRecord == IntPtr.Zero)
                    {
                        status = "record invoke returned null";
                        return false;
                    }

                    uint recordPin = AuraMonoPinNew(boxedRecord);
                    try
                    {
                        if (!this.TryGetMonoObjectMember(boxedRecord, "SellRecord", out IntPtr sellRecordObj) || sellRecordObj == IntPtr.Zero)
                        {
                            // Component present but the dictionary is null — the game reads it with
                            // `SellRecord?.TryGetValue(...)`, i.e. nothing sold yet.
                            status = "SellRecord null (sold=0)";
                            return true;
                        }

                        uint dictPin = AuraMonoPinNew(sellRecordObj);
                        try
                        {
                            List<IntPtr> entries = new List<IntPtr>(32);
                            List<uint> entryPins = new List<uint>(32);
                            if (!this.TryEnumerateAuraMonoCollectionItems(sellRecordObj, entries, entryPins))
                            {
                                FreeAuraMonoPins(entryPins);
                                status = "SellRecord enumeration failed";
                                return false;
                            }

                            try
                            {
                                for (int i = 0; i < entries.Count; i++)
                                {
                                    IntPtr entryObj = entries[i];
                                    if (entryObj == IntPtr.Zero)
                                    {
                                        continue;
                                    }

                                    if (!this.TryGetAuraMonoDictionaryEntryIntKey(entryObj, out int entityId, out IntPtr valueObj)
                                        || entityId <= 0 || valueObj == IntPtr.Zero)
                                    {
                                        continue;
                                    }

                                    // The boxed int came out of the pinned entry box — pin it across
                                    // the unbox read (pinning is not transitive).
                                    uint valuePin = AuraMonoPinNew(valueObj);
                                    try
                                    {
                                        IntPtr raw = auraMonoObjectUnbox(valueObj);
                                        if (raw != IntPtr.Zero)
                                        {
                                            int soldCount = Marshal.ReadInt32(raw);
                                            if (soldCount > 0)
                                            {
                                                soldByEntityId[entityId] = soldCount;
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        AuraMonoPinFree(valuePin);
                                    }
                                }
                            }
                            finally
                            {
                                FreeAuraMonoPins(entryPins);
                            }

                            status = "ok entries=" + soldByEntityId.Count;
                            return true;
                        }
                        finally
                        {
                            AuraMonoPinFree(dictPin);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(recordPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(servicePin);
                }
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

    }
}
