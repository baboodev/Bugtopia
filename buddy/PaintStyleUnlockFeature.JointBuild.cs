using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Paint Style Unlock, JOINT-BUILD half.
    //
    // ── WHY THE DETOURS DO NOT REACH THIS ───────────────────────────────────────────────────────
    // The solo path is CraftBank.GetMaterialByStableType -> DyeFurnitureSystem ->
    // HouseTextureClientService.GetAllUnlockTexture, and that is where the two detours sit. In a
    // joint build the live bank is MultiBuildCraftBank (measured on a live session: Wall 26,
    // Floor 17, Ceil 7 with the detours active), which inherits from MultiSandBoxCraftBank, and
    // that class OVERRIDES both ends:
    //
    //   GetMaterialByStableType(s)  -> return _structDyeColor[s]
    //   CheckTextureIsLock(colors)  -> any texture not in _buildingTextures
    //
    // Both collections are filled by HandleMaterialAndTexture from JointBuildingResData — the
    // session's SHARED resource pool, sent by the server through IJointBuildingClientService:
    //
    //   foreach (var t in TableData.TableHousetextures)
    //       if (data.ResDict.ContainsKey(t.Key)) _structDyeColor[t.Value.type - 1].Add(material);
    //
    // No unlock check and no DyeFurnitureSystem anywhere on it, so nothing to detour.
    //
    // ── HOW IT IS OPENED ────────────────────────────────────────────────────────────────────────
    // By appending to the bank's OWN collections: every missing Housetexture row goes into the
    // matching `_structDyeColor[type - 1]` list, and its id into `_buildingTextures`. Both are
    // concrete instantiations already, so List.Add / HashSet.Add resolve on the object's own
    // class and nothing is inflated. The paint panel holds a reference to that same List (its
    // `_cacheStyles` IS `_structDyeColor[s]`), so the extra styles appear on its next redraw.
    //
    // It is a POLL, deliberately, and not a hook: the bank rebuilds both collections from scratch
    // on entry (InitDataStep) and again on every pool update from the server
    // (OnJointBuildingResUpdated), and the entry fill dispatches no event at all. So a cheap
    // count check at PaintStyleJointPollInterval re-patches whenever a slot has fallen back below
    // what we last left there. When nothing changed, a poll is one module resolve and three
    // get_Count calls.
    //
    // ── KNOWN LIMIT ─────────────────────────────────────────────────────────────────────────────
    // This is weaker ground than the solo half. The pool is explicitly server-shared state — the
    // whole point of a joint build is that the session gets what was shared into it — so the
    // server may well reject a style from outside it at save. There is no dedicated error code
    // for that in XDT.Scene.Shared.Modules.Player.ErrorCode, but the build-save mapper also has
    // NotJointBuildingAuthority and ShopConditionNotEnough. Untested: one wall, save, relog.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float PaintStyleJointPollInterval = 0.5f;

        private float paintStyleJointNextPollAt;
        private FeatureBreakerState paintStyleJointBreaker;

        // What each slot held after our last patch. A lower count means the bank rebuilt it.
        private readonly int[] paintStyleJointTarget = new int[3];
        private bool paintStyleJointPatched;     // at least one patch landed in this world
        private int paintStyleJointEpoch = -1;

        // Called from ProcessPaintStyleUnlockOnUpdate, so it shares the feature's toggle.
        private void ProcessPaintStyleJointBuildOnUpdate()
        {
            if (!this.paintStyleUnlockEnabled || !this.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.paintStyleJointNextPollAt || !this.paintStyleJointBreaker.ShouldRun(now))
            {
                return;
            }
            this.paintStyleJointNextPollAt = now + PaintStyleJointPollInterval;

            if (this.paintStyleJointEpoch != this.WorldReadyEpoch)
            {
                this.paintStyleJointEpoch = this.WorldReadyEpoch;
                Array.Clear(this.paintStyleJointTarget, 0, this.paintStyleJointTarget.Length);
                this.paintStyleJointPatched = false;
            }

            try
            {
                this.TryPatchPaintStyleJointBank();
                this.paintStyleJointBreaker.Success();
            }
            catch (Exception ex)
            {
                this.paintStyleJointBreaker.Failure(PaintStyleUnlockTag + ".JointBuild", ex, now);
            }
        }

        private unsafe void TryPatchPaintStyleJointBank()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || !AuraMonoPinningAvailable)
            {
                return;
            }

            // Not in build mode -> no module. Quiet: that is the normal state.
            if (!this.TryGetPadBuildAuraModule(out IntPtr module) || module == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryInvokeAuraMonoZeroArg(module, out IntPtr bank, "get_CraftBank") || bank == IntPtr.Zero)
            {
                return;
            }

            uint bankPin = AuraMonoPinNew(bank);
            List<uint> pins = new List<uint>();
            try
            {
                // `_structDyeColor` exists only on the MultiSandBoxCraftBank family: its absence is
                // how a solo or plain-sandbox bank is told apart, and those are the detours' job.
                if (!this.TryGetMonoObjectMember(bank, "_structDyeColor", out IntPtr slots) || slots == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(bank, "_buildingTextures", out IntPtr allowed) || allowed == IntPtr.Zero)
                {
                    return;
                }
                pins.Add(AuraMonoPinNew(slots));
                pins.Add(AuraMonoPinNew(allowed));

                IntPtr[] lists = new IntPtr[3];
                // Never patched in this world yet -> patch. Otherwise only when a slot fell below
                // what we left in it, i.e. the bank rebuilt from the pool.
                bool stale = !this.paintStyleJointPatched;
                for (int s = 0; s < 3; s++)
                {
                    lists[s] = this.ReadPaintStyleJointSlot(slots, s);
                    if (lists[s] == IntPtr.Zero)
                    {
                        return;
                    }
                    pins.Add(AuraMonoPinNew(lists[s]));
                    int count = this.ReadPaintStyleJointCount(lists[s]);
                    if (count >= 0 && count < this.paintStyleJointTarget[s])
                    {
                        stale = true;
                    }
                }
                if (!stale)
                {
                    return;
                }

                int added = this.FillPaintStyleJointBank(lists, allowed);
                for (int s = 0; s < 3; s++)
                {
                    this.paintStyleJointTarget[s] = Math.Max(0, this.ReadPaintStyleJointCount(lists[s]));
                }
                this.paintStyleJointPatched = true;

                if (added > 0)
                {
                    this.paintStyleUnlockStatus = "Active — joint build: " + added
                        + " extra style(s) listed; switch the paint tab to see them.";
                    FeatureLog.Life(PaintStyleUnlockTag, "joint build: added " + added
                        + " style(s) to the shared pool list (wall/floor/ceil now "
                        + this.paintStyleJointTarget[0] + "/" + this.paintStyleJointTarget[1] + "/"
                        + this.paintStyleJointTarget[2] + ") — server acceptance untested");
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                if (bankPin != 0U) { AuraMonoPinFree(bankPin); }
            }
        }

        // Element s of List<ItemDyeColorByMaterial>[3] — a reference array, so each slot is a
        // pointer. The stride is checked rather than assumed.
        private IntPtr ReadPaintStyleJointSlot(IntPtr array, int index)
        {
            if (auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null
                || auraMonoArrayElementSize == null || auraMonoObjectGetClass == null)
            {
                return IntPtr.Zero;
            }
            if ((int)auraMonoArrayLength(array).ToUInt32() <= index)
            {
                return IntPtr.Zero;
            }
            IntPtr klass = auraMonoObjectGetClass(array);
            int stride = klass == IntPtr.Zero ? 0 : auraMonoArrayElementSize(klass);
            if (stride != IntPtr.Size)
            {
                FeatureLog.Fail(PaintStyleUnlockTag, "joint build: _structDyeColor stride " + stride
                    + ", expected " + IntPtr.Size + " — layout changed, not touching it");
                return IntPtr.Zero;
            }
            IntPtr addr = auraMonoArrayAddrWithSize(array, stride, (UIntPtr)(uint)index);
            return addr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(addr);
        }

        private int ReadPaintStyleJointCount(IntPtr collection)
        {
            IntPtr klass = auraMonoObjectGetClass(collection);
            IntPtr getCount = klass == IntPtr.Zero ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(klass, "get_Count", 0);
            return this.ReadFurnitureDyeDictCount(collection, getCount);   // same boxed-int read
        }

        // Appends every Housetexture the bank is missing. Returns how many styles were added.
        private unsafe int FillPaintStyleJointBank(IntPtr[] lists, IntPtr allowed)
        {
            // The config pointer is only good while the cache's pin holds it, i.e. for this call.
            if (!this.TryEnsureFurnitureDyeConfig(out IntPtr configObj, out string cfgStatus) || configObj == IntPtr.Zero)
            {
                FeatureLog.Fail(PaintStyleUnlockTag, "joint build: DyeColorConfig unavailable: " + cfgStatus);
                return 0;
            }
            IntPtr cfgClass = auraMonoObjectGetClass(configObj);
            IntPtr getMaterial = cfgClass == IntPtr.Zero ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(cfgClass, "GetMaterial", 1);
            IntPtr listAdd = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(lists[0]), "Add", 1);
            IntPtr setAdd = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(allowed), "Add", 1);
            if (getMaterial == IntPtr.Zero || listAdd == IntPtr.Zero || setAdd == IntPtr.Zero)
            {
                FeatureLog.Fail(PaintStyleUnlockTag, "joint build: GetMaterial/List.Add/HashSet.Add unresolved");
                return 0;
            }

            // What each slot already lists, so the pool's own entries are never duplicated.
            HashSet<int>[] present = new HashSet<int>[3];
            for (int s = 0; s < 3; s++)
            {
                present[s] = this.ReadPaintStyleJointIds(lists[s]);
            }

            if (!this.TryGetPaintStyleTextureTable(out IntPtr table) || table == IntPtr.Zero)
            {
                return 0;
            }

            int added = 0;
            uint tablePin = AuraMonoPinNew(table);
            List<uint> rowPins = new List<uint>();
            try
            {
                if (!this.TryGetMonoObjectMember(table, "Values", out IntPtr values) || values == IntPtr.Zero)
                {
                    return 0;
                }
                List<IntPtr> rows = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(values, rows, rowPins))
                {
                    return 0;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == IntPtr.Zero
                        || !this.TryGetMonoInt32Member(rows[i], "id", out int id) || id <= 0
                        || !this.TryGetMonoInt32Member(rows[i], "type", out int type) || type < 1 || type > 3)
                    {
                        continue;   // type 0 rows are unused in the table
                    }
                    int slot = type - 1;
                    if (present[slot].Contains(id))
                    {
                        continue;
                    }

                    // DyeColorConfig.GetMaterial(int) — null for a texture with no material, which
                    // the game's own fill skips too.
                    int arg = id;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&arg);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr material = auraMonoRuntimeInvoke(getMaterial, configObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || material == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint matPin = AuraMonoPinNew(material);
                    try
                    {
                        // Reference argument: the object pointer itself goes in the slot.
                        IntPtr* addArgs = stackalloc IntPtr[1];
                        addArgs[0] = material;
                        exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(listAdd, lists[slot], (IntPtr)addArgs, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            continue;
                        }

                        // Value argument: the address of the int. HashSet.Add is idempotent, so a
                        // texture the pool already had costs nothing.
                        int texId = id;
                        IntPtr* setArgs = stackalloc IntPtr[1];
                        setArgs[0] = (IntPtr)(&texId);
                        exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(setAdd, allowed, (IntPtr)setArgs, ref exc);

                        present[slot].Add(id);
                        added++;
                    }
                    finally
                    {
                        if (matPin != 0U) { AuraMonoPinFree(matPin); }
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(rowPins);
                if (tablePin != 0U) { AuraMonoPinFree(tablePin); }
            }

            return added;
        }

        private HashSet<int> ReadPaintStyleJointIds(IntPtr list)
        {
            HashSet<int> ids = new HashSet<int>();
            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (this.TryEnumerateAuraMonoCollectionItems(list, items, pins))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i] != IntPtr.Zero && this.TryGetMonoInt32Member(items[i], "materialId", out int id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
            return ids;
        }

        // TableData.TableHousetextures — static Dictionary<int, TableHousetexture> on a class in the
        // GLOBAL namespace (see TryGetFurnitureDyeCostTable for why the image-sweep resolver).
        private bool TryGetPaintStyleTextureTable(out IntPtr table)
        {
            table = IntPtr.Zero;
            IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            IntPtr field = tableData == IntPtr.Zero ? IntPtr.Zero
                : this.FindAuraMonoFieldOnHierarchy(tableData, "TableHousetextures");
            if (field == IntPtr.Zero)
            {
                FeatureLog.Fail(PaintStyleUnlockTag, "joint build: TableData.TableHousetextures not found");
                return false;
            }
            return this.TryReadFurnitureDyeStaticObject(field, out table) && table != IntPtr.Zero;
        }
    }
}
