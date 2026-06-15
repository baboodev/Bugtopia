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
        internal unsafe void ModWarehouseBypassTickBagOpenState(out bool bypassOpen)
        {
            bypassOpen = this.ModTryWarehouseBagOpenForBypassCached();
        }

        internal unsafe void ModTryEnableWarehouseTabViaMono(bool itemSelected)
        {
            if (!this.warehouseBypassEnabled || this.warehouseMonoTabGiveUp)
            {
                return;
            }

            if (ModTryUnityFindActiveBagPanel(out GameObject unityBag)
                && ModTryUnityBagPanelLooksStaleClosed(unityBag))
            {
                return;
            }

            if (!this.ModTryWarehouseBagOpenForBypassCached())
            {
                return;
            }

            if (this.ModTryResolveAuraMonoBagPanel(out IntPtr bagPanelLifeObj) && bagPanelLifeObj != IntPtr.Zero
                && this.ModTryAuraMonoReadBagPanelLifeCycle(bagPanelLifeObj, out int lifeCycle)
                && lifeCycle != BagPanelLifeCycleOpened)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool maintainOnly = this.warehouseMonoTabUnlockCommitted;
            float throttle = maintainOnly ? 1.5f : 0.25f;
            if (now < this.warehouseMonoTabNextAttemptAt)
            {
                return;
            }

            this.warehouseMonoTabNextAttemptAt = now + throttle;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return;
                }

                if (!this.ModTryResolveAuraMonoBagPanel(out IntPtr bagPanelObj) || bagPanelObj == IntPtr.Zero)
                {
                    return;
                }

                if (!this.TryInvokeAuraMonoZeroArg(bagPanelObj, out IntPtr nodesObj, "get_nodes") || nodesObj == IntPtr.Zero)
                {
                    return;
                }

                if (!this.TryReadAuraMonoObjectField(nodesObj, out IntPtr tabBarObj, "tabBar_widget") || tabBarObj == IntPtr.Zero)
                {
                    return;
                }

                IntPtr tabBarClass = auraMonoObjectGetClass(tabBarObj);
                IntPtr getChildAt = this.FindAuraMonoMethodOnHierarchy(tabBarClass, "GetChildAt", 1);
                if (getChildAt == IntPtr.Zero)
                {
                    return;
                }

                int warehouseIndex = 1;
                IntPtr exc = IntPtr.Zero;
                IntPtr* childArgs = stackalloc IntPtr[1];
                childArgs[0] = (IntPtr)(&warehouseIndex);
                IntPtr tabWidgetObj = auraMonoRuntimeInvoke(getChildAt, tabBarObj, (IntPtr)childArgs, ref exc);
                if (exc != IntPtr.Zero || tabWidgetObj == IntPtr.Zero)
                {
                    return;
                }

                if (!maintainOnly)
                {
                    IntPtr tabWidgetClass = auraMonoObjectGetClass(tabWidgetObj);
                    IntPtr setInteractable = this.FindAuraMonoMethodOnHierarchy(tabWidgetClass, "SetInteractable", 1);
                    if (setInteractable == IntPtr.Zero)
                    {
                        return;
                    }

                    int interactable = 1;
                    exc = IntPtr.Zero;
                    IntPtr* interactArgs = stackalloc IntPtr[1];
                    interactArgs[0] = (IntPtr)(&interactable);
                    auraMonoRuntimeInvoke(setInteractable, tabWidgetObj, (IntPtr)interactArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        return;
                    }
                }

                if (!this.warehouseMonoTabUnlockedLogged)
                {
                    this.warehouseMonoTabUnlockedLogged = true;
                    this.warehouseMonoTabUnlockCommitted = true;
                    WarehouseBypassFeature.LogMonoTabUnlockOnce(
                        "[WarehouseBypass] Mono tab unlock OK — TabWidget.SetInteractable(true) on warehouse tab.");
                }

                if (itemSelected)
                {
                    IntPtr bagPanelClass = auraMonoObjectGetClass(bagPanelObj);
                    IntPtr setBtnFrame = bagPanelClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(bagPanelClass, "SetBtnFrame", 1) : IntPtr.Zero;
                    if (setBtnFrame != IntPtr.Zero)
                    {
                        int frame = 1;
                        exc = IntPtr.Zero;
                        IntPtr* frameArgs = stackalloc IntPtr[1];
                        frameArgs[0] = (IntPtr)(&frame);
                        auraMonoRuntimeInvoke(setBtnFrame, bagPanelObj, (IntPtr)frameArgs, ref exc);
                        if (exc == IntPtr.Zero && !this.warehouseMonoMoveButtonLogged)
                        {
                            this.warehouseMonoMoveButtonLogged = true;
                            ModLogger.Msg("[WarehouseBypass] SetBtnFrame(1) — Store/Carry for selected item.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.warehouseMonoTabGiveUp = true;
                ModLogger.Msg("[WarehouseBypass] Mono tab unlock failed: " + ex.Message);
            }
        }

        internal void ModWarehouseOnBagClosed()
        {
            this.warehouseMonoTabNextAttemptAt = -999f;
            this.warehouseMonoTabUnlockCommitted = false;
            this.warehouseMonoMoveButtonLogged = false;
            this.warehouseBagOpenBypassCacheFrame = -1;
            WarehouseBypassFeature.ResetWarehouseBagSession();
            WarehouseBypassFeature.ResetWarehouseTabMonoWarmup();
        }

        internal static bool ModTryUnityWarehouseBagShouldSkipMonoProbe()
        {
            if (!ModTryUnityFindActiveBagPanel(out GameObject bag))
            {
                return true;
            }

            return ModTryUnityBagPanelLooksStaleClosed(bag);
        }

        internal unsafe bool ModTryWarehouseBagOpenForBypass()
        {
            return this.ModTryWarehouseBagOpenForBypassCached();
        }

        internal unsafe bool ModTryWarehouseBagOpenForBypassCached()
        {
            if (Time.frameCount == this.warehouseBagOpenBypassCacheFrame)
            {
                return this.warehouseBagOpenBypassCacheValue;
            }

            this.warehouseBagOpenBypassCacheValue = this.ModTryWarehouseBagOpenForBypassCore();
            this.warehouseBagOpenBypassCacheFrame = Time.frameCount;
            return this.warehouseBagOpenBypassCacheValue;
        }

        private unsafe bool ModTryWarehouseBagOpenForBypassCore()
        {
            if (ModTryUnityWarehouseBagShouldSkipMonoProbe())
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.ModTryResolveAuraMonoBagPanel(out IntPtr bagPanelObj) || bagPanelObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.ModTryAuraMonoBagPanelObjectIsBagPanel(bagPanelObj))
            {
                return false;
            }

            if (!this.ModTryAuraMonoReadBagPanelLifeCycle(bagPanelObj, out int lifeCycle))
            {
                return false;
            }

            if (lifeCycle == BagPanelLifeCycleClosing || lifeCycle == BagPanelLifeCycleClosed || lifeCycle != BagPanelLifeCycleOpened)
            {
                return false;
            }

            return this.ModTryAuraMonoBagPanelUiIsVisible(bagPanelObj);
        }

        private bool TrySendTransferBatch(Dictionary<uint, int> netIdToCounts, int sourceStorageType, out string error)
        {
            error = string.Empty;
            if (netIdToCounts == null || netIdToCounts.Count == 0)
            {
                error = "Nothing selected";
                return false;
            }

            if (netIdToCounts.Count > TransferBatchMaxCount)
            {
                error = "Too many stacks (max " + TransferBatchMaxCount + ")";
                return false;
            }

            int targetStorageType = this.GetTransferTargetStorageType(sourceStorageType);
            bool sent = this.TryInvokeMoveBatchBackpackItemsAuraMono(netIdToCounts, targetStorageType)
                || this.TryInvokeMoveBatchBackpackItemsManaged(netIdToCounts, targetStorageType);
            if (!sent)
            {
                error = "MoveBatchBackpackItems unavailable";
                return false;
            }

            string destLabel = targetStorageType == 2 ? "Warehouse" : "Bag";
            ModLogger.Msg("[TRANSFER] Sent " + netIdToCounts.Count + " stack(s) to " + destLabel + " (targetStorageType=" + targetStorageType + ")");
            return true;
        }

        private Dictionary<uint, int> BuildTransferItemMapForSend(out string error)
        {
            error = string.Empty;
            Dictionary<uint, int> map = new Dictionary<uint, int>();
            int sourceStorageType = this.GetTransferScanStorageType();

            if (this.transferBatch.Count > 0)
            {
                foreach (KeyValuePair<uint, int> pair in this.transferBatch)
                {
                    if (pair.Key == 0U || pair.Value <= 0)
                    {
                        continue;
                    }

                    if (!this.TryGetTransferStackCount(pair.Key, out int maxCount, out bool isLocked) || isLocked)
                    {
                        continue;
                    }

                    int qty = Mathf.Clamp(pair.Value, 1, maxCount);
                    map[pair.Key] = qty;
                }
            }
            else
            {
                TransferItemEntry selected = this.GetSelectedTransferItemEntry();
                if (selected == null || selected.NetId == 0U)
                {
                    error = "Select an item first";
                    return null;
                }

                if (selected.IsLocked)
                {
                    error = "Selected item is locked";
                    return null;
                }

                int qty = Mathf.Clamp(this.transferQty, 1, Math.Max(1, selected.Count));
                map[selected.NetId] = qty;
            }

            if (map.Count == 0)
            {
                error = "No transferable stacks";
                return null;
            }

            if (map.Count > TransferBatchMaxCount)
            {
                error = "Too many stacks (max " + TransferBatchMaxCount + ")";
                return null;
            }

            return map;
        }

        private bool TryGetTransferStackCount(uint netId, out int count, out bool isLocked)
        {
            count = 0;
            isLocked = false;
            if (this.transferItems == null)
            {
                return false;
            }

            for (int i = 0; i < this.transferItems.Count; i++)
            {
                TransferItemEntry entry = this.transferItems[i];
                if (entry != null && entry.NetId == netId)
                {
                    count = Math.Max(1, entry.Count);
                    isLocked = entry.IsLocked;
                    return true;
                }
            }

            return false;
        }

        private void ExecuteTransferItems()
        {
            Dictionary<uint, int> map = this.BuildTransferItemMapForSend(out string error);
            if (map == null)
            {
                this.transferStatus = error;
                this.AddMenuNotification(error, new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (map.Count > TransferBatchMaxCount)
            {
                this.ExecuteTransferItemsChunked();
                return;
            }

            int sourceStorageType = this.GetTransferScanStorageType();
            if (!this.TrySendTransferBatch(map, sourceStorageType, out error))
            {
                this.transferStatus = error;
                this.AddMenuNotification(error, new Color(1f, 0.55f, 0.55f));
                return;
            }

            string dest = this.GetTransferTargetStorageType(sourceStorageType) == 2 ? "Warehouse" : "Bag";
            int totalQty = 0;
            foreach (int qty in map.Values)
            {
                totalQty += qty;
            }

            string successMessage = "Sent " + map.Count + " stack(s), qty " + totalQty + " -> " + dest;
            this.transferStatus = successMessage;
            this.AddMenuNotification(successMessage, new Color(0.45f, 1f, 0.55f));
            this.FinalizeTransferSend(map, successMessage);
        }

        private void ExecuteTransferItemsChunked()
        {
            Dictionary<uint, int> fullMap = this.BuildTransferItemMapForSend(out string error);
            if (fullMap == null)
            {
                this.transferStatus = error;
                this.AddMenuNotification(error, new Color(1f, 0.55f, 0.55f));
                return;
            }

            int sourceStorageType = this.GetTransferScanStorageType();
            List<uint> keys = new List<uint>(fullMap.Keys);
            int sentStacks = 0;
            int sentQty = 0;
            for (int offset = 0; offset < keys.Count; offset += TransferBatchMaxCount)
            {
                Dictionary<uint, int> chunk = new Dictionary<uint, int>();
                int end = Math.Min(keys.Count, offset + TransferBatchMaxCount);
                for (int i = offset; i < end; i++)
                {
                    uint netId = keys[i];
                    chunk[netId] = fullMap[netId];
                }

                if (!this.TrySendTransferBatch(chunk, sourceStorageType, out error))
                {
                    this.transferStatus = error + " (after " + sentStacks + " stack(s))";
                    this.AddMenuNotification(this.transferStatus, new Color(1f, 0.75f, 0.45f));
                    return;
                }

                sentStacks += chunk.Count;
                foreach (int qty in chunk.Values)
                {
                    sentQty += qty;
                }
            }

            string dest = this.GetTransferTargetStorageType(sourceStorageType) == 2 ? "Warehouse" : "Bag";
            string successMessage = "Sent " + sentStacks + " stack(s), qty " + sentQty + " -> " + dest;
            this.transferStatus = successMessage;
            this.AddMenuNotification(successMessage, new Color(0.45f, 1f, 0.55f));
            this.FinalizeTransferSend(fullMap, successMessage);
        }

        private void FinalizeTransferSend(Dictionary<uint, int> sentMap, string statusMessage)
        {
            this.ApplyOptimisticTransferToItemList(sentMap);
            this.transferBatch.Clear();
            this.transferStatus = statusMessage;
            this.ScheduleTransferListRescan();
        }

        private void ApplyOptimisticTransferToItemList(Dictionary<uint, int> sentMap)
        {
            if (this.transferItems == null || sentMap == null || sentMap.Count == 0)
            {
                return;
            }

            uint? selectedNetId = null;
            if (this.selectedTransferIndex >= 0 && this.selectedTransferIndex < this.transferItems.Count)
            {
                TransferItemEntry selected = this.transferItems[this.selectedTransferIndex];
                if (selected != null && selected.NetId != 0U)
                {
                    selectedNetId = selected.NetId;
                }
            }

            for (int i = this.transferItems.Count - 1; i >= 0; i--)
            {
                TransferItemEntry entry = this.transferItems[i];
                if (entry == null || !sentMap.TryGetValue(entry.NetId, out int sentQty) || sentQty <= 0)
                {
                    continue;
                }

                entry.Count -= sentQty;
                if (entry.Count <= 0)
                {
                    this.transferItems.RemoveAt(i);
                    this.transferBatch.Remove(entry.NetId);
                }
                else if (this.transferBatch.TryGetValue(entry.NetId, out int batchQty))
                {
                    this.transferBatch[entry.NetId] = Mathf.Min(batchQty, entry.Count);
                }
            }

            this.selectedTransferIndex = -1;
            if (selectedNetId.HasValue)
            {
                for (int i = 0; i < this.transferItems.Count; i++)
                {
                    TransferItemEntry entry = this.transferItems[i];
                    if (entry != null && entry.NetId == selectedNetId.Value)
                    {
                        this.selectedTransferIndex = i;
                        this.transferQty = Mathf.Clamp(this.transferQty, 1, Math.Max(1, entry.Count));
                        break;
                    }
                }
            }
        }

        private void ScheduleTransferListRescan()
        {
            this.transferPendingRescanAt = Time.unscaledTime + 0.35f;
            this.transferPendingRescanRetries = 6;
        }

        private void ProcessPendingTransferListRescan()
        {
            if (this.transferPendingRescanRetries <= 0)
            {
                return;
            }

            if (!this.showMenu || this.selectedTab != 6)
            {
                this.transferPendingRescanRetries = 0;
                return;
            }

            if (Time.unscaledTime < this.transferPendingRescanAt)
            {
                return;
            }

            string statusMessage = this.transferStatus;
            this.transferItems = this.ScanTransferItems(updateStatus: false);
            this.transferStatus = statusMessage;
            this.transferPendingRescanRetries--;
            this.transferPendingRescanAt = Time.unscaledTime + 0.3f;
        }

        private int GetTransferScanStorageType()
        {
            return Mathf.Clamp(this.transferScanSource, 0, 1) == 1 ? 2 : 1;
        }

        private string GetTransferScanSourceLabel()
        {
            return this.transferScanSourceLabels[Mathf.Clamp(this.transferScanSource, 0, this.transferScanSourceLabels.Length - 1)];
        }

        private int GetTransferTargetStorageType(int sourceStorageType)
        {
            return sourceStorageType == 1 ? 2 : 1;
        }

        private List<TransferItemEntry> ScanTransferItems(bool updateStatus = true)
        {
            try
            {
                List<TransferItemEntry> items = new List<TransferItemEntry>();
                int inspected = 0;
                this.CollectTransferItemEntriesMono(items, ref inspected);
                this.PrimeTransferItemTextureCache(items);
                items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (updateStatus)
                {
                    this.transferStatus = items.Count > 0
                        ? (this.GetTransferScanSourceLabel() + ": " + items.Count + " stack(s), inspected=" + inspected)
                        : ("No items in " + this.GetTransferScanSourceLabel().ToLowerInvariant());
                }
                ModLogger.Msg("[TRANSFER] Scan " + this.GetTransferScanSourceLabel() + " found " + items.Count + " stack(s), inspected=" + inspected);
                return items;
            }
            catch (Exception ex)
            {
                if (updateStatus)
                {
                    this.transferStatus = "Scan failed: " + ex.Message;
                }
                ModLogger.Msg("[TRANSFER] Scan exception: " + ex.GetType().Name + ": " + ex.Message);
                return new List<TransferItemEntry>();
            }
        }

        private void CollectTransferItemEntriesMono(List<TransferItemEntry> items, ref int inspected)
        {
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return;
                }

                int storageTypeValue = this.GetTransferScanStorageType();
                bool fromBackpack = storageTypeValue == 1;
                IntPtr exc = IntPtr.Zero;
                IntPtr itemListObj;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeValue);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                }
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return;
                }

                List<IntPtr> backpackItems = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, backpackItems))
                {
                    return;
                }

                foreach (IntPtr itemObj in backpackItems)
                {
                    inspected++;
                    if (itemObj == IntPtr.Zero || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    int count = 1;
                    int staticId = 0;
                    int entityType = 0;
                    int starRate = 0;
                    bool isLocked = false;
                    this.TryGetDirectBackpackItemCount(itemObj, out count);
                    this.TryGetDirectBackpackItemStaticId(itemObj, out staticId);
                    this.TryGetDirectBackpackItemEntityType(itemObj, out entityType);
                    this.TryGetDirectBackpackItemIsLocked(itemObj, out isLocked);
                    if (!this.TryGetAutoSellQualityComponentStar(netId, out starRate))
                    {
                        this.TryGetDirectBackpackItemStarRate(itemObj, out starRate);
                    }

                    string descriptor = this.GetDirectBackpackItemDescriptor(itemObj);

                    if (starRate <= 0 && this.IsAutoSellBirdPhotoDescriptor(descriptor))
                    {
                        if (this.TryGetAutoSellCachedUiStar(descriptor, count, out int uiStar))
                        {
                            starRate = uiStar;
                        }
                        else
                        {
                            int step = this.GetDirectBackpackItemStep(itemObj);
                            if (step >= 1 && step <= 5)
                            {
                                starRate = step;
                            }
                        }
                    }

                    this.AddTransferItemEntry(items, descriptor, netId, count, staticId, entityType, starRate, fromBackpack, isLocked);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TRANSFER] Mono scan exception: " + ex.Message);
            }
        }

        private void AddTransferItemEntry(List<TransferItemEntry> items, string descriptor, uint netId, int count, int staticId, int entityType, int starRate, bool fromBackpack, bool isLocked)
        {
            if (netId == 0U || count <= 0)
            {
                return;
            }

            string matchKey = this.ExtractAutoSellMatchKeyFromDescriptor(descriptor);
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                matchKey = staticId > 0 ? staticId.ToString() : netId.ToString();
            }

            int normalizedStar = this.NormalizeAutoSellStarRateForDescriptor(starRate, descriptor);
            if (normalizedStar <= 0 && this.TryGetAutoSellCachedUiStar(matchKey, count, out int uiStar))
            {
                normalizedStar = uiStar;
            }

            string spriteName = this.GetAutoSellSpriteNameFromMatchKey(matchKey);
            string displayName = this.ResolveBagItemDisplayName(matchKey, staticId);
            this.RememberRadarStaticIdIconMapping(staticId, spriteName);
            items.Add(new TransferItemEntry
            {
                SpriteName = spriteName,
                DisplayName = displayName,
                MatchKey = matchKey,
                NetId = netId,
                Count = Math.Max(1, count),
                StaticId = staticId,
                EntityType = entityType,
                StarRate = normalizedStar,
                IsLocked = isLocked,
                FromBackpack = fromBackpack
            });
        }

        private void PrimeTransferItemTextureCache(List<TransferItemEntry> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            List<AutoSellBagItemEntry> proxy = new List<AutoSellBagItemEntry>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                TransferItemEntry entry = items[i];
                if (entry == null)
                {
                    continue;
                }

                proxy.Add(new AutoSellBagItemEntry
                {
                    SpriteName = entry.SpriteName,
                    MatchKey = entry.MatchKey,
                    DisplayName = entry.DisplayName,
                    NetId = entry.NetId,
                    Count = entry.Count,
                    StaticId = entry.StaticId,
                    EntityType = entry.EntityType,
                    StarRate = entry.StarRate,
                    FromBackpack = entry.FromBackpack,
                    FromWarehouse = !entry.FromBackpack
                });
            }

            this.PrimeAutoSellItemTextureCache(proxy);
        }

        private bool TryGetTransferItemTexture(TransferItemEntry entry, out Texture2D texture)
        {
            texture = null;
            if (entry == null)
            {
                return false;
            }

            return this.TryGetAutoSellItemTexture(new AutoSellBagItemEntry
            {
                SpriteName = entry.SpriteName,
                MatchKey = entry.MatchKey,
                DisplayName = entry.DisplayName,
                NetId = entry.NetId,
                Count = entry.Count,
                StaticId = entry.StaticId,
                EntityType = entry.EntityType,
                StarRate = entry.StarRate,
                FromBackpack = entry.FromBackpack,
                FromWarehouse = !entry.FromBackpack
            }, out texture);
        }

        private TransferItemEntry GetSelectedTransferItemEntry()
        {
            if (this.transferItems == null || this.selectedTransferIndex < 0 || this.selectedTransferIndex >= this.transferItems.Count)
            {
                return null;
            }

            return this.transferItems[this.selectedTransferIndex];
        }

        private int GetTransferTilePickQuantity(TransferItemEntry entry, int itemIndex, out bool showOnTile)
        {
            showOnTile = false;
            if (entry == null || entry.NetId == 0U)
            {
                return 1;
            }

            int maxQty = Math.Max(1, entry.Count);
            if (this.transferBatch.TryGetValue(entry.NetId, out int batchQty))
            {
                showOnTile = true;
                return Mathf.Clamp(batchQty, 1, maxQty);
            }

            if (this.selectedTransferIndex == itemIndex)
            {
                showOnTile = true;
                return Mathf.Clamp(this.transferQty, 1, maxQty);
            }

            return 1;
        }

        private void SetTransferTilePickQuantity(TransferItemEntry entry, int itemIndex, int quantity)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return;
            }

            if (quantity <= 0)
            {
                if (this.transferMultiSelectMode && this.transferBatch.Remove(entry.NetId))
                {
                    this.transferQty = 1;
                    if (this.selectedTransferIndex == itemIndex)
                    {
                        this.selectedTransferIndex = -1;
                    }
                    this.ClearTransferQtyHold();
                }
                else if (!this.transferMultiSelectMode)
                {
                    this.transferQty = 1;
                }
                return;
            }

            int maxQty = Math.Max(1, entry.Count);
            int qty = Mathf.Clamp(quantity, 1, maxQty);
            this.transferQty = qty;
            if (this.transferMultiSelectMode && this.transferBatch.ContainsKey(entry.NetId))
            {
                this.transferBatch[entry.NetId] = qty;
            }
        }

        private void AdjustTransferTilePickQuantity(TransferItemEntry entry, int itemIndex, int delta)
        {
            if (entry == null)
            {
                return;
            }

            int current = this.GetTransferTilePickQuantity(entry, itemIndex, out _);
            this.SetTransferTilePickQuantity(entry, itemIndex, current + delta);
        }

        private void ClearTransferQtyHold()
        {
            this.transferQtyHoldDirection = 0;
            this.transferQtyHoldNetId = 0U;
            this.transferQtyHoldItemIndex = -1;
        }

        private void BeginTransferQtyHold(int direction, TransferItemEntry entry, int itemIndex)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return;
            }

            this.transferQtyHoldDirection = direction;
            this.transferQtyHoldNetId = entry.NetId;
            this.transferQtyHoldItemIndex = itemIndex;
            this.transferQtyHoldStartedAt = Time.unscaledTime;
            // First step is applied by the click; first repeat after one interval.
            this.transferQtyHoldLastStepAt = Time.unscaledTime;
        }

        private void UpdateTransferQtyHoldRepeat()
        {
            if (this.transferQtyHoldDirection == 0)
            {
                return;
            }

            if (!this.showMenu || this.selectedTab != 6)
            {
                this.ClearTransferQtyHold();
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                this.ClearTransferQtyHold();
                return;
            }

            if (this.transferItems == null
                || this.transferQtyHoldItemIndex < 0
                || this.transferQtyHoldItemIndex >= this.transferItems.Count)
            {
                this.ClearTransferQtyHold();
                return;
            }

            if (this.selectedTransferIndex != this.transferQtyHoldItemIndex)
            {
                this.ClearTransferQtyHold();
                return;
            }

            TransferItemEntry entry = this.transferItems[this.transferQtyHoldItemIndex];
            if (entry == null || entry.NetId != this.transferQtyHoldNetId)
            {
                this.ClearTransferQtyHold();
                return;
            }

            float heldSeconds = Time.unscaledTime - this.transferQtyHoldStartedAt;
            if (heldSeconds < TransferQtyHoldRepeatDelay)
            {
                return;
            }

            float repeatSeconds = heldSeconds - TransferQtyHoldRepeatDelay;
            float interval = repeatSeconds >= TransferQtyHoldFastAfterSeconds ? TransferQtyHoldFastInterval : TransferQtyHoldSlowInterval;
            if (Time.unscaledTime - this.transferQtyHoldLastStepAt < interval)
            {
                return;
            }

            this.AdjustTransferTilePickQuantity(entry, this.transferQtyHoldItemIndex, this.transferQtyHoldDirection);
            this.transferQtyHoldLastStepAt = Time.unscaledTime;
        }

        private void DrawTransferQtyStepButton(Rect rect, string label, int direction, TransferItemEntry entry, int itemIndex, GUIStyle style)
        {
            Event e = Event.current;
            if (e != null && e.button == 0 && e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                this.AdjustTransferTilePickQuantity(entry, itemIndex, direction);
                this.BeginTransferQtyHold(direction, entry, itemIndex);
                e.Use();
            }

            bool holdingThisButton = this.transferQtyHoldDirection == direction
                && this.transferQtyHoldItemIndex == itemIndex
                && this.transferQtyHoldNetId == entry.NetId
                && Input.GetMouseButton(0);
            Color previousBg = GUI.backgroundColor;
            if (holdingThisButton)
            {
                GUI.backgroundColor = new Color(0.35f, 0.55f, 0.65f, 1f);
            }

            GUI.Button(rect, label, style);
            GUI.backgroundColor = previousBg;
        }

        private void SelectTransferTile(TransferItemEntry entry, int itemIndex)
        {
            if (entry == null || entry.IsLocked)
            {
                return;
            }

            int maxQty = Math.Max(1, entry.Count);
            int pickQty = this.transferSelectFullStack ? maxQty : 1;
            this.selectedTransferIndex = itemIndex;
            if (this.transferMultiSelectMode)
            {
                if (this.transferBatch.TryGetValue(entry.NetId, out int batchQty))
                {
                    if (this.transferSelectFullStack)
                    {
                        this.transferQty = maxQty;
                        this.transferBatch[entry.NetId] = maxQty;
                    }
                    else
                    {
                        this.transferQty = Mathf.Clamp(batchQty, 1, maxQty);
                    }
                    return;
                }

                this.transferQty = pickQty;
                this.transferBatch[entry.NetId] = pickQty;
            }
            else
            {
                this.transferQty = pickQty;
                this.transferBatch.Clear();
            }
        }

        private string GetTransferTileStarLabel(TransferItemEntry entry)
        {
            if (entry == null || entry.StarRate <= 0)
            {
                return string.Empty;
            }

            return entry.StarRate + "*";
        }

    }
}
