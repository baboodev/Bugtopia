using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool ShopBuyAllLogsEnabled = MasterLogAutoBuy;
        private const int ShopBuyAllCurrencyCoin = 1;
        private const int ShopBuyAllStoreMoneyCurrency = 1;
        private const int ShopBuyAllCurrencyEnumLeft = 0;
        private const int ShopBuyAllRewardNoticeMedium = 1;
        private const float ShopBuyAllDelaySeconds = 0.1f;
        private const int ShopBuyAllMaxPerCommand = 99;
        private const int ShopBuyAllClothingStoreId = 5;
        private const int ShopBuyAllRewardTypeAvatar = 21;

        private const int ShopBuyAllMaxPerItemDefault = 200;

        private object shopBuyAllCoroutine = null;
        private string shopBuyAllStatus = "Idle.";
        private bool shopBuyAllRunning = false;
        private int shopBuyAllMaxPerItem = ShopBuyAllMaxPerItemDefault;

        private IntPtr shopBuyAllShelfBuyItemMethod = IntPtr.Zero;
        private IntPtr shopBuyAllShelfBuyClothesMethod = IntPtr.Zero;
        private IntPtr shopBuyAllGetStoreGoodsDataMethod = IntPtr.Zero;
        private AuraMonoObjectCache shopBuyAllShopSystemObj;
        private AuraMonoObjectCache shopBuyAllPlayerServiceObj;
        private IntPtr shopBuyAllGetCurrencyCountMethod = IntPtr.Zero;
        private IntPtr shopBuyAllGetItemCountMethod = IntPtr.Zero;
        private IntPtr shopBuyAllCheckAvatarObtainMethod = IntPtr.Zero;
        private IntPtr shopBuyAllClothesListClass = IntPtr.Zero;
        private IntPtr shopBuyAllClothesListAddMethod = IntPtr.Zero;

        private struct ShopBuyAllCandidate
        {
            public uint NetId;
            public int StoreId;
            public int SlotId;
            public int ItemId;
            public int Price;
            public int LeftCount;
            public int CurrencyType;
            public int StoreMoneyType;
            public bool IsUnlock;
            public int ItemStaticId;
            public int BoughtCount;
            public int RewardType;
            public int RewardId;
            public bool IsLimitedOne;
            public string Name;
        }

        private void ShopBuyAllLog(string message, bool always = false)
        {
            if (string.IsNullOrEmpty(message) || (!always && !ShopBuyAllLogsEnabled))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[ShopBuyAll] " + message);
            }
            catch
            {
            }
        }

        private void StartShopBuyAllGold()
        {
            if (this.shopBuyAllRunning || this.shopBuyAllCoroutine != null)
            {
                this.AddMenuNotification(this.L("Shop buy-all already running"), new Color(0.45f, 0.88f, 1f));
                return;
            }

            if (this.forceOpenShopSelectedIndex == 6)
            {
                this.StartFaceShopBuyAllCoin();
                return;
            }

            if (!this.TryResolveForceOpenShopStoreId(this.forceOpenShopSelectedIndex, out int storeId, out string label, out string unsupported))
            {
                this.shopBuyAllStatus = unsupported ?? "Shop not supported.";
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                return;
            }

            this.shopBuyAllStatus = "Preparing: " + label + " (id " + storeId + ")...";
            this.shopBuyAllRunning = true;
            this.shopBuyAllCoroutine = ModCoroutines.Start(this.ShopBuyAllGoldRoutine(storeId, label));
        }

        private static string ShopBuyAllFormatName(in ShopBuyAllCandidate candidate)
        {
            if (candidate.ItemStaticId > 0)
            {
                return "item " + candidate.ItemStaticId;
            }

            if (candidate.ItemId > 0)
            {
                return "item " + candidate.ItemId;
            }

            return "net " + candidate.NetId;
        }

        private bool IsShopBuyAllPurchasableCoinItem(in ShopBuyAllCandidate candidate)
        {
            return candidate.StoreMoneyType == ShopBuyAllStoreMoneyCurrency
                && candidate.CurrencyType == ShopBuyAllCurrencyCoin
                && candidate.IsUnlock
                && candidate.LeftCount > 0
                && candidate.Price > 0
                && candidate.StoreId > 0
                && candidate.SlotId > 0
                && candidate.ItemId > 0;
        }

        private bool IsShopBuyAllAlreadyOwned(object managedItem, in ShopBuyAllCandidate candidate)
        {
            if (candidate.BoughtCount > 0 && candidate.IsLimitedOne)
            {
                return true;
            }

            if (candidate.IsLimitedOne
                && candidate.ItemStaticId > 0
                && this.TryGetPlayerItemCount(candidate.ItemStaticId, out int ownedCount)
                && ownedCount > 0)
            {
                return true;
            }

            if (candidate.IsLimitedOne
                && managedItem != null
                && this.TryGetObjectMember(managedItem, "isObtained", out object obtainedObj)
                && obtainedObj is bool isObtained
                && isObtained)
            {
                return true;
            }

            return this.TryIsShopBuyAllAvatarRewardOwned(in candidate);
        }

        private bool TryCollectGoldCoinItems(int storeId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            if (this.TryCollectGoldCoinItemsManaged(storeId, items, out error))
            {
                return items.Count > 0;
            }

            this.ShopBuyAllLog("managed collect unavailable: " + (error ?? "unknown"));
            if (this.TryCollectGoldCoinItemsAura(storeId, items, out error))
            {
                return items.Count > 0;
            }

            if (string.IsNullOrEmpty(error))
            {
                error = "No purchasable Coin items in this store.";
            }

            return false;
        }

        private bool TryCollectGoldCoinItemsManaged(int storeId, List<ShopBuyAllCandidate> items, out string error)
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

                    if (!this.IsShopBuyAllPurchasableCoinItem(in candidate) || this.IsShopBuyAllAlreadyOwned(entry, in candidate))
                    {
                        continue;
                    }

                    items.Add(candidate);
                    added++;
                }

                if (added <= 0)
                {
                    error = "No purchasable Coin items in this store.";
                    return false;
                }

                this.ShopBuyAllLog("managed collect count=" + added);
                return true;
            }
            catch (Exception ex)
            {
                error = "managed collect failed: " + ex.Message;
                return false;
            }
        }

        private bool TryReadManagedShopItemData(object itemObj, out ShopBuyAllCandidate candidate)
        {
            candidate = default(ShopBuyAllCandidate);
            if (itemObj == null)
            {
                return false;
            }

            this.TryGetManagedUInt32Member(itemObj, "netId", out candidate.NetId);
            this.TryGetManagedInt32Member(itemObj, "storeId", out candidate.StoreId);
            this.TryGetManagedInt32Member(itemObj, "slotId", out candidate.SlotId);
            this.TryGetManagedInt32Member(itemObj, "storeGroupId", out candidate.ItemId);
            this.TryGetManagedInt32Member(itemObj, "currencyType", out candidate.CurrencyType);
            this.TryGetManagedInt32Member(itemObj, "price", out candidate.Price);
            this.TryGetManagedInt32Member(itemObj, "storeMoneyType", out candidate.StoreMoneyType);
            this.TryGetManagedInt32Member(itemObj, "boughtCount", out candidate.BoughtCount);

            if (this.TryGetObjectMember(itemObj, "isUnlock", out object unlockObj) && unlockObj is bool unlock)
            {
                candidate.IsUnlock = unlock;
            }

            if (this.TryGetObjectMember(itemObj, "isLimitedOne", out object limitedObj) && limitedObj is bool limitedOne)
            {
                candidate.IsLimitedOne = limitedOne;
            }

            if (this.TryGetObjectMember(itemObj, "rewardData", out object rewardObj) && rewardObj != null)
            {
                this.TryGetManagedInt32Member(rewardObj, "staticId", out candidate.ItemStaticId);
                this.TryGetManagedInt32Member(rewardObj, "rewardType", out candidate.RewardType);
                this.TryGetManagedInt32Member(rewardObj, "rewardId", out candidate.RewardId);
            }

            if (!this.TryGetManagedInt32Member(itemObj, "_leftCount", out candidate.LeftCount)
                && this.TryGetObjectMember(itemObj, "leftCount", out object leftCountObj)
                && leftCountObj != null)
            {
                try
                {
                    candidate.LeftCount = Convert.ToInt32(leftCountObj);
                }
                catch
                {
                    candidate.LeftCount = 0;
                }
            }

            if (this.TryGetObjectMember(itemObj, "name", out object nameObj) && nameObj != null)
            {
                candidate.Name = Convert.ToString(nameObj);
            }

            if (string.IsNullOrWhiteSpace(candidate.Name))
            {
                candidate.Name = ShopBuyAllFormatName(in candidate);
            }

            return candidate.ItemId > 0 || candidate.NetId != 0U;
        }

        private bool TryCollectGoldCoinItemsAura(int storeId, List<ShopBuyAllCandidate> items, out string error)
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

                if (!this.IsShopBuyAllPurchasableCoinItem(in candidate) || this.IsShopBuyAllAlreadyOwned(null, in candidate))
                {
                    continue;
                }

                items.Add(candidate);
                added++;
            }

            if (added <= 0)
            {
                error = "No purchasable Coin items in this store.";
                return false;
            }

            this.ShopBuyAllLog("aura collect count=" + added);
            return true;
        }

        private bool TryReadAuraShopItemData(IntPtr itemObj, out ShopBuyAllCandidate candidate)
        {
            candidate = default(ShopBuyAllCandidate);
            if (itemObj == IntPtr.Zero)
            {
                return false;
            }

            candidate.NetId = (uint)this.TryReadAuraMonoStructIntField(itemObj, "netId");
            candidate.StoreId = this.TryReadAuraMonoStructIntField(itemObj, "storeId");
            candidate.SlotId = this.TryReadAuraMonoStructIntField(itemObj, "slotId");
            candidate.ItemId = this.TryReadAuraMonoStructIntField(itemObj, "storeGroupId");
            candidate.CurrencyType = this.TryReadAuraMonoStructIntField(itemObj, "currencyType");
            candidate.Price = this.TryReadAuraMonoStructIntField(itemObj, "price");
            candidate.StoreMoneyType = this.TryReadAuraMonoStructIntField(itemObj, "storeMoneyType");
            candidate.IsUnlock = this.TryReadAuraMonoStructBoolField(itemObj, "isUnlock");
            candidate.BoughtCount = this.TryReadAuraMonoStructIntField(itemObj, "boughtCount");
            candidate.IsLimitedOne = this.TryReadAuraMonoStructBoolField(itemObj, "isLimitedOne");
            candidate.ItemStaticId = this.TryReadAuraMonoStructNestedIntField(itemObj, "rewardData", "staticId");
            candidate.RewardType = this.TryReadAuraMonoStructNestedIntField(itemObj, "rewardData", "rewardType");
            candidate.RewardId = this.TryReadAuraMonoStructNestedIntField(itemObj, "rewardData", "rewardId");
            candidate.LeftCount = this.TryReadAuraMonoStructIntField(itemObj, "_leftCount");
            if (candidate.LeftCount <= 0)
            {
                int reserve = this.TryReadAuraMonoStructIntField(itemObj, "reserve");
                if (reserve > 0)
                {
                    candidate.LeftCount = Mathf.Max(0, reserve - candidate.BoughtCount);
                }
            }

            if (candidate.IsLimitedOne && candidate.ItemStaticId > 0 && this.TryGetPlayerItemCount(candidate.ItemStaticId, out int ownedCount))
            {
                candidate.LeftCount = Mathf.Max(0, candidate.LeftCount - ownedCount);
            }

            candidate.Name = ShopBuyAllFormatName(in candidate);
            return candidate.ItemId > 0 || candidate.NetId != 0U;
        }

        private int TryReadAuraMonoStructIntField(IntPtr boxedObj, string fieldName)
        {
            if (boxedObj == IntPtr.Zero || string.IsNullOrEmpty(fieldName) || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null)
            {
                return 0;
            }

            IntPtr klass = auraMonoObjectGetClass(boxedObj);
            IntPtr field = klass != IntPtr.Zero ? this.FindAuraMonoFieldOnHierarchy(klass, fieldName) : IntPtr.Zero;
            if (field == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr boxed = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, boxedObj);
            if (boxed == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryUnboxMonoInt32(boxed, out int value))
            {
                return value;
            }

            ulong fallback = this.TryReadMonoUnsignedIntegral(boxed);
            return fallback <= int.MaxValue ? (int)fallback : 0;
        }

        private bool TryReadAuraMonoStructBoolField(IntPtr boxedObj, string fieldName)
        {
            return this.TryReadAuraMonoStructIntField(boxedObj, fieldName) != 0;
        }

        private int TryReadAuraMonoStructNestedIntField(IntPtr boxedObj, string structFieldName, string innerFieldName)
        {
            if (boxedObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null)
            {
                return 0;
            }

            IntPtr structField = this.FindAuraMonoFieldOnHierarchy(auraMonoObjectGetClass(boxedObj), structFieldName);
            if (structField == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr nestedObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, structField, boxedObj);
            return nestedObj != IntPtr.Zero ? this.TryReadAuraMonoStructIntField(nestedObj, innerFieldName) : 0;
        }

        private bool TryEnsureShopBuyAllAuraShopSystem(out string error)
        {
            error = null;
            if (this.shopBuyAllShopSystemObj.TryGet(out _))
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                error = "AuraMono unavailable.";
                return false;
            }

            IntPtr shopClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Shop.ShopSystem");
            if (shopClass == IntPtr.Zero)
            {
                error = "Aura ShopSystem class missing.";
                return false;
            }

            IntPtr shopSys = this.TryGetAuraMonoDataModuleInstance(shopClass);
            if (shopSys == IntPtr.Zero && !this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Shop.ShopSystem", out shopSys))
            {
                error = "Aura ShopSystem instance missing.";
                return false;
            }

            this.shopBuyAllShopSystemObj.Set(shopSys);
            return true;
        }

        private bool TryEnsureShopBuyAllAuraListing(out string error)
        {
            if (this.shopBuyAllGetStoreGoodsDataMethod != IntPtr.Zero && this.TryEnsureShopBuyAllAuraShopSystem(out error))
            {
                return true;
            }

            error = null;
            if (!this.TryEnsureShopBuyAllAuraShopSystem(out error))
            {
                return false;
            }

            this.shopBuyAllShopSystemObj.TryGet(out IntPtr shopSystemForListing);
            IntPtr shopClass = auraMonoObjectGetClass != null && shopSystemForListing != IntPtr.Zero ? auraMonoObjectGetClass(shopSystemForListing) : IntPtr.Zero;
            IntPtr getGoods = shopClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(shopClass, "GetStoreGoodsData", 1) : IntPtr.Zero;
            if (getGoods == IntPtr.Zero)
            {
                error = "Aura GetStoreGoodsData missing.";
                return false;
            }

            this.shopBuyAllGetStoreGoodsDataMethod = getGoods;
            return true;
        }

        private unsafe bool TryInvokeAuraGetStoreGoodsData(int storeId, out IntPtr listObj, out string error)
        {
            listObj = IntPtr.Zero;
            if (!this.TryEnsureShopBuyAllAuraListing(out error) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.shopBuyAllShopSystemObj.TryGet(out IntPtr shopSystemObj))
            {
                error = "Aura ShopSystem instance stale.";
                return false;
            }

            int storeIdValue = storeId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&storeIdValue);
            IntPtr exc = IntPtr.Zero;
            listObj = auraMonoRuntimeInvoke(this.shopBuyAllGetStoreGoodsDataMethod, shopSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                error = "Aura GetStoreGoodsData invoke failed.";
                return false;
            }

            return true;
        }

        private bool TryIsShopBuyAllAvatarRewardOwned(in ShopBuyAllCandidate candidate)
        {
            if (candidate.RewardType != ShopBuyAllRewardTypeAvatar || candidate.ItemStaticId <= 0)
            {
                return false;
            }

            if (!this.TryEnsureShopBuyAllAvatarObtainCheck(out _))
            {
                return false;
            }

            int avatarUnlockType = candidate.RewardId switch
            {
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                _ => 2
            };

            if (!this.shopBuyAllShopSystemObj.TryGet(out IntPtr shopSystemObj))
            {
                return false;
            }

            unsafe
            {
                int unlockTypeValue = avatarUnlockType;
                int staticIdValue = candidate.ItemStaticId;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&unlockTypeValue);
                args[1] = (IntPtr)(&staticIdValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(this.shopBuyAllCheckAvatarObtainMethod, shopSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return false;
                }

                return (this.TryUnboxMonoBoolean(boxed, out bool hasObtain) && hasObtain)
                    || (this.TryUnboxMonoInt32(boxed, out int rawBool) && rawBool != 0);
            }
        }

        private bool TryEnsureShopBuyAllAvatarObtainCheck(out string error)
        {
            error = null;
            if (this.shopBuyAllCheckAvatarObtainMethod != IntPtr.Zero)
            {
                return this.TryEnsureShopBuyAllAuraShopSystem(out error);
            }

            if (!this.TryEnsureShopBuyAllAuraShopSystem(out error) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            this.shopBuyAllShopSystemObj.TryGet(out IntPtr shopSystemForCheck);
            IntPtr shopClass = auraMonoObjectGetClass != null && shopSystemForCheck != IntPtr.Zero ? auraMonoObjectGetClass(shopSystemForCheck) : IntPtr.Zero;
            IntPtr checkMethod = shopClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(shopClass, "CheckIfAvatarHasObtain", 2) : IntPtr.Zero;
            if (checkMethod == IntPtr.Zero)
            {
                error = "CheckIfAvatarHasObtain missing.";
                return false;
            }

            this.shopBuyAllCheckAvatarObtainMethod = checkMethod;
            return true;
        }

        private unsafe bool TryGetPlayerItemCount(int staticId, out int count)
        {
            count = 0;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.TryGetPlayerItemCountAura(staticId, out count))
            {
                return true;
            }

            return this.TryGetPlayerItemCountManaged(staticId, out count);
        }

        private bool TryGetPlayerItemCountManaged(int staticId, out int count)
        {
            count = 0;
            try
            {
                Type playerType = this.FindLoadedType("XDTGameSystem.PlayerService.PlayerServiceSystem", "PlayerServiceSystem");
                if (playerType == null)
                {
                    return false;
                }

                object playerObj = null;
                PropertyInfo instanceProperty = this.GetDataModuleInstanceProperty(playerType);
                if (instanceProperty != null)
                {
                    playerObj = instanceProperty.GetValue(null, null);
                }

                if (playerObj == null && !this.TryGetManagedModule(playerType, out playerObj))
                {
                    return false;
                }

                MethodInfo getItemCount = playerType.GetMethod("GetItemCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (getItemCount == null)
                {
                    return false;
                }

                count = Convert.ToInt32(getItemCount.Invoke(playerObj, new object[] { staticId }));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryGetPlayerItemCountAura(int staticId, out int count)
        {
            count = 0;
            if (!this.TryEnsureShopBuyAllPlayerService(out _) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.shopBuyAllPlayerServiceObj.TryGet(out IntPtr playerServiceForCount))
            {
                return false;
            }

            if (this.shopBuyAllGetItemCountMethod == IntPtr.Zero)
            {
                IntPtr playerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerServiceForCount) : IntPtr.Zero;
                this.shopBuyAllGetItemCountMethod = playerClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(playerClass, "GetItemCount", 1) : IntPtr.Zero;
                if (this.shopBuyAllGetItemCountMethod == IntPtr.Zero)
                {
                    return false;
                }
            }

            int staticIdValue = staticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&staticIdValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.shopBuyAllGetItemCountMethod, playerServiceForCount, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryUnboxMonoInt32(boxed, out count))
            {
                return true;
            }

            if (auraMonoObjectUnbox != null)
            {
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw != IntPtr.Zero)
                {
                    count = Marshal.ReadInt32(raw);
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryEnsureShopBuyAllPlayerService(out string error)
        {
            error = null;
            if (this.shopBuyAllPlayerServiceObj.TryGet(out _) && this.shopBuyAllGetCurrencyCountMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono unavailable.";
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassByFullName("XDTGameSystem.PlayerService.PlayerServiceSystem");
            if (playerClass == IntPtr.Zero)
            {
                error = "PlayerServiceSystem class missing.";
                return false;
            }

            IntPtr playerServiceObj = this.TryGetAuraMonoDataModuleInstance(playerClass);
            if (playerServiceObj == IntPtr.Zero && !this.TryResolveAuraMonoModule("XDTGameSystem.PlayerService.PlayerServiceSystem", out playerServiceObj))
            {
                error = "PlayerServiceSystem instance missing.";
                return false;
            }

            IntPtr getCurrencyCount = this.FindAuraMonoMethodOnHierarchy(playerClass, "GetCurrencyCount", 1);
            if (getCurrencyCount == IntPtr.Zero)
            {
                error = "GetCurrencyCount missing.";
                return false;
            }

            this.shopBuyAllPlayerServiceObj.Set(playerServiceObj);
            this.shopBuyAllGetCurrencyCountMethod = getCurrencyCount;
            return true;
        }

        private unsafe bool TryGetPlayerCoinBalance(out long balance, out string error)
        {
            balance = long.MaxValue;
            error = null;
            if (!this.TryEnsureShopBuyAllPlayerService(out error) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.shopBuyAllPlayerServiceObj.TryGet(out IntPtr playerServiceObj))
            {
                error = "PlayerService instance stale.";
                return false;
            }

            int currencyType = ShopBuyAllCurrencyCoin;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&currencyType);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.shopBuyAllGetCurrencyCountMethod, playerServiceObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                error = "GetCurrencyCount invoke failed.";
                return false;
            }

            if (this.TryUnboxMonoInt32(boxed, out int intBalance))
            {
                balance = intBalance;
                return true;
            }

            if (auraMonoObjectUnbox != null)
            {
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw != IntPtr.Zero)
                {
                    balance = Marshal.ReadInt32(raw);
                    return true;
                }
            }

            error = "GetCurrencyCount unbox failed.";
            return false;
        }

        private bool TryInvokeManagedShopBuyItem(in ShopBuyAllCandidate item, int count)
        {
            try
            {
                Type shopType = this.FindLoadedType("XDTGameSystem.GameplaySystem.Shop.ShopSystem", "ShopSystem");
                if (shopType == null || item.NetId == 0U)
                {
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
                    return false;
                }

                MethodInfo buyItem = shopType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "BuyItem", StringComparison.Ordinal))
                        {
                            return false;
                        }

                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length >= 2 && parameters[0].ParameterType == typeof(uint);
                    });
                if (buyItem == null)
                {
                    return false;
                }

                object[] args = buyItem.GetParameters().Length switch
                {
                    2 => new object[] { item.NetId, count },
                    3 => new object[] { item.NetId, count, ShopBuyAllCurrencyEnumLeft },
                    4 => new object[] { item.NetId, count, ShopBuyAllCurrencyEnumLeft, ShopBuyAllRewardNoticeMedium },
                    _ => new object[] { item.NetId, count, ShopBuyAllCurrencyEnumLeft, ShopBuyAllRewardNoticeMedium, null }
                };
                buyItem.Invoke(shopObj, args);
                this.ShopBuyAllLog("managed BuyItem netId=" + item.NetId + " count=" + count);
                return true;
            }
            catch (Exception ex)
            {
                this.ShopBuyAllLog("managed BuyItem failed: " + ex.Message, always: true);
                return false;
            }
        }

        private bool TryEnsureShopBuyAllAuraShelfBuyItem(out string error)
        {
            error = null;
            if (this.shopBuyAllShelfBuyItemMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono unavailable.";
                return false;
            }

            IntPtr shelfClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.ShopShelf.ShopShelfProtocolManager");
            if (shelfClass == IntPtr.Zero)
            {
                error = "ShopShelfProtocolManager class missing.";
                return false;
            }

            IntPtr shelfBuy = this.FindAuraMonoMethodOnHierarchy(shelfClass, "BuyItem", 7);
            if (shelfBuy == IntPtr.Zero)
            {
                shelfBuy = this.FindAuraMonoMethodOnHierarchy(shelfClass, "BuyItem", 6);
            }

            if (shelfBuy == IntPtr.Zero)
            {
                error = "ShopShelfProtocolManager.BuyItem missing.";
                return false;
            }

            this.shopBuyAllShelfBuyItemMethod = shelfBuy;
            return true;
        }

        private unsafe bool TryInvokeAuraShelfBuyItem(in ShopBuyAllCandidate item, int count, out string error)
        {
            error = null;
            if (!this.TryEnsureShopBuyAllAuraShelfBuyItem(out error) || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int storeIdValue = item.StoreId;
            int slotIdValue = item.SlotId;
            int itemIdValue = item.ItemId;
            int buyCountValue = count;
            int currencyEnum = ShopBuyAllCurrencyEnumLeft;
            int notifyLevel = ShopBuyAllRewardNoticeMedium;
            IntPtr* args = stackalloc IntPtr[7];
            args[0] = (IntPtr)(&storeIdValue);
            args[1] = (IntPtr)(&slotIdValue);
            args[2] = (IntPtr)(&itemIdValue);
            args[3] = (IntPtr)(&buyCountValue);
            args[4] = (IntPtr)(&currencyEnum);
            args[5] = (IntPtr)(&notifyLevel);
            args[6] = IntPtr.Zero;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.shopBuyAllShelfBuyItemMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "ShopShelfProtocolManager.BuyItem failed exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            this.ShopBuyAllLog("aura shelf buy store=" + item.StoreId + " slot=" + item.SlotId + " item=" + item.ItemId + " count=" + count);
            return true;
        }

        private bool TryEnsureShopBuyAllAuraShelfBuyClothes(out string error)
        {
            error = null;
            if (this.shopBuyAllShelfBuyClothesMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono unavailable.";
                return false;
            }

            IntPtr shelfClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.ShopShelf.ShopShelfProtocolManager");
            if (shelfClass == IntPtr.Zero)
            {
                error = "ShopShelfProtocolManager class missing.";
                return false;
            }

            IntPtr buyClothes = this.FindAuraMonoMethodOnHierarchy(shelfClass, "BuyClothes", 2);
            if (buyClothes == IntPtr.Zero)
            {
                error = "ShopShelfProtocolManager.BuyClothes missing.";
                return false;
            }

            this.shopBuyAllShelfBuyClothesMethod = buyClothes;
            return true;
        }

        private unsafe bool TryCreateAuraMonoClothesStoreEntryList(in ShopBuyAllCandidate item, out IntPtr listObj, out string error)
        {
            listObj = IntPtr.Zero;
            error = null;
            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                error = "AuraMono list prerequisites unavailable.";
                return false;
            }

            if (this.shopBuyAllClothesListClass != IntPtr.Zero && auraMonoObjectNew != null)
            {
                listObj = auraMonoObjectNew(this.auraMonoRootDomain, this.shopBuyAllClothesListClass);
                if (listObj != IntPtr.Zero && auraMonoRuntimeObjectInit != null)
                {
                    auraMonoRuntimeObjectInit(listObj);
                }
                else
                {
                    listObj = IntPtr.Zero;
                }
            }

            string[] listTypeCandidates = new string[]
            {
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.DepartmentStore.ClothesStoreEntry, EcsClient]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.DepartmentStore.ClothesStoreEntry, Client]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < listTypeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, listTypeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                error = "List<ClothesStoreEntry> create failed.";
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass != IntPtr.Zero)
            {
                this.shopBuyAllClothesListClass = listClass;
            }

            IntPtr addMethod = this.shopBuyAllClothesListAddMethod;
            if (addMethod == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                this.shopBuyAllClothesListAddMethod = addMethod;
            }

            if (addMethod == IntPtr.Zero)
            {
                error = "List<ClothesStoreEntry>.Add missing.";
                return false;
            }

            const int structSize = 24;
            byte* structData = stackalloc byte[structSize];
            *(int*)(structData + 0) = item.StoreId;
            *(int*)(structData + 4) = item.SlotId;
            *(int*)(structData + 8) = item.ItemId;
            *(int*)(structData + 12) = 0;
            *(int*)(structData + 16) = 0;
            *(uint*)(structData + 20) = 0U;
            IntPtr* addArgs = stackalloc IntPtr[1];
            addArgs[0] = (IntPtr)structData;
            IntPtr excAdd = IntPtr.Zero;
            auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref excAdd);
            if (excAdd != IntPtr.Zero)
            {
                error = "List<ClothesStoreEntry>.Add failed exc=0x" + excAdd.ToInt64().ToString("X");
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeAuraShelfBuyClothes(in ShopBuyAllCandidate item, out string error)
        {
            error = null;
            if (!this.TryEnsureShopBuyAllAuraShelfBuyClothes(out error)
                || !this.TryCreateAuraMonoClothesStoreEntryList(item, out IntPtr listObj, out error)
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int wearValue = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = listObj;
            args[1] = (IntPtr)(&wearValue);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.shopBuyAllShelfBuyClothesMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "Aura BuyClothes failed exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            this.ShopBuyAllLog("aura BuyClothes store=" + item.StoreId + " slot=" + item.SlotId + " item=" + item.ItemId);
            return true;
        }

        private bool TryInvokeShopBuyItem(in ShopBuyAllCandidate item, int count, out string error)
        {
            error = null;
            if (item.StoreId == ShopBuyAllClothingStoreId)
            {
                if (this.TryInvokeAuraShelfBuyClothes(item, out error))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(error))
                {
                    error = "BuyClothes path unavailable.";
                }

                return false;
            }

            count = Mathf.Clamp(count, 1, ShopBuyAllMaxPerCommand);
            if (item.NetId != 0U && this.TryInvokeManagedShopBuyItem(item, count))
            {
                return true;
            }

            if (this.TryInvokeAuraShelfBuyItem(item, count, out error))
            {
                return true;
            }

            if (string.IsNullOrEmpty(error))
            {
                error = "Buy path unavailable.";
            }

            return false;
        }

        private IEnumerator ShopBuyAllGoldRoutine(int storeId, string label)
        {
            yield return null;
            yield return null;

            List<ShopBuyAllCandidate> items;
            string prepError;
            try
            {
                if (!this.TryCollectGoldCoinItems(storeId, items = new List<ShopBuyAllCandidate>(), out prepError))
                {
                    this.shopBuyAllStatus = prepError;
                    this.ShopBuyAllLog(prepError, always: true);
                    this.AddMenuNotification(prepError, new Color(1f, 0.55f, 0.45f));
                    this.shopBuyAllRunning = false;
                    this.shopBuyAllCoroutine = null;
                    yield break;
                }
            }
            catch (Exception ex)
            {
                this.shopBuyAllStatus = "Shop buy-all error: " + ex.Message;
                this.ShopBuyAllLog(this.shopBuyAllStatus, always: true);
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                this.shopBuyAllRunning = false;
                this.shopBuyAllCoroutine = null;
                yield break;
            }

            this.ShopBuyAllLog("candidates=" + items.Count + " storeId=" + storeId + " label=" + label);
            long coinBalance = long.MaxValue;
            try
            {
                if (!this.TryGetPlayerCoinBalance(out coinBalance, out _))
                {
                    coinBalance = long.MaxValue;
                }
            }
            catch (Exception ex)
            {
                this.ShopBuyAllLog("balance read failed: " + ex.Message, always: true);
                coinBalance = long.MaxValue;
            }

            bool clothingStore = storeId == ShopBuyAllClothingStoreId;
            int bought = 0;
            int skipped = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ShopBuyAllCandidate item = items[i];
                int unitPrice = item.Price;
                int perItemCap = Mathf.Clamp(this.shopBuyAllMaxPerItem, 1, 999999);
                int maxAffordable = unitPrice > 0
                    ? (clothingStore
                        ? (coinBalance >= unitPrice && item.LeftCount > 0 ? 1 : 0)
                        : (int)Math.Min(item.LeftCount, coinBalance / unitPrice))
                    : 0;
                if (maxAffordable > 0)
                {
                    maxAffordable = Math.Min(maxAffordable, perItemCap);
                }
                if (maxAffordable <= 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (!this.TryInvokeShopBuyItem(item, maxAffordable, out string buyError))
                    {
                        this.ShopBuyAllLog("buy failed " + item.Name + ": " + buyError, always: true);
                        skipped++;
                    }
                    else
                    {
                        bought++;
                        coinBalance -= (long)unitPrice * maxAffordable;
                        this.shopBuyAllStatus = "Buying " + (i + 1) + "/" + items.Count + ": " + item.Name + " x" + maxAffordable;
                    }
                }
                catch (Exception ex)
                {
                    this.ShopBuyAllLog("buy exception " + item.Name + ": " + ex.Message, always: true);
                    skipped++;
                }

                yield return new WaitForSecondsRealtime(ShopBuyAllDelaySeconds);
            }

            this.shopBuyAllStatus = "Done (" + label + "): bought " + bought + ", skipped " + skipped + ".";
            this.AddMenuNotification(this.shopBuyAllStatus, new Color(0.55f, 1f, 0.65f));
            this.shopBuyAllRunning = false;
            this.shopBuyAllCoroutine = null;
        }
    }
}
