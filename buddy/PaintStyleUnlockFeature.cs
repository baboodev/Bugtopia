using System;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // ============================================================================================
    // Paint Style Unlock — every wall / floor / ceiling paint style offered in the build paint
    // panel, including the ones the account never bought.
    //
    // ── WHERE THE GATE IS ───────────────────────────────────────────────────────────────────────
    // BuildPaintPanel has no lock check of its own: it renders whatever
    // DyeFurnitureSystem.GetMaterialByStableType() hands it, and that is
    // HouseTextureClientService.GetAllUnlockTexture() — the ONLY filter in the whole path:
    //
    //     foreach (TableHousetexture t in TableData.TableHousetextures.Values)
    //         if (_houseUnlockClientService.IsHouseBuildUnlock(Texture, t.id))          add;
    //         else if (CombinedLogicHelper.Check(entity, _stateCenter, t.condition))    add;
    //
    // Housetexture has 137 rows (type 1 = wall 64, 2 = floor 50, 3 = ceiling 19, plus 4 rows with
    // type 0). 43 carry no condition at all — those are the ones everybody has. The other 94 carry
    // `PlayerHomeLevel >= 999` or `PlayerHomeLevel <= -10`, deliberately unsatisfiable expressions,
    // so their second branch never fires and IsHouseBuildUnlock is the only door.
    //
    // ── WHY TWO HOOKS ───────────────────────────────────────────────────────────────────────────
    //  1. HouseUnlockClientService.IsHouseBuildUnlock(type, id) — makes the PANEL list complete.
    //  2. HouseTextureClientService.TextureIsUnlock(id) — GetAllUnlockTexture does NOT call it (the
    //     logic above is duplicated inline), and it has a consumer of its own:
    //     CraftBank.CheckTextureIsLock -> BuildModule.CheckCanPutModule refuses a whole
    //     blueprint/module when any of its DyeColorData carries a locked texture. Without hook 2
    //     the panel would offer a style that placing a module then rejects.
    //
    // Hook 1 is SCOPED BY TYPE, and that is not decoration. HouseBuildItemUnlockType is
    // { None, Material, Texture } and the same method also gates the build-shop catalogue
    // (HouseMaterialClientService.MaterialIsUnlock) and house modules. Answering true for every
    // type would advertise furniture the account does not own — which the SERVER refuses at save
    // time anyway with ErrorCode.ShopConditionNotEnough (BuildSaveOption.BuildErrorCodeToTipId ->
    // loc 92889, "Shop unlock conditions not met"), i.e. a strictly worse experience. So the hook
    // answers only for Texture and hands every other type to the trampoline unchanged.
    //
    // ── SHAPE ───────────────────────────────────────────────────────────────────────────────────
    // Mono-JIT NativeDetours, never GameAssembly .text patches — the anti-cheat surface rule holds.
    // Installed once and NEVER undone: tearing a live detour down across a world change corrupts
    // (see the native-detour rule in AGENTS.md §7). The toggle only flips a static bool that the
    // hook bodies read, so a UI callback never runs native code.
    //
    // Both delegates return `byte`, not `bool`. Mono hands a 1-byte result back in AL; the default
    // Boolean marshalling would read all four bytes of EAX off the trampoline and could see stale
    // high bytes as `true`. Same convention as AlignmentInAreaDelegate / SlabOverlapDelegate in
    // BuildingFreeRotateFeature.
    //
    // ── KNOWN LIMIT ─────────────────────────────────────────────────────────────────────────────
    // This is the CLIENT's list. Whether the server keeps a style it never granted is UNVERIFIED:
    // XDT.Scene.Shared.Modules.Player.ErrorCode has ~40 build codes and not one of them is about a
    // texture or a paint material, whereas items have ShopConditionNotEnough — suggestive, but an
    // argument from silence. Treat a style that survives a relog as the only proof.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string PaintStyleUnlockTag = "PaintStyleUnlock";

        // HouseBuildItemUnlockType { None = 0, Material = 1, Texture = 2 }
        private const int PaintStyleUnlockTypeTexture = 2;

        private bool paintStyleUnlockEnabled;
        private bool paintStyleUnlockHookTried;
        private string paintStyleUnlockStatus = "Idle.";

        // Read by the native hook bodies — static because a reverse-P/Invoke callback has no `this`.
        private static volatile bool paintStyleUnlockActive;

        private static MonoMod.RuntimeDetour.NativeDetour paintStyleUnlockDetour;
        private static MonoMod.RuntimeDetour.NativeDetour paintStyleTextureDetour;
        private static PaintStyleUnlockHookDelegate paintStyleUnlockHookKeepAlive;    // anti-GC
        private static PaintStyleUnlockHookDelegate paintStyleUnlockTrampoline;
        private static PaintStyleTextureHookDelegate paintStyleTextureHookKeepAlive;  // anti-GC
        private static PaintStyleTextureHookDelegate paintStyleTextureTrampoline;

        // bool HouseUnlockClientService.IsHouseBuildUnlock(HouseBuildItemUnlockType, int)
        // -> byte(self, int, int). The enum is passed as its underlying int.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PaintStyleUnlockHookDelegate(IntPtr self, int type, int id);

        // bool HouseTextureClientService.TextureIsUnlock(int) -> byte(self, int).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PaintStyleTextureHookDelegate(IntPtr self, int textureId);

        public bool PaintStyleUnlockEnabled
        {
            get { return this.paintStyleUnlockEnabled; }
        }

        public string PaintStyleUnlockStatus
        {
            get { return this.paintStyleUnlockStatus; }
        }

        // The detours outlive every world, so unlike EmoteUnlock there is nothing to rebuild on a
        // world change: the hooks hold no Mono pointers and no fabricated objects. All this tick
        // does is install once, then keep the static flag in step with the toggle.
        private void ProcessPaintStyleUnlockOnUpdate()
        {
            if (this.paintStyleUnlockEnabled && !this.paintStyleUnlockHookTried && this.IsWorldReady)
            {
                this.EnsurePaintStyleUnlockHooks();
            }

            paintStyleUnlockActive = this.paintStyleUnlockEnabled && paintStyleUnlockDetour != null;

            // A joint build never asks the detoured services at all — see
            // PaintStyleUnlockFeature.JointBuild.cs.
            this.ProcessPaintStyleJointBuildOnUpdate();
        }

        private void EnsurePaintStyleUnlockHooks()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry next frame, do not burn the tried flag
                }

                IntPtr unlockCls = this.FindAuraMonoClassInAllLoadedImages(
                    "HouseUnlockClientService", "ClientSystem.Homeland");
                IntPtr textureCls = this.FindAuraMonoClassInAllLoadedImages(
                    "HouseTextureClientService", "ClientSystem.Homeland");
                if (unlockCls == IntPtr.Zero || textureCls == IntPtr.Zero)
                {
                    return; // EcsSystem image not loaded yet — retry
                }

                // Past this point every exit is final: the classes exist, so a miss is a real
                // signature change rather than a not-yet-loaded image.
                this.paintStyleUnlockHookTried = true;

                IntPtr unlockPtr = this.ResolveBuildingMonoNative(unlockCls, "IsHouseBuildUnlock", 2);
                IntPtr texturePtr = this.ResolveBuildingMonoNative(textureCls, "TextureIsUnlock", 1);
                if (unlockPtr == IntPtr.Zero)
                {
                    this.PaintStyleUnlockFail("HouseUnlockClientService.IsHouseBuildUnlock(2) not resolved — game update?");
                    return;
                }

                paintStyleUnlockHookKeepAlive = PaintStyleUnlockIsHouseBuildUnlockNative;
                paintStyleUnlockDetour = new MonoMod.RuntimeDetour.NativeDetour(
                    unlockPtr, paintStyleUnlockHookKeepAlive);
                paintStyleUnlockTrampoline =
                    paintStyleUnlockDetour.GenerateTrampoline<PaintStyleUnlockHookDelegate>();
                if (paintStyleUnlockTrampoline == null)
                {
                    // The only place this detour is ever undone: it was never usable, so this is
                    // install rollback, not a live teardown. Leaving it applied without a
                    // trampoline would answer `false` for Material and Module too.
                    try { paintStyleUnlockDetour.Undo(); } catch { }
                    paintStyleUnlockDetour = null;
                    paintStyleUnlockHookKeepAlive = null;
                    this.PaintStyleUnlockFail("no trampoline for IsHouseBuildUnlock; detour reverted");
                    return;
                }

                // Hook 2 is optional: without it the panel still lists everything, only module
                // placement keeps refusing a locked style. Its failure must not undo hook 1.
                bool moduleGateOpen = false;
                if (texturePtr == IntPtr.Zero)
                {
                    FeatureLog.Fail(PaintStyleUnlockTag,
                        "HouseTextureClientService.TextureIsUnlock(1) not resolved — panel list is open, module placement still gated.");
                }
                else
                {
                    paintStyleTextureHookKeepAlive = PaintStyleUnlockTextureIsUnlockNative;
                    paintStyleTextureDetour = new MonoMod.RuntimeDetour.NativeDetour(
                        texturePtr, paintStyleTextureHookKeepAlive);
                    paintStyleTextureTrampoline =
                        paintStyleTextureDetour.GenerateTrampoline<PaintStyleTextureHookDelegate>();
                    if (paintStyleTextureTrampoline == null)
                    {
                        try { paintStyleTextureDetour.Undo(); } catch { }
                        paintStyleTextureDetour = null;
                        paintStyleTextureHookKeepAlive = null;
                        FeatureLog.Fail(PaintStyleUnlockTag,
                            "no trampoline for TextureIsUnlock; detour reverted — panel list is open, module placement still gated.");
                    }
                    else
                    {
                        moduleGateOpen = true;
                    }
                }

                this.paintStyleUnlockStatus = moduleGateOpen
                    ? "Active — reopen the paint panel to see all styles."
                    : "Active (list only) — module placement still refuses locked styles.";
                FeatureLog.Life(PaintStyleUnlockTag, "hooks installed: IsHouseBuildUnlock @0x"
                    + unlockPtr.ToInt64().ToString("X")
                    + (moduleGateOpen ? ", TextureIsUnlock @0x" + texturePtr.ToInt64().ToString("X") : " (TextureIsUnlock off)"));
            }
            catch (Exception ex)
            {
                this.paintStyleUnlockHookTried = true;
                this.PaintStyleUnlockFail("install threw: " + ex.Message);
            }
        }

        private void PaintStyleUnlockFail(string message)
        {
            this.paintStyleUnlockStatus = "Off — " + message;
            FeatureLog.Fail(PaintStyleUnlockTag, message);
        }

        // ── the hooks ───────────────────────────────────────────────────────────────────────────

        // Texture is answered locally; Material and Module fall through untouched, so the build
        // catalogue keeps telling the truth about what the account owns.
        private static byte PaintStyleUnlockIsHouseBuildUnlockNative(IntPtr self, int type, int id)
        {
            if (paintStyleUnlockActive && type == PaintStyleUnlockTypeTexture)
            {
                return 1;
            }

            PaintStyleUnlockHookDelegate trampoline = paintStyleUnlockTrampoline;
            return trampoline != null ? trampoline(self, type, id) : (byte)0;
        }

        // Texture-only by construction — no type to scope on.
        private static byte PaintStyleUnlockTextureIsUnlockNative(IntPtr self, int textureId)
        {
            if (paintStyleUnlockActive)
            {
                return 1;
            }

            PaintStyleTextureHookDelegate trampoline = paintStyleTextureTrampoline;
            return trampoline != null ? trampoline(self, textureId) : (byte)0;
        }
    }
}
