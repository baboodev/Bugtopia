using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Furniture Dye, PANEL mode — drive the game's own DyeColorPanel instead of a build ghost.
    //
    // ── WHY THIS IS THE BETTER HALF OF THE FEATURE ──────────────────────────────────────────────
    // A click on a swatch in that panel sends NOTHING. ColorItemRender's toggle handler only:
    //   1. moves the highlight,
    //   2. writes `_partDyeColorData[partIndex] = (colour, Coin, coinCost, dyeItemId, dyeCost)`,
    //   3. calls SetDyeColorCost() to redraw the price,
    //   4. calls _guiModel.SetDyeColor(partIndex, colour) for the 3D preview.
    //
    // The send happens later, in Save() -> _ConfirmSave(), and Save reads exactly that dictionary:
    // with `_partDyeColorData.Count == 0` it just Close()s. So the whole pending edit lives in one
    // field, and filling it in IS the feature — the player then presses the game's own Confirm and
    // the game sends it through its own path (MakeItemEvent -> MakeItemCommand ->
    // CraftProtocolManager.DyeColor -> DyeFurnitureNewNetworkCommand).
    //
    // That is strictly better than detouring the send: the player stays in control, Cancel still
    // cancels, and the price line stays honest because SetDyeColorCost reads the same tuple we
    // wrote. We never touch the network.
    //
    // ── WHY NOT CALL UpdatePartDyeColor ─────────────────────────────────────────────────────────
    // DyeColorPanel has TWO methods of that name and BOTH take three parameters —
    // `(bool, int, bool) -> bool` and `(int, byte, Color32) -> void` (verified on the running
    // build). mono_class_get_method_from_name resolves by name+arity only, so it cannot tell them
    // apart, and picking wrong means handing a Color32 pointer to something expecting a bool. The
    // dictionary is written directly instead: `set_Item` takes two parameters, and it resolves on
    // the dictionary's OWN class, which is already the concrete instantiation — the same shape that
    // works for List<DyeColorData>.Add in the build path.
    //
    // ── THE TUPLE ───────────────────────────────────────────────────────────────────────────────
    // `_partDyeColorData` is Dictionary<byte, ValueTuple<Color32, CurrencyType, int, int, int>>
    // (field verified at offset 160). The value is written as a 20-byte blob:
    //   0  Color32 r,g,b,a      — the only part _ConfirmSave reads
    //   4  CurrencyType (Coin=1) — written by the game, read by nobody in this panel
    //   8  currencyCost          — SetDyeColorCost sums these into _currencyCost
    //   12 dye item id           — SetDyeColorCost shows have/need for it
    //   16 dye item count
    // The staging buffer is deliberately oversized and zeroed, so if a future build widens the
    // tuple mono copies our zeros rather than reading past the buffer.
    //
    // Costs come from TableData.TableDyeColors — a plain Dictionary<int, TableDyeColor> whose rows
    // expose itemId / colorIndex / currencyCost / itemType / itemCost as ints. NOT from
    // DyeColorUtil.GetDyeColorCost: that one returns its item cost through an `out (int, int)`, and
    // a value-type out wider than a pointer is the stack-corruption trap this project has already
    // paid for once.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // A PLAIN full name, never assembly-qualified. TryCreateAuraMonoSystemTypeObject resolves game
        // types by splitting namespace and class and sweeping every image; a ", XDTGameUI" suffix
        // breaks that split, and the only fallback left is Type.GetType, which it refuses for game
        // types because that icall kills the process. Net effect of the suffix: the panel reads as
        // closed forever, silently. (Measured: suffixed -> false, plain -> resolved.)
        private const string FurnitureDyePanelTypeName = "XDTGame.UI.Panel.DyeColorPanel";
        private const int FurnitureDyeCurrencyCoin = 1;   // CurrencyType.Coin

        // ValueTuple<Color32, CurrencyType, int, int, int>: 4 + 4 + 4 + 4 + 4, every field 4-aligned.
        private const int FurnitureDyeTupleSize = 20;
        private const int FurnitureDyeTupleStageSize = 64;   // zeroed headroom, see the header

        private sealed class FurnitureDyeCost
        {
            public int CurrencyCost;
            public int ItemId;
            public int ItemCost;
        }

        // (staticId << 8) | body -> cost. The table never changes at runtime.
        private readonly Dictionary<int, FurnitureDyeCost> furnitureDyeCostCache =
            new Dictionary<int, FurnitureDyeCost>();
        private bool furnitureDyeCostTableMissing;

        private IntPtr furnitureDyePanelGetViewMethod;
        private IntPtr furnitureDyePanelTypeObj;       // valid only while furnitureDyePanelTypePin holds it
        private uint furnitureDyePanelTypePin;
        private int furnitureDyePanelTypeEpoch = -1;

        // ----------------------------------------------------------------------------------------
        // Detection
        // ----------------------------------------------------------------------------------------

        /// True when the game's dye panel is open AND the item it is editing is one we understand.
        /// Fills in furnitureDyeTarget; the caller treats a false as "not this mode".
        private bool TryRefreshFurnitureDyePanelTarget()
        {
            if (!this.TryGetOpenFurnitureDyePanel(out IntPtr panel) || panel == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(panel);
            try
            {
                if (!this.TryGetMonoInt32Member(panel, "_currStaticId", out int staticId) || staticId <= 0)
                {
                    return false;
                }

                List<FurnitureDyePart> parts = this.FurnitureDyePartsFor(staticId);
                if (parts == null || parts.Count == 0)
                {
                    return false;
                }

                // The panel owns part selection — it has its own tab strip, and our grouping is
                // built by the same rule (partNameTextId), so the indices line up.
                int tab = this.TryGetMonoInt32Member(panel, "_currSelectedPartIndex", out int t) ? t : 0;
                tab = Mathf.Clamp(tab, 0, parts.Count - 1);

                if (this.furnitureDyeTarget == null
                    || this.furnitureDyeTarget.Source != FurnitureDyeSource.Panel
                    || this.furnitureDyeTargetStaticId != staticId)
                {
                    this.furnitureDyeTarget = new FurnitureDyeTarget
                    {
                        Source = FurnitureDyeSource.Panel,
                        StaticId = staticId,
                        Parts = parts,
                    };
                    this.furnitureDyeTargetStaticId = staticId;
                    FeatureLog.Once(FurnitureDyeTag, "panel:" + staticId,
                        "dye panel open for staticId " + staticId + ", " + parts.Count + " part(s)");
                }

                this.furnitureDyeTarget.PanelSelectedPart = tab;
                this.ReadFurnitureDyePanelColors(panel, this.furnitureDyeTarget);
                return true;
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(FurnitureDyeTag, "panel read threw: " + ex.Message);
                return false;
            }
            finally
            {
                if (pin != 0U) { AuraMonoPinFree(pin); }
            }
        }

        // UIManager.GetView(Type) returns null for a panel that is closed or closing, so a non-null
        // result IS "the panel is up". Reuses the resolver the persistent HUD already owns.
        private unsafe bool TryGetOpenFurnitureDyePanel(out IntPtr panel)
        {
            panel = IntPtr.Zero;

            if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj)
                || uiManagerObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.furnitureDyePanelGetViewMethod == IntPtr.Zero)
            {
                this.furnitureDyePanelGetViewMethod = this.persistentHudGetViewMethod;
            }
            if (this.furnitureDyePanelGetViewMethod == IntPtr.Zero)
            {
                return false;
            }

            // The System.Type object holds a reference into the world's assemblies; re-make it when
            // the world changes rather than carrying a stale one across a level load.
            if (this.furnitureDyePanelTypeObj == IntPtr.Zero
                || this.furnitureDyePanelTypeEpoch != this.WorldReadyEpoch)
            {
                if (!this.TryCreateAuraMonoSystemTypeObject(FurnitureDyePanelTypeName, out IntPtr typeObj)
                    || typeObj == IntPtr.Zero)
                {
                    // Not transient: an unresolvable type means the panel can never be seen, and
                    // that must not look like "the panel just is not open".
                    FeatureLog.Fail(FurnitureDyeTag, "System.Type for " + FurnitureDyePanelTypeName
                        + " unresolved - panel mode cannot detect the dye panel (game update?)");
                    return false;
                }
                // A MonoObject* kept across frames must be pinned: the GC moves nursery objects,
                // and a freshly made type object starts there. The pin also keeps it alive.
                if (this.furnitureDyePanelTypePin != 0U)
                {
                    AuraMonoPinFree(this.furnitureDyePanelTypePin);
                }
                this.furnitureDyePanelTypePin = AuraMonoPinNew(typeObj);
                if (this.furnitureDyePanelTypePin == 0U)
                {
                    this.furnitureDyePanelTypeObj = IntPtr.Zero;
                    return false;
                }
                this.furnitureDyePanelTypeObj = typeObj;
                this.furnitureDyePanelTypeEpoch = this.WorldReadyEpoch;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = this.furnitureDyePanelTypeObj;
            IntPtr view = auraMonoRuntimeInvoke(this.furnitureDyePanelGetViewMethod, uiManagerObj,
                                                (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || view == IntPtr.Zero)
            {
                return false;
            }

            panel = view;
            return true;
        }

        // The item's colours as the panel knows them: _dyeColorItems is what it was opened with.
        // A pending edit in _partDyeColorData is deliberately NOT read back — that dictionary is a
        // generic with a ValueTuple value, and the picker already knows what it last wrote.
        private void ReadFurnitureDyePanelColors(IntPtr panel, FurnitureDyeTarget target)
        {
            target.Current.Clear();

            if (!this.TryGetMonoObjectMember(panel, "_dyeColorItems", out IntPtr list)
                || list == IntPtr.Zero)
            {
                return;
            }

            uint pin = AuraMonoPinNew(list);
            try
            {
                List<IntPtr> rows = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(list, rows))
                {
                    return; // an undyed item legitimately enumerates empty
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == IntPtr.Zero
                        || !this.TryGetMonoInt32Member(rows[i], "body", out int body)
                        || !this.TryGetMonoInt32Member(rows[i], "color", out int color))
                    {
                        continue;
                    }
                    target.Current[(byte)(body & 0xFF)] = color;
                }
            }
            finally
            {
                if (pin != 0U) { AuraMonoPinFree(pin); }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Applying
        // ----------------------------------------------------------------------------------------

        /// Stage `packed` on every body of the panel's currently selected part. Nothing is sent —
        /// the player's own Confirm does that.
        internal unsafe bool TryApplyFurnitureDyePanel(FurnitureDyeTarget target, int packed,
                                                       out string status)
        {
            status = "not attempted";

            if (target == null || target.Parts == null || target.Parts.Count == 0)
            {
                status = "no target";
                return false;
            }
            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable — refusing to touch the panel";
                return false;
            }
            if (!this.TryGetOpenFurnitureDyePanel(out IntPtr panel) || panel == IntPtr.Zero)
            {
                status = "dye panel is not open";
                return false;
            }

            uint panelPin = AuraMonoPinNew(panel);
            uint dictPin = 0U;
            try
            {
                if (!this.TryGetMonoObjectMember(panel, "_partDyeColorData", out IntPtr dict)
                    || dict == IntPtr.Zero)
                {
                    status = "_partDyeColorData unavailable (game update?)";
                    return false;
                }

                dictPin = AuraMonoPinNew(dict);
                IntPtr dictClass = auraMonoObjectGetClass(dict);
                // Concrete instantiation — nothing to inflate, and set_Item's two parameters make it
                // unambiguous where UpdatePartDyeColor's two 3-arg overloads are not.
                IntPtr setItem = dictClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(dictClass, "set_Item", 2);
                IntPtr getCount = dictClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Count", 0);
                if (setItem == IntPtr.Zero)
                {
                    status = "Dictionary.set_Item unresolved";
                    return false;
                }

                int before = this.ReadFurnitureDyeDictCount(dict, getCount);

                int idx = Mathf.Clamp(target.PanelSelectedPart, 0, target.Parts.Count - 1);
                List<FurnitureDyeSubPart> sub = target.Parts[idx].Sub;
                byte cr = (byte)((packed >> 24) & 0xFF);
                byte cg = (byte)((packed >> 16) & 0xFF);
                byte cb = (byte)((packed >> 8) & 0xFF);

                for (int i = 0; i < sub.Count; i++)
                {
                    byte body = sub[i].Body;
                    FurnitureDyeCost cost = this.FurnitureDyeCostFor(target.StaticId, body);

                    byte* blob = stackalloc byte[FurnitureDyeTupleStageSize];
                    for (int b = 0; b < FurnitureDyeTupleStageSize; b++)
                    {
                        blob[b] = 0;
                    }
                    blob[0] = cr;
                    blob[1] = cg;
                    blob[2] = cb;
                    blob[3] = 255;                                   // a dye colour is always opaque
                    *(int*)(blob + 4) = FurnitureDyeCurrencyCoin;
                    *(int*)(blob + 8) = cost != null ? cost.CurrencyCost : 0;
                    *(int*)(blob + 12) = cost != null ? cost.ItemId : 0;
                    *(int*)(blob + 16) = cost != null ? cost.ItemCost : 0;

                    byte key = body;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&key);      // byte key, by value
                    args[1] = (IntPtr)blob;        // the tuple, by value
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(setItem, dict, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "set_Item threw for body " + body;
                        return false;
                    }

                    target.Current[body] = packed;
                }

                // Proof the write landed where we think: a fresh panel starts empty, so the count
                // must have moved. Cheap, and it fails loudly instead of leaving the player with a
                // Confirm that silently does nothing.
                int after = this.ReadFurnitureDyeDictCount(dict, getCount);
                if (before >= 0 && after >= 0 && after < sub.Count && after <= before)
                {
                    status = "set_Item did not take (count " + before + " -> " + after + ")";
                    FeatureLog.Fail(FurnitureDyeTag, status);
                    return false;
                }

                this.RefreshFurnitureDyePanelVisuals(panel, sub, cr, cg, cb);

                status = FurnitureDyeHex(packed) + " staged — press Confirm in the game panel";
                FeatureLog.Once(FurnitureDyeTag, "first-panel-apply",
                    "first colour staged into the game's dye panel (" + sub.Count + " body/bodies)");
                FeatureLog.Detail(FurnitureDyeTag, MasterLogFurnitureDye, status);
                return true;
            }
            catch (Exception ex)
            {
                status = "panel apply threw: " + ex.Message;
                FeatureLog.Fail(FurnitureDyeTag, status);
                return false;
            }
            finally
            {
                if (dictPin != 0U) { AuraMonoPinFree(dictPin); }
                if (panelPin != 0U) { AuraMonoPinFree(panelPin); }
            }
        }

        private unsafe int ReadFurnitureDyeDictCount(IntPtr dict, IntPtr getCount)
        {
            if (getCount == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return -1;
            }
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getCount, dict, IntPtr.Zero, ref exc);
            return (exc != IntPtr.Zero || boxed == IntPtr.Zero) ? -1 : this.ReadAuraMonoBoxedInt32(boxed);
        }

        // The two redraws the game's own swatch click does after writing the dictionary. Neither is
        // required for correctness — skipping them only leaves a stale price and a stale preview —
        // so a failure here is logged and swallowed.
        private unsafe void RefreshFurnitureDyePanelVisuals(IntPtr panel, List<FurnitureDyeSubPart> sub,
                                                            byte cr, byte cg, byte cb)
        {
            try
            {
                IntPtr panelClass = auraMonoObjectGetClass(panel);
                IntPtr setCost = panelClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(panelClass, "SetDyeColorCost", 0);
                if (setCost != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(setCost, panel, IntPtr.Zero, ref exc);
                }

                if (!this.TryGetMonoObjectMember(panel, "_guiModel", out IntPtr guiModel)
                    || guiModel == IntPtr.Zero)
                {
                    return;
                }

                uint modelPin = AuraMonoPinNew(guiModel);
                try
                {
                    IntPtr modelClass = auraMonoObjectGetClass(guiModel);
                    // SetDyeColor(int bodyIndex, Color color) — the 2-arg overload; the 3-arg one
                    // takes a palette index and is not what we want.
                    IntPtr setDye = modelClass == IntPtr.Zero
                        ? IntPtr.Zero
                        : this.FindAuraMonoMethodOnHierarchy(modelClass, "SetDyeColor", 2);
                    if (setDye == IntPtr.Zero)
                    {
                        return;
                    }

                    for (int i = 0; i < sub.Count; i++)
                    {
                        int bodyIndex = sub[i].Body;
                        // UnityEngine.Color is four floats, by value.
                        float* col = stackalloc float[4];
                        col[0] = cr / 255f;
                        col[1] = cg / 255f;
                        col[2] = cb / 255f;
                        col[3] = 1f;

                        IntPtr* args = stackalloc IntPtr[2];
                        args[0] = (IntPtr)(&bodyIndex);
                        args[1] = (IntPtr)col;
                        IntPtr exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(setDye, guiModel, (IntPtr)args, ref exc);
                    }
                }
                finally
                {
                    if (modelPin != 0U) { AuraMonoPinFree(modelPin); }
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(FurnitureDyeTag, "panel redraw threw: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Cost lookup — TableData.TableDyeColors, one walk per (item, body), cached
        // ----------------------------------------------------------------------------------------

        private FurnitureDyeCost FurnitureDyeCostFor(int staticId, byte body)
        {
            int key = (staticId << 8) | body;
            if (this.furnitureDyeCostCache.TryGetValue(key, out FurnitureDyeCost hit))
            {
                return hit;
            }
            if (this.furnitureDyeCostTableMissing)
            {
                return null;
            }

            FurnitureDyeCost found = null;
            try
            {
                if (this.TryGetFurnitureDyeCostTable(out IntPtr table) && table != IntPtr.Zero)
                {
                    found = this.ScanFurnitureDyeCostTable(table, staticId, body);
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(FurnitureDyeTag, "cost lookup threw: " + ex.Message);
            }

            this.furnitureDyeCostCache[key] = found;
            return found;
        }

        private bool TryGetFurnitureDyeCostTable(out IntPtr table)
        {
            table = IntPtr.Zero;

            // TableData sits in the GLOBAL namespace of the EcsClient image. Do not reach for
            // FindAuraMonoClassAcrossLoadedAssemblies here: it returns Zero for an empty namespace
            // by design, and "EcsClient" is the image name, not the namespace - both miss.
            // (Measured: that pair -> 0, FindAuraMonoClassInAllLoadedImages("TableData", "") -> hit.)
            IntPtr tableDataClass = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            if (tableDataClass == IntPtr.Zero)
            {
                this.furnitureDyeCostTableMissing = true;
                FeatureLog.Fail(FurnitureDyeTag, "TableData class unresolved — dye costs will show as 0");
                return false;
            }

            IntPtr field = this.FindAuraMonoFieldOnHierarchy(tableDataClass, "TableDyeColors");
            if (field == IntPtr.Zero)
            {
                this.furnitureDyeCostTableMissing = true;
                FeatureLog.Fail(FurnitureDyeTag, "TableData.TableDyeColors not found — dye costs will show as 0");
                return false;
            }

            // Static REFERENCE field on its own declaring class — the shape the static-read helper
            // is safe for (a value-type static read through the object helper is the one that AVs).
            return this.TryReadFurnitureDyeStaticObject(field, out table) && table != IntPtr.Zero;
        }

        private unsafe bool TryReadFurnitureDyeStaticObject(IntPtr field, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (auraMonoFieldStaticGetValue == null
                || !this.TryGetAuraMonoStaticFieldVtable(field, out IntPtr vtable) || vtable == IntPtr.Zero)
            {
                return false;
            }

            IntPtr obj = IntPtr.Zero;
            auraMonoFieldStaticGetValue(vtable, field, (IntPtr)(&obj));
            value = obj;
            return obj != IntPtr.Zero;
        }

        private FurnitureDyeCost ScanFurnitureDyeCostTable(IntPtr table, int staticId, byte body)
        {
            uint pin = AuraMonoPinNew(table);
            List<uint> pins = new List<uint>();
            try
            {
                List<IntPtr> rows = new List<IntPtr>();
                // Dictionary<int, TableDyeColor>: enumerating it yields KeyValuePair boxes, so ask
                // for the Values collection instead and read the rows directly.
                if (!this.TryGetMonoObjectMember(table, "Values", out IntPtr values) || values == IntPtr.Zero)
                {
                    values = table;
                }
                if (!this.TryEnumerateAuraMonoCollectionItems(values, rows, pins))
                {
                    return null;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr row = rows[i];
                    if (row == IntPtr.Zero
                        || !this.TryGetMonoInt32Member(row, "itemId", out int rowItem) || rowItem != staticId
                        || !this.TryGetMonoInt32Member(row, "colorIndex", out int rowPart) || rowPart != body)
                    {
                        continue;
                    }

                    FurnitureDyeCost cost = new FurnitureDyeCost();
                    this.TryGetMonoInt32Member(row, "currencyCost", out cost.CurrencyCost);
                    this.TryGetMonoInt32Member(row, "itemType", out cost.ItemId);
                    this.TryGetMonoInt32Member(row, "itemCost", out cost.ItemCost);
                    return cost;
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                if (pin != 0U) { AuraMonoPinFree(pin); }
            }

            return null;
        }
    }
}
