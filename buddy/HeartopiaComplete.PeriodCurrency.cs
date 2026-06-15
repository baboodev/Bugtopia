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

        private void ClassifyPeriodCurrencyStack(int currencyTypeId, uint netId, string descriptor, HashSet<int> allowedStaticIds, bool useAllowlist, out bool isFestival)
        {
            isFestival = false;
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

#if BEPINEX
            if (this.TryLookupPeriodCurrencySaleIl2CppContains(currencyTypeId, entityId))
            {
                lookupPath = "il2cpp[ContainsKey]";
                return true;
            }
#endif

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
                        if (this.TryReadPeriodCurrencySaleRowManaged(entry.Value, out int rowCurrency, out int rowEntityId)
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

            List<IntPtr> rows = new List<IntPtr>(512);
            if (!this.TryEnumerateAuraMonoCollectionItems(flatTable, rows))
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                IntPtr rowObj = rows[i];
                if (rowObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr valueObj = rowObj;
                if (this.TryGetAuraMonoDictionaryEntryIntKey(rowObj, out int pairKey, out IntPtr pairValue) && pairValue != IntPtr.Zero)
                {
                    valueObj = pairValue;
                }
                else if (this.TryGetMonoObjectMember(rowObj, "Value", out IntPtr boxedValue) && boxedValue != IntPtr.Zero)
                {
                    valueObj = boxedValue;
                }

                if (this.TryReadPeriodCurrencySaleRowAura(valueObj, out int rowCurrency, out int rowEntityId)
                    && rowCurrency == currencyTypeId
                    && rowEntityId == entityId)
                {
                    return true;
                }
            }

            return false;
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

            if (!this.TryFindAuraMonoPeriodCurrencyEntityMap(currencyTypeId, out IntPtr entityMapObj) || entityMapObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryAuraMonoDictionaryContainsIntKey(entityMapObj, staticId);
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

        private bool TryGetAllowedPeriodCurrencyStaticIds(int currencyTypeId, out HashSet<int> allowedStaticIds)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0)
            {
                return false;
            }
            string source = string.Empty;
            if (this.TryGetAllowedPeriodCurrencyStaticIdsAura(currencyTypeId, out allowedStaticIds) && allowedStaticIds.Count > 0)
            {
                source = "nestedAura";
            }
            else if (this.TryGetAllowedPeriodCurrencyStaticIdsManaged(currencyTypeId, out allowedStaticIds) && allowedStaticIds.Count > 0)
            {
                source = "nestedManaged";
            }
            else if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTable(currencyTypeId, out allowedStaticIds, out string flatSource) && allowedStaticIds.Count > 0)
            {
                source = flatSource;
            }
            if (allowedStaticIds.Count > 0)
            {
                this.AutoSellLogSellResult("Period allowlist via " + source + " count=" + allowedStaticIds.Count + " sample=" + this.FormatAutoSellIdSample(allowedStaticIds, 8) + " currency=" + currencyTypeId);
                return true;
            }
            allowedStaticIds = new HashSet<int>();
            return false;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTable(int currencyTypeId, out HashSet<int> allowedStaticIds, out string source)
        {
            allowedStaticIds = new HashSet<int>();
            source = string.Empty;
            if (currencyTypeId <= 0)
            {
                return false;
            }

            if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableManaged(currencyTypeId, allowedStaticIds))
            {
                source = "flatManaged";
                return allowedStaticIds.Count > 0;
            }

            if (this.TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableAura(currencyTypeId, allowedStaticIds))
            {
                source = "flatAura";
                return allowedStaticIds.Count > 0;
            }

            return false;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableManaged(int currencyTypeId, HashSet<int> allowedStaticIds)
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

                return this.TryAddPeriodCurrencyEntityIdsFromFlatTableObject(flatTable, currencyTypeId, allowedStaticIds);
            }
            catch (Exception ex)
            {
                this.AutoSellLog("flatManaged allowlist exception: " + ex.Message);
                return false;
            }
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsFromFlatTableAura(int currencyTypeId, HashSet<int> allowedStaticIds)
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

            List<IntPtr> rows = new List<IntPtr>(512);
            if (!this.TryEnumerateAuraMonoCollectionItems(flatTable, rows) || rows.Count == 0)
            {
                return false;
            }

            bool foundAny = false;
            for (int i = 0; i < rows.Count; i++)
            {
                IntPtr rowObj = rows[i];
                if (rowObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr valueObj = rowObj;
                if (this.TryGetAuraMonoDictionaryEntryIntKey(rowObj, out int pairKey, out IntPtr pairValue) && pairValue != IntPtr.Zero)
                {
                    valueObj = pairValue;
                }
                else if (this.TryGetMonoObjectMember(rowObj, "Value", out IntPtr boxedValue) && boxedValue != IntPtr.Zero)
                {
                    valueObj = boxedValue;
                }

                if (!this.TryReadPeriodCurrencySaleRowAura(valueObj, out int rowCurrency, out int entityId) || entityId <= 0)
                {
                    continue;
                }

                if (rowCurrency == currencyTypeId)
                {
                    allowedStaticIds.Add(entityId);
                    foundAny = true;
                }
            }

            return foundAny;
        }

        private bool TryReadPeriodCurrencySaleRowAura(IntPtr rowObj, out int currency, out int entityId)
        {
            currency = 0;
            entityId = 0;
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

            return entityId > 0;
        }

        private bool TryAddPeriodCurrencyEntityIdsFromFlatTableObject(object flatTable, int currencyTypeId, HashSet<int> allowedStaticIds)
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
                    if (!this.TryReadPeriodCurrencySaleRowManaged(entry.Value, out int rowCurrency, out int entityId))
                    {
                        continue;
                    }

                    if (rowCurrency == currencyTypeId && entityId > 0)
                    {
                        allowedStaticIds.Add(entityId);
                        foundAny = true;
                    }
                }

                return foundAny;
            }

            if (flatTable is IEnumerable enumerable)
            {
                foreach (object row in enumerable)
                {
                    if (!this.TryReadPeriodCurrencySaleRowManaged(row, out int rowCurrency, out int entityId))
                    {
                        continue;
                    }

                    if (rowCurrency == currencyTypeId && entityId > 0)
                    {
                        allowedStaticIds.Add(entityId);
                        foundAny = true;
                    }
                }
            }

            return foundAny;
        }

        private bool TryReadPeriodCurrencySaleRowManaged(object row, out int currency, out int entityId)
        {
            currency = 0;
            entityId = 0;
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

            return entityId > 0;
        }

        private bool TryGetAllowedPeriodCurrencyStaticIdsManaged(int currencyTypeId, out HashSet<int> allowedStaticIds)
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

        private bool TryGetAllowedPeriodCurrencyStaticIdsAura(int currencyTypeId, out HashSet<int> allowedStaticIds)
        {
            allowedStaticIds = new HashSet<int>();
            if (currencyTypeId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryFindAuraMonoPeriodCurrencyEntityMap(currencyTypeId, out IntPtr entityMapObj) || entityMapObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryCollectPeriodSaleEntityIdsFromAuraEntityMap(entityMapObj, allowedStaticIds) && allowedStaticIds.Count > 0;
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
            if (!this.TryEnumerateAuraMonoCollectionItems(mapObj, buckets) || buckets.Count == 0)
            {
                return false;
            }

            bool foundAny = false;
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

                foundAny = this.TryCollectPeriodSaleEntityIdsFromAuraEntityMap(entityMapObj, allowedStaticIds) || foundAny;
            }

            return foundAny;
        }

        private bool TryCollectPeriodSaleEntityIdsFromAuraEntityMap(IntPtr entityMapObj, HashSet<int> allowedStaticIds)
        {
            if (entityMapObj == IntPtr.Zero || allowedStaticIds == null)
            {
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>(128);
            if (!this.TryEnumerateAuraMonoCollectionItems(entityMapObj, entries) || entries.Count == 0)
            {
                return false;
            }

            bool foundAny = false;
            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entryObj = entries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetAuraMonoDictionaryEntryIntKey(entryObj, out int keyInt, out IntPtr valueObj)
                    && this.TryExtractPeriodSaleEntityIdFromAuraValue(keyInt, valueObj, out int entityId))
                {
                    allowedStaticIds.Add(entityId);
                    foundAny = true;
                    continue;
                }

                int fallbackKey = 0;
                if (this.TryGetMonoInt32Member(entryObj, "Key", out fallbackKey) || this.TryGetMonoIntMember(entryObj, "Key", out fallbackKey))
                {
                    if (this.TryExtractPeriodSaleEntityIdFromAuraValue(fallbackKey, IntPtr.Zero, out int fallbackEntityId))
                    {
                        allowedStaticIds.Add(fallbackEntityId);
                        foundAny = true;
                    }
                }
            }

            return foundAny;
        }

        private bool TryExtractPeriodSaleEntityIdFromAuraValue(int keyInt, IntPtr valueObj, out int entityId)
        {
            entityId = 0;
            if (valueObj != IntPtr.Zero)
            {
                if (!this.TryGetMonoInt32Member(valueObj, "entityId", out entityId))
                {
                    this.TryGetMonoIntMember(valueObj, "entityId", out entityId);
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

        private bool TryFindAuraMonoPeriodCurrencyEntityMap(int currencyTypeId, out IntPtr entityMapObj)
        {
            entityMapObj = IntPtr.Zero;
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
            if (!this.TryEnumerateAuraMonoCollectionItems(salesMapObj, buckets) || buckets.Count == 0)
            {
                return false;
            }

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
                    return true;
                }
            }

            return false;
        }

    }
}
