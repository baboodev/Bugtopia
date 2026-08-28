using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // MASS COOK INGREDIENT PICKING — choose what goes into each material slot instead of taking
    // whatever the game's AutoFill grabbed.
    //
    // A recipe is a flat list of MaterialSlot. Each slot is either a specific item or an
    // "any <category>" slot, and the game fills them itself when cooking starts: AutoFill picks the
    // cheapest match by price. That is usually what you want, and it is what Automatic mode keeps.
    // Manual mode exists for the case AutoFill cannot express: spend THIS fish, not that one.
    //
    // WHY PREFERENCES ARE KEYED BY staticId AND NOT netId. A slot is filled with
    // FillMaterialInSlot(slot, netId, staticId), and netIds are reassigned every session. Saving a
    // netId would produce a feature that works today and silently stops working tomorrow, filling
    // nothing while reporting success. So the preference stores the ITEM KIND, and the netId of a
    // matching stack is resolved at cook time from GetSlotMaterials — which is also what keeps a
    // single stack from over-filling a recipe, since the game subtracts units already consumed by
    // earlier slots.
    //
    // FALLBACK IS NOT OPTIONAL. A preferred item can be gone by the time you cook. Manual mode
    // therefore never blocks: a slot whose preference cannot be satisfied is left exactly as
    // AutoFill left it, and the Universal top-up still applies afterwards if it is enabled. Manual
    // is a preference, not a constraint.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // recipeId -> slotIndex -> item staticId. Persisted.
        private readonly Dictionary<int, Dictionary<int, int>> netCookSlotPrefs = new Dictionary<int, Dictionary<int, int>>();

        internal bool HasNetCookSlotPreference(int recipeId, int slotIndex)
        {
            return this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> slots)
                && slots.ContainsKey(slotIndex);
        }

        internal int GetNetCookSlotPreference(int recipeId, int slotIndex)
        {
            if (this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> slots)
                && slots.TryGetValue(slotIndex, out int staticId))
            {
                return staticId;
            }

            return 0;
        }

        internal void SetNetCookSlotPreference(int recipeId, int slotIndex, int staticId)
        {
            if (recipeId <= 0 || slotIndex < 0)
            {
                return;
            }

            if (staticId <= 0)
            {
                if (this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> existing))
                {
                    existing.Remove(slotIndex);
                    if (existing.Count == 0)
                    {
                        this.netCookSlotPrefs.Remove(recipeId);
                    }
                }
            }
            else
            {
                if (!this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> slots))
                {
                    slots = new Dictionary<int, int>();
                    this.netCookSlotPrefs[recipeId] = slots;
                }

                slots[slotIndex] = staticId;
            }

            this.SaveKeybinds();
        }

        internal void ClearNetCookSlotPreferences(int recipeId)
        {
            if (this.netCookSlotPrefs.Remove(recipeId))
            {
                this.SaveKeybinds();
            }
        }

        // ---------------------------------------------------------------- persistence

        // Flat "recipeId:slotIndex=staticId" list. A nested structure would need a schema change in
        // UnifiedConfigData for something a string round-trips fine.
        internal string SerializeNetCookSlotPrefs()
        {
            if (this.netCookSlotPrefs.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, Dictionary<int, int>> recipe in this.netCookSlotPrefs)
            {
                foreach (KeyValuePair<int, int> slot in recipe.Value)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(';');
                    }

                    sb.Append(recipe.Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(slot.Key.ToString(CultureInfo.InvariantCulture)).Append('=')
                      .Append(slot.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }

        internal void DeserializeNetCookSlotPrefs(string raw)
        {
            this.netCookSlotPrefs.Clear();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (string entry in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                int colon = entry.IndexOf(':');
                int eq = entry.IndexOf('=');
                if (colon <= 0 || eq <= colon + 1)
                {
                    continue;
                }

                if (int.TryParse(entry.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int recipeId)
                    && int.TryParse(entry.Substring(colon + 1, eq - colon - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotIndex)
                    && int.TryParse(entry.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int staticId)
                    && recipeId > 0 && slotIndex >= 0 && staticId > 0)
                {
                    if (!this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> slots))
                    {
                        slots = new Dictionary<int, int>();
                        this.netCookSlotPrefs[recipeId] = slots;
                    }

                    slots[slotIndex] = staticId;
                }
            }
        }

        // ---------------------------------------------------------------- cookable filter

        // "Only recipes I can cook right now."
        //
        // Answered with the same TryComputeNetCookMaxQuantity the DISH LIMIT row already uses, so a
        // recipe counts as cookable exactly when the mod would be able to start one dish of it —
        // including the warehouse when Move Ingredients is on, because that is the stock the cook
        // would actually draw from.
        //
        // Throttled and cached because it is not free: the first call for a recipe resolves its
        // requirement list through AuraMono. The list is then reused (netCookRecipeRequirementsCache
        // holds the per-recipe requirements), so a refresh over a few dozen recipes is arithmetic.
        // Recomputing per frame would still be wasteful, hence the interval.
        private readonly Dictionary<int, bool> netCookCookableCache = new Dictionary<int, bool>();
        private float nextNetCookCookableRefreshAt = 0f;
        private bool netCookCookableCacheMoveIngredients = false;

        private const float NetCookCookableRefreshSeconds = 3f;

        internal bool IsNetCookRecipeCookable(int recipeId)
        {
            if (recipeId <= 0)
            {
                return false;
            }

            if (this.netCookCookableCache.TryGetValue(recipeId, out bool cookable))
            {
                return cookable;
            }

            // Unknown entry: answer optimistically and let the next refresh correct it. Hiding a
            // recipe because it has not been measured yet would make the list flicker on open.
            return true;
        }

        internal void RefreshNetCookCookableCache(List<KeyValuePair<int, string>> entries, bool force = false)
        {
            if (!this.netCookCookableOnly || entries == null || entries.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            // Move Ingredients changes the answer (warehouse stock counts or it does not), so a
            // toggle flip invalidates rather than waits out the interval.
            bool sourceChanged = this.netCookCookableCacheMoveIngredients != this.netCookMoveIngredients;
            if (!force && !sourceChanged && now < this.nextNetCookCookableRefreshAt)
            {
                return;
            }

            this.nextNetCookCookableRefreshAt = now + NetCookCookableRefreshSeconds;
            this.netCookCookableCacheMoveIngredients = this.netCookMoveIngredients;
            this.netCookCookableCache.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                int recipeId = entries[i].Key;
                if (recipeId <= 0 || this.netCookCookableCache.ContainsKey(recipeId))
                {
                    continue;
                }

                bool ok = this.TryComputeNetCookMaxQuantity(recipeId, this.netCookMoveIngredients, out int max) && max > 0;
                this.netCookCookableCache[recipeId] = ok;
            }
        }

        // ---------------------------------------------------------------- UI reads

        internal sealed class NetCookSlotInfo
        {
            public int Index;
            public bool IsCategory;      // "any <category>" slot vs a specific item
            public int MaterialId;       // specific slots only
            public int MaterialType;     // category slots only (FoodMaterialType)
            public bool CanChange;
            public int PreferredStaticId;
        }

        internal sealed class NetCookSlotCandidate
        {
            public int StaticId;
            public uint NetId;
            public int Count;
            public int StarRate;
            public string Name;
        }

        // Slots of a recipe, for the picker UI.
        //
        // Uses InitCookingRecipeDetail, not GetRecipeDetail: the detail is a single shared instance
        // on CookingSystem and Init is what points it at THIS recipe and refreshes its slots. Reading
        // the stale one would show the slots of whatever was selected last. It also runs the game's
        // AutoFill, which is what makes the "currently filled with" readout truthful.
        internal unsafe bool TryReadNetCookRecipeSlots(int recipeId, List<NetCookSlotInfo> slots, out string status)
        {
            status = string.Empty;
            slots.Clear();
            if (recipeId <= 0)
            {
                status = "No recipe selected.";
                return false;
            }

            List<uint> slotPins = null;
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "CookingSystem unavailable.";
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
                if (cookingSystemClass == IntPtr.Zero || initDetailMethod == IntPtr.Zero)
                {
                    status = "InitCookingRecipeDetail unavailable.";
                    return false;
                }

                int id = recipeId;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                IntPtr detailObj = auraMonoRuntimeInvoke(initDetailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || detailObj == IntPtr.Zero)
                {
                    status = "Recipe detail unavailable.";
                    return false;
                }

                if (!this.TryGetMonoObjectMember(detailObj, "materialSlots", out IntPtr slotsObj) || slotsObj == IntPtr.Zero)
                {
                    status = "Recipe slots unavailable.";
                    return false;
                }

                List<IntPtr> slotItems = new List<IntPtr>(16);
                slotPins = new List<uint>(16);
                if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, slotItems, slotPins))
                {
                    status = "Recipe slots unreadable.";
                    return false;
                }

                for (int i = 0; i < slotItems.Count; i++)
                {
                    IntPtr slotObj = slotItems[i];
                    if (slotObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    NetCookSlotInfo info = new NetCookSlotInfo { Index = i };
                    this.TryGetMonoInt32Member(slotObj, "materialId", out info.MaterialId);
                    this.TryGetMonoInt32Member(slotObj, "materialType", out info.MaterialType);
                    this.TryGetMonoBoolMember(slotObj, "canChange", out info.CanChange);
                    // The ingredient model: id < 100 means the slot takes a FoodMaterialType rather
                    // than one specific item, and those carry materialId == 0.
                    info.IsCategory = info.MaterialId <= 0;
                    info.PreferredStaticId = this.GetNetCookSlotPreference(recipeId, i);
                    slots.Add(info);
                }

                return slots.Count > 0;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (slotPins != null)
                {
                    FreeAuraMonoPins(slotPins);
                }
            }
        }

        // What the player actually owns that fits this slot. The game does the filtering: category
        // matching, removing stacks already consumed by other slots, and price ordering.
        internal unsafe bool TryListNetCookSlotCandidates(int slotIndex, List<NetCookSlotCandidate> candidates, out string status)
        {
            status = string.Empty;
            candidates.Clear();

            List<uint> itemPins = null;
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "CookingSystem unavailable.";
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                IntPtr getSlotMaterialsMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetSlotMaterials", 1);
                if (getSlotMaterialsMethod == IntPtr.Zero)
                {
                    status = "GetSlotMaterials unavailable.";
                    return false;
                }

                int slot = slotIndex;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&slot);
                IntPtr itemListObj = auraMonoRuntimeInvoke(getSlotMaterialsMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    status = "No candidates for this slot.";
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>(32);
                itemPins = new List<uint>(32);
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins))
                {
                    status = "Candidate list unreadable.";
                    return false;
                }

                // One row per item KIND: the preference is stored by staticId, so listing three
                // stacks of the same fish as three choices would offer the same outcome three times.
                HashSet<int> seen = new HashSet<int>();
                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero
                        || !this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || staticId <= 0)
                    {
                        continue;
                    }

                    this.TryGetDirectBackpackItemCount(itemObj, out int count);
                    if (!seen.Add(staticId))
                    {
                        // Same kind seen again: fold the stack into the row already listed.
                        for (int k = 0; k < candidates.Count; k++)
                        {
                            if (candidates[k].StaticId == staticId)
                            {
                                candidates[k].Count += Math.Max(0, count);
                                break;
                            }
                        }

                        continue;
                    }

                    NetCookSlotCandidate c = new NetCookSlotCandidate
                    {
                        StaticId = staticId,
                        Count = Math.Max(0, count),
                    };
                    this.TryGetDirectBackpackItemNetId(itemObj, out c.NetId);
                    this.TryGetDirectBackpackItemStarRate(itemObj, out c.StarRate);
                    if (!this.TryGetItemName(staticId, out c.Name) || string.IsNullOrWhiteSpace(c.Name))
                    {
                        c.Name = staticId.ToString(CultureInfo.InvariantCulture);
                    }

                    candidates.Add(c);
                }

                return candidates.Count > 0;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (itemPins != null)
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
        }

        // ---------------------------------------------------------------- cook-time application

        // Called per slot from the recipe slot walk, before the Universal top-up.
        //
        // Returns true only when it actually placed the preferred item. Every failure is silent by
        // design: the slot keeps whatever AutoFill put there and cooking proceeds.
        internal unsafe bool TryApplyNetCookSlotPreference(
            IntPtr cookingSystemObj, IntPtr cookingSystemClass, IntPtr slotObj, int slotIndex, int recipeId)
        {
            if (!this.netCookSlotManualMode || cookingSystemObj == IntPtr.Zero || slotObj == IntPtr.Zero)
            {
                return false;
            }

            int wantStaticId = this.GetNetCookSlotPreference(recipeId, slotIndex);
            if (wantStaticId <= 0)
            {
                return false;
            }

            // The game marks slots it will not let the player change (fixed recipe parts). Honour it
            // rather than fighting the server for a slot it would reject.
            if (this.TryGetMonoBoolMember(slotObj, "canChange", out bool canChange) && !canChange)
            {
                return false;
            }

            // Already holding the wanted kind: nothing to do. Re-filling would spend a second stack.
            if (this.TryGetMonoBoolMember(slotObj, "filled", out bool filled) && filled
                && this.TryGetMonoInt32Member(slotObj, "filledMaterialStaticId", out int currentStaticId)
                && currentStaticId == wantStaticId)
            {
                return true;
            }

            if (filled && !this.TryClearNetCookSlot(cookingSystemObj, cookingSystemClass, slotIndex))
            {
                return false;
            }

            return this.TryFillNetCookSlotWithStaticId(
                cookingSystemObj, cookingSystemClass, slotIndex, wantStaticId, out _);
        }

        private unsafe bool TryClearNetCookSlot(IntPtr cookingSystemObj, IntPtr cookingSystemClass, int slotIndex)
        {
            try
            {
                IntPtr clearMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "ClearSlot", 1);
                if (clearMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                int slot = slotIndex;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&slot);
                auraMonoRuntimeInvoke(clearMethod, cookingSystemObj, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        // Generalisation of TryFillNetCookSlotWithUniversalIngredient: same walk, any staticId.
        internal unsafe bool TryFillNetCookSlotWithStaticId(
            IntPtr cookingSystemObj, IntPtr cookingSystemClass, int slotIndex, int wantStaticId, out string status)
        {
            status = string.Empty;
            if (cookingSystemObj == IntPtr.Zero || cookingSystemClass == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || wantStaticId <= 0)
            {
                status = "AuraMono CookingSystem unavailable.";
                return false;
            }

            List<uint> itemPins = null;
            try
            {
                IntPtr getSlotMaterialsMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetSlotMaterials", 1);
                IntPtr fillMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "FillMaterialInSlot", 3);
                if (getSlotMaterialsMethod == IntPtr.Zero || fillMethod == IntPtr.Zero)
                {
                    status = "CookingSystem slot-fill methods unavailable.";
                    return false;
                }

                int slot = slotIndex;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&slot);
                IntPtr itemListObj = auraMonoRuntimeInvoke(getSlotMaterialsMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    status = "GetSlotMaterials returned nothing.";
                    return false;
                }

                // BackpackItem is a struct: every enumerated element is a fresh mono box, and the
                // member reads below can trigger a moving collection. Pin the walk.
                List<IntPtr> items = new List<IntPtr>(16);
                itemPins = new List<uint>(16);
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins))
                {
                    status = "Slot material list unreadable.";
                    return false;
                }

                uint chosenNetId = 0U;
                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero
                        || !this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || staticId != wantStaticId
                        || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                        || netId == 0U)
                    {
                        continue;
                    }

                    // A stack already drained by earlier slots comes back with count 0.
                    if (this.TryGetDirectBackpackItemCount(itemObj, out int count) && count < 1)
                    {
                        continue;
                    }

                    chosenNetId = netId;
                    break;
                }

                if (chosenNetId == 0U)
                {
                    status = "Preferred ingredient not available for this slot.";
                    return false;
                }

                uint materialNetId = chosenNetId;
                int materialStaticId = wantStaticId;
                exc = IntPtr.Zero;
                args[0] = (IntPtr)(&slot);
                args[1] = (IntPtr)(&materialNetId);
                args[2] = (IntPtr)(&materialStaticId);
                auraMonoRuntimeInvoke(fillMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "FillMaterialInSlot raised exception.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (itemPins != null)
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
        }
    }
}
