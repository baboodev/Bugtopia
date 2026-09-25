using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Furniture Dye — the MODEL half of the free colour picker (the UI half is
    // HeartopiaComplete.UguiColorPicker.cs).
    //
    // ── WHY THIS EXISTS ─────────────────────────────────────────────────────────────────────────
    // The game's own furniture dye offers a fixed palette per part — 20-ish swatches, no free
    // colour. That is a CLIENT-UI limit, not a data or server limit, and the difference was
    // measured, not assumed (2026-09-09, Workbench 330001):
    //
    //   * DyeColorData.color is a raw packed int, (r<<24)|(g<<16)|(b<<8)|a — not a palette index.
    //   * The live renderer GamePlay.Dye.DyeColorClientUtil.GetDyeColor uses `.color.ToColor32()`
    //     straight, with no palette lookup.
    //   * DyeColorCosts keys on (staticId, body) — the PART. The price does not depend on colour.
    //   * A pure-magenta 0xFF00FFFF was applied, saved, survived a level transition out of the
    //     build sandbox, and the server CHARGED 2 units of Dye (41001) for it: 11 -> 9. A rejected
    //     operation would have hit RevertInvalidLocalOperations + CraftBank.RevertCurrency instead.
    //
    // The game even ships an unreachable HSV picker for this: DyeColorPanel_Auto binds
    // paintTool@t@go/customDyeBar@go (H/S/V sliders, three recent-colour slots, confirm/cancel) and
    // the server syncs LatestUsedDayColorComponent for the recent list — but the only code that
    // shows it hangs off a list cell at index == colors.Length, and the list is filled with
    // SetCount(colors.Length). The cell is never built, and nothing binds the sliders.
    //
    // ⚠ WALLS / FLOORS / CEILINGS ARE NOT THIS. Their colour lives in the bake as ONE BYTE of
    // palette index per location, re-resolved as material.colorThemes[colorindex] in
    // BakeRenderingProcessorFloor/Wall. An arbitrary RGB has nowhere to live there, so this feature
    // deliberately refuses them (see FurnitureDyeIsStructure).
    //
    // ── WHAT "DYEABLE" MEANS ────────────────────────────────────────────────────────────────────
    // Two independent conditions, both mirrored from the game:
    //   1. NOT structure — HomelandSystem.CheckCanPaint(staticId) is EntityType floor/wall/
    //      quarterwall; those take the paint-style flow, not the dye flow.
    //   2. The item has at least one dyeable PART — ItemDyeColorData's constructor keeps only
    //      ColorParts whose `colors` array is non-empty, and that is what the dye panel tabs on.
    //      We read the same source: DyeColorConfig.itemDyeColorConfigs[staticId].colorParts.
    //
    // `body` == `partIndex` for furniture. Proven by DyeColorPanel._ConfirmSave, which reconciles
    // its (part, colour) pairs against existing rows with `x.part == dyeColorData.body`.
    //
    // ── HOW A COLOUR IS APPLIED ─────────────────────────────────────────────────────────────────
    // Exactly the path the build confirm already uses, so nothing here is a private back door:
    //   craftBox.buildObject          -> BuildSingle
    //   BuildSingle.element           -> IBuildBoxElement (a BuildComponent)
    //   element.GenDyeColorData()     -> a FRESH List<DyeColorData> (the game never hands back its
    //                                    own), which we Clear and refill
    //   BuildSingle.ModifyDyeColor(list) -> writes FurnitureBaseData.dyeDolorData + UpdateData,
    //                                    so the object recolours on the spot
    // The player then commits with the game's own confirm. We never send a network command
    // ourselves: the build save carries the rows, and the server prices them normally.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogFurnitureDye = false;

        private const string FurnitureDyeTag = "FurnitureDye";

        // IBuildBoxElement is implemented explicitly, so the methods carry their interface-qualified
        // names. The short names are tried too, in case a future build implements them implicitly.
        private const string FurnitureDyeGenName =
            "XDTLevelAndEntity.Core.Craft.IBuildBoxElement.GenDyeColorData";
        private const string FurnitureDyeStaticIdName =
            "XDTLevelAndEntity.Core.Craft.IBuildBoxElement.GetStaticId";

        // DyeColorData: body@0, colorIndex@1, color@4, texture@8.
        private const int FurnitureDyeRowSize = 12;
        // BuildDyeData.Colors is [MaxLength(10)] — more rows than that and the command is refused
        // at deserialize time, so the cap belongs here rather than at the wire.
        private const int FurnitureDyeMaxRows = 10;

        // ColorPart { int partNameTextId; byte partIndex; Color32[] colors; } — 16 bytes on x64,
        // the reference field last and 8-aligned. Both numbers are VERIFIED against the running
        // build before any read, so a layout change fails loudly instead of returning garbage.
        private const int FurnitureDyeColorPartSize = 16;
        private const int FurnitureDyeNameTextIdOffset = 0;
        private const int FurnitureDyeColorsOffset = 8;
        private const int FurnitureDyePartIndexOffset = 4;
        private const string FurnitureDyeColor32ArrayClass = "Color32[]";

        // EntityType values that take the PAINT-STYLE flow instead (HomelandSystem.CheckCanPaint).
        private const int FurnitureDyeEntityTypeWall = 1;
        private const int FurnitureDyeEntityTypeFloor = 2;
        private const int FurnitureDyeEntityTypeQuarterWall = 24;

        // One ColorPart: a single `body` with its own swatch list.
        internal sealed class FurnitureDyeSubPart
        {
            public byte Body;            // ColorPart.partIndex == DyeColorData.body
            public int[] Palette;        // this sub-part's own swatches
            public int DefaultColor { get { return this.Palette[0]; } }
        }

        // One row in the picker's part selector. ⚠ NOT one ColorPart: ItemDyeColorData.AddColorPart
        // MERGES every ColorPart sharing a non-zero partNameTextId into a single UI part, and the
        // game's own panel drives them together — clicking swatch i writes colors[i] to EACH of
        // them, so a two-tone leg+frame stays coordinated. Parts with partNameTextId == 0 are never
        // merged (they get generated tab names) and so each become their own row here.
        internal sealed class FurnitureDyePart
        {
            public int NameTextId;
            public readonly List<FurnitureDyeSubPart> Sub = new List<FurnitureDyeSubPart>();

            // The row's representative swatches — the first sub-part's. Picking swatch i still
            // writes each sub-part's OWN colors[i]; this is only what the row draws.
            public int[] Palette { get { return this.Sub[0].Palette; } }
        }

        // What is focused right now, as far as dyeing is concerned.
        // Where the colour has to be written. The two are genuinely different actions, not two
        // skins of one: Build edits a focused build object and rides the build save, Panel fills in
        // the game's own dye panel and rides the player's Confirm.
        internal enum FurnitureDyeSource
        {
            Build,
            Panel,
        }

        internal sealed class FurnitureDyeTarget
        {
            public FurnitureDyeSource Source;
            public int StaticId;
            public List<FurnitureDyePart> Parts;
            public readonly Dictionary<byte, int> Current = new Dictionary<byte, int>();
            // Panel mode only: the tab the game's own panel has selected. The picker follows it
            // rather than offering its own part row — the panel already has one.
            public int PanelSelectedPart;
        }

        // staticId -> parts (null = looked up and NOT dyeable). The config never changes at
        // runtime, so one lookup per item id is enough for the session.
        private readonly Dictionary<int, List<FurnitureDyePart>> furnitureDyePartCache =
            new Dictionary<int, List<FurnitureDyePart>>();
        private readonly Dictionary<int, int> furnitureDyeEntityTypeCache = new Dictionary<int, int>();

        // DyeColorConfig, re-resolved per world. Held through AuraMonoObjectCache, never as a raw
        // MonoObject* field: the cache owns the pin and drops the object by itself when the scene
        // epoch turns over, so nothing here can survive into a world where it is no longer live.
        private AuraMonoObjectCache furnitureDyeConfigCache;
        private int furnitureDyeConfigEpoch = -1;

        private FurnitureDyeTarget furnitureDyeTarget;
        private int furnitureDyeTargetStaticId = -1;
        private string furnitureDyeWhyNot = string.Empty;

        internal FurnitureDyeTarget FurnitureDyeFocusedTarget
        {
            get { return this.furnitureDyeTarget; }
        }

        internal string FurnitureDyeWhyNot
        {
            get { return this.furnitureDyeWhyNot; }
        }

        // ----------------------------------------------------------------------------------------
        // Focus tracking — called from the picker's own tick, never unconditionally: every step
        // below is an AuraMono invoke chain and there is no reason to walk it when nothing is
        // listening.
        // ----------------------------------------------------------------------------------------

        internal void RefreshFurnitureDyeTarget()
        {
            try
            {
                if (!this.IsWorldReady || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    this.ClearFurnitureDyeTarget("not in a world yet");
                    return;
                }

                // The dye panel is a full-screen modal: while it is up it owns the interaction, so
                // it wins over whatever build object happens to still be focused behind it.
                if (this.TryRefreshFurnitureDyePanelTarget())
                {
                    this.furnitureDyeWhyNot = string.Empty;
                    return;
                }

                if (!this.TryGetFurnitureDyeHandles(out IntPtr buildSingle, out IntPtr element)
                    || element == IntPtr.Zero)
                {
                    this.ClearFurnitureDyeTarget("nothing is focused — pick an object in build mode");
                    return;
                }

                if (!this.TryGetFurnitureDyeStaticId(element, out int staticId) || staticId <= 0)
                {
                    this.ClearFurnitureDyeTarget("focused object has no static id");
                    return;
                }

                if (this.FurnitureDyeIsStructure(staticId))
                {
                    this.ClearFurnitureDyeTarget("walls, floors and ceilings use paint styles, not dye"
                        + " — their colour is a palette INDEX in the bake, not an RGB value");
                    return;
                }

                List<FurnitureDyePart> parts = this.FurnitureDyePartsFor(staticId);
                if (parts == null || parts.Count == 0)
                {
                    this.ClearFurnitureDyeTarget("this item has no dyeable parts");
                    return;
                }

                // Same target as last frame: refresh only the live colours, keep the parts.
                if (this.furnitureDyeTarget == null || this.furnitureDyeTargetStaticId != staticId)
                {
                    this.furnitureDyeTarget = new FurnitureDyeTarget
                    {
                        Source = FurnitureDyeSource.Build,
                        StaticId = staticId,
                        Parts = parts,
                    };
                    this.furnitureDyeTargetStaticId = staticId;
                    FeatureLog.Once(FurnitureDyeTag, "target:" + staticId,
                        "focused a dyeable item: staticId " + staticId + ", " + parts.Count + " part(s)");
                }

                this.ReadFurnitureDyeCurrentColors(element, this.furnitureDyeTarget);
                this.furnitureDyeWhyNot = string.Empty;
            }
            catch (Exception ex)
            {
                this.ClearFurnitureDyeTarget("read failed: " + ex.Message);
                FeatureLog.Fail(FurnitureDyeTag, "focus read threw: " + ex.Message);
            }
        }

        private void ClearFurnitureDyeTarget(string why)
        {
            this.furnitureDyeTarget = null;
            this.furnitureDyeTargetStaticId = -1;
            this.furnitureDyeWhyNot = why;
        }

        // craftBox -> buildObject (BuildSingle) -> element. The same walk
        // TryGetBuildingFocusedElementQuiet does, but it needs the BuildSingle too (that is what
        // carries ModifyDyeColor), so the chain is repeated rather than the element re-derived.
        private bool TryGetFurnitureDyeHandles(out IntPtr buildSingle, out IntPtr element)
        {
            buildSingle = IntPtr.Zero;
            element = IntPtr.Zero;

            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj))
            {
                return false;
            }
            // Same signal the Free Placement / Move Panel gates on (UpdateBuildingMovePanelState):
            // CraftState.Focus == 2 means an object is held or focused. Without it the craftBox can
            // still hand back a stale buildObject from the previous selection, and the picker would
            // sit there offering to dye something the player already dropped.
            if (!this.TryGetPadBuildAuraSubState(moduleObj, out int subState) || subState != 2)
            {
                return false;
            }
            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr craftBoxObj, "GetCraftBox")
                || craftBoxObj == IntPtr.Zero)
            {
                return false;
            }
            if ((!this.TryInvokeAuraMonoZeroArg(craftBoxObj, out buildSingle, "get_buildObject")
                    || buildSingle == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(craftBoxObj, "buildObject", out buildSingle)
                    || buildSingle == IntPtr.Zero))
            {
                buildSingle = IntPtr.Zero;
                return false;
            }
            // A group edit fans one change out over many objects with their own part layouts —
            // out of scope, and silently dyeing the wrong things is worse than refusing.
            if (this.TryInvokeAuraMonoZeroArg(craftBoxObj, out IntPtr grp, "get_isGroup")
                && grp != IntPtr.Zero && this.ReadAuraMonoBoxedInt32(grp) != 0)
            {
                buildSingle = IntPtr.Zero;
                return false;
            }
            if ((!this.TryGetMonoObjectMember(buildSingle, "element", out element) || element == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(buildSingle, out element, "get_Element", "get_element")
                    || element == IntPtr.Zero))
            {
                buildSingle = IntPtr.Zero;
                element = IntPtr.Zero;
                return false;
            }
            return true;
        }

        private bool TryGetFurnitureDyeStaticId(IntPtr element, out int staticId)
        {
            staticId = 0;
            if (!this.TryInvokeAuraMonoZeroArg(element, out IntPtr boxed,
                    FurnitureDyeStaticIdName, "GetStaticId")
                || boxed == IntPtr.Zero)
            {
                return false;
            }
            staticId = this.ReadAuraMonoBoxedInt32(boxed);
            return staticId > 0;
        }

        // HomelandSystem.CheckCanPaint: floor / wall / quarterwall take the paint-style flow.
        private bool FurnitureDyeIsStructure(int staticId)
        {
            if (!this.furnitureDyeEntityTypeCache.TryGetValue(staticId, out int entityType))
            {
                entityType = this.TryGetFurnitureDyeEntityType(staticId, out int t) ? t : 0;
                this.furnitureDyeEntityTypeCache[staticId] = entityType;
            }

            return entityType == FurnitureDyeEntityTypeWall
                || entityType == FurnitureDyeEntityTypeFloor
                || entityType == FurnitureDyeEntityTypeQuarterWall;
        }

        private bool TryGetFurnitureDyeEntityType(int staticId, out int entityType)
        {
            entityType = 0;
            try
            {
                IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", null);
                if (tableData == IntPtr.Zero)
                {
                    return false;
                }
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(tableData, "GetEntityTypeID", 1);
                if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }
                return this.TryInvokeFurnitureDyeIntArg(method, IntPtr.Zero, staticId, out entityType);
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryInvokeFurnitureDyeIntArg(IntPtr method, IntPtr instance, int value,
                                                        out int result)
        {
            result = 0;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr boxed = IntPtr.Zero;
            try
            {
                boxed = auraMonoRuntimeInvoke != null
                    ? auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc)
                    : IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }
            result = this.ReadAuraMonoBoxedInt32(boxed);
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // The part list — DyeColorConfig.itemDyeColorConfigs[staticId].colorParts
        // ----------------------------------------------------------------------------------------

        private List<FurnitureDyePart> FurnitureDyePartsFor(int staticId)
        {
            if (this.furnitureDyePartCache.TryGetValue(staticId, out List<FurnitureDyePart> hit))
            {
                return hit;
            }

            List<FurnitureDyePart> found = null;
            try
            {
                if (this.TryEnsureFurnitureDyeConfig(out IntPtr configObj, out string status))
                {
                    found = this.ReadFurnitureDyeParts(configObj, staticId);
                }
                else
                {
                    // Do NOT cache a config-not-ready miss as "not dyeable": the config loads late.
                    FeatureLog.Fail(FurnitureDyeTag, "DyeColorConfig unavailable: " + status);
                    return null;
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(FurnitureDyeTag, "part read threw for " + staticId + ": " + ex.Message);
            }

            this.furnitureDyePartCache[staticId] = found;
            return found;
        }

        // Hands the caller the config pointer instead of parking it in a field: the pointer is only
        // good for as long as the cache's pin holds it, which is this call.
        private bool TryEnsureFurnitureDyeConfig(out IntPtr configObj, out string status)
        {
            status = null;
            if (this.furnitureDyeConfigEpoch == this.WorldReadyEpoch
                && this.furnitureDyeConfigCache.TryGet(out configObj) && configObj != IntPtr.Zero)
            {
                return true;
            }

            configObj = IntPtr.Zero;
            this.furnitureDyeConfigCache.Clear();
            this.furnitureDyeConfigEpoch = -1;

            if (!this.TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out uint managerPin,
                    out status) || configManagerObj == IntPtr.Zero)
            {
                if (managerPin != 0U) { AuraMonoPinFree(managerPin); }
                return false;
            }

            try
            {
                // Auto-property on ConfigManager; the backing field is the fallback for a build
                // where the getter is inlined away.
                if ((!this.TryGetMonoObjectMember(configManagerObj, "DyeColorConfig", out IntPtr cfg)
                        || cfg == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(configManagerObj, "<DyeColorConfig>k__BackingField",
                            out cfg) || cfg == IntPtr.Zero))
                {
                    status = "ConfigManager.DyeColorConfig is null (config not loaded yet)";
                    return false;
                }

                // Set pins; it leaves the cache empty when pinning fails rather than keeping an
                // unpinned pointer, so TryGet is how we learn the pin did not take.
                this.furnitureDyeConfigCache.Set(cfg);
                if (!this.furnitureDyeConfigCache.TryGet(out configObj) || configObj == IntPtr.Zero)
                {
                    status = "could not pin DyeColorConfig";
                    return false;
                }

                this.furnitureDyeConfigEpoch = this.WorldReadyEpoch;
                return true;
            }
            finally
            {
                if (managerPin != 0U) { AuraMonoPinFree(managerPin); }
            }
        }

        private List<FurnitureDyePart> ReadFurnitureDyeParts(IntPtr configObj, int staticId)
        {
            if (!this.TryGetMonoObjectMember(configObj, "itemDyeColorConfigs",
                    out IntPtr list) || list == IntPtr.Zero)
            {
                FeatureLog.Fail(FurnitureDyeTag, "DyeColorConfig.itemDyeColorConfigs is null");
                return null;
            }

            uint listPin = AuraMonoPinNew(list);
            try
            {
                List<IntPtr> entries = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(list, entries))
                {
                    return null;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr e = entries[i];
                    if (e == IntPtr.Zero || !this.TryGetMonoInt32Member(e, "staticId", out int rowStaticId) || rowStaticId != staticId)
                    {
                        continue;
                    }
                    if (!this.TryGetMonoObjectMember(e, "colorParts", out IntPtr parts)
                        || parts == IntPtr.Zero)
                    {
                        return null;
                    }

                    uint partsPin = AuraMonoPinNew(parts);
                    try
                    {
                        return this.ReadFurnitureDyeColorParts(parts);
                    }
                    finally
                    {
                        if (partsPin != 0U) { AuraMonoPinFree(partsPin); }
                    }
                }
            }
            finally
            {
                if (listPin != 0U) { AuraMonoPinFree(listPin); }
            }

            return null;
        }

        private List<FurnitureDyePart> ReadFurnitureDyeColorParts(IntPtr parts)
        {
            if (auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null
                || auraMonoArrayElementSize == null || auraMonoObjectGetClass == null)
            {
                FeatureLog.Fail(FurnitureDyeTag, "AuraMono array exports unavailable");
                return null;
            }

            int count = (int)auraMonoArrayLength(parts).ToUInt32();
            IntPtr klass = auraMonoObjectGetClass(parts);
            int elemSize = klass == IntPtr.Zero ? 0 : auraMonoArrayElementSize(klass);
            if (elemSize != FurnitureDyeColorPartSize)
            {
                // Refuse rather than read blind: a wrong stride walks off into other objects.
                FeatureLog.Fail(FurnitureDyeTag, "ColorPart is " + elemSize + " bytes, expected "
                    + FurnitureDyeColorPartSize + " — layout changed, refusing to read colours");
                return null;
            }

            List<FurnitureDyePart> outv = new List<FurnitureDyePart>();
            for (int i = 0; i < count; i++)
            {
                IntPtr addr = auraMonoArrayAddrWithSize(parts, elemSize, (UIntPtr)(uint)i);
                if (addr == IntPtr.Zero)
                {
                    continue;
                }

                byte body = Marshal.ReadByte(addr, FurnitureDyePartIndexOffset);
                IntPtr colours = Marshal.ReadIntPtr(addr, FurnitureDyeColorsOffset);
                if (colours == IntPtr.Zero || colours.ToInt64() < 0x10000 || (colours.ToInt64() & 7) != 0)
                {
                    continue;
                }
                // The pointer is only trusted once the object at it really is a Color32[].
                IntPtr coloursClass = auraMonoObjectGetClass(colours);
                if (coloursClass == IntPtr.Zero || auraMonoClassGetName == null
                    || !string.Equals(Marshal.PtrToStringAnsi(auraMonoClassGetName(coloursClass)),
                                      FurnitureDyeColor32ArrayClass, StringComparison.Ordinal))
                {
                    continue;
                }

                int[] palette = this.ReadFurnitureDyeColor32Array(colours);
                if (palette == null || palette.Length == 0)
                {
                    // ItemDyeColorData's own constructor drops these — a part with no colours is
                    // not a dyeable part.
                    continue;
                }

                int nameTextId = Marshal.ReadInt32(addr, FurnitureDyeNameTextIdOffset);
                FurnitureDyeSubPart sub = new FurnitureDyeSubPart { Body = body, Palette = palette };

                // ItemDyeColorData.AddColorPart, verbatim: merge on a non-zero name id, never on 0.
                FurnitureDyePart group = null;
                if (nameTextId != 0)
                {
                    for (int g = 0; g < outv.Count; g++)
                    {
                        if (outv[g].NameTextId == nameTextId)
                        {
                            group = outv[g];
                            break;
                        }
                    }
                }
                if (group == null)
                {
                    group = new FurnitureDyePart { NameTextId = nameTextId };
                    outv.Add(group);
                }
                group.Sub.Add(sub);
            }

            return outv.Count > 0 ? outv : null;
        }

        private int[] ReadFurnitureDyeColor32Array(IntPtr array)
        {
            int count = (int)auraMonoArrayLength(array).ToUInt32();
            IntPtr klass = auraMonoObjectGetClass(array);
            int elemSize = klass == IntPtr.Zero ? 0 : auraMonoArrayElementSize(klass);
            if (elemSize != 4 || count <= 0)
            {
                return null;
            }

            int[] outv = new int[count];
            for (int i = 0; i < count; i++)
            {
                IntPtr addr = auraMonoArrayAddrWithSize(array, elemSize, (UIntPtr)(uint)i);
                if (addr == IntPtr.Zero)
                {
                    return null;
                }
                // Color32 is r,g,b,a in memory order; ToHex packs it (r<<24)|(g<<16)|(b<<8)|a.
                byte r = Marshal.ReadByte(addr, 0);
                byte g = Marshal.ReadByte(addr, 1);
                byte b = Marshal.ReadByte(addr, 2);
                byte a = Marshal.ReadByte(addr, 3);
                outv[i] = FurnitureDyePack(r, g, b, a);
            }
            return outv;
        }

        // ----------------------------------------------------------------------------------------
        // Current colours of the focused object
        // ----------------------------------------------------------------------------------------

        private void ReadFurnitureDyeCurrentColors(IntPtr element, FurnitureDyeTarget target)
        {
            target.Current.Clear();

            if (!this.TryInvokeAuraMonoZeroArg(element, out IntPtr list,
                    FurnitureDyeGenName, "GenDyeColorData")
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
                    return; // an undyed object legitimately enumerates empty
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr row = rows[i];
                    if (row == IntPtr.Zero)
                    {
                        continue;
                    }
                    // Boxed DyeColorData — read its fields by name rather than by offset.
                    if (!this.TryGetMonoInt32Member(row, "body", out int body)
                        || !this.TryGetMonoInt32Member(row, "color", out int color))
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
        // Applying — the one mutating entry point
        // ----------------------------------------------------------------------------------------

        /// Write `rows` onto the focused object. Preview only: the object recolours immediately, and
        /// the change reaches the server when the player confirms the placement as usual.
        internal unsafe bool TryApplyFurnitureDye(IList<KeyValuePair<byte, int>> rows, out string status)
        {
            status = "not attempted";

            if (rows == null || rows.Count == 0)
            {
                status = "no rows";
                return false;
            }
            if (!AuraMonoPinningAvailable)
            {
                // Fail closed — an unpinned list can move out from under the Add loop.
                status = "pinning unavailable — refusing to touch the focused object";
                return false;
            }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono not ready";
                return false;
            }
            if (!this.TryGetFurnitureDyeHandles(out IntPtr buildSingle, out IntPtr element))
            {
                status = "nothing is focused";
                return false;
            }

            uint elementPin = AuraMonoPinNew(element);
            uint singlePin = AuraMonoPinNew(buildSingle);
            uint listPin = 0U;
            try
            {
                if (elementPin == 0U || singlePin == 0U)
                {
                    status = "could not pin the focused object";
                    return false;
                }

                // A FRESH list from the game itself — GenDyeColorData never returns its own.
                if (!this.TryInvokeAuraMonoZeroArg(element, out IntPtr list,
                        FurnitureDyeGenName, "GenDyeColorData")
                    || list == IntPtr.Zero)
                {
                    status = "GenDyeColorData returned null";
                    return false;
                }

                listPin = AuraMonoPinNew(list);
                if (listPin == 0U)
                {
                    status = "could not pin the dye list";
                    return false;
                }

                IntPtr listClass = auraMonoObjectGetClass(list);
                IntPtr clearMethod = listClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(listClass, "Clear", 0);
                IntPtr addMethod = listClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                if (clearMethod == IntPtr.Zero || addMethod == IntPtr.Zero)
                {
                    status = "List<DyeColorData>.Clear/Add unresolved";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(clearMethod, list, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Clear threw";
                    return false;
                }

                int n = Math.Min(rows.Count, FurnitureDyeMaxRows);
                for (int i = 0; i < n; i++)
                {
                    // Add takes the struct BY VALUE, so the argument is a pointer to the payload.
                    byte* blob = stackalloc byte[FurnitureDyeRowSize];
                    for (int b = 0; b < FurnitureDyeRowSize; b++)
                    {
                        blob[b] = 0;
                    }
                    blob[0] = rows[i].Key;              // body
                    blob[1] = 0;                        // colorIndex — unused on this path
                    *(int*)(blob + 4) = rows[i].Value;  // color
                    *(int*)(blob + 8) = 0;              // texture — furniture carries none

                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)blob;
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(addMethod, list, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "Add threw on row " + i;
                        return false;
                    }
                }

                IntPtr singleClass = auraMonoObjectGetClass(buildSingle);
                IntPtr modify = singleClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(singleClass, "ModifyDyeColor", 1);
                if (modify == IntPtr.Zero)
                {
                    status = "BuildSingle.ModifyDyeColor(1) unresolved (game update?)";
                    return false;
                }

                // Reference argument: the OBJECT pointer goes in the slot, not its address. The
                // struct case above is the opposite, and swapping them corrupts silently.
                IntPtr* modArgs = stackalloc IntPtr[1];
                modArgs[0] = list;
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(modify, buildSingle, (IntPtr)modArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "ModifyDyeColor threw";
                    return false;
                }

                status = n < rows.Count
                    ? ("applied " + n + " of " + rows.Count + " row(s) - BuildDyeData.Colors is capped"
                       + " at " + FurnitureDyeMaxRows + ", the rest were dropped")
                    : ("applied " + n + " row(s)");
                if (n < rows.Count)
                {
                    // Unreachable on the current tables (the widest item has 7 parts), so if this
                    // ever fires the data changed and the cap needs revisiting - say so loudly.
                    FeatureLog.Fail(FurnitureDyeTag, status);
                }
                FeatureLog.Once(FurnitureDyeTag, "first-apply",
                    "first dye applied to a focused object (" + n + " row(s))");
                FeatureLog.Detail(FurnitureDyeTag, MasterLogFurnitureDye, status);
                return true;
            }
            catch (Exception ex)
            {
                status = "apply threw: " + ex.Message;
                FeatureLog.Fail(FurnitureDyeTag, status);
                return false;
            }
            finally
            {
                if (listPin != 0U) { AuraMonoPinFree(listPin); }
                if (singlePin != 0U) { AuraMonoPinFree(singlePin); }
                if (elementPin != 0U) { AuraMonoPinFree(elementPin); }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Colour packing — Color32.ToHex()'s layout, kept in one place
        // ----------------------------------------------------------------------------------------

        internal static int FurnitureDyePack(byte r, byte g, byte b, byte a)
        {
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        internal static Color FurnitureDyeUnpack(int packed)
        {
            byte r = (byte)((packed >> 24) & 0xFF);
            byte g = (byte)((packed >> 16) & 0xFF);
            byte b = (byte)((packed >> 8) & 0xFF);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        internal static int FurnitureDyePack(Color c)
        {
            // Alpha is always 255 for a real dye colour — a lower one is how the bake's packed
            // colour INDICES give themselves away, and writing one here would look like an index.
            return FurnitureDyePack(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
                255);
        }

        internal static string FurnitureDyeHex(int packed)
        {
            return "#" + ((packed >> 24) & 0xFF).ToString("X2")
                       + ((packed >> 16) & 0xFF).ToString("X2")
                       + ((packed >> 8) & 0xFF).ToString("X2");
        }

        internal static bool TryParseFurnitureDyeHex(string text, out int packed)
        {
            packed = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string s = text.Trim();
            if (s.Length > 0 && s[0] == '#')
            {
                s = s.Substring(1);
            }
            if (s.Length == 3)
            {
                // #abc -> #aabbcc, the shorthand every graphics tool accepts
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            }
            if (s.Length != 6)
            {
                return false;
            }
            for (int i = 0; i < 6; i++)
            {
                if (!Uri.IsHexDigit(s[i]))
                {
                    return false;
                }
            }
            int r = Convert.ToInt32(s.Substring(0, 2), 16);
            int g = Convert.ToInt32(s.Substring(2, 2), 16);
            int b = Convert.ToInt32(s.Substring(4, 2), 16);
            packed = FurnitureDyePack((byte)r, (byte)g, (byte)b, 255);
            return true;
        }
    }
}
