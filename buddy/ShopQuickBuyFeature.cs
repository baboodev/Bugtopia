using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private string shopQuickBuyStoreIdInput = string.Empty;
        private string shopQuickBuySlotIdInput = string.Empty;
        private string shopQuickBuyItemIdInput = string.Empty;
        private string shopQuickBuyStatus = "Idle.";

        private IntPtr shopQuickBuyAuraGetShopItemDataMethod = IntPtr.Zero;
        private IntPtr shopQuickBuyAuraSalePanelOpenMethod = IntPtr.Zero;

        private void StartShopQuickBuyOpenPanel()
        {
            if (!this.TryParseShopQuickBuyInputs(out int storeId, out int slotId, out int itemId, out string parseError))
            {
                this.shopQuickBuyStatus = parseError;
                this.AddMenuNotification(parseError, new Color(1f, 0.55f, 0.45f));
                return;
            }

            if (this.TryOpenSalePanelViaQuickBuyItemManaged(storeId, slotId, itemId, out string managedError))
            {
                this.shopQuickBuyStatus = "Opened SalePanel (managed) store=" + storeId + " slot=" + slotId + " item=" + itemId;
                this.AddMenuNotification(this.shopQuickBuyStatus, new Color(0.45f, 0.88f, 1f));
                return;
            }

            if (this.TryOpenSalePanelViaQuickBuyItemAura(storeId, slotId, itemId, out string auraError))
            {
                this.shopQuickBuyStatus = "Opened SalePanel (aura) store=" + storeId + " slot=" + slotId + " item=" + itemId;
                this.AddMenuNotification(this.shopQuickBuyStatus, new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.shopQuickBuyStatus = auraError ?? managedError ?? "Open failed.";
            this.AddMenuNotification(this.shopQuickBuyStatus, new Color(1f, 0.55f, 0.45f));
        }

        private bool TryParseShopQuickBuyInputs(out int storeId, out int slotId, out int itemId, out string error)
        {
            storeId = 0;
            slotId = 0;
            itemId = 0;
            error = null;

            if (!int.TryParse((this.shopQuickBuyStoreIdInput ?? string.Empty).Trim(), out storeId) || storeId <= 0)
            {
                error = "Invalid storeId.";
                return false;
            }

            if (!int.TryParse((this.shopQuickBuySlotIdInput ?? string.Empty).Trim(), out slotId) || slotId <= 0)
            {
                error = "Invalid slotId.";
                return false;
            }

            if (!int.TryParse((this.shopQuickBuyItemIdInput ?? string.Empty).Trim(), out itemId) || itemId <= 0)
            {
                error = "Invalid itemId.";
                return false;
            }

            return true;
        }

        private bool TryOpenSalePanelViaQuickBuyItemManaged(int storeId, int slotId, int itemId, out string error)
        {
            error = null;
            try
            {
                Type shopType = this.FindLoadedType(
                    "XDTGameSystem.GameplaySystem.Shop.ShopSystem",
                    "ShopSystem");
                Type quickBuyType = this.FindLoadedType(
                    "XDTGameSystem.GameplaySystem.Shop.QuickBuyItem",
                    "QuickBuyItem");
                Type salePanelType = this.FindLoadedType(
                    "XDTGame.UI.Panel.SalePanel",
                    "SalePanel");
                if (shopType == null || quickBuyType == null || salePanelType == null)
                {
                    error = "managed types missing (ShopSystem/QuickBuyItem/SalePanel).";
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
                    error = "managed ShopSystem instance missing.";
                    return false;
                }

                object quickBuy = Activator.CreateInstance(quickBuyType);
                this.TrySetManagedInt32Member(quickBuy, "StoreId", storeId);
                this.TrySetManagedInt32Member(quickBuy, "SlotId", slotId);
                this.TrySetManagedInt32Member(quickBuy, "ItemId", itemId);

                MethodInfo getShopItemData = shopType.GetMethod(
                    "GetShopItemData",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { quickBuyType },
                    null);
                if (getShopItemData == null)
                {
                    error = "managed GetShopItemData(QuickBuyItem) missing.";
                    return false;
                }

                object shopItemData = getShopItemData.Invoke(shopObj, new[] { quickBuy });
                if (!this.TryValidateManagedShopItemData(shopItemData, storeId, slotId, itemId, out error))
                {
                    return false;
                }

                MethodInfo openMethod = null;
                foreach (MethodInfo candidate in salePanelType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(candidate.Name, "Open", StringComparison.Ordinal) || candidate.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType.Name != "ShopItemData")
                    {
                        continue;
                    }

                    openMethod = candidate;
                    break;
                }

                if (openMethod == null)
                {
                    error = "managed SalePanel.Open missing.";
                    return false;
                }

                ParameterInfo[] openParams = openMethod.GetParameters();
                object[] invokeArgs = new object[openParams.Length];
                invokeArgs[0] = shopItemData;
                for (int i = 1; i < openParams.Length; i++)
                {
                    invokeArgs[i] = openParams[i].DefaultValue ?? Type.Missing;
                }

                openMethod.Invoke(null, invokeArgs);
                return true;
            }
            catch (Exception ex)
            {
                error = "managed open failed: " + ex.Message;
                return false;
            }
        }

        private bool TryValidateManagedShopItemData(object shopItemData, int storeId, int slotId, int itemId, out string error)
        {
            error = null;
            if (shopItemData == null)
            {
                error = "GetShopItemData returned null (item not in runtime shop).";
                return false;
            }

            if (!this.TryReadManagedShopItemData(shopItemData, out ShopBuyAllCandidate candidate))
            {
                error = "GetShopItemData returned unreadable ShopItemData.";
                return false;
            }

            if (candidate.NetId == 0U && candidate.ItemId <= 0)
            {
                error = "Item not in runtime shop (_storeItemData empty for itemId=" + itemId + ").";
                return false;
            }

            if (candidate.StoreId > 0 && candidate.StoreId != storeId)
            {
                error = "Resolved storeId=" + candidate.StoreId + " expected " + storeId + ".";
                return false;
            }

            if (candidate.SlotId > 0 && candidate.SlotId != slotId)
            {
                error = "Resolved slotId=" + candidate.SlotId + " expected " + slotId + ".";
                return false;
            }

            if (candidate.ItemId > 0 && candidate.ItemId != itemId)
            {
                error = "Resolved itemId=" + candidate.ItemId + " expected " + itemId + ".";
                return false;
            }

            return true;
        }

        private bool TrySetManagedInt32Member(object target, string memberName, int value)
        {
            if (target == null)
            {
                return false;
            }

            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }

            return false;
        }

        private unsafe bool TryOpenSalePanelViaQuickBuyItemAura(int storeId, int slotId, int itemId, out string error)
        {
            error = null;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                error = "AuraMono runtime not ready.";
                return false;
            }

            if (!this.TryEnsureShopBuyAllAuraShopSystem(out error))
            {
                return false;
            }

            IntPtr shopClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(this.shopBuyAllShopSystemObj) : IntPtr.Zero;
            if (shopClass == IntPtr.Zero)
            {
                error = "Aura ShopSystem class missing.";
                return false;
            }

            IntPtr salePanelClass = this.FindAuraMonoClassByFullName("XDTGame.UI.Panel.SalePanel");
            if (salePanelClass == IntPtr.Zero)
            {
                error = "Aura SalePanel class missing.";
                return false;
            }

            if (this.shopQuickBuyAuraSalePanelOpenMethod == IntPtr.Zero)
            {
                this.shopQuickBuyAuraSalePanelOpenMethod = this.FindAuraMonoMethodOnHierarchy(salePanelClass, "Open", 8);
                if (this.shopQuickBuyAuraSalePanelOpenMethod == IntPtr.Zero)
                {
                    this.shopQuickBuyAuraSalePanelOpenMethod = this.FindAuraMonoMethodOnHierarchy(salePanelClass, "Open", 1);
                }
            }

            if (this.shopQuickBuyAuraSalePanelOpenMethod == IntPtr.Zero)
            {
                error = "Aura SalePanel.Open missing.";
                return false;
            }

            if (!this.TryInvokeAuraGetShopItemDataQuickBuy(
                    shopClass,
                    storeId,
                    slotId,
                    itemId,
                    out IntPtr shopItemBoxed,
                    out ShopBuyAllCandidate candidate,
                    out error))
            {
                return false;
            }

            IntPtr shopItemPtr = auraMonoObjectUnbox(shopItemBoxed);
            if (shopItemPtr == IntPtr.Zero)
            {
                error = "Aura ShopItemData unbox failed.";
                return false;
            }

            int openParamCount = this.TryGetAuraMonoMethodParamCount(this.shopQuickBuyAuraSalePanelOpenMethod);
            if (openParamCount <= 0)
            {
                openParamCount = 8;
            }

            IntPtr* openArgs = stackalloc IntPtr[openParamCount];
            openArgs[0] = shopItemPtr;
            int falseValue = 0;
            for (int i = 1; i < openParamCount; i++)
            {
                ParameterTypeKind kind = this.TryGetAuraMonoMethodParameterKind(this.shopQuickBuyAuraSalePanelOpenMethod, i);
                openArgs[i] = kind == ParameterTypeKind.Boolean ? (IntPtr)(&falseValue) : IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.shopQuickBuyAuraSalePanelOpenMethod, IntPtr.Zero, (IntPtr)openArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "Aura SalePanel.Open invoke failed.";
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeAuraGetShopItemDataQuickBuy(
            IntPtr shopClass,
            int storeId,
            int slotId,
            int itemId,
            out IntPtr shopItemBoxed,
            out ShopBuyAllCandidate candidate,
            out string error)
        {
            shopItemBoxed = IntPtr.Zero;
            candidate = default(ShopBuyAllCandidate);
            error = null;

            List<IntPtr> candidates = this.CollectAuraGetShopItemDataCandidates(shopClass);
            if (candidates.Count == 0)
            {
                error = "Aura GetShopItemData(QuickBuyItem) missing.";
                return false;
            }

            byte* quickBuyPtr = stackalloc byte[12];
            *(int*)(quickBuyPtr + 0) = storeId;
            *(int*)(quickBuyPtr + 4) = slotId;
            *(int*)(quickBuyPtr + 8) = itemId;
            IntPtr* getArgs = stackalloc IntPtr[1];
            getArgs[0] = (IntPtr)quickBuyPtr;

            for (int i = 0; i < candidates.Count; i++)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(candidates[i], this.shopBuyAllShopSystemObj, (IntPtr)getArgs, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryReadAuraShopItemData(boxed, out candidate))
                {
                    continue;
                }

                if (candidate.NetId == 0U && candidate.ItemId <= 0)
                {
                    continue;
                }

                if (candidate.ItemId > 0 && candidate.ItemId != itemId)
                {
                    continue;
                }

                shopItemBoxed = boxed;
                this.shopQuickBuyAuraGetShopItemDataMethod = candidates[i];
                return true;
            }

            error = "Item not in runtime shop (_storeItemData empty for itemId=" + itemId + ").";
            return false;
        }

        private List<IntPtr> CollectAuraGetShopItemDataCandidates(IntPtr shopClass)
        {
            List<IntPtr> candidates = new List<IntPtr>();
            if (shopClass == IntPtr.Zero || auraMonoClassGetMethods == null || auraMonoMethodGetName == null)
            {
                return candidates;
            }

            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr method = auraMonoClassGetMethods(shopClass, ref iter);
                if (method == IntPtr.Zero)
                {
                    break;
                }

                string methodName = Marshal.PtrToStringAnsi(auraMonoMethodGetName(method)) ?? string.Empty;
                if (!string.Equals(methodName, "GetShopItemData", StringComparison.Ordinal)
                    || this.TryGetAuraMonoMethodParamCount(method) != 1)
                {
                    continue;
                }

                candidates.Add(method);
            }

            return candidates;
        }

        private enum ParameterTypeKind
        {
            Unknown,
            Boolean,
            Reference
        }

        private ParameterTypeKind TryGetAuraMonoMethodParameterKind(IntPtr method, int parameterIndex)
        {
            if (method == IntPtr.Zero || auraMonoMethodGetName == null)
            {
                return ParameterTypeKind.Unknown;
            }

            string methodName = Marshal.PtrToStringAnsi(auraMonoMethodGetName(method)) ?? string.Empty;
            if (string.Equals(methodName, "Open", StringComparison.Ordinal) && parameterIndex >= 3)
            {
                return ParameterTypeKind.Boolean;
            }

            return ParameterTypeKind.Reference;
        }

    }
}
