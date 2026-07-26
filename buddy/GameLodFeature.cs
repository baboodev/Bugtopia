using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // GAME LOD (Self → Game LOD) — world detail / draw distance overrides.
    //
    // Research map (memory: world-lod-streaming-map): the game runs five independent
    // LOD/streaming systems; this feature drives the four that are client-moddable, mirroring the
    // game's own debug panel (XDTGame.UI.Panel.ObserverPanel) where one exists:
    //
    //  1. FURNITURE / HOMELAND STREAMING — LoaderManager voxel grid caps object count
    //     (RenderLoadConfig.NumMaxLimit=60) and load distances (LoadDis=[80,30,30,24],
    //     MeshLoadDis=100). Apply = the ObserverPanel.SetHomeland recipe:
    //     LayerDistanceCulling.Instance.enabled=false + LoaderManager.SetParam(max, 3, 20, 20,
    //     dist[4], dist[4], meshDis). Revert = the ObserverPanel.RevertBuildLoader recipe
    //     (re-read RenderLoadConfig getters, SetParam with defaults, culling back on).
    //  2. BRG MESH QUALITY — BrgManager.ForeceLOD0 (static, native icall) forces max mesh LOD on
    //     all batched furniture; AreaPriorityManager.UpdateGlobalLodBias raises the per-area bias
    //     (re-asserted, because SignificanceManager's low-FPS governor drops it to 0.2).
    //  3. VEGETATION / NEIGHBOR HOUSES — TreeLoad/HomeLoad divide their baked LOD threshold
    //     distances by PlayerPrefs "PC_LODBIAS"/10 at load time (default 10 = x1; 5 = x2; 1 = x10).
    //     Nothing else writes that key. Changing it needs a rebake:
    //     TreeLoad.SetAllDynamicInstanceBlockEnable(false→true) + HomeLoad.Inst OnDisable/OnEnable.
    //  4. CHARACTERS — SignificanceManager (DataModule singleton) actively degrades NPC/player/pet
    //     LOD + animation rate by distance in normal play. Enable=false restores full quality
    //     (exactly what the game's photo mode does); re-asserted because mode switches set it back.
    //  5. LIVE ENTITIES — NineCell view streaming culls server-synced entities at per-type cell
    //     ranges (EntityResLoad_NineCell). LevelEntityComponent.SetForceNineCell(short) re-registers
    //     a live component at a bigger range; originals are remembered per netId for revert.
    //     Capped by the server AOI — entities the server never synced cannot appear.
    //  + SHADOWS — URP asset shadowDistance via RenderingSettings.Instance.GetCurrentURPAsset()
    //     (the ObserverPanel.SetShadowDistance path; quality changes swap the asset → re-asserted).
    //
    // All game types here are embedded-Mono (XDT*/EngineWrapper) → AuraMono only, no managed
    // fallback (user rule). Class/method IntPtrs are cached (image lifetime); OBJECT pointers are
    // re-resolved every use and pinned only for the synchronous scope (SGen moving GC).
    // Everything is default-OFF, idempotently re-applied on a slow cadence (world reloads and mode
    // switches silently recreate the managers), and reverted through the game's own default paths.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Config-backed state (persisted via UnifiedConfigData; see HeartopiaComplete.Config.cs)
        // ----------------------------------------------------------------------------------------
        internal bool gameLodFurnitureEnabled = false;
        internal int gameLodFurnitureMaxObjects = 1500;    // 60..5000 (game default 60)
        internal int gameLodFurnitureDistance = 9999;      // 100..9999 m (game default 80/30/30/24)
        internal int gameLodFurnitureMeshDistance = 1000;  // 100..2000 m (game default 100)

        internal bool gameLodForceLod0Enabled = false;
        internal bool gameLodBrgBiasEnabled = false;
        internal float gameLodBrgBias = 2f;                // 1..4 (game default 1)

        internal bool gameLodVegetationEnabled = false;
        internal int gameLodVegetationPref = 5;            // legacy (pre-2026-07-25 configs): raw PC_LODBIAS value
        internal float gameLodVegetationMult = 4f;         // legacy: multiplier-vs-baseline (migrated to the target below)
        internal int gameLodVegetationBaselinePref = 0;    // persisted game PC_LODBIAS, used ONLY to restore on revert
        // ABSOLUTE target instead of "baseline × multiplier": the multiplier form kept getting
        // poisoned (adopting our own raised value as the new baseline, then clamping to ×1) and it
        // hid the number that actually matters. This is written verbatim into PC_LODBIAS.
        internal int gameLodVegetationTargetPref = 300;    // 10..1000
        // Terrain chunks bake their LOD thresholds from PC_LODBIAS AT CHUNK LOAD TIME, and the
        // scene's initial chunks load behind the splash screen. So the value has to be live during
        // the load for distant islands/cliffs to be built at full detail — which is exactly what
        // makes the load slow. There is no way to have both, so it is an explicit choice:
        //   off (default) → stock during load, fast loading, chunks near spawn stay coarse
        //   on            → full detail everywhere, slower loading
        // (a post-load rebake only reaches QualityLoader-owned instance blocks, never chunk-owned
        // terrain — proven live: rebake said "30 -> 300 ok" yet the islands stayed coarse.)
        internal bool gameLodVegetationApplyDuringLoad = false;

        internal bool gameLodSignificanceOffEnabled = false;

        internal bool gameLodNineCellEnabled = false;
        internal float gameLodNineCellMult = 2f;           // 1..5

        internal bool gameLodShadowEnabled = false;
        internal float gameLodShadowDistance = 300f;       // 50..800 m

        // Landscape HLOD proxies (Unity.HLODSystem, IL2CPP side): scene-baked low-poly merged
        // meshes swap to real objects inside hlod1/2LoadAndVisDistance — the multiplier pushes
        // both distances out so full-detail content holds much further.
        internal bool gameLodHlodEnabled = false;
        internal float gameLodHlodMult = 2f;               // 1..4

        // Props XDLod (custom mesh-swap LOD; XDLodManager registry): ForceLOD(0) holds max-detail
        // meshes at any distance for every registered group.
        internal bool gameLodXdLodEnabled = false;

        // ----------------------------------------------------------------------------------------
        // Runtime state
        // ----------------------------------------------------------------------------------------
        private const float GameLodApplyIntervalSeconds = 2.5f;
        private const float GameLodNineCellWalkIntervalSeconds = 3f;
        private const short GameLodNineCellRangeCap = 60;
        private const int GameLodNineCellWalkBudget = 600; // components per walk (GC-storm guard)
        private const int GameLodVegetationPrefDefault = 10;

        // The game's own PC_LODBIAS. CAPTURED ONCE, AT CONFIG LOAD, BEFORE THIS SESSION WRITES
        // ANYTHING — and persisted (gameLodVegetationBaselinePref). A per-session capture is
        // WRONG and was a real bug: the value we wrote last session is still in the registry at
        // the next startup, so the mod captured its own output as "the game's value" and every
        // restart compounded it (live smoke: real 30 → captured 3 → ×10 landed back on 30, i.e.
        // no visible gain). Restored verbatim when the toggle goes off.
        private int gameLodVegetationOriginalPref = -1;
        private int gameLodVegetationLastWrittenPref = -1;

        private float nextGameLodApplyAt = 0f;
        private float nextGameLodNineCellWalkAt = 0f;

        // Heavy sections (furniture streaming + scene-chunk/HLOD distances) are DEFERRED until the
        // world has finished loading. Applying them mid-load makes the loading screen crawl: the
        // scene-chunk multiplier turns every load radius into ~mult² the area that must stream in
        // before the world is considered ready, and the furniture cap queues thousands of extra
        // asset loads. Deferring costs nothing visually — the extra content streams in behind the
        // player a few seconds later — and keeps load times close to stock.
        private const float GameLodWorldSettleSeconds = 8f;
        private const float GameLodWorldSettleFallbackSeconds = 45f;
        private string gameLodLastSceneName = string.Empty;
        private float gameLodSceneSeenAt = 0f;
        private float gameLodPlayerSeenAt = 0f;
        private bool gameLodHeavyApplyLogged = false;
        private int gameLodVegetationPrefAtSceneLoad = -1;

        // Loading-screen gate (the game's own signal, per user request "wait for the splash to go
        // away"). The LoadingOpenedEvent / LoadingClosedEvent tracking that used to live here is now
        // the shared world-ready gate (HeartopiaComplete.WorldReady.cs) — this feature just
        // subscribes. While the screen is up we keep the world completely stock — including
        // PC_LODBIAS, which TreeLoad reads in OnEnable: leaving OUR value there makes the scene
        // build every instance block at LOD0 during the load itself.
        private const string GameLodWorldReadyCallbackName = "GameLodApply";
        private bool gameLodLoadingHooksRegistered = false;

        // Pending one-shot work queued by UI handlers; executed inside the guarded tick.
        private bool gameLodFurnitureRevertPending = false;
        private bool gameLodForceLod0RevertPending = false;
        private bool gameLodBrgBiasRevertPending = false;
        private bool gameLodSignificanceRevertPending = false;
        private bool gameLodNineCellRevertPending = false;
        private bool gameLodShadowRevertPending = false;
        private bool gameLodVegetationRebakePending = false;
        private bool gameLodHlodRevertPending = false;
        private bool gameLodXdLodRevertPending = false;

        // Live status lines for the UGUI page (read by UguiGameLodContent).
        internal string gameLodFurnitureStatus = "";
        internal string gameLodBrgStatus = "";
        internal string gameLodVegetationStatus = "";
        internal string gameLodSignificanceStatus = "";
        internal string gameLodNineCellStatus = "";
        internal string gameLodShadowStatus = "";
        internal string gameLodHlodStatus = "";
        internal string gameLodXdLodStatus = "";

        // HLOD controllers live on the IL2CPP side (scene MonoBehaviours) — plain interop, no
        // AuraMono. Originals keyed by GetInstanceID (fresh ids after scene reload = fresh capture).
        private Il2CppSystem.Type gameLodHlodIl2CppType = null;
        private bool gameLodHlodIl2CppTypeResolved = false;
        private readonly Dictionary<int, Vector2> gameLodHlodOriginals = new Dictionary<int, Vector2>();

        // Base-scene chunk streamer (SceneLoader.SceneLoaderRoot, IL2CPP): serialized per-layer
        // configs with public loadDistance/unloadDistance/hlodDistances[] — the rocks/islands
        // low-poly→high-poly swap on approach. Baselines keyed by (root instanceID, config index).
        private struct GameLodSceneLoaderBaseline
        {
            public float Load;
            public float Unload;
            public float[] Hlods;
        }
        private Il2CppSystem.Type gameLodSceneLoaderIl2CppType = null;
        private bool gameLodSceneLoaderIl2CppTypeResolved = false;
        private readonly Dictionary<long, GameLodSceneLoaderBaseline> gameLodSceneLoaderBaselines = new Dictionary<long, GameLodSceneLoaderBaseline>();

        // XDLod: netIds of groups WE forced (game code ForceLOD(0)s some NPCs itself — those must
        // survive our revert untouched).
        private readonly HashSet<uint> gameLodXdLodForcedNetIds = new HashSet<uint>();
        private IntPtr gameLodXdLodManagerClass = IntPtr.Zero;
        private float nextGameLodXdLodWalkAt = 0f;
        private int gameLodXdLodWalkOffset = 0;
        internal int gameLodXdLodForcedCount = 0;
        private bool gameLodXdLodFirstWalkLogged = false;
        private const int GameLodXdLodWalkBudget = 300;

        // Shadow original captured once per apply-session (restored on revert).
        private bool gameLodShadowOriginalCaptured = false;
        private float gameLodShadowOriginal = 0f;

        // NineCell per-netId memory: orig = the game's own range when first seen, lastTarget = the
        // range we last forced. A live range differing from lastTarget means the game re-created the
        // component (world reload / respawn) → re-capture orig from it.
        private struct GameLodNineCellEntry
        {
            public short Orig;
            public short LastTarget;
        }
        private readonly Dictionary<uint, GameLodNineCellEntry> gameLodNineCellRanges = new Dictionary<uint, GameLodNineCellEntry>();
        internal int gameLodNineCellBoostedCount = 0;
        private int gameLodNineCellWalkOffset = 0;

        // Cached mono class pointers (image lifetime — safe to keep raw).
        private IntPtr gameLodLevelEntityComponentClass = IntPtr.Zero;
        private IntPtr gameLodLoaderManagerClass = IntPtr.Zero;
        private IntPtr gameLodSignificanceManagerClass = IntPtr.Zero;
        private IntPtr gameLodTreeLoadClass = IntPtr.Zero;
        private IntPtr gameLodHomeLoadClass = IntPtr.Zero;
        private IntPtr gameLodBrgManagerClass = IntPtr.Zero;
        private IntPtr gameLodLayerCullingClass = IntPtr.Zero;
        private IntPtr gameLodRenderingSettingsClass = IntPtr.Zero;
        private IntPtr gameLodPhotoFrameClass = IntPtr.Zero;

        // PhotoFrameComponent.PhotoLodDis ships as { 100f, 100f, 100f } — the value the revert
        // path restores. See TryGameLodSetPhotoRequestDistance for why this matters.
        private const float GameLodStockPhotoLodDistance = 100f;

        private FeatureBreakerState gameLodApplyBreaker;
        private FeatureBreakerState gameLodNineCellBreaker;

        private static readonly string[] GameLodEngineWrapperImages = { "EngineWrapper", "EngineWrapper.dll" };

        // ----------------------------------------------------------------------------------------
        // Verbose resolve/apply logging — user-controllable from Settings → Logging → "Game LOD"
        // (session-only, like every MasterLog* flag). Off by default now that the feature is proven
        // live. Every line goes through GameLodLogOnce, which dedups identical messages, so
        // steady-state re-applies stay silent even with the flag on; a line reappears only when its
        // content (status/counts) actually changes. Turning it on mid-session also re-runs the
        // read-only resolve probe, so the "did every type resolve?" report is reproducible on demand.
        // ----------------------------------------------------------------------------------------
        internal static bool MasterLogGameLod = false;
        private readonly HashSet<string> gameLodLoggedLines = new HashSet<string>(StringComparer.Ordinal);
        private bool gameLodResolveProbeAllOk = false;
        private bool gameLodNineCellFirstWalkLogged = false;
        private bool gameLodLastLogFlagSeen = false;

        private void GameLodLogOnce(string message)
        {
            if (!MasterLogGameLod || string.IsNullOrEmpty(message))
            {
                return;
            }
            if (this.gameLodLoggedLines.Count > 512)
            {
                this.gameLodLoggedLines.Clear();
            }
            if (this.gameLodLoggedLines.Add(message))
            {
                ModLogger.Msg("[GameLod] " + message);
            }
        }

        private static string GameLodPtr(IntPtr ptr)
        {
            return ptr == IntPtr.Zero ? "null" : ("0x" + ptr.ToString("X"));
        }

        private bool GameLodProbe(string label, bool ok, string detail = null)
        {
            this.GameLodLogOnce("probe " + label + ": "
                + (ok ? ("ok" + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")")) : "MISS"));
            return ok;
        }

        private void GameLodLogAuraGate()
        {
            string gate;
            if (!this.EnsureAuraMonoApiReady())
            {
                gate = "mono api not ready";
            }
            else if (!this.AttachAuraMonoThread())
            {
                gate = "mono thread attach failed";
            }
            else if (!AuraMonoStaticFieldReadsAllowed())
            {
                gate = "static-field gate closed (pre-login / world not proven live)";
            }
            else
            {
                gate = "runtime invoke unavailable";
            }
            this.GameLodLogOnce("waiting: " + gate);
        }

        // Read-only metadata sweep over every class/method/field this feature touches. Runs on the
        // apply cadence until everything resolves (many game images only load after entering a
        // town), logging each item once per state — the log answers "did all types resolve on this
        // build?" without enabling any toggle. Doubles as cache warm-up for the class fields.
        private void GameLodRunResolveProbe()
        {
            if (!MasterLogGameLod || this.gameLodResolveProbeAllOk)
            {
                return;
            }

            bool allOk = true;

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }
            allOk &= this.GameLodProbe("Managers class", managersClass != IntPtr.Zero, GameLodPtr(managersClass));
            allOk &= this.GameLodProbe("Managers._serviceDic field",
                managersClass != IntPtr.Zero && this.FindAuraMonoFieldOnHierarchy(managersClass, "_serviceDic") != IntPtr.Zero);

            allOk &= this.GameLodProbe("IRenderingSystem interface class",
                this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem") != IntPtr.Zero);
            allOk &= this.GameLodProbe("IConfigManager interface class",
                this.FindAuraMonoClassByFullName("XDTDataAndProtocol.Config.IConfigManager") != IntPtr.Zero);

            IntPtr loaderClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.LoadManager.LoaderManager");
            allOk &= this.GameLodProbe("LoaderManager class", loaderClass != IntPtr.Zero, GameLodPtr(loaderClass));
            allOk &= this.GameLodProbe("LoaderManager.SetParam(7)",
                loaderClass != IntPtr.Zero && this.FindAuraMonoMethodOnHierarchy(loaderClass, "SetParam", 7) != IntPtr.Zero);

            IntPtr renderLoadConfigClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.Config.RenderLoadConfig");
            allOk &= this.GameLodProbe("RenderLoadConfig class", renderLoadConfigClass != IntPtr.Zero, GameLodPtr(renderLoadConfigClass));
            if (renderLoadConfigClass != IntPtr.Zero)
            {
                string missingGetters = "";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetLoadDis", 1) == IntPtr.Zero) missingGetters += " GetLoadDis";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetMaxNum", 0) == IntPtr.Zero) missingGetters += " GetMaxNum";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetLoadNum", 0) == IntPtr.Zero) missingGetters += " GetLoadNum";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetUnloadNum", 0) == IntPtr.Zero) missingGetters += " GetUnloadNum";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetStructLoadNum", 0) == IntPtr.Zero) missingGetters += " GetStructLoadNum";
                if (this.FindAuraMonoMethodOnHierarchy(renderLoadConfigClass, "GetLoadMeshDis", 0) == IntPtr.Zero) missingGetters += " GetLoadMeshDis";
                allOk &= this.GameLodProbe("RenderLoadConfig getters", missingGetters.Length == 0,
                    missingGetters.Length == 0 ? "all 6" : ("missing:" + missingGetters));
            }
            else
            {
                allOk = false;
            }

            IntPtr layerCullingClass = this.GameLodEngineWrapperClass(ref this.gameLodLayerCullingClass, string.Empty, "LayerDistanceCulling");
            allOk &= this.GameLodProbe("LayerDistanceCulling class", layerCullingClass != IntPtr.Zero, GameLodPtr(layerCullingClass));
            allOk &= this.GameLodProbe("LayerDistanceCulling.get_Instance + Behaviour.set_enabled",
                layerCullingClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(layerCullingClass, "get_Instance", 0) != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(layerCullingClass, "set_enabled", 1) != IntPtr.Zero);

            IntPtr brgClass = this.GameLodEngineWrapperClass(ref this.gameLodBrgManagerClass,
                "ScriptsRefactory.BaseService.RenderSystem.Brg", "BrgManager");
            if (brgClass == IntPtr.Zero)
            {
                brgClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.BaseService.RenderSystem.Brg.BrgManager");
                this.gameLodBrgManagerClass = brgClass;
            }
            allOk &= this.GameLodProbe("BrgManager class", brgClass != IntPtr.Zero, GameLodPtr(brgClass));
            allOk &= this.GameLodProbe("BrgManager.set_ForeceLOD0",
                brgClass != IntPtr.Zero && this.FindAuraMonoMethodOnHierarchy(brgClass, "set_ForeceLOD0", 1) != IntPtr.Zero);

            IntPtr areaPriorityClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.RenderPriorityManager.AreaPriorityManager");
            allOk &= this.GameLodProbe("AreaPriorityManager class", areaPriorityClass != IntPtr.Zero, GameLodPtr(areaPriorityClass));
            allOk &= this.GameLodProbe("AreaPriorityManager.UpdateGlobalLodBias(1)",
                areaPriorityClass != IntPtr.Zero && this.FindAuraMonoMethodOnHierarchy(areaPriorityClass, "UpdateGlobalLodBias", 1) != IntPtr.Zero);

            if (this.gameLodTreeLoadClass == IntPtr.Zero)
            {
                this.gameLodTreeLoadClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.InstanceBlock.TreeLoad");
            }
            allOk &= this.GameLodProbe("TreeLoad class", this.gameLodTreeLoadClass != IntPtr.Zero, GameLodPtr(this.gameLodTreeLoadClass));
            allOk &= this.GameLodProbe("TreeLoad.SetAllDynamicInstanceBlockEnable(1)",
                this.gameLodTreeLoadClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(this.gameLodTreeLoadClass, "SetAllDynamicInstanceBlockEnable", 1) != IntPtr.Zero);

            if (this.gameLodHomeLoadClass == IntPtr.Zero)
            {
                this.gameLodHomeLoadClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.InstanceBlock.HomeLoad");
            }
            allOk &= this.GameLodProbe("HomeLoad class + get_Inst",
                this.gameLodHomeLoadClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(this.gameLodHomeLoadClass, "get_Inst", 0) != IntPtr.Zero,
                GameLodPtr(this.gameLodHomeLoadClass));

            if (this.gameLodSignificanceManagerClass == IntPtr.Zero)
            {
                this.gameLodSignificanceManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.BaseSystem.SignificanceManager.SignificanceManager");
            }
            allOk &= this.GameLodProbe("SignificanceManager class", this.gameLodSignificanceManagerClass != IntPtr.Zero,
                GameLodPtr(this.gameLodSignificanceManagerClass));
            allOk &= this.GameLodProbe("SignificanceManager.get_Instance + set_Enable",
                this.gameLodSignificanceManagerClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(this.gameLodSignificanceManagerClass, "get_Instance", 0) != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(this.gameLodSignificanceManagerClass, "set_Enable", 1) != IntPtr.Zero);

            if (this.gameLodLevelEntityComponentClass == IntPtr.Zero)
            {
                this.gameLodLevelEntityComponentClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.EntityView.LevelEntityComponent");
            }
            allOk &= this.GameLodProbe("LevelEntityComponent class", this.gameLodLevelEntityComponentClass != IntPtr.Zero,
                GameLodPtr(this.gameLodLevelEntityComponentClass));
            if (this.gameLodLevelEntityComponentClass != IntPtr.Zero)
            {
                allOk &= this.GameLodProbe("LevelEntityComponent.SetForceNineCell(1)",
                    this.FindAuraMonoMethodOnHierarchy(this.gameLodLevelEntityComponentClass, "SetForceNineCell", 1) != IntPtr.Zero);
                allOk &= this.GameLodProbe("LevelEntityComponent._nineCellRange field",
                    this.FindAuraMonoFieldOnHierarchy(this.gameLodLevelEntityComponentClass, "_nineCellRange") != IntPtr.Zero);
            }
            else
            {
                allOk = false;
            }

            bool infraOk = this.TryAuraMonoEntitiesGetComponentsInfraReady(out string infraStatus);
            allOk &= this.GameLodProbe("Entities.GetComponents<T> infra", infraOk, infraOk ? null : infraStatus);

            IntPtr renderingSettingsClass = this.GameLodEngineWrapperClass(ref this.gameLodRenderingSettingsClass, string.Empty, "RenderingSettings");
            allOk &= this.GameLodProbe("RenderingSettings class + get_Instance + GetCurrentURPAsset",
                renderingSettingsClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(renderingSettingsClass, "get_Instance", 0) != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(renderingSettingsClass, "GetCurrentURPAsset", 0) != IntPtr.Zero,
                GameLodPtr(renderingSettingsClass));

            IntPtr urpAssetClass = this.FindAuraMonoClassInImages("UnityEngine.Rendering.Universal",
                "UniversalRenderPipelineAsset", GameLodEngineWrapperImages);
            allOk &= this.GameLodProbe("UniversalRenderPipelineAsset.shadowDistance get/set",
                urpAssetClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(urpAssetClass, "get_shadowDistance", 0) != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(urpAssetClass, "set_shadowDistance", 1) != IntPtr.Zero);

            IntPtr coreImage = this.FindAuraMonoImage(new[] { "mscorlib", "mscorlib.dll", "System.Private.CoreLib", "System.Private.CoreLib.dll" });
            allOk &= this.GameLodProbe("mscorlib System.Int32 (int[] builder)",
                coreImage != IntPtr.Zero && auraMonoClassFromName != null
                && auraMonoClassFromName(coreImage, "System", "Int32") != IntPtr.Zero);

            if (this.gameLodXdLodManagerClass == IntPtr.Zero)
            {
                this.gameLodXdLodManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.BaseSystem.XDLodManager.XDLodManager");
            }
            allOk &= this.GameLodProbe("XDLodManager class + xdlodgroupmap field",
                this.gameLodXdLodManagerClass != IntPtr.Zero
                && this.FindAuraMonoFieldOnHierarchy(this.gameLodXdLodManagerClass, "xdlodgroupmap") != IntPtr.Zero,
                GameLodPtr(this.gameLodXdLodManagerClass));

            IntPtr xdLodGroupClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.XDLodGroupComponent");
            allOk &= this.GameLodProbe("XDLodGroupComponent.ForceLOD(1) + get_IsLODForced",
                xdLodGroupClass != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(xdLodGroupClass, "ForceLOD", 1) != IntPtr.Zero
                && this.FindAuraMonoMethodOnHierarchy(xdLodGroupClass, "get_IsLODForced", 0) != IntPtr.Zero,
                GameLodPtr(xdLodGroupClass));

            // IL2CPP side (interop, not AuraMono): the landscape HLOD controller type + fields.
            bool hlodTypeOk = false;
            bool hlodFieldsOk = false;
            try
            {
                Il2CppSystem.Type hlodType = Il2CppSystem.Type.GetType("Unity.HLODSystem.Streaming.HLODController, HLOD")
                    ?? Il2CppSystem.Type.GetType("Unity.HLODSystem.Streaming.HLODController");
                hlodTypeOk = hlodType != null;
                hlodFieldsOk = hlodTypeOk
                    && hlodType.GetField("hlod1LoadAndVisDistance") != null
                    && hlodType.GetField("hlod2LoadAndVisDistance") != null;
            }
            catch { }
            allOk &= this.GameLodProbe("HLODController il2cpp type", hlodTypeOk);
            allOk &= this.GameLodProbe("HLODController hlod1/2LoadAndVisDistance fields", hlodFieldsOk);

            bool sceneLoaderOk = false;
            try
            {
                Il2CppSystem.Type slType = Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot, SceneLoader")
                    ?? Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot");
                sceneLoaderOk = slType != null && slType.GetField("configs") != null;
            }
            catch { }
            allOk &= this.GameLodProbe("SceneLoaderRoot il2cpp type + configs field", sceneLoaderOk);

            if (allOk)
            {
                this.gameLodResolveProbeAllOk = true;
                this.GameLodLogOnce("resolve probe: ALL OK — every class/method/field this feature uses resolved on this build");
            }
        }

        // ----------------------------------------------------------------------------------------
        // OnUpdate tick
        // ----------------------------------------------------------------------------------------
        private void ProcessGameLodFeatureOnUpdate()
        {
            // Flipping the Settings → Logging toggle ON re-arms diagnostics: the dedup set and the
            // "already reported" latches are cleared so the resolve probe and the first-walk lines
            // are produced again on demand, instead of being swallowed as duplicates from earlier
            // in the session. (Checked before the early-out so it works while everything is off.)
            if (MasterLogGameLod != this.gameLodLastLogFlagSeen)
            {
                this.gameLodLastLogFlagSeen = MasterLogGameLod;
                if (MasterLogGameLod)
                {
                    this.gameLodLoggedLines.Clear();
                    this.gameLodResolveProbeAllOk = false;
                    this.gameLodNineCellFirstWalkLogged = false;
                    this.gameLodXdLodFirstWalkLogged = false;
                    ModLogger.Msg("[GameLod] verbose logging enabled — re-running the resolve probe");
                }
            }

            bool anyEnabled = this.gameLodFurnitureEnabled || this.gameLodForceLod0Enabled
                || this.gameLodBrgBiasEnabled || this.gameLodSignificanceOffEnabled
                || this.gameLodNineCellEnabled || this.gameLodShadowEnabled
                || this.gameLodHlodEnabled || this.gameLodXdLodEnabled;
            bool anyPending = this.gameLodFurnitureRevertPending || this.gameLodForceLod0RevertPending
                || this.gameLodBrgBiasRevertPending || this.gameLodSignificanceRevertPending
                || this.gameLodNineCellRevertPending || this.gameLodShadowRevertPending
                || this.gameLodVegetationRebakePending || this.gameLodHlodRevertPending
                || this.gameLodXdLodRevertPending;
            // With verbose logging on, the tick also runs idle just to drive the resolve probe
            // (read-only metadata sweep) until every type is proven resolved on this build.
            bool probeWanted = MasterLogGameLod && !this.gameLodResolveProbeAllOk;
            if (!anyEnabled && !anyPending && !probeWanted)
            {
                return;
            }

            float now = Time.unscaledTime;
            this.EnsureGameLodLoadingHooks(now);
            if (this.gameLodApplyBreaker.ShouldRun(now) && (now >= this.nextGameLodApplyAt || anyPending))
            {
                this.nextGameLodApplyAt = now + GameLodApplyIntervalSeconds;
                try
                {
                    // HLOD is IL2CPP-side (plain interop) — no AuraMono gate needed.
                    this.GameLodTickHlod();
                    if (this.IsGameLodAuraReady())
                    {
                        this.GameLodRunResolveProbe();
                        this.GameLodTickApplySections();
                    }
                    else if (probeWanted || anyEnabled || anyPending)
                    {
                        this.GameLodLogAuraGate();
                    }
                    this.gameLodApplyBreaker.Success();
                }
                catch (Exception ex)
                {
                    this.gameLodApplyBreaker.Failure("GameLod", ex, now);
                }
            }

            if ((this.gameLodNineCellEnabled || this.gameLodNineCellRevertPending)
                && this.gameLodNineCellBreaker.ShouldRun(now) && now >= this.nextGameLodNineCellWalkAt)
            {
                this.nextGameLodNineCellWalkAt = now + GameLodNineCellWalkIntervalSeconds;
                try
                {
                    // Boosting entity ranges mid-load activates ~mult² the entities, each pulling
                    // its own prefab — reverts still run so switching off is always immediate.
                    if (this.IsGameLodAuraReady()
                        && (this.gameLodNineCellRevertPending || this.IsGameLodHeavyApplyAllowed()))
                    {
                        this.GameLodTickNineCell();
                    }
                    else if (this.gameLodNineCellEnabled)
                    {
                        this.gameLodNineCellStatus = this.L("Waiting for the world to finish loading…");
                    }
                    this.gameLodNineCellBreaker.Success();
                }
                catch (Exception ex)
                {
                    this.gameLodNineCellBreaker.Failure("GameLod NineCell", ex, now);
                }
            }

            if ((this.gameLodXdLodEnabled || this.gameLodXdLodRevertPending)
                && this.gameLodNineCellBreaker.ShouldRun(now) && now >= this.nextGameLodXdLodWalkAt)
            {
                this.nextGameLodXdLodWalkAt = now + GameLodNineCellWalkIntervalSeconds;
                try
                {
                    // ForceLOD(0) makes each group load its high-poly mesh (XDLodGroupComponent
                    // .HandleResourceLoading) — pure extra asset traffic during a world load.
                    if (this.IsGameLodAuraReady()
                        && (this.gameLodXdLodRevertPending || this.IsGameLodHeavyApplyAllowed()))
                    {
                        this.GameLodTickXdLod();
                    }
                    else if (this.gameLodXdLodEnabled)
                    {
                        this.gameLodXdLodStatus = this.L("Waiting for the world to finish loading…");
                    }
                    this.gameLodNineCellBreaker.Success();
                }
                catch (Exception ex)
                {
                    this.gameLodNineCellBreaker.Failure("GameLod XdLod", ex, now);
                }
            }
        }

        // Subscribe to the shared world-ready gate. One-shot: the gate owns the event registration
        // (and its retry), so there is nothing to re-attempt here.
        private void EnsureGameLodLoadingHooks(float now)
        {
            if (this.gameLodLoadingHooksRegistered)
            {
                return;
            }

            this.gameLodLoadingHooksRegistered = true;
            this.RegisterWorldLoadingStartedCallback(this.OnGameLodLoadingOpened);
            this.RegisterWorldReadyCallback(GameLodWorldReadyCallbackName, this.OnGameLodWorldReady);
        }

        // World-ready: apply on the very next tick instead of waiting out the apply interval.
        // Always "done" — the per-section gates in GameLodTickApplySections do the real work.
        private bool OnGameLodWorldReady()
        {
            this.nextGameLodApplyAt = 0f;
            ModLogger.Msg("[GameLod] world ready — heavy settings apply once the world settles.");
            return true;
        }

        private void OnGameLodLoadingOpened()
        {
            this.gameLodHeavyApplyLogged = false;
            // Hand the game back its own PC_LODBIAS for the whole load: TreeLoad reads the key in
            // OnEnable, so our raised value would otherwise make the scene build every instance
            // block at max detail while the loading screen is still up.
            if (this.gameLodVegetationEnabled && !this.gameLodVegetationApplyDuringLoad)
            {
                try
                {
                    PlayerPrefs.SetInt("PC_LODBIAS", this.GameLodVegetationOriginalPref());
                    PlayerPrefs.Save();
                    this.gameLodVegetationLastWrittenPref = this.GameLodVegetationOriginalPref();
                }
                catch { }
            }
            ModLogger.Msg("[GameLod] loading screen opened — stock settings for the load"
                + (this.gameLodVegetationApplyDuringLoad ? " (terrain detail kept raised by request)" : ""));
        }

        // The game's OWN "world is ready" signal: LoaderManager.IsRun (public static bool). The
        // scene pipeline sets it false in XDTwonScene_ShaderWarmup.OnInitialize and true only when
        // the shader-warmup stage reports done — i.e. it is exactly the post-warmup gate the game
        // uses before it lets its own furniture streaming run. Returns false when unreadable, so a
        // failed read never lets heavy work slip into the loading screen.
        private unsafe bool TryGameLodIsLoaderManagerRunning(out bool isRun)
        {
            isRun = false;
            if (!AuraMonoStaticFieldReadsAllowed() || auraMonoClassVtable == null
                || auraMonoFieldStaticGetValue == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            if (this.gameLodLoaderManagerClass == IntPtr.Zero)
            {
                this.gameLodLoaderManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.BaseSystem.LoadManager.LoaderManager");
            }
            if (this.gameLodLoaderManagerClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr field = this.FindAuraMonoFieldOnHierarchy(this.gameLodLoaderManagerClass, "IsRun");
            if (field == IntPtr.Zero)
            {
                return false;
            }

            // FindAuraMonoFieldOnHierarchy can return a BASE class's field — it must then be read off
            // that class's vtable, never this one's (TryGetAuraMonoStaticFieldVtable; the mismatch is
            // an uncatchable AV).
            if (!this.TryGetAuraMonoStaticFieldVtable(field, out IntPtr vtable))
            {
                return false;
            }

            byte raw = 0;
            auraMonoFieldStaticGetValue(vtable, field, (IntPtr)(&raw));
            isRun = raw != 0;
            return true;
        }

        // True once the world is up and has had a moment to settle. Scene changes reset the timer
        // AND drop the per-instance baselines (a new scene brings new objects with fresh ids).
        private bool IsGameLodHeavyApplyAllowed()
        {
            float now = Time.unscaledTime;
            string scene = string.Empty;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty; } catch { }

            if (!string.Equals(scene, this.gameLodLastSceneName, StringComparison.Ordinal))
            {
                this.gameLodLastSceneName = scene;
                this.gameLodSceneSeenAt = now;
                this.gameLodPlayerSeenAt = 0f;
                this.gameLodHeavyApplyLogged = false;
                this.gameLodSceneLoaderBaselines.Clear();
                this.gameLodHlodOriginals.Clear();
                // A rebake must never run while loading (it re-creates every instance-block
                // material → shader-variant compilation). Drop whatever was queued and instead
                // record the PC_LODBIAS the scene is actually building with: the game's own
                // settings code rewrites that key during startup (seen live: our 300 was reset
                // to 30), so the scene can legitimately come up with the wrong value and then a
                // rebake IS needed — just later, once the world has settled.
                this.gameLodVegetationRebakePending = false;
                try { this.gameLodVegetationPrefAtSceneLoad = PlayerPrefs.GetInt("PC_LODBIAS", GameLodVegetationPrefDefault); }
                catch { this.gameLodVegetationPrefAtSceneLoad = -1; }
            }

            if (this.gameLodPlayerSeenAt <= 0f)
            {
                try
                {
                    if (this.TryGetLocalPlayerPosition(out Vector3 playerPos) && playerPos != Vector3.zero)
                    {
                        this.gameLodPlayerSeenAt = now;
                    }
                }
                catch { }
            }

            // Gate, strongest signal first:
            //  1. the shared world-ready gate — loading screen GONE (LoadingClosedEvent) + grace,
            //     with its own player-present fallback for builds where the events never arrive,
            //  2. the game's post-warmup flag LoaderManager.IsRun must be true,
            //  3. a local player must exist and have been around for a moment.
            bool loaderRunning = true;
            if (this.TryGameLodIsLoaderManagerRunning(out bool isRun))
            {
                loaderRunning = isRun;
            }

            bool settled = loaderRunning && this.IsWorldReady
                && ((this.gameLodPlayerSeenAt > 0f && now - this.gameLodPlayerSeenAt >= GameLodWorldSettleSeconds)
                    || (now - this.gameLodSceneSeenAt >= GameLodWorldSettleFallbackSeconds));
            if (settled && !this.gameLodHeavyApplyLogged)
            {
                this.gameLodHeavyApplyLogged = true;
                ModLogger.Msg("[GameLod] world settled in '" + this.gameLodLastSceneName
                    + "' — applying deferred heavy sections (furniture streaming, scene chunks)");
            }
            return settled;
        }

        private bool IsGameLodAuraReady()
        {
            return this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread()
                && AuraMonoStaticFieldReadsAllowed() && auraMonoRuntimeInvoke != null
                && this.auraMonoRootDomain != IntPtr.Zero;
        }

        private void GameLodTickApplySections()
        {
            // EVERY apply path waits for the world; only reverts are unconditional. Each of these
            // sections triggers real asset loading — Significance off makes every NPC load full
            // detail, NineCell ×N activates ~N² the entities (each pulling its prefab), XDLod
            // ForceLOD(0) loads the high-poly mesh of every group, and forced BRG LOD0 renders the
            // lot on the first frames (shader-variant compilation). Running them while the world is
            // still coming up is what kept load times long even after the first deferral.
            bool heavyOk = this.IsGameLodHeavyApplyAllowed();

            // Furniture / homeland streaming.
            if (this.gameLodFurnitureRevertPending)
            {
                if (this.TryGameLodFurnitureRevert(out string revertStatus))
                {
                    this.gameLodFurnitureRevertPending = false;
                    this.gameLodFurnitureStatus = this.L("Reverted to game defaults.");
                }
                else
                {
                    this.gameLodFurnitureStatus = revertStatus;
                }
                this.GameLodLogOnce("furniture revert: " + this.gameLodFurnitureStatus);
            }
            else if (this.gameLodFurnitureEnabled)
            {
                if (!heavyOk)
                {
                    this.gameLodFurnitureStatus = this.L("Waiting for the world to finish loading…");
                }
                else
                {
                    this.gameLodFurnitureStatus = this.TryGameLodFurnitureApply(out string applyStatus)
                        ? this.LF("Applied: {0} objects, {1} m, mesh {2} m.", this.gameLodFurnitureMaxObjects,
                            this.gameLodFurnitureDistance, this.gameLodFurnitureMeshDistance)
                        : applyStatus;
                    this.GameLodLogOnce("furniture apply: " + this.gameLodFurnitureStatus);
                }
            }

            // BRG force LOD0.
            if (this.gameLodForceLod0RevertPending)
            {
                if (this.TryGameLodSetForceLod0(false, out string lod0RevertStatus))
                {
                    this.gameLodForceLod0RevertPending = false;
                    this.GameLodLogOnce("force-lod0 revert: ok");
                }
                else
                {
                    this.GameLodLogOnce("force-lod0 revert: " + lod0RevertStatus);
                }
            }
            else if (this.gameLodForceLod0Enabled && heavyOk)
            {
                this.TryGameLodSetForceLod0(true, out string lod0Status);
                this.gameLodBrgStatus = lod0Status;
                this.GameLodLogOnce("force-lod0 apply: " + lod0Status);
            }

            // BRG global bias (re-asserted: the low-FPS governor writes 0.2 over it).
            if (this.gameLodBrgBiasRevertPending)
            {
                if (this.TryGameLodApplyBrgBias(1f, out string biasRevertStatus))
                {
                    this.gameLodBrgBiasRevertPending = false;
                    this.GameLodLogOnce("brg-bias revert: ok");
                }
                else
                {
                    this.GameLodLogOnce("brg-bias revert: " + biasRevertStatus);
                }
            }
            else if (this.gameLodBrgBiasEnabled && heavyOk)
            {
                this.TryGameLodApplyBrgBias(this.gameLodBrgBias, out string biasStatus);
                if (!this.gameLodForceLod0Enabled)
                {
                    this.gameLodBrgStatus = biasStatus;
                }
                this.GameLodLogOnce("brg-bias apply: " + biasStatus);
            }

            // Characters (Significance).
            if (this.gameLodSignificanceRevertPending)
            {
                if (this.TryGameLodSetSignificanceEnabled(true, out string sigRevertStatus))
                {
                    this.gameLodSignificanceRevertPending = false;
                    this.gameLodSignificanceStatus = this.L("Reverted to game defaults.");
                    this.GameLodLogOnce("significance revert: ok");
                }
                else
                {
                    this.GameLodLogOnce("significance revert: " + sigRevertStatus);
                }
            }
            else if (this.gameLodSignificanceOffEnabled && !heavyOk)
            {
                this.gameLodSignificanceStatus = this.L("Waiting for the world to finish loading…");
            }
            else if (this.gameLodSignificanceOffEnabled)
            {
                bool sigOk = this.TryGameLodSetSignificanceEnabled(false, out string sigStatus);
                this.gameLodSignificanceStatus = sigOk
                    ? this.L("Distance quality reduction disabled (full detail).")
                    : sigStatus;
                this.GameLodLogOnce("significance apply: " + (sigOk ? "ok (Enable=false)" : sigStatus));
            }

            // Shadows.
            if (this.gameLodShadowRevertPending)
            {
                if (this.TryGameLodApplyShadowDistance(true, out string shadowRevertStatus))
                {
                    this.gameLodShadowRevertPending = false;
                    this.gameLodShadowOriginalCaptured = false;
                    this.gameLodShadowStatus = this.L("Reverted to game defaults.");
                    this.GameLodLogOnce("shadow revert: ok");
                }
                else
                {
                    this.GameLodLogOnce("shadow revert: " + shadowRevertStatus);
                }
            }
            else if (this.gameLodShadowEnabled)
            {
                bool shadowOk = this.TryGameLodApplyShadowDistance(false, out string shadowStatus);
                this.gameLodShadowStatus = shadowOk
                    ? this.LF("Shadow distance: {0:F0} m.", this.gameLodShadowDistance)
                    : shadowStatus;
                this.GameLodLogOnce("shadow apply: " + (shadowOk
                    ? ("ok (" + this.gameLodShadowDistance.ToString("F0") + " m)") : shadowStatus));
            }

            // Vegetation: keep our PC_LODBIAS asserted, then run any queued rebake — but never
            // while the world is still loading (the rebake re-creates every instance-block
            // material, which is exactly the kind of work that stretches a loading screen).
            this.GameLodReassertVegetationPref(heavyOk);

            if (this.gameLodVegetationRebakePending && !heavyOk)
            {
                this.gameLodVegetationStatus = this.L("Waiting for the world to finish loading…");
            }
            else if (this.gameLodVegetationRebakePending)
            {
                if (this.TryGameLodVegetationRebake(out string rebakeStatus))
                {
                    this.gameLodVegetationRebakePending = false;
                    this.gameLodVegetationStatus = this.LF("Rebaked: PC_LODBIAS {0} → {1} (x{2:0.#} LOD distance).",
                        this.GameLodVegetationOriginalPref(), this.GameLodEffectiveVegetationPref(),
                        this.GameLodVegetationEffectiveMult());
                    this.GameLodLogOnce("vegetation rebake: ok (PC_LODBIAS " + this.GameLodVegetationOriginalPref()
                        + " -> " + this.GameLodEffectiveVegetationPref() + ", x"
                        + this.GameLodVegetationEffectiveMult().ToString("0.#") + " distance)");
                }
                else
                {
                    this.gameLodVegetationStatus = rebakeStatus;
                    this.GameLodLogOnce("vegetation rebake: " + rebakeStatus);
                }
            }
        }

        // The game's stored PC_LODBIAS, captured on first read (default 10 = the value TreeLoad
        // itself falls back to). This is the baseline every multiplier is expressed against.
        internal int GameLodVegetationOriginalPref()
        {
            if (this.gameLodVegetationOriginalPref <= 0)
            {
                this.gameLodVegetationOriginalPref = this.gameLodVegetationBaselinePref > 0
                    ? this.gameLodVegetationBaselinePref
                    : GameLodVegetationPrefDefault;
            }
            return this.gameLodVegetationOriginalPref;
        }

        // Called ONCE per config load, before any write this session: whatever sits in the
        // registry right now is the game's own value. Persisted so later sessions never re-capture
        // our own output (the compounding bug above).
        private void GameLodCaptureVegetationBaseline()
        {
            if (this.gameLodVegetationBaselinePref > GameLodVegetationMaxSaneBaseline)
            {
                ModLogger.Msg("[GameLod] vegetation: stored baseline " + this.gameLodVegetationBaselinePref
                    + " is not a game value (mod output adopted by mistake) — resetting to the engine default "
                    + GameLodVegetationPrefDefault);
                this.gameLodVegetationBaselinePref = 0;
            }

            if (this.gameLodVegetationBaselinePref > 0)
            {
                this.gameLodVegetationOriginalPref = this.gameLodVegetationBaselinePref;
                this.GameLodLogOnce("vegetation: PC_LODBIAS baseline (persisted) = " + this.gameLodVegetationBaselinePref
                    + "; higher = high-poly held further, 10 = engine default");
                return;
            }

            int stored = GameLodVegetationPrefDefault;
            try { stored = PlayerPrefs.GetInt("PC_LODBIAS", GameLodVegetationPrefDefault); } catch { }
            if (stored > GameLodVegetationMaxSaneBaseline)
            {
                // Registry still holds a raised value from a previous session — not a game value.
                stored = GameLodVegetationPrefDefault;
            }
            this.gameLodVegetationBaselinePref = Mathf.Clamp(stored, 1, GameLodVegetationMaxSaneBaseline);
            this.gameLodVegetationOriginalPref = this.gameLodVegetationBaselinePref;
            this.GameLodLogOnce("vegetation: PC_LODBIAS baseline captured from game = "
                + this.gameLodVegetationBaselinePref + " (persisted; restored when the toggle goes off)");
        }

        // Repair hatch, kept but no longer reachable from the UI: the "adopt current" button was
        // removed because pressing it while our raised value was live re-adopted the mod's own
        // output as "the game's value" (live log: baseline drifted 30 → 300 → 2000, at which point
        // the target clamped and the whole setting silently did nothing). Refuses anything outside
        // the sane game range for the same reason.
        internal void ResetGameLodVegetationBaseline()
        {
            int stored = GameLodVegetationPrefDefault;
            try { stored = PlayerPrefs.GetInt("PC_LODBIAS", GameLodVegetationPrefDefault); } catch { }
            if (stored > GameLodVegetationMaxSaneBaseline)
            {
                ModLogger.Msg("[GameLod] vegetation: refusing to adopt PC_LODBIAS " + stored
                    + " as baseline (that is the mod's own raised value, not a game setting)");
                return;
            }
            this.gameLodVegetationBaselinePref = Mathf.Clamp(stored, 1, GameLodVegetationMaxSaneBaseline);
            this.gameLodVegetationOriginalPref = this.gameLodVegetationBaselinePref;
            this.gameLodVegetationLastWrittenPref = -1;
            ModLogger.Msg("[GameLod] vegetation: baseline RESET to live PC_LODBIAS = " + this.gameLodVegetationBaselinePref);
            try { this.SaveKeybinds(false); } catch { }
        }

        // Direction (proven on the live build, 2026-07-25): PC_LODBIAS is a QUALITY factor, so a
        // HIGHER value holds the high-poly mesh further out.
        //   TreeLoad: num = PC_LODBIAS / 10; lodThreshold[k] /= num
        // The thresholds behave like Unity's screenRelativeTransitionHeight — BIGGER = swap sooner
        // (closer). TreeLoad.ToQualityBiasArray confirms it: the LOW-quality preset MULTIPLIES the
        // thresholds. So bigger num ⇒ smaller thresholds ⇒ swap later ⇒ what we want.
        // The first attempt divided (30 → 3) and the swap moved CLOSER, which is what proved it.
        private int GameLodEffectiveVegetationPref()
        {
            return this.gameLodVegetationEnabled
                ? Mathf.Clamp(this.gameLodVegetationTargetPref, 10, 1000)
                : this.GameLodVegetationOriginalPref();
        }

        // UI mirror of GameLodEffectiveVegetationPref that ignores the enabled gate, so the label
        // can preview what the slider WOULD write while the toggle is still off.
        internal int GameLodEffectiveVegetationPrefForUi()
        {
            return Mathf.Clamp(this.gameLodVegetationTargetPref, 10, 1000);
        }

        // Actual distance factor the written pref buys vs the game's own value (the label shows
        // this, so a clamped-at-1 pref reports the truth instead of the requested multiplier).
        internal float GameLodVegetationEffectiveMult()
        {
            int original = this.GameLodVegetationOriginalPref();
            int effective = this.GameLodEffectiveVegetationPref();
            return original > 0 ? (float)effective / original : 1f;
        }

        // Sanity for the persisted baseline: real game values live in the 10..30 range, so anything
        // bigger is our own raised value that got adopted by mistake (the old "Adopt as baseline"
        // button let that happen — live log showed a baseline of 2000). Reset it rather than keep
        // restoring a bogus value when the toggle goes off.
        private const int GameLodVegetationMaxSaneBaseline = 100;

        // ----------------------------------------------------------------------------------------
        // Shared AuraMono plumbing
        // ----------------------------------------------------------------------------------------

        // Managers._serviceDic[typeof(interface)].manager — the proven module-dict walk
        // (TryResolveDynamicObjectManagerAura) retargeted at the SERVICE dictionary, whose values
        // wrap the manager in ServiceObject.manager. Returned object is pinned; caller frees.
        private unsafe bool TryResolveGameLodService(string interfaceFullName, out IntPtr managerObj, out uint managerPin, out string status)
        {
            managerObj = IntPtr.Zero;
            managerPin = 0U;
            status = "AuraMono unavailable";
            if (auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null)
            {
                return false;
            }

            if (!this.TryCreateAuraMonoSystemTypeObjectFromClass(interfaceFullName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
            {
                status = interfaceFullName + " Type object unresolved";
                return false;
            }

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }

            if (managersClass == IntPtr.Zero || !this.TryGetAuraMonoStaticObjectField(managersClass, "_serviceDic", out IntPtr serviceDicObj)
                || serviceDicObj == IntPtr.Zero)
            {
                status = "Managers._serviceDic unavailable (world not loaded?)";
                return false;
            }

            uint dicPin = AuraMonoPinNew(serviceDicObj);
            IntPtr serviceWrapperObj;
            try
            {
                IntPtr dicClass = auraMonoObjectGetClass(serviceDicObj);
                IntPtr tryGetValueMethod = dicClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(dicClass, "TryGetValue", 2) : IntPtr.Zero;
                if (tryGetValueMethod == IntPtr.Zero)
                {
                    status = "Dictionary.TryGetValue method missing";
                    return false;
                }

                IntPtr localServiceObj = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = typeObj;                     // Type key (reference) — object ptr directly.
                args[1] = (IntPtr)(&localServiceObj);  // out ServiceObject (reference out) — ptr to local.
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(tryGetValueMethod, serviceDicObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "TryGetValue invoke exception";
                    return false;
                }

                bool got = result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool b) && b;
                if (!got || localServiceObj == IntPtr.Zero)
                {
                    status = interfaceFullName + " not registered (world not loaded?)";
                    return false;
                }

                serviceWrapperObj = localServiceObj;
            }
            finally
            {
                AuraMonoPinFree(dicPin);
            }

            uint wrapperPin = AuraMonoPinNew(serviceWrapperObj);
            try
            {
                if (!this.TryGetMonoObjectMember(serviceWrapperObj, "manager", out managerObj) || managerObj == IntPtr.Zero)
                {
                    status = "ServiceObject.manager null";
                    managerObj = IntPtr.Zero;
                    return false;
                }

                managerPin = AuraMonoPinNew(managerObj);
                this.GameLodLogOnce("service " + interfaceFullName + ": ok (impl "
                    + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(managerObj)) + ")");
                status = "ok";
                return true;
            }
            finally
            {
                AuraMonoPinFree(wrapperPin);
            }
        }

        // Invoke a 0-arg instance getter/method returning an object; result NOT pinned.
        private unsafe bool TryGameLodInvokeObject(IntPtr instanceObj, string methodName, out IntPtr resultObj)
        {
            resultObj = IntPtr.Zero;
            if (instanceObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(instanceObj);
            IntPtr method = klass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(klass, methodName, 0) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            resultObj = auraMonoRuntimeInvoke(method, instanceObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }

        // Fresh mono int[] with the given values (element writes via mono_array_addr_with_size —
        // never mutate arrays returned by game getters, they are the config's own instances).
        private unsafe bool TryCreateGameLodIntArray(int[] values, out IntPtr arrayObj)
        {
            arrayObj = IntPtr.Zero;
            if (values == null || auraMonoArrayNew == null || auraMonoArrayAddrWithSize == null
                || auraMonoClassFromName == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr coreImage = this.FindAuraMonoImage(new[] { "mscorlib", "mscorlib.dll", "System.Private.CoreLib", "System.Private.CoreLib.dll" });
            if (coreImage == IntPtr.Zero)
            {
                return false;
            }

            IntPtr int32Class = auraMonoClassFromName(coreImage, "System", "Int32");
            if (int32Class == IntPtr.Zero)
            {
                return false;
            }

            IntPtr arr = auraMonoArrayNew(this.auraMonoRootDomain, int32Class, (UIntPtr)values.Length);
            if (arr == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                IntPtr slot = auraMonoArrayAddrWithSize(arr, 4, (UIntPtr)i);
                if (slot == IntPtr.Zero)
                {
                    return false;
                }
                *(int*)slot = values[i];
            }

            arrayObj = arr;
            return true;
        }

        private unsafe bool TryGameLodUnboxInt32(IntPtr boxed, out int value)
        {
            value = 0;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(int*)raw;
            return true;
        }

        private unsafe bool TryGameLodGetInt16Member(IntPtr obj, string memberName, out short value)
        {
            value = 0;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) || boxed == IntPtr.Zero
                || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(short*)raw;
            return true;
        }

        private IntPtr GameLodEngineWrapperClass(ref IntPtr cache, string nameSpace, string className)
        {
            if (cache == IntPtr.Zero)
            {
                cache = this.FindAuraMonoClassInImages(nameSpace ?? string.Empty, className, GameLodEngineWrapperImages);
            }
            return cache;
        }

        // ----------------------------------------------------------------------------------------
        // Section 1: furniture / homeland streaming (LoaderManager + LayerDistanceCulling)
        // ----------------------------------------------------------------------------------------
        private unsafe bool TryGameLodFurnitureApply(out string status)
        {
            if (!this.TryResolveGameLodService("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem",
                out IntPtr renderSystemObj, out uint renderPin, out status))
            {
                return false;
            }

            try
            {
                if (!this.TryGameLodInvokeObject(renderSystemObj, "get_LoaderManager", out IntPtr loaderObj) || loaderObj == IntPtr.Zero)
                {
                    status = "LoaderManager unavailable";
                    return false;
                }

                uint loaderPin = AuraMonoPinNew(loaderObj);
                try
                {
                    this.GameLodLogOnce("furniture: LoaderManager instance ok ("
                        + this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(loaderObj)) + ")");
                    int dist = Mathf.Clamp(this.gameLodFurnitureDistance, 100, 9999);
                    if (!this.TryCreateGameLodIntArray(new[] { dist, dist, dist, dist }, out IntPtr otherDisArr)
                        || otherDisArr == IntPtr.Zero)
                    {
                        status = "int[] build failed";
                        return false;
                    }

                    uint otherPin = AuraMonoPinNew(otherDisArr);
                    try
                    {
                        if (!this.TryCreateGameLodIntArray(new[] { dist, dist, dist, dist }, out IntPtr myDisArr)
                            || myDisArr == IntPtr.Zero)
                        {
                            status = "int[] build failed";
                            return false;
                        }

                        uint myPin = AuraMonoPinNew(myDisArr);
                        try
                        {
                            IntPtr loaderClass = auraMonoObjectGetClass(loaderObj);
                            IntPtr setParam = loaderClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(loaderClass, "SetParam", 7) : IntPtr.Zero;
                            if (setParam == IntPtr.Zero)
                            {
                                status = "LoaderManager.SetParam missing";
                                return false;
                            }

                            // Pacing: FIXED at the game's own vanilla per-frame rate (3 objects/frame,
                            // same as ObserverPanel's default), deliberately NOT scaled up with `max`
                            // anymore (2026-07-26). It used to scale (max/500, up to 10/frame) so a
                            // big raised cap would visually fill in within a few seconds instead of
                            // ~28s — see git history — but that meant a high max+distance dumped
                            // hundreds of new furniture instances into the scene within a couple of
                            // seconds after a teleport/town-entry. A meaningful fraction of "furniture"
                            // is UGC-photo-bearing (frames/screens/puzzles), and each one kicks off its
                            // own texture download the instant it's created — so a fast fill-in meant a
                            // burst of simultaneous downloads far beyond what the base game (capped at
                            // 60 objects total) ever has to handle at once, which is the confirmed cause
                            // of the blank/white UGC textures (see ugc-texture-cache-blank-fix project
                            // memory: purge + raising the LRU cache to 2000 did NOT fix it; disabling
                            // this draw-distance extension did). Keeping the per-frame rate at the
                            // vanilla constant instead trades faster pop-in (now spread over more
                            // seconds at a high max/distance) for not overwhelming the download
                            // pipeline — max object count and distance are UNCHANGED, only how fast the
                            // client walks up to that ceiling.
                            int max = Mathf.Clamp(this.gameLodFurnitureMaxObjects, 60, 5000);
                            int loadNum = 3;
                            int unloadNum = 20;
                            int strucLoadNum = 20;
                            int meshDis = Mathf.Clamp(this.gameLodFurnitureMeshDistance, 100, 2000);
                            IntPtr* args = stackalloc IntPtr[7];
                            args[0] = (IntPtr)(&max);
                            args[1] = (IntPtr)(&loadNum);
                            args[2] = (IntPtr)(&unloadNum);
                            args[3] = (IntPtr)(&strucLoadNum);
                            args[4] = otherDisArr;
                            args[5] = myDisArr;
                            args[6] = (IntPtr)(&meshDis);
                            IntPtr exc = IntPtr.Zero;
                            auraMonoRuntimeInvoke(setParam, loaderObj, (IntPtr)args, ref exc);
                            if (exc != IntPtr.Zero)
                            {
                                status = "SetParam invoke exception";
                                return false;
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(myPin);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(otherPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(loaderPin);
                }
            }
            finally
            {
                AuraMonoPinFree(renderPin);
            }

            // ObserverPanel.SetHomeland also drops the layer distance culler; non-fatal if missing.
            this.TryGameLodSetLayerCullingEnabled(false, out string cullingStatus);
            this.GameLodLogOnce("furniture: LayerDistanceCulling disable: " + cullingStatus);

            // Push the photo-request radius out to match the spawn radius, or every frame beyond
            // the game's hardcoded 100 m renders white — see TryGameLodSetPhotoRequestDistance.
            // Add the ring-corner margin (spawn is a square ring, the request gate is a sphere):
            // sqrt(3) covers the 3D box diagonal, so nothing can spawn outside the request sphere.
            float photoDis = Mathf.Clamp(this.gameLodFurnitureDistance * 1.75f, 100f, 20000f);
            this.TryGameLodSetPhotoRequestDistance(photoDis, out string photoStatus);
            this.GameLodLogOnce("furniture: photo request distance: " + photoStatus);
            status = "ok";
            return true;
        }

        // ObserverPanel.RevertBuildLoader mirror: SetParam back from RenderLoadConfig defaults.
        private unsafe bool TryGameLodFurnitureRevert(out string status)
        {
            if (!this.TryResolveGameLodService("XDTDataAndProtocol.Config.IConfigManager",
                out IntPtr configManagerObj, out uint configPin, out status))
            {
                return false;
            }

            IntPtr renderLoadConfigObj;
            uint renderLoadConfigPin = 0U;
            try
            {
                if (!this.TryGameLodInvokeObject(configManagerObj, "get_RenderLoadConfig", out renderLoadConfigObj)
                    || renderLoadConfigObj == IntPtr.Zero)
                {
                    status = "RenderLoadConfig unavailable";
                    return false;
                }

                renderLoadConfigPin = AuraMonoPinNew(renderLoadConfigObj);
            }
            finally
            {
                AuraMonoPinFree(configPin);
            }

            try
            {
                IntPtr cfgClass = auraMonoObjectGetClass(renderLoadConfigObj);
                if (cfgClass == IntPtr.Zero)
                {
                    status = "RenderLoadConfig class unavailable";
                    return false;
                }

                // GetLoadDis(bool isSelfHome) — the returned arrays are the config's OWN instances;
                // pass them straight to SetParam like RevertBuildLoader does, never mutate them.
                IntPtr getLoadDis = this.FindAuraMonoMethodOnHierarchy(cfgClass, "GetLoadDis", 1);
                if (getLoadDis == IntPtr.Zero)
                {
                    status = "RenderLoadConfig.GetLoadDis missing";
                    return false;
                }

                bool selfFalse = false;
                bool selfTrue = true;
                IntPtr exc = IntPtr.Zero;
                IntPtr* oneArg = stackalloc IntPtr[1];
                oneArg[0] = (IntPtr)(&selfFalse);
                IntPtr otherDisArr = auraMonoRuntimeInvoke(getLoadDis, renderLoadConfigObj, (IntPtr)oneArg, ref exc);
                if (exc != IntPtr.Zero || otherDisArr == IntPtr.Zero)
                {
                    status = "GetLoadDis(false) failed";
                    return false;
                }

                uint otherPin = AuraMonoPinNew(otherDisArr);
                try
                {
                    oneArg[0] = (IntPtr)(&selfTrue);
                    exc = IntPtr.Zero;
                    IntPtr myDisArr = auraMonoRuntimeInvoke(getLoadDis, renderLoadConfigObj, (IntPtr)oneArg, ref exc);
                    if (exc != IntPtr.Zero || myDisArr == IntPtr.Zero)
                    {
                        status = "GetLoadDis(true) failed";
                        return false;
                    }

                    uint myPin = AuraMonoPinNew(myDisArr);
                    try
                    {
                        if (!this.TryGameLodReadConfigInt(renderLoadConfigObj, "GetMaxNum", out int maxNum)
                            || !this.TryGameLodReadConfigInt(renderLoadConfigObj, "GetLoadNum", out int loadNum)
                            || !this.TryGameLodReadConfigInt(renderLoadConfigObj, "GetUnloadNum", out int unloadNum)
                            || !this.TryGameLodReadConfigInt(renderLoadConfigObj, "GetStructLoadNum", out int structLoadNum)
                            || !this.TryGameLodReadConfigInt(renderLoadConfigObj, "GetLoadMeshDis", out int loadMeshDis))
                        {
                            status = "RenderLoadConfig getters failed";
                            return false;
                        }

                        if (!this.TryResolveGameLodService("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem",
                            out IntPtr renderSystemObj, out uint renderPin, out status))
                        {
                            return false;
                        }

                        try
                        {
                            if (!this.TryGameLodInvokeObject(renderSystemObj, "get_LoaderManager", out IntPtr loaderObj)
                                || loaderObj == IntPtr.Zero)
                            {
                                status = "LoaderManager unavailable";
                                return false;
                            }

                            uint loaderPin = AuraMonoPinNew(loaderObj);
                            try
                            {
                                IntPtr loaderClass = auraMonoObjectGetClass(loaderObj);
                                IntPtr setParam = loaderClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(loaderClass, "SetParam", 7) : IntPtr.Zero;
                                if (setParam == IntPtr.Zero)
                                {
                                    status = "LoaderManager.SetParam missing";
                                    return false;
                                }

                                IntPtr* args = stackalloc IntPtr[7];
                                args[0] = (IntPtr)(&maxNum);
                                args[1] = (IntPtr)(&loadNum);
                                args[2] = (IntPtr)(&unloadNum);
                                args[3] = (IntPtr)(&structLoadNum);
                                args[4] = otherDisArr;
                                args[5] = myDisArr;
                                args[6] = (IntPtr)(&loadMeshDis);
                                exc = IntPtr.Zero;
                                auraMonoRuntimeInvoke(setParam, loaderObj, (IntPtr)args, ref exc);
                                if (exc != IntPtr.Zero)
                                {
                                    status = "SetParam invoke exception";
                                    return false;
                                }
                            }
                            finally
                            {
                                AuraMonoPinFree(loaderPin);
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(renderPin);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(myPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(otherPin);
                }
            }
            finally
            {
                AuraMonoPinFree(renderLoadConfigPin);
            }

            // Stock behavior has the layer distance culler active.
            this.TryGameLodSetLayerCullingEnabled(true, out string cullingStatus);
            this.GameLodLogOnce("furniture: LayerDistanceCulling re-enable: " + cullingStatus);

            // Back to the game's own hardcoded PhotoFrameComponent.PhotoLodDis value.
            this.TryGameLodSetPhotoRequestDistance(GameLodStockPhotoLodDistance, out string photoStatus);
            this.GameLodLogOnce("furniture: photo request distance revert: " + photoStatus);
            status = "ok";
            return true;
        }

        private unsafe bool TryGameLodReadConfigInt(IntPtr configObj, string methodName, out int value)
        {
            value = 0;
            return this.TryGameLodInvokeObject(configObj, methodName, out IntPtr boxed)
                && this.TryGameLodUnboxInt32(boxed, out value);
        }

        // ----------------------------------------------------------------------------------------
        // THE blank-UGC-photo fix (2026-07-26). Root cause, confirmed in the decompiled source:
        //
        //   PhotoFrameComponent.PhotoLodDis = new float[3] { 100f, 100f, 100f };   // hardcoded
        //   OnSpawned():  _lodDisSqr = PhotoLodDis[type-1] ^ 2;
        //   Tick():       if (!_haveCache && _loader == null
        //                     && sqrDist(selfPlayer, entity) < _lodDisSqr)  -> Create(loader)
        //
        // A photo frame only ever REQUESTS its photo within 100 m. Vanilla furniture streaming
        // distances are RenderLoadConfig.LoadDis = [80, 30, 30, 24] per voxel tier, and photo
        // frames / screens / display items are ordinary furniture = tier 2/3, i.e. vanilla spawns
        // them only within 24-30 m — far INSIDE the 100 m request radius. The game's design
        // silently depends on furniture-spawn-radius << photo-request-radius, so the 100 m gate
        // never binds and every frame you can see has already fetched its photo.
        //
        // This feature inverts that relationship: it applies ONE distance to all four tiers, and
        // its slider bottoms out at 100 (TryGameLodFurnitureApply clamps 100..9999). So even at
        // the LOWEST possible setting, photo-bearing furniture spawns out to 100 m — and because
        // the voxel streamer expands in square RINGS (validDis = dis/cellSize, Chebyshev), the box
        // corners reach ~dist*sqrt(2) horizontally, i.e. ~140 m at a setting of 100 — while the
        // photo request gate stays a 100 m EUCLIDEAN SPHERE. Every frame spawned in that shell is
        // visible but never requests its texture, so it renders white until you walk closer.
        // That is exactly why no slider value helped and only switching the feature off did:
        // there is no setting where spawn-radius stays under the hardcoded 100 m gate.
        //
        // Fix: move the gate out with us. PhotoLodDis is `public static readonly float[]` — the
        // reference is readonly, the CONTENTS are not — so we write its 3 elements via
        // mono_array_addr_with_size (same element-write technique already used for the
        // SceneLoaderRoot hlodDistances arrays). Caveat, deliberate: OnSpawned caches _lodDisSqr
        // per component, so this only affects frames spawned AFTER the write — which is precisely
        // the ones streaming in at distance, the ones that were blank. Already-spawned nearby
        // frames were never broken.
        private unsafe bool TryGameLodSetPhotoRequestDistance(float distance, out string status)
        {
            status = "PhotoFrameComponent unavailable";
            if (auraMonoArrayAddrWithSize == null || auraMonoArrayLength == null)
            {
                return false;
            }

            if (this.gameLodPhotoFrameClass == IntPtr.Zero)
            {
                // Fully qualified: there is a SECOND, unrelated PhotoFrameComponent stub under
                // ...Gameplay.Component.GuiChar with empty method bodies — resolving by bare name
                // could bind the wrong one.
                this.gameLodPhotoFrameClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.PhotoFrameComponent");
            }

            if (this.gameLodPhotoFrameClass == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetAuraMonoStaticObjectField(this.gameLodPhotoFrameClass, "PhotoLodDis", out IntPtr arrayObj)
                || arrayObj == IntPtr.Zero)
            {
                status = "PhotoLodDis not initialized yet";
                return false;
            }

            uint pin = AuraMonoPinNew(arrayObj);
            try
            {
                int length = (int)auraMonoArrayLength(arrayObj);
                if (length <= 0 || length > 16)
                {
                    status = "PhotoLodDis unexpected length " + length;
                    return false;
                }

                for (int i = 0; i < length; i++)
                {
                    IntPtr slot = auraMonoArrayAddrWithSize(arrayObj, 4, (UIntPtr)i);
                    if (slot == IntPtr.Zero)
                    {
                        status = "PhotoLodDis element " + i + " unreachable";
                        return false;
                    }
                    *(float*)slot = distance;
                }

                status = "ok (" + distance.ToString("0") + " m)";
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe bool TryGameLodSetLayerCullingEnabled(bool enabled, out string status)
        {
            status = "LayerDistanceCulling unavailable";
            IntPtr klass = this.GameLodEngineWrapperClass(ref this.gameLodLayerCullingClass, string.Empty, "LayerDistanceCulling");
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getInstance = this.FindAuraMonoMethodOnHierarchy(klass, "get_Instance", 0);
            if (getInstance == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr instanceObj = auraMonoRuntimeInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || instanceObj == IntPtr.Zero)
            {
                status = "LayerDistanceCulling.Instance null";
                return false;
            }

            uint pin = AuraMonoPinNew(instanceObj);
            try
            {
                IntPtr instClass = auraMonoObjectGetClass(instanceObj);
                IntPtr setEnabled = instClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(instClass, "set_enabled", 1) : IntPtr.Zero;
                if (setEnabled == IntPtr.Zero)
                {
                    status = "Behaviour.set_enabled missing";
                    return false;
                }

                bool value = enabled;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&value);
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(setEnabled, instanceObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "set_enabled invoke exception";
                    return false;
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            status = "ok";
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 2: BRG mesh quality (ForeceLOD0 static flag + AreaPriorityManager global bias)
        // ----------------------------------------------------------------------------------------
        private unsafe bool TryGameLodSetForceLod0(bool on, out string status)
        {
            status = "BrgManager unavailable";
            IntPtr klass = this.GameLodEngineWrapperClass(ref this.gameLodBrgManagerClass,
                "ScriptsRefactory.BaseService.RenderSystem.Brg", "BrgManager");
            if (klass == IntPtr.Zero)
            {
                klass = this.FindAuraMonoClassByFullName("ScriptsRefactory.BaseService.RenderSystem.Brg.BrgManager");
                this.gameLodBrgManagerClass = klass;
            }
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            // The game's own typo: "ForeceLOD0".
            IntPtr setter = this.FindAuraMonoMethodOnHierarchy(klass, "set_ForeceLOD0", 1);
            if (setter == IntPtr.Zero)
            {
                status = "BrgManager.set_ForeceLOD0 missing";
                return false;
            }

            bool value = on;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(setter, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "set_ForeceLOD0 invoke exception";
                return false;
            }

            status = on ? this.L("Max mesh detail forced (BRG LOD0).") : "ok";
            return true;
        }

        private unsafe bool TryGameLodApplyBrgBias(float bias, out string status)
        {
            if (!this.TryResolveGameLodService("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem",
                out IntPtr renderSystemObj, out uint renderPin, out status))
            {
                return false;
            }

            try
            {
                if (!this.TryGameLodInvokeObject(renderSystemObj, "get_AreaPriorityManager", out IntPtr areaObj) || areaObj == IntPtr.Zero)
                {
                    status = "AreaPriorityManager unavailable";
                    return false;
                }

                uint areaPin = AuraMonoPinNew(areaObj);
                try
                {
                    IntPtr areaClass = auraMonoObjectGetClass(areaObj);
                    IntPtr update = areaClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(areaClass, "UpdateGlobalLodBias", 1) : IntPtr.Zero;
                    if (update == IntPtr.Zero)
                    {
                        status = "UpdateGlobalLodBias missing";
                        return false;
                    }

                    float value = Mathf.Clamp(bias, 0.2f, 4f);
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&value);
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(update, areaObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "UpdateGlobalLodBias invoke exception";
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(areaPin);
                }
            }
            finally
            {
                AuraMonoPinFree(renderPin);
            }

            status = this.LF("Furniture LOD bias: x{0:0.0}.", bias);
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 3: vegetation / neighbor houses (PC_LODBIAS + InstanceBlock rebake)
        // ----------------------------------------------------------------------------------------
        internal void GameLodWriteVegetationPref()
        {
            try
            {
                int target = this.GameLodEffectiveVegetationPref();
                PlayerPrefs.SetInt("PC_LODBIAS", target);
                PlayerPrefs.Save();
                this.gameLodVegetationLastWrittenPref = target;
            }
            catch (Exception ex)
            {
                this.gameLodVegetationStatus = "PlayerPrefs write failed: " + ex.Message;
            }
        }

        // The game's own settings UI writes PC_LODBIAS too (live smoke: it held 30 while our
        // toggle was on), so the value is re-checked on the apply cadence: an external change
        // gets logged, re-written, and re-baked — otherwise the override silently dies.
        private void GameLodReassertVegetationPref(bool heavyOk)
        {
            if (!this.gameLodVegetationEnabled)
            {
                return;
            }

            int live;
            try { live = PlayerPrefs.GetInt("PC_LODBIAS", GameLodVegetationPrefDefault); }
            catch { return; }

            // While the world is loading the game normally sees ITS OWN value, otherwise TreeLoad
            // builds every instance block at max detail during the load (that is the cost the
            // loading screen was paying). Opting in to "apply during load" keeps ours live instead:
            // the only way terrain chunks — which bake at chunk-load time and are out of reach of
            // any post-load rebake — come up at full detail.
            if (!heavyOk && !this.gameLodVegetationApplyDuringLoad)
            {
                int baseline = this.GameLodVegetationOriginalPref();
                if (live != baseline)
                {
                    try
                    {
                        PlayerPrefs.SetInt("PC_LODBIAS", baseline);
                        PlayerPrefs.Save();
                        this.gameLodVegetationLastWrittenPref = baseline;
                    }
                    catch { }
                }
                return;
            }

            int want = this.GameLodEffectiveVegetationPref();
            if (live == want)
            {
                return;
            }

            this.GameLodLogOnce("vegetation: PC_LODBIAS " + live + " -> " + want + " (post-load) — rebaking once");
            this.GameLodWriteVegetationPref();
            this.gameLodVegetationRebakePending = true;
        }

        private unsafe bool TryGameLodVegetationRebake(out string status)
        {
            status = "TreeLoad unavailable";
            if (this.gameLodTreeLoadClass == IntPtr.Zero)
            {
                this.gameLodTreeLoadClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.InstanceBlock.TreeLoad");
            }
            if (this.gameLodHomeLoadClass == IntPtr.Zero)
            {
                this.gameLodHomeLoadClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.InstanceBlock.HomeLoad");
            }

            if (this.gameLodTreeLoadClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr setAll = this.FindAuraMonoMethodOnHierarchy(this.gameLodTreeLoadClass, "SetAllDynamicInstanceBlockEnable", 1);
            if (setAll == IntPtr.Zero)
            {
                status = "SetAllDynamicInstanceBlockEnable missing";
                return false;
            }

            // The game method walks GameObject.Find("QualityLoader") children — without that root
            // the rebake is a silent no-op, which would look exactly like "the setting does nothing".
            bool qualityLoaderPresent = false;
            try { qualityLoaderPresent = GameObject.Find("QualityLoader") != null; } catch { }
            if (!qualityLoaderPresent)
            {
                status = this.L("QualityLoader root missing in this scene — nothing to rebake.");
                this.GameLodLogOnce("vegetation: QualityLoader GameObject NOT found — TreeLoad rebake is a no-op here");
                return false;
            }

            bool off = false;
            bool on = true;
            IntPtr* args = stackalloc IntPtr[1];
            IntPtr exc = IntPtr.Zero;
            args[0] = (IntPtr)(&off);
            auraMonoRuntimeInvoke(setAll, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "TreeLoad disable invoke exception";
                return false;
            }

            exc = IntPtr.Zero;
            args[0] = (IntPtr)(&on);
            auraMonoRuntimeInvoke(setAll, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "TreeLoad enable invoke exception";
                return false;
            }

            this.GameLodLogOnce("vegetation: TreeLoad.SetAllDynamicInstanceBlockEnable off/on ok");

            // Neighbor-house instancing (HomeLoad) — optional; scene may not have it loaded.
            if (this.gameLodHomeLoadClass != IntPtr.Zero)
            {
                IntPtr getInst = this.FindAuraMonoMethodOnHierarchy(this.gameLodHomeLoadClass, "get_Inst", 0);
                if (getInst != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    IntPtr homeLoadObj = auraMonoRuntimeInvoke(getInst, IntPtr.Zero, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && homeLoadObj != IntPtr.Zero)
                    {
                        uint pin = AuraMonoPinNew(homeLoadObj);
                        try
                        {
                            bool disOk = this.TryGameLodInvokeObject(homeLoadObj, "OnDisable", out _);
                            bool enOk = this.TryGameLodInvokeObject(homeLoadObj, "OnEnable", out _);
                            this.GameLodLogOnce("vegetation: HomeLoad rebake "
                                + (disOk && enOk ? "ok" : ("FAILED (disable=" + disOk + " enable=" + enOk + ")")));
                        }
                        finally
                        {
                            AuraMonoPinFree(pin);
                        }
                    }
                    else
                    {
                        this.GameLodLogOnce("vegetation: HomeLoad.Inst null (no neighbor-house instancing in this scene)");
                    }
                }
            }
            else
            {
                this.GameLodLogOnce("vegetation: HomeLoad class unresolved (TreeLoad-only rebake)");
            }

            status = "ok";
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 4: characters (SignificanceManager.Enable)
        // ----------------------------------------------------------------------------------------
        private unsafe bool TryGameLodSetSignificanceEnabled(bool enabled, out string status)
        {
            status = "SignificanceManager unavailable";
            if (this.gameLodSignificanceManagerClass == IntPtr.Zero)
            {
                this.gameLodSignificanceManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.BaseSystem.SignificanceManager.SignificanceManager");
            }
            if (this.gameLodSignificanceManagerClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr instanceObj = this.TryGetAuraMonoDataModuleInstance(this.gameLodSignificanceManagerClass);
            if (instanceObj == IntPtr.Zero)
            {
                status = "SignificanceManager.Instance null (world not loaded?)";
                return false;
            }

            uint pin = AuraMonoPinNew(instanceObj);
            try
            {
                IntPtr setter = this.FindAuraMonoMethodOnHierarchy(this.gameLodSignificanceManagerClass, "set_Enable", 1);
                if (setter == IntPtr.Zero)
                {
                    status = "set_Enable missing";
                    return false;
                }

                bool value = enabled;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&value);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(setter, instanceObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "set_Enable invoke exception";
                    return false;
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            status = "ok";
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 5: shadows (URP asset shadowDistance via RenderingSettings — ObserverPanel path)
        // ----------------------------------------------------------------------------------------
        private unsafe bool TryGameLodApplyShadowDistance(bool revert, out string status)
        {
            status = "RenderingSettings unavailable";
            IntPtr klass = this.GameLodEngineWrapperClass(ref this.gameLodRenderingSettingsClass, string.Empty, "RenderingSettings");
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getInstance = this.FindAuraMonoMethodOnHierarchy(klass, "get_Instance", 0);
            if (getInstance == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr settingsObj = auraMonoRuntimeInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || settingsObj == IntPtr.Zero)
            {
                status = "RenderingSettings.Instance null";
                return false;
            }

            uint settingsPin = AuraMonoPinNew(settingsObj);
            try
            {
                if (!this.TryGameLodInvokeObject(settingsObj, "GetCurrentURPAsset", out IntPtr urpObj) || urpObj == IntPtr.Zero)
                {
                    status = "GetCurrentURPAsset null";
                    return false;
                }

                uint urpPin = AuraMonoPinNew(urpObj);
                try
                {
                    IntPtr urpClass = auraMonoObjectGetClass(urpObj);
                    if (urpClass == IntPtr.Zero)
                    {
                        status = "URP asset class unavailable";
                        return false;
                    }

                    this.GameLodLogOnce("shadow: URP asset ok (" + this.GetAuraMonoClassDisplayName(urpClass) + ")");

                    if (!revert && !this.gameLodShadowOriginalCaptured)
                    {
                        IntPtr getter = this.FindAuraMonoMethodOnHierarchy(urpClass, "get_shadowDistance", 0);
                        if (getter != IntPtr.Zero)
                        {
                            exc = IntPtr.Zero;
                            IntPtr boxed = auraMonoRuntimeInvoke(getter, urpObj, IntPtr.Zero, ref exc);
                            if (exc == IntPtr.Zero && boxed != IntPtr.Zero && auraMonoObjectUnbox != null
                                && this.TryAuraMonoBoxedIsValueType(boxed))
                            {
                                IntPtr raw = auraMonoObjectUnbox(boxed);
                                if (raw != IntPtr.Zero)
                                {
                                    this.gameLodShadowOriginal = *(float*)raw;
                                    this.gameLodShadowOriginalCaptured = true;
                                    this.GameLodLogOnce("shadow: original captured "
                                        + this.gameLodShadowOriginal.ToString("F0") + " m");
                                }
                            }
                        }
                    }

                    IntPtr setter = this.FindAuraMonoMethodOnHierarchy(urpClass, "set_shadowDistance", 1);
                    if (setter == IntPtr.Zero)
                    {
                        status = "set_shadowDistance missing";
                        return false;
                    }

                    float value = revert
                        ? (this.gameLodShadowOriginalCaptured ? this.gameLodShadowOriginal : 100f)
                        : Mathf.Clamp(this.gameLodShadowDistance, 50f, 800f);
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&value);
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(setter, urpObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "set_shadowDistance invoke exception";
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(urpPin);
                }
            }
            finally
            {
                AuraMonoPinFree(settingsPin);
            }

            status = "ok";
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 6: live entities (NineCell range boost via LevelEntityComponent.SetForceNineCell)
        // ----------------------------------------------------------------------------------------
        private unsafe void GameLodTickNineCell()
        {
            if (this.gameLodLevelEntityComponentClass == IntPtr.Zero)
            {
                this.gameLodLevelEntityComponentClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.EntityView.LevelEntityComponent");
            }

            if (this.gameLodLevelEntityComponentClass == IntPtr.Zero)
            {
                this.gameLodNineCellStatus = "LevelEntityComponent class unresolved";
                this.GameLodLogOnce("ninecell: " + this.gameLodNineCellStatus);
                return;
            }

            bool reverting = this.gameLodNineCellRevertPending;
            List<uint> pins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(this.gameLodLevelEntityComponentClass, out List<IntPtr> components, pins))
            {
                this.GameLodLogOnce("ninecell: GetComponents<LevelEntityComponent> returned nothing (world loading / infra not ready)");
                // No components (loading screen / empty world) is normal during revert too — if the
                // dict is already empty there is nothing left to restore.
                if (reverting && this.gameLodNineCellRanges.Count == 0)
                {
                    this.gameLodNineCellRevertPending = false;
                    this.gameLodNineCellStatus = this.L("Reverted to game defaults.");
                }
                return;
            }

            if (!this.gameLodNineCellFirstWalkLogged)
            {
                this.gameLodNineCellFirstWalkLogged = true;
                this.GameLodLogOnce("ninecell: first walk ok, components=" + components.Count
                    + " (class " + GameLodPtr(this.gameLodLevelEntityComponentClass) + ")");
            }

            int boosted = 0;
            int restored = 0;
            try
            {
                float mult = Mathf.Clamp(this.gameLodNineCellMult, 1f, 5f);
                // Revert is a one-shot restore — walk everything. Boost walks are budgeted, with a
                // rotating start so a >budget world still converges over successive walks.
                int limit = reverting ? components.Count : Mathf.Min(components.Count, GameLodNineCellWalkBudget);
                int offset = (reverting || components.Count <= GameLodNineCellWalkBudget)
                    ? 0
                    : this.gameLodNineCellWalkOffset % components.Count;
                if (!reverting && components.Count > GameLodNineCellWalkBudget)
                {
                    this.gameLodNineCellWalkOffset = (offset + GameLodNineCellWalkBudget) % components.Count;
                }
                for (int step = 0; step < limit; step++)
                {
                    int i = (offset + step) % components.Count;
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGameLodGetInt16Member(comp, "_nineCellRange", out short range) || range <= 0)
                    {
                        continue; // not distance-streamed (furniture / local / unregistered)
                    }

                    if (!this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    if (reverting)
                    {
                        if (this.gameLodNineCellRanges.TryGetValue(netId, out GameLodNineCellEntry entry)
                            && range != entry.Orig)
                        {
                            if (this.TryGameLodSetForceNineCell(comp, entry.Orig))
                            {
                                restored++;
                            }
                        }
                        continue;
                    }

                    GameLodNineCellEntry state;
                    if (!this.gameLodNineCellRanges.TryGetValue(netId, out state) || range != state.LastTarget)
                    {
                        // First sighting, or the game re-created the component with its own config
                        // range (world reload / respawn) — capture that as the original.
                        state.Orig = range;
                        state.LastTarget = 0;
                    }

                    short target = (short)Mathf.Clamp(Mathf.RoundToInt(state.Orig * mult), state.Orig, GameLodNineCellRangeCap);
                    if (target != range && this.TryGameLodSetForceNineCell(comp, target))
                    {
                        state.LastTarget = target;
                        this.gameLodNineCellRanges[netId] = state;
                        boosted++;
                    }
                    else if (target == range)
                    {
                        state.LastTarget = target;
                        this.gameLodNineCellRanges[netId] = state;
                        boosted++;
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            if (reverting)
            {
                this.gameLodNineCellRevertPending = false;
                this.gameLodNineCellRanges.Clear();
                this.gameLodNineCellBoostedCount = 0;
                this.gameLodNineCellStatus = this.LF("Reverted {0} entities to game defaults.", restored);
                this.GameLodLogOnce("ninecell revert: restored " + restored + " entities");
                return;
            }

            this.gameLodNineCellBoostedCount = boosted;
            this.gameLodNineCellStatus = this.LF("Extended range on {0} entities (x{1:0.0}, server-capped).",
                boosted, Mathf.Clamp(this.gameLodNineCellMult, 1f, 5f));
            this.GameLodLogOnce("ninecell apply: boosted " + boosted + " entities (x"
                + Mathf.Clamp(this.gameLodNineCellMult, 1f, 5f).ToString("0.0") + ")");
        }

        private unsafe bool TryGameLodSetForceNineCell(IntPtr componentObj, short range)
        {
            if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(componentObj);
            IntPtr method = klass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(klass, "SetForceNineCell", 1) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                this.GameLodLogOnce("ninecell: SetForceNineCell missing on " + this.GetAuraMonoClassDisplayName(klass));
                return false;
            }

            short value = range;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, componentObj, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // ----------------------------------------------------------------------------------------
        // Section 7: landscape HLOD (Unity.HLODSystem.Streaming.HLODController, IL2CPP interop)
        //
        // Scene-baked controllers hold public float fields hlod1/2LoadAndVisDistance; their
        // per-frame Update() re-evaluates the tree against camera distance, so field writes take
        // effect live — no rebake. Multiplying both distances keeps REAL (high-poly) content
        // loaded/visible much further before collapsing to the merged low-poly proxy.
        // ----------------------------------------------------------------------------------------
        private void GameLodTickHlod()
        {
            if (this.gameLodHlodRevertPending)
            {
                bool ctrlOk = this.TryGameLodHlodApply(1f, true, out string ctrlStatus, out int ctrlReverted);
                bool loaderOk = this.TryGameLodSceneLoaderApply(1f, true, out string loaderStatus, out int layersReverted);
                if (ctrlOk && loaderOk)
                {
                    this.gameLodHlodRevertPending = false;
                    this.gameLodHlodStatus = this.LF("Reverted {0} controllers + {1} scene layers to game defaults.",
                        ctrlReverted, layersReverted);
                    this.GameLodLogOnce("hlod revert: controllers=" + ctrlReverted + " sceneLayers=" + layersReverted);
                }
                else
                {
                    this.gameLodHlodStatus = ctrlOk ? loaderStatus : ctrlStatus;
                    this.GameLodLogOnce("hlod revert: ctrl=" + ctrlStatus + " loader=" + loaderStatus);
                }
            }
            else if (this.gameLodHlodEnabled)
            {
                if (!this.IsGameLodHeavyApplyAllowed())
                {
                    this.gameLodHlodStatus = this.L("Waiting for the world to finish loading…");
                    return;
                }

                float mult = Mathf.Clamp(this.gameLodHlodMult, 1f, 4f);
                bool ctrlOk = this.TryGameLodHlodApply(mult, false, out string ctrlStatus, out int controllers);
                bool loaderOk = this.TryGameLodSceneLoaderApply(mult, false, out string loaderStatus, out int layers);
                // Either subsystem counts as success — interiors have neither, sea/homeland scenes
                // often have only the scene-loader layers, towns can have both.
                bool ok = ctrlOk || loaderOk;
                this.gameLodHlodStatus = ok
                    ? this.LF("Extended {0} HLOD controllers + {1} scene layers (x{2:0.0}).",
                        ctrlOk ? controllers : 0, loaderOk ? layers : 0, mult)
                    : ctrlStatus + " / " + loaderStatus;
                this.GameLodLogOnce("hlod apply: controllers=" + (ctrlOk ? controllers.ToString() : ("FAIL " + ctrlStatus))
                    + " sceneLayers=" + (loaderOk ? layers.ToString() : ("FAIL " + loaderStatus))
                    + " x" + mult.ToString("0.0"));
            }
        }

        // SceneLoader.SceneLoaderRoot.configs[] — multiply loadDistance/unloadDistance and every
        // hlodDistances element. The hlodDistances float[] is written ELEMENT-wise (the LoadLayer
        // holds the same array instance, so element writes are live); scalars via il2cpp field
        // reflection. cellSize/cellCount/pcLoadMag untouched.
        private bool TryGameLodSceneLoaderApply(float mult, bool revert, out string status, out int layers)
        {
            layers = 0;
            status = "SceneLoaderRoot type unresolved";
            if (!this.gameLodSceneLoaderIl2CppTypeResolved)
            {
                this.gameLodSceneLoaderIl2CppTypeResolved = true;
                try
                {
                    this.gameLodSceneLoaderIl2CppType =
                        Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot, SceneLoader")
                        ?? Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot");
                }
                catch (Exception ex)
                {
                    this.gameLodSceneLoaderIl2CppType = null;
                    this.GameLodLogOnce("sceneloader: il2cpp type resolve failed: " + ex.Message);
                }
                this.GameLodLogOnce("sceneloader: SceneLoaderRoot il2cpp type "
                    + (this.gameLodSceneLoaderIl2CppType != null ? "resolved" : "UNRESOLVED"));
            }

            if (this.gameLodSceneLoaderIl2CppType == null)
            {
                this.gameLodSceneLoaderIl2CppTypeResolved = false; // image may load later
                return false;
            }

            Il2CppReferenceArray<UnityEngine.Object> roots;
            try
            {
                roots = UnityEngine.Object.FindObjectsOfType(this.gameLodSceneLoaderIl2CppType);
                if (roots == null || roots.Length == 0)
                {
                    roots = Resources.FindObjectsOfTypeAll(this.gameLodSceneLoaderIl2CppType);
                }
            }
            catch (Exception ex)
            {
                status = "SceneLoaderRoot find failed: " + ex.Message;
                return false;
            }

            if (roots == null || roots.Length == 0)
            {
                if (revert)
                {
                    this.gameLodSceneLoaderBaselines.Clear();
                    status = "ok";
                    return true;
                }
                status = this.L("No SceneLoaderRoot in this scene.");
                return false;
            }

            for (int r = 0; r < roots.Length; r++)
            {
                UnityEngine.Object root = roots[r];
                if (root == null)
                {
                    continue;
                }

                try
                {
                    int rootId = root.GetInstanceID();
                    Il2CppSystem.Type rootType = root.GetIl2CppType();
                    Il2CppSystem.Reflection.FieldInfo configsField = rootType.GetField("configs");
                    Il2CppSystem.Object configsBoxed = configsField != null ? configsField.GetValue(root) : null;
                    if (configsBoxed == null)
                    {
                        continue;
                    }

                    Il2CppReferenceArray<Il2CppSystem.Object> configs =
                        new Il2CppReferenceArray<Il2CppSystem.Object>(configsBoxed.Pointer);
                    for (int c = 0; c < configs.Length; c++)
                    {
                        Il2CppSystem.Object cfg = configs[c];
                        if (cfg == null)
                        {
                            continue;
                        }

                        Il2CppSystem.Type cfgType = cfg.GetIl2CppType();
                        Il2CppSystem.Reflection.FieldInfo loadField = cfgType.GetField("loadDistance");
                        Il2CppSystem.Reflection.FieldInfo unloadField = cfgType.GetField("unloadDistance");
                        Il2CppSystem.Reflection.FieldInfo hlodField = cfgType.GetField("hlodDistances");
                        if (loadField == null || unloadField == null)
                        {
                            continue;
                        }

                        float curLoad = loadField.GetValue(cfg).Unbox<float>();
                        float curUnload = unloadField.GetValue(cfg).Unbox<float>();
                        Il2CppStructArray<float> hlods = null;
                        Il2CppSystem.Object hlodBoxed = hlodField != null ? hlodField.GetValue(cfg) : null;
                        if (hlodBoxed != null)
                        {
                            hlods = new Il2CppStructArray<float>(hlodBoxed.Pointer);
                        }

                        long key = ((long)rootId << 8) ^ (uint)c;
                        if (revert)
                        {
                            if (this.gameLodSceneLoaderBaselines.TryGetValue(key, out GameLodSceneLoaderBaseline baseline))
                            {
                                loadField.SetValue(cfg, new Il2CppSystem.Single { m_value = baseline.Load }.BoxIl2CppObject());
                                unloadField.SetValue(cfg, new Il2CppSystem.Single { m_value = baseline.Unload }.BoxIl2CppObject());
                                if (hlods != null && baseline.Hlods != null)
                                {
                                    for (int i = 0; i < hlods.Length && i < baseline.Hlods.Length; i++)
                                    {
                                        hlods[i] = baseline.Hlods[i];
                                    }
                                }
                                layers++;
                            }
                            continue;
                        }

                        if (!this.gameLodSceneLoaderBaselines.TryGetValue(key, out GameLodSceneLoaderBaseline b))
                        {
                            b = new GameLodSceneLoaderBaseline { Load = curLoad, Unload = curUnload };
                            if (hlods != null)
                            {
                                b.Hlods = new float[hlods.Length];
                                for (int i = 0; i < hlods.Length; i++)
                                {
                                    b.Hlods[i] = hlods[i];
                                }
                            }
                            this.gameLodSceneLoaderBaselines[key] = b;
                            this.GameLodLogOnce("sceneloader: root " + rootId + " cfg " + c
                                + " originals load=" + curLoad.ToString("F0")
                                + " unload=" + curUnload.ToString("F0")
                                + " hlods=" + (b.Hlods != null ? string.Join("/", Array.ConvertAll(b.Hlods, v => v.ToString("F0"))) : "none"));
                        }

                        float targetLoad = b.Load * mult;
                        float targetUnload = b.Unload * mult;
                        if (Mathf.Abs(curLoad - targetLoad) > 0.01f)
                        {
                            loadField.SetValue(cfg, new Il2CppSystem.Single { m_value = targetLoad }.BoxIl2CppObject());
                        }
                        if (Mathf.Abs(curUnload - targetUnload) > 0.01f)
                        {
                            unloadField.SetValue(cfg, new Il2CppSystem.Single { m_value = targetUnload }.BoxIl2CppObject());
                        }
                        if (hlods != null && b.Hlods != null)
                        {
                            for (int i = 0; i < hlods.Length && i < b.Hlods.Length; i++)
                            {
                                float target = b.Hlods[i] * mult;
                                if (Mathf.Abs(hlods[i] - target) > 0.01f)
                                {
                                    hlods[i] = target;
                                }
                            }
                        }
                        layers++;
                    }
                }
                catch (Exception ex)
                {
                    this.GameLodLogOnce("sceneloader: root access failed: " + ex.Message);
                }
            }

            if (revert)
            {
                this.gameLodSceneLoaderBaselines.Clear();
            }

            status = "ok";
            return true;
        }

        private bool TryGameLodHlodApply(float mult, bool revert, out string status, out int touched)
        {
            touched = 0;
            status = "HLODController type unresolved";
            if (!this.gameLodHlodIl2CppTypeResolved)
            {
                this.gameLodHlodIl2CppTypeResolved = true;
                try
                {
                    this.gameLodHlodIl2CppType =
                        Il2CppSystem.Type.GetType("Unity.HLODSystem.Streaming.HLODController, HLOD")
                        ?? Il2CppSystem.Type.GetType("Unity.HLODSystem.Streaming.HLODController");
                }
                catch (Exception ex)
                {
                    this.gameLodHlodIl2CppType = null;
                    this.GameLodLogOnce("hlod: il2cpp type resolve failed: " + ex.Message);
                }
                this.GameLodLogOnce("hlod: HLODController il2cpp type "
                    + (this.gameLodHlodIl2CppType != null ? "resolved" : "UNRESOLVED"));
            }

            if (this.gameLodHlodIl2CppType == null)
            {
                // Retry the resolve later — the HLOD image may not be loaded pre-town.
                this.gameLodHlodIl2CppTypeResolved = false;
                return false;
            }

            Il2CppSystem.Reflection.FieldInfo f1;
            Il2CppSystem.Reflection.FieldInfo f2;
            try
            {
                f1 = this.gameLodHlodIl2CppType.GetField("hlod1LoadAndVisDistance");
                f2 = this.gameLodHlodIl2CppType.GetField("hlod2LoadAndVisDistance");
            }
            catch (Exception ex)
            {
                status = "HLOD field lookup failed: " + ex.Message;
                return false;
            }

            if (f1 == null || f2 == null)
            {
                status = "HLOD distance fields missing";
                this.GameLodLogOnce("hlod: " + status);
                return false;
            }

            Il2CppReferenceArray<UnityEngine.Object> found;
            try
            {
                found = UnityEngine.Object.FindObjectsOfType(this.gameLodHlodIl2CppType);
                // Scene HLOD roots can sit on inactive parents — FindObjectsOfType (active-only)
                // then misses them; sweep the full object table before concluding "none".
                if (found == null || found.Length == 0)
                {
                    found = Resources.FindObjectsOfTypeAll(this.gameLodHlodIl2CppType);
                }
            }
            catch (Exception ex)
            {
                status = "FindObjectsOfType failed: " + ex.Message;
                return false;
            }

            if (found == null || found.Length == 0)
            {
                // No controllers in this scene (interiors etc.) — not an error; report it so the
                // status line explains why nothing changes here.
                if (revert)
                {
                    this.gameLodHlodOriginals.Clear();
                    status = "ok";
                    return true;
                }
                string sceneName = "?";
                try { sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                status = this.LF("No HLOD controllers in this scene ({0}).", sceneName);
                return false;
            }

            for (int i = 0; i < found.Length; i++)
            {
                UnityEngine.Object o = found[i];
                if (o == null)
                {
                    continue;
                }

                try
                {
                    int id = o.GetInstanceID();
                    float h1 = f1.GetValue(o).Unbox<float>();
                    float h2 = f2.GetValue(o).Unbox<float>();
                    if (revert)
                    {
                        if (this.gameLodHlodOriginals.TryGetValue(id, out Vector2 orig))
                        {
                            f1.SetValue(o, new Il2CppSystem.Single { m_value = orig.x }.BoxIl2CppObject());
                            f2.SetValue(o, new Il2CppSystem.Single { m_value = orig.y }.BoxIl2CppObject());
                            touched++;
                        }
                        continue;
                    }

                    if (!this.gameLodHlodOriginals.TryGetValue(id, out Vector2 baseline))
                    {
                        baseline = new Vector2(h1, h2);
                        this.gameLodHlodOriginals[id] = baseline;
                        this.GameLodLogOnce("hlod: controller " + id + " originals h1=" + h1.ToString("F0")
                            + " h2=" + h2.ToString("F0"));
                    }

                    float t1 = baseline.x * mult;
                    float t2 = baseline.y * mult;
                    if (Mathf.Abs(h1 - t1) > 0.01f)
                    {
                        f1.SetValue(o, new Il2CppSystem.Single { m_value = t1 }.BoxIl2CppObject());
                    }
                    if (Mathf.Abs(h2 - t2) > 0.01f)
                    {
                        f2.SetValue(o, new Il2CppSystem.Single { m_value = t2 }.BoxIl2CppObject());
                    }
                    touched++;
                }
                catch (Exception ex)
                {
                    this.GameLodLogOnce("hlod: controller access failed: " + ex.Message);
                }
            }

            if (revert)
            {
                this.gameLodHlodOriginals.Clear();
            }

            status = "ok";
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Section 8: props XDLod (XDLodManager registry → XDLodGroupComponent.ForceLOD, AuraMono)
        //
        // Only groups WE forced (tracked by netId) get reverted — the game itself ForceLOD(0)s
        // some NPC/GUI groups at spawn and those must keep their state.
        // ----------------------------------------------------------------------------------------
        private unsafe void GameLodTickXdLod()
        {
            if (this.gameLodXdLodManagerClass == IntPtr.Zero)
            {
                this.gameLodXdLodManagerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.BaseSystem.XDLodManager.XDLodManager");
            }

            if (this.gameLodXdLodManagerClass == IntPtr.Zero)
            {
                this.gameLodXdLodStatus = "XDLodManager class unresolved";
                this.GameLodLogOnce("xdlod: " + this.gameLodXdLodStatus);
                return;
            }

            IntPtr managerObj = this.TryGetAuraMonoDataModuleInstance(this.gameLodXdLodManagerClass);
            if (managerObj == IntPtr.Zero)
            {
                this.gameLodXdLodStatus = "XDLodManager.Instance null (world not loaded?)";
                this.GameLodLogOnce("xdlod: " + this.gameLodXdLodStatus);
                return;
            }

            bool reverting = this.gameLodXdLodRevertPending;
            uint managerPin = AuraMonoPinNew(managerObj);
            List<uint> pins = new List<uint>();
            List<IntPtr> groups = null;
            try
            {
                if (!this.TryGetMonoObjectMember(managerObj, "xdlodgroupmap", out IntPtr mapObj) || mapObj == IntPtr.Zero)
                {
                    this.gameLodXdLodStatus = "xdlodgroupmap unavailable";
                    this.GameLodLogOnce("xdlod: " + this.gameLodXdLodStatus);
                    return;
                }

                uint mapPin = AuraMonoPinNew(mapObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(mapObj, "Keys", out IntPtr keysObj) || keysObj == IntPtr.Zero)
                    {
                        this.gameLodXdLodStatus = "xdlodgroupmap.Keys unavailable";
                        this.GameLodLogOnce("xdlod: " + this.gameLodXdLodStatus);
                        return;
                    }

                    uint keysPin = AuraMonoPinNew(keysObj);
                    try
                    {
                        List<IntPtr> items = new List<IntPtr>();
                        if (!this.TryEnumerateAuraMonoCollectionItems(keysObj, items, pins) || items.Count == 0)
                        {
                            if (reverting && this.gameLodXdLodForcedNetIds.Count == 0)
                            {
                                this.gameLodXdLodRevertPending = false;
                                this.gameLodXdLodStatus = this.L("Reverted to game defaults.");
                            }
                            else if (!reverting)
                            {
                                this.gameLodXdLodStatus = this.L("No XDLod groups registered in this scene.");
                            }
                            return;
                        }

                        groups = items;
                    }
                    finally
                    {
                        AuraMonoPinFree(keysPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(mapPin);
                }

                if (!this.gameLodXdLodFirstWalkLogged)
                {
                    this.gameLodXdLodFirstWalkLogged = true;
                    this.GameLodLogOnce("xdlod: first walk ok, groups=" + groups.Count);
                }

                int forced = 0;
                int restored = 0;
                int limit = reverting ? groups.Count : Mathf.Min(groups.Count, GameLodXdLodWalkBudget);
                int offset = (reverting || groups.Count <= GameLodXdLodWalkBudget)
                    ? 0
                    : this.gameLodXdLodWalkOffset % groups.Count;
                if (!reverting && groups.Count > GameLodXdLodWalkBudget)
                {
                    this.gameLodXdLodWalkOffset = (offset + GameLodXdLodWalkBudget) % groups.Count;
                }

                for (int step = 0; step < limit; step++)
                {
                    IntPtr group = groups[(offset + step) % groups.Count];
                    if (group == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(group, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U)
                    {
                        continue; // untrackable — leave it alone so revert stays exact
                    }

                    if (reverting)
                    {
                        if (this.gameLodXdLodForcedNetIds.Contains(netId)
                            && this.TryGameLodXdLodForce(group, -1))
                        {
                            restored++;
                        }
                        continue;
                    }

                    if (this.gameLodXdLodForcedNetIds.Contains(netId))
                    {
                        forced++;
                        continue; // ours already
                    }

                    // The game forces some groups itself (NPC spawn ForceLOD(0)) — skip those so
                    // our revert never clears state we do not own.
                    if (this.TryGameLodXdLodIsForced(group, out bool alreadyForced) && alreadyForced)
                    {
                        continue;
                    }

                    if (this.TryGameLodXdLodForce(group, 0))
                    {
                        this.gameLodXdLodForcedNetIds.Add(netId);
                        forced++;
                    }
                }

                if (reverting)
                {
                    this.gameLodXdLodRevertPending = false;
                    this.gameLodXdLodForcedNetIds.Clear();
                    this.gameLodXdLodForcedCount = 0;
                    this.gameLodXdLodStatus = this.LF("Reverted {0} prop groups to game defaults.", restored);
                    this.GameLodLogOnce("xdlod revert: restored " + restored + " groups");
                }
                else
                {
                    this.gameLodXdLodForcedCount = forced;
                    this.gameLodXdLodStatus = this.LF("Max detail forced on {0} prop groups.", forced);
                    this.GameLodLogOnce("xdlod apply: forced " + forced + " groups");
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(managerPin);
            }
        }

        private unsafe bool TryGameLodXdLodForce(IntPtr groupObj, int lodIndex)
        {
            if (groupObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(groupObj);
            IntPtr method = klass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(klass, "ForceLOD", 1) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                this.GameLodLogOnce("xdlod: ForceLOD missing on " + this.GetAuraMonoClassDisplayName(klass));
                return false;
            }

            int value = lodIndex;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, groupObj, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private bool TryGameLodXdLodIsForced(IntPtr groupObj, out bool isForced)
        {
            isForced = false;
            if (groupObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(groupObj);
            IntPtr getter = klass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(klass, "get_IsLODForced", 0) : IntPtr.Zero;
            if (getter == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, groupObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out isForced);
        }

        // ----------------------------------------------------------------------------------------
        // Diagnostics: dump every LOD-owning object near the player so a still-swapping mesh can
        // be attributed to its system. Read-only, user-triggered from the Game LOD page.
        //  - has a GameObject WITH LODGroup           → Unity lodBias (Settings → Performance)
        //  - has a GameObject, NO LODGroup, appears/disappears whole → streaming (LoaderManager /
        //    native scene streamer)
        //  - has NO GameObject at all (pure GPU instance)            → InstanceBlock/BRG
        //    (PC_LODBIAS / ForceLOD0)
        // ----------------------------------------------------------------------------------------
        internal void DumpGameLodNearbyLodObjects()
        {
            try
            {
                Vector3 center;
                if (!this.TryGetLocalPlayerPosition(out center) || center == Vector3.zero)
                {
                    Camera main = Camera.main;
                    if (main == null)
                    {
                        ModLogger.Msg("[GameLodDump] no player position and no camera — cannot dump");
                        return;
                    }
                    center = main.transform.position;
                }

                string sceneName = "?";
                try { sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                int qualityLoaderChildren = -1;
                try
                {
                    GameObject ql = GameObject.Find("QualityLoader");
                    qualityLoaderChildren = ql != null ? ql.transform.childCount : -1;
                }
                catch { }
                ModLogger.Msg("[GameLodDump] scene=" + sceneName
                    + " lodBias=" + QualitySettings.lodBias.ToString("0.##")
                    + " maxLODLevel=" + QualitySettings.maximumLODLevel
                    + " PC_LODBIAS=" + PlayerPrefs.GetInt("PC_LODBIAS", 10)
                    + " (mod baseline=" + this.gameLodVegetationBaselinePref
                    + ", target=" + this.GameLodEffectiveVegetationPref() + ")"
                    + " QualityLoader children=" + qualityLoaderChildren
                    + " center=" + center.ToString("F0"));

                int hlodActive = -1;
                int hlodAll = -1;
                try
                {
                    if (this.gameLodHlodIl2CppType != null)
                    {
                        Il2CppReferenceArray<UnityEngine.Object> a = UnityEngine.Object.FindObjectsOfType(this.gameLodHlodIl2CppType);
                        hlodActive = a != null ? a.Length : 0;
                        Il2CppReferenceArray<UnityEngine.Object> b = Resources.FindObjectsOfTypeAll(this.gameLodHlodIl2CppType);
                        hlodAll = b != null ? b.Length : 0;
                    }
                }
                catch { }
                ModLogger.Msg("[GameLodDump] HLODController: active=" + hlodActive + " all=" + hlodAll
                    + "; XDLod groups forced=" + this.gameLodXdLodForcedCount
                    + "; NineCell boosted=" + this.gameLodNineCellBoostedCount);

                // Base-scene chunk streamer layers — THE owner of rock/island low-poly→high-poly
                // swaps. Dump every config's live values so over/under-multiplication is visible.
                try
                {
                    Il2CppSystem.Type slType = Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot, SceneLoader")
                        ?? Il2CppSystem.Type.GetType("SceneLoader.SceneLoaderRoot");
                    if (slType == null)
                    {
                        ModLogger.Msg("[GameLodDump] SceneLoaderRoot: type unresolved");
                    }
                    else
                    {
                        Il2CppReferenceArray<UnityEngine.Object> roots = UnityEngine.Object.FindObjectsOfType(slType);
                        if (roots == null || roots.Length == 0)
                        {
                            roots = Resources.FindObjectsOfTypeAll(slType);
                        }
                        int rootCount = roots != null ? roots.Length : 0;
                        ModLogger.Msg("[GameLodDump] SceneLoaderRoot instances=" + rootCount);
                        for (int r = 0; roots != null && r < roots.Length; r++)
                        {
                            UnityEngine.Object root = roots[r];
                            if (root == null) { continue; }
                            Il2CppSystem.Reflection.FieldInfo cf = root.GetIl2CppType().GetField("configs");
                            Il2CppSystem.Object boxed = cf != null ? cf.GetValue(root) : null;
                            if (boxed == null) { continue; }
                            Il2CppReferenceArray<Il2CppSystem.Object> cfgs = new Il2CppReferenceArray<Il2CppSystem.Object>(boxed.Pointer);
                            for (int c = 0; c < cfgs.Length; c++)
                            {
                                Il2CppSystem.Object cfg = cfgs[c];
                                if (cfg == null) { continue; }
                                Il2CppSystem.Type ct = cfg.GetIl2CppType();
                                float load = ct.GetField("loadDistance").GetValue(cfg).Unbox<float>();
                                float unload = ct.GetField("unloadDistance").GetValue(cfg).Unbox<float>();
                                float cellSize = ct.GetField("cellSize").GetValue(cfg).Unbox<float>();
                                float pcMag = 0f;
                                try { pcMag = ct.GetField("pcLoadMag").GetValue(cfg).Unbox<float>(); } catch { }
                                string hlodsText = "none";
                                try
                                {
                                    Il2CppSystem.Object hb = ct.GetField("hlodDistances").GetValue(cfg);
                                    if (hb != null)
                                    {
                                        Il2CppStructArray<float> ha = new Il2CppStructArray<float>(hb.Pointer);
                                        string[] parts = new string[ha.Length];
                                        for (int i = 0; i < ha.Length; i++) { parts[i] = ha[i].ToString("F0"); }
                                        hlodsText = string.Join("/", parts);
                                    }
                                }
                                catch { }
                                ModLogger.Msg("[GameLodDump]   layer root=" + root.GetInstanceID() + " cfg=" + c
                                    + " cellSize=" + cellSize.ToString("F0")
                                    + " load=" + load.ToString("F0") + " unload=" + unload.ToString("F0")
                                    + " pcLoadMag=" + pcMag.ToString("F2") + " hlods=" + hlodsText);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[GameLodDump] SceneLoaderRoot dump failed: " + ex.Message);
                }

                LODGroup[] groups = UnityEngine.Object.FindObjectsOfType<LODGroup>();
                if (groups == null)
                {
                    ModLogger.Msg("[GameLodDump] LODGroup scan returned null");
                    return;
                }

                List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
                int total = 0;
                for (int i = 0; i < groups.Length; i++)
                {
                    LODGroup g = groups[i];
                    if (g == null)
                    {
                        continue;
                    }

                    total++;
                    try
                    {
                        Vector3 pos = g.transform.position;
                        float dist = Vector3.Distance(center, pos);
                        if (dist > 250f)
                        {
                            continue;
                        }

                        string rootName = "?";
                        try { rootName = g.transform.root != null ? g.transform.root.name : "?"; } catch { }
                        rows.Add(new KeyValuePair<float, string>(dist,
                            "d=" + dist.ToString("F0") + "m lods=" + g.lodCount
                            + " size=" + g.size.ToString("F1")
                            + " name=" + g.gameObject.name + " root=" + rootName));
                    }
                    catch { }
                }

                rows.Sort((a, b) => a.Key.CompareTo(b.Key));
                // Distant objects are the interesting ones here (the reported swap is a far rock /
                // island), so list the FARTHEST LODGroups — the nearest ones are always the player
                // and neighbouring furniture and told us nothing on the first dump.
                int show = Mathf.Min(rows.Count, 25);
                ModLogger.Msg("[GameLodDump] Unity LODGroups: total=" + total + ", within 250m=" + rows.Count
                    + " (showing FARTHEST " + show + "). A swapping object NOT in this list has no LODGroup"
                    + " — it is BRG/InstanceBlock (PC_LODBIAS) or streamed geometry.");
                for (int i = rows.Count - 1; i >= 0 && i >= rows.Count - show; i--)
                {
                    ModLogger.Msg("[GameLodDump]   " + rows[i].Value);
                }

                this.DumpGameLodCameraRay();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GameLodDump] failed: " + ex.Message);
            }
        }

        // "What am I looking at": raycast down the camera's forward axis and identify the hit
        // object's LOD ownership. This is the decisive attribution step — a rock that reports NO
        // LODGroup on its hierarchy is BRG/InstanceBlock geometry (PC_LODBIAS), one WITH a LODGroup
        // obeys QualitySettings.lodBias, and "no hit" means it has no collider (baked scenery).
        private void DumpGameLodCameraRay()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    ModLogger.Msg("[GameLodDump] camera ray: no Camera.main");
                    return;
                }

                // Renderer cone first: distant scenery (islands, cliffs) is baked geometry that
                // often has NO collider, or whose box collision is streamed only up close — the
                // first live dump proved it (the only ray hit was the audio listener at 1 m).
                // Angle-to-view-centre over every Renderer works regardless of physics.
                this.DumpGameLodRendererCone(cam);

                Ray ray = new Ray(cam.transform.position, cam.transform.forward);
                RaycastHit[] hits = Physics.RaycastAll(ray, 600f, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0)
                {
                    ModLogger.Msg("[GameLodDump] camera ray: no collider hit within 600 m"
                        + " (the object you are facing has no collider — baked scenery / instanced geometry)");
                    return;
                }

                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                int show = Mathf.Min(hits.Length, 6);
                ModLogger.Msg("[GameLodDump] camera ray: " + hits.Length + " hit(s), nearest " + show + ":");
                for (int i = 0; i < show; i++)
                {
                    RaycastHit hit = hits[i];
                    Transform t = hit.transform;
                    if (t == null || hit.distance < 2f)
                    {
                        continue; // camera-attached rigs (audio listener, capsule) are noise
                    }

                    string lodInfo = "no LODGroup on hierarchy";
                    try
                    {
                        LODGroup own = t.GetComponentInParent<LODGroup>();
                        if (own == null)
                        {
                            own = t.GetComponentInChildren<LODGroup>();
                        }
                        if (own != null)
                        {
                            lodInfo = "LODGroup lods=" + own.lodCount + " size=" + own.size.ToString("F1")
                                + " on '" + own.gameObject.name + "'";
                        }
                    }
                    catch { }

                    string meshInfo = "";
                    try
                    {
                        MeshFilter mf = t.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            meshInfo = " mesh=" + mf.sharedMesh.name + " tris~" + mf.sharedMesh.triangles.Length / 3;
                        }
                    }
                    catch { }

                    string chain = t.name;
                    try
                    {
                        Transform p = t.parent;
                        int depth = 0;
                        while (p != null && depth < 4)
                        {
                            chain = p.name + "/" + chain;
                            p = p.parent;
                            depth++;
                        }
                    }
                    catch { }

                    ModLogger.Msg("[GameLodDump]   hit d=" + hit.distance.ToString("F0") + "m " + chain
                        + " layer=" + t.gameObject.layer + " | " + lodInfo + meshInfo);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GameLodDump] camera ray failed: " + ex.Message);
            }
        }

        // Every Renderer whose bounds centre sits within ~12° of the camera's forward axis and
        // further than 40 m — i.e. the distant thing the user is looking at. Reports LODGroup
        // ownership per hit, which is what decides the lever:
        //   LODGroup present  → QualitySettings.lodBias (Settings → Performance)
        //   no LODGroup       → BRG / InstanceBlock (PC_LODBIAS) or streamed scene geometry
        private void DumpGameLodRendererCone(Camera cam)
        {
            try
            {
                Vector3 camPos = cam.transform.position;
                Vector3 camFwd = cam.transform.forward;
                Renderer[] rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
                if (rends == null)
                {
                    ModLogger.Msg("[GameLodDump] renderer cone: scan returned null");
                    return;
                }

                List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
                for (int i = 0; i < rends.Length; i++)
                {
                    Renderer r = rends[i];
                    if (r == null || !r.enabled)
                    {
                        continue;
                    }

                    try
                    {
                        Vector3 dir = r.bounds.center - camPos;
                        float dist = dir.magnitude;
                        if (dist < 40f || dist > 1200f)
                        {
                            continue;
                        }

                        float angle = Vector3.Angle(camFwd, dir);
                        if (angle > 12f)
                        {
                            continue;
                        }

                        string lodInfo = "NO LODGroup";
                        try
                        {
                            LODGroup lg = r.GetComponentInParent<LODGroup>();
                            if (lg != null)
                            {
                                lodInfo = "LODGroup(" + lg.lodCount + ", size " + lg.size.ToString("F1") + ")";
                            }
                        }
                        catch { }

                        string rootName = "?";
                        try { rootName = r.transform.root != null ? r.transform.root.name : "?"; } catch { }

                        rows.Add(new KeyValuePair<float, string>(angle,
                            "a=" + angle.ToString("F0") + "° d=" + dist.ToString("F0") + "m "
                            + r.gameObject.name + " root=" + rootName
                            + " ext=" + r.bounds.extents.ToString("F0") + " " + lodInfo
                            + " type=" + r.GetType().Name));
                    }
                    catch { }
                }

                rows.Sort((a, b) => a.Key.CompareTo(b.Key));
                int show = Mathf.Min(rows.Count, 20);
                ModLogger.Msg("[GameLodDump] renderer cone (>40 m, within 12° of view centre): "
                    + rows.Count + " match(es) of " + rends.Length + " renderers, showing " + show
                    + " (nearest to centre first). NOTE: BRG/InstanceBlock geometry (trees, rocks,"
                    + " islands, grass) is drawn without Renderer components and CANNOT appear here"
                    + " — an empty/short list while facing big scenery means exactly that, and"
                    + " PC_LODBIAS is its lever.");
                for (int i = 0; i < show; i++)
                {
                    ModLogger.Msg("[GameLodDump]   " + rows[i].Value);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GameLodDump] renderer cone failed: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // UI-facing toggle entry points (queue reverts; applies run in the guarded tick)
        // ----------------------------------------------------------------------------------------
        internal void SetGameLodFurnitureEnabled(bool value)
        {
            if (this.gameLodFurnitureEnabled == value)
            {
                return;
            }
            this.gameLodFurnitureEnabled = value;
            this.gameLodFurnitureRevertPending = !value;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodForceLod0Enabled(bool value)
        {
            if (this.gameLodForceLod0Enabled == value)
            {
                return;
            }
            this.gameLodForceLod0Enabled = value;
            this.gameLodForceLod0RevertPending = !value;
            if (!value)
            {
                this.gameLodBrgStatus = this.L("Reverted to game defaults.");
            }
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodBrgBiasEnabled(bool value)
        {
            if (this.gameLodBrgBiasEnabled == value)
            {
                return;
            }
            this.gameLodBrgBiasEnabled = value;
            this.gameLodBrgBiasRevertPending = !value;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodVegetationEnabled(bool value)
        {
            if (this.gameLodVegetationEnabled == value)
            {
                return;
            }
            this.gameLodVegetationEnabled = value;
            this.GameLodWriteVegetationPref();
            this.gameLodVegetationRebakePending = true;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodSignificanceOffEnabled(bool value)
        {
            if (this.gameLodSignificanceOffEnabled == value)
            {
                return;
            }
            this.gameLodSignificanceOffEnabled = value;
            this.gameLodSignificanceRevertPending = !value;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodNineCellEnabled(bool value)
        {
            if (this.gameLodNineCellEnabled == value)
            {
                return;
            }
            this.gameLodNineCellEnabled = value;
            this.gameLodNineCellRevertPending = !value;
            this.nextGameLodNineCellWalkAt = 0f;
        }

        internal void SetGameLodShadowEnabled(bool value)
        {
            if (this.gameLodShadowEnabled == value)
            {
                return;
            }
            this.gameLodShadowEnabled = value;
            this.gameLodShadowRevertPending = !value;
            this.nextGameLodApplyAt = 0f;
        }

        internal void RequestGameLodVegetationRebake()
        {
            this.GameLodWriteVegetationPref();
            this.gameLodVegetationRebakePending = true;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodHlodEnabled(bool value)
        {
            if (this.gameLodHlodEnabled == value)
            {
                return;
            }
            this.gameLodHlodEnabled = value;
            this.gameLodHlodRevertPending = !value;
            this.nextGameLodApplyAt = 0f;
        }

        internal void SetGameLodXdLodEnabled(bool value)
        {
            if (this.gameLodXdLodEnabled == value)
            {
                return;
            }
            this.gameLodXdLodEnabled = value;
            this.gameLodXdLodRevertPending = !value;
            this.nextGameLodXdLodWalkAt = 0f;
        }

        // Config load hook: never leave a stale PlayerPrefs override behind when the toggle was
        // saved OFF, and queue applies for saved-ON toggles (the tick re-applies idempotently).
        private void SyncGameLodAfterConfigLoad()
        {
            this.gameLodFurnitureMaxObjects = Mathf.Clamp(this.gameLodFurnitureMaxObjects <= 0 ? 1500 : this.gameLodFurnitureMaxObjects, 60, 5000);
            this.gameLodFurnitureDistance = Mathf.Clamp(this.gameLodFurnitureDistance <= 0 ? 9999 : this.gameLodFurnitureDistance, 100, 9999);
            this.gameLodFurnitureMeshDistance = Mathf.Clamp(this.gameLodFurnitureMeshDistance <= 0 ? 1000 : this.gameLodFurnitureMeshDistance, 100, 2000);
            this.gameLodBrgBias = Mathf.Clamp(this.gameLodBrgBias <= 0f ? 2f : this.gameLodBrgBias, 1f, 4f);
            // Legacy configs stored a raw PC_LODBIAS pref (1..10); migrate it to the multiplier.
            if (this.gameLodVegetationMult <= 0f)
            {
                this.gameLodVegetationMult = this.gameLodVegetationPref > 0
                    ? Mathf.Clamp(10f / this.gameLodVegetationPref, 1f, 10f)
                    : 4f;
            }
            this.gameLodVegetationMult = Mathf.Clamp(this.gameLodVegetationMult, 1f, 10f);
            // Legacy multiplier configs → absolute target (the multiplier field stays only for
            // one-way migration; nothing reads it afterwards).
            if (this.gameLodVegetationTargetPref <= 0)
            {
                this.gameLodVegetationTargetPref = 300;
            }
            this.gameLodVegetationTargetPref = Mathf.Clamp(this.gameLodVegetationTargetPref, 10, 1000);
            // Baseline BEFORE the first write of this session (see GameLodCaptureVegetationBaseline).
            this.GameLodCaptureVegetationBaseline();
            this.gameLodNineCellMult = Mathf.Clamp(this.gameLodNineCellMult <= 0f ? 2f : this.gameLodNineCellMult, 1f, 5f);
            this.gameLodShadowDistance = Mathf.Clamp(this.gameLodShadowDistance <= 0f ? 300f : this.gameLodShadowDistance, 50f, 800f);
            this.gameLodHlodMult = Mathf.Clamp(this.gameLodHlodMult <= 0f ? 2f : this.gameLodHlodMult, 1f, 4f);

            // PC_LODBIAS persists in the registry across sessions. Put the GAME's own baseline
            // back for the upcoming world load — regardless of the toggle — and let the
            // post-settle reassert raise it (plus exactly one rebake). Leaving our value here
            // made every world load build its instance blocks at max detail, which is precisely
            // what kept the loading screen slow. No rebake is queued here on purpose.
            try
            {
                int atLoad = (this.gameLodVegetationEnabled && this.gameLodVegetationApplyDuringLoad)
                    ? this.GameLodEffectiveVegetationPref()
                    : this.GameLodVegetationOriginalPref();
                PlayerPrefs.SetInt("PC_LODBIAS", atLoad);
                PlayerPrefs.Save();
                this.gameLodVegetationLastWrittenPref = atLoad;
            }
            catch { }

            this.nextGameLodApplyAt = 0f;
            this.nextGameLodNineCellWalkAt = 0f;
        }
    }
}
