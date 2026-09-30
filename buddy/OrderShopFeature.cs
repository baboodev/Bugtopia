using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Order machine ("Subscription" in the game's code) — live readout of the player's current
    // order for the UGUI Research / Order page, plus a state-aware "open the order panel" action.
    //
    // READ PATH — no generic invoke anywhere (invoking a runtime-inflated generic method is a
    // proven instant crash on this build), every call below is a plain static or valuetype method:
    //   PlayerDataCenter.GetSelfEcsEntity()                              -> boxed EcsEntity (player)
    //   StoreHelper.TryGetStoreEntity(in player, out NetworkEntityRef)   -> the player's store entity
    //   NetworkEntityRef.get_Entity()                                    -> boxed EcsEntity (store)
    //   EcsEntityExtensions.GetComponentValues(in store, ref object[])   -> every component, boxed
    //   the SubscriptionComponent box -> Id (storeGroupId) + RefreshTime (arrival, DateTime UTC)
    // Remaining = GameTimeUtility.ParseToUnixMs(RefreshTime) - GameTimeUtility.GetUnixTimeMs() —
    // exactly what ShowOrderedShopItemPanel counts down with. The game clock is NOT the PC clock
    // (measured 7 h apart), so never subtract DateTime.UtcNow from RefreshTime.
    // Item label = new ShopItemData(storeGroupId).name — the game's own localized string, e.g.
    // "Building Permit: Vibrant Adjustable Window". Cached per id.
    //
    // OPEN PATH: ShopSystem.GeOrderShopMachineState() (1 no order / 2 in transit / 3 arrived) picks
    // the panel the order machine itself would open (OpenOrderShopPanelCommand), then
    // UIManager.OpenView(Type, null) — where the machine's own path ends too (OpenInteractPanel ->
    // G2UOpenInteractPanelEvent -> DefaultModule.NoticeOpen -> PanelDef lookup -> OpenView). None of
    // the three panels has a PanelLogic, so a bare OpenView is the vanilla path, not an empty shell.
    // Verified live 2026-09-30, far from the machine.
    //
    // Polled, not event-driven: the only signals are DataUpdated/DataRemoved<SubscriptionComponent>
    // — generic event structs with an empty payload — and the page needs the data only while it is
    // on screen. The poll runs from the page's own 1 Hz tick (never while the menu is hidden), every
    // OrderShopPollInterval seconds, plus immediately when the page opens and after the button.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string OrderShopLogTag = "OrderShop";
        private const float OrderShopPollInterval = 15f;
        // The store entity carries ~4 components; GetComponentValues replaces a shorter array, so
        // this only has to be "usually enough" — a replacement array is pinned just the same.
        private const int OrderShopComponentSlots = 32;

        private const int OrderShopStateNoOrder = 1;
        private const int OrderShopStateInTransit = 2;
        private const int OrderShopStateArrived = 3;

        private struct OrderShopSnapshot
        {
            public bool Valid;        // a read has succeeded at least once this world
            public bool HasOrder;
            public int Id;            // storeGroupId of the ordered item
            public string Name;
            public long RemainingMs;  // at SampledAt, on the game clock
            public float SampledAt;   // Time.unscaledTime of the read
        }

        private OrderShopSnapshot orderShopSnapshot;
        private float orderShopNextPollAt;
        private int orderShopSnapshotEpoch = -1;
        private readonly Dictionary<int, string> orderShopNameCache = new Dictionary<int, string>();

        // Class / method handles are image-lifetime metadata (AGENTS.md §9: may stay raw).
        private bool orderShopResolved;
        private IntPtr orderShopPlayerDataCenterClass;
        private IntPtr orderShopGetSelfEntityMethod;
        private IntPtr orderShopTryGetStoreEntityMethod;
        private IntPtr orderShopEntityRefClass;
        private IntPtr orderShopEntityRefGetEntityMethod;
        private IntPtr orderShopGetComponentValuesMethod;
        private IntPtr orderShopSubscriptionClass;
        private IntPtr orderShopGetUnixTimeMsMethod;
        private IntPtr orderShopParseToUnixMsMethod;
        private IntPtr orderShopShopItemDataClass;
        private IntPtr orderShopShopItemDataCtor;
        private IntPtr orderShopObjectClass;
        private IntPtr orderShopShopSystemClass;
        private IntPtr orderShopGetMachineStateMethod;

        // Remaining time as of now, interpolated from the last read. Only meaningful for HasOrder.
        private long GetOrderShopRemainingMsNow()
        {
            OrderShopSnapshot snap = this.orderShopSnapshot;
            return snap.RemainingMs - (long)((Time.unscaledTime - snap.SampledAt) * 1000f);
        }

        // Called from the page's 1 Hz tick. force = page just opened / button just pressed.
        private void RefreshOrderShopSnapshotIfDue(bool force)
        {
            float now = Time.unscaledTime;
            if (!force && now < this.orderShopNextPollAt)
            {
                return;
            }
            this.orderShopNextPollAt = now + OrderShopPollInterval;

            // A world change invalidates the old order data (another account, or not loaded yet).
            int epoch = AuraMonoWorldEpoch;
            if (epoch != this.orderShopSnapshotEpoch)
            {
                this.orderShopSnapshotEpoch = epoch;
                this.orderShopSnapshot = default(OrderShopSnapshot);
            }

            if (!this.IsWorldReady)
            {
                return;
            }

            if (this.TryReadOrderShopSnapshot(out OrderShopSnapshot fresh, out string error))
            {
                this.orderShopSnapshot = fresh;
                FeatureLog.Once(OrderShopLogTag, "first-read",
                    "Order readout working: " + (fresh.HasOrder
                        ? "order " + fresh.Id + " (" + fresh.Name + "), " + fresh.RemainingMs + " ms left."
                        : "no active order."));
            }
            else
            {
                FeatureLog.Fail(OrderShopLogTag, "Order readout failed: " + error);
            }
        }

        private bool EnsureOrderShopResolved(out string error)
        {
            error = null;
            if (this.orderShopResolved)
            {
                return true;
            }

            this.orderShopPlayerDataCenterClass = this.FindAuraMonoClassAnySpelling("XDTDataAndProtocol.PlayerDataCenter");
            IntPtr storeHelper = this.FindAuraMonoClassAnySpelling("XDT.Scene.Shared.Modules.DepartmentStore.StoreHelper");
            this.orderShopEntityRefClass = this.FindAuraMonoClassAnySpelling("XD.GameGerm.Ecs.Boost.Network.NetworkEntityRef");
            IntPtr entityExtensions = this.FindAuraMonoClassAnySpelling("XD.GameGerm.Ecs.EcsEntityExtensions");
            this.orderShopSubscriptionClass = this.FindAuraMonoClassAnySpelling("EcsClient.XDT.Scene.Shared.Modules.DepartmentStore.SubscriptionComponent");
            IntPtr gameTime = this.FindAuraMonoClassAnySpelling("XDTDataAndProtocol.ProtocolService.GameTimeUtility");
            this.orderShopShopItemDataClass = this.FindAuraMonoClassAnySpelling("XDTGameSystem.GameplaySystem.Shop.ShopItemData");
            this.orderShopObjectClass = this.FindAuraMonoClassAnySpelling("System.Object");
            this.orderShopShopSystemClass = this.FindAuraMonoClassAnySpelling("XDTGameSystem.GameplaySystem.Shop.ShopSystem");

            this.orderShopGetSelfEntityMethod = this.FindAuraMonoMethodOnHierarchy(this.orderShopPlayerDataCenterClass, "GetSelfEcsEntity", 0);
            this.orderShopTryGetStoreEntityMethod = this.FindAuraMonoMethodOnHierarchy(storeHelper, "TryGetStoreEntity", 2);
            this.orderShopEntityRefGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(this.orderShopEntityRefClass, "get_Entity", 0);
            this.orderShopGetComponentValuesMethod = this.FindAuraMonoMethodOnHierarchy(entityExtensions, "GetComponentValues", 2);
            this.orderShopGetUnixTimeMsMethod = this.FindAuraMonoMethodOnHierarchy(gameTime, "GetUnixTimeMs", 0);
            this.orderShopParseToUnixMsMethod = this.FindAuraMonoMethodOnHierarchy(gameTime, "ParseToUnixMs", 1);
            // ShopItemData has exactly one 1-parameter constructor: (int storeGroupId).
            this.orderShopShopItemDataCtor = this.FindAuraMonoMethodOnHierarchy(this.orderShopShopItemDataClass, ".ctor", 1);
            this.orderShopGetMachineStateMethod = this.FindAuraMonoMethodOnHierarchy(this.orderShopShopSystemClass, "GeOrderShopMachineState", 0);

            string missing = string.Empty;
            if (this.orderShopGetSelfEntityMethod == IntPtr.Zero) missing += " PlayerDataCenter.GetSelfEcsEntity";
            if (this.orderShopTryGetStoreEntityMethod == IntPtr.Zero) missing += " StoreHelper.TryGetStoreEntity";
            if (this.orderShopEntityRefGetEntityMethod == IntPtr.Zero) missing += " NetworkEntityRef.get_Entity";
            if (this.orderShopGetComponentValuesMethod == IntPtr.Zero) missing += " EcsEntityExtensions.GetComponentValues";
            if (this.orderShopSubscriptionClass == IntPtr.Zero) missing += " SubscriptionComponent";
            if (this.orderShopGetUnixTimeMsMethod == IntPtr.Zero) missing += " GameTimeUtility.GetUnixTimeMs";
            if (this.orderShopParseToUnixMsMethod == IntPtr.Zero) missing += " GameTimeUtility.ParseToUnixMs";
            if (this.orderShopObjectClass == IntPtr.Zero) missing += " System.Object";
            if (missing.Length > 0)
            {
                // Not latched: a class that is missing on the first try (image not loaded yet) is
                // retried on the next poll, which is at most every OrderShopPollInterval seconds.
                error = "unresolved:" + missing;
                return false;
            }

            this.orderShopResolved = true;
            return true;
        }

        private unsafe bool TryReadOrderShopSnapshot(out OrderShopSnapshot snapshot, out string error)
        {
            snapshot = default(OrderShopSnapshot);
            error = null;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                error = "AuraMono runtime not ready";
                return false;
            }
            if (!AuraMonoPinningAvailable || auraMonoObjectNew == null || auraMonoObjectUnbox == null
                || auraMonoArrayNew == null || auraMonoArrayAddrWithSize == null || auraMonoObjectGetClass == null)
            {
                // Fail closed: every object below is held across allocating calls.
                error = "pinning or object/array exports unavailable";
                return false;
            }
            if (!this.EnsureOrderShopResolved(out error))
            {
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                // 1. Player entity (boxed EcsEntity — it carries an EcsWorld reference, so it lives
                //    in a pinned managed box, never in native memory the GC cannot see).
                if (!TryAuraInvoke(this.orderShopGetSelfEntityMethod, IntPtr.Zero, IntPtr.Zero, out IntPtr playerBox, out error)
                    || playerBox == IntPtr.Zero)
                {
                    error = "GetSelfEcsEntity: " + (error ?? "null");
                    return false;
                }
                if (!OrderShopPin(playerBox, pins))
                {
                    error = "pin failed (player entity)";
                    return false;
                }

                // 2. Store entity ref, written into a pinned NetworkEntityRef box (out param).
                IntPtr entityRefBox = auraMonoObjectNew(this.auraMonoRootDomain, this.orderShopEntityRefClass);
                if (entityRefBox == IntPtr.Zero || !OrderShopPin(entityRefBox, pins))
                {
                    error = "could not allocate NetworkEntityRef";
                    return false;
                }

                IntPtr* storeArgs = stackalloc IntPtr[2];
                storeArgs[0] = auraMonoObjectUnbox(playerBox);
                storeArgs[1] = auraMonoObjectUnbox(entityRefBox);
                if (!TryAuraInvoke(this.orderShopTryGetStoreEntityMethod, IntPtr.Zero, (IntPtr)storeArgs, out IntPtr hasStoreBox, out error)
                    || hasStoreBox == IntPtr.Zero)
                {
                    error = "TryGetStoreEntity: " + (error ?? "null");
                    return false;
                }
                if (!this.TryUnboxMonoBoolean(hasStoreBox, out bool hasStore))
                {
                    error = "TryGetStoreEntity returned a non-boolean";
                    return false;
                }
                if (!hasStore)
                {
                    // No store entity = no order (ShopSystem.GeOrderShopMachineState returns 1 here too).
                    snapshot.Valid = true;
                    snapshot.SampledAt = Time.unscaledTime;
                    return true;
                }

                // 3. Store EcsEntity (valuetype instance method: `this` is the UNBOXED struct).
                if (!TryAuraInvoke(this.orderShopEntityRefGetEntityMethod, auraMonoObjectUnbox(entityRefBox), IntPtr.Zero, out IntPtr storeBox, out error)
                    || storeBox == IntPtr.Zero)
                {
                    error = "NetworkEntityRef.get_Entity: " + (error ?? "null");
                    return false;
                }
                if (!OrderShopPin(storeBox, pins))
                {
                    error = "pin failed (store entity)";
                    return false;
                }

                // 4. Every component of the store entity, boxed into an object[] we own and pin.
                IntPtr array = auraMonoArrayNew(this.auraMonoRootDomain, this.orderShopObjectClass, (UIntPtr)OrderShopComponentSlots);
                if (array == IntPtr.Zero || !OrderShopPin(array, pins))
                {
                    error = "could not allocate object[]";
                    return false;
                }

                // `ref object[]` slot on OUR stack: mono's conservative stack scan sees it, so a
                // replacement array written here stays alive until it is pinned below.
                IntPtr* arraySlot = stackalloc IntPtr[1];
                arraySlot[0] = array;
                IntPtr* valuesArgs = stackalloc IntPtr[2];
                valuesArgs[0] = auraMonoObjectUnbox(storeBox);
                valuesArgs[1] = (IntPtr)arraySlot;
                if (!TryAuraInvoke(this.orderShopGetComponentValuesMethod, IntPtr.Zero, (IntPtr)valuesArgs, out IntPtr countBox, out error)
                    || countBox == IntPtr.Zero)
                {
                    error = "GetComponentValues: " + (error ?? "null");
                    return false;
                }
                if (arraySlot[0] != array)
                {
                    array = arraySlot[0];
                    if (array == IntPtr.Zero || !OrderShopPin(array, pins))
                    {
                        error = "pin failed (replacement component array)";
                        return false;
                    }
                }
                if (!this.TryUnboxMonoInt32(countBox, out int count) || count < 0)
                {
                    error = "GetComponentValues returned a bad count";
                    return false;
                }

                IntPtr subscription = IntPtr.Zero;
                for (int i = 0; i < count; i++)
                {
                    IntPtr slot = auraMonoArrayAddrWithSize(array, IntPtr.Size, (UIntPtr)(uint)i);
                    IntPtr item = slot != IntPtr.Zero ? *(IntPtr*)slot : IntPtr.Zero;
                    if (item != IntPtr.Zero && auraMonoObjectGetClass(item) == this.orderShopSubscriptionClass)
                    {
                        subscription = item;
                        break;
                    }
                }

                snapshot.Valid = true;
                snapshot.SampledAt = Time.unscaledTime;
                if (subscription == IntPtr.Zero)
                {
                    return true; // store entity without a subscription = no active order
                }
                if (!OrderShopPin(subscription, pins))
                {
                    error = "pin failed (subscription)";
                    return false;
                }

                // 5. Id + RefreshTime. DateTime is 8 bytes (ticks + kind bits); it is passed back to
                //    the game unchanged, so its kind never has to be interpreted here.
                snapshot.Id = this.TryReadAuraMonoStructIntField(subscription, "Id");
                if (!this.TryGetMonoObjectMember(subscription, "RefreshTime", out IntPtr refreshBox)
                    || refreshBox == IntPtr.Zero || !this.TryAuraMonoBoxedIsValueType(refreshBox))
                {
                    error = "SubscriptionComponent.RefreshTime unreadable";
                    return false;
                }
                long refreshRaw = *(long*)auraMonoObjectUnbox(refreshBox);

                IntPtr* parseArgs = stackalloc IntPtr[1];
                parseArgs[0] = (IntPtr)(&refreshRaw);
                if (!TryAuraInvoke(this.orderShopParseToUnixMsMethod, IntPtr.Zero, (IntPtr)parseArgs, out IntPtr arrivalBox, out error)
                    || !this.TryUnboxMonoInt64(arrivalBox, out long arrivalMs))
                {
                    error = "ParseToUnixMs: " + (error ?? "bad result");
                    return false;
                }
                if (!TryAuraInvoke(this.orderShopGetUnixTimeMsMethod, IntPtr.Zero, IntPtr.Zero, out IntPtr nowBox, out error)
                    || !this.TryUnboxMonoInt64(nowBox, out long nowMs) || nowMs <= 0L)
                {
                    error = "GetUnixTimeMs: " + (error ?? "bad result");
                    return false;
                }

                snapshot.HasOrder = true;
                snapshot.RemainingMs = arrivalMs - nowMs;
                snapshot.Name = this.ResolveOrderShopItemName(snapshot.Id);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private static bool OrderShopPin(IntPtr obj, List<uint> pins)
        {
            uint handle = AuraMonoPinNew(obj);
            if (handle == 0U)
            {
                return false;
            }
            pins.Add(handle);
            return true;
        }

        private unsafe bool TryUnboxMonoInt64(IntPtr boxed, out long value)
        {
            value = 0L;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(long*)raw;
            return true;
        }

        // The game's own label for the ordered item, via new ShopItemData(storeGroupId).name. The
        // struct is built inside a pinned box so every reference it stores is visible to the GC.
        private unsafe string ResolveOrderShopItemName(int storeGroupId)
        {
            if (storeGroupId <= 0)
            {
                return string.Empty;
            }
            if (this.orderShopNameCache.TryGetValue(storeGroupId, out string cached))
            {
                return cached;
            }

            string fallback = this.LF("Item #{0}", storeGroupId);
            if (this.orderShopShopItemDataCtor == IntPtr.Zero)
            {
                return fallback;
            }

            IntPtr box = auraMonoObjectNew(this.auraMonoRootDomain, this.orderShopShopItemDataClass);
            uint pin = box != IntPtr.Zero ? AuraMonoPinNew(box) : 0U;
            if (pin == 0U)
            {
                return fallback;
            }

            try
            {
                int id = storeGroupId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                if (!TryAuraInvoke(this.orderShopShopItemDataCtor, auraMonoObjectUnbox(box), (IntPtr)args, out _, out string error))
                {
                    FeatureLog.Fail(OrderShopLogTag, "ShopItemData(" + storeGroupId + ") failed: " + error);
                    return fallback;
                }

                string name = this.TryReadMonoStringMemberOrEmpty(box, "name");
                if (string.IsNullOrEmpty(name))
                {
                    return fallback;
                }

                this.orderShopNameCache[storeGroupId] = name;
                return name;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // The state the order machine itself branches on. Falls back to the last snapshot when
        // ShopSystem cannot be reached (its DataModule only exists in the main level).
        private unsafe int ResolveOrderShopMachineState()
        {
            if (this.orderShopGetMachineStateMethod != IntPtr.Zero && this.orderShopShopSystemClass != IntPtr.Zero)
            {
                IntPtr shopSystem = this.TryGetAuraMonoDataModuleInstance(this.orderShopShopSystemClass);
                uint pin = shopSystem != IntPtr.Zero ? AuraMonoPinNew(shopSystem) : 0U;
                if (pin != 0U)
                {
                    try
                    {
                        if (TryAuraInvoke(this.orderShopGetMachineStateMethod, shopSystem, IntPtr.Zero, out IntPtr stateBox, out _)
                            && this.TryUnboxMonoInt32(stateBox, out int state)
                            && state >= OrderShopStateNoOrder && state <= OrderShopStateArrived)
                        {
                            return state;
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(pin);
                    }
                }
            }

            OrderShopSnapshot snap = this.orderShopSnapshot;
            if (!snap.HasOrder)
            {
                return OrderShopStateNoOrder;
            }
            return this.GetOrderShopRemainingMsNow() > 0L ? OrderShopStateInTransit : OrderShopStateArrived;
        }

        // "OPEN ORDER PANEL": the panel the order machine would open right now, from anywhere.
        // Returns true when OpenView was invoked; `userStatus` is a short plain line for the page.
        private bool TryOpenOrderShopPanel(out string userStatus)
        {
            userStatus = this.L("Could not open the order panel — see bugtopia.log.");
            if (!this.IsWorldReady)
            {
                userStatus = this.L("Order data unavailable here.");
                return false;
            }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                FeatureLog.Fail(OrderShopLogTag, "Open order panel: AuraMono runtime not ready.");
                return false;
            }
            // Resolution failures are logged by the read path; the open path only needs ShopSystem,
            // which ResolveOrderShopMachineState tolerates missing.
            this.EnsureOrderShopResolved(out _);

            int state = this.ResolveOrderShopMachineState();
            string panel = state == OrderShopStateArrived
                ? "OrderShopBuyPanel"
                : state == OrderShopStateInTransit ? "ShowOrderedShopItemPanel" : "OrderShopItemPanel";

            bool opened = this.TryOpenAuraPanelByTypeNameViaMono("XDTGame.UI.Panel." + panel, "Opened " + panel + ".");
            FeatureLog.Life(OrderShopLogTag, "Open order panel: state " + state + " -> " + panel
                + (opened ? " opened." : " FAILED: " + (this.forceOpenShopStatus ?? "unknown")));
            if (!opened)
            {
                FeatureLog.Fail(OrderShopLogTag, "OpenView(" + panel + ") failed: " + (this.forceOpenShopStatus ?? "unknown"));
                return false;
            }

            // The panel may change the order (buy / give up / new order) — re-read on the next tick.
            this.orderShopNextPollAt = 0f;
            userStatus = this.L("Order panel opened.");
            return true;
        }

        // "1d 22h 35m" — minute-granular, like the research countdowns.
        private static string FormatOrderShopRemaining(long remainingMs)
        {
            if (remainingMs <= 0L)
            {
                return "<1m";
            }

            long totalMinutes = remainingMs / 60000L;
            long days = totalMinutes / 1440L;
            long hours = (totalMinutes / 60L) % 24L;
            long minutes = totalMinutes % 60L;
            if (days > 0L)
            {
                return days + "d " + hours + "h " + minutes + "m";
            }
            if (hours > 0L)
            {
                return hours + "h " + minutes + "m";
            }
            return minutes > 0L ? minutes + "m" : "<1m";
        }
    }
}
