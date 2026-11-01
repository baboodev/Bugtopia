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
        // Throttled, cached AND budgeted, because it is not free: the first measurement of a recipe
        // resolves its requirement list, and that resolve runs CookingSystem.InitCookingRecipeDetail
        // — which rebuilds the shared recipe detail and re-runs the game's AutoFill over the bag.
        // Measuring forty recipes in one frame is forty AutoFill passes; the panel repaints EVERY
        // frame, so that has to be spread out. The sweep therefore walks the entry list a few
        // recipes per frame and only claims to be ready once it has been all the way round.
        // Afterwards the requirement lists are cached (netCookRecipeRequirementsCache) and a sweep
        // is plain arithmetic, but the interval keeps even that off the per-frame path.
        private readonly Dictionary<int, bool> netCookCookableCache = new Dictionary<int, bool>();
        private float nextNetCookCookableRefreshAt = 0f;
        private bool netCookCookableCacheMoveIngredients = false;
        private int netCookCookableSweepCursor = 0;
        private bool netCookCookableSweepComplete = false;

        private const float NetCookCookableRefreshSeconds = 3f;
        private const int NetCookCookableMeasureBudget = 4;

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

            // Unknown entry: answer optimistically and let the sweep correct it. Hiding a recipe
            // because it has not been measured yet would make the list flicker on open.
            return true;
        }

        // The filter must not hide anything until a full sweep has been round once, or the grid
        // would drop rows one budget at a time while the first sweep is still walking.
        internal bool IsNetCookCookableFilterReady()
        {
            return this.netCookCookableSweepComplete;
        }

        internal void RefreshNetCookCookableCache(List<KeyValuePair<int, string>> entries, bool force = false)
        {
            if (!this.netCookCookableOnly || entries == null || entries.Count == 0)
            {
                return;
            }

            // Move Ingredients changes the answer (warehouse stock counts or it does not), so a
            // toggle flip invalidates rather than waits out the interval.
            if (this.netCookCookableCacheMoveIngredients != this.netCookMoveIngredients)
            {
                this.netCookCookableCacheMoveIngredients = this.netCookMoveIngredients;
                this.netCookCookableCache.Clear();
                this.netCookCookableSweepCursor = 0;
                this.netCookCookableSweepComplete = false;
                this.nextNetCookCookableRefreshAt = 0f;
            }

            float now = Time.unscaledTime;
            bool sweepInFlight = this.netCookCookableSweepCursor > 0;
            if (!force && !sweepInFlight && now < this.nextNetCookCookableRefreshAt)
            {
                return;
            }

            if (this.netCookCookableSweepCursor >= entries.Count)
            {
                this.netCookCookableSweepCursor = 0;
            }

            // Verdicts are overwritten in place rather than cleared up front: a row keeps its last
            // answer until a fresh one replaces it, so a re-sweep never makes the grid flicker.
            int budget = NetCookCookableMeasureBudget;
            while (this.netCookCookableSweepCursor < entries.Count && budget > 0)
            {
                int recipeId = entries[this.netCookCookableSweepCursor++].Key;
                if (recipeId <= 0)
                {
                    continue;
                }

                budget--;
                this.netCookCookableCache[recipeId] =
                    this.TryComputeNetCookMaxQuantity(recipeId, this.netCookMoveIngredients, out int max) && max > 0;
            }

            if (this.netCookCookableSweepCursor >= entries.Count)
            {
                this.netCookCookableSweepCursor = 0;
                this.netCookCookableSweepComplete = true;
                this.nextNetCookCookableRefreshAt = now + NetCookCookableRefreshSeconds;
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
            // What the game's AutoFill has actually put in the slot right now. The read below runs
            // InitCookingRecipeDetail, so this is fresh — it is what the tile shows for a slot the
            // player has not pinned, which beats showing an empty box.
            public int FilledStaticId;
        }

        internal sealed class NetCookSlotCandidate
        {
            public int StaticId;
            public uint NetId;
            public int Count;            // units in the BAG — the only ones fillable right now
            public int WarehouseCount;   // units sitting in the warehouse, 0 unless Move Ingredients
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
                    if (this.TryGetMonoBoolMember(slotObj, "filled", out bool slotFilled) && slotFilled)
                    {
                        this.TryGetMonoInt32Member(slotObj, "filledMaterialStaticId", out info.FilledStaticId);
                    }
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

        // Point the shared recipe detail at this recipe. CookingSystem keeps ONE _recipeDetail, and
        // both GetSlotMaterials and ClearSlot/FillMaterialInSlot index into whatever it currently
        // holds — so anything that reads a slot has to say which recipe it means first. Plenty of
        // other code re-points it (the cookable sweep, the cook loop, the game's own CookPanel),
        // and without this the picker would be reading another dish's slots.
        private unsafe bool TryInitNetCookRecipeDetailForSlots(
            IntPtr cookingSystemObj, IntPtr cookingSystemClass, int recipeId, out IntPtr detail)
        {
            detail = IntPtr.Zero;
            if (cookingSystemObj == IntPtr.Zero || cookingSystemClass == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || recipeId <= 0)
            {
                return false;
            }

            IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
            if (initDetailMethod == IntPtr.Zero)
            {
                return false;
            }

            int id = recipeId;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr detailObj = auraMonoRuntimeInvoke(initDetailMethod, cookingSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || detailObj == IntPtr.Zero)
            {
                return false;
            }

            detail = detailObj;
            return true;
        }

        // What this one slot accepts: a concrete item id, or a FoodMaterialType category (the
        // recipe encodes the category as an ingredient id below 100, which leaves materialId 0).
        // Read off the freshly initialised detail so it costs no second AutoFill pass.
        private unsafe bool TryReadNetCookSlotCriteria(IntPtr detailObj, int slotIndex, out int materialId, out int materialType)
        {
            materialId = 0;
            materialType = 0;
            if (detailObj == IntPtr.Zero || slotIndex < 0)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(detailObj, "materialSlots", out IntPtr slotsObj) || slotsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> slotItems = new List<IntPtr>(16);
            List<uint> slotPins = new List<uint>(16);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, slotItems, slotPins)
                    || slotIndex >= slotItems.Count || slotItems[slotIndex] == IntPtr.Zero)
                {
                    return false;
                }

                this.TryGetMonoInt32Member(slotItems[slotIndex], "materialId", out materialId);
                this.TryGetMonoInt32Member(slotItems[slotIndex], "materialType", out materialType);
                return true;
            }
            finally
            {
                FreeAuraMonoPins(slotPins);
            }
        }

        // Display name for an ingredient id. TryGetItemName (the radar's) goes through
        // mapResGetEntityMethod, which is only resolved once the map/radar subsystem has run — in a
        // session that never opened it every candidate fell back to printing its staticId, which is
        // what the grid was showing instead of names. TryGetResolvedFoodNameFromStaticId is the
        // bag's own resolver (BackpackItem.GetBackPackName, then TableData.GetEntity) and needs no
        // such priming. Cached because the grid asks per row per repaint.
        private readonly Dictionary<int, string> netCookItemNameCache = new Dictionary<int, string>();

        internal bool TryResolveNetCookItemName(int staticId, out string name)
        {
            name = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.netCookItemNameCache.TryGetValue(staticId, out string cached))
            {
                name = cached ?? string.Empty;
                return name.Length > 0;
            }

            string resolved = string.Empty;
            if (this.TryGetResolvedFoodNameFromStaticId(staticId, out string bagName)
                && !this.IsPoorBagItemDisplayName(bagName, staticId))
            {
                resolved = bagName.Trim();
            }
            else if (this.TryGetItemName(staticId, out string mapName)
                && !this.IsPoorBagItemDisplayName(mapName, staticId))
            {
                resolved = mapName.Trim();
            }

            // A negative answer is cached too — otherwise an id the tables cannot name would re-run
            // both resolvers for every row of every repaint.
            this.netCookItemNameCache[staticId] = resolved;
            name = resolved;
            return resolved.Length > 0;
        }

        // What the player actually owns that fits this slot. The game does the filtering: category
        // matching, removing stacks already consumed by other slots, and price ordering.
        internal unsafe bool TryListNetCookSlotCandidates(int recipeId, int slotIndex, List<NetCookSlotCandidate> candidates, out string status)
        {
            status = string.Empty;
            candidates.Clear();

            int slotMaterialId = 0;
            int slotMaterialType = 0;
            bool criteriaKnown = false;

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "CookingSystem unavailable.";
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (!this.TryInitNetCookRecipeDetailForSlots(cookingSystemObj, cookingSystemClass, recipeId, out IntPtr detailObj))
                {
                    status = "Recipe detail unavailable.";
                    return false;
                }

                criteriaKnown = this.TryReadNetCookSlotCriteria(detailObj, slotIndex, out slotMaterialId, out slotMaterialType);

                // Best effort, and deliberately not fatal. An empty bag list is indistinguishable
                // from a failed read here (the collection walk answers false for both), and "the
                // bag has nothing that fits" is precisely the case where the warehouse is the only
                // place the ingredient can come from — returning early on it was what made the
                // picker come up blank with Move Ingredients on.
                this.AppendNetCookBagSlotCandidates(cookingSystemObj, cookingSystemClass, slotIndex, candidates);
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            // GetSlotMaterials only ever sees the BAG (BackPackSystem.GetItems). With Move
            // Ingredients on, the warehouse is stock the cook will actually draw from, so the
            // picker has to offer it too.
            if (this.netCookMoveIngredients && criteriaKnown)
            {
                this.AppendNetCookWarehouseSlotCandidates(candidates, slotMaterialId, slotMaterialType);
            }

            // The Universal Ingredient substitutes ANY slot, so it belongs in every slot's list
            // whether or not the stores happen to hold one right now — a row with no count is how
            // the picker says "this is an option, you have none".
            this.EnsureNetCookUniversalSlotCandidate(candidates);

            if (candidates.Count <= 0)
            {
                status = criteriaKnown ? "No candidates for this slot." : "Recipe slots unreadable.";
                return false;
            }

            return true;
        }

        // 46999 is listed for every slot by the game itself (GetSlotMaterials, gated on
        // CheckMagicIngredientUnlocked), but only when a stack is in the BAG. This adds the row
        // unconditionally so it can be pinned ahead of owning one — the counts on it stay honest,
        // and a pin that cannot be met at cook time falls back like any other.
        private void EnsureNetCookUniversalSlotCandidate(List<NetCookSlotCandidate> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].StaticId == NetCookUniversalIngredientStaticId)
                {
                    return;
                }
            }

            NetCookSlotCandidate c = new NetCookSlotCandidate
            {
                StaticId = NetCookUniversalIngredientStaticId,
            };
            if (!this.TryResolveNetCookItemName(NetCookUniversalIngredientStaticId, out c.Name))
            {
                c.Name = "#" + NetCookUniversalIngredientStaticId.ToString(CultureInfo.InvariantCulture);
            }

            candidates.Add(c);
        }

        // The bag half: CookingSystem.GetSlotMaterials already does the category matching, drops
        // stacks other slots have consumed, and orders by price. Silent on failure — the caller
        // treats an empty bag as a fact, not an error.
        private unsafe void AppendNetCookBagSlotCandidates(
            IntPtr cookingSystemObj, IntPtr cookingSystemClass, int slotIndex, List<NetCookSlotCandidate> candidates)
        {
            List<uint> itemPins = null;
            try
            {
                IntPtr getSlotMaterialsMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetSlotMaterials", 1);
                if (getSlotMaterialsMethod == IntPtr.Zero)
                {
                    return;
                }

                int slot = slotIndex;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&slot);
                IntPtr itemListObj = auraMonoRuntimeInvoke(getSlotMaterialsMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return;
                }

                List<IntPtr> items = new List<IntPtr>(32);
                itemPins = new List<uint>(32);
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins))
                {
                    return;
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
                    // QC-first like every other star read since 2026-11-01 — the picker row's N★ label
                    // must not show 0★ for a stack the game draws as 5★.
                    if (!this.TryResolveAutoSellMonoItemStar(c.NetId, itemObj, string.Empty, Math.Max(0, c.Count), out c.StarRate))
                    {
                        c.StarRate = 0;
                    }
                    if (!this.TryResolveNetCookItemName(staticId, out c.Name))
                    {
                        c.Name = "#" + staticId.ToString(CultureInfo.InvariantCulture);
                    }

                    candidates.Add(c);
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("bag slot candidates failed: " + ex.Message);
            }
            finally
            {
                if (itemPins != null)
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
        }

        // Warehouse stock that fits this slot, merged into the bag candidates. Uses the same scan
        // the ingredient move uses, with the same category predicate, so what the picker offers and
        // what the move can actually deliver cannot drift apart.
        //
        // The Universal Ingredient rides along in the same scan. It matches no category
        // (foodMaterial [99] is outside FoodMaterialType) and is nobody's specific requirement, so
        // it has to be asked for by id or the scan would never return it — and its warehouse stock
        // is as pinnable as anything else now that it is always offered.
        private void AppendNetCookWarehouseSlotCandidates(List<NetCookSlotCandidate> candidates, int slotMaterialId, int slotMaterialType)
        {
            try
            {
                HashSet<int> wantIds = new HashSet<int> { NetCookUniversalIngredientStaticId };
                List<int> wantCategories = null;
                if (slotMaterialId > 0)
                {
                    wantIds.Add(slotMaterialId);
                }
                else
                {
                    wantCategories = new List<int> { slotMaterialType };
                }

                Dictionary<int, List<KeyValuePair<uint, int>>> stacksByStaticId = new Dictionary<int, List<KeyValuePair<uint, int>>>();
                Dictionary<uint, int> starByNetId = new Dictionary<uint, int>();
                if (!this.TryCollectNetCookWarehouseStacks(stacksByStaticId, starByNetId, wantIds, wantCategories, out _))
                {
                    return;
                }

                foreach (KeyValuePair<int, List<KeyValuePair<uint, int>>> kvp in stacksByStaticId)
                {
                    int staticId = kvp.Key;
                    if (staticId <= 0 || kvp.Value == null)
                    {
                        continue;
                    }

                    int total = 0;
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        total += Math.Max(0, kvp.Value[i].Value);
                    }

                    if (total <= 0)
                    {
                        continue;
                    }

                    NetCookSlotCandidate existing = null;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i].StaticId == staticId)
                        {
                            existing = candidates[i];
                            break;
                        }
                    }

                    if (existing != null)
                    {
                        existing.WarehouseCount += total;
                        continue;
                    }

                    NetCookSlotCandidate c = new NetCookSlotCandidate
                    {
                        StaticId = staticId,
                        WarehouseCount = total,
                    };
                    if (!this.TryResolveNetCookItemName(staticId, out c.Name))
                    {
                        c.Name = "#" + staticId.ToString(CultureInfo.InvariantCulture);
                    }

                    candidates.Add(c);
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("warehouse slot candidates failed: " + ex.Message);
            }
        }

        // Slots pinned to each item for this recipe. The warehouse move consults it so a pinned
        // ingredient is pulled ahead of the cheap-first default — without it a pin on a warehouse
        // item is a pin on something the move may never bring, and the preference silently does
        // nothing at cook time.
        internal void CollectNetCookPinnedStaticIdCounts(int recipeId, Dictionary<int, int> counts)
        {
            if (counts == null)
            {
                return;
            }

            counts.Clear();
            if (!this.netCookSlotManualMode
                || !this.netCookSlotPrefs.TryGetValue(recipeId, out Dictionary<int, int> slots))
            {
                return;
            }

            foreach (KeyValuePair<int, int> slot in slots)
            {
                if (slot.Value <= 0)
                {
                    continue;
                }

                counts.TryGetValue(slot.Value, out int seen);
                counts[slot.Value] = seen + 1;
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

            // NO ClearSlot first. FillMaterialInSlot overwrites the slot outright, and the fill
            // below only touches it once it has actually found a stack of the wanted kind — so a
            // preference that cannot be met leaves the slot exactly as AutoFill left it, which is
            // the whole fallback contract.
            //
            // Clearing first would break that contract the moment the game stops being buggy:
            // CookingSystem.ClearSlot guards with `materialSlots.Length >= slotIndex`, which is
            // true for every VALID index, so today it logs an error and returns without clearing
            // anything. Rely on that and the day XD fixes the comparison, a preferred item that
            // ran out would leave an emptied slot behind — burning a Universal Ingredient in place
            // of the real one AutoFill had already put there, or failing the whole prepare with
            // "Missing ingredients".
            return this.TryFillNetCookSlotWithStaticId(
                cookingSystemObj, cookingSystemClass, slotIndex, wantStaticId, out _);
        }

        // R3 (§5.1-5.2, plan 2026-10-31; semantics STRICTED by the requester's live test 2026-11-01):
        // demote a slot the game's own AutoFill filled with a 5★ stack — and when nothing else fits,
        // BLOCK the cook instead of quietly spending the 5★.
        //
        // The requester's own words: bag holds only 5★ Glomwood Meal parts, toggle ON, and "Ingredients
        // max: 1" was read as the mod about to spend them. The plan's permissive v1 ("keep AutoFill's
        // pick") is replaced: toggle ON = 5★ ingredients are NEVER spent automatically. Allowed fills
        // are, in order: a <5★ stack → the Universal Ingredient (only while its own toggle is on) →
        // nothing. A manual pin outranks the policy — that is the "unless manually selected" half.
        // The "Ingredients max" math mirrors this (TryComputeNetCookMaxQuantity's strict branch), so
        // the UI already shows the recipe as not cookable before any prepare is attempted.
        //
        // The gap this closes on the fill side is the bag's AutoFill pricing: price follows each item's
        // table row, not its stars — a 5★ vegetable claims an "any ingredient" category slot while a
        // 1★ fish would fit it just as well. The warehouse side needed no new filter:
        // AllocateNetCookMoveFromStacks sorts LOWEST star first, so 5★ stacks are only allocated when
        // the <5★ pool cannot cover the deficit — the strict block lives in the demand math instead
        // (the move map derives from <5★-counted demands, so 5★ stacks never enter it).
        //
        // FillMaterialInSlot overwrites outright; NO ClearSlot, same contract and same reasoning as
        // TryApplyNetCookSlotPreference above.
        internal enum NetCookFiveStarSlotOutcome
        {
            NoOp,     // nothing to demote (slot not 5★, star unknown, Universal stand-in, manual pin,
                      // or a game-fixed / walk-failed slot whose fill could not be PROVEN 5★)
            Demoted,  // slot rewritten with a <5★ stack or the Universal Ingredient
            Blocked,  // 5★ filled (or proven 5★ with no working walk), no <5★ candidate, Universal
                      // not allowed/available — refuse the cook (mid-run this drains, no livelock)
        }

        private readonly HashSet<int> netCookPreferUnderFiveLoggedRecipes = new HashSet<int>();

        // Fail-closed partner for the star of a slot's CURRENT fill when the candidate walk could not
        // run at all. QualityComponent-by-netId needs no GetSlotMaterials, so a failure of the walk
        // cannot hide a 5★ fill: PROVEN 5★ → Blocked (silence would mean spending it, against the
        // strict ask); star unknown or <5★ → NoOp (0 = "no quality" is spendable per plan §5.1 — the
        // policy is about 5★, not about API errors).
        private NetCookFiveStarSlotOutcome FailClosedNetCookSlotFromFiveStar(uint filledNetId, int slotIndex, int recipeId)
        {
            if (filledNetId == 0U || !this.TryGetAutoSellQualityComponentStar(filledNetId, out int star) || star < NetCookStarMax)
            {
                return NetCookFiveStarSlotOutcome.NoOp;
            }

            FeatureLog.Fail("NetCookQuality", "R3: slot " + slotIndex + " of recipe " + recipeId
                + " holds a 5-star fill and the demotion walk could not run; the cook is blocked"
                + " (switch the preference off, or pin the kind by hand to spend 5-star).");
            return NetCookFiveStarSlotOutcome.Blocked;
        }

        internal unsafe NetCookFiveStarSlotOutcome TryDemoteNetCookSlotFromFiveStar(
            IntPtr cookingSystemObj, IntPtr cookingSystemClass, IntPtr slotObj, int slotIndex, int recipeId)
        {
            if (cookingSystemObj == IntPtr.Zero || cookingSystemClass == IntPtr.Zero || slotObj == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return NetCookFiveStarSlotOutcome.NoOp;
            }

            // One strict rule (review 2026-11-01): "Use All Ingredients" is an explicit whole-bag
            // instruction that sanctions spending 5-star stock, and the move map already ships those
            // stacks to the bag under the same exemption — the walk must not then Block the fills it
            // just authorized. Same predicate as the headroom, the move-map deficit and the start
            // gate. Short-circuits BEFORE the canChange fail-close: a fixed-slot 5-star fill is
            // spendable under useAll as well.
            if (this.netCookUseAllIngredients)
            {
                return NetCookFiveStarSlotOutcome.NoOp;
            }

            if (!this.TryGetMonoBoolMember(slotObj, "filled", out bool filled) || !filled)
            {
                return NetCookFiveStarSlotOutcome.NoOp; // an empty slot belongs to the Universal top-up, not to this pass
            }

            if (!this.TryGetMonoInt32Member(slotObj, "filledMaterialStaticId", out int currentStaticId)
                || currentStaticId <= 0)
            {
                return NetCookFiveStarSlotOutcome.NoOp;
            }

            this.TryGetMonoUInt32Member(slotObj, "filledMaterialNetId", out uint currentNetId);

            if (currentStaticId == NetCookUniversalIngredientStaticId)
            {
                return NetCookFiveStarSlotOutcome.NoOp; // a Universal stand-in is not 5★ spending in the first place
            }

            // The game marks slots it will not let the player change (fixed recipe parts) — even the
            // player's own panel cannot swap those, so no walk (the mod's or AutoFill's alternative)
            // can demote one. The strict DEMAND math (max + move map) keeps fixed-5★-only recipes out
            // of reach; with MIXED stock the demand math passes but price-based AutoFill can still
            // pick a 5★ stack for the fixed part — so a proven-5★ fill here blocks the cook rather
            // than letting the net channel consume it (2026-11-01 review, was a silent NoOp leak).
            if (this.TryGetMonoBoolMember(slotObj, "canChange", out bool canChange) && !canChange)
            {
                return this.FailClosedNetCookSlotFromFiveStar(currentNetId, slotIndex, recipeId);
            }

            if (this.netCookSlotManualMode)
            {
                int wantStaticId = this.GetNetCookSlotPreference(recipeId, slotIndex);
                if (wantStaticId > 0 && wantStaticId == currentStaticId)
                {
                    return NetCookFiveStarSlotOutcome.NoOp; // the user picked this kind — the pin wins even when it is 5★
                }
            }

            List<uint> itemPins = null;
            try
            {
                IntPtr getSlotMaterialsMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetSlotMaterials", 1);
                IntPtr fillMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "FillMaterialInSlot", 3);
                if (getSlotMaterialsMethod == IntPtr.Zero || fillMethod == IntPtr.Zero)
                {
                    return this.FailClosedNetCookSlotFromFiveStar(currentNetId, slotIndex, recipeId);
                }

                int slot = slotIndex;
                IntPtr exc = IntPtr.Zero;
                // Sized for the widest invoke below: GetSlotMaterials uses slot 0 only, but
                // FillMaterialInSlot (3 args) writes args[0..2] — [1] here would write 16 bytes past
                // the allocation and mono would read them (AGENTS §12 out-param slot rule; the
                // TryFillNetCookSlotWithStaticId sibling allocates [3] for the same pair).
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&slot);
                IntPtr itemListObj = auraMonoRuntimeInvoke(getSlotMaterialsMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return this.FailClosedNetCookSlotFromFiveStar(currentNetId, slotIndex, recipeId);
                }

                // Pinned walk: BackpackItem is a struct — every enumerated element is a fresh mono box
                // and the member reads below can trigger a moving sgen collection (TryFillNetCookSlotWithStaticId).
                List<IntPtr> items = new List<IntPtr>(32);
                itemPins = new List<uint>(32);
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins))
                {
                    return this.FailClosedNetCookSlotFromFiveStar(currentNetId, slotIndex, recipeId);
                }

                // Slot fit is intrinsic to this candidate list: GetSlotMaterials is exactly what the
                // game's own panel lists for THIS slot (specific id or category match, units already
                // consumed by earlier filled slots subtracted) — so anything picked from it fills the
                // slot by construction, and no separate fit check is needed.
                int currentStar = -1;
                uint bestNetId = 0U;
                int bestStaticId = 0;
                int bestStar = int.MaxValue;
                uint universalNetId = 0U;

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero
                        || !this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || staticId <= 0
                        || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                        || netId == 0U)
                    {
                        continue;
                    }

                    // A stack already drained by an earlier slot comes back with count 0 — only the
                    // CURRENT fill may be seen at count 0 (its own unit is spent); anything else needs
                    // a unit left to spend.
                    this.TryGetDirectBackpackItemCount(itemObj, out int count);
                    bool isCurrentFill = netId == currentNetId;
                    if (!isCurrentFill && count < 1)
                    {
                        continue;
                    }

                    if (staticId == NetCookUniversalIngredientStaticId)
                    {
                        if (!isCurrentFill)
                        {
                            universalNetId = netId; // second-chance substitute, only if its toggle is on
                        }

                        continue;
                    }

                    // Canonical QC-first star (QualityComponent by netId, item fields only as the
                    // fallback — the live dish proof: item-level starRate is 0 while QC carries the
                    // real star). No star data at all = star 0 ("no quality"): spendable, and never
                    // mistaken for 5★.
                    if (!this.TryResolveAutoSellMonoItemStar(netId, itemObj, string.Empty, Math.Max(1, count), out int starRate))
                    {
                        starRate = 0;
                    }

                    if (isCurrentFill)
                    {
                        currentStar = starRate; // the stack AutoFill put into this slot (a fully drained
                        // one is still listed with count 0 — same behaviour the fill walk relies on)
                        continue;
                    }

                    if (starRate >= 5)
                    {
                        continue; // not a demotion target: 5★ for 5★ is no saving at all
                    }

                    if (starRate < bestStar)
                    {
                        bestStar = starRate;
                        bestNetId = netId;
                        bestStaticId = staticId;
                    }
                }

                if (currentStar != 5)
                {
                    return NetCookFiveStarSlotOutcome.NoOp; // AutoFill already honored the policy (or the fill has no quality)
                }

                uint materialNetId = bestNetId;
                int materialStaticId = bestStaticId;
                bool viaUniversal = false;
                if (materialNetId == 0U)
                {
                    // No <5★ stack fits. The strict hierarchy's second rung: the Universal Ingredient
                    // — but only while its own toggle is on (the toggle governs automatic substitution;
                    // stealing a Universal unit behind an OFF toggle would be its own kind of surprise).
                    if (!this.netCookUseUniversalIngredient || universalNetId == 0U)
                    {
                        if (this.netCookPreferUnderFiveLoggedRecipes.Add(recipeId))
                        {
                            // Tier 1 (errors-to-log): a strict block must reach the file log without
                            // MasterLogNetCook; the once-per-recipe set keeps it bounded.
                            FeatureLog.Fail("NetCookQuality", "R3: only 5-star stock fits slot " + slotIndex + " of recipe " + recipeId
                                + "; the cook is blocked to keep the 5-star stack (pin the kind by hand, or let"
                                + " Universal Ingredient cover it, or switch the preference off to spend 5-star).");
                        }

                        return NetCookFiveStarSlotOutcome.Blocked;
                    }

                    materialNetId = universalNetId;
                    materialStaticId = NetCookUniversalIngredientStaticId;
                    viaUniversal = true;
                    this.netCookUniversalDemotedSlots++;
                }

                exc = IntPtr.Zero;
                args[0] = (IntPtr)(&slot);
                args[1] = (IntPtr)(&materialNetId);
                args[2] = (IntPtr)(&materialStaticId);
                auraMonoRuntimeInvoke(fillMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    // The rewrite failed on the mono side — do NOT leave the 5★ fill as the fallback
                    // under a strict toggle; the caller fails this prepare and the stove retries.
                    return NetCookFiveStarSlotOutcome.Blocked;
                }

                if (viaUniversal)
                {
                    this.NetCookDiagLog("R3 substituted slot=" + slotIndex + " 5*static=" + currentStaticId
                        + " -> Universal Ingredient net=" + materialNetId, true);
                }
                else
                {
                    this.NetCookDiagLog("R3 demoted slot=" + slotIndex + " 5*static=" + currentStaticId
                        + " -> " + bestStar + "*static=" + bestStaticId + " net=" + bestNetId, true);
                }

                return NetCookFiveStarSlotOutcome.Demoted;
            }
            catch (Exception ex)
            {
                this.NetCookLog("R3 slot demotion failed: " + ex.GetType().Name + ": " + ex.Message);
                return this.FailClosedNetCookSlotFromFiveStar(currentNetId, slotIndex, recipeId);
            }
            finally
            {
                if (itemPins != null)
                {
                    FreeAuraMonoPins(itemPins);
                }
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
