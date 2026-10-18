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
        private static bool ShopBuyAllLogsEnabled => MasterLogAutoBuy;
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
            public bool UnlimitedStock;
            public string Name;
        }

        private struct ShopBuyGridRow
        {
            public ShopBuyAllCandidate Item;
            public string Name;
            public bool AlreadyOwned;
        }

        private const string ShopItemUpdateEventName = "XDTDataAndProtocol.Events.ShopItemUpdate";
        private const float ShopBuyGridCollectInterval = 0.5f;

        private readonly List<ShopBuyPickEntry> shopBuyPicks = new List<ShopBuyPickEntry>();
        private readonly List<ShopBuyGridRow> shopBuyGridRows = new List<ShopBuyGridRow>();
        private readonly Dictionary<int, string> shopBuyNameCache = new Dictionary<int, string>();
        private readonly Dictionary<int, float> shopBuyNameRetryAt = new Dictionary<int, float>();
        private int shopBuyGridStoreId = -1;
        private bool shopBuyGridDirty = true;
        private bool shopBuyGridShowList;
        private float shopBuyGridNextCollectAt;
        private bool shopBuyGridHookArmed;
        private string shopBuyGridHintKey = string.Empty;
        private string shopBuyGridHintRaw = string.Empty;

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
            bool selected = this.CountShopBuyPicks(storeId) > 0;
            this.shopBuyAllCoroutine = ModCoroutines.Start(selected
                ? this.ShopBuySelectedRoutine(storeId, label)
                : this.ShopBuyAllGoldRoutine(storeId, label));
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



        private bool TryCollectGoldCoinItemsAura(int storeId, List<ShopBuyAllCandidate> items, out string error)
        {
            error = null;
            if (!this.TryInvokeAuraGetStoreGoodsData(storeId, out IntPtr listObj, out error))
            {
                return false;
            }

            List<IntPtr> elements = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            int added = 0;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, elements, pins) || elements.Count == 0)
                {
                    error = "Aura shop item list empty.";
                    return false;
                }

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
            }
            finally
            {
                FreeAuraMonoPins(pins);
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
            int rawLeft = this.TryReadAuraMonoStructIntField(itemObj, "_leftCount");
            int reserve = this.TryReadAuraMonoStructIntField(itemObj, "reserve");
            candidate.LeftCount = rawLeft;
            if (candidate.LeftCount <= 0 && reserve > 0)
            {
                candidate.LeftCount = Mathf.Max(0, reserve - candidate.BoughtCount);
            }

            // buyCount == 0 in the table: _leftCount and reserve both stay <= 0 and IsSellOut is false.
            candidate.UnlimitedStock = rawLeft <= 0 && reserve <= 0;
            if (candidate.IsLimitedOne && candidate.ItemStaticId > 0 && this.TryGetPlayerItemCount(candidate.ItemStaticId, out int ownedCount) && ownedCount > 0)
            {
                candidate.LeftCount = Mathf.Max(0, candidate.LeftCount - ownedCount);
                candidate.UnlimitedStock = false;
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

        // Find an unlocked shop listing for this exact item staticId in the given store (local
        // ShopSystem read via GetStoreGoodsData — no UI, no server round-trip). The Quest Assistant
        // uses this to buy quest-required items (PurchaseItem steps) from whichever store lists them.
        // Unlike TryCollectGoldCoinItemsAura this applies NO coin/owned filters — the quest needs the
        // purchase itself. Raw list pointers are read within this single call (no yields).
        private bool TryFindShopCandidateByStaticIdAura(int storeId, int itemStaticId, out ShopBuyAllCandidate found)
        {
            found = default(ShopBuyAllCandidate);
            if (storeId <= 0 || itemStaticId <= 0)
            {
                return false;
            }

            if (!this.TryInvokeAuraGetStoreGoodsData(storeId, out IntPtr listObj, out _))
            {
                return false;
            }

            List<IntPtr> elements = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, elements) || elements.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                if (!this.TryReadAuraShopItemData(elements[i], out ShopBuyAllCandidate candidate))
                {
                    continue;
                }

                if (candidate.ItemStaticId != itemStaticId || !candidate.IsUnlock)
                {
                    continue;
                }

                found = candidate;
                return true;
            }

            return false;
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

            return false;
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
                    this.FinishShopBuyAllRun();
                    yield break;
                }
            }
            catch (Exception ex)
            {
                this.shopBuyAllStatus = "Shop buy-all error: " + ex.Message;
                FeatureLog.Fail("ShopBuyAll", this.shopBuyAllStatus);
                this.ShopBuyAllLog(this.shopBuyAllStatus, always: true);
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                this.FinishShopBuyAllRun();
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

                yield return ModWait.Realtime(ShopBuyAllDelaySeconds);
            }

            this.shopBuyAllStatus = "Done (" + label + "): bought " + bought + ", skipped " + skipped + ".";
            FeatureLog.Life("ShopBuyAll", this.shopBuyAllStatus);
            this.AddMenuNotification(this.shopBuyAllStatus, new Color(0.55f, 1f, 0.65f));
            this.FinishShopBuyAllRun();
        }

        private void FinishShopBuyAllRun()
        {
            this.shopBuyGridDirty = true;
            this.shopBuyGridNextCollectAt = 0f;
            this.shopBuyAllRunning = false;
            this.shopBuyAllCoroutine = null;
        }

        private void EnsureShopBuyGridHook()
        {
            if (this.shopBuyGridHookArmed)
            {
                return;
            }

            this.shopBuyGridHookArmed = true;
            this.RegisterGameEventHook(ShopItemUpdateEventName, 0, this.OnShopItemUpdate);
        }

        private void OnShopItemUpdate(GameEventSnapshot snapshot)
        {
            this.shopBuyGridDirty = true;
        }

        private void MarkShopBuyGridDirty()
        {
            this.shopBuyGridDirty = true;
            this.shopBuyGridNextCollectAt = 0f;
        }

        private string ShopBuyPickKey(int slotId, int itemId)
        {
            return slotId.ToString() + ":" + itemId.ToString();
        }

        private int FindShopBuyPick(int storeId, int slotId, int itemId)
        {
            for (int i = 0; i < this.shopBuyPicks.Count; i++)
            {
                ShopBuyPickEntry entry = this.shopBuyPicks[i];
                if (entry != null && entry.StoreId == storeId && entry.SlotId == slotId && entry.ItemId == itemId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int CountShopBuyPicks(int storeId)
        {
            int count = 0;
            for (int i = 0; i < this.shopBuyPicks.Count; i++)
            {
                ShopBuyPickEntry entry = this.shopBuyPicks[i];
                if (entry != null && entry.StoreId == storeId && entry.Count > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetShopBuyPickCount(int storeId, int slotId, int itemId, out int count)
        {
            int index = this.FindShopBuyPick(storeId, slotId, itemId);
            if (index < 0)
            {
                count = 0;
                return false;
            }

            count = this.shopBuyPicks[index].Count;
            return count > 0;
        }

        private void SetShopBuyPick(int storeId, int slotId, int itemId, int count, bool save)
        {
            if (storeId <= 0 || slotId <= 0 || itemId <= 0)
            {
                return;
            }

            int index = this.FindShopBuyPick(storeId, slotId, itemId);
            if (count <= 0)
            {
                if (index < 0)
                {
                    return;
                }

                this.shopBuyPicks.RemoveAt(index);
            }
            else
            {
                int clamped = Mathf.Clamp(count, 1, 999999);
                if (index >= 0)
                {
                    if (this.shopBuyPicks[index].Count == clamped)
                    {
                        return;
                    }

                    this.shopBuyPicks[index].Count = clamped;
                }
                else
                {
                    this.shopBuyPicks.Add(new ShopBuyPickEntry
                    {
                        StoreId = storeId,
                        SlotId = slotId,
                        ItemId = itemId,
                        Count = clamped
                    });
                }
            }

            if (!save)
            {
                return;
            }

            try { this.SaveKeybinds(false); } catch { }
        }

        private List<ShopBuyPickEntry> CopyShopBuyPicks()
        {
            List<ShopBuyPickEntry> copy = new List<ShopBuyPickEntry>(this.shopBuyPicks.Count);
            for (int i = 0; i < this.shopBuyPicks.Count; i++)
            {
                ShopBuyPickEntry entry = this.shopBuyPicks[i];
                if (entry == null || entry.StoreId <= 0 || entry.SlotId <= 0 || entry.ItemId <= 0 || entry.Count <= 0)
                {
                    continue;
                }

                copy.Add(new ShopBuyPickEntry
                {
                    StoreId = entry.StoreId,
                    SlotId = entry.SlotId,
                    ItemId = entry.ItemId,
                    Count = Mathf.Clamp(entry.Count, 1, 999999)
                });
            }

            return copy;
        }

        private void ReplaceShopBuyPicks(List<ShopBuyPickEntry> source)
        {
            this.shopBuyPicks.Clear();
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                ShopBuyPickEntry entry = source[i];
                if (entry == null || entry.StoreId <= 0 || entry.SlotId <= 0 || entry.ItemId <= 0 || entry.Count <= 0)
                {
                    continue;
                }

                int index = this.FindShopBuyPick(entry.StoreId, entry.SlotId, entry.ItemId);
                int clamped = Mathf.Clamp(entry.Count, 1, 999999);
                if (index >= 0)
                {
                    this.shopBuyPicks[index].Count = clamped;
                }
                else
                {
                    this.shopBuyPicks.Add(new ShopBuyPickEntry
                    {
                        StoreId = entry.StoreId,
                        SlotId = entry.SlotId,
                        ItemId = entry.ItemId,
                        Count = clamped
                    });
                }
            }
        }

        private Dictionary<string, int> SnapshotShopBuyPicks(int storeId)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < this.shopBuyPicks.Count; i++)
            {
                ShopBuyPickEntry entry = this.shopBuyPicks[i];
                if (entry == null || entry.StoreId != storeId || entry.Count <= 0)
                {
                    continue;
                }

                map[this.ShopBuyPickKey(entry.SlotId, entry.ItemId)] = Mathf.Clamp(entry.Count, 1, 999999);
            }

            return map;
        }

        private void SetShopBuyGridHint(string key, string raw)
        {
            this.shopBuyGridHintKey = key ?? string.Empty;
            this.shopBuyGridHintRaw = raw ?? string.Empty;
        }

        private string ShopBuyGridHintText()
        {
            if (!string.IsNullOrEmpty(this.shopBuyGridHintRaw))
            {
                return this.shopBuyGridHintRaw;
            }

            if (!string.IsNullOrEmpty(this.shopBuyGridHintKey))
            {
                return this.L(this.shopBuyGridHintKey);
            }

            return string.Empty;
        }

        private static bool IsShopBuyFallbackName(string name, int staticId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            if (staticId > 0 && string.Equals(name, "Item " + staticId.ToString(), StringComparison.Ordinal))
            {
                return true;
            }

            if (name.StartsWith("item ", StringComparison.Ordinal) || string.Equals(name, "Unknown Item", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private string ResolveShopBuyItemName(in ShopBuyAllCandidate candidate)
        {
            return this.ShopBuyCellName(candidate.ItemStaticId, ShopBuyAllFormatName(in candidate));
        }

        private string ShopBuyCellName(int staticId, string fallback)
        {
            if (staticId > 0
                && this.shopBuyNameCache.TryGetValue(staticId, out string cached)
                && !IsShopBuyFallbackName(cached, staticId))
            {
                return cached;
            }

            if (staticId > 0
                && this.shopBuyNameRetryAt.TryGetValue(staticId, out float retryAt)
                && Time.unscaledTime < retryAt)
            {
                return string.IsNullOrWhiteSpace(fallback) ? ("Item " + staticId.ToString()) : fallback;
            }

            if (staticId > 0)
            {
                this.shopBuyNameRetryAt[staticId] = Time.unscaledTime + 1f;
                string resolved = this.ResolveBagItemDisplayName(null, staticId);
                if (!this.IsPoorBagItemDisplayName(resolved, staticId) && !IsShopBuyFallbackName(resolved, staticId))
                {
                    this.shopBuyNameCache[staticId] = resolved;
                    this.shopBuyNameRetryAt.Remove(staticId);
                    return resolved;
                }
            }

            return string.IsNullOrWhiteSpace(fallback)
                ? (staticId > 0 ? ("Item " + staticId.ToString()) : "Item")
                : fallback;
        }

        private bool IsShopBuyGridCoinItem(in ShopBuyAllCandidate candidate)
        {
            return candidate.StoreMoneyType == ShopBuyAllStoreMoneyCurrency
                && candidate.CurrencyType == ShopBuyAllCurrencyCoin
                && candidate.IsUnlock
                && candidate.Price > 0
                && candidate.StoreId > 0
                && candidate.SlotId > 0
                && candidate.ItemId > 0;
        }

        private bool TryCollectShopBuyGridRows(int storeId, List<ShopBuyGridRow> rows, out string error)
        {
            error = null;
            rows.Clear();
            if (!this.TryInvokeAuraGetStoreGoodsData(storeId, out IntPtr listObj, out error))
            {
                return false;
            }

            List<IntPtr> elements = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, elements, pins) || elements.Count == 0)
                {
                    error = "Aura shop item list empty.";
                    return false;
                }

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

                    if (!this.IsShopBuyGridCoinItem(in candidate))
                    {
                        continue;
                    }

                    rows.Add(new ShopBuyGridRow
                    {
                        Item = candidate,
                        Name = this.ResolveShopBuyItemName(in candidate),
                        AlreadyOwned = this.IsShopBuyAllAlreadyOwned(null, in candidate)
                    });
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return true;
        }

        private void TickShopBuyGrid()
        {
            try
            {
                this.EnsureShopBuyGridHook();
                int index = this.forceOpenShopSelectedIndex;
                if (index == 6)
                {
                    this.shopBuyGridRows.Clear();
                    this.shopBuyGridStoreId = -1;
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint("Face Shop has no item grid.", null);
                    return;
                }

                if (!this.IsForceShopBuyAllSupported(index, out _))
                {
                    this.shopBuyGridRows.Clear();
                    this.shopBuyGridStoreId = -1;
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint(null, null);
                    return;
                }

                if (!this.IsGameDataQueryable)
                {
                    this.shopBuyGridRows.Clear();
                    this.shopBuyGridStoreId = -1;
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint("Enter the world to load this shop.", null);
                    return;
                }

                if (!this.TryResolveForceOpenShopStoreId(index, out int storeId, out _, out string unsupported))
                {
                    this.shopBuyGridRows.Clear();
                    this.shopBuyGridStoreId = -1;
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint(null, unsupported);
                    return;
                }

                if (storeId != this.shopBuyGridStoreId)
                {
                    this.shopBuyGridRows.Clear();
                    this.shopBuyGridStoreId = storeId;
                    this.shopBuyGridShowList = false;
                    this.shopBuyGridDirty = true;
                    this.shopBuyGridNextCollectAt = 0f;
                    this.SetShopBuyGridHint("Shop catalog is not loaded yet.", null);
                }

                bool hookInstalled = this.IsGameEventHookInstalled(ShopItemUpdateEventName);
                if (!this.shopBuyGridDirty && hookInstalled)
                {
                    return;
                }

                if (Time.unscaledTime < this.shopBuyGridNextCollectAt)
                {
                    return;
                }

                this.shopBuyGridNextCollectAt = Time.unscaledTime + ShopBuyGridCollectInterval;
                this.shopBuyGridDirty = false;
                if (!this.TryCollectShopBuyGridRows(storeId, this.shopBuyGridRows, out _))
                {
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint("Shop catalog is not loaded yet.", null);
                    return;
                }

                if (this.shopBuyGridRows.Count == 0)
                {
                    this.shopBuyGridShowList = false;
                    this.SetShopBuyGridHint("No coin items in this shop.", null);
                    return;
                }

                this.shopBuyGridShowList = true;
                this.SetShopBuyGridHint(null, null);
            }
            catch (Exception ex)
            {
                this.shopBuyGridRows.Clear();
                this.shopBuyGridShowList = false;
                this.SetShopBuyGridHint("Shop catalog is not loaded yet.", null);
                this.ShopBuyAllLog("grid collect failed: " + ex.Message, always: true);
            }
        }

        private IEnumerator ShopBuySelectedRoutine(int storeId, string label)
        {
            yield return null;
            yield return null;

            List<ShopBuyGridRow> rows = new List<ShopBuyGridRow>();
            Dictionary<string, int> picks;
            try
            {
                picks = this.SnapshotShopBuyPicks(storeId);
                if (picks.Count == 0)
                {
                    this.shopBuyAllStatus = "No items selected.";
                    this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                    this.FinishShopBuyAllRun();
                    yield break;
                }

                if (!this.TryCollectShopBuyGridRows(storeId, rows, out string prepError))
                {
                    this.shopBuyAllStatus = prepError ?? "Shop catalog is not loaded yet.";
                    this.ShopBuyAllLog(this.shopBuyAllStatus, always: true);
                    this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                    this.FinishShopBuyAllRun();
                    yield break;
                }
            }
            catch (Exception ex)
            {
                this.shopBuyAllStatus = "Shop buy-all error: " + ex.Message;
                FeatureLog.Fail("ShopBuyAll", this.shopBuyAllStatus);
                this.ShopBuyAllLog(this.shopBuyAllStatus, always: true);
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                this.FinishShopBuyAllRun();
                yield break;
            }

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
            int stopped = 0;
            int skipped = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                ShopBuyGridRow row = rows[i];
                ShopBuyAllCandidate item = row.Item;
                string key = this.ShopBuyPickKey(item.SlotId, item.ItemId);
                if (!picks.TryGetValue(key, out int wanted))
                {
                    continue;
                }

                picks.Remove(key);
                wanted = Mathf.Clamp(wanted, 1, 999999);
                if (row.AlreadyOwned)
                {
                    skipped++;
                    continue;
                }

                int stockCap = item.UnlimitedStock ? wanted : item.LeftCount;
                int unitPrice = item.Price;
                int affordable = 0;
                if (unitPrice > 0 && stockCap > 0 && coinBalance >= unitPrice)
                {
                    long byCoins = coinBalance / unitPrice;
                    affordable = (int)Math.Min(stockCap, byCoins);
                }

                int target = Math.Min(wanted, affordable);
                if (target <= 0)
                {
                    skipped++;
                    continue;
                }

                int sent = 0;
                while (sent < target)
                {
                    int chunk = clothingStore ? 1 : Math.Min(ShopBuyAllMaxPerCommand, target - sent);
                    bool ok = false;
                    string buyError = null;
                    try
                    {
                        ok = this.TryInvokeShopBuyItem(item, chunk, out buyError);
                    }
                    catch (Exception ex)
                    {
                        buyError = ex.Message;
                        ok = false;
                    }

                    if (!ok)
                    {
                        this.ShopBuyAllLog("buy failed " + row.Name + ": " + buyError, always: true);
                        break;
                    }

                    sent += chunk;
                    coinBalance -= (long)unitPrice * chunk;
                    this.shopBuyAllStatus = "Buying " + row.Name + " x" + sent + "/" + wanted;
                    if (sent < target)
                    {
                        yield return ModWait.Realtime(ShopBuyAllDelaySeconds);
                    }
                }

                if (sent <= 0)
                {
                    skipped++;
                }
                else
                {
                    bought++;
                    if (sent < wanted)
                    {
                        stopped++;
                    }
                }

                yield return ModWait.Realtime(ShopBuyAllDelaySeconds);
            }

            skipped += picks.Count;

            this.shopBuyAllStatus = "Done (" + label + "): bought " + bought + ", short " + stopped + ", skipped " + skipped + ".";
            FeatureLog.Life("ShopBuyAll", this.shopBuyAllStatus);
            this.AddMenuNotification(this.shopBuyAllStatus, new Color(0.55f, 1f, 0.65f));
            this.FinishShopBuyAllRun();
        }
    }
}
