using System;
using System.Runtime.InteropServices;
using System.Threading;
using MonoMod.RuntimeDetour;

namespace HeartopiaMod
{
    // Reveal Blocked Players on Map — puts blocked players' dots back on the big map and the
    // minimap (and, as a side effect, their avatars back in the world).
    //
    // WHY IT IS NEEDED: the Stealth Block feature mass-blocks the town so nobody can see the farm
    // run, and the game hides every blocked player from both maps. That leaves the map blank
    // exactly where situational awareness matters most — "who just walked up to my node".
    //
    // HOW THE GAME HIDES THEM (two paths, verified in the dumps):
    //   PULL — every spot build/refresh gates on
    //          DataModule<BlockingSystem>.Instance.GetBlockStateByNetId(netId) != BlockState.none:
    //          MapPanel:723 / :832 / :1236 / :1676 -> MapSpotWidget.SetViewActive(false), and
    //          MiniMapSystem:137, where the spot is not even created.
    //   PUSH — BlockingSystem drives IBlockFunction.UpdateBlockState from its own _playerBlockStates
    //          dictionary (NOT through the getter): MapPanel.UpdateBlockState ->
    //          MapSpotWidget.SetBlocked(true), and CommonMapBar.UpdateBlockState -> RefreshMiniMap
    //          (which rebuilds through MiniMapSystem, i.e. back onto the PULL path).
    //
    // So detour 1 (the getter) covers the minimap completely and the big map everywhere except a
    // block that lands while the big map is already open; detour 2 (SetBlocked) closes that hole.
    //
    // Detour 2 forwards `false` rather than no-opping: SetBlocked also writes _isBlocked, which
    // MapSpotWidget.RefreshInLOD:130 reads independently. A silent no-op would leave that field
    // stale and the widget would blink out on the next LOD change.
    //
    // BOTH BlockState bits are masked (active = "I blocked them", passive = "they blocked me"), by
    // explicit request: the map should show everyone. Masking passive does NOT make us visible to
    // them — their client holds its own `active` bit for us and keeps hiding us either way.
    //
    // NOT covered: floating name/head bubbles (PlayerImmersiveWidget:72, PlayerLeaveWidget:67,
    // FriendPanel) filter through BlockListProtocolManager.IsPlayerInBlockList(shortId) — a
    // different path (the ECS block service), untouched here. The dot comes back, the nameplate
    // does not. Deliberate: fewer detours.
    public partial class HeartopiaComplete
    {
        // Persisted toggle. OFF = vanilla hiding (default). Detours are installed lazily, only
        // once this has been turned on, so a user who never touches it carries zero extra hooks.
        public bool mapRevealBlockedPlayers;

        internal static int mapRevealMaskedCount;

        private const string MapRevealWorldReadyCallbackName = "MapRevealBlocked";

        private static readonly string[] MapRevealBlockingImageNames =
        {
            "XDTGameSystem", "XDTGameSystem.dll",
            "Client", "Client.dll"
        };

        private static readonly string[] MapRevealWidgetImageNames =
        {
            "XDTGameUI", "XDTGameUI.dll",
            "Client", "Client.dll"
        };

        private bool mapRevealCallbackRegistered;
        private bool mapRevealHookTried;

        private static NativeDetour mapRevealBlockStateDetour;
        private static NativeDetour mapRevealSetBlockedDetour;

        private static GetBlockStateByNetIdHookDelegate mapRevealBlockStateKeepAlive;
        private static MapSpotSetBlockedHookDelegate mapRevealSetBlockedKeepAlive;

        private static GetBlockStateByNetIdHookDelegate mapRevealBlockStateTrampoline;
        private static MapSpotSetBlockedHookDelegate mapRevealSetBlockedTrampoline;

        // BlockState GetBlockStateByNetId(uint netId) on a DataModule instance ->
        // int(IntPtr self, uint netId); BlockState is a [Flags] enum:int returned in EAX.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetBlockStateByNetIdHookDelegate(IntPtr self, uint netId);

        // void MapSpotWidget.SetBlocked(bool isBlocked) -> void(IntPtr self, int isBlocked);
        // mono passes a managed bool as int32.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MapSpotSetBlockedHookDelegate(IntPtr self, int isBlocked);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MapRevealCompileMethodDelegate(IntPtr method);

        // Cheap per-frame tick: with the toggle off (default) this is a single bool test.
        private void ProcessMapRevealBlockedOnUpdate()
        {
            if (!this.mapRevealBlockedPlayers || this.mapRevealHookTried || mapRevealBlockStateTrampoline != null)
            {
                return;
            }

            // Hook installs run on the world-ready gate, never from a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent.
            if (!this.mapRevealCallbackRegistered)
            {
                this.mapRevealCallbackRegistered = true;
                this.RegisterWorldReadyCallback(MapRevealWorldReadyCallbackName, this.TryInstallMapRevealHooksOnWorldReady);
            }
        }

        // Called by the UI when the toggle flips on: if a world is already up, the gate has long
        // since drained, so re-arm the entry instead of waiting for the next world load.
        private void OnMapRevealBlockedToggled(bool value)
        {
            if (value == this.mapRevealBlockedPlayers)
            {
                return;
            }

            this.mapRevealBlockedPlayers = value;
            if (value && this.mapRevealCallbackRegistered && !this.mapRevealHookTried && mapRevealBlockStateTrampoline == null)
            {
                this.ResetWorldReadyCallback(MapRevealWorldReadyCallbackName);
            }

            try { this.SaveKeybinds(false); } catch { }
        }

        // Returns true when settled for this world (installed, or permanently unavailable), false
        // to be retried by the gate.
        private bool TryInstallMapRevealHooksOnWorldReady()
        {
            if (this.mapRevealHookTried || mapRevealBlockStateTrampoline != null)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false; // AuraMono not up yet — retry
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                MapRevealCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<MapRevealCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.mapRevealHookTried = true;
                    ModLogger.Msg("[MapReveal] mono_compile_method unavailable — feature off.");
                    return true;
                }

                if (!this.TryInstallMapRevealBlockStateHook(compile))
                {
                    return this.mapRevealHookTried;
                }

                // Secondary hook: a miss here only costs the "blocked while the big map is open"
                // case, so it never burns the primary.
                this.TryInstallMapRevealSetBlockedHook(compile);
                return true;
            }
            catch (Exception ex)
            {
                this.mapRevealHookTried = true;
                ModLogger.Msg("[MapReveal] install pass failed: " + ex.Message);
                return true;
            }
        }

        private bool TryInstallMapRevealBlockStateHook(MapRevealCompileMethodDelegate compile)
        {
            const string nameSpace = "XDTGameSystem.GameplaySystem.Blocking";
            const string shortName = "BlockingSystem";

            IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, MapRevealBlockingImageNames);
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
            }
            if (cls == IntPtr.Zero)
            {
                return false; // image not loaded yet — retry
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "GetBlockStateByNetId", 1);
            if (method == IntPtr.Zero)
            {
                this.mapRevealHookTried = true;
                ModLogger.Msg("[MapReveal] BlockingSystem.GetBlockStateByNetId(1) not found — feature off (game update?).");
                return false;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                this.mapRevealHookTried = true;
                ModLogger.Msg("[MapReveal] mono_compile_method returned null for GetBlockStateByNetId — feature off.");
                return false;
            }

            mapRevealBlockStateKeepAlive = MapRevealBlockStateDetourBody;
            mapRevealBlockStateDetour = new NativeDetour(nativePtr, mapRevealBlockStateKeepAlive);
            mapRevealBlockStateTrampoline = mapRevealBlockStateDetour.GenerateTrampoline<GetBlockStateByNetIdHookDelegate>();
            if (mapRevealBlockStateTrampoline == null)
            {
                // Install rollback, not a live-detour teardown (the only case where Undo is safe —
                // memory: native-detours-world-change-corruption).
                try { mapRevealBlockStateDetour?.Undo(); } catch { }
                mapRevealBlockStateDetour = null;
                mapRevealBlockStateKeepAlive = null;
                this.mapRevealHookTried = true;
                ModLogger.Msg("[MapReveal] trampoline unavailable for GetBlockStateByNetId; detour reverted — feature off.");
                return false;
            }

            ModLogger.Msg("[MapReveal] hooked BlockingSystem.GetBlockStateByNetId @0x" + nativePtr.ToInt64().ToString("X"));
            return true;
        }

        private void TryInstallMapRevealSetBlockedHook(MapRevealCompileMethodDelegate compile)
        {
            if (mapRevealSetBlockedTrampoline != null)
            {
                return;
            }

            const string nameSpace = "XDTGame.UI.Widget";
            const string shortName = "MapSpotWidget";

            IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, MapRevealWidgetImageNames);
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
            }
            if (cls == IntPtr.Zero)
            {
                ModLogger.Msg("[MapReveal] MapSpotWidget not loaded — big-map push path left vanilla"
                    + " (reopen the map after a block to refresh).");
                return;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "SetBlocked", 1);
            if (method == IntPtr.Zero)
            {
                ModLogger.Msg("[MapReveal] MapSpotWidget.SetBlocked(1) not found — big-map push path left vanilla.");
                return;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                ModLogger.Msg("[MapReveal] mono_compile_method null for MapSpotWidget.SetBlocked — big-map push path left vanilla.");
                return;
            }

            mapRevealSetBlockedKeepAlive = MapRevealSetBlockedDetourBody;
            mapRevealSetBlockedDetour = new NativeDetour(nativePtr, mapRevealSetBlockedKeepAlive);
            mapRevealSetBlockedTrampoline = mapRevealSetBlockedDetour.GenerateTrampoline<MapSpotSetBlockedHookDelegate>();
            if (mapRevealSetBlockedTrampoline == null)
            {
                try { mapRevealSetBlockedDetour?.Undo(); } catch { }
                mapRevealSetBlockedDetour = null;
                mapRevealSetBlockedKeepAlive = null;
                ModLogger.Msg("[MapReveal] trampoline unavailable for SetBlocked; detour reverted — big-map push path left vanilla.");
                return;
            }

            ModLogger.Msg("[MapReveal] hooked MapSpotWidget.SetBlocked @0x" + nativePtr.ToInt64().ToString("X"));
        }

        // Native->coreclr reverse-pinvoke bodies. Allocation-free, no Mono calls, no logging: the
        // getter is hit once per player spot per map/minimap refresh, and both run from game code
        // that can be mid-teardown.
        private static int MapRevealBlockStateDetourBody(IntPtr self, uint netId)
        {
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.mapRevealBlockedPlayers)
            {
                Interlocked.Increment(ref mapRevealMaskedCount);
                return 0; // BlockState.none — both `active` and `passive` masked
            }

            return mapRevealBlockStateTrampoline != null ? mapRevealBlockStateTrampoline.Invoke(self, netId) : 0;
        }

        private static void MapRevealSetBlockedDetourBody(IntPtr self, int isBlocked)
        {
            if (mapRevealSetBlockedTrampoline == null)
            {
                return;
            }

            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.mapRevealBlockedPlayers)
            {
                mapRevealSetBlockedTrampoline.Invoke(self, 0);
                return;
            }

            mapRevealSetBlockedTrampoline.Invoke(self, isBlocked);
        }

    }
}
